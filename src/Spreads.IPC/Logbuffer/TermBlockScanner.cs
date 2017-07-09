// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using Spreads.Buffers;
using Spreads.Utils;

namespace Spreads.IPC.Logbuffer
{
    /// <summary>
    /// Scan a term buffer for a block of messages including padding. The block must include complete messages.
    /// </summary>
    public static class TermBlockScanner
    {
        /// <summary>
        /// Scan a term buffer for a block of messages from and offset up to a limit.
        /// </summary>
        /// <param name="termBuffer"> to scan for messages. </param>
        /// <param name="offset">     at which the scan should begin. </param>
        /// <param name="limit">      at which the scan should stop. </param>
        /// <returns> the offset at which the scan terminated. </returns>
        public static int Scan(DirectBuffer termBuffer, int offset, int limit)
        {
            do
            {
                int frameLength = FrameDescriptor.FrameLengthVolatile(termBuffer, offset);
                if (frameLength <= 0)
                {
                    break;
                }

                int alignedFrameLength = BitUtil.Align(frameLength, FrameDescriptor.FRAME_ALIGNMENT);
                offset += alignedFrameLength;
                if (offset >= limit)
                {
                    if (offset > limit)
                    {
                        offset -= alignedFrameLength;
                    }

                    break;
                }
            }
            while (true);

            return offset;
        }
    }
}