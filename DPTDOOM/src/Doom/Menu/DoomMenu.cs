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

namespace DPTDOOM
{
    public sealed class DoomMenu
    {
        private Doom doom;

        private SelectableMenu main;
        private SelectableMenu episodeMenu;
        private SelectableMenu skillMenu;
        private TextMenu settings;
        private TextMenu settingsView;
        private TextMenu settingsAudio;
        private TextMenu settingsInput;
        private TextMenu settingsSystem;
        private SelectableMenu volume;
        private TextMenu about;
        private LoadMenu load;
        private SaveMenu save;
        private HelpScreen help;

        private PressAnyKey thisIsShareware;
        private PressAnyKey saveFailed;
        private YesNoConfirm nightmareConfirm;
        private YesNoConfirm endGameConfirm;
        private QuitConfirm quitConfirm;

        private MenuDef current;

        private bool active;

        private int tics;

        private int selectedEpisode;

        private SaveSlots saveSlots;

        public DoomMenu(Doom doom)
        {
            this.doom = doom;

            thisIsShareware = new PressAnyKey(
                this,
                DoomInfo.Strings.SWSTRING,
                null);

            saveFailed = new PressAnyKey(
                this,
                DoomInfo.Strings.SAVEDEAD,
                null);

            nightmareConfirm = new YesNoConfirm(
                this,
                DoomInfo.Strings.NIGHTMARE,
                () => doom.NewGame(GameSkill.Nightmare, selectedEpisode, 1));

            endGameConfirm = new YesNoConfirm(
                this,
                DoomInfo.Strings.ENDGAME,
                () => doom.EndGame());

            quitConfirm = new QuitConfirm(
                this,
                doom);

            skillMenu = new SelectableMenu(
                this,
                "M_NEWG", 96, 14,
                "M_SKILL", 54, 38,
                2,

                new SimpleMenuItem(
                    "M_JKILL", 16, 58, 48, 63,
                    () => doom.NewGame(GameSkill.Baby, selectedEpisode, 1),
                    null),

                new SimpleMenuItem(
                    "M_ROUGH", 16, 74, 48, 79,
                    () => doom.NewGame(GameSkill.Easy, selectedEpisode, 1),
                    null),

                new SimpleMenuItem(
                    "M_HURT", 16, 90, 48, 95,
                    () => doom.NewGame(GameSkill.Medium, selectedEpisode, 1),
                    null),

                new SimpleMenuItem(
                    "M_ULTRA", 16, 106, 48, 111,
                    () => doom.NewGame(GameSkill.Hard, selectedEpisode, 1),
                    null),

                new SimpleMenuItem(
                    "M_NMARE", 16, 122, 48, 127,
                    null,
                    nightmareConfirm));

            if (doom.Options.GameMode == GameMode.Retail)
            {
                episodeMenu = new SelectableMenu(
                    this,
                    "M_EPISOD", 54, 38,
                    0,

                    new SimpleMenuItem(
                        "M_EPI1", 16, 58, 48, 63,
                        () => selectedEpisode = 1,
                        skillMenu),

                    new SimpleMenuItem(
                        "M_EPI2", 16, 74, 48, 79,
                        () => selectedEpisode = 2,
                        skillMenu),

                    new SimpleMenuItem(
                        "M_EPI3", 16, 90, 48, 95,
                        () => selectedEpisode = 3,
                        skillMenu),

                    new SimpleMenuItem(
                        "M_EPI4", 16, 106, 48, 111,
                        () => selectedEpisode = 4,
                        skillMenu));
            }
            else
            {
                if (doom.Options.GameMode == GameMode.Shareware)
                {
                    episodeMenu = new SelectableMenu(
                        this,
                        "M_EPISOD", 54, 38,
                        0,

                        new SimpleMenuItem(
                            "M_EPI1", 16, 58, 48, 63,
                            () => selectedEpisode = 1,
                            skillMenu),

                        new SimpleMenuItem(
                            "M_EPI2", 16, 74, 48, 79,
                            null,
                            thisIsShareware),

                        new SimpleMenuItem(
                            "M_EPI3", 16, 90, 48, 95,
                            null,
                            thisIsShareware));
                }
                else
                {
                    episodeMenu = new SelectableMenu(
                        this,
                        "M_EPISOD", 54, 38,
                        0,

                        new SimpleMenuItem(
                            "M_EPI1", 16, 58, 48, 63,
                            () => selectedEpisode = 1,
                            skillMenu),
                        new SimpleMenuItem(
                            "M_EPI2", 16, 74, 48, 79,
                            () => selectedEpisode = 2,
                            skillMenu),
                        new SimpleMenuItem(
                            "M_EPI3", 16, 90, 48, 95,
                            () => selectedEpisode = 3,
                            skillMenu));
                }
            }

            var sound = doom.Options.Sound;
            var music = doom.Options.Music;
            volume = new SelectableMenu(
                this,
                "M_SVOL", 60, 38,
                0,

                new SliderMenuItem(
                    "M_SFXVOL", 48, 59, 80, 64,
                    sound.MaxVolume + 1,
                    () => sound.Volume,
                    vol => sound.Volume = vol),

                new SliderMenuItem("M_MUSVOL", 48, 91, 80, 96,
                    music.MaxVolume + 1,
                    () => music.Volume,
                    vol => music.Volume = vol));

            var video = doom.Options.Video;
            var userInput = doom.Options.UserInput;

            // Options leads straight into the settings screen, and the old
            // patch-based options menu is gone: everything it had is on these pages,
            // plus everything added since 1994. Two menus for the same settings was
            // the worst of both - a player had to remember which one held what.
            //
            // Paged because a single list of twenty rows does not fit on a 320x200
            // screen, and shrinking the font to make it fit would be worse than
            // scrolling a page.
            var config = doom.Config;

            var crosshairColours = MakeCrosshairColours(doom.Content.Wad);

            settingsView = new TextMenu(
                this,
                "VIEW",
                null,

                new ChoiceTextItem(
                    "CROSSHAIR",
                    new[] { "OFF", "CROSS", "DOT", "CROSS+DOT" },
                    () => config.hud_crosshair,
                    value => config.hud_crosshair = value,
                    "Mark, not aiming. Shots use Doom autoaim."),

                new ChoiceTextItem(
                    "CROSS COLOUR",
                    crosshairColours.Names,
                    () => crosshairColours.IndexOfLight(config.hud_crosshair_light),
                    value => config.hud_crosshair_light = crosshairColours.LightAt(value),
                    "The mark itself."),

                new ChoiceTextItem(
                    "CROSS SHADOW",
                    crosshairColours.Names,
                    () => crosshairColours.IndexOfDark(config.hud_crosshair_dark),
                    value => config.hud_crosshair_dark = crosshairColours.DarkAt(value),
                    "The dark edge that keeps it visible."),

                new ToggleTextItem(
                    "FREE LOOK",
                    () => config.view_freelook,
                    value => config.view_freelook = value,
                    "Mouse tilts the view. No aiming change."),

                new ToggleTextItem(
                    "MESSAGES",
                    () => video.DisplayMessage,
                    value => video.DisplayMessage = value,
                    "Pickup text in the message line."),

                new SliderTextItem(
                    "SCREEN SIZE", 0, video.MaxWindowSize,
                    () => video.WindowSize,
                    value => video.WindowSize = value,
                    "View size. Last step is full screen."),

                new SliderTextItem(
                    "GAMMA", 0, video.MaxGammaCorrectionLevel,
                    () => video.GammaCorrectionLevel,
                    value => video.GammaCorrectionLevel = value,
                    "Brightness. Raise if too dark."));

            settingsAudio = new TextMenu(
                this,
                "AUDIO",
                null,

                new SliderTextItem(
                    "SOUND VOLUME", 0, sound.MaxVolume,
                    () => sound.Volume,
                    value => sound.Volume = value,
                    "Effects: shots, doors, monsters.", true),

                new SliderTextItem(
                    "MUSIC VOLUME", 0, music.MaxVolume,
                    () => music.Volume,
                    value => music.Volume = value,
                    "Background music.", true));

            settingsInput = new TextMenu(
                this,
                "INPUT",
                null,

                new SliderTextItem(
                    "MOUSE SPEED", 1, userInput.MaxMouseSensitivity,
                    () => userInput.MouseSensitivity,
                    value => userInput.MouseSensitivity = value,
                    "Mouse turning speed.", true),

                new ToggleTextItem(
                    "ALWAYS RUN",
                    () => config.game_alwaysrun,
                    value => config.game_alwaysrun = value,
                    "Run by default. Shift walks."));

            settingsSystem = new TextMenu(
                this,
                "SYSTEM",
                null,

                new ChoiceTextItem(
                    "PROFILER",
                    DPTDOOM.Video.ProfilerLevel.ShortNames,
                    () => config.hud_profiler,
                    value => config.hud_profiler = value,
                    "How much the counter in the corner shows."),

                new ToggleTextItem(
                    "FRAME PROFILE",
                    () => video.Profile != null && video.Profile.Enabled,
                    value =>
                    {
                        if (video.Profile != null)
                        {
                            video.Profile.Enabled = value;
                        }
                    },
                    "Frame sections, reported on exit."),

                new InfoTextItem(
                    "VERSION",
                    () => ApplicationInfo.Title,
                    "The build you are running."),

                new ActionTextItem(
                    "END GAME",
                    () => doom.EndGame(),
                    "Leave the level and go back to the title.",
                    () => doom.State == DoomState.Game));

            // The root page: navigation only. Keeping it to five rows means it never
            // scrolls, so a player always sees where they can go.
            settings = new TextMenu(
                this,
                "OPTIONS",
                null,

                new ActionTextItem("VIEW", () => ShowPage(settingsView),
                    "Crosshair, free look, messages, screen, gamma."),

                new ActionTextItem("AUDIO", () => ShowPage(settingsAudio),
                    "Sound and music volume."),

                new ActionTextItem("INPUT", () => ShowPage(settingsInput),
                    "Mouse speed and always-run."),

                new ActionTextItem("SYSTEM", () => ShowPage(settingsSystem),
                    "Profiler, frame profile, version, end game."),

                new ActionTextItem("BACK", () => Close(),
                    "Return to the main menu."));

            // About, as a screen rather than a Windows message box. A MessageBox is in
            // the system font with a system icon and belongs to the operating system;
            // this is drawn by the renderer with Doom's own font, so it looks like the
            // program it is describing and can be photographed by the same tooling as
            // every other screen.
            about = new TextMenu(
                this,
                "ABOUT",
                null,

                new InfoTextItem("PROGRAM", () => ApplicationInfo.Title,
                    "DPT.DOOM, a source port of Doom."),

                new InfoTextItem("ENGINE", () => "GNU GPL v2 or later",
                    "The source is free software; game data is not."),

                new InfoTextItem("BASED ON", () => "MANAGED DOOM",
                    "Nobuaki Tanaka's C# translation of Linux Doom."),

                new InfoTextItem("ORIGINAL", () => "ID SOFTWARE",
                    "DOOM belongs to id Software. No game data is included."),

                new InfoTextItem("ABOUTBOT", () => "COMING LATER",
                    "The bot's documentation will live in refs/ and be summarised here."),

                new ActionTextItem("BACK", () => Close(),
                    "Return to the game."));

            // The pages return to the root, not to the main menu: a player who
            // pressed Escape to leave a page wanted the list of pages back.
            settingsView.Parent = settings;
            settingsAudio.Parent = settings;
            settingsInput.Parent = settings;
            settingsSystem.Parent = settings;

            load = new LoadMenu(
                this,
                "M_LOADG", 72, 28,
                0,
                new TextBoxMenuItem(48, 49, 72, 61),
                new TextBoxMenuItem(48, 65, 72, 77),
                new TextBoxMenuItem(48, 81, 72, 93),
                new TextBoxMenuItem(48, 97, 72, 109),
                new TextBoxMenuItem(48, 113, 72, 125),
                new TextBoxMenuItem(48, 129, 72, 141));

            save = new SaveMenu(
                this,
                "M_SAVEG", 72, 28,
                0,
                new TextBoxMenuItem(48, 49, 72, 61),
                new TextBoxMenuItem(48, 65, 72, 77),
                new TextBoxMenuItem(48, 81, 72, 93),
                new TextBoxMenuItem(48, 97, 72, 109),
                new TextBoxMenuItem(48, 113, 72, 125),
                new TextBoxMenuItem(48, 129, 72, 141));

            help = new HelpScreen(this);

            if (doom.Options.GameMode == GameMode.Commercial)
            {
                main = new SelectableMenu(
                this,
                "M_DOOM", 94, 2,
                0,
                new SimpleMenuItem("M_NGAME", 65, 67, 97, 72, null, skillMenu),
                new SimpleMenuItem("M_OPTION", 65, 83, 97, 88, null, settings),
                new SimpleMenuItem("M_LOADG", 65, 99, 97, 104, null, load),
                new SimpleMenuItem("M_SAVEG", 65, 115, 97, 120, null, save,
                    () => !(doom.State == DoomState.Game &&
                        doom.Game.State != GameState.Level)),
                new SimpleMenuItem("M_QUITG", 65, 131, 97, 136, null, quitConfirm));
            }
            else
            {
                main = new SelectableMenu(
                this,
                "M_DOOM", 94, 2,
                0,
                new SimpleMenuItem("M_NGAME", 65, 59, 97, 64, null, episodeMenu),
                new SimpleMenuItem("M_OPTION", 65, 75, 97, 80, null, settings),
                new SimpleMenuItem("M_LOADG", 65, 91, 97, 96, null, load),
                new SimpleMenuItem("M_SAVEG", 65, 107, 97, 112, null, save,
                    () => !(doom.State == DoomState.Game &&
                        doom.Game.State != GameState.Level)),
                new SimpleMenuItem("M_RDTHIS", 65, 123, 97, 128, null, help),
                new SimpleMenuItem("M_QUITG", 65, 139, 97, 144, null, quitConfirm));
            }

            current = main;
            active = false;

            tics = 0;

            selectedEpisode = 1;

            saveSlots = new SaveSlots();
        }

