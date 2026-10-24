using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM.Net;

namespace DPTDOOMTest.UnitTests
{
    /// <summary>
    /// What the player is told when a connection fails.
    ///
    /// The screen draws with Doom's own font - no Cyrillic, nothing below 0x20 - so
    /// this is a short uppercase ASCII line, and it is the only thing a player has to
    /// act on. A wrong line here costs more than no line: they go and check their
    /// firewall while the real answer was "the session is full".
    ///
    /// That is exactly what happened. The host's own refusal ("Host refused the
    /// connection: session is full") contains the word "refused", and a substring
    /// check turned it into "REFUSED - IS THE HOST RUNNING?" - a diagnosis pointing at
    /// the one thing that was definitely working.
    /// </summary>
    [TestClass]
    public class NetFailureTextTest
    {
        [TestMethod]
        public void ARefusalFromTheHostKeepsItsReason()
        {
            var text = NetStartup.DescribeFailure(
                "192.168.0.5", 5029,
                new NetRefusalException("Host refused the connection: session is full"));

            StringAssert.Contains(text, "REFUSED", text);
            StringAssert.Contains(text, "SESSION IS FULL", text);

            Assert.IsFalse(text.Contains("IS THE HOST RUNNING"),
                "the host answered, so it is running: " + text);
        }

        [TestMethod]
        public void AnAbandonedSlotSaysSo()
        {
            // Three different problems, three different actions: a full session means
            // wait, a stale token means the session restarted, an abandoned slot means
            // start a new one. The player cannot tell them apart from "refused".
            var text = NetStartup.DescribeFailure(
                "10.0.0.1", 5029,
                new NetRefusalException(
                    "Host refused the connection: your slot was given up on; start a new session"));

            StringAssert.Contains(text, "GIVEN UP ON", text);
            StringAssert.Contains(text, "10.0.0.1:5029", text);
        }

        [TestMethod]
        public void AClosedPortIsStillBlamedOnTheHostNotRunning()
        {
            // The genuine case for that line, and it must survive: nothing answered at
            // all, so the question is worth asking.
            var socket = new System.Net.Sockets.SocketException(
                (int)System.Net.Sockets.SocketError.ConnectionRefused);

            var text = NetStartup.DescribeFailure("127.0.0.1", 5999, new Exception("wrapped", socket));

            StringAssert.Contains(text, "IS THE HOST RUNNING", text);
        }

        [TestMethod]
        public void ATimeoutPointsAtTheFirewall()
        {
            var text = NetStartup.DescribeFailure(
                "94.100.1.1", 5029, new Exception("The connection timed out."));

            StringAssert.Contains(text, "NO ANSWER", text);
            StringAssert.Contains(text, "94.100.1.1:5029", text);
        }

        [TestMethod]
        public void TheLineIsUppercaseAscii()
        {
            // The font has no lowercase Cyrillic and nothing below 0x20. A Russian
            // message here came out as a smear of stray punctuation, and the player saw
            // "1 -- 127.0.0.1:5999" and learned nothing.
            var text = NetStartup.DescribeFailure(
                "host.example", 5029,
                new NetRefusalException("Host refused the connection: session is full"));

            foreach (var c in text)
            {
                Assert.IsTrue(c >= 0x20 && c < 0x7F,
                    "character " + (int)c + " is outside the printable ASCII range in: " + text);
            }

            Assert.AreEqual(text.ToUpperInvariant(), text, "and it is uppercase: " + text);
        }
    }
}
