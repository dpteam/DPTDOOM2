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

        public RenderProfile Profile => renderer.Profile;

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

        /// <summary>Chooses which parts of the frame the next render draws.</summary>
        public Renderer.Layer Layer
        {
            get => renderer.RenderLayer;
            set => renderer.RenderLayer = value;
        }

        /// <summary>The colour grade applied to the palette, or null.</summary>
        public PaletteGrade Grade
        {
            get => renderer.Grade;
            set => renderer.Grade = value;
        }

        /// <summary>Writes the last frame as a PNG, with the key colour where nothing was drawn.</summary>
        public void SavePng(string path, Renderer.Layer layer, byte keyRed, byte keyGreen, byte keyBlue)
        {
            var frame = pixels;

            if (layer != Renderer.Layer.Full)
            {
                frame = new byte[pixels.Length];
                Array.Copy(pixels, frame, pixels.Length);

                var covered = renderer.Covered;

                if (covered != null)
                {
                    for (var i = 0; i < covered.Length; i++)
                    {
                        if (covered[i] != 0)
                        {
                            continue;
                        }

                        frame[i * 4] = keyRed;
                        frame[i * 4 + 1] = keyGreen;
                        frame[i * 4 + 2] = keyBlue;
                    }
                }
            }

            PngWriter.Save(path, frame, renderer.Width, renderer.Height);
        }

        /// <summary>
        /// Writes the last frame as an RGBA PNG whose alpha is the layer's own coverage.
        ///
        /// This is the form an editor wants for a weapon or HUD pass: a cut-out that drops straight
        /// onto another shot, with no key colour to remove and no fringe around the edges.
        /// </summary>
        public void SavePngWithAlpha(string path, Renderer.Layer layer)
        {
            var frame = new byte[pixels.Length];
            Array.Copy(pixels, frame, pixels.Length);

            if (layer != Renderer.Layer.Full)
            {
                var covered = renderer.Covered;

                if (covered != null)
                {
                    for (var i = 0; i < covered.Length; i++)
                    {
                        frame[i * 4 + 3] = covered[i] != 0 ? (byte)255 : (byte)0;
                    }
                }
            }

            PngWriter.SaveRgba(path, frame, renderer.Width, renderer.Height);
        }
    }
}