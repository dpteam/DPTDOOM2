//
// Copyright (C) 2026 DartPower Team
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//

using System.Runtime.InteropServices;

// The launcher host object is exposed to the page as window.external, which
// requires the assembly to allow COM access. Without this the browser control
// silently drops the object and the page sees an empty window.external.
[assembly: ComVisible(true)]
