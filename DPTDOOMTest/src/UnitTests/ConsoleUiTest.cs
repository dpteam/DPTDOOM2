using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM;

namespace DPTDOOMTest.UnitTests
{
    /// <summary>
    /// The startup log's format.
    ///
    /// Checked here rather than on screen because the properties that matter are
    /// textual: a timestamp on every line, a fixed-width subsystem column, and - most
    /// importantly - that a redirected run is byte-for-byte what it always was. Sixteen
    /// scripts parse that output, and decoration leaking into it breaks a build rather
    /// than a game.
    ///
    /// The shape is a kernel log's: <c>[    0.412817] wad      : 1900 lumps</c>.
    /// </summary>
    [TestClass]
    public class ConsoleUiTest
    {
        private static string Capture(Action action, bool interactive)
        {
            var original = Console.Out;

            using (var writer = new StringWriter())
            {
                try
                {
                    ConsoleUi.ForceInteractive(interactive);
                    Console.SetOut(writer);
                    action();
                }
                finally
                {
                    Console.SetOut(original);
                    ConsoleUi.ForceInteractive(false);
                }

                return writer.ToString();
            }
        }

        [TestMethod]
        public void ARedirectedRunIsNotDecorated()
        {
            // The contract that keeps the scripts working: no timestamp, no column, no
            // colours, exactly the old shape.
            var text = Capture(() =>
            {
                ConsoleUi.Begin("flats");
                ConsoleUi.Ok("147");
            }, false);

            Assert.AreEqual("flats: OK (147)" + Environment.NewLine, text,
                "redirected output keeps the plain format");
        }

        [TestMethod]
        public void AnInteractiveRunGetsATimestampAndAColumn()
        {
            var text = Capture(() =>
            {
                ConsoleUi.Begin("flats");
                ConsoleUi.Ok("147");
            }, true);

            StringAssert.Contains(text, "] flats    : 147",
                "bracket, then the subsystem in a fixed-width column: " + text);
        }

        [TestMethod]
        public void TheTimestampIsSixDecimalsAndRightAligned()
        {
            var text = Capture(() => ConsoleUi.Line("wad", "doom2.wad"), true);

            var match = Regex.Match(text, @"\[\s*(\d+\.\d{6})\]");

            Assert.IsTrue(match.Success, "the timestamp has six decimals: " + text);
            Assert.AreEqual(12, match.Value.Length - 2,
                "and the number is padded to a fixed width so a column lines up");
        }

        [TestMethod]
        public void TheTimestampIsSeparatedFromTheSubsystem()
        {
            // Without a space after the bracket the line reads as one run-on token,
            // which is exactly how the first version came out.
            var text = Capture(() => ConsoleUi.Line("wad", "x"), true);

            Assert.IsTrue(text.Contains("] wad"),
                "there is a space between the bracket and the name: " + text);
        }

        [TestMethod]
        public void TheSubsystemColumnIsTheSameWidthForEveryName()
        {
            // A short name and a long one must put their colons in the same place, or
            // the column is only aligned for names that happen to match in length.
            var shortName = Capture(() => ConsoleUi.Line("wad", "x"), true);
            var longName = Capture(() => ConsoleUi.Line("dehacked", "x"), true);

            Assert.AreEqual(shortName.IndexOf(':'), longName.IndexOf(':'),
                "the colon lands in the same column for 'wad' and 'dehacked'");
        }

        [TestMethod]
        public void AFailureIsMarkedAndExplained()
        {
            var text = Capture(() =>
            {
                ConsoleUi.Begin("wad");
                ConsoleUi.Failed("file not found");
            }, true);

            StringAssert.Contains(text, "FAILED - file not found",
                "a failure says so and says why: " + text);
        }

        [TestMethod]
        public void OneStepPrintsOneLine()
        {
            // The line appears when the step ends, not when it starts. An earlier
            // version printed a provisional line and overwrote it, which fell apart the
            // moment a step printed a detail line partway through.
            var text = Capture(() =>
            {
                ConsoleUi.Begin("flats");
                ConsoleUi.Ok("147");
            }, true);

            var newlines = 0;
            foreach (var ch in text)
            {
                if (ch == '\n')
                {
                    newlines++;
                }
            }

            Assert.AreEqual(1, newlines, "one step, one line: " + text);
        }

        [TestMethod]
        public void AProvisionalLineIsNeverPrinted()
        {
            // Begin must print nothing at all on a console. If it does, every detail
            // line printed during the step lands on top of it.
            var text = Capture(() => ConsoleUi.Begin("flats"), true);

            Assert.AreEqual("", text, "Begin is silent on a console");
        }

        [TestMethod]
        public void ADetailLineKeepsTheStepsColumn()
        {
            var text = Capture(() =>
            {
                ConsoleUi.Begin("flats");
                ConsoleUi.Detail("skipping broken.png");
                ConsoleUi.Ok("147");
            }, true);

            var lines = text.Replace("\r\n", "\n").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

            Assert.AreEqual(2, lines.Length, "a detail adds one line: " + text);

            // The detail's timestamp column is blank, but its subsystem colon must land
            // in the same place as the step's, or the column is broken for exactly the
            // lines that are hardest to read.
            Assert.AreEqual(lines[0].IndexOf(':'), lines[1].IndexOf(':'),
                "the detail keeps the column: " + text);
        }

        [TestMethod]
        public void AWarningIsItsOwnLine()
        {
            var text = Capture(() => ConsoleUi.Warn("sound unavailable"), true);

            StringAssert.Contains(text, "] warn     : sound unavailable", text);
        }

        [TestMethod]
        public void TheTimestampGrowsWithTheRun()
        {
            // The number is time since the process started, not a counter: a later line
            // must carry a later time, or the format is decoration rather than
            // information.
            var first = Capture(() => ConsoleUi.Line("a", "first"), true);

            System.Threading.Thread.Sleep(20);

            var second = Capture(() => ConsoleUi.Line("a", "second"), true);

            var a = double.Parse(Regex.Match(first, @"\[\s*(\d+\.\d+)\]").Groups[1].Value,
                                 CultureInfo.InvariantCulture);
            var b = double.Parse(Regex.Match(second, @"\[\s*(\d+\.\d+)\]").Groups[1].Value,
                                 CultureInfo.InvariantCulture);

            Assert.IsTrue(b > a, "the second line is stamped later (" + a + " < " + b + ")");
        }
    }
}
