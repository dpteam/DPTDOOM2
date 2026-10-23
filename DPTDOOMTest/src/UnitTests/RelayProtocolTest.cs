using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM.Net;
using DPTDOOM.Shared;

namespace DPTDOOMTest.UnitTests
{
    /// <summary>
    /// The relay's shared pieces. None of this touches a socket: what is checked
    /// here is the part that two processes have to agree on, and that a person
    /// has to be able to misread safely.
    ///
    /// The session id is the one value a player copies by hand, so a parsing bug
    /// shows up as "the host is waiting, the guest says no such session" with no
    /// hint as to why. Round-tripping it must be exact, and anything that is not
    /// exactly 32 hex characters must be rejected rather than half-parsed.
    /// </summary>
    [TestClass]
    public class RelayProtocolTest
    {
        [TestMethod]
        public void SessionIdRoundTrips()
        {
            var id = new byte[16];

            for (var i = 0; i < id.Length; i++)
            {
                id[i] = (byte)(i * 17 + 3);
            }

            var hex = MasterProtocol.IdToHex(id);
            Assert.AreEqual(32, hex.Length);

            var back = MasterProtocol.IdFromHex(hex);
            Assert.IsNotNull(back);

            for (var i = 0; i < id.Length; i++)
            {
                Assert.AreEqual(id[i], back[i]);
            }
        }

        [TestMethod]
        public void SessionIdIsLowercaseHex()
        {
            var id = new byte[16];
            id[0] = 0xAB;
            id[15] = 0x0F;

            var hex = MasterProtocol.IdToHex(id);

            Assert.AreEqual("ab" + new string('0', 28) + "0f", hex);
        }

        [TestMethod]
        public void SessionIdParsingIsCaseInsensitive()
        {
            var upper = MasterProtocol.IdFromHex("AB000000000000000000000000000000");
            var lower = MasterProtocol.IdFromHex("ab000000000000000000000000000000");

            Assert.IsNotNull(upper);
            Assert.IsNotNull(lower);
            Assert.AreEqual(upper[0], lower[0]);
        }

        [TestMethod]
        public void MalformedSessionIdsAreRejected()
        {
            Assert.IsNull(MasterProtocol.IdFromHex(null));
            Assert.IsNull(MasterProtocol.IdFromHex(""));
            Assert.IsNull(MasterProtocol.IdFromHex("abcd"));

            // 31 characters: one short.
            Assert.IsNull(MasterProtocol.IdFromHex("0000000000000000000000000000000"));

            // 33 characters: one long.
            Assert.IsNull(MasterProtocol.IdFromHex("000000000000000000000000000000000"));

            // Right length, not hex.
            Assert.IsNull(MasterProtocol.IdFromHex("zz000000000000000000000000000000"));
        }

        [TestMethod]
        public void RelayRequestLengthsAreEnforced()
        {
            var header = MasterProtocol.Header(MasterProtocol.OpRelayHost, MasterProtocol.AuthSize, 0);
            Assert.AreEqual(MasterProtocol.AuthSize, MasterProtocol.RequestLength(header));

            header = MasterProtocol.Header(MasterProtocol.OpRelayHost, 16, 0);
            Assert.AreEqual(-1, MasterProtocol.RequestLength(header));

            header = MasterProtocol.Header(MasterProtocol.OpRelayJoin, 16, 0);
            Assert.AreEqual(16, MasterProtocol.RequestLength(header));

            header = MasterProtocol.Header(MasterProtocol.OpRelayJoin, MasterProtocol.AuthSize, 0);
            Assert.AreEqual(-1, MasterProtocol.RequestLength(header));
        }

        [TestMethod]
        public void MasterEndpointParsing()
        {
            string host;
            int port;

            Assert.IsTrue(NetEndpoint.TryParse("example.org", 25135, out host, out port));
            Assert.AreEqual("example.org", host);
            Assert.AreEqual(25135, port);

            Assert.IsTrue(NetEndpoint.TryParse("example.org:30000", 25135, out host, out port));
            Assert.AreEqual("example.org", host);
            Assert.AreEqual(30000, port);

            Assert.IsTrue(NetEndpoint.TryParse("127.0.0.1:1", 25135, out host, out port));
            Assert.AreEqual("127.0.0.1", host);
            Assert.AreEqual(1, port);

            Assert.IsFalse(NetEndpoint.TryParse("", 25135, out host, out port));
            Assert.IsFalse(NetEndpoint.TryParse("   ", 25135, out host, out port));
            Assert.IsFalse(NetEndpoint.TryParse("host:0", 25135, out host, out port));
            Assert.IsFalse(NetEndpoint.TryParse("host:70000", 25135, out host, out port));
            Assert.IsFalse(NetEndpoint.TryParse("host:abc", 25135, out host, out port));
            Assert.IsFalse(NetEndpoint.TryParse(":25135", 25135, out host, out port));
        }
    }
}
