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
using System.Runtime.InteropServices;
using DPTDOOM.Video;

namespace DPTDOOM.Win32
{
    /// <summary>
    /// Software renderer output presented through a Win32 DIB. The renderer
    /// works in a column-major byte buffer; this class owns the pinned BGRA
    /// framebuffer that GDI actually draws.
    /// </summary>
    internal sealed class Win32Video : IVideo, IDisposable
    {
        private readonly Config config;
        private readonly Renderer renderer;
        private readonly byte[] pixels;
        private readonly GCHandle pixelsHandle;
        private readonly IntPtr pixelsPointer;
        private readonly Win32Window window;

        private bool disposed;

        public Win32Video(Config config, GameContent content)
        {
            this.config = config;

            renderer = new Renderer(config, content);
            pixels = new byte[renderer.Width * renderer.Height * 4];
            pixelsHandle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            pixelsPointer = pixelsHandle.AddrOfPinnedObject();

            window = new Win32Window(
                ApplicationInfo.Title,
                config.video_screenwidth,
                config.video_screenheight,
                config.video_fullscreen);
            window.Create();
        }

        public void Render(Doom doom, Fixed frameFrac)
        {
            var start = System.Diagnostics.Stopwatch.GetTimestamp();

            renderer.Render(doom, pixels, frameFrac);

            var drawn = System.Diagnostics.Stopwatch.GetTimestamp();

            window.Present(pixelsPointer, renderer.Width, renderer.Height);

            var presented = System.Diagnostics.Stopwatch.GetTimestamp();

            if (Profiling)
            {
                var frequency = System.Diagnostics.Stopwatch.Frequency;
                LastRenderMs = (drawn - start) * 1000.0 / frequency;
                LastPresentMs = (presented - drawn) * 1000.0 / frequency;
            }
        }

        /// <summary>Enables the per-frame render/present split in the fps log.</summary>
        public bool Profiling { get; set; }

        public double LastRenderMs { get; private set; }

        public double LastPresentMs { get; private set; }

        public void InitializeWipe()
        {
            renderer.InitializeWipe();
        }

        /// <summary>
        /// Shows a status screen (connection progress, errors) instead of the
        /// black window that would otherwise be visible while the session is
        /// being established.
        /// </summary>
        public void ShowStatus(string title, params string[] lines)
        {
            renderer.RenderStatus(title, lines, pixels);
            window.Present(pixelsPointer, renderer.Width, renderer.Height);
        }

        public bool HasFocus()
        {
            return window.HasFocus;
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

        public Win32Window Window => window;

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;

            window.Dispose();

            if (pixelsHandle.IsAllocated)
            {
                pixelsHandle.Free();
            }
        }
    }
}
