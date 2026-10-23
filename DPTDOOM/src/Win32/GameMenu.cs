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

namespace DPTDOOM.Win32
{
    /// <summary>Everything the menu bar and the tray menu can ask for.</summary>
    internal enum MenuCommand
    {
        None = 0,

        GameNew = 1001,
        GameWarpNext,
        GameSave,
        GameLoad,
        GamePause,
        GameQuit,

        ViewFullscreen = 1101,
        ViewSize320,
        ViewSize640,
        ViewSize960,
        ViewSize1280,
        ViewGammaUp,
        ViewGammaDown,
        ViewMessages,
        ViewFps,

        AudioSfxDown = 1201,
        AudioSfxUp,
        AudioMusicDown,
        AudioMusicUp,
        AudioMuteSfx,
        AudioMuteMusic,

        DebugFpsLog = 1301,
        DebugSaveFrame,
        DebugDumpState,
        DebugSoundTest,
        DebugGod,
        DebugNoClip,
        DebugGiveAll,
        DebugWireframe,
        DebugMapOnly,
        DebugFreeze,
        DebugSlowMotion,
        DebugKillAll,
        DebugRevealMap,
        DebugWarpNext,
        DebugWarpPrev,
        DebugSpawnHealth,
        DebugSpawnArmor,
        DebugSpawnAmmo,

        HelpControls = 1401,
        HelpAbout,

        TrayShow = 1501,
        TrayHide,
        TrayQuit
    }

    /// <summary>
    /// The classic Windows menu bar, plus the tray menu.
    ///
    /// This exists because a Win32 window without a menu is a dead end for
    /// anything that is not a key binding: volumes, window size, gamma and the
    /// debug tools all have to live somewhere discoverable. A menu bar is the
    /// idiom of the era the game comes from, and it costs nothing at runtime —
    /// unlike, say, drawing our own in-game options screen.
    ///
    /// Check marks are tracked per command because CheckMenuItem only searches
    /// the menu it is given, not its submenus.
    /// </summary>
    internal sealed class GameMenu : IDisposable
    {
        private readonly IntPtr hWnd;

        private IntPtr bar;
        private IntPtr tray;
        private bool disposed;

        private readonly Dictionary<int, List<IntPtr>> owners = new Dictionary<int, List<IntPtr>>();

        public GameMenu(IntPtr hWnd)
        {
            this.hWnd = hWnd;
        }

        /// <summary>Builds the menu bar and attaches it to the window.</summary>
        public void Attach()
        {
            bar = NativeMethods.CreateMenu();
            if (bar == IntPtr.Zero)
            {
                return;
            }

            BuildGame();
            BuildView();
            BuildAudio();
            BuildDebug();
            BuildHelp();

            // Built now, not on first use: the command ids must be registered
            // before any WM_COMMAND arrives, and the tray can be shown before
            // the tray menu is ever opened.
            BuildTrayMenu();

            NativeMethods.SetMenu(hWnd, bar);
            NativeMethods.DrawMenuBar(hWnd);
        }

        private void BuildGame()
        {
            var popup = Popup("Игра");

            Item(popup, MenuCommand.GameNew, "Новая игра");
            Item(popup, MenuCommand.GameWarpNext, "Следующая карта");
            Separator(popup);
            Item(popup, MenuCommand.GameSave, "Сохранить...\tF2");
            Item(popup, MenuCommand.GameLoad, "Загрузить...\tF3");
            Separator(popup);
            Item(popup, MenuCommand.GamePause, "Пауза\tPause");
            Separator(popup);
            Item(popup, MenuCommand.GameQuit, "Выход\tF10");
        }

