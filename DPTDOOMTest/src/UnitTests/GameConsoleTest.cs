using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;

namespace DPTDOOMTest.UnitTests
{
    /// <summary>
    /// The drop-down console and its command table.
    ///
    /// Tested without a game, because that is the point of the split: the console
    /// owns text and history, the command table owns names and handlers, and neither
    /// needs a world. A console that could only be tested by launching Doom would be
    /// a console nobody tests.
    ///
    /// Two properties matter more than the commands themselves:
    ///
    ///   - every key belongs to the console while it is open, or typing "iddqd" into
    ///     it would enable a cheat and a digit would also change the view size;
    ///   - nothing in it can change the simulation. The table has no cheat command at
    ///     all, and the one command that could change the world - "map" - refuses to
    ///     run in a network game.
    /// </summary>
    [TestClass]
    public class GameConsoleTest
    {
        private static DoomEvent Key(DoomKey key)
        {
            return new DoomEvent(EventType.KeyDown, key);
        }

        /// <summary>
        /// The DoomKey that produces a character, found by asking each key what it
        /// types. Doing it this way means the test types the same characters a player
        /// does, instead of guessing at the mapping.
        /// </summary>
        private static DoomKey KeyFor(char ch)
        {
            foreach (DoomKey key in Enum.GetValues(typeof(DoomKey)))
            {
                if (key.GetChar() == ch)
                {
                    return key;
                }
            }

            Assert.Fail("no key produces '" + ch + "'");
            return DoomKey.Unknown;
        }

        private static void Type(GameConsole console, string text)
        {
            foreach (var ch in text)
            {
                console.Handle(Key(KeyFor(ch)));
            }
        }

        [TestMethod]
        public void InactiveConsoleIgnoresEverything()
        {
            var console = new GameConsole();

            Assert.IsFalse(console.Handle(Key(DoomKey.A)), "an unopened console takes no keys");
            Assert.AreEqual("", console.Text, "and does not accumulate text");
        }

        [TestMethod]
        public void TypingBuildsTheLine()
        {
            var console = new GameConsole();
            console.Open();

            Type(console, "gamma 3");

            Assert.AreEqual("gamma 3", console.Text);
        }

        [TestMethod]
        public void EveryKeyIsSwallowedWhileOpen()
        {
            // The console returns true for every event, not only the ones it acts on.
            // Anything else would let a letter also select a weapon.
            //
            // Reopened for each key because some of them close the console - Escape
            // and Tilde do exactly that - and a closed console correctly ignores
            // everything.
            foreach (DoomKey key in Enum.GetValues(typeof(DoomKey)))
            {
                var console = new GameConsole();
                console.Open();

                Assert.IsTrue(console.Handle(Key(key)), "the console consumed " + key);
            }
        }

        [TestMethod]
        public void TildeClosesIt()
        {
            var console = new GameConsole();
            console.Open();

            console.Handle(Key(DoomKey.Tilde));

            Assert.IsFalse(console.Active, "the same key that opened it closes it");
        }

        [TestMethod]
        public void EnterSubmitsWithoutClosing()
        {
            // Several commands in a row is the whole point of a console; closing on
            // every one would make it a one-shot text box.
            var console = new GameConsole();
            console.Open();

            Type(console, "help");
            console.Handle(Key(DoomKey.Enter));

            Assert.IsTrue(console.Submitted, "the line is submitted");
            Assert.IsTrue(console.Active, "and the console stays open");
        }

        [TestMethod]
        public void RememberClearsTheLineAndTheHistoryWalksIt()
        {
            var console = new GameConsole();
            console.Open();

            Type(console, "gamma 3");
            console.Handle(Key(DoomKey.Enter));
            console.Remember("gamma 3");

            Assert.AreEqual("", console.Text, "the line is cleared after running");

            Type(console, "volume sfx 5");
            console.Handle(Key(DoomKey.Enter));
            console.Remember("volume sfx 5");

            console.Handle(Key(DoomKey.Up));
            Assert.AreEqual("volume sfx 5", console.Text, "up brings back the last command");

            console.Handle(Key(DoomKey.Up));
            Assert.AreEqual("gamma 3", console.Text, "and up again the one before it");

            console.Handle(Key(DoomKey.Down));
            Assert.AreEqual("volume sfx 5", console.Text, "down walks forward again");

            console.Handle(Key(DoomKey.Down));
            Assert.AreEqual("", console.Text, "past the newest is the empty line, not a repeat");
        }

