// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using Spreads.Buffers;

namespace Spreads.IPC.Logbuffer
{
    public delegate void FragmentHandler(DirectBuffer buffer, int offset, int length, Header header);
}