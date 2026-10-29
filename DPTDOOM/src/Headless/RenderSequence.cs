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
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DPTDOOM.Video;

namespace DPTDOOM.Headless
{
    /// <summary>
    /// Renders a run as a numbered PNG sequence, for an external encoder to assemble.
    ///
    /// This is the difference between a screenshot and footage, and every rule below exists to make
    /// a take **reproducible**:
    ///
    ///  - the frame rate is fixed, not measured: the pacer is advanced by an ideal frame time, so
    ///    frame N always sits at the same fraction of the same tic, whatever the machine was doing;
    ///  - the simulation still runs at 35 tics a second, and a frame that spans more than one tic
    ///    runs all of them, exactly as the windowed loop does;
    ///  - recording starts after a fixed number of tics, because the level-start wipe takes a
    ///    variable number of updates - it seeds its column heights from the clock - so a recording
    ///    that begins at frame zero cannot be repeated.
    ///
    /// The files are named <c>frame_00000.png</c> upwards, which is what every video tool expects.
    /// </summary>
    internal static class RenderSequence
    {
        public static int Run(CommandLineArgs args, Config config, GameContent content)
        {
            var directory = args.render.Value;

            Directory.CreateDirectory(directory);

            var fpsScale = args.renderfps.Present ? MathEx.Clamp(args.renderfps.Value, 1, 100) : 1;
            var resolution = args.renderres.Present ? MathEx.Clamp(args.renderres.Value, 1, 8) : 1;

            // The renderer is built for 320x200 scaled by a whole number. Setting the size before
            // the renderer exists is the only moment it can be chosen: the status bar and the
            // weapon place themselves against it when they are constructed.
            config.video_highresolution = resolution > 1;
            config.video_fpsscale = fpsScale;

            if (resolution > 2)
            {
                config.video_gamescreensize = ThreeDRenderer.MaxScreenSize;
            }

            var layer = ParseLayer(args);
            var keyed = layer != Renderer.Layer.Full;

            var video = new HeadlessVideo(config, content);
            var pacer = new FramePacer(fpsScale);

            video.Layer = layer;

            var grade = BuildGrade(args);

            if (grade != null && !grade.IsIdentity)
            {
                video.Grade = grade;
            }

            var doom = new Doom(args, config, content, video, null, null, null, null);

            var limit = args.tics.Present ? args.tics.Value : 0;
            var skip = args.renderskip.Present ? Math.Max(0, args.renderskip.Value) : 0;

            var frame = 0;
            var recorded = 0;
            var finished = false;

            // One loop iteration is one **frame**, and a frame may advance the world by more than
            // one tic. The frame index is therefore not the tic index, and neither is derived from
            // the other.
            while (!finished)
            {
                var step = pacer.Advance();

                for (var tic = 0; tic < step.UpdateCount && !finished; tic++)
                {
                    finished = doom.Update() == UpdateResult.Completed;
                }

                if (finished)
                {
                    break;
                }

                var simulated = doom.Game != null && doom.Game.World != null
                    ? doom.Game.World.LevelTime
                    : frame;

                if (simulated < skip)
                {
                    continue;
                }

                if (limit > 0 && simulated >= limit)
                {
                    break;
                }

                video.Render(doom, step.Fraction);

                var path = Path.Combine(directory,
                    "frame_" + recorded.ToString("00000", CultureInfo.InvariantCulture) + ".png");

                if (keyed)
                {
                    if (args.renderkey.Present &&
                        string.Equals(args.renderkey.Value, "alpha", StringComparison.OrdinalIgnoreCase))
                    {
                        video.SavePngWithAlpha(path, layer);
                    }
                    else
                    {
                        // Pure green by default: every encoder and editor has a chroma keyer, and a
                        // colour that appears nowhere in Doom's palette is the one that removes
                        // cleanly. Alpha is better where it is supported, and both are offered
                        // because a compositor may want either.
                        video.SavePng(path, layer, 0, 255, 0);
                    }
                }
                else
                {
                    video.SavePng(path, Renderer.Layer.Full, 0, 0, 0);
                }

                recorded++;
                frame++;
            }

            WriteManifest(directory, video, fpsScale, recorded, layer, args, grade);

            // The world hash, in the same form a headless run prints it. A take is footage, and
            // footage that cannot be shown to be the same run cannot be re-shot after an edit:
            // this line is what makes two renders of one command comparable.
            if (doom.Game != null && doom.Game.World != null)
            {
                Console.WriteLine(
                    "State: mobj=" + DoomDebug.GetMobjHash(doom.Game.World).ToString("x8") +
                    " sector=" + DoomDebug.GetSectorHash(doom.Game.World).ToString("x8") +
                    " tic=" + doom.Game.World.LevelTime);
            }

            Console.WriteLine("Rendered " + recorded + " frame(s) to " + directory);
            Console.WriteLine("Sequence: frame_%05d.png at " + (FramePacer.TicRate * fpsScale) +
                              " fps, " + video.Width + "x" + video.Height +
                              (layer == Renderer.Layer.Full ? "" : ", layer " + layer));

            return 0;
        }

