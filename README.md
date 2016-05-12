**Status**: This is a code drop for LogBuffers port from Aeron that is used in Spreads. A full .NET Aeron client
[will be published very soon](https://github.com/real-logic/Aeron/issues/225), so there will be no activity in this repo.


Aeron.NET
----------

.NET port of [Aeron](https://github.com/real-logic/Aeron) client.

Currently only LogBuffers functionality is implemented. Full client functionality will be implemented sooner or later (without any ETA). Contributors are welcome!

This project was extracted from [Spreads](https://github.com/Spreads/Spreads) 
library where LogBuffers are used for IPC.


Porting
--------

Porting is strightforward line-by-line. UnsafeBuffer is replaced with Spreads's DirectBuffer. 
Classes are replaced with structs where possible.

Free tool Java to C# [convertor](http://www.tangiblesoftwaresolutions.com/) is usefull to format XML doc comments from Java format.

Currently C# coding style is F#-ish and follows Spreads coding style (as little lines as possible, 
for I mostly code on MacBook Air with large font). However, I will reformat this library to standard/classic 
C# style as soon as I learn how to use per-solution settings with Ctrl+E+D.



License (See LICENSE file for full license)
-------------------------------------------
Copyright 2016 Victor Baybekov (.NET port)
Copyright 2014 - 2016 Real Logic Limited (original Java code)

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