        private void BuildView()
        {
            var popup = Popup("Вид");

            Item(popup, MenuCommand.ViewFullscreen, "Полный экран");

            var sizes = NativeMethods.CreatePopupMenu();
            Item(sizes, MenuCommand.ViewSize320, "320 x 200");
            Item(sizes, MenuCommand.ViewSize640, "640 x 400");
            Item(sizes, MenuCommand.ViewSize960, "960 x 600");
            Item(sizes, MenuCommand.ViewSize1280, "1280 x 800");

            // Appended to this popup, not to the bar: a submenu belongs to the
            // menu it appears in.
            NativeMethods.AppendMenu(popup, NativeMethods.MF_POPUP | NativeMethods.MF_STRING,
                sizes, "Размер окна");

            Separator(popup);
            Item(popup, MenuCommand.ViewGammaUp, "Гамма ярче\tF11");
            Item(popup, MenuCommand.ViewGammaDown, "Гамма темнее");
            Separator(popup);
            Item(popup, MenuCommand.ViewMessages, "Сообщения\tF8");
            Item(popup, MenuCommand.ViewFps, "Счётчик кадров");
        }

        private void BuildAudio()
        {
            var popup = Popup("Звук");

            Item(popup, MenuCommand.AudioSfxUp, "Эффекты громче");
            Item(popup, MenuCommand.AudioSfxDown, "Эффекты тише");
            Separator(popup);
            Item(popup, MenuCommand.AudioMusicUp, "Музыка громче");
            Item(popup, MenuCommand.AudioMusicDown, "Музыка тише");
            Separator(popup);
            Item(popup, MenuCommand.AudioMuteSfx, "Без звуковых эффектов");
            Item(popup, MenuCommand.AudioMuteMusic, "Без музыки");
        }

        private void BuildDebug()
        {
            var popup = Popup("Отладка");

            Item(popup, MenuCommand.DebugFpsLog, "Лог кадров в файл");
            Item(popup, MenuCommand.DebugSaveFrame, "Сохранить кадр в BMP");
            Item(popup, MenuCommand.DebugDumpState, "Сбросить состояние мира в лог");
            Item(popup, MenuCommand.DebugSoundTest, "Отрендерить звуки в WAV");
            Separator(popup);

            Item(popup, MenuCommand.DebugWireframe, "Каркас геометрии");
            Item(popup, MenuCommand.DebugMapOnly, "Только карта");
            Item(popup, MenuCommand.DebugFreeze, "Заморозить монстров");
            Item(popup, MenuCommand.DebugSlowMotion, "Замедление x1");
            Separator(popup);

            Item(popup, MenuCommand.DebugGod, "Бог");
            Item(popup, MenuCommand.DebugNoClip, "Сквозь стены");
            Item(popup, MenuCommand.DebugGiveAll, "Всё оружие и патроны");
            Item(popup, MenuCommand.DebugKillAll, "Убить всех монстров");
            Item(popup, MenuCommand.DebugRevealMap, "Открыть карту");
            Separator(popup);

            Item(popup, MenuCommand.DebugSpawnHealth, "Бросить аптечку");
            Item(popup, MenuCommand.DebugSpawnArmor, "Бросить броню");
            Item(popup, MenuCommand.DebugSpawnAmmo, "Бросить патроны");
            Separator(popup);

            Item(popup, MenuCommand.DebugWarpNext, "Следующая карта");
            Item(popup, MenuCommand.DebugWarpPrev, "Предыдущая карта");
        }

        private void BuildHelp()
        {
            var popup = Popup("Справка");

            Item(popup, MenuCommand.HelpControls, "Управление\tF1");
            Item(popup, MenuCommand.HelpAbout, "О программе");
        }

        /// <summary>
        /// Builds the tray menu. Same command ids, so the handlers are shared
        /// with the menu bar and the check marks stay in sync.
        /// </summary>
        public void BuildTrayMenu()
        {
            if (tray != IntPtr.Zero)
            {
                return;
            }

            tray = NativeMethods.CreatePopupMenu();
            if (tray == IntPtr.Zero)
            {
                return;
            }

            Item(tray, MenuCommand.TrayShow, "Показать окно");
            Item(tray, MenuCommand.TrayHide, "Свернуть в трей");
            Separator(tray);
            Item(tray, MenuCommand.ViewFps, "Счётчик кадров");
            Item(tray, MenuCommand.DebugFpsLog, "Лог кадров в файл");
            Item(tray, MenuCommand.DebugSaveFrame, "Сохранить кадр в BMP");
            Separator(tray);
            Item(tray, MenuCommand.TrayQuit, "Выход");
        }

