// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using Spreads.Buffers;
using Spreads.IPC.Protocol;
using System;
using System.Runtime.CompilerServices;

namespace Spreads.IPC.Logbuffer
{
    /// <summary>
    /// Represents a claimed range in a buffer to be used for recording a message without copy semantics for later commit.
    /// <para>
    /// The claimed space is in <seealso cref="Buffer"/> between <seealso cref="Offset"/> and <seealso cref="Offset"/> + <seealso cref="Length"/>.
    /// When the buffer is filled with message data, use <seealso cref="Commit()"/> to make it available to subscribers.
    /// </para>
    /// <para>
    /// If the claimed space is no longer required it can be aborted by calling <seealso cref="Abort()"/>.
    /// </para>
    /// </summary>
    public struct BufferClaim
    {
        private readonly DirectBuffer _buffer;

        /// <summary>
        /// Wrap a region of an underlying log buffer so can can represent a claimed space for use by a publisher.
        /// </summary>
        /// <param name="buffer"> to be wrapped. </param>
        /// <param name="offset"> at which the claimed region begins including space for the header. </param>
        /// <param name="length"> length of the underlying claimed region including space for the header. </param>
        public BufferClaim(DirectBuffer buffer, int offset, int length)
        {
            _buffer = DirectBuffer.CreateWithoutChecks(length, buffer.Data + offset);
        }

        /// <summary>
        /// The referenced buffer to be used.
        /// </summary>
        /// <returns> the referenced buffer to be used.. </returns>
        public DirectBuffer Buffer
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return _buffer; }
        }

        /// <summary>
        /// The offset in the buffer at which the claimed range begins.
        /// </summary>
        /// <returns> offset in the buffer at which the range begins. </returns>
        public int Offset
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return DataHeaderFlyweight.HEADER_LENGTH; }
        }

        internal IntPtr Data
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return _buffer.Data + DataHeaderFlyweight.HEADER_LENGTH; }
        }

        public int Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { checked { return (int)_buffer.Length - DataHeaderFlyweight.HEADER_LENGTH; } }
        }

        public long ReservedValue
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return _buffer.ReadInt64(DataHeaderFlyweight.RESERVED_VALUE_OFFSET); }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set { _buffer.WriteInt64(DataHeaderFlyweight.RESERVED_VALUE_OFFSET, value); }
        }

        /// <summary>
        /// Commit the message to the log buffer so that is it available to subscribers.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Commit()
        {
            checked
            {
                var frameLength = (int)_buffer.Length;
                _buffer.VolatileWriteInt32(HeaderFlyweight.FRAME_LENGTH_FIELD_OFFSET, frameLength);
            }
        }

        /// <summary>
        /// Abort a claim of the message space to the log buffer so that the log can progress by ignoring this claim.
        /// </summary>
        public void Abort()
        {
            checked
            {
                var frameLength = (int)_buffer.Length;
                _buffer.WriteInt16(HeaderFlyweight.TYPE_FIELD_OFFSET, (short)HeaderFlyweight.HDR_TYPE_PAD);
                _buffer.VolatileWriteInt32(HeaderFlyweight.FRAME_LENGTH_FIELD_OFFSET, frameLength);
            }
        }
    }
}