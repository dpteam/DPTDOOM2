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
using System.IO;

namespace DPTDOOM
{
    public sealed class Config
    {
        public KeyBinding key_forward;
        public KeyBinding key_backward;
        public KeyBinding key_strafeleft;
        public KeyBinding key_straferight;
        public KeyBinding key_turnleft;
        public KeyBinding key_turnright;
        public KeyBinding key_fire;
        public KeyBinding key_use;
        public KeyBinding key_run;
        public KeyBinding key_strafe;
        public KeyBinding key_lookup;
        public KeyBinding key_lookdown;
        public KeyBinding key_center;

        public int mouse_sensitivity;
        public bool mouse_disableyaxis;

        public bool game_alwaysrun;

        /// <summary>
        /// Vertical look, Duke Nukem 3D style: the mouse and the look keys tilt the
        /// view instead of moving the player.
        ///
        /// Client-side only. Pitch never enters a TicCmd, so it cannot desynchronise
        /// a lockstep session or a demo - and, for the same reason, it does not aim:
        /// shots keep using Doom's own 2D autoaim. That is a real limitation and the
        /// menu says "обзор", not "прицеливание".
        /// </summary>
        public bool view_freelook;

        /// <summary>0 off, 1 cross, 2 dot, 3 cross with a dot.</summary>
        public int hud_crosshair;

        /// <summary>
        /// Crosshair colours, as indices into PLAYPAL.
        ///
        /// Indices rather than RGB because the screen is 8-bit: a colour has to be
        /// one of the 256 the game already has, and picking the nearest entry from an
        /// RGB triple would give a different answer in every IWAD. The settings
        /// screen offers named presets and resolves them here at startup.
        /// </summary>
        public int hud_crosshair_light;
        public int hud_crosshair_dark;

        /// <summary>
        /// How much the frame counter shows: 0 off, then a difficulty ladder.
        ///
        /// Named after Doom's own skill levels, because "how detailed do you want
        /// this" is a question the game already has a vocabulary for, and a list of
        /// numbers 0-5 says nothing about what each one adds.
        /// </summary>
        public int hud_profiler;

        public int video_screenwidth;
        public int video_screenheight;
        public bool video_fullscreen;
        public bool video_highresolution;
        public bool video_displaymessage;
        public int video_gamescreensize;
        public int video_gammacorrection;
        public int video_fpsscale;

        public int audio_soundvolume;
        public int audio_musicvolume;
        public bool audio_randompitch;
        public string audio_soundfont;
        public bool audio_musiceffect;

        private bool isRestoredFromFile;

        // Default settings.
        public Config()
        {
            key_forward = new KeyBinding(
                new DoomKey[]
                {
                    DoomKey.Up,
                    DoomKey.W
                });
            key_backward = new KeyBinding(
                new DoomKey[]
                {
                    DoomKey.Down,
                    DoomKey.S
                });
            key_strafeleft = new KeyBinding(
                new DoomKey[]
                {
                    DoomKey.A
                });
            key_straferight = new KeyBinding(
                new DoomKey[]
                {
                    DoomKey.D
                });
            key_turnleft = new KeyBinding(
                new DoomKey[]
                {
                    DoomKey.Left
                });
            key_turnright = new KeyBinding(
                new DoomKey[]
                {
                    DoomKey.Right
                });
            key_fire = new KeyBinding(
                new DoomKey[]
                {
                    DoomKey.LControl,
                    DoomKey.RControl
                },
                new DoomMouseButton[]
                {
                    DoomMouseButton.Mouse1
                });
            key_use = new KeyBinding(
                new DoomKey[]
                {
                    DoomKey.Space
                },
                new DoomMouseButton[]
                {
                    DoomMouseButton.Mouse2
                });
            key_run = new KeyBinding(
                new DoomKey[]
                {
                    DoomKey.LShift,
                    DoomKey.RShift
                });
            key_strafe = new KeyBinding(
                new DoomKey[]
                {
                    DoomKey.LAlt,
                    DoomKey.RAlt
                });
            key_lookup = new KeyBinding(
                new DoomKey[]
                {
                    DoomKey.PageUp
                });
            key_lookdown = new KeyBinding(
                new DoomKey[]
                {
                    DoomKey.PageDown
                });
            key_center = new KeyBinding(
                new DoomKey[]
                {
                    DoomKey.End
                });

            mouse_sensitivity = 8;
            mouse_disableyaxis = false;

            game_alwaysrun = true;

            // Off by default: Doom's own aiming is 2D, so tilting the view changes
            // what you see and not what you hit. A player who does not know that
            // would think the shots were broken.
            view_freelook = false;

            hud_crosshair = 1;
            hud_crosshair_light = -1;
            hud_crosshair_dark = -1;
            hud_profiler = 0;

            video_screenwidth = 640;
            video_screenheight = 400;
            video_fullscreen = false;
            video_highresolution = true;
            video_gamescreensize = 7;
            video_displaymessage = true;
            video_gammacorrection = 2;
            video_fpsscale = 2;

            audio_soundvolume = 8;
            audio_musicvolume = 8;
            audio_randompitch = true;
            audio_soundfont = "TimGM6mb.sf2";
            audio_musiceffect = true;

            isRestoredFromFile = false;
        }

