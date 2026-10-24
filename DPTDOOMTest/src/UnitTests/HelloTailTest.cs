using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM.Net;

namespace DPTDOOMTest.UnitTests
{
    /// <summary>
    /// The optional tail of a HELLO frame, which is where the resume token and the
    /// session password live.
    ///
    /// This is the most fragile part of the wire format. Both of those were added to a
    /// frame that used to carry nothing but the player's name, and the rule that keeps
    /// old builds working is entirely positional: whatever a host does not understand
    /// has to be behind something it does, and something it does not expect must not be
    /// read as something it does.
    ///
    /// The failure this guards against is not "the new build cannot talk to the old
    /// one". It is worse and quieter: a host reads password bytes as a resume token and
    /// refuses a legitimate reconnection, or a guest that sent nothing is treated as a
    /// guest that sent an empty password.
    /// </summary>
    [TestClass]
    public class HelloTailTest
    {
        private static byte[] Build(string name, byte flags, Action<BinaryWriter> extra)
        {
            using (var payload = new MemoryStream())
            {
                using (var writer = new BinaryWriter(payload))
                {
                    NetProtocol.WriteString(writer, name);

                    if (flags != 0)
                    {
                        writer.Write(flags);
                        extra(writer);
                    }

                    writer.Flush();
                }

                return payload.ToArray();
            }
        }

        /// <summary>
        /// The host's reading rules, copied here so the format is pinned by a test
        /// rather than by the one place that consumes it.
        /// </summary>
        private static void Parse(byte[] payload,
                                  out string name, out bool resume, out ulong token,
                                  out int player, out string password)
        {
            name = null;
            resume = false;
            token = 0;
            player = -1;
            password = null;

            using (var stream = new MemoryStream(payload))
            using (var reader = new BinaryReader(stream))
            {
                name = NetProtocol.ReadString(reader);

                if (stream.Position >= stream.Length)
                {
                    password = "";
                    return;
                }

                var flags = reader.ReadByte();

                if ((flags & NetProtocol.HelloResume) != 0 && stream.Length - stream.Position >= 9)
                {
                    resume = true;
                    token = reader.ReadUInt64();
                    player = reader.ReadByte();
                }

                if ((flags & NetProtocol.HelloPassword) != 0 && stream.Position < stream.Length)
                {
                    password = NetProtocol.ReadPassword(reader);
                }

                if (password == null)
                {
                    password = "";
                }
            }
        }

        [TestMethod]
        public void ANameOnlyHelloStillReads()
        {
            // The oldest build there is: nothing but a name. It must parse, with no
            // resume and no password.
            var payload = Build("player", 0, writer => { });

            Parse(payload, out var name, out var resume, out var token, out var player, out var password);

            Assert.AreEqual("player", name);
            Assert.IsFalse(resume);
            Assert.AreEqual(0UL, token);
            Assert.AreEqual(-1, player);
            Assert.AreEqual("", password);
        }

        [TestMethod]
        public void AResumeHelloStillReads()
        {
            // A build with reconnection but no password: the token has to be exactly
            // where it always was.
            var payload = Build("player", NetProtocol.HelloResume, writer =>
            {
                writer.Write(0x1122334455667788UL);
                writer.Write((byte)2);
            });

            Parse(payload, out _, out var resume, out var token, out var player, out var password);

            Assert.IsTrue(resume);
            Assert.AreEqual(0x1122334455667788UL, token);
            Assert.AreEqual(2, player);
            Assert.AreEqual("", password, "no password block means no password");
        }

        [TestMethod]
        public void APasswordHelloReads()
        {
            var payload = Build("player", NetProtocol.HelloPassword, writer =>
            {
                NetProtocol.WritePassword(writer, "hunter2");
            });

            Parse(payload, out _, out var resume, out _, out _, out var password);

            Assert.IsFalse(resume, "a password is not a resume");
            Assert.AreEqual("hunter2", password);
        }

