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

namespace DPTDOOM.Win32
{
    /// <summary>
    /// Menu, tray and simple GDI text declarations. Kept in a separate file
    /// from the window basics so the P/Invoke surface stays readable.
    ///
    /// Every Unicode import names its W entry point explicitly: with
    /// CharSet.Unicode and ExactSpelling the runtime looks for the exact name,
    /// and Windows only exports the W and A variants.
    /// </summary>
    internal static partial class NativeMethods
    {
        public const int WM_COMMAND = 0x0111;
        public const int WM_INITMENUPOPUP = 0x0117;
        public const int WM_CONTEXTMENU = 0x007B;
        public const int WM_LBUTTONDBLCLK = 0x0203;
        public const int WM_APP = 0x8000;

        public const uint MF_STRING = 0x0000;
        public const uint MF_POPUP = 0x0010;
        public const uint MF_SEPARATOR = 0x0800;
        public const uint MF_CHECKED = 0x0008;
        public const uint MF_UNCHECKED = 0x0000;
        public const uint MF_GRAYED = 0x0001;
        public const uint MF_DISABLED = 0x0002;
        public const uint MF_ENABLED = 0x0000;
        public const uint MF_BYCOMMAND = 0x0000;
        public const uint MF_BYPOSITION = 0x0400;
        public const uint MF_RADIOCHECK = 0x0200;

        public const uint TPM_RIGHTBUTTON = 0x0002;
        public const uint TPM_LEFTALIGN = 0x0000;
        public const uint TPM_RETURNCMD = 0x0100;

        public const uint NIM_ADD = 0x00000000;
        public const uint NIM_MODIFY = 0x00000001;
        public const uint NIM_DELETE = 0x00000002;
        public const uint NIF_MESSAGE = 0x00000001;
        public const uint NIF_ICON = 0x00000002;
        public const uint NIF_TIP = 0x00000004;

        public const int TRANSPARENT = 1;

        public const int SIZE_RESTORED = 0;
        public const int SIZE_MINIMIZED = 1;

        public const int FW_NORMAL = 400;
        public const int FW_BOLD = 700;
        public const int DEFAULT_CHARSET = 1;

        [StructLayout(LayoutKind.Sequential)]
        public struct NOTIFYICONDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uID;
            public uint uFlags;
            public int uCallbackMessage;
            public IntPtr hIcon;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;

            public uint dwState;
            public uint dwStateMask;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;

            public uint uVersion;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;

            public uint dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [DllImport("user32.dll", ExactSpelling = true)]
        public static extern IntPtr CreateMenu();

        [DllImport("user32.dll", ExactSpelling = true)]
        public static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll", ExactSpelling = true)]
        public static extern bool DestroyMenu(IntPtr menu);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, EntryPoint = "AppendMenuW")]
        public static extern bool AppendMenu(IntPtr menu, uint flags, IntPtr item, string text);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, EntryPoint = "AppendMenuW")]
        public static extern bool AppendMenuId(IntPtr menu, uint flags, int item, string text);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, EntryPoint = "ModifyMenuW")]
        public static extern bool ModifyMenu(IntPtr menu, uint position, uint flags, IntPtr item, string text);

        [DllImport("user32.dll", ExactSpelling = true)]
        public static extern IntPtr GetMenu(IntPtr hWnd);

        [DllImport("user32.dll", ExactSpelling = true)]
        public static extern bool SetMenu(IntPtr hWnd, IntPtr menu);

        [DllImport("user32.dll", ExactSpelling = true)]
        public static extern bool DrawMenuBar(IntPtr hWnd);

        [DllImport("user32.dll", ExactSpelling = true)]
        public static extern int CheckMenuItem(IntPtr menu, int id, uint flags);

        [DllImport("user32.dll", ExactSpelling = true)]
        public static extern bool CheckMenuRadioItem(IntPtr menu, int first, int last, int check, uint flags);

        [DllImport("user32.dll", ExactSpelling = true)]
        public static extern bool EnableMenuItem(IntPtr menu, int id, uint flags);

        [DllImport("user32.dll", ExactSpelling = true)]
        public static extern int TrackPopupMenu(
            IntPtr menu, uint flags, int x, int y, int reserved, IntPtr hWnd, IntPtr rect);

        [DllImport("user32.dll", ExactSpelling = true)]
        public static extern bool GetCursorPos(out POINT point);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, EntryPoint = "PostMessageW")]
        public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, EntryPoint = "GetWindowLongW")]
        public static extern int GetWindowLong(IntPtr hWnd, int index);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, EntryPoint = "SetWindowLongW")]
        public static extern int SetWindowLong(IntPtr hWnd, int index, int value);

        [DllImport("user32.dll", ExactSpelling = true)]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", ExactSpelling = true)]
        public static extern bool IsIconic(IntPtr hWnd);

        public const int GWL_STYLE = -16;
        public const int GWL_EXSTYLE = -20;

        public static readonly IntPtr HWND_TOP = IntPtr.Zero;

        public const uint SWP_FRAMECHANGED = 0x0020;
        public const uint SWP_SHOWWINDOW = 0x0040;
        public const uint SWP_HIDEWINDOW = 0x0080;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, EntryPoint = "Shell_NotifyIconW")]
        public static extern bool Shell_NotifyIcon(uint message, ref NOTIFYICONDATA data);

        public const uint MB_OK = 0x00000000;
        public const uint MB_ICONINFORMATION = 0x00000040;
        public const uint MB_ICONWARNING = 0x00000030;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, EntryPoint = "MessageBoxW")]
        public static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

        [DllImport("gdi32.dll", ExactSpelling = true)]
        public static extern int SetTextColor(IntPtr dc, int color);

        [DllImport("gdi32.dll", ExactSpelling = true)]
        public static extern int SetBkMode(IntPtr dc, int mode);

        [DllImport("gdi32.dll", ExactSpelling = true)]
        public static extern IntPtr CreateSolidBrush(int color);

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, EntryPoint = "TextOutW")]
        public static extern bool TextOut(IntPtr dc, int x, int y, string text, int length);

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, EntryPoint = "CreateFontW")]
        public static extern IntPtr CreateFont(
            int height, int width, int escapement, int orientation, int weight,
            uint italic, uint underline, uint strikeOut, int charSet,
            int outputPrecision, int clipPrecision, int quality, int pitchAndFamily,
            string face);

        public static int Rgb(int r, int g, int b)
        {
            return (b << 16) | (g << 8) | r;
        }
    }
}
