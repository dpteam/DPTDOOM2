using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM.Net;

namespace DPTDOOMTest.UnitTests
{
    /// <summary>
    /// The host's ban list: what a rule means, and what it refuses to guess.
    ///
    /// Written against the parser and the matcher rather than through a session,
    /// because the interesting cases are the ones a live session cannot reach on
    /// demand: an expired rule, a rule for the other address family, a nickname with a
    /// wildcard. A ban that silently stops matching is worse than no ban at all - the
    /// host believes the troublemaker is gone and stops looking.
    /// </summary>
    [TestClass]
    public class BanListTest
    {
        private static readonly DateTime Now = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

        private static BanList Parse(params string[] lines)
        {
            var path = Path.Combine(
                Path.GetTempPath(), "dptdoom-bans-" + Guid.NewGuid().ToString("N") + ".txt");

            try
            {
                File.WriteAllLines(path, lines);

                var warnings = new List<string>();
                var list = BanList.Load(path, warnings);

                Assert.AreEqual(0, warnings.Count,
                    "the file parsed without warnings: " + string.Join("; ", warnings.ToArray()));

                return list;
            }
            finally
            {
                try { File.Delete(path); } catch (IOException) { }
            }
        }

        private static IPAddress Ip(string text)
        {
            return IPAddress.Parse(text);
        }

        // ------------------------------------------------------------ addresses

        [TestMethod]
        public void OneAddressIsBanned()
        {
            var list = Parse("203.0.113.7");

            Assert.IsTrue(list.IsAddressBanned(Ip("203.0.113.7"), Now));
            Assert.IsFalse(list.IsAddressBanned(Ip("203.0.113.8"), Now));
        }

        [TestMethod]
        public void ASubnetIsBanned()
        {
            // The reason subnets exist at all: a troublemaker with an address pool.
            var list = Parse("203.0.113.0/24");

            Assert.IsTrue(list.IsAddressBanned(Ip("203.0.113.1"), Now));
            Assert.IsTrue(list.IsAddressBanned(Ip("203.0.113.254"), Now));
            Assert.IsFalse(list.IsAddressBanned(Ip("203.0.114.1"), Now),
                "the next subnet over is a different network");
        }

        [TestMethod]
        public void APrefixThatIsNotAWholeByteWorks()
        {
            // 20 bits cuts in the middle of the third byte, which is where a mask that is
            // only handled per byte quietly matches everything.
            var list = Parse("203.0.112.0/20");

            Assert.IsTrue(list.IsAddressBanned(Ip("203.0.112.1"), Now));
            Assert.IsTrue(list.IsAddressBanned(Ip("203.0.127.254"), Now));
            Assert.IsFalse(list.IsAddressBanned(Ip("203.0.128.1"), Now));
            Assert.IsFalse(list.IsAddressBanned(Ip("203.0.111.1"), Now));
        }

        [TestMethod]
        public void EverythingCanBeBanned()
        {
            var list = Parse("0.0.0.0/0");

            Assert.IsTrue(list.IsAddressBanned(Ip("127.0.0.1"), Now));
            Assert.IsTrue(list.IsAddressBanned(Ip("203.0.113.7"), Now));
        }

        [TestMethod]
        public void IPv6IsBannedToo()
        {
            // Not decoration: a Windows session on a LAN connects over IPv6 when both
            // ends have it, so an IPv4-only ban list is a hole rather than a limit.
            var list = Parse("2001:db8::/32");

            Assert.IsTrue(list.IsAddressBanned(Ip("2001:db8::1"), Now));
            Assert.IsTrue(list.IsAddressBanned(Ip("2001:db8:ffff::9"), Now));
            Assert.IsFalse(list.IsAddressBanned(Ip("2001:db9::1"), Now));
        }

        [TestMethod]
        public void AnIPv4RuleDoesNotMatchAnIPv6Address()
        {
            // Compared as bytes, so 0.0.0.0/0 must not swallow ::1 - the families are
            // different lengths and comparing them would be nonsense.
            var list = Parse("0.0.0.0/0");

            Assert.IsTrue(list.IsAddressBanned(Ip("127.0.0.1"), Now));
            Assert.IsFalse(list.IsAddressBanned(Ip("::1"), Now),
                "an IPv4 rule says nothing about IPv6");
        }

        [TestMethod]
        public void AnUnknownAddressIsNeverBanned()
        {
            // A relayed guest has no address this process can see. Treating "unknown"
            // as a match would cut off every relayed player at once.
            var list = Parse("0.0.0.0/0");

            Assert.IsFalse(list.IsAddressBanned(null, Now));
        }

        // ------------------------------------------------------------ names

        [TestMethod]
        public void AnExactNicknameIsBanned()
        {
            var list = Parse("name troll");

            Assert.IsTrue(list.IsNameBanned("troll", Now));
            Assert.IsFalse(list.IsNameBanned("troll2", Now));
        }

        [TestMethod]
        public void ANicknameIsBannedRegardlessOfCase()
        {
            // "Troll" a second later is the same person, and nicks are typed freely.
            var list = Parse("name troll");

            Assert.IsTrue(list.IsNameBanned("Troll", Now));
            Assert.IsTrue(list.IsNameBanned("TROLL", Now));
        }

        [TestMethod]
        public void AWildcardMatches()
        {
            var list = Parse("name bad*");

            Assert.IsTrue(list.IsNameBanned("badguy", Now));
            Assert.IsTrue(list.IsNameBanned("bad", Now));
            Assert.IsTrue(list.IsNameBanned("BADGIRL", Now));
            Assert.IsFalse(list.IsNameBanned("notbad", Now));
        }