        [TestMethod]
        public void RepeatedCommandsAreNotStoredTwice()
        {
            var console = new GameConsole();
            console.Open();

            console.Remember("gamma 3");
            console.Remember("gamma 3");

            console.Handle(Key(DoomKey.Up));
            Assert.AreEqual("gamma 3", console.Text);

            console.Handle(Key(DoomKey.Up));
            Assert.AreEqual("gamma 3", console.Text, "the second press does not go nowhere");
        }

        [TestMethod]
        public void TabCompletesASharedPrefix()
        {
            // Completing to the first match would be a guess; the longest common
            // prefix is the part that is certainly right.
            //
            // The two names share "screen" and differ after it, which is the case
            // that makes the difference visible.
            var console = new GameConsole();
            console.Open();

            console.AddCompletion("screensize");
            console.AddCompletion("screenwidth");
            console.AddCompletion("gamma");

            Type(console, "scr");
            console.Handle(Key(DoomKey.Tab));

            Assert.AreEqual("screen", console.Text, "both candidates share \"screen\"");

            Type(console, "size");
            console.Handle(Key(DoomKey.Tab));

            Assert.AreEqual("screensize ", console.Text,
                "a single match completes fully and adds a space");
        }

        [TestMethod]
        public void BackspaceRemovesTheLastCharacter()
        {
            var console = new GameConsole();
            console.Open();

            Type(console, "abc");
            console.Handle(Key(DoomKey.Backspace));

            Assert.AreEqual("ab", console.Text);
        }

        [TestMethod]
        public void TheLineIsCapped()
        {
            var console = new GameConsole();
            console.Open();

            Type(console, new string('x', GameConsole.MaxInput + 50));

            Assert.AreEqual(GameConsole.MaxInput, console.Text.Length,
                "the input line has a limit, so a stuck key cannot grow it forever");
        }

        [TestMethod]
        public void TheScrollbackIsBounded()
        {
            var console = new GameConsole();

            for (var i = 0; i < GameConsole.MaxLines + 100; i++)
            {
                console.Print("line " + i);
            }

            Assert.AreEqual(GameConsole.MaxLines, console.LineCount,
                "the scrollback does not grow without limit");

            // And the newest line is the one that survived, not the oldest.
            Assert.AreEqual("line " + (GameConsole.MaxLines + 99),
                console.Lines[console.LineCount - 1].Text);
        }

        [TestMethod]
        public void MultiLineOutputIsSplit()
        {
            var console = new GameConsole();

            console.Print("one\ntwo\nthree");

            Assert.AreEqual(3, console.LineCount, "each line is its own row");
        }

        [TestMethod]
        public void ScrollingBackClamps()
        {
            var console = new GameConsole();

            for (var i = 0; i < 5; i++)
            {
                console.Print("line " + i);
            }

            console.Open();

            for (var i = 0; i < 50; i++)
            {
                console.Handle(Key(DoomKey.PageUp));
            }

            Assert.IsTrue(console.ScrollOffset <= console.LineCount,
                "the scroll offset cannot pass the oldest line (" + console.ScrollOffset + ")");

            console.Handle(Key(DoomKey.PageDown));

            Assert.IsTrue(console.ScrollOffset >= 0, "and cannot go negative");
        }

        [TestMethod]
        public void PrintingFollowsTheOutputAgain()
        {
            var console = new GameConsole();

            for (var i = 0; i < 40; i++)
            {
                console.Print("line " + i);
            }

            console.Open();

            console.Handle(Key(DoomKey.PageUp));
            Assert.IsTrue(console.ScrollOffset > 0, "scrolled back");

            console.Print("new");

            Assert.AreEqual(0, console.ScrollOffset,
                "new output puts the view back on the newest line");
        }

