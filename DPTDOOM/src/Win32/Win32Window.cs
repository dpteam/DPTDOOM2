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
    /// A minimal Win32 window with a DIB blitter and direct keyboard/mouse
    /// state. No WinForms, no OpenGL, no external DLLs: just user32 and gdi32,
    /// both present on every supported Windows version.
    /// </summary>
    internal sealed class Win32Window : IDisposable
    {
        private const string WindowClassName = "DPTDOOMWindow";

        private readonly string title;
        private readonly int requestedWidth;
        private readonly int requestedHeight;
        private readonly bool fullscreen;

        private readonly bool[] keyPressed;
        private readonly bool[] mousePressed;

        private NativeMethods.WndProc wndProcDelegate;
        private IntPtr windowIcon;
        private IntPtr hWnd;
        private IntPtr hInstance;

        private int clientWidth;
        private int clientHeight;

        private bool quit;
        private bool disposed;
        private bool focused;
        private bool mouseGrabbed;
        private bool recenteringMouse;

        private int mouseDeltaX;
        private int mouseDeltaY;

        private IntPtr pixels;
        private int pixelWidth;
        private int pixelHeight;
        private bool hasFrame;

        private NativeMethods.BITMAPINFO bitmapInfo;

        private IntPtr memoryDc;
        private IntPtr surface;
        private IntPtr surfaceBits;
        private IntPtr previousObject;
        private int surfaceWidth;
        private int surfaceHeight;

        private GameMenu menu;
        private bool fullscreenActive;
        private NativeMethods.RECT windowedRect;

        private bool trayAdded;
        private NativeMethods.NOTIFYICONDATA trayData;

        private IntPtr overlayFont;

        public event Action<MenuCommand> Command;

        /// <summary>Shows a small text overlay in the top-left corner.</summary>
        public bool ShowOverlay { get; set; }

        public string OverlayText { get; set; }

        /// <summary>
        /// When set, minimising hides the window and leaves the tray icon, the
        /// way Windows 3.x applications lived in the background.
        /// </summary>
        public bool MinimizeToTray { get; set; }

        public Win32Window(string title, int width, int height, bool fullscreen)
        {
            this.title = title;
            this.requestedWidth = width;
            this.requestedHeight = height;
            this.fullscreen = fullscreen;

            keyPressed = new bool[(int)DoomKey.Count];
            mousePressed = new bool[(int)DoomMouseButton.Count];

            bitmapInfo = new NativeMethods.BITMAPINFO();
            bitmapInfo.bmiHeader.biSize = (uint)Marshal.SizeOf(typeof(NativeMethods.BITMAPINFOHEADER));
            bitmapInfo.bmiHeader.biPlanes = 1;
            bitmapInfo.bmiHeader.biBitCount = 32;
            bitmapInfo.bmiHeader.biCompression = NativeMethods.BI_BITFIELDS;
            bitmapInfo.redMask = 0x000000FF;
            bitmapInfo.greenMask = 0x0000FF00;
            bitmapInfo.blueMask = 0x00FF0000;
        }

        public void Create()
        {
            try
            {
                NativeMethods.SetProcessDPIAware();
            }
            catch (EntryPointNotFoundException)
            {
                // Windows XP: DPI awareness did not exist yet, and is not needed.
            }

            hInstance = NativeMethods.GetModuleHandle(null);

            wndProcDelegate = WndProc;

            var wndClass = new NativeMethods.WNDCLASSEX();
            wndClass.cbSize = (uint)Marshal.SizeOf(typeof(NativeMethods.WNDCLASSEX));
            wndClass.style = NativeMethods.CS_HREDRAW | NativeMethods.CS_VREDRAW | NativeMethods.CS_OWNDC;
            wndClass.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(wndProcDelegate);
            wndClass.hInstance = hInstance;
            wndClass.hCursor = NativeMethods.LoadCursor(IntPtr.Zero, new IntPtr(NativeMethods.IDC_ARROW));
            wndClass.lpszClassName = WindowClassName;

            // The exe carries the DPT.DOOM icon (ApplicationIcon in the project
            // file), but a window class does not pick it up on its own: without
            // these two fields the title bar and the taskbar show the generic
            // application icon.
            windowIcon = LoadWindowIcon();
            wndClass.hIcon = windowIcon;
            wndClass.hIconSm = windowIcon;

            if (NativeMethods.RegisterClassEx(ref wndClass) == 0)
            {
                var error = Marshal.GetLastWin32Error();
                if (error != 1410) // ERROR_CLASS_ALREADY_EXISTS
                {
                    throw new Exception("RegisterClassEx failed: " + error);
                }
            }

            uint style;
            uint exStyle;
            int x;
            int y;
            int width;
            int height;

            if (fullscreen)
            {
                style = NativeMethods.WS_POPUP | NativeMethods.WS_VISIBLE;
                exStyle = NativeMethods.WS_EX_APPWINDOW;
                x = 0;
                y = 0;
                width = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN);
                height = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN);
            }
            else
            {
                style = NativeMethods.WS_OVERLAPPEDWINDOW | NativeMethods.WS_CLIPCHILDREN | NativeMethods.WS_CLIPSIBLINGS;
                exStyle = 0;
                x = 64;
                y = 64;
                width = requestedWidth;
                height = requestedHeight;

                var rect = new NativeMethods.RECT();
                rect.right = width;
                rect.bottom = height;
                NativeMethods.AdjustWindowRectEx(ref rect, style, false, exStyle);
                width = rect.right - rect.left;
                height = rect.bottom - rect.top;
            }

            hWnd = NativeMethods.CreateWindowEx(
                exStyle,
                WindowClassName,
                title,
                style,
                x,
                y,
                width,
                height,
                IntPtr.Zero,
                IntPtr.Zero,
                hInstance,
                IntPtr.Zero);

            if (hWnd == IntPtr.Zero)
            {
                throw new Exception("CreateWindowEx failed: " + Marshal.GetLastWin32Error());
            }

            // The menu bar is part of the window frame, so it must exist before
            // the client area is measured; otherwise the game is squeezed by
            // the height of the menu.
            if (!fullscreen)
            {
                menu = new GameMenu(hWnd);
                menu.Attach();
            }

            ApplyWindowIcon();
            FitClientArea();

            NativeMethods.ShowWindow(hWnd, NativeMethods.SW_SHOW);
            NativeMethods.UpdateWindow(hWnd);
            NativeMethods.SetForegroundWindow(hWnd);
            NativeMethods.SetFocus(hWnd);
        }

        /// <summary>
        /// Resizes the window so the client area is exactly the framebuffer
        /// size. Without this every frame goes through GDI's generic stretch
        /// path, which is several times slower than a plain blit.
        /// </summary>
        private void FitClientArea()
        {
            var style = (uint)NativeMethods.GetWindowLong(hWnd, NativeMethods.GWL_STYLE);
            var exStyle = (uint)NativeMethods.GetWindowLong(hWnd, NativeMethods.GWL_EXSTYLE);

            var frame = new NativeMethods.RECT();
            frame.right = requestedWidth;
            frame.bottom = requestedHeight;

            var hasMenu = NativeMethods.GetMenu(hWnd) != IntPtr.Zero;
            NativeMethods.AdjustWindowRectEx(ref frame, style, hasMenu, exStyle);

            NativeMethods.SetWindowPos(
                hWnd,
                IntPtr.Zero,
                0,
                0,
                frame.right - frame.left,
                frame.bottom - frame.top,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOZORDER);

            NativeMethods.RECT client;
            if (NativeMethods.GetClientRect(hWnd, out client))
            {
                clientWidth = client.right - client.left;
                clientHeight = client.bottom - client.top;
            }
        }

        /// <summary>
        /// Changes the window size. The framebuffer keeps its own resolution and
        /// GDI scales it, which is what makes this cheap enough to put in a menu.
        /// </summary>
        public void SetClientSize(int width, int height)
        {
            if (hWnd == IntPtr.Zero || fullscreen)
            {
                return;
            }

            var style = (uint)NativeMethods.GetWindowLong(hWnd, NativeMethods.GWL_STYLE);
            var exStyle = (uint)NativeMethods.GetWindowLong(hWnd, NativeMethods.GWL_EXSTYLE);

            var frame = new NativeMethods.RECT();
            frame.right = width;
            frame.bottom = height;

            var hasMenu = NativeMethods.GetMenu(hWnd) != IntPtr.Zero;
            NativeMethods.AdjustWindowRectEx(ref frame, style, hasMenu, exStyle);

            NativeMethods.SetWindowPos(
                hWnd,
                IntPtr.Zero,
                0,
                0,
                frame.right - frame.left,
                frame.bottom - frame.top,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOZORDER);

            NativeMethods.RECT client;
            if (NativeMethods.GetClientRect(hWnd, out client))
            {
                clientWidth = client.right - client.left;
                clientHeight = client.bottom - client.top;
            }
        }

        /// <summary>
        /// Switches between a normal window and a borderless full-screen one.
        /// The window is recreated in neither case: only the style and size
        /// change, so the renderer and the input state survive.
        /// </summary>
        public void SetFullscreen(bool value)
        {
            if (hWnd == IntPtr.Zero || value == fullscreenActive)
            {
                return;
            }

            fullscreenActive = value;

            var exStyle = (uint)NativeMethods.GetWindowLong(hWnd, NativeMethods.GWL_EXSTYLE);

            if (value)
            {
                NativeMethods.GetWindowRect(hWnd, out windowedRect);

                var style = (uint)NativeMethods.GetWindowLong(hWnd, NativeMethods.GWL_STYLE);
                style &= ~(NativeMethods.WS_CAPTION | NativeMethods.WS_THICKFRAME |
                           NativeMethods.WS_MINIMIZEBOX | NativeMethods.WS_MAXIMIZEBOX |
                           NativeMethods.WS_SYSMENU);
                NativeMethods.SetWindowLong(hWnd, NativeMethods.GWL_STYLE, (int)style);
                NativeMethods.SetMenu(hWnd, IntPtr.Zero);

                NativeMethods.SetWindowPos(
                    hWnd,
                    NativeMethods.HWND_TOP,
                    0,
                    0,
                    NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN),
                    NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN),
                    NativeMethods.SWP_FRAMECHANGED | NativeMethods.SWP_NOZORDER);
            }
            else
            {
                var style = (uint)NativeMethods.GetWindowLong(hWnd, NativeMethods.GWL_STYLE);
                style |= NativeMethods.WS_CAPTION | NativeMethods.WS_THICKFRAME |
                         NativeMethods.WS_MINIMIZEBOX | NativeMethods.WS_MAXIMIZEBOX |
                         NativeMethods.WS_SYSMENU;
                NativeMethods.SetWindowLong(hWnd, NativeMethods.GWL_STYLE, (int)style);

                if (menu != null)
                {
                    NativeMethods.SetMenu(hWnd, menu.Handle);
                }

                // The menu is back, so the frame is taller than it was before
                // full screen. Restoring the old outer rect would leave the
                // client area shorter than the framebuffer, so the size is
                // computed again from the framebuffer size and only the
                // position is kept.
                var frame = new NativeMethods.RECT();
                frame.right = requestedWidth;
                frame.bottom = requestedHeight;
                NativeMethods.AdjustWindowRectEx(ref frame, style, menu != null, exStyle);

                NativeMethods.SetWindowPos(
                    hWnd,
                    NativeMethods.HWND_TOP,
                    windowedRect.left,
                    windowedRect.top,
                    frame.right - frame.left,
                    frame.bottom - frame.top,
                    NativeMethods.SWP_FRAMECHANGED | NativeMethods.SWP_NOZORDER);
            }

            NativeMethods.DrawMenuBar(hWnd);
            NativeMethods.RECT client;
            if (NativeMethods.GetClientRect(hWnd, out client))
            {
                clientWidth = client.right - client.left;
                clientHeight = client.bottom - client.top;
            }
        }

        /// <summary>
        /// Loads the application icon from this executable's resources.
        ///
        /// Falls back to the system icon rather than leaving the field null:
        /// a null icon means the window gets a blank one, which looks broken.
        /// </summary>
        private IntPtr LoadWindowIcon()
        {
            var icon = NativeMethods.LoadIcon(hInstance, NativeMethods.IconResourceId);

            if (icon == IntPtr.Zero)
            {
                icon = NativeMethods.LoadIcon(IntPtr.Zero, NativeMethods.IconResourceId);
            }

            return icon;
        }

        /// <summary>
        /// Sets the icon after creation as well. The window class covers the
        /// normal case; setting it per window also covers the taskbar entry and
        /// Alt+Tab, which some shells take from the window rather than the class.
        /// </summary>
        private void ApplyWindowIcon()
        {
            if (windowIcon == IntPtr.Zero || hWnd == IntPtr.Zero)
            {
                return;
            }

            NativeMethods.SendMessage(
                hWnd,
                NativeMethods.WM_SETICON,
                new IntPtr(NativeMethods.ICON_BIG),
                windowIcon);

            NativeMethods.SendMessage(
                hWnd,
                NativeMethods.WM_SETICON,
                new IntPtr(NativeMethods.ICON_SMALL),
                windowIcon);
        }

        ////////////////////////////////////////////////////////////
        // Tray icon
        ////////////////////////////////////////////////////////////

        private const int TrayCallbackMessage = NativeMethods.WM_APP + 1;

        private void ShowTrayIcon()
        {
            if (trayAdded || windowIcon == IntPtr.Zero)
            {
                return;
            }

            if (menu != null)
            {
                menu.BuildTrayMenu();
            }

            trayData = new NativeMethods.NOTIFYICONDATA();
            trayData.cbSize = Marshal.SizeOf(typeof(NativeMethods.NOTIFYICONDATA));
            trayData.hWnd = hWnd;
            trayData.uID = 1;
            trayData.uFlags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP;
            trayData.uCallbackMessage = TrayCallbackMessage;
            trayData.hIcon = windowIcon;
            trayData.szTip = title;

            trayAdded = NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref trayData);

            if (!trayAdded)
            {
                // Some shells need the icon re-added after Explorer restarts;
                // not fatal, the window is still reachable from the taskbar.
                Console.WriteLine("Could not add the tray icon.");
            }
        }

        public void HideTrayIcon()
        {
            if (!trayAdded)
            {
                return;
            }

            NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref trayData);
            trayAdded = false;
        }

        /// <summary>Hides the window and leaves the tray icon behind.</summary>
        public void HideToTray()
        {
            if (hWnd == IntPtr.Zero)
            {
                return;
            }

            ShowTrayIcon();
            NativeMethods.ShowWindow(hWnd, NativeMethods.SW_HIDE);
        }

        public void RestoreFromTray()
        {
            if (hWnd == IntPtr.Zero)
            {
                return;
            }

            NativeMethods.ShowWindow(hWnd, NativeMethods.SW_SHOWNORMAL);
            NativeMethods.SetForegroundWindow(hWnd);
            NativeMethods.SetFocus(hWnd);
            HideTrayIcon();
        }

        private void ShowTrayMenu()
        {
            if (menu == null || menu.TrayMenu == IntPtr.Zero)
            {
                return;
            }

            NativeMethods.POINT point;
            NativeMethods.GetCursorPos(out point);

            // Without the SetForegroundWindow / WM_NULL pair the menu can stay
            // on screen after a click elsewhere; this is a long standing Win32
            // quirk with TrackPopupMenu on a tray icon.
            NativeMethods.SetForegroundWindow(hWnd);

            var id = NativeMethods.TrackPopupMenu(
                menu.TrayMenu,
                NativeMethods.TPM_RIGHTBUTTON | NativeMethods.TPM_RETURNCMD,
                point.x,
                point.y,
                0,
                hWnd,
                IntPtr.Zero);

            NativeMethods.PostMessage(hWnd, (uint)NativeMethods.WM_NULL, IntPtr.Zero, IntPtr.Zero);

            if (id == 0)
            {
                return;
            }

            var handler = Command;
            if (handler != null && menu.Handles(id))
            {
                handler((MenuCommand)id);
            }
        }

        ////////////////////////////////////////////////////////////
        // Overlay
        ////////////////////////////////////////////////////////////

        /// <summary>
        /// Draws the debug overlay over the frame with GDI. Deliberately not
        /// part of the software renderer: it must stay out of the framebuffer
        /// that gets saved to BMP and compared by tests.
        /// </summary>
        private void DrawOverlay(IntPtr dc)
        {
            if (overlayFont == IntPtr.Zero)
            {
                overlayFont = NativeMethods.CreateFont(
                    14, 0, 0, 0, NativeMethods.FW_BOLD,
                    0, 0, 0, NativeMethods.DEFAULT_CHARSET,
                    0, 0, 0, 0, "Consolas");
            }

            var previous = NativeMethods.SelectObject(dc, overlayFont);

            NativeMethods.SetBkMode(dc, NativeMethods.TRANSPARENT);
            NativeMethods.SetTextColor(dc, NativeMethods.Rgb(0xE6, 0xE6, 0x50));

            var lines = (OverlayText ?? "").Split('\n');
            var y = 6;

            foreach (var line in lines)
            {
                NativeMethods.TextOut(dc, 8, y, line, line.Length);
                y += 16;
            }

            NativeMethods.SelectObject(dc, previous);
        }

        public void PumpMessages()
        {
            NativeMethods.MSG msg;
            while (NativeMethods.PeekMessage(out msg, IntPtr.Zero, 0, 0, NativeMethods.PM_REMOVE))
            {
                if (msg.message == NativeMethods.WM_QUIT)
                {
                    quit = true;
                    return;
                }

                NativeMethods.TranslateMessage(ref msg);
                NativeMethods.DispatchMessage(ref msg);
            }
        }

        /// <summary>
        /// Gives the window the address of a pinned BGRA framebuffer and shows
        /// the current contents.
        /// </summary>
        public void Present(IntPtr pixelBuffer, int width, int height)
        {
            pixels = pixelBuffer;
            pixelWidth = width;
            pixelHeight = height;
            hasFrame = true;
            Blit();
        }

        /// <summary>
        /// Creates the offscreen surface the frame is copied into. Keeping a
        /// DIB section and blitting from it is several times faster than
        /// StretchDIBits straight to the window: StretchDIBits takes a generic
        /// path in GDI and, at 640x400, was measured at ~7 ms per frame, which
        /// is half the frame budget at 70 fps and the direct cause of visible
        /// stutter.
        /// </summary>
        private void EnsureSurface()
        {
            if (memoryDc != IntPtr.Zero &&
                surfaceWidth == pixelWidth &&
                surfaceHeight == pixelHeight)
            {
                return;
            }

            ReleaseSurface();

            var screenDc = NativeMethods.GetDC(hWnd);
            if (screenDc == IntPtr.Zero)
            {
                return;
            }

            try
            {
                memoryDc = NativeMethods.CreateCompatibleDC(screenDc);
                if (memoryDc == IntPtr.Zero)
                {
                    return;
                }

                bitmapInfo.bmiHeader.biWidth = pixelWidth;
                bitmapInfo.bmiHeader.biHeight = -pixelHeight; // top-down rows
                bitmapInfo.bmiHeader.biSizeImage = (uint)(pixelWidth * pixelHeight * 4);

                surfaceBits = IntPtr.Zero;
                surface = NativeMethods.CreateDIBSection(
                    screenDc,
                    ref bitmapInfo,
                    NativeMethods.DIB_RGB_COLORS,
                    out surfaceBits,
                    IntPtr.Zero,
                    0);

                if (surface == IntPtr.Zero)
                {
                    NativeMethods.DeleteDC(memoryDc);
                    memoryDc = IntPtr.Zero;
                    return;
                }

                previousObject = NativeMethods.SelectObject(memoryDc, surface);

                surfaceWidth = pixelWidth;
                surfaceHeight = pixelHeight;
            }
            finally
            {
                NativeMethods.ReleaseDC(hWnd, screenDc);
            }
        }

        private void ReleaseSurface()
        {
            if (memoryDc != IntPtr.Zero)
            {
                if (previousObject != IntPtr.Zero)
                {
                    NativeMethods.SelectObject(memoryDc, previousObject);
                    previousObject = IntPtr.Zero;
                }

                NativeMethods.DeleteDC(memoryDc);
                memoryDc = IntPtr.Zero;
            }

            if (surface != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(surface);
                surface = IntPtr.Zero;
            }

            surfaceBits = IntPtr.Zero;
            surfaceWidth = 0;
            surfaceHeight = 0;
        }

        private void Blit()
        {
            if (!hasFrame || hWnd == IntPtr.Zero || clientWidth <= 0 || clientHeight <= 0)
            {
                return;
            }

            EnsureSurface();
            if (memoryDc == IntPtr.Zero || surfaceBits == IntPtr.Zero)
            {
                return;
            }

            // Copy the framebuffer into the DIB section. This is a plain memory
            // copy of one frame; the expensive part was the GDI stretch.
            CopyMemory(surfaceBits, pixels, (IntPtr)(pixelWidth * pixelHeight * 4));

            var dc = NativeMethods.GetDC(hWnd);
            if (dc == IntPtr.Zero)
            {
                return;
            }

            try
            {
                if (clientWidth == pixelWidth && clientHeight == pixelHeight)
                {
                    NativeMethods.BitBlt(
                        dc, 0, 0, pixelWidth, pixelHeight,
                        memoryDc, 0, 0, NativeMethods.SRCCOPY);
                }
                else
                {
                    // COLORONCOLOR is nearest-neighbour-ish scaling, which is
                    // what pixel art wants; HALFTONE would blur it and is slower.
                    NativeMethods.SetStretchBltMode(dc, NativeMethods.COLORONCOLOR);
                    NativeMethods.StretchBlt(
                        dc, 0, 0, clientWidth, clientHeight,
                        memoryDc, 0, 0, pixelWidth, pixelHeight,
                        NativeMethods.SRCCOPY);
                }
            }
            finally
            {
                NativeMethods.ReleaseDC(hWnd, dc);
            }

            // The whole client area has just been painted, so drop any pending
            // WM_PAINT. Without this, a partly covered window asks for a repaint
            // every frame and the frame is blitted twice.
            NativeMethods.ValidateRect(hWnd, IntPtr.Zero);

            if (ShowOverlay && !string.IsNullOrEmpty(OverlayText))
            {
                DrawOverlay(dc);
            }
        }

        [DllImport("kernel32.dll", ExactSpelling = true, EntryPoint = "RtlMoveMemory")]
        private static extern void CopyMemory(IntPtr destination, IntPtr source, IntPtr length);

        public void SetTitle(string value)
        {
            if (hWnd != IntPtr.Zero)
            {
                NativeMethods.SetWindowText(hWnd, value);
            }
        }

        public bool IsKeyPressed(DoomKey key)
        {
            var index = (int)key;
            if (index < 0 || index >= keyPressed.Length)
            {
                return false;
            }

            return keyPressed[index];
        }

        public bool IsMouseButtonPressed(DoomMouseButton button)
        {
            var index = (int)button;
            if (index < 0 || index >= mousePressed.Length)
            {
                return false;
            }

            return mousePressed[index];
        }

        public void ConsumeMouseDelta(out int dx, out int dy)
        {
            dx = mouseDeltaX;
            dy = mouseDeltaY;
            mouseDeltaX = 0;
            mouseDeltaY = 0;
        }

        public void GrabMouse()
        {
            if (mouseGrabbed || hWnd == IntPtr.Zero)
            {
                return;
            }

            mouseGrabbed = true;
            mouseDeltaX = 0;
            mouseDeltaY = 0;

            while (NativeMethods.ShowCursor(false) >= 0)
            {
            }

            var rect = new NativeMethods.RECT();
            if (NativeMethods.GetClientRect(hWnd, out rect))
            {
                var topLeft = new NativeMethods.POINT();
                topLeft.x = rect.left;
                topLeft.y = rect.top;
                NativeMethods.ClientToScreen(hWnd, ref topLeft);

                var bottomRight = new NativeMethods.POINT();
                bottomRight.x = rect.right;
                bottomRight.y = rect.bottom;
                NativeMethods.ClientToScreen(hWnd, ref bottomRight);

                var clip = new NativeMethods.RECT();
                clip.left = topLeft.x;
                clip.top = topLeft.y;
                clip.right = bottomRight.x;
                clip.bottom = bottomRight.y;
                NativeMethods.ClipCursor(ref clip);

                var center = new NativeMethods.POINT();
                center.x = (clip.left + clip.right) / 2;
                center.y = (clip.top + clip.bottom) / 2;
                NativeMethods.SetCursorPos(center.x, center.y);
            }
        }

        public void ReleaseMouse()
        {
            if (!mouseGrabbed)
            {
                return;
            }

            mouseGrabbed = false;
            NativeMethods.ClipCursor(IntPtr.Zero);

            while (NativeMethods.ShowCursor(true) < 0)
            {
            }

            mouseDeltaX = 0;
            mouseDeltaY = 0;
        }

        private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            switch (msg)
            {
                case NativeMethods.WM_CLOSE:
                    NativeMethods.DestroyWindow(hWnd);
                    return IntPtr.Zero;

                case NativeMethods.WM_DESTROY:
                    NativeMethods.PostQuitMessage(0);
                    return IntPtr.Zero;

                case NativeMethods.WM_PAINT:
                    Blit();
                    NativeMethods.ValidateRect(hWnd, IntPtr.Zero);
                    return IntPtr.Zero;

                case NativeMethods.WM_ERASEBKGND:
                    return new IntPtr(1);

                case NativeMethods.WM_COMMAND:
                {
                    // Low word is the command id; notifications use the high
                    // word, which is zero for menu items.
                    var id = (int)(wParam.ToInt64() & 0xFFFF);
                    if (menu != null && menu.Handles(id))
                    {
                        var handler = Command;
                        if (handler != null)
                        {
                            handler((MenuCommand)id);
                        }

                        return IntPtr.Zero;
                    }

                    break;
                }

                case NativeMethods.WM_SIZE:
                {
                    clientWidth = (int)(lParam.ToInt64() & 0xFFFF);
                    clientHeight = (int)((lParam.ToInt64() >> 16) & 0xFFFF);

                    var sizeType = wParam.ToInt32();
                    if (sizeType == NativeMethods.SIZE_MINIMIZED && MinimizeToTray)
                    {
                        NativeMethods.ShowWindow(hWnd, NativeMethods.SW_HIDE);
                        ShowTrayIcon();
                    }

                    return IntPtr.Zero;
                }

                case TrayCallbackMessage:
                {
                    var mouseMessage = (int)(lParam.ToInt64() & 0xFFFF);

                    if (mouseMessage == NativeMethods.WM_LBUTTONDBLCLK)
                    {
                        RestoreFromTray();
                        return IntPtr.Zero;
                    }

                    if (mouseMessage == NativeMethods.WM_RBUTTONUP)
                    {
                        ShowTrayMenu();
                        return IntPtr.Zero;
                    }

                    break;
                }

                case NativeMethods.WM_SYSCOMMAND:
                    if ((wParam.ToInt64() & 0xFFF0) == NativeMethods.SC_KEYMENU)
                    {
                        return IntPtr.Zero;
                    }

                    break;

                case NativeMethods.WM_SYSKEYDOWN:
                case NativeMethods.WM_KEYDOWN:
                    OnKeyDown(hWnd, wParam);
                    break;

                case NativeMethods.WM_SYSKEYUP:
                case NativeMethods.WM_KEYUP:
                    OnKeyUp(hWnd, wParam);
                    break;

                case NativeMethods.WM_MOUSEMOVE:
                    OnMouseMove(lParam);
                    break;

                case NativeMethods.WM_LBUTTONDOWN:
                    SetMouseButton(DoomMouseButton.Mouse1, true);
                    break;

                case NativeMethods.WM_LBUTTONUP:
                    SetMouseButton(DoomMouseButton.Mouse1, false);
                    break;

                case NativeMethods.WM_RBUTTONDOWN:
                    SetMouseButton(DoomMouseButton.Mouse2, true);
                    break;

                case NativeMethods.WM_RBUTTONUP:
                    SetMouseButton(DoomMouseButton.Mouse2, false);
                    break;

                case NativeMethods.WM_MBUTTONDOWN:
                    SetMouseButton(DoomMouseButton.Mouse3, true);
                    break;

                case NativeMethods.WM_MBUTTONUP:
                    SetMouseButton(DoomMouseButton.Mouse3, false);
                    break;

                case NativeMethods.WM_SETFOCUS:
                    focused = true;
                    break;

                case NativeMethods.WM_KILLFOCUS:
                    focused = false;
                    ClearInput();
                    ReleaseMouse();
                    break;
            }
            return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
        }

        private void OnKeyDown(IntPtr hWnd, IntPtr wParam)
        {
            var key = ToDoomKey(wParam.ToInt32());
            if (key != DoomKey.Unknown)
            {
                keyPressed[(int)key] = true;
                KeyDown?.Invoke(key);
            }
        }

        private void OnKeyUp(IntPtr hWnd, IntPtr wParam)
        {
            var key = ToDoomKey(wParam.ToInt32());
            if (key != DoomKey.Unknown)
            {
                keyPressed[(int)key] = false;
                KeyUp?.Invoke(key);
            }
        }

        private void OnMouseMove(IntPtr lParam)
        {
            if (!mouseGrabbed || recenteringMouse)
            {
                return;
            }

            var x = (short)(lParam.ToInt64() & 0xFFFF);
            var y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);

            var centerX = clientWidth / 2;
            var centerY = clientHeight / 2;

            var dx = x - centerX;
            var dy = y - centerY;

            if (dx != 0 || dy != 0)
            {
                mouseDeltaX += dx;
                mouseDeltaY += dy;

                var center = new NativeMethods.POINT();
                center.x = centerX;
                center.y = centerY;
                NativeMethods.ClientToScreen(hWnd, ref center);

                recenteringMouse = true;
                NativeMethods.SetCursorPos(center.x, center.y);
                recenteringMouse = false;
            }
        }

        private void SetMouseButton(DoomMouseButton button, bool pressed)
        {
            mousePressed[(int)button] = pressed;
        }

        private void ClearInput()
        {
            for (var i = 0; i < keyPressed.Length; i++)
            {
                keyPressed[i] = false;
            }

            for (var i = 0; i < mousePressed.Length; i++)
            {
                mousePressed[i] = false;
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;

            ReleaseMouse();
            ReleaseSurface();
            HideTrayIcon();

            if (overlayFont != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(overlayFont);
                overlayFont = IntPtr.Zero;
            }

            if (menu != null)
            {
                menu.Dispose();
                menu = null;
            }

            if (hWnd != IntPtr.Zero)
            {
                NativeMethods.DestroyWindow(hWnd);
                hWnd = IntPtr.Zero;
            }
        }

        public bool HasFocus => focused;

        /// <summary>The menu bar, or null in full-screen mode.</summary>
        public GameMenu Menu => menu;

        public bool IsFullscreen => fullscreenActive;

        public bool Closed => quit;

        public IntPtr Handle => hWnd;

        public int ClientWidth => clientWidth;

        public int ClientHeight => clientHeight;

        public event Action<DoomKey> KeyDown;

        public event Action<DoomKey> KeyUp;

        private static DoomKey ToDoomKey(int vk)
        {
            switch (vk)
            {
                case NativeMethods.VK_BACK: return DoomKey.Backspace;
                case NativeMethods.VK_TAB: return DoomKey.Tab;
                case NativeMethods.VK_RETURN: return DoomKey.Enter;
                case NativeMethods.VK_PAUSE: return DoomKey.Pause;
                case NativeMethods.VK_ESCAPE: return DoomKey.Escape;
                case NativeMethods.VK_SPACE: return DoomKey.Space;
                case NativeMethods.VK_PRIOR: return DoomKey.PageUp;
                case NativeMethods.VK_NEXT: return DoomKey.PageDown;
                case NativeMethods.VK_END: return DoomKey.End;
                case NativeMethods.VK_HOME: return DoomKey.Home;
                case NativeMethods.VK_LEFT: return DoomKey.Left;
                case NativeMethods.VK_UP: return DoomKey.Up;
                case NativeMethods.VK_RIGHT: return DoomKey.Right;
                case NativeMethods.VK_DOWN: return DoomKey.Down;
                case NativeMethods.VK_INSERT: return DoomKey.Insert;
                case NativeMethods.VK_DELETE: return DoomKey.Delete;
                case NativeMethods.VK_LWIN: return DoomKey.LSystem;
                case NativeMethods.VK_RWIN: return DoomKey.RSystem;
                case NativeMethods.VK_MULTIPLY: return DoomKey.Multiply;
                case NativeMethods.VK_ADD: return DoomKey.Add;
                case NativeMethods.VK_SUBTRACT: return DoomKey.Subtract;
                case NativeMethods.VK_DIVIDE: return DoomKey.Divide;
                case NativeMethods.VK_LSHIFT: return DoomKey.LShift;
                case NativeMethods.VK_RSHIFT: return DoomKey.RShift;
                case NativeMethods.VK_LCONTROL: return DoomKey.LControl;
                case NativeMethods.VK_RCONTROL: return DoomKey.RControl;
                case NativeMethods.VK_LMENU: return DoomKey.LAlt;
                case NativeMethods.VK_RMENU: return DoomKey.RAlt;
                case NativeMethods.VK_OEM_1: return DoomKey.Semicolon;
                case NativeMethods.VK_OEM_PLUS: return DoomKey.Equal;
                case NativeMethods.VK_OEM_COMMA: return DoomKey.Comma;
                case NativeMethods.VK_OEM_MINUS: return DoomKey.Hyphen;
                case NativeMethods.VK_OEM_PERIOD: return DoomKey.Period;
                case NativeMethods.VK_OEM_2: return DoomKey.Slash;
                case NativeMethods.VK_OEM_3: return DoomKey.Tilde;
                case NativeMethods.VK_OEM_4: return DoomKey.LBracket;
                case NativeMethods.VK_OEM_5: return DoomKey.Backslash;
                case NativeMethods.VK_OEM_6: return DoomKey.RBracket;
                case NativeMethods.VK_OEM_7: return DoomKey.Quote;
            }

            if (vk >= 0x30 && vk <= 0x39)
            {
                return (DoomKey)((int)DoomKey.Num0 + (vk - 0x30));
            }

            if (vk >= 0x41 && vk <= 0x5A)
            {
                return (DoomKey)((int)DoomKey.A + (vk - 0x41));
            }

            if (vk >= NativeMethods.VK_NUMPAD0 && vk <= NativeMethods.VK_NUMPAD9)
            {
                return (DoomKey)((int)DoomKey.Numpad0 + (vk - NativeMethods.VK_NUMPAD0));
            }

            if (vk >= NativeMethods.VK_F1 && vk <= NativeMethods.VK_F15)
            {
                return (DoomKey)((int)DoomKey.F1 + (vk - NativeMethods.VK_F1));
            }

            return DoomKey.Unknown;
        }
    }
}
