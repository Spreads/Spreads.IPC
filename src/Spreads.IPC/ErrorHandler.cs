// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;

namespace Spreads.IPC
{
    /// <summary>
    /// Callback interface for handling an error/exception that has occurred when processing an operation or event.
    /// </summary>
    public delegate void ErrorHandler(Exception exception);
}