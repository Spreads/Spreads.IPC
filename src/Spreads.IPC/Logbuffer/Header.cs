// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.Runtime.CompilerServices;
using Spreads.Buffers;
using Spreads.IPC.Logbuffer.Protocol;
using Spreads.Utils;

namespace Spreads.IPC.Logbuffer
{
    internal struct Header
    {
        private readonly int _positionBitsToShift;
        private int _initialTermId;
        private int _offset;
        private DirectBuffer _buffer;

        /**
         * Construct a header that references a buffer for the log.
         *
         * @param initialTermId       this stream started at.
         * @param positionBitsToShift for calculating positions.
         */

        public Header(int initialTermId, int positionBitsToShift)
        {
            _buffer = default(DirectBuffer);
            _offset = 0;
            _initialTermId = initialTermId;
            _positionBitsToShift = positionBitsToShift;
        }

        /**
         * Get the current position to which the image has advanced on reading this message.
         *
         * @return the current position to which the image has advanced on reading this message.
         */

        public long Position
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                var resultingOffset = BitUtil.Align(TermOffset + FrameLength, FrameDescriptor.FRAME_ALIGNMENT);
                return LogBufferDescriptor.ComputePosition(TermId, resultingOffset, _positionBitsToShift, _initialTermId);
            }
        }

        public int InitialTermId
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return _initialTermId; }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set { _initialTermId = value; }
        }

        public int Offset
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return _offset; }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set { _offset = value; }
        }

        public DirectBuffer Buffer
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return _buffer; }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set { _buffer = value; }
        }

        /**
         * The total length of the frame including the header.
         *
         * @return the total length of the frame including the header.
         */
        public int FrameLength
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return _buffer.ReadInt32(_offset); }
        }

        /**
         * The session ID to which the frame belongs.
         *
         * @return the session ID to which the frame belongs.
         */
        public int SessionId
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return _buffer.ReadInt32(_offset + DataHeaderFlyweight.SESSION_ID_FIELD_OFFSET); }
        }

        /**
         * The stream ID to which the frame belongs.
         *
         * @return the stream ID to which the frame belongs.
         */
        public int StreamId
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return _buffer.ReadInt32(_offset + DataHeaderFlyweight.STREAM_ID_FIELD_OFFSET); }
        }

        /**
         * The term ID to which the frame belongs.
         *
         * @return the term ID to which the frame belongs.
         */
        public int TermId
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return _buffer.ReadInt32(_offset + DataHeaderFlyweight.TERM_ID_FIELD_OFFSET); }
        }

        /**
         * The offset in the term at which the frame begins. This will be the same as {@link #offset()}
         *
         * @return the offset in the term at which the frame begins.
         */
        public int TermOffset
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return _offset; }
        }

        /**
         * The type of the the frame which should always be {@link DataHeaderFlyweight#HDR_TYPE_DATA}
         *
         * @return type of the the frame which should always be {@link DataHeaderFlyweight#HDR_TYPE_DATA}
         */
        public int Type
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return _buffer.ReadInt32(_offset + HeaderFlyweight.TYPE_FIELD_OFFSET) & 0xFFFF; }
        }

        /**
         * The flags for this frame. Valid flags are {@link DataHeaderFlyweight#BEGIN_FLAG}
         * and {@link DataHeaderFlyweight#END_FLAG}. A convenience flag {@link DataHeaderFlyweight#BEGIN_AND_END_FLAGS}
         * can be used for both flags.
         *
         * @return the flags for this frame.
         */
        public byte Flags
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return _buffer.ReadByte(_offset + HeaderFlyweight.FLAGS_FIELD_OFFSET); }
        }

        /**
         * Get the value stored in the reserve space at the end of a data frame header.
         * <p>
         * Note: The value is in {@link ByteOrder#LITTLE_ENDIAN} format.
         *
         * @return the value stored in the reserve space at the end of a data frame header.
         * @see DataHeaderFlyweight
         */
        public long RreservedValue
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return _buffer.ReadInt64(_offset + DataHeaderFlyweight.RESERVED_VALUE_OFFSET); }
        }
    }
}