        public bool DoEvent(DoomEvent e)
        {
            if (active)
            {
                if (current.DoEvent(e))
                {
                    return true;
                }

                if (e.Key == DoomKey.Escape && e.Type == EventType.KeyDown)
                {
                    Close();
                }

                return true;
            }
            else
            {
                if (e.Key == DoomKey.Escape && e.Type == EventType.KeyDown)
                {
                    SetCurrent(main);
                    Open();
                    StartSound(Sfx.SWTCHN);
                    return true;
                }

                if (e.Type == EventType.KeyDown && doom.State == DoomState.Opening)
                {
                    if (e.Key == DoomKey.Enter ||
                        e.Key == DoomKey.Space ||
                        e.Key == DoomKey.LControl ||
                        e.Key == DoomKey.RControl ||
                        e.Key == DoomKey.Escape)
                    {
                        SetCurrent(main);
                        Open();
                        StartSound(Sfx.SWTCHN);
                        return true;
                    }
                }

                return false;
            }
        }

        public void Update()
        {
            tics++;

            if (current != null)
            {
                current.Update();
            }

            if (active && !doom.Options.NetGame)
            {
                doom.PauseGame();
            }
        }

        public void SetCurrent(MenuDef next)
        {
            current = next;
            current.Open();
        }

        public void Open()
        {
            active = true;
        }

