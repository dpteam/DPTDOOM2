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
    /// The complete Win32 surface used by DPT.DOOM. Everything here exists on
    /// Windows XP, which is why the port can stay free of third-party runtime
    /// libraries.
    /// </summary>
    internal static class NativeMethods
    {
        public const int WM_NULL = 0x0000;
        public const int WM_DESTROY = 0x0002;
        public const int WM_SIZE = 0x0005;
        public const int WM_ACTIVATE = 0x0006;
        public const int WM_SETFOCUS = 0x0007;
        public const int WM_KILLFOCUS = 0x0008;
        public const int WM_CLOSE = 0x0010;
        public const int WM_QUIT = 0x0012;
        public const int WM_ERASEBKGND = 0x0014;
        public const int WM_SYSCOMMAND = 0x0112;
        public const int WM_KEYDOWN = 0x0100;
        public const int WM_KEYUP = 0x0101;
        public const int WM_SYSKEYDOWN = 0x0104;
        public const int WM_SYSKEYUP = 0x0105;
        public const int WM_MOUSEMOVE = 0x0200;
        public const int WM_LBUTTONDOWN = 0x0201;
        public const int WM_LBUTTONUP = 0x0202;
        public const int WM_RBUTTONDOWN = 0x0204;
        public const int WM_RBUTTONUP = 0x0205;
        public const int WM_MBUTTONDOWN = 0x0207;
        public const int WM_MBUTTONUP = 0x0208;
        public const int WM_MOUSEWHEEL = 0x020A;
        public const int WM_ACTIVATEAPP = 0x001C;
        public const int WM_PAINT = 0x000F;
        public const int WM_DISPLAYCHANGE = 0x007E;

        public const int SC_KEYMENU = 0xF100;

        public const uint PM_NOREMOVE = 0x0000;
        public const uint PM_REMOVE = 0x0001;

        public const int SW_SHOWNORMAL = 1;
        public const int SW_SHOW = 5;

        public const int SM_CXSCREEN = 0;
        public const int SM_CYSCREEN = 1;

        public const uint WS_OVERLAPPED = 0x00000000;
        public const uint WS_POPUP = 0x80000000;
        public const uint WS_VISIBLE = 0x10000000;
        public const uint WS_CAPTION = 0x00C00000;
        public const uint WS_SYSMENU = 0x00080000;
        public const uint WS_THICKFRAME = 0x00040000;
        public const uint WS_MINIMIZEBOX = 0x00020000;
        public const uint WS_MAXIMIZEBOX = 0x00010000;
        public const uint WS_OVERLAPPEDWINDOW =
            WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX;
        public const uint WS_CLIPSIBLINGS = 0x04000000;
        public const uint WS_CLIPCHILDREN = 0x02000000;

        public const uint WS_EX_APPWINDOW = 0x00040000;

        public const uint CS_HREDRAW = 0x0002;
        public const uint CS_VREDRAW = 0x0001;
        public const uint CS_OWNDC = 0x0020;

        public const int IDC_ARROW = 32512;

        public const uint BI_RGB = 0;
        public const uint BI_BITFIELDS = 3;
        public const uint DIB_RGB_COLORS = 0;
        public const int SRCCOPY = 0x00CC0020;

        public const int VK_BACK = 0x08;
        public const int VK_TAB = 0x09;
        public const int VK_RETURN = 0x0D;
        public const int VK_SHIFT = 0x10;
        public const int VK_CONTROL = 0x11;
        public const int VK_MENU = 0x12;
        public const int VK_PAUSE = 0x13;
        public const int VK_CAPITAL = 0x14;
        public const int VK_ESCAPE = 0x1B;
        public const int VK_SPACE = 0x20;
        public const int VK_PRIOR = 0x21;
        public const int VK_NEXT = 0x22;
        public const int VK_END = 0x23;
        public const int VK_HOME = 0x24;
        public const int VK_LEFT = 0x25;
        public const int VK_UP = 0x26;
        public const int VK_RIGHT = 0x27;
        public const int VK_DOWN = 0x28;
        public const int VK_INSERT = 0x2D;
        public const int VK_DELETE = 0x2E;
        public const int VK_LWIN = 0x5B;
        public const int VK_RWIN = 0x5C;
        public const int VK_NUMPAD0 = 0x60;
        public const int VK_NUMPAD9 = 0x69;
        public const int VK_MULTIPLY = 0x6A;
        public const int VK_ADD = 0x6B;
        public const int VK_SUBTRACT = 0x6D;
        public const int VK_DIVIDE = 0x6F;
        public const int VK_F1 = 0x70;
        public const int VK_F15 = 0x7E;
        public const int VK_LSHIFT = 0xA0;
        public const int VK_RSHIFT = 0xA1;
        public const int VK_LCONTROL = 0xA2;
        public const int VK_RCONTROL = 0xA3;
        public const int VK_LMENU = 0xA4;
        public const int VK_RMENU = 0xA5;
        public const int VK_OEM_1 = 0xBA;
        public const int VK_OEM_PLUS = 0xBB;
        public const int VK_OEM_COMMA = 0xBC;
        public const int VK_OEM_MINUS = 0xBD;
        public const int VK_OEM_PERIOD = 0xBE;
        public const int VK_OEM_2 = 0xBF;
        public const int VK_OEM_3 = 0xC0;
        public const int VK_OEM_4 = 0xDB;
        public const int VK_OEM_5 = 0xDC;
        public const int VK_OEM_6 = 0xDD;
        public const int VK_OEM_7 = 0xDE;

        public delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WNDCLASSEX
        {
            public uint cbSize;
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string lpszMenuName;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string lpszClassName;
            public IntPtr hIconSm;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public int ptX;
            public int ptY;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth;
            public int biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public uint biCompression;
            public uint biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public uint biClrUsed;
            public uint biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFO
        {
            public BITMAPINFOHEADER bmiHeader;
            public uint redMask;
            public uint greenMask;
            public uint blueMask;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true, EntryPoint = "RegisterClassExW")]
        public static extern ushort RegisterClassEx(ref WNDCLASSEX wndClass);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true, EntryPoint = "CreateWindowExW")]
        public static extern IntPtr CreateWindowEx(
            uint exStyle,
            string className,
            string windowName,
            uint style,
            int x,
            int y,
            int width,
            int height,
            IntPtr parent,
            IntPtr menu,
            IntPtr instance,
            IntPtr param);

        /// <summary>
        /// Must be the Unicode variant: DefWindowProc handles window messages
        /// that carry strings (WM_SETTEXT, WM_GETTEXT), and the ANSI variant
        /// would mangle them.
        /// </summary>
        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, EntryPoint = "DefWindowProcW")]
        public static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int cmdShow);

        [DllImport("user32.dll")]
        public static extern bool UpdateWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ValidateRect(IntPtr hWnd, IntPtr rect);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, EntryPoint = "PeekMessageW")]
        public static extern bool PeekMessage(out MSG msg, IntPtr hWnd, uint filterMin, uint filterMax, uint removeMsg);

        [DllImport("user32.dll")]
        public static extern bool TranslateMessage(ref MSG msg);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, EntryPoint = "DispatchMessageW")]
        public static extern IntPtr DispatchMessage(ref MSG msg);

        [DllImport("user32.dll")]
        public static extern void PostQuitMessage(int exitCode);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true, EntryPoint = "GetModuleHandleW")]
        public static extern IntPtr GetModuleHandle(string moduleName);

        [DllImport("user32.dll")]
        public static extern IntPtr LoadCursor(IntPtr instance, IntPtr cursorName);

        [DllImport("user32.dll")]
        public static extern IntPtr SetCursor(IntPtr cursor);

        [DllImport("user32.dll")]
        public static extern int ShowCursor(bool show);

        [DllImport("user32.dll")]
        public static extern bool ClipCursor(IntPtr rect);

        [DllImport("user32.dll")]
        public static extern bool ClipCursor(ref RECT rect);

        [DllImport("user32.dll")]
        public static extern bool GetClientRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        public static extern bool ClientToScreen(IntPtr hWnd, ref POINT point);

        [DllImport("user32.dll")]
        public static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        public static extern bool AdjustWindowRectEx(ref RECT rect, uint style, bool menu, uint exStyle);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(
            IntPtr hWnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr SetFocus(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int index);

        [DllImport("user32.dll")]
        public static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr hWnd, IntPtr dc);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, EntryPoint = "SetWindowTextW")]
        public static extern bool SetWindowText(IntPtr hWnd, string text);

        [DllImport("user32.dll")]
        public static extern short GetKeyState(int virtualKey);

        [DllImport("gdi32.dll")]
        public static extern int StretchDIBits(
            IntPtr dc,
            int xDest,
            int yDest,
            int destWidth,
            int destHeight,
            int xSrc,
            int ySrc,
            int srcWidth,
            int srcHeight,
            IntPtr bits,
            ref BITMAPINFO bitmapInfo,
            uint usage,
            uint rasterOperation);

        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        [DllImport("winmm.dll")]
        public static extern uint timeBeginPeriod(uint period);

        [DllImport("winmm.dll")]
        public static extern uint timeEndPeriod(uint period);
    }
}
