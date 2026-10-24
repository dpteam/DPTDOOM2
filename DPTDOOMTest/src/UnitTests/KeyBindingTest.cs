using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;

namespace DPTDOOMTest.UnitTests
{
    /// <summary>
    /// Rebindable keys, driven through the real menu.
    ///
    /// Three things are worth pinning, and all three are easy to get wrong in a way
    /// that only shows up in play:
    ///
    ///  - rebinding a key must keep the mouse button the default paired with it, or
    ///    "use" stops working on the right mouse button for anyone who moved it to E;
    ///  - Escape must cancel the capture rather than become a binding, because Escape
    ///    is the key that leaves every screen;
    ///  - a row left waiting for a key must not still be waiting when the screen is
    ///    reopened, or the first press on an idle-looking screen is swallowed.
    /// </summary>
    [TestClass]
    public class KeyBindingTest
    {
        private static DoomMenu Menu(out Config config)
        {
            var local = new Config();
            config = local;

            var args = new CommandLineArgs(new[] { "-iwad", "doom2.wad" });
            var content = GameContent.CreateDummy(WadPath.Doom2);
            var doom = new Doom(args, local, content, null, null, null, null);

            return new DoomMenu(doom);
        }

        private static DoomEvent Key(DoomKey key)
        {
            return new DoomEvent(EventType.KeyDown, key);
        }

        /// <summary>
        /// Walks the menu to a row by its label, using the same events a player does.
        /// </summary>
        private static bool Select(TextMenu menu, string label)
        {
            for (var i = 0; i < 100; i++)
            {
                if (menu.Current != null && menu.Current.Label == label)
                {
                    return true;
                }

                menu.DoEvent(Key(DoomKey.Down));
            }

            return false;
        }

        [TestMethod]
        public void TheDefaultForUseIsSpaceAndTheRightMouseButton()
        {
            // The defaults are what id Software chose and what most people expect on
            // arrival. If this ever changes, it is a decision, not an accident.
            var config = new Config();

            Assert.AreEqual(DoomKey.Space, config.key_use.Keys[0], "use is Space by default");
            Assert.AreEqual(DoomMouseButton.Mouse2, config.key_use.MouseButtons[0],
                "and the right mouse button");
        }

        [TestMethod]
        public void AnItemAssignsTheKeyItIsGiven()
        {
            var config = new Config();

            var item = new KeyBindingTextItem("USE",
                () => config.key_use,
                value => config.key_use = value,
                "Open doors.");

            item.Assign(DoomKey.E);

            Assert.AreEqual(DoomKey.E, config.key_use.Keys[0], "the key changed");
            Assert.AreEqual(1, config.key_use.Keys.Count, "and it replaced, not appended");
        }

        [TestMethod]
        public void RebindingKeepsTheMouseButton()
        {
            // The player asked to change the key. Taking the mouse button away at the
            // same time would be changing something they did not ask about, and the
            // loss is silent - the button simply stops working.
            var config = new Config();

            var item = new KeyBindingTextItem("USE",
                () => config.key_use,
                value => config.key_use = value,
                "Open doors.");

            item.Assign(DoomKey.E);

            Assert.AreEqual(DoomMouseButton.Mouse2, config.key_use.MouseButtons[0],
                "the right mouse button still opens doors");
        }

        [TestMethod]
        public void TheItemShowsTheKeyInUpperCase()
        {
            var config = new Config();

            var item = new KeyBindingTextItem("USE",
                () => config.key_use,
                value => config.key_use = value,
                "Open doors.");

            Assert.AreEqual("SPACE", item.Value);

            item.Assign(DoomKey.E);
            Assert.AreEqual("E", item.Value);
        }

        [TestMethod]
        public void EnterArmsTheCaptureAndTheNextKeyBinds()
        {
            var config = new Config();
            var menu = Menu(out config);

            // The keys page is built by the menu; walk to it the way a player does.
            var keys = FindKeysPage(menu);
            Assert.IsNotNull(keys, "the settings screen has a KEYS page");

            Assert.IsTrue(Select(keys, "USE"), "the USE row is reachable");

            // Enter: the row arms itself.
            keys.DoEvent(Key(DoomKey.Enter));
            Assert.IsNotNull(keys.Capturing, "the row is waiting for a key");

            // And the key that follows becomes the binding.
            keys.DoEvent(Key(DoomKey.E));

            Assert.IsNull(keys.Capturing, "the capture is over");
            Assert.AreEqual(DoomKey.E, config.key_use.Keys[0], "Space became E");
        }

        [TestMethod]
        public void EscapeCancelsTheCapture()
        {
            // Escape leaves every other screen. A player who captured it by accident
            // would have no way back out of a menu, so it cancels instead.
            var config = new Config();
            var menu = Menu(out config);

            var keys = FindKeysPage(menu);
            Assert.IsTrue(Select(keys, "USE"));

            keys.DoEvent(Key(DoomKey.Enter));
            Assert.IsNotNull(keys.Capturing);

            keys.DoEvent(Key(DoomKey.Escape));

            Assert.IsNull(keys.Capturing, "the capture is over");
            Assert.AreEqual(DoomKey.Space, config.key_use.Keys[0],
                "and Space is still the binding");
        }

        [TestMethod]
        public void ReopeningDoesNotLeaveARowWaitingForKey()
        {
            var config = new Config();
            var menu = Menu(out config);

            var keys = FindKeysPage(menu);
            Assert.IsTrue(Select(keys, "USE"));

            keys.DoEvent(Key(DoomKey.Enter));
            Assert.IsNotNull(keys.Capturing);

            // Walking away and coming back is what the player does; the abandoned
            // capture must not survive it.
            keys.Open();

            Assert.IsNull(keys.Capturing, "reopening clears the capture");
        }

        /// <summary>
        /// Finds the KEYS page by opening the settings screen and walking its pages.
        ///
        /// The pages are private, so this goes through the public surface: the settings
        /// screen, then the INPUT page, then the row that opens the keys.
        /// </summary>
        private static TextMenu FindKeysPage(DoomMenu menu)
        {
            menu.ShowSettings();

            var root = menu.Current as TextMenu;
            Assert.IsNotNull(root, "the settings screen is a text menu");

            Assert.IsTrue(Select(root, "INPUT"), "the root page has INPUT");
            root.DoEvent(Key(DoomKey.Enter));

            var input = menu.Current as TextMenu;
            Assert.IsNotNull(input, "INPUT opens a page");

            Assert.IsTrue(Select(input, "KEYS"), "the input page has KEYS");
            input.DoEvent(Key(DoomKey.Enter));

            return menu.Current as TextMenu;
        }
    }
}
