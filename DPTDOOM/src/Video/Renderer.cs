//
// Copyright (C) 1993-1996 Id Software, Inc.
// Copyright (C) 2019-2020 Nobuaki Tanaka
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
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace DPTDOOM.Video
{
    public sealed class Renderer
    {
        private static double[] gammaCorrectionParameters = new double[]
        {
            1.00,
            0.95,
            0.90,
            0.85,
            0.80,
            0.75,
            0.70,
            0.65,
            0.60,
            0.55,
            0.50
        };

        private Config config;

        private Palette palette;

        private DrawScreen screen;

        private MenuRenderer menu;
        private ThreeDRenderer threeD;
        private StatusBarRenderer statusBar;
        private IntermissionRenderer intermission;
        private OpeningSequenceRenderer openingSequence;
        private AutoMapRenderer autoMap;
        private FinaleRenderer finale;

        private Patch pause;

        private int wipeBandWidth;
        private int wipeBandCount;
        private int wipeHeight;
        private byte[] wipeBuffer;

        public Renderer(Config config, GameContent content)
        {
            this.config = config;

            palette = content.Palette;

            if (config.video_highresolution)
            {
                screen = new DrawScreen(content.Wad, 640, 400);
            }
            else
            {
                screen = new DrawScreen(content.Wad, 320, 200);
            }

            config.video_gamescreensize = MathEx.Clamp(config.video_gamescreensize, 0, MaxWindowSize);
            config.video_gammacorrection = MathEx.Clamp(config.video_gammacorrection, 0, MaxGammaCorrectionLevel);

            menu = new MenuRenderer(content.Wad, screen);
            threeD = new ThreeDRenderer(content, screen, config.video_gamescreensize);
            statusBar = new StatusBarRenderer(content.Wad, screen);
            intermission = new IntermissionRenderer(content.Wad, screen);
            openingSequence = new OpeningSequenceRenderer(content.Wad, screen, this);
            autoMap = new AutoMapRenderer(content.Wad, screen);
            finale = new FinaleRenderer(content, screen);

            pause = Patch.FromWad(content.Wad, "M_PAUSE");

            var scale = screen.Width / 320;
            wipeBandWidth = 2 * scale;
            wipeBandCount = screen.Width / wipeBandWidth + 1;
            wipeHeight = screen.Height / scale;
            wipeBuffer = new byte[screen.Data.Length];

            palette.ResetColors(gammaCorrectionParameters[config.video_gammacorrection]);
        }

        public void RenderDoom(Doom doom, Fixed frameFrac)
        {
            if (doom.State == DoomState.Opening)
            {
                openingSequence.Render(doom.Opening, frameFrac);
            }
            else if (doom.State == DoomState.DemoPlayback)
            {
                RenderGame(doom.DemoPlayback.Game, frameFrac);
            }
            else if (doom.State == DoomState.Game)
            {
                RenderGame(doom.Game, frameFrac);
            }

            if (!doom.Menu.Active)
            {
                if (doom.State == DoomState.Game &&
                    doom.Game.State == GameState.Level &&
                    doom.Game.Paused)
                {
                    var scale = screen.Width / 320;
                    screen.DrawPatch(
                        pause,
                        (screen.Width - scale * pause.Width) / 2,
                        4 * scale,
                        scale);
                }
            }
        }

        /// <summary>
        /// Where the frame time goes. Owned here rather than by the window
        /// because a frame is what this class draws: the world, the HUD and the
        /// final palette lookup all belong to the same sample.
        /// </summary>
        public RenderProfile Profile => threeD.Profile;

        public void RenderMenu(Doom doom)
        {
            if (doom.Menu.Active)
            {
                menu.Render(doom.Menu);
            }
        }

        public void RenderGame(DoomGame game, Fixed frameFrac)
        {
            if (game.Paused)
            {
                frameFrac = Fixed.One;
            }

            if (game.State == GameState.Level && game.World != null)
            {
                var consolePlayer = game.World.ConsolePlayer;
                var displayPlayer = game.World.DisplayPlayer;

                // MapOnly is a view override, not a world change: it forces the
                // automap without touching the session, which is why it can be
                // honoured here rather than by opening the automap somewhere in the
                // game loop. It used to be applied only by the window's own toggle,
                // so "-maponly" in a headless run did nothing and a screenshot of
                // the debug overlays was impossible to take.
                if (game.World.AutoMap.Visible || DebugSettings.MapOnly)
                {
                    var hudStart = Profile.Stamp();
                    autoMap.Render(consolePlayer);
                    statusBar.Render(consolePlayer, true);
                    Profile.Add(RenderProfile.Section.Hud, Profile.Stamp() - hudStart);
                }
                else
                {
                    threeD.Render(displayPlayer, frameFrac);

                    var hudStart = Profile.Stamp();
                    if (threeD.WindowSize < 8)
                    {
                        statusBar.Render(consolePlayer, true);
                    }
                    else if (threeD.WindowSize == ThreeDRenderer.MaxScreenSize)
                    {
                        statusBar.Render(consolePlayer, false);
                    }

                    Profile.Add(RenderProfile.Section.Hud, Profile.Stamp() - hudStart);
                }

                if (config.video_displaymessage ||
                    consolePlayer.MessageAlwaysShown ||
                    ReferenceEquals(consolePlayer.Message, (string)DoomInfo.Strings.MSGOFF))
                {
                    if (consolePlayer.MessageTime > 0)
                    {
                        var hudStart = Profile.Stamp();
                        var scale = screen.Width / 320;
                        screen.DrawText(consolePlayer.Message, 0, 7 * scale, scale);
                        Profile.Add(RenderProfile.Section.Hud, Profile.Stamp() - hudStart);
                    }
                }
            }
            else if (game.State == GameState.Intermission)
            {
                intermission.Render(game.Intermission);
            }
            else if (game.State == GameState.Finale)
            {
                finale.Render(game.Finale);
            }
        }

        public void Render(Doom doom, byte[] destination, Fixed frameFrac)
        {
            if (doom.Wiping)
            {
                RenderWipe(doom, destination);
                return;
            }

            // One frame is one sample. The wipe frames above are deliberately not
            // counted: they draw the same world twice and would double every
            // average in the report.
            Profile.BeginFrame();

            RenderDoom(doom, frameFrac);
            RenderMenu(doom);

            var colors = palette[0];
            if (doom.State == DoomState.Game &&
                doom.Game.State == GameState.Level &&
                doom.Game.World != null)
            {
                colors = palette[GetPaletteNumber(doom.Game.World.ConsolePlayer)];
            }
            else if (doom.State == DoomState.Opening &&
                doom.Opening.State == OpeningSequenceState.Demo &&
                doom.Opening.DemoGame.State == GameState.Level)
            {
                colors = palette[GetPaletteNumber(doom.Opening.DemoGame.World.ConsolePlayer)];
            }
            else if (doom.State == DoomState.DemoPlayback &&
                doom.DemoPlayback.Game.State == GameState.Level)
            {
                colors = palette[GetPaletteNumber(doom.DemoPlayback.Game.World.ConsolePlayer)];
            }

            var convertStart = Profile.Stamp();
            WriteData(colors, destination);
            Profile.Add(RenderProfile.Section.Convert, Profile.Stamp() - convertStart);

            Profile.EndFrame();
        }

        private void RenderWipe(Doom doom, byte[] destination)
        {
            RenderDoom(doom, Fixed.One);

            var wipe = doom.WipeEffect;
            var scale = screen.Width / 320;
            for (var i = 0; i < wipeBandCount - 1; i++)
            {
                var x1 = wipeBandWidth * i;
                var x2 = x1 + wipeBandWidth;
                var y1 = Math.Max(scale * wipe.Y[i], 0);
                var y2 = Math.Max(scale * wipe.Y[i + 1], 0);
                var dy = (float)(y2 - y1) / wipeBandWidth;
                for (var x = x1; x < x2; x++)
                {
                    var y = (int)MathF.Round(y1 + dy * ((x - x1) / 2 * 2));
                    var copyLength = screen.Height - y;
                    if (copyLength > 0)
                    {
                        var srcPos = screen.Height * x;
                        var dstPos = screen.Height * x + y;
                        Array.Copy(wipeBuffer, srcPos, screen.Data, dstPos, copyLength);
                    }
                }
            }

            RenderMenu(doom);

            WriteData(palette[0], destination);
        }

        public void InitializeWipe()
        {
            Array.Copy(screen.Data, wipeBuffer, screen.Data.Length);
        }

        // Palette indices from Doom's own PLAYPAL. The status screen cannot tint the
        // font (see RenderStatus), but it can still draw its frame and its progress
        // bar in Doom's colours instead of whatever index happens to look red.
        private const int ColorRule = 4;    // dark red, like Doom's own borders
        private const int ColorFrame = 4;
        private const int ColorFill = 80;   // dark grey for the bar's trough
        private const int ColorBar = 176;   // the HUD's red

        /// <summary>
        /// Draws the connection status screen.
        ///
        /// This is the first thing a player sees when they host or join, and it
        /// used to be five lines of red text on black at one size, so "waiting for
        /// a player" and "the connection failed" looked identical - both read as
        /// the game having hung. The layout now separates them: the product name
        /// identifies the program, the state is the largest thing on screen, the
        /// details sit under a rule, and the wait has a bar that moves.
        ///
        /// Colour is deliberately not used to tell them apart. Doom's STCFN glyphs
        /// carry no transparency in their pixel data - the holes in a letter come
        /// from the missing columns of the patch - so any attempt to tint a glyph
        /// paints the whole form solid. Size, spacing and the frame do the work
        /// instead, and the font keeps its own red, which is what a player of this
        /// game expects to see.
        ///
        /// The state word is scaled to fit, not fixed: "HOSTING" at double size is
        /// comfortable, "CONNECTION FAILED" at double size runs past the frame.
        ///
        /// The bar is driven by the "WAITING FOR PLAYER n/m" line that NetStartup
        /// already produces. Parsing our own output is mildly inelegant, but the
        /// alternative is widening the status delegate with a progress structure for
        /// one screen; the parse degrades to "no bar" if the text ever changes.
        /// </summary>
        public void RenderStatus(string title, IList<string> lines, byte[] destination)
        {
            Array.Clear(screen.Data, 0, screen.Data.Length);

            var scale = screen.Width / 320;

            var boxWidth = screen.Width - 40 * scale;

            var stateScale = FitScale(title, scale * 2, boxWidth - 20 * scale);

            // Long detail lines are shrunk rather than clipped: "127.0.0.1:5999
            // REFUSED - IS THE HOST RUNNING?" at normal size is wider than the box
            // and ran out through the frame on both sides.
            var detailScale = scale;

            foreach (var line in lines)
            {
                var fitted = FitScale(line, scale, boxWidth - 24 * scale);

                if (fitted < detailScale)
                {
                    detailScale = fitted;
                }
            }

            var titleScale = scale;

            var detailHeight = TextHeight(detailScale) + 4 * scale;
            var footerHeight = TextHeight(detailScale) + 10 * scale;
            var barHeight = 6 * scale + 6 * scale;

            // The box has to be tall enough for its contents. A guessed height put
            // the bar and the hint on top of the last detail line, which is why the
            // screen showed a smear of overlapping glyphs instead of three readable
            // lines.
            var content = 14 * scale
                          + TextHeight(titleScale) + 5 * scale
                          + TextHeight(stateScale) + 7 * scale
                          + 9 * scale
                          + lines.Count * detailHeight
                          + (lines.Count > 0 ? barHeight : 0)
                          + footerHeight
                          + 4 * scale;

            var boxHeight = Math.Min(screen.Height - 20 * scale, content);
            var boxX = (screen.Width - boxWidth) / 2;
            var boxY = (screen.Height - boxHeight) / 2;

            DrawFrame(boxX, boxY, boxWidth, boxHeight, scale);

            var y = boxY + 14 * scale;

            // Layout is measured from the *top* of each line. DrawText anchors at
            // the baseline and grows upward by 7*scale, so mixing the two made the
            // double-size state climb back over the line above it.
            DrawTextTop("DPT.DOOM", y, titleScale);
            y += TextHeight(titleScale) + 5 * scale;

            DrawTextTop(title, y, stateScale);
            y += TextHeight(stateScale) + 7 * scale;

            // Separator, so the details read as a block rather than as more title.
            screen.FillRect(boxX + 10 * scale, y, boxWidth - 20 * scale, Math.Max(scale / 2, 1), ColorRule);
            y += 9 * scale;

            var progress = FindProgress(lines);

            foreach (var line in lines)
            {
                DrawTextTop(line, y, detailScale);
                y += detailHeight;
            }

            if (progress >= 0F)
            {
                DrawProgressBar(boxX, boxWidth, y + 2 * scale, scale, progress);
            }

            DrawTextTop("ESC  CANCEL", boxY + boxHeight - 10 * scale - TextHeight(scale), scale);

            WriteData(palette[0], destination);
        }

        /// <summary>
        /// Two nested rules, which is what makes a rectangle read as a panel
        /// instead of as a stray box. Cheap: four filled rectangles each.
        /// </summary>
        private void DrawFrame(int x, int y, int w, int h, int scale)
        {
            var thickness = Math.Max(scale, 1);

            // Outer rule.
            screen.FillRect(x, y, w, thickness, ColorFrame);
            screen.FillRect(x, y + h - thickness, w, thickness, ColorFrame);
            screen.FillRect(x, y, thickness, h, ColorFrame);
            screen.FillRect(x + w - thickness, y, thickness, h, ColorFrame);

            // Inner rule, inset by a few pixels.
            var inset = 3 * scale;

            if (w > inset * 3 && h > inset * 3)
            {
                screen.FillRect(x + inset, y + inset, w - inset * 2, Math.Max(scale / 2, 1), ColorFrame);
                screen.FillRect(x + inset, y + h - inset, w - inset * 2, Math.Max(scale / 2, 1), ColorFrame);
            }
        }

        /// <summary>
        /// Reads "WAITING FOR PLAYER 1/3" out of the status lines and returns
        /// 0..1, or -1 when the line is absent or unreadable.
        /// </summary>
        private static float FindProgress(IList<string> lines)
        {
            const string marker = "WAITING FOR PLAYER ";

            foreach (var line in lines)
            {
                if (line == null)
                {
                    continue;
                }

                var at = line.IndexOf(marker, StringComparison.Ordinal);

                if (at < 0)
                {
                    continue;
                }

                var text = line.Substring(at + marker.Length);
                var slash = text.IndexOf('/');

                if (slash <= 0)
                {
                    continue;
                }

                int have;
                int want;

                if (!int.TryParse(text.Substring(0, slash), out have) ||
                    !int.TryParse(text.Substring(slash + 1), out want) ||
                    want <= 0)
                {
                    return -1F;
                }

                var value = (float)have / want;

                return value < 0F ? 0F : (value > 1F ? 1F : value);
            }

            return -1F;
        }

        private void DrawProgressBar(int boxX, int boxWidth, int y, int scale, float progress)
        {
            var width = boxWidth - 40 * scale;
            var height = 6 * scale;
            var x = boxX + 20 * scale;

            screen.FillRect(x, y, width, height, ColorFill);
            screen.FillRect(x, y, (int)(width * progress), height, ColorBar);

            // A one-pixel outline keeps the empty part visible on a dark panel, so
            // the bar reads as "this much of the way" rather than as a lighter
            // rectangle that happened to appear.
            screen.FillRect(x, y, width, Math.Max(scale / 2, 1), ColorFrame);
            screen.FillRect(x, y + height - Math.Max(scale / 2, 1), width, Math.Max(scale / 2, 1), ColorFrame);
        }

        private void DrawCentered(string text, int y, int scale)
        {
            var width = 0;
            foreach (var ch in text)
            {
                width += screen.MeasureChar(ch, scale);
            }

            var x = (screen.Width - width) / 2;
            screen.DrawText(text, x, y, scale);
        }

        /// <summary>Height of one line of the HUD font, in pixels.</summary>
        private static int TextHeight(int scale)
        {
            return 7 * scale;
        }

        /// <summary>
        /// Largest scale, at most <paramref name="max"/>, at which the text fits in
        /// <paramref name="width"/>. Falls back to 1 so a very long title still
        /// appears small rather than being clipped.
        /// </summary>
        private int FitScale(string text, int max, int width)
        {
            for (var s = max; s > 1; s--)
            {
                var measured = 0;
                foreach (var ch in text)
                {
                    measured += screen.MeasureChar(ch, s);
                }

                if (measured <= width)
                {
                    return s;
                }
            }

            return 1;
        }

        /// <summary>
        /// Draws centred text with the given *top* edge.
        ///
        /// DrawText anchors at the baseline, so drawing a double-size line at the
        /// same y as a single-size one makes it grow upward through it. Everything
        /// on this screen is positioned from the top, which is the way the layout is
        /// actually thought about.
        /// </summary>
        private void DrawTextTop(string text, int top, int scale)
        {
            DrawCentered(text, top + TextHeight(scale), scale);
        }

        private unsafe void WriteData(uint[] colors, byte[] destination)
        {
            var screenData = screen.Data;
            var width = screen.Width;
            var height = screen.Height;

            fixed (byte* destinationPointer = destination)
            {
                var destinationData = (uint*)destinationPointer;

                // DrawScreen keeps its pixels in column-major order; the
                // framebuffer handed to the platform is a normal top-down
                // row-major image.
                for (var y = 0; y < height; y++)
                {
                    var row = y * width;
                    for (var x = 0; x < width; x++)
                    {
                        destinationData[row + x] = colors[screenData[x * height + y]];
                    }
                }
            }
        }

        private static int GetPaletteNumber(Player player)
        {
            var count = player.DamageCount;

            if (player.Powers[(int)PowerType.Strength] != 0)
            {
                // Slowly fade the berzerk out.
                var bzc = 12 - (player.Powers[(int)PowerType.Strength] >> 6);
                if (bzc > count)
                {
                    count = bzc;
                }
            }

            int palette;

            if (count != 0)
            {
                palette = (count + 7) >> 3;

                if (palette >= Palette.DamageCount)
                {
                    palette = Palette.DamageCount - 1;
                }

                palette += Palette.DamageStart;
            }
            else if (player.BonusCount != 0)
            {
                palette = (player.BonusCount + 7) >> 3;

                if (palette >= Palette.BonusCount)
                {
                    palette = Palette.BonusCount - 1;
                }

                palette += Palette.BonusStart;
            }
            else if (player.Powers[(int)PowerType.IronFeet] > 4 * 32 ||
                (player.Powers[(int)PowerType.IronFeet] & 8) != 0)
            {
                palette = Palette.IronFeet;
            }
            else
            {
                palette = 0;
            }

            return palette;
        }

        public int Width => screen.Width;
        public int Height => screen.Height;

        public int WipeBandCount => wipeBandCount;
        public int WipeHeight => wipeHeight;

        public int MaxWindowSize
        {
            get
            {
                return ThreeDRenderer.MaxScreenSize;
            }
        }

        public int WindowSize
        {
            get
            {
                return threeD.WindowSize;
            }

            set
            {
                config.video_gamescreensize = value;
                threeD.WindowSize = value;
            }
        }

        public bool DisplayMessage
        {
            get
            {
                return config.video_displaymessage;
            }

            set
            {
                config.video_displaymessage = value;
            }
        }

        public int MaxGammaCorrectionLevel
        {
            get
            {
                return gammaCorrectionParameters.Length - 1;
            }
        }

        public int GammaCorrectionLevel
        {
            get
            {
                return config.video_gammacorrection;
            }

            set
            {
                config.video_gammacorrection = value;
                palette.ResetColors(gammaCorrectionParameters[config.video_gammacorrection]);
            }
        }
    }
}
