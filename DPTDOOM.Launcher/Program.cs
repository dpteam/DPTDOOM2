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
using System.IO;
using System.Windows.Forms;
using DPTDOOM.Shared;
using Microsoft.Win32;

namespace DPTDOOM.Launcher
{
    internal static class Program
    {
        /// <summary>
        /// Opts this executable into the newest MSHTML document mode available
        /// on the machine. Without it the WebBrowser control falls back to IE7
        /// rendering, where the layout collapses. The key is per user and
        /// harmless if the write is refused.
        /// </summary>
        private static void EnableModernDocumentMode()
        {
            const string keyPath =
                @"Software\Microsoft\Internet Explorer\Main\FeatureControl\FEATURE_BROWSER_EMULATION";

            try
            {
                var name = Path.GetFileName(Application.ExecutablePath);

                using (var key = Registry.CurrentUser.CreateSubKey(keyPath))
                {
                    if (key != null)
                    {
                        // 11001 = IE11 edge mode; older systems ignore it and
                        // use their newest mode anyway.
                        key.SetValue(name, 11001, RegistryValueKind.DWord);
                    }
                }
            }
            catch
            {
            }
        }

        [STAThread]
        public static void Main(string[] args)
        {
            EnableModernDocumentMode();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (args.Length > 0 && args[0] == "--dump-html")
            {
                // Design-time helper: writes the assembled page so the markup
                // can be inspected or opened in a browser without the EXE.
                var target = args.Length > 1 ? args[1] : "launcher-preview.html";
                File.WriteAllText(target, LauncherForm.AssemblePage());
                Console.WriteLine("Wrote " + target);
                return;
            }

            try
            {
                Application.Run(new LauncherForm());
            }
            catch (Exception e)
            {
                MessageBox.Show(e.ToString(), Branding.LauncherName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
