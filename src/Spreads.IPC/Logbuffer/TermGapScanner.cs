// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using Spreads.Buffers;
using Spreads.IPC.Logbuffer.Protocol;
using Spreads.Utils;

namespace Spreads.IPC.Logbuffer
{
    internal delegate void GapHandler(int termId, DirectBuffer buffer, int offset, int length);

    internal class TermGapScanner
    {
        private static int ALIGNED_HEADER_LENGTH = BitUtil.Align(DataHeaderFlyweight.HEADER_LENGTH, FrameDescriptor.FRAME_ALIGNMENT);

        /**
         * Scan for gaps from the rebuildOffset up to the high-water-mark. Each gap will be reported to the {@link GapHandler}.
         *
         * @param termBuffer    to be scanned for a gap.
         * @param termId        of the current term buffer.
         * @param rebuildOffset at which to start scanning.
         * @param hwmOffset     at which to stop scanning.
         * @param handler       to call if a gap is found.
         * @return offset of last contiguous frame
         */

        public static int ScanForGap(
            DirectBuffer termBuffer, int termId, int rebuildOffset, int hwmOffset, GapHandler handler)
        {
            do
            {
                int frameLength = FrameDescriptor.FrameLengthVolatile(termBuffer, rebuildOffset);
                if (frameLength <= 0)
                {
                    break;
                }

                rebuildOffset += BitUtil.Align(frameLength, FrameDescriptor.FRAME_ALIGNMENT);
            }
            while (rebuildOffset < hwmOffset);

            int gapBeginOffset = rebuildOffset;
            if (rebuildOffset < hwmOffset)
            {
                int limit = hwmOffset - ALIGNED_HEADER_LENGTH;
                while (rebuildOffset < limit)
                {
                    rebuildOffset += FrameDescriptor.FRAME_ALIGNMENT;

                    if (0 != termBuffer.VolatileReadInt32(rebuildOffset))
                    {
                        rebuildOffset -= ALIGNED_HEADER_LENGTH;
                        break;
                    }
                }

                int gapLength = (rebuildOffset - gapBeginOffset) + ALIGNED_HEADER_LENGTH;
                handler?.Invoke(termId, termBuffer, gapBeginOffset, gapLength);
            }

            return gapBeginOffset;
        }
    }
}