        // ------------------------------------------------------------ commands

        private static ConsoleCommands Table(Action<ConsoleCall> run, out int calls)
        {
            var counter = 0;
            var commands = new ConsoleCommands();

            commands.Register("test", "test <arg>", "A test command.",
                call =>
                {
                    counter++;
                    run(call);
                });

            calls = counter;
            return commands;
        }

        [TestMethod]
        public void ACommandRunsAndGetsItsArguments()
        {
            string seen = null;
            string raw = null;
            var commands = new ConsoleCommands();

            commands.Register("say", "say <text>", "Repeat.",
                call =>
                {
                    seen = string.Join("|", call.Args);
                    raw = call.Raw;
                });

            var console = new GameConsole();
            Assert.IsTrue(commands.Execute("say hello   world", console), "the command ran");

            Assert.AreEqual("hello|world", seen, "arguments are split on runs of spaces");
            Assert.AreEqual("hello   world", raw, "the raw tail keeps the spacing");
        }

        [TestMethod]
        public void AnUnknownCommandIsReportedNotThrown()
        {
            // A typo must not take the game with it.
            var commands = new ConsoleCommands();
            var console = new GameConsole();

            Assert.IsFalse(commands.Execute("nosuchthing", console), "nothing ran");

            Assert.AreEqual(1, console.LineCount);
            Assert.AreEqual(ConsoleLineKind.Error, console.Lines[0].Kind);
            StringAssert.Contains(console.Lines[0].Text, "Unknown command");
        }

        [TestMethod]
        public void AThrowingCommandIsCaught()
        {
            var commands = new ConsoleCommands();
            var console = new GameConsole();

            commands.Register("boom", "boom", "Throws.",
                call => { throw new InvalidOperationException("on purpose"); });

            Assert.IsTrue(commands.Execute("boom", console), "the command was found");

            Assert.AreEqual(ConsoleLineKind.Error, console.Lines[0].Kind,
                "the failure is reported as an error line");
        }

        [TestMethod]
        public void CommandNamesAreCaseInsensitive()
        {
            var ran = false;
            var commands = new ConsoleCommands();

            commands.Register("gamma", "gamma", "Brightness.", call => ran = true);

            commands.Execute("GAMMA", new GameConsole());

            Assert.IsTrue(ran, "GAMMA is the same command as gamma");
        }

        [TestMethod]
        public void AnEmptyLineRunsNothing()
        {
            var commands = new ConsoleCommands();
            var console = new GameConsole();

            Assert.IsFalse(commands.Execute("", console));
            Assert.IsFalse(commands.Execute("   ", console));

            Assert.AreEqual(0, console.LineCount, "and prints nothing");
        }

        [TestMethod]
        public void ToggleArgumentsAcceptWords()
        {
            // A console that only takes "1" and "0" is a console that answers
            // "expected on or off" to the word "on".
            bool value;

            Assert.IsTrue(ConsoleCall.TryParseToggle("on", out value));
            Assert.IsTrue(value);

            Assert.IsTrue(ConsoleCall.TryParseToggle("OFF", out value));
            Assert.IsFalse(value);

            Assert.IsTrue(ConsoleCall.TryParseToggle("1", out value));
            Assert.IsTrue(value);

            Assert.IsFalse(ConsoleCall.TryParseToggle("maybe", out value));
        }

        [TestMethod]
        public void HelpListsEverythingAndOneCommand()
        {
            var commands = new ConsoleCommands();
            var console = new GameConsole();

            commands.Register("alpha", "alpha", "First.", call => { });
            commands.Register("beta", "beta", "Second.", call => { });

            commands.ShowHelp(null, console);

            Assert.IsTrue(console.LineCount >= 3, "a heading and a line per command");

            console.Clear();
            commands.ShowHelp("beta", console);

            Assert.AreEqual(2, console.LineCount, "usage and description");
            StringAssert.Contains(console.Lines[0].Text, "beta");
        }
    }
}