        public Config(string path) : this()
        {
            try
            {
                Console.Write("Restore settings: ");

                var dic = new Dictionary<string, string>();
                foreach (var line in File.ReadLines(path))
                {
                    var split = line.Split(new[] { '=' }, StringSplitOptions.RemoveEmptyEntries);
                    if (split.Length == 2)
                    {
                        dic[split[0].Trim()] = split[1].Trim();
                    }
                }

                key_forward = GetKeyBinding(dic, nameof(key_forward), key_forward);
                key_backward = GetKeyBinding(dic, nameof(key_backward), key_backward);
                key_strafeleft = GetKeyBinding(dic, nameof(key_strafeleft), key_strafeleft);
                key_straferight = GetKeyBinding(dic, nameof(key_straferight), key_straferight);
                key_turnleft = GetKeyBinding(dic, nameof(key_turnleft), key_turnleft);
                key_turnright = GetKeyBinding(dic, nameof(key_turnright), key_turnright);
                key_fire = GetKeyBinding(dic, nameof(key_fire), key_fire);
                key_use = GetKeyBinding(dic, nameof(key_use), key_use);
                key_run = GetKeyBinding(dic, nameof(key_run), key_run);
                key_strafe = GetKeyBinding(dic, nameof(key_strafe), key_strafe);
                key_lookup = GetKeyBinding(dic, nameof(key_lookup), key_lookup);
                key_lookdown = GetKeyBinding(dic, nameof(key_lookdown), key_lookdown);
                key_center = GetKeyBinding(dic, nameof(key_center), key_center);

                mouse_sensitivity = GetInt(dic, nameof(mouse_sensitivity), mouse_sensitivity);
                mouse_disableyaxis = GetBool(dic, nameof(mouse_disableyaxis), mouse_disableyaxis);

                game_alwaysrun = GetBool(dic, nameof(game_alwaysrun), game_alwaysrun);

                view_freelook = GetBool(dic, nameof(view_freelook), view_freelook);
                hud_crosshair = GetInt(dic, nameof(hud_crosshair), hud_crosshair);
                hud_crosshair_light = GetInt(dic, nameof(hud_crosshair_light), hud_crosshair_light);
                hud_crosshair_dark = GetInt(dic, nameof(hud_crosshair_dark), hud_crosshair_dark);
                hud_profiler = GetInt(dic, nameof(hud_profiler), hud_profiler);

                video_screenwidth = GetInt(dic, nameof(video_screenwidth), video_screenwidth);
                video_screenheight = GetInt(dic, nameof(video_screenheight), video_screenheight);
                video_fullscreen = GetBool(dic, nameof(video_fullscreen), video_fullscreen);
                video_highresolution = GetBool(dic, nameof(video_highresolution), video_highresolution);
                video_displaymessage = GetBool(dic, nameof(video_displaymessage), video_displaymessage);
                video_gamescreensize = GetInt(dic, nameof(video_gamescreensize), video_gamescreensize);
                video_gammacorrection = GetInt(dic, nameof(video_gammacorrection), video_gammacorrection);
                video_fpsscale = GetInt(dic, nameof(video_fpsscale), video_fpsscale);

                audio_soundvolume = GetInt(dic, nameof(audio_soundvolume), audio_soundvolume);
                audio_musicvolume = GetInt(dic, nameof(audio_musicvolume), audio_musicvolume);
                audio_randompitch = GetBool(dic, nameof(audio_randompitch), audio_randompitch);
                audio_soundfont = GetString(dic, nameof(audio_soundfont), audio_soundfont);
                audio_musiceffect = GetBool(dic, nameof(audio_musiceffect), audio_musiceffect);

                isRestoredFromFile = true;

                Console.WriteLine("OK");
            }
            catch
            {
                Console.WriteLine("Failed");
            }
        }