        /// <summary>
        /// The description an assembler reads, so the frame rate and the size are not guessed.
        ///
        /// Written as plain <c>key = value</c> lines rather than JSON: it is read by a shell script
        /// as often as by a program, and the numbers are the whole content.
        /// </summary>
        private static void WriteManifest(string directory, HeadlessVideo video, int fpsScale,
                                          int frames, Renderer.Layer layer, CommandLineArgs args,
                                          PaletteGrade grade)
        {
            var text = new StringBuilder();

            text.AppendLine("frames = " + frames);
            text.AppendLine("fps = " +
                            (FramePacer.TicRate * fpsScale).ToString(CultureInfo.InvariantCulture));
            text.AppendLine("width = " + video.Width);
            text.AppendLine("height = " + video.Height);
            text.AppendLine("pattern = frame_%05d.png");
            text.AppendLine("layer = " + layer);

            if (layer != Renderer.Layer.Full)
            {
                text.AppendLine("key = " + (args.renderkey.Present ? args.renderkey.Value : "green"));
            }

            if (args.pitch.Present)
            {
                text.AppendLine("pitch = " + args.pitch.Value);
            }

            if (args.yaw.Present)
            {
                text.AppendLine("yaw = " + args.yaw.Value);
            }

            if (grade != null && !grade.IsIdentity)
            {
                text.AppendLine("grade = saturation " +
                                grade.Saturation.ToString("0.###", CultureInfo.InvariantCulture) +
                                ", tint " + grade.TintRed.ToString("0.###", CultureInfo.InvariantCulture) +
                                "/" + grade.TintGreen.ToString("0.###", CultureInfo.InvariantCulture) +
                                "/" + grade.TintBlue.ToString("0.###", CultureInfo.InvariantCulture) +
                                ", brightness " +
                                grade.Brightness.ToString("0.###", CultureInfo.InvariantCulture) +
                                ", contrast " +
                                grade.Contrast.ToString("0.###", CultureInfo.InvariantCulture) +
                                (grade.Posterise > 0 ? ", posterise " + grade.Posterise : ""));
            }

            File.WriteAllText(Path.Combine(directory, "render.txt"), text.ToString());
        }

        /// <summary>
        /// The named grades, as starting points that single switches can then adjust.
        ///
        /// Four rather than a hundred: each one is a look somebody actually asks for - a night
        /// scene, a black-and-white flashback, an old film, a brighter take for a thumbnail - and a
        /// long list of names nobody remembers is worse than four that are named in the docs.
        /// </summary>
        private static void ApplyPreset(PaletteGrade grade, string name)
        {
            switch (name == null ? "" : name.ToLowerInvariant())
            {
                case "night":
                    grade.Brightness = -0.12;
                    grade.Contrast = 1.15;
                    grade.Saturation = 0.75;
                    grade.TintRed = 0.75;
                    grade.TintGreen = 0.85;
                    grade.TintBlue = 1.25;
                    break;

                case "noir":
                    grade.Saturation = 0.0;
                    grade.Contrast = 1.35;
                    break;

                case "sepia":
                    grade.Saturation = 0.35;
                    grade.TintRed = 1.25;
                    grade.TintGreen = 1.05;
                    grade.TintBlue = 0.75;
                    grade.Contrast = 1.05;
                    break;

                case "vivid":
                    grade.Saturation = 1.35;
                    grade.Contrast = 1.1;
                    break;

                default:
                    Console.WriteLine("Render: unknown grade '" + name +
                                      "'; known grades are night, noir, sepia and vivid");
                    break;
            }
        }

        private static Renderer.Layer ParseLayer(CommandLineArgs args)
        {
            if (!args.renderlayer.Present)
            {
                return Renderer.Layer.Full;
            }

            var name = args.renderlayer.Value;

            if (string.Equals(name, "world", StringComparison.OrdinalIgnoreCase))
            {
                return Renderer.Layer.World;
            }

            if (string.Equals(name, "weapon", StringComparison.OrdinalIgnoreCase))
            {
                return Renderer.Layer.Weapon;
            }

            if (string.Equals(name, "hud", StringComparison.OrdinalIgnoreCase))
            {
                return Renderer.Layer.Hud;
            }

            if (string.Equals(name, "full", StringComparison.OrdinalIgnoreCase))
            {
                return Renderer.Layer.Full;
            }

            Console.WriteLine("Render: unknown layer '" + name +
                              "'; using full. Known layers: full, world, weapon, hud.");
            return Renderer.Layer.Full;
        }

        /// <summary>
        /// Builds the colour grade from the command line, or null when nothing was asked for.
        ///
        /// Several switches rather than one expression string: a grade is a handful of numbers, and
        /// a small parser for a made-up syntax would be one more thing to get wrong in a take.
        /// </summary>
        private static PaletteGrade BuildGrade(CommandLineArgs args)
        {
            if (!args.grade.Present && !args.saturation.Present && !args.tint.Present &&
                !args.brightness.Present && !args.contrast.Present && !args.posterise.Present)
            {
                return null;
            }

            var grade = new PaletteGrade();

            if (args.grade.Present)
            {
                ApplyPreset(grade, args.grade.Value);
            }

            if (args.saturation.Present)
            {
                grade.Saturation = args.saturation.Value;
            }

            if (args.tint.Present)
            {
                var parts = args.tint.Value.Split(',');

                if (parts.Length == 3)
                {
                    double red, green, blue;

                    if (double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out red) &&
                        double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out green) &&
                        double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out blue))
                    {
                        grade.TintRed = red;
                        grade.TintGreen = green;
                        grade.TintBlue = blue;
                    }
                    else
                    {
                        Console.WriteLine("Render: -tint needs three numbers such as 1.1,0.9,0.8");
                    }
                }
                else
                {
                    Console.WriteLine("Render: -tint needs three comma-separated numbers");
                }
            }

            if (args.brightness.Present)
            {
                grade.Brightness = args.brightness.Value;
            }

            if (args.contrast.Present)
            {
                grade.Contrast = args.contrast.Value;
            }

            if (args.posterise.Present)
            {
                grade.Posterise = MathEx.Clamp(args.posterise.Value, 0, 64);
            }

            return grade;
        }
    }
}