        [TestMethod]
        public void AWildcardInTheMiddleMatches()
        {
            var list = Parse("name *troll*");

            Assert.IsTrue(list.IsNameBanned("thetroll", Now));
            Assert.IsTrue(list.IsNameBanned("trolling", Now));
            Assert.IsTrue(list.IsNameBanned("a-troll-b", Now));
            Assert.IsFalse(list.IsNameBanned("trol", Now));
        }

        [TestMethod]
        public void SeveralWildcardsDoNotExplode()
        {
            // The backtracking matcher has one star to fall back to; a pattern with
            // several must still terminate.
            var list = Parse("name *a*b*c*");

            Assert.IsTrue(list.IsNameBanned("xaxbxcx", Now));
            Assert.IsFalse(list.IsNameBanned("xaxcxb", Now));
        }

        [TestMethod]
        public void AnEmptyNameIsNotBanned()
        {
            var list = Parse("name *");

            Assert.IsFalse(list.IsNameBanned("", Now));
            Assert.IsFalse(list.IsNameBanned(null, Now));
        }

        [TestMethod]
        public void ANameRuleNeverMatchesAnAddress()
        {
            var list = Parse("name troll");

            Assert.IsFalse(list.IsAddressBanned(Ip("127.0.0.1"), Now));
        }

        // ------------------------------------------------------------ expiry

        [TestMethod]
        public void AnExpiredRuleStopsMatching()
        {
            // A ban nobody dares to set is a ban that never gets used, so every rule can
            // carry an end.
            var list = Parse("203.0.113.7 until 2026-01-01T00:00:00Z");

            Assert.IsFalse(list.IsAddressBanned(Ip("203.0.113.7"), Now),
                "the ban ended in January and it is June");
        }

        [TestMethod]
        public void ARuleThatHasNotExpiredStillMatches()
        {
            var list = Parse("203.0.113.7 until 2027-01-01T00:00:00Z");

            Assert.IsTrue(list.IsAddressBanned(Ip("203.0.113.7"), Now));
        }

        [TestMethod]
        public void AnExpiredNameRuleStopsMatchingToo()
        {
            var list = Parse("name troll until 2026-01-01T00:00:00Z");

            Assert.IsFalse(list.IsNameBanned("troll", Now));
        }

        // ------------------------------------------------------------ file shape

        [TestMethod]
        public void CommentsAndBlankLinesAreIgnored()
        {
            var list = Parse(
                "# this is why the file exists",
                "",
                "203.0.113.7     # a trailing comment",
                "   ",
                "name troll");

            Assert.AreEqual(2, list.Count);
            Assert.IsTrue(list.IsAddressBanned(Ip("203.0.113.7"), Now), "the rule before the comment still works");
            Assert.IsTrue(list.IsNameBanned("troll", Now));
        }

        [TestMethod]
        public void AHashInsideANicknameIsNotAComment()
        {
            // Only a '#' that starts a token, so a nickname can contain one.
            var list = Parse("name a#b");

            Assert.IsTrue(list.IsNameBanned("a#b", Now));
        }

        [TestMethod]
        public void ABadLineIsAWarningNotAFailure()
        {
            // A typo on line nine must not stop line eight from working, and must not
            // stop the game from starting.
            var path = Path.Combine(
                Path.GetTempPath(), "dptdoom-bans-" + Guid.NewGuid().ToString("N") + ".txt");

            try
            {
                File.WriteAllLines(path, new[]
                {
                    "203.0.113.7",
                    "not-an-address",
                    "name troll",
                    "203.0.113.0/99",
                    "name"
                });

                var warnings = new List<string>();
                var list = BanList.Load(path, warnings);

                Assert.AreEqual(2, list.Count, "the two good rules loaded");
                Assert.AreEqual(3, warnings.Count, "and each bad line was reported");

                Assert.IsTrue(list.IsAddressBanned(Ip("203.0.113.7"), Now));
                Assert.IsTrue(list.IsNameBanned("troll", Now));
            }
            finally
            {
                try { File.Delete(path); } catch (IOException) { }
            }
        }

        [TestMethod]
        public void AMissingFileIsAWarningAndAnEmptyList()
        {
            var warnings = new List<string>();
            var list = BanList.Load(Path.Combine(Path.GetTempPath(), "no-such-ban-file.txt"), warnings);

            Assert.AreEqual(0, list.Count);
            Assert.AreEqual(1, warnings.Count);
            Assert.IsFalse(list.IsAddressBanned(Ip("127.0.0.1"), Now));
        }

        [TestMethod]
        public void NoPathIsNoListAtAll()
        {
            // The default for a host who never configured anything: silent, not a
            // warning about a file nobody asked for.
            var warnings = new List<string>();
            var list = BanList.Load(null, warnings);

            Assert.AreEqual(0, list.Count);
            Assert.AreEqual(0, warnings.Count);
        }

        [TestMethod]
        public void TheMatchedRuleCanBeQuoted()
        {
            // The log and the refusal both say which rule did it; "banned" alone leaves
            // the host hunting through a file.
            var list = Parse("203.0.113.0/24", "name troll");

            StringAssert.Contains(list.Explain(Ip("203.0.113.9"), null, Now), "203.0.113.0/24");
            StringAssert.Contains(list.Explain(null, "troll", Now), "name troll");
            Assert.AreEqual("", list.Explain(Ip("10.0.0.1"), "friend", Now));
        }
    }
}
