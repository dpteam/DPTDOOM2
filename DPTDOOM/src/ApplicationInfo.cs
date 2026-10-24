//
// Copyright (C) 1993-1996 Id Software, Inc.
// Copyright (C) 2019-2020 Nobuaki Tanaka
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

using System;
using DPTDOOM.Shared;

namespace DPTDOOM
{
    /// <summary>
    /// The identity of this build, as the game sees it.
    ///
    /// Everything comes from <see cref="Branding"/>, which lives in the shared assembly
    /// because the launcher and the master need the same strings and the page inside the
    /// launcher cannot reference C# at all. This class stays as the game's short name for
    /// it: the rest of the engine already asks <c>ApplicationInfo.Title</c>, and moving
    /// that call everywhere would be churn for no gain.
    /// </summary>
    public static class ApplicationInfo
    {
        public const string Name = Branding.Name;
        public const string Version = Branding.Version;
        public const string Title = Branding.Title;
    }
}
