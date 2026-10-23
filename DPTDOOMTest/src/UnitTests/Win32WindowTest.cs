using System;
using System.Runtime.InteropServices;
using System.Text;
using DPTDOOM.Win32;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DPTDOOMTest.UnitTests
{
    /// <summary>
    /// Regression test for a real bug: DefWindowProc was declared without
    /// CharSet, so P/Invoke bound the ANSI variant. Every window message that
    /// carries a string was then decoded as ANSI and the title arrived as "D".
    ///
    /// The test needs no IWAD: it creates the window directly and reads the
    /// title back through the Win32 API, which is exactly what a user sees in
    /// the title bar and the taskbar.
    /// </summary>
    [TestClass]
    public class Win32WindowTest
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, EntryPoint = "GetWindowTextW")]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, EntryPoint = "GetWindowTextLengthW")]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        private static bool IsWindows
        {
            get { return Environment.OSVersion.Platform == PlatformID.Win32NT; }
        }

        [TestMethod]
        public void TitleSurvivesWindowCreation()
        {
            if (!IsWindows)
            {
                Assert.Inconclusive("The Win32 front end is Windows-only.");
            }

            var expected = "DPT.DOOM v0.1.0";

            using (var window = new Win32Window(expected, 320, 200, false))
            {
                window.Create();

                Assert.AreEqual(expected, ReadTitle(window.Handle));
            }
        }

        [TestMethod]
        public void TitleCanBeChangedAfterCreation()
        {
            if (!IsWindows)
            {
                Assert.Inconclusive("The Win32 front end is Windows-only.");
            }

            using (var window = new Win32Window("initial", 320, 200, false))
            {
                window.Create();

                var expected = "DPT.DOOM - HOSTING";
                window.SetTitle(expected);

                Assert.AreEqual(expected, ReadTitle(window.Handle));
            }
        }

        private static string ReadTitle(IntPtr handle)
        {
            Assert.AreNotEqual(IntPtr.Zero, handle, "The window was not created.");

            var length = GetWindowTextLength(handle);
            var buffer = new StringBuilder(Math.Max(length + 1, 16));
            GetWindowText(handle, buffer, buffer.Capacity);

            return buffer.ToString();
        }
    }
}