        public void Close()
        {
            active = false;

            if (!doom.Options.NetGame)
            {
                doom.ResumeGame();
            }
        }

        public void StartSound(Sfx sfx)
        {
            doom.Options.Sound.StartSound(sfx);
        }

        public void NotifySaveFailed()
        {
            SetCurrent(saveFailed);
        }

        public void ShowHelpScreen()
        {
            SetCurrent(help);
            Open();
            StartSound(Sfx.SWTCHN);
        }

        public void ShowSaveScreen()
        {
            SetCurrent(save);
            Open();
            StartSound(Sfx.SWTCHN);
        }

        public void ShowLoadScreen()
        {
            SetCurrent(load);
            Open();
            StartSound(Sfx.SWTCHN);
        }

        public void ShowVolumeControl()
        {
            SetCurrent(volume);
            Open();
            StartSound(Sfx.SWTCHN);
        }

        public void QuickSave()
        {
            if (save.LastSaveSlot == -1)
            {
                ShowSaveScreen();
            }
            else
            {
                var desc = saveSlots[save.LastSaveSlot];
                var confirm = new YesNoConfirm(
                    this,
                    ((string)DoomInfo.Strings.QSPROMPT).Replace("%s", desc),
                    () => save.DoSave(save.LastSaveSlot));
                SetCurrent(confirm);
                Open();
                StartSound(Sfx.SWTCHN);
            }
        }

