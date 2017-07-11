// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.Runtime.CompilerServices;
using Spreads.Buffers;
using Spreads.IPC.Protocol;

namespace Spreads.IPC.Logbuffer
{
    /// <summary>
    /// Utility for applying a header to a message in a term buffer.
    ///
    /// This class is designed to be thread safe to be used across multiple producers and makes the header
    /// visible in the correct order for consumers.
    /// </summary>
    public struct HeaderWriter
    {
        private readonly DataHeader _defaultHeader;

        public HeaderWriter(DataHeader defaultHeader)
        {
            _defaultHeader = defaultHeader;
        }

        /// <summary>
        /// Write a header to the term buffer in {@link ByteOrder#LITTLE_ENDIAN} format using the minimum instructions.
        /// </summary>
        /// <param name="termBuffer">termBuffer to be written to.</param>
        /// <param name="offset">offset at which the header should be written.</param>
        /// <param name="length">length of the fragment including the header.</param>
        /// <param name="termId">termId of the current term buffer.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void Write(DirectBuffer termBuffer, int offset, int length, int termId)
        {
            // NB in Spreads implementation negative length is already written
            // using Interlocked.Exchange, so no need for additional barriers

            var ptr = (DataHeader*)(termBuffer.Data + offset + HeaderFlyweight.FRAME_LENGTH_FIELD_OFFSET);

            ptr->Header.VersionFlagsType = _defaultHeader.Header.VersionFlagsType;
            ptr->TermOffset = offset;
            ptr->TermID = termId;
            ptr->SessionID = _defaultHeader.SessionID;
            ptr->StreamID = _defaultHeader.StreamID;

        }
    }
}