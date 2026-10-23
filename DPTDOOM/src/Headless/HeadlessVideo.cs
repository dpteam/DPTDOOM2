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
using System.IO;
using DPTDOOM.Video;

namespace DPTDOOM.Headless
{
    /// <summary>
    /// Software rendering without a window. Used by automated tests and by the
    /// headless build, and able to dump a real rendered frame to a BMP file so
    /// the renderer can be checked without a display.
    /// </summary>
    internal sealed class HeadlessVideo : IVideo
    {
        private readonly Renderer renderer;
        private readonly byte[] pixels;

        public HeadlessVideo(Config config, GameContent content)
        {
            renderer = new Renderer(config, content);
            pixels = new byte[renderer.Width * renderer.Height * 4];
        }

        public void Render(Doom doom, Fixed frameFrac)
        {
            renderer.Render(doom, pixels, frameFrac);
        }

        public void InitializeWipe()
        {
            renderer.InitializeWipe();
        }

        public bool HasFocus()
        {
            return true;
        }

        public int MaxWindowSize => renderer.MaxWindowSize;

        public int WindowSize
        {
            get => renderer.WindowSize;
            set => renderer.WindowSize = value;
        }

        public bool DisplayMessage
        {
            get => renderer.DisplayMessage;
            set => renderer.DisplayMessage = value;
        }

        public int MaxGammaCorrectionLevel => renderer.MaxGammaCorrectionLevel;

        public int GammaCorrectionLevel
        {
            get => renderer.GammaCorrectionLevel;
            set => renderer.GammaCorrectionLevel = value;
        }

        public int WipeBandCount => renderer.WipeBandCount;
        public int WipeHeight => renderer.WipeHeight;

        public int Width => renderer.Width;
        public int Height => renderer.Height;

        /// <summary>
        /// Writes the last rendered frame as a 24-bit bottom-up BMP. BMP is used
        /// because it needs no external encoder and every Windows version can
        /// open it.
        /// </summary>
        /// <summary>
        /// Writes the last rendered frame as a BMP. The encoder is shared with
        /// the debug menu so a captured frame looks the same either way.
        /// </summary>
        public void SaveBmp(string path)
        {
            BmpWriter.Save(path, pixels, renderer.Width, renderer.Height);
        }
    }
}