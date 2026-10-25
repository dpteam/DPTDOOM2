using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM.Scripting;

namespace DPTDOOMTest.UnitTests
{
    /// <summary>
    /// The two names a person is most likely to want changed, and that they are collected.
    ///
    /// `fn` and `Thing_` are spelling, not structure. They used to be literals in five files - the
    /// lexer, the library, the actor writer, the DECORATE converter and the content loader - so a
    /// rename meant a search and a hope. Now they live in <see cref="ScriptSyntax"/> and this test
    /// proves it: no other source file mentions them.
    ///
    /// That is the whole claim, and it is a claim about the codebase rather than about behaviour,
    /// so it is checked by reading the codebase. Behaviour is covered where it belongs: the lexer
    /// and the actor naming in the existing script tests.
    /// </summary>
    [TestClass]
    public class ScriptSyntaxTest
    {
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

            Assert.Fail("could not find the repository root");
            return null;
        }

        [TestMethod]
        public void TheSpellingIsWhereItIsExpected()
        {
            // If either of these fails, the rename is no longer a one-file edit, and the test that
            // follows would be checking the wrong thing.
            Assert.AreEqual("fn", ScriptSyntax.FunctionKeyword);
            Assert.AreEqual("Thing_", ScriptSyntax.ActorPrefix);
        }

        [TestMethod]
        public void AnActorNameIsThePrefixAndTheNumber()
        {
            Assert.AreEqual("Thing_3004", ScriptSyntax.ActorName(3004));
            Assert.AreEqual("fn Thing_3004() {", ScriptSyntax.ActorDefinitionHeader(3004));
        }

        [TestMethod]
        public void TheNameParsesBackToTheNumber()
        {
            int number;

            Assert.IsTrue(ScriptSyntax.TryParseActorName("Thing_3004", out number));
            Assert.AreEqual(3004, number);

            Assert.IsFalse(ScriptSyntax.TryParseActorName("Thing_", out number), "no number");
            Assert.IsFalse(ScriptSyntax.TryParseActorName("thing_3004", out number), "case matters");
            Assert.IsFalse(ScriptSyntax.TryParseActorName("Thing_3004x", out number), "trailing rubbish");
            Assert.IsFalse(ScriptSyntax.TryParseActorName(null, out number));
        }

        [TestMethod]
        public void NoOtherSourceFileHardCodesThem()
        {
            var offenders = new List<string>();

            foreach (var file in Directory.GetFiles(Path.Combine(Root(), "DPTDOOM", "src"),
                                                    "*.cs", SearchOption.AllDirectories))
            {
                if (Path.GetFileName(file) == "ScriptSyntax.cs")
                {
                    continue;
                }

                var text = File.ReadAllText(file);

                // The prefix in a string literal anywhere, or the keyword as a lexer case. Both
                // are the shapes a hard-coded name takes.
                if (Regex.IsMatch(text, "\"Thing_\""))
                {
                    offenders.Add(Relative(file) + ": \"Thing_\"");
                }

                if (Regex.IsMatch(text, "case\\s+\"fn\""))
                {
                    offenders.Add(Relative(file) + ": case \"fn\"");
                }
            }

            Assert.AreEqual(0, offenders.Count,
                "these files hard-code the spelling instead of using ScriptSyntax: " +
                string.Join("; ", offenders.ToArray()));
        }

        [TestMethod]
        public void DocumentationPointsAtThePlaceToChangeIt()
        {
            // A rename is only cheap if the next person can find where it happens. The docs page
            // has to name the file, or the knowledge lives only in this test.
            var page = Path.Combine(Root(), "docs", "SCRIPTS.md");

            Assert.IsTrue(File.Exists(page), "docs/SCRIPTS.md describes the language");

            var text = File.ReadAllText(page);

            StringAssert.Contains(text, "ScriptSyntax");
        }

        private static string Relative(string path)
        {
            var root = Root();

            return path.StartsWith(root, StringComparison.Ordinal)
                ? path.Substring(root.Length).TrimStart('\\', '/')
                : path;
        }
    }
}