        [TestMethod]
        public void BothBlocksTogetherReadInOrder()
        {
            // The order is the whole contract: resume first, password behind it. Swap
            // them and the host reads password bytes as a token, which is a refusal on
            // a session that was working a moment ago.
            var payload = Build("player", NetProtocol.HelloResume | NetProtocol.HelloPassword, writer =>
            {
                writer.Write(0x0102030405060708UL);
                writer.Write((byte)1);
                NetProtocol.WritePassword(writer, "secret");
            });

            Parse(payload, out _, out var resume, out var token, out var player, out var password);

            Assert.IsTrue(resume);
            Assert.AreEqual(0x0102030405060708UL, token);
            Assert.AreEqual(1, player);
            Assert.AreEqual("secret", password);
        }

        [TestMethod]
        public void AnUnknownFlagBitIsIgnored()
        {
            // Whatever gets added next must not break this build: reading stops at the
            // blank, and the known blocks are still found.
            var payload = Build("player", NetProtocol.HelloPassword | 0x80, writer =>
            {
                NetProtocol.WritePassword(writer, "hunter2");
                writer.Write((byte)0xFF);
            });

            Parse(payload, out _, out var resume, out _, out _, out var password);

            Assert.IsFalse(resume);
            Assert.AreEqual("hunter2", password);
        }

        [TestMethod]
        public void APasswordMayBeAsLongAsTheLimit()
        {
            var longest = new string('x', ServerPassword.MaxLength);
            var payload = Build("player", NetProtocol.HelloPassword, writer =>
            {
                NetProtocol.WritePassword(writer, longest);
            });

            Parse(payload, out _, out _, out _, out _, out var password);

            Assert.AreEqual(longest, password);
        }

        [TestMethod]
        public void ATooLongPasswordIsClippedAtACharacterBoundary()
        {
            // The limit is in bytes, so a Cyrillic password has to be clipped at half
            // the character count - and clipped cleanly. Cutting a multi-byte character
            // in half would produce a password that is neither what the host typed nor a
            // prefix of it, and nobody would ever work out why it does not match.
            var cyrillic = new string('ж', ServerPassword.MaxLength);
            var payload = Build("player", NetProtocol.HelloPassword, writer =>
            {
                NetProtocol.WritePassword(writer, cyrillic);
            });

            Parse(payload, out _, out _, out _, out _, out var password);

            Assert.AreEqual(ServerPassword.MaxLength / 2, password.Length, "32 Cyrillic letters fit in 64 bytes");
            Assert.AreEqual(cyrillic.Substring(0, password.Length), password, "and it is a real prefix");
        }

        [TestMethod]
        public void ASurrogatePairIsNotSplit()
        {
            // Four bytes per character: an odd split would leave half a character, which
            // is not a string anybody typed.
            var emoji = new string('\uD83D', 1) + new string('\uDE00', 1);
            var text = emoji + new string('x', ServerPassword.MaxLength);

            var payload = Build("player", NetProtocol.HelloPassword, writer =>
            {
                NetProtocol.WritePassword(writer, text);
            });

            Parse(payload, out _, out _, out _, out _, out var password);

            Assert.AreEqual(emoji, password.Substring(0, 2), "the pair survived whole");
            Assert.AreEqual(2 + (ServerPassword.MaxLength - 4), password.Length,
                "the emoji takes four of the sixty-four bytes, the rest is x");
        }

        [TestMethod]
        public void APasswordIsNotTrimmedOrDefaulted()
        {
            // Names are trimmed and defaulted to "player"; a password must be neither,
            // or the host silently sets a different password from the one typed.
            var payload = Build("player", NetProtocol.HelloPassword, writer =>
            {
                NetProtocol.WritePassword(writer, "  spaced  ");
            });

            Parse(payload, out _, out _, out _, out _, out var password);

            Assert.AreEqual("  spaced  ", password);
        }

        [TestMethod]
        public void AnEmptyPasswordBlockMeansAnEmptyPassword()
        {
            // Distinct from no block at all only in intent; both mean "this guest
            // offered nothing", and a protected host refuses both.
            var payload = Build("player", NetProtocol.HelloPassword, writer =>
            {
                NetProtocol.WritePassword(writer, "");
            });

            Parse(payload, out _, out _, out _, out _, out var password);

            Assert.AreEqual("", password);
            Assert.IsFalse(ServerPassword.Matches("hunter2", password));
        }
    }
}
