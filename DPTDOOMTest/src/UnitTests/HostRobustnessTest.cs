using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM.Net;

namespace DPTDOOMTest.UnitTests
{
    /// <summary>
    /// A hosting session must survive whatever a stranger connects and sends.
    ///
    /// This is not hypothetical. Before this test existed, sending an HTTP request
    /// to a hosting game made the host exit with "Network: guest handshake failed:
    /// Bad frame magic." - one stray connection from a port scanner, a browser, or
    /// a client built from a different version killed the session for every player
    /// already in it.
    ///
    /// The rule the tests pin: anything a *remote* peer can trigger must not end a
    /// *local* game. The listener keeps accepting; the peer gets closed.
    /// </summary>
    [TestClass]
    public class HostRobustnessTest
    {
        private const int Port = 5401;

        /// <summary>
        /// Connects, sends whatever a scanner or a wrong client would send, and
        /// closes. Returns once the host has had a moment to react.
        /// </summary>
        private static void Poke(int port, string payload)
        {
            using (var client = new TcpClient())
            {
                client.Connect("127.0.0.1", port);

                var stream = client.GetStream();
                var bytes = Encoding.ASCII.GetBytes(payload);

                stream.Write(bytes, 0, bytes.Length);
                stream.Flush();

                Thread.Sleep(150);
            }

            Thread.Sleep(150);
        }

        [TestMethod]
        public void HttpRequestDoesNotKillTheSession()
        {
            using (var host = NetSession.StartHost(Port, 2, "hostname", new SessionSettings(), 0))
            {
                Poke(Port, "GET / HTTP/1.1\r\nHost: example\r\n\r\n");

                Assert.IsFalse(host.Faulted,
                    "an HTTP request from a stranger must not fault a hosting session");

                // And the session must still work afterwards, not merely report
                // itself as healthy.
                using (var guest = NetSession.Join("127.0.0.1", Port, "guestname", 5000))
                {
                    var deadline = Stopwatch.StartNew();

                    while (host.PlayerCount < 2 && deadline.ElapsedMilliseconds < 5000)
                    {
                        Thread.Sleep(20);
                    }

                    Assert.AreEqual(2, host.PlayerCount,
                        "a real guest can still join after the stray connection");
                }
            }
        }

        [TestMethod]
        public void GarbageAndTruncatedFramesDoNotKillTheSession()
        {
            using (var host = NetSession.StartHost(Port + 1, 2, "hostname", new SessionSettings(), 0))
            {
                // No magic at all.
                Poke(Port + 1, "hello there, this is not a DPTN frame");

                // Correct magic, then a truncated header: the peer vanished
                // mid-frame, which is what a killed process looks like on the wire.
                Poke(Port + 1, "DPTN");

                // Correct magic and version, then nothing.
                Poke(Port + 1, "DPTN\x01\x00\x01\x00\x00\x00\x00\x00\x00\x00\x00\x00");

                Assert.IsFalse(host.Faulted,
                    "garbage, truncated frames and a bare connection must all be survivable");
            }
        }

        [TestMethod]
        public void AConnectionThatSaysNothingDoesNotKillTheSession()
        {
            using (var host = NetSession.StartHost(Port + 2, 2, "hostname", new SessionSettings(), 0))
            {
                // Connect and close without a byte: what a port scan does.
                using (var client = new TcpClient())
                {
                    client.Connect("127.0.0.1", Port + 2);
                }

                Thread.Sleep(300);

                Assert.IsFalse(host.Faulted,
                    "a port scan must not fault a hosting session");
            }
        }
    }
}