        public void QuickLoad()
        {
            if (save.LastSaveSlot == -1)
            {
                var pak = new PressAnyKey(
                    this,
                    DoomInfo.Strings.QSAVESPOT,
                    null);
                SetCurrent(pak);
                Open();
                StartSound(Sfx.SWTCHN);
            }
            else
            {
                var desc = saveSlots[save.LastSaveSlot];
                var confirm = new YesNoConfirm(
                    this,
                    ((string)DoomInfo.Strings.QLPROMPT).Replace("%s", desc),
                    () => load.DoLoad(save.LastSaveSlot));
                SetCurrent(confirm);
                Open();
                StartSound(Sfx.SWTCHN);
            }
        }

        public void EndGame()
        {
            SetCurrent(endGameConfirm);
            Open();
            StartSound(Sfx.SWTCHN);
        }

        public void Quit()
        {
            SetCurrent(quitConfirm);
            Open();
            StartSound(Sfx.SWTCHN);
        }

        /// <summary>Opens the settings screen, from the menu or from the Windows menu.</summary>
        public void ShowSettings()
        {
            SetCurrent(settings);
            Open();
            StartSound(Sfx.SWTCHN);
        }

        /// <summary>
        /// Opens one settings page and points it back at the root.
        ///
        /// The parent is assigned here rather than at construction because the pages
        /// are built before the root exists, and a page that could not find its way
        /// back would strand the player with a menu Escape cannot leave.
        /// </summary>
        private void ShowPage(TextMenu page)
        {
            page.Parent = settings;

            SetCurrent(page);
            Open();
            StartSound(Sfx.SWTCHN);
        }