        public void Save(string path)
        {
            try
            {
                using (var writer = new StreamWriter(path))
                {
                    writer.WriteLine(nameof(key_forward) + " = " + key_forward);
                    writer.WriteLine(nameof(key_backward) + " = " + key_backward);
                    writer.WriteLine(nameof(key_strafeleft) + " = " + key_strafeleft);
                    writer.WriteLine(nameof(key_straferight) + " = " + key_straferight);
                    writer.WriteLine(nameof(key_turnleft) + " = " + key_turnleft);
                    writer.WriteLine(nameof(key_turnright) + " = " + key_turnright);
                    writer.WriteLine(nameof(key_fire) + " = " + key_fire);
                    writer.WriteLine(nameof(key_use) + " = " + key_use);
                    writer.WriteLine(nameof(key_run) + " = " + key_run);
                    writer.WriteLine(nameof(key_strafe) + " = " + key_strafe);
                writer.WriteLine(nameof(key_lookup) + " = " + key_lookup);
                writer.WriteLine(nameof(key_lookdown) + " = " + key_lookdown);
                writer.WriteLine(nameof(key_center) + " = " + key_center);

                    writer.WriteLine(nameof(mouse_sensitivity) + " = " + mouse_sensitivity);
                    writer.WriteLine(nameof(mouse_disableyaxis) + " = " + BoolToString(mouse_disableyaxis));

                    writer.WriteLine(nameof(game_alwaysrun) + " = " + BoolToString(game_alwaysrun));

                    writer.WriteLine(nameof(view_freelook) + " = " + BoolToString(view_freelook));
                    writer.WriteLine(nameof(hud_crosshair) + " = " + hud_crosshair);
                    writer.WriteLine(nameof(hud_crosshair_light) + " = " + hud_crosshair_light);
                    writer.WriteLine(nameof(hud_crosshair_dark) + " = " + hud_crosshair_dark);
                    writer.WriteLine(nameof(hud_profiler) + " = " + hud_profiler);

                    writer.WriteLine(nameof(video_screenwidth) + " = " + video_screenwidth);
                    writer.WriteLine(nameof(video_screenheight) + " = " + video_screenheight);
                    writer.WriteLine(nameof(video_fullscreen) + " = " + BoolToString(video_fullscreen));
                    writer.WriteLine(nameof(video_highresolution) + " = " + BoolToString(video_highresolution));
                    writer.WriteLine(nameof(video_displaymessage) + " = " + BoolToString(video_displaymessage));
                    writer.WriteLine(nameof(video_gamescreensize) + " = " + video_gamescreensize);
                    writer.WriteLine(nameof(video_gammacorrection) + " = " + video_gammacorrection);
                    writer.WriteLine(nameof(video_fpsscale) + " = " + video_fpsscale);

                    writer.WriteLine(nameof(audio_soundvolume) + " = " + audio_soundvolume);
                    writer.WriteLine(nameof(audio_musicvolume) + " = " + audio_musicvolume);
                    writer.WriteLine(nameof(audio_randompitch) + " = " + BoolToString(audio_randompitch));
                    writer.WriteLine(nameof(audio_soundfont) + " = " + audio_soundfont);
                    writer.WriteLine(nameof(audio_musiceffect) + " = " + BoolToString(audio_musiceffect));
                }
            }
            catch
            {
            }
        }

        private static int GetInt(Dictionary<string, string> dic, string name, int defaultValue)
        {
            string stringValue;
            if (dic.TryGetValue(name, out stringValue))
            {
                int value;
                if (int.TryParse(stringValue, out value))
                {
                    return value;
                }
            }

            return defaultValue;
        }

        private static string GetString(Dictionary<string, string> dic, string name, string defaultValue)
        {
            string stringValue;
            if (dic.TryGetValue(name, out stringValue))
            {
                return stringValue;
            }

            return defaultValue;
        }

        private static bool GetBool(Dictionary<string, string> dic, string name, bool defaultValue)
        {
            string stringValue;
            if (dic.TryGetValue(name, out stringValue))
            {
                if (stringValue == "true")
                {
                    return true;
                }
                else if (stringValue == "false")
                {
                    return false;
                }
            }

            return defaultValue;
        }

        private static KeyBinding GetKeyBinding(Dictionary<string, string> dic, string name, KeyBinding defaultValue)
        {
            string stringValue;
            if (dic.TryGetValue(name, out stringValue))
            {
                return KeyBinding.Parse(stringValue);
            }

            return defaultValue;
        }

        private static string BoolToString(bool value)
        {
            return value ? "true" : "false";
        }

        public bool IsRestoredFromFile => isRestoredFromFile;
    }
}
