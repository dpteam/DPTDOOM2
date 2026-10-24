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

using System;

namespace DPTDOOM.Shared
{
    /// <summary>
    /// The project's name, version and colours, in one place.
    ///
    /// Shared rather than game-only because the name appears in four separate programs
    /// - the game, the launcher, the master and the console banner - and in four
    /// separate kinds of file: C# source, embedded HTML, CSS and the version resource.
    /// A fork that changes the name has to change all of them or ship a program that
    /// calls itself two things, and the parts it forgets are the ones nobody looks at
    /// until a bug report arrives with a screenshot of the wrong title.
    ///
    /// So: everything that is a *name* or a *colour* lives here, and the places that
    /// draw them ask. What stays outside, deliberately:
    ///
    ///  - the executable names (<c>dptdoom.exe</c>) are in the project files, because
    ///    an assembly name is not a runtime value;
    ///  - the icons are binary assets;
    ///  - the copyright and licence text is legal text, not branding, and changing it
    ///    is a licensing decision rather than a cosmetic one.
    ///
    /// The launcher page cannot reference C# at all - it is HTML inside MSHTML - so it
    /// is handed these values through the same bridge it already uses for everything
    /// else. That is why the colours are here as strings: the page needs exactly what
    /// CSS wants.
    /// </summary>
    public static class Branding
    {
        /// <summary>The public name, as it appears in window titles and on screen.</summary>
        public const string Name = "DPT.DOOM";

        /// <summary>The version, without a leading 'v'.</summary>
        public const string Version = "0.1.0";

        /// <summary>Name and version together, for banners and About screens.</summary>
        public const string Title = Name + " v" + Version;

        /// <summary>What the project is, in one line.</summary>
        public const string Tagline = "a source port of Doom";

        /// <summary>
        /// The primary brand colour: links, highlights, the active item in the launcher.
        ///
        /// The palette was the owner's choice and is not up for debate here; it lives in
        /// this file only so that a fork can change it once.
        /// </summary>
        public const string PrimaryColor = "#0088FF";

        /// <summary>The alternative brand colour, used where the primary would shout.</summary>
        public const string SecondaryColor = "#0080FF";

        /// <summary>The dark brand colour, for backgrounds behind the primary.</summary>
        public const string DarkColor = "#000088";

        /// <summary>The text colour on top of <see cref="DarkColor"/>.</summary>
        public const string OnDarkColor = "#FFFFFF";

        /// <summary>
        /// Where the project lives, for About screens and bug reports. Empty when a fork
        /// has no home to point at, and the screens then simply do not show a link.
        /// </summary>
        public const string HomeUrl = "https://github.com/DartPower/DPTDOOM";

        /// <summary>The name of the launcher window and process.</summary>
        public const string LauncherName = Name + " Launcher";

        /// <summary>The name of the directory server.</summary>
        public const string MasterName = Name + " master";
    }
}
