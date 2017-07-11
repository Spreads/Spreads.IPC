// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using Spreads.Buffers;
using Spreads.IPC.Protocol;
using Spreads.Utils;
using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Spreads.IPC.Logbuffer
{
    /// <summary>
    /// Term buffer appender which supports many producers concurrently writing an append-only log.
    ///
    /// <b>Note:</b> This class is threadsafe.
    ///
    /// Messages are appended to a term using a framing protocol as described in <seealso cref="FrameDescriptor"/>.
    ///
    /// A default message headerWriter is applied to each message with the fields filled in for fragment flags, type, term number,
    /// as appropriate.
    ///
    /// A message of type <seealso cref="FrameDescriptor#PADDING_FRAME_TYPE"/> is appended at the end of the buffer if claimed
    /// space is not sufficiently large to accommodate the message about to be written.
    /// </summary>
    internal struct TermAppender
    {
        private readonly DirectBuffer _termBuffer;
        private readonly DirectBuffer _metaDataBuffer;

        /**
         * The append operation tripped the end of the buffer and needs to rotate.
         */
        public const int TRIPPED = -1;

        /**
         * The append operation went past the end of the buffer and failed.
         */
        public const int FAILED = -2;

        /**
         * Construct a view over a term buffer and state buffer for appending frames.
         *
         * @param termBuffer     for where messages are stored.
         * @param metaDataBuffer for where the state of writers is stored manage concurrency.
         */

        public TermAppender(DirectBuffer termBuffer, DirectBuffer metaDataBuffer)
        {
            _termBuffer = termBuffer;
            _metaDataBuffer = metaDataBuffer;
        }

        public TermAppender(LogBufferPartition partition)
        {
            _termBuffer = partition.TermBuffer;
            _metaDataBuffer = partition.MetaDataBuffer;
        }

        public DirectBuffer TermBuffer
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return _termBuffer; }
        }

        public DirectBuffer MetaDataBuffer
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return _metaDataBuffer; }
        }

        /// <summary>
        /// Get the raw value current tail value in a volatile memory ordering fashion.
        /// </summary>
        /// <returns> the current tail value. </returns>
        public long RawTailVolatile
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return _metaDataBuffer.VolatileReadInt64(LogBufferDescriptor.TERM_TAIL_COUNTER_OFFSET); }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set { _metaDataBuffer.VolatileWriteInt64(LogBufferDescriptor.TERM_TAIL_COUNTER_OFFSET, value); }
        }


        /// <summary>
        /// Set the value for the tail counter.
        /// </summary>
        /// <param name="termId">termId for the tail counter</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void TailTermId(int termId)
        {
            _metaDataBuffer.WriteInt64(LogBufferDescriptor.TERM_TAIL_COUNTER_OFFSET, ((long)termId) << 32);
        }

        /**
         * Set the status of the log buffer with StoreStore memory ordering semantics.
         *
         * @param status to be set for the log buffer.
         */

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void StatusOrdered(int status)
        {
            _metaDataBuffer.VolatileWriteInt32(LogBufferDescriptor.TERM_STATUS_OFFSET, status);
        }

        /// <summary>
        /// Claim length of a the term buffer for writing in the message with zero copy semantics.
        /// </summary>
        /// <param name="header">      for writing the default header. </param>
        /// <param name="length">      of the message to be written. </param>
        /// <param name="bufferClaim"> to be updated with the claimed region. </param>
        /// <returns> the resulting offset of the term after the append on success otherwise <seealso cref="#TRIPPED"/> or <seealso cref="#FAILED"/>
        /// packed with the termId if a padding record was inserted at the end. </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe long Claim(HeaderWriter header, int length, out BufferClaim bufferClaim)
        {
            int frameLength = length + DataHeaderFlyweight.HEADER_LENGTH;
            int alignedLength = BitUtil.Align(frameLength, FrameDescriptor.FRAME_ALIGNMENT);

            var termBuffer = _termBuffer;
            int termLength = checked((int)termBuffer.Length);
            long resultingOffset;
            var spinCounter = 0;
            var spinWait = new SpinWait();
            var rawTail = RawTailVolatile;

            while (true)
            {
                var termOffset = rawTail & 0xFFFFFFFFL;
                resultingOffset = termOffset + alignedLength;

                if (resultingOffset > termLength)
                {
                    RawTailVolatile = rawTail + alignedLength;
                    resultingOffset = HandleEndOfLogCondition(termBuffer, termOffset, header, termLength, TermId(rawTail));
                    bufferClaim = default(BufferClaim);
                    break;
                }

                // true if we are the first to claim space at current offset
                if (0 ==
                    Interlocked.CompareExchange(
                        ref *(int*)(new IntPtr(_termBuffer.Data.ToInt64() + termOffset)), -frameLength, 0))
                {
                    // if a writer dies here, another writer will unblock below
                    // NB no need for volatile write because it prevents only reordering
                    // from abve it to below, but above it we have a full barrier due to Interlocked
                    _metaDataBuffer.WriteInt64(LogBufferDescriptor.TERM_TAIL_COUNTER_OFFSET,
                        rawTail + alignedLength);
                    int offset = (int)Volatile.Read(ref termOffset);
                    header.Write(termBuffer, offset, frameLength, TermId(rawTail));
                    bufferClaim = new BufferClaim(termBuffer, offset, frameLength);
                    break;
                }

                spinWait.SpinOnce();

                // spin, will re-read (volatile) current tail and try again
                // single writer will always succeed on first try
                var previousRawTail = rawTail;
                rawTail = RawTailVolatile;

                if (previousRawTail == rawTail)
                {
                    // incrementing tail happens right next to interlocked -length write
                    // we should spin in case another writer has written -length but not yet incremented tail
                    // but such situation should be very short-lived
                    spinCounter++;
                    if (spinCounter > 100)
                    {
                        // no-one is progressing, need to unblock
                        _termBuffer.VolatileWriteInt32(termOffset, 0);
                    }
                }
            }

            return resultingOffset;
        }


        /**
         * Pack the values for termOffset and termId into a long for returning on the stack.
         *
         * @param termId     value to be packed.
         * @param termOffset value to be packed.
         * @return a long with both ints packed into it.
         */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long Pack(int termId, int termOffset)
        {
            return ((long)termId << 32) | (termOffset & 0xFFFFFFFFL);
        }

        /**
         * The termOffset as a result of the append
         *
         * @param result into which the termOffset value has been packed.
         * @return the termOffset after the append
         */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int TermOffset(long result)
        {
            return unchecked((int)result);
        }

        /**
         * The termId in which the append operation took place.
         *
         * @param result into which the termId value has been packed.
         * @return the termId in which the append operation took place.
         */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int TermId(long result)
        {
            return (int)((long)((ulong)result >> 32));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private long HandleEndOfLogCondition(
            DirectBuffer termBuffer,
            long termOffset,
            HeaderWriter header,
            int termLength,
            int termId)
        {
            int resultingOffset = FAILED;

            if (termOffset <= termLength)
            {
                resultingOffset = TRIPPED;

                if (termOffset < termLength)
                {
                    int offset = (int)termOffset;
                    int paddingLength = termLength - offset;
                    header.Write(termBuffer, offset, paddingLength, termId);
                    FrameDescriptor.FrameType(termBuffer, offset, FrameDescriptor.PADDING_FRAME_TYPE);
                    FrameDescriptor.FrameLengthOrdered(termBuffer, offset, paddingLength);
                }
            }

            return Pack(termId, resultingOffset);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private long GetAndAddRawTail(int alignedLength)
        {
            return _metaDataBuffer.InterlockedAddInt64(LogBufferDescriptor.TERM_TAIL_COUNTER_OFFSET, alignedLength);
        }
    }
}