        /// <summary>
        /// The crosshair colour presets.
        ///
        /// Named colours rather than a raw palette index: 256 values on a slider is
        /// not a colour picker, it is a lottery, and the index means a different
        /// colour in every IWAD. Each preset is resolved to the nearest palette entry
        /// once, from this IWAD's own PLAYPAL.
        /// </summary>
        private sealed class CrosshairColours
        {
            private readonly byte[] playpal;

            public readonly string[] Names =
            {
                "DEFAULT", "WHITE", "RED", "GREEN", "BLUE",
                "YELLOW", "CYAN", "MAGENTA", "BRAND"
            };

            private static readonly int[][] Rgb =
            {
                null,                        // DEFAULT: nearest white / nearest black
                new[] { 255, 255, 255 },
                new[] { 235, 60, 50 },
                new[] { 90, 230, 90 },
                new[] { 80, 150, 255 },
                new[] { 250, 230, 90 },
                new[] { 110, 230, 240 },
                new[] { 240, 110, 240 },
                new[] { 0, 136, 255 }
            };

            public CrosshairColours(byte[] playpal)
            {
                this.playpal = playpal;
            }

            public int LightAt(int index)
            {
                return Resolve(index, 0);
            }

            public int DarkAt(int index)
            {
                // "DEFAULT" is the only entry that differs between the two roles: the
                // light one becomes white and the dark one becomes black. Every named
                // colour is used for both, and a dark shade of it is picked instead.
                return Resolve(index, 1);
            }

            private int Resolve(int index, int role)
            {
                if (index <= 0 || index >= Rgb.Length)
                {
                    return -1;
                }

                var rgb = Rgb[index];

                if (role == 0)
                {
                    return DPTDOOM.Video.PaletteLookup.Nearest(playpal, rgb[0], rgb[1], rgb[2]);
                }

                // A quarter of the brightness, so a red cross has a dark red shadow
                // rather than a second bright red one behind it.
                return DPTDOOM.Video.PaletteLookup.Nearest(playpal, rgb[0] / 4, rgb[1] / 4, rgb[2] / 4);
            }

            public int IndexOfLight(int paletteIndex)
            {
                return IndexOf(paletteIndex, 0);
            }

            public int IndexOfDark(int paletteIndex)
            {
                return IndexOf(paletteIndex, 1);
            }

            private int IndexOf(int paletteIndex, int role)
            {
                for (var i = 1; i < Rgb.Length; i++)
                {
                    if (Resolve(i, role) == paletteIndex)
                    {
                        return i;
                    }
                }

                return 0;
            }
        }

        private CrosshairColours MakeCrosshairColours(Wad wad)
        {
            return new CrosshairColours(wad.ReadLump("PLAYPAL"));
        }

        /// <summary>Opens the about screen, from the Windows menu or from a command line flag.</summary>
        public void ShowAbout()
        {
            SetCurrent(about);
            Open();
            StartSound(Sfx.SWTCHN);
        }

        public Doom Doom => doom;
        public GameOptions Options => doom.Options;
        public MenuDef Current => current;
        public bool Active => active;
        public int Tics => tics;
        public SaveSlots SaveSlots => saveSlots;
        public TextMenu Settings => settings;
    }
}
