// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using Spreads.Buffers;

namespace Spreads.IPC.Logbuffer
{
    internal delegate ControlledFragmentHandlerAction ControlledFragmentHandler(DirectBuffer buffer,
        int offset, int length, Header header);

    internal enum ControlledFragmentHandlerAction
    {
        /// <summary>
        /// Abort the current polling operation and do not advance the position for this fragment.
        /// </summary>
        ABORT,

        /// <summary>
        /// Break from the current polling operation and commit the position as of the end of the current fragment
        /// being handled.
        /// </summary>
        BREAK,

        /// <summary>
        /// Continue processing but commit the position as of the end of the current fragment so that
        /// flow control is applied to this point.
        /// </summary>
        COMMIT,

        /// <summary>
        /// Continue processing taking the same approach as the in
        /// <seealso cref="FragmentHandler#onFragment(DirectBuffer, int, int, Header)"/>.
        /// </summary>
        CONTINUE,
    }
}