        public IntPtr TrayMenu
        {
            get { return tray; }
        }

        /// <summary>The menu bar handle, so it can be restored after a full-screen toggle.</summary>
        public IntPtr Handle
        {
            get { return bar; }
        }

        /// <summary>True when this command id belongs to us.</summary>
        public bool Handles(int id)
        {
            return owners.ContainsKey(id);
        }

        public void SetChecked(MenuCommand command, bool value)
        {
            List<IntPtr> popups;
            if (!owners.TryGetValue((int)command, out popups))
            {
                return;
            }

            var flags = NativeMethods.MF_BYCOMMAND |
                        (value ? NativeMethods.MF_CHECKED : NativeMethods.MF_UNCHECKED);

            foreach (var popup in popups)
            {
                NativeMethods.CheckMenuItem(popup, (int)command, flags);
            }
        }

        /// <summary>Marks one item of a radio group, clearing the others.</summary>
        public void SetRadio(MenuCommand first, MenuCommand last, MenuCommand selected)
        {
            List<IntPtr> popups;
            if (!owners.TryGetValue((int)first, out popups) || popups.Count == 0)
            {
                return;
            }

            NativeMethods.CheckMenuRadioItem(
                popups[0],
                (int)first,
                (int)last,
                (int)selected,
                NativeMethods.MF_BYCOMMAND);
        }

        /// <summary>Changes the text of an existing item, keeping its id.</summary>
        public void SetLabel(MenuCommand command, string text)
        {
            List<IntPtr> popups;
            if (!owners.TryGetValue((int)command, out popups))
            {
                return;
            }

            foreach (var popup in popups)
            {
                NativeMethods.ModifyMenu(
                    popup,
                    (uint)command,
                    NativeMethods.MF_BYCOMMAND | NativeMethods.MF_STRING,
                    new IntPtr((int)command),
                    text);
            }
        }

        public void SetEnabled(MenuCommand command, bool value)
        {
            List<IntPtr> popups;
            if (!owners.TryGetValue((int)command, out popups))
            {
                return;
            }

            var flags = NativeMethods.MF_BYCOMMAND |
                        (value ? NativeMethods.MF_ENABLED : NativeMethods.MF_GRAYED);

            foreach (var popup in popups)
            {
                NativeMethods.EnableMenuItem(popup, (int)command, flags);
            }
        }

        ////////////////////////////////////////////////////////////
        // Building blocks
        ////////////////////////////////////////////////////////////

        private IntPtr Popup(string title)
        {
            var popup = NativeMethods.CreatePopupMenu();
            NativeMethods.AppendMenu(bar, NativeMethods.MF_POPUP | NativeMethods.MF_STRING, popup, title);
            return popup;
        }

        private void Item(IntPtr popup, MenuCommand command, string text)
        {
            NativeMethods.AppendMenuId(popup, NativeMethods.MF_STRING, (int)command, text);

            List<IntPtr> list;
            if (!owners.TryGetValue((int)command, out list))
            {
                list = new List<IntPtr>();
                owners.Add((int)command, list);
            }

            if (!list.Contains(popup))
            {
                list.Add(popup);
            }
        }

        private static void Separator(IntPtr popup)
        {
            NativeMethods.AppendMenuId(popup, NativeMethods.MF_SEPARATOR, 0, null);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;

            if (tray != IntPtr.Zero)
            {
                NativeMethods.DestroyMenu(tray);
                tray = IntPtr.Zero;
            }

            if (bar != IntPtr.Zero)
            {
                NativeMethods.DestroyMenu(bar);
                bar = IntPtr.Zero;
            }
        }
    }
}
