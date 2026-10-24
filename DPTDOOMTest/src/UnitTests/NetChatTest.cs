using System;
using System.Diagnostics;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM.Net;

namespace DPTDOOMTest.UnitTests
{
    /// <summary>
    /// Chat over a real session.
    ///
    /// Two NetSessions on loopback, because the interesting part is the path, not
    /// the message: a guest only talks to the host, and only the host knows who the
    /// other guests are. A three-player session would silently lose messages
    /// without the host's forward, and nothing about the two-player case would show
    /// that - which is exactly why the forward exists and why it is worth a test
    /// that fails loudly if the host stops relaying.
    ///
    /// These tests exercise the transport directly instead of going through the
    /// game: chat must not need a world, a level or a tic.
    /// </summary>
    [TestClass]
    public class NetChatTest
    {
        private const int Port = 5351;

        private static bool WaitFor(NetSession session, TimeSpan timeout, out string text)
        {
            var watch = Stopwatch.StartNew();

            while (watch.Elapsed < timeout)
            {
                if (session.TryTakeChat(out text))
                {
                    return true;
                }

                Thread.Sleep(20);
            }

            text = null;
            return false;
        }

        [TestMethod]
        public void GuestMessageReachesTheHost()
        {
            using (var host = NetSession.StartHost(Port, 2, "hostname", new SessionSettings(), 0))
            {
                using (var guest = NetSession.Join("127.0.0.1", Port, "guestname", 5000))
                {
                    // The host has to know the guest joined before chat is useful;
                    // waits for the handshake rather than sleeping a fixed time.
                    var deadline = Stopwatch.StartNew();

                    while (host.PlayerCount < 2 && deadline.ElapsedMilliseconds < 5000)
                    {
                        Thread.Sleep(20);
                    }

                    Assert.AreEqual(2, host.PlayerCount, "the guest joined");

                    guest.SendChat(guest.LocalPlayer, "hello host");

                    string text;
                    Assert.IsTrue(WaitFor(host, TimeSpan.FromSeconds(5), out text),
                        "the host received the guest's message");
                    Assert.AreEqual("guestname: hello host", text);
                }
            }
        }

        [TestMethod]
        public void HostMessageReachesTheGuest()
        {
            using (var host = NetSession.StartHost(Port + 1, 2, "hostname", new SessionSettings(), 0))
            {
                using (var guest = NetSession.Join("127.0.0.1", Port + 1, "guestname", 5000))
                {
                    var deadline = Stopwatch.StartNew();

                    while (host.PlayerCount < 2 && deadline.ElapsedMilliseconds < 5000)
                    {
                        Thread.Sleep(20);
                    }

                    host.SendChat(0, "hello guest");

                    string text;
                    Assert.IsTrue(WaitFor(guest, TimeSpan.FromSeconds(5), out text),
                        "the guest received the host's message");
                    Assert.AreEqual("hostname: hello guest", text);
                }
            }
        }

        [TestMethod]
        public void SenderDoesNotEchoToHimself()
        {
            // The sender shows their own line locally. If the transport echoed it
            // back, the host - which is both a sender and a relay - would print
            // every one of its own messages twice.
            using (var host = NetSession.StartHost(Port + 2, 2, "hostname", new SessionSettings(), 0))
            {
                using (var guest = NetSession.Join("127.0.0.1", Port + 2, "guestname", 5000))
                {
                    var deadline = Stopwatch.StartNew();

                    while (host.PlayerCount < 2 && deadline.ElapsedMilliseconds < 5000)
                    {
                        Thread.Sleep(20);
                    }

                    host.SendChat(0, "only once");
                    Thread.Sleep(400);

                    string text;
                    Assert.IsFalse(host.TryTakeChat(out text),
                        "the host must not receive its own message back");
                }
            }
        }

        [TestMethod]
        public void HostForwardsBetweenGuests()
        {
            // A guest only ever has a connection to the host, so without the
            // host's forward two guests never see each other's messages. The
            // two-player case cannot show this, which is why it needs its own test.
            using (var host = NetSession.StartHost(Port + 4, 3, "hostname", new SessionSettings(), 0))
            {
                using (var first = NetSession.Join("127.0.0.1", Port + 4, "first", 5000))
                {
                    var deadline = Stopwatch.StartNew();

                    while (host.PlayerCount < 2 && deadline.ElapsedMilliseconds < 5000)
                    {
                        Thread.Sleep(20);
                    }

                    using (var second = NetSession.Join("127.0.0.1", Port + 4, "second", 5000))
                    {
                        deadline.Restart();

                        while (host.PlayerCount < 3 && deadline.ElapsedMilliseconds < 5000)
                        {
                            Thread.Sleep(20);
                        }

                        Assert.AreEqual(3, host.PlayerCount, "both guests joined");

                        first.SendChat(first.LocalPlayer, "to the others");

                        string text;
                        Assert.IsTrue(WaitFor(second, TimeSpan.FromSeconds(5), out text),
                            "the second guest received a message from the first");
                        Assert.AreEqual("first: to the others", text);
                    }
                }
            }
        }

        [TestMethod]
        public void OverlongMessagesAreTruncatedNotDropped()
        {
            using (var host = NetSession.StartHost(Port + 3, 2, "hostname", new SessionSettings(), 0))
            {
                using (var guest = NetSession.Join("127.0.0.1", Port + 3, "guestname", 5000))
                {
                    var deadline = Stopwatch.StartNew();

                    while (host.PlayerCount < 2 && deadline.ElapsedMilliseconds < 5000)
                    {
                        Thread.Sleep(20);
                    }

                    guest.SendChat(guest.LocalPlayer, new string('x', 500));

                    string text;
                    Assert.IsTrue(WaitFor(host, TimeSpan.FromSeconds(5), out text),
                        "an overlong message still arrives");

                    // "guestname: " plus at most Chat.MaxLength characters.
                    Assert.IsTrue(text.Length <= "guestname: ".Length + DPTDOOM.Chat.MaxLength,
                        "the message was truncated to the framing limit, not forwarded whole");
                }
            }
        }
    }
}
