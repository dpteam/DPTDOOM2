using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DPTDOOMTest.UnitTests
{
    /// <summary>
    /// Where the About screen lives and where the Konami code works.
    ///
    /// Both are decisions about the shape of the menu, and the menu cannot be built without a
    /// window, a renderer and a WAD - so these read the source the way <see cref="ConfigCoverageTest"/>
    /// does. That is weaker than running the menu, and it is honest about what it proves: the wiring,
    /// not the behaviour. The behaviour that can be tested without a screen - that the code watcher
    /// fires once and only on the right sequence - is in <c>KonamiCodeTest</c>.
    ///
    /// What this pins:
    ///
    ///  - `VERSION` opens the About screen rather than being a line that only prints the build.
    ///    A row that does nothing when activated is the bug this was written for: it looked like
    ///    a screen and behaved like a label;
    ///  - the Konami code is fed **only** while the About screen is current. Anywhere else it was
    ///    found by accident, which is the opposite of how an easter egg is meant to be found.
    /// </summary>
    [TestClass]
    public class MenuAboutTest
    {
        private static string Source(string path)
        {
            var full = Path.Combine(Root(), path);

            Assert.IsTrue(File.Exists(full), "cannot read " + path);

            return File.ReadAllText(full);
        }

        private static string Root()
        {
            var directory = AppDomain.CurrentDomain.BaseDirectory;

            while (directory != null)
            {
                if (Directory.Exists(Path.Combine(directory, "DPTDOOM", "src")))
                {
                    return directory;
                }

                directory = Path.GetDirectoryName(directory);
            }

            Assert.Fail("could not find the repository root from " + AppDomain.CurrentDomain.BaseDirectory);
            return null;
        }

        [TestMethod]
        public void VersionOpensTheAboutScreen()
        {
            var menu = Source("DPTDOOM/src/Doom/Menu/DoomMenu.cs");

            // The row has to be an action, not an info line: an info line cannot be activated,
            // and "press enter and nothing happens" reads as a broken menu.
            var match = Regex.Match(menu,
                @"""?VERSION""?\s*,\s*\r?\n\s*\(\)\s*=>\s*ShowAbout\(\)",
                RegexOptions.Multiline);

            Assert.IsTrue(match.Success,
                "SYSTEM -> VERSION must be an ActionTextItem that calls ShowAbout()");
        }

        [TestMethod]
        public void TheKonamiCodeOnlyWorksOnTheAboutScreen()
        {
            var menu = Source("DPTDOOM/src/Doom/Menu/DoomMenu.cs");

            // Every call site must be guarded by the About page being current. One unguarded call
            // would make the code work from every page again, which is what this change undid.
            var calls = Regex.Matches(menu, @"FeedKonami\s*\(");

            Assert.IsTrue(calls.Count >= 1, "the code watcher is still wired");

            var guarded = Regex.Matches(menu, @"current\s*==\s*about\s*&&[^;]*FeedKonami\s*\(");

            Assert.AreEqual(1, guarded.Count,
                "exactly one call site, and it is guarded by the About page being current");

            // The definition itself does not count as a call site.
            Assert.AreEqual(guarded.Count + 1, calls.Count,
                "no other call site exists: " + calls.Count + " calls, " + guarded.Count + " guarded");
        }

        [TestMethod]
        public void TheAboutScreenReachesTheKonamiCode()
        {
            // The guard is only worth anything if the page it names is the one VERSION opens.
            var menu = Source("DPTDOOM/src/Doom/Menu/DoomMenu.cs");

            StringAssert.Contains(menu, "public void ShowAbout()");
            StringAssert.Contains(menu, "SetCurrent(about);");

            Assert.IsTrue(Regex.IsMatch(menu, @"current\s*==\s*about"),
                "and the guard names the same page");
        }
    }
}
