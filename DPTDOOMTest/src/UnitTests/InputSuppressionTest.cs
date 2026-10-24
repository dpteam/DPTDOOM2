using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;
using DPTDOOM.UserInput;

namespace DPTDOOMTest.UnitTests
{
    /// <summary>
    /// The console and the chat must take the keyboard and the mouse with them.
    ///
    /// Both are read from *state*, not from events: movement keys are polled, and the mouse
    /// delta is taken straight from the window. So swallowing key events is not enough - the
    /// input has to be switched off wholesale, which is what <c>IUserInput.Suppressed</c> is
    /// for.
    ///
    /// The bug this pins: the switch lived inside the chat pump, after an early return for
    /// "no network driver". In single player it was therefore never thrown at all, so opening
    /// the console left the mouse turning the view and the keys walking the player while the
    /// console looked like it owned the input.
    /// </summary>
    [TestClass]
    public class InputSuppressionTest
    {
        /// <summary>
        /// Records what the game asks of the input, and nothing else.
        ///
        /// Reset is counted rather than ignored: motion collected while the console was open
        /// must be thrown away, or it is applied in one tic when the console closes and the
        /// view snaps by however far the hand travelled.
        /// </summary>
        private sealed class RecordingInput : IUserInput
        {
            public bool Suppressed { get; set; }
            public int Resets;

            public void BuildTicCmd(TicCmd cmd)
            {
                cmd.Clear();
            }

            public void Reset()
            {
                Resets++;
            }

            public void GrabMouse()
            {
            }

            public void ReleaseMouse()
            {
            }

            public int TakeViewPitch(out bool center)
            {
                center = false;
                return 0;
            }

            public int MaxMouseSensitivity { get { return 15; } }
            public int MouseSensitivity { get; set; }
        }

        [TestMethod]
        public void TheConsoleSuppressesInputInASinglePlayerGame()
        {
            // No -net on the command line: this is the case the bug lived in.
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var input = new RecordingInput();

                var args = new CommandLineArgs(new[] { "-iwad", "doom2.wad" });
                var doom = new Doom(args, new Config(), content, null, null, null, input);

                Assert.IsFalse(input.Suppressed, "nothing is open, so nothing is suppressed");

                doom.ConsoleOverlay.Open();
                doom.Update();

                Assert.IsTrue(input.Suppressed, "an open console must own the input");
                Assert.IsTrue(input.Resets > 0,
                    "and the mouse motion collected while it was open must be thrown away");
            }
        }

        [TestMethod]
        public void ClosingTheConsoleGivesTheInputBack()
        {
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var input = new RecordingInput();

                var args = new CommandLineArgs(new[] { "-iwad", "doom2.wad" });
                var doom = new Doom(args, new Config(), content, null, null, null, input);

                doom.ConsoleOverlay.Open();
                doom.Update();
                Assert.IsTrue(input.Suppressed);

                doom.ConsoleOverlay.Close();
                doom.Update();

                Assert.IsFalse(input.Suppressed, "the player has the controls back");
            }
        }

        [TestMethod]
        public void TheChatSuppressesInputToo()
        {
            // The original reason the switch exists. It worked before because a network game
            // has a driver; this checks it is not lost now that the switch lives elsewhere.
            using (var content = GameContent.CreateDummy(WadPath.Doom2))
            {
                var input = new RecordingInput();

                var args = new CommandLineArgs(new[] { "-iwad", "doom2.wad" });
                var doom = new Doom(args, new Config(), content, null, null, null, input);

                doom.ChatInput.Open();
                doom.Update();

                Assert.IsTrue(input.Suppressed, "typing a message must not drive the player");

                doom.ChatInput.Close();
                doom.Update();

                Assert.IsFalse(input.Suppressed);
            }
        }
    }
}
