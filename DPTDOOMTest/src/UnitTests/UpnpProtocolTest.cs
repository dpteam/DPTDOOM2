using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DPTDOOM.Net;

namespace DPTDOOMTest.UnitTests
{
    /// <summary>
    /// The two text protocols UPnP forwarding is made of, tested without a router.
    ///
    /// A router cannot be put in a unit test; the bytes a router sends can. That is the
    /// division this file exists for: <see cref="UpnpPortMapper"/> moves packets and is
    /// checked by a live script, while everything that decides *what* those packets mean
    /// is here.
    ///
    /// The inputs are real-world shapes, not the tidy ones from the specification. UPnP
    /// in the field means namespace prefixes that vary per device, header names in
    /// different case, entities in text, self-closing tags on services that do not
    /// exist, and error codes nobody documents. A parser tested only against one router
    /// is a parser that works for that router.
    /// </summary>
    [TestClass]
    public class UpnpProtocolTest
    {
        // ------------------------------------------------------------ SSDP

        [TestMethod]
        public void TheSearchIsAWellFormedDatagram()
        {
            var search = UpnpProtocol.BuildSearch("upnp:rootdevice", 3);

            StringAssert.Contains(search, "M-SEARCH * HTTP/1.1\r\n");
            StringAssert.Contains(search, "HOST: 239.255.255.250:1900\r\n");
            StringAssert.Contains(search, "MAN: \"ssdp:discover\"\r\n");
            StringAssert.Contains(search, "MX: 3\r\n");
            StringAssert.Contains(search, "ST: upnp:rootdevice\r\n");

            // The trailing blank line is part of the message: without it the device waits
            // for more headers and never answers.
            Assert.IsTrue(search.EndsWith("\r\n\r\n"), "the datagram ends with a blank line");
        }

        [TestMethod]
        public void ALocationHeaderIsFoundWhateverItsCase()
        {
            // Case-insensitivity is not pedantry here: devices write LOCATION, Location
            // and location, and the specification says all three are the same header.
            var variants = new[]
            {
                "HTTP/1.1 200 OK\r\nLOCATION: http://192.168.1.1:5000/rootDesc.xml\r\n\r\n",
                "HTTP/1.1 200 OK\r\nLocation: http://192.168.1.1:5000/rootDesc.xml\r\n\r\n",
                "HTTP/1.1 200 OK\r\nlocation: http://192.168.1.1:5000/rootDesc.xml\r\n\r\n"
            };

            foreach (var reply in variants)
            {
                string location;

                Assert.IsTrue(UpnpProtocol.TryParseSearchReply(reply, out location), reply);
                Assert.AreEqual("http://192.168.1.1:5000/rootDesc.xml", location);
            }
        }

        [TestMethod]
        public void AReplyWithoutALocationIsRejected()
        {
            string location;

            Assert.IsFalse(UpnpProtocol.TryParseSearchReply(
                "HTTP/1.1 200 OK\r\nSERVER: Some/1.0 UPnP/1.0\r\n\r\n", out location));

            Assert.IsFalse(UpnpProtocol.TryParseSearchReply("", out location));
            Assert.IsFalse(UpnpProtocol.TryParseSearchReply(null, out location));
        }

        [TestMethod]
        public void JunkThatIsNotAnSsdpReplyIsRejected()
        {
            // The multicast group is public, so anything on the network can send to it.
            string location;

            Assert.IsFalse(UpnpProtocol.TryParseSearchReply("hello there", out location));
            Assert.IsFalse(UpnpProtocol.TryParseSearchReply("LOCATION:", out location),
                "an empty location is not a location");
        }

        // ------------------------------------------------------------ description

        [TestMethod]
        public void TheControlUrlIsFoundInADeviceWithANamespacePrefix()
        {
            // The common shape: one prefix on everything, declared once.
            var xml =
                "<?xml version=\"1.0\"?>\n" +
                "<root xmlns=\"urn:schemas-upnp-org:device-1-0\">\n" +
                "  <device>\n" +
                "    <deviceList><device>\n" +
                "      <serviceList>\n" +
                "        <service>\n" +
                "          <serviceType>urn:schemas-upnp-org:service:WANIPConnection:1</serviceType>\n" +
                "          <controlURL>/ctl/IPConn</controlURL>\n" +
                "        </service>\n" +
                "      </serviceList>\n" +
                "    </device></deviceList>\n" +
                "  </device>\n" +
                "</root>";

            string type;
            string url;

            Assert.IsTrue(UpnpProtocol.TryFindControlUrl(xml, out type, out url));
            Assert.AreEqual("urn:schemas-upnp-org:service:WANIPConnection:1", type);
            Assert.AreEqual("/ctl/IPConn", url);
        }

        [TestMethod]
        public void APrefixedElementIsFound()
        {
            // Devices that emit "u:serviceType" rather than a default namespace. Matching
            // on the qualified name would fail here, which is why the parser matches the
            // local name.
            var xml =
                "<u:root><u:device><u:serviceList><u:service>\n" +
                "  <u:serviceType>urn:schemas-upnp-org:service:WANIPConnection:1</u:serviceType>\n" +
                "  <u:controlURL>/upnp/control/WANIPConn1</u:controlURL>\n" +
                "</u:service></u:serviceList></u:device></u:root>";

            string type;
            string url;

            Assert.IsTrue(UpnpProtocol.TryFindControlUrl(xml, out type, out url));
            Assert.AreEqual("/upnp/control/WANIPConn1", url);
        }

        [TestMethod]
        public void ADeviceThatOnlySpeaksPppIsAccepted()
        {
            // DSL routers that own the PPPoE session expose WANPPPConnection instead, and
            // refusing them would be refusing half the routers in Europe.
            var xml =
                "<root><device><serviceList><service>\n" +
                "  <serviceType>urn:schemas-upnp-org:service:WANPPPConnection:1</serviceType>\n" +
                "  <controlURL>/ctl/PPPConn</controlURL>\n" +
                "</service></serviceList></device></root>";

            string type;
            string url;

            Assert.IsTrue(UpnpProtocol.TryFindControlUrl(xml, out type, out url));
            Assert.AreEqual("urn:schemas-upnp-org:service:WANPPPConnection:1", type);
            Assert.AreEqual("/ctl/PPPConn", url);
        }

        [TestMethod]
        public void AServiceThatCannotForwardIsSkipped()
        {
            // A device answers SSDP with everything it has: content directories, printers,
            // connection managers. Only the one that can forward a port is wanted.
            var xml =
                "<root><device><serviceList>\n" +
                "  <service>\n" +
                "    <serviceType>urn:schemas-upnp-org:service:ContentDirectory:1</serviceType>\n" +
                "    <controlURL>/ctl/CD</controlURL>\n" +
                "  </service>\n" +
                "  <service>\n" +
                "    <serviceType>urn:schemas-upnp-org:service:WANIPConnection:1</serviceType>\n" +
                "    <controlURL>/ctl/IPConn</controlURL>\n" +
                "  </service>\n" +
                "</serviceList></device></root>";

            string type;
            string url;

            Assert.IsTrue(UpnpProtocol.TryFindControlUrl(xml, out type, out url));
            Assert.AreEqual("/ctl/IPConn", url, "the forwarding service, not the first one");
        }

        [TestMethod]
        public void AServiceWithAnEmptyControlUrlIsSkipped()
        {
            // Advertised but not implemented. Posting to an empty URL would go to the
            // document itself and come back as HTML.
            var xml =
                "<root><device><serviceList><service>\n" +
                "  <serviceType>urn:schemas-upnp-org:service:WANIPConnection:1</serviceType>\n" +
                "  <controlURL></controlURL>\n" +
                "</service></serviceList></device></root>";

            string type;
            string url;

            Assert.IsFalse(UpnpProtocol.TryFindControlUrl(xml, out type, out url));
        }

        [TestMethod]
        public void JunkIsRejectedRatherThanGuessed()
        {
            string type;
            string url;

            Assert.IsFalse(UpnpProtocol.TryFindControlUrl("", out type, out url));
            Assert.IsFalse(UpnpProtocol.TryFindControlUrl(null, out type, out url));
            Assert.IsFalse(UpnpProtocol.TryFindControlUrl("<html><body>404</body></html>", out type, out url));
            Assert.IsFalse(UpnpProtocol.TryFindControlUrl("<root><unclosed>", out type, out url));
        }

        [TestMethod]
        public void AnElementIsNotMatchedByItsPrefix()
        {
            // "serviceList" must not be found when asking for "service": the name is
            // matched to the character after it, not by substring.
            string value;

            Assert.IsFalse(UpnpProtocol.TryReadElement("<serviceList></serviceList>", "service", out value));

            Assert.IsTrue(UpnpProtocol.TryReadElement("<service>x</service>", "service", out value));
            Assert.AreEqual("x", value);
        }

        [TestMethod]
        public void ASelfClosingElementHasNoText()
        {
            string value;

            Assert.IsTrue(UpnpProtocol.TryReadElement("<a><empty/><b>z</b></a>", "empty", out value));
            Assert.AreEqual("", value);
        }

        [TestMethod]
        public void EntitiesAreDecoded()
        {
            string value;

            Assert.IsTrue(UpnpProtocol.TryReadElement(
                "<a><title>Tom &amp; Jerry &lt;3</title></a>", "title", out value));

            Assert.AreEqual("Tom & Jerry <3", value);
        }

        [TestMethod]
        public void EntitiesAreEscapedForTheRouter()
        {
            // The description goes into a SOAP body, so a name with an ampersand has to
            // become an entity or the request is not XML any more. Ampersand first, or
            // the escapes get double-escaped - the classic order bug.
            Assert.AreEqual("a &amp; b", UpnpProtocol.Escape("a & b"));
            Assert.AreEqual("&lt;tag&gt;", UpnpProtocol.Escape("<tag>"));
            Assert.AreEqual("", UpnpProtocol.Escape(null));
        }

        [TestMethod]
        public void EscapingAndDecodingRoundTrip()
        {
            const string original = "DPT.DOOM <host> & \"friends\" 'here'";

            Assert.AreEqual(original, UpnpProtocol.Decode(UpnpProtocol.Escape(original)));
        }

        // ------------------------------------------------------------ SOAP requests

        [TestMethod]
        public void TheAddPortRequestCarriesEveryFieldTheRouterNeeds()
        {
            var arguments = UpnpProtocol.BuildAddPortArguments(5029, "TCP", 5029, "192.168.1.5", "DPT.DOOM", 0);

            StringAssert.Contains(arguments, "<NewRemoteHost></NewRemoteHost>",
                "an empty remote host means from anywhere");
            StringAssert.Contains(arguments, "<NewExternalPort>5029</NewExternalPort>");
            StringAssert.Contains(arguments, "<NewProtocol>TCP</NewProtocol>");
            StringAssert.Contains(arguments, "<NewInternalPort>5029</NewInternalPort>");
            StringAssert.Contains(arguments, "<NewInternalClient>192.168.1.5</NewInternalClient>");
            StringAssert.Contains(arguments, "<NewEnabled>1</NewEnabled>");
            StringAssert.Contains(arguments, "<NewPortMappingDescription>DPT.DOOM</NewPortMappingDescription>");
            StringAssert.Contains(arguments, "<NewLeaseDuration>0</NewLeaseDuration>",
                "zero is a permanent mapping, which is what a game host wants");
        }

        [TestMethod]
        public void TheEnvelopeNamesTheServiceAndTheAction()
        {
            var envelope = UpnpProtocol.BuildEnvelope(
                "urn:schemas-upnp-org:service:WANIPConnection:1",
                "AddPortMapping",
                "<x>1</x>");

            StringAssert.Contains(envelope, "xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"");
            StringAssert.Contains(envelope,
                "<u:AddPortMapping xmlns:u=\"urn:schemas-upnp-org:service:WANIPConnection:1\">");
            StringAssert.Contains(envelope, "<x>1</x>");
            StringAssert.Contains(envelope, "</u:AddPortMapping>");
            StringAssert.Contains(envelope, "</s:Envelope>");
        }

        [TestMethod]
        public void TheSoapActionHeaderIsQuoted()
        {
            // The quotes are part of the header value, which is why they are in the
            // builder rather than left to whoever formats the request.
            var header = UpnpProtocol.BuildSoapAction(
                "urn:schemas-upnp-org:service:WANIPConnection:1", "AddPortMapping");

            Assert.AreEqual("\"urn:schemas-upnp-org:service:WANIPConnection:1#AddPortMapping\"", header);
        }

        [TestMethod]
        public void TheDeleteRequestCarriesOnlyTheKey()
        {
            // A delete names the mapping by port and protocol; sending the rest would be
            // harmless and confusing.
            var arguments = UpnpProtocol.BuildDeletePortArguments(5029, "TCP");

            StringAssert.Contains(arguments, "<NewExternalPort>5029</NewExternalPort>");
            StringAssert.Contains(arguments, "<NewProtocol>TCP</NewProtocol>");
            Assert.IsFalse(arguments.Contains("NewInternalClient"), arguments);
        }

        // ------------------------------------------------------------ SOAP replies

        [TestMethod]
        public void AFaultIsRecognisedAndExplained()
        {
            // A router that already forwards the port answers 718, and that is worth
            // telling the host: it is usually a previous run whose mapping outlived it.
            var response =
                "<?xml version=\"1.0\"?>\n" +
                "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\">\n" +
                "  <s:Body><s:Fault>\n" +
                "    <faultcode>s:Client</faultcode>\n" +
                "    <faultstring>UPnPError</faultstring>\n" +
                "    <detail><UPnPError xmlns=\"urn:schemas-upnp-org:control-1-0\">\n" +
                "      <errorCode>718</errorCode>\n" +
                "      <errorDescription>ConflictInMappingEntry</errorDescription>\n" +
                "    </UPnPError></detail>\n" +
                "  </s:Fault></s:Body>\n" +
                "</s:Envelope>";

            int code;
            string description;

            Assert.IsTrue(UpnpProtocol.IsSoapFault(response, out code, out description));
            Assert.AreEqual(718, code);
            Assert.AreEqual("ConflictInMappingEntry", description);
        }

        [TestMethod]
        public void ASuccessIsNotAFault()
        {
            var response =
                "<s:Envelope><s:Body><u:AddPortMappingResponse " +
                "xmlns:u=\"urn:schemas-upnp-org:service:WANIPConnection:1\"/></s:Body></s:Envelope>";

            int code;
            string description;

            Assert.IsFalse(UpnpProtocol.IsSoapFault(response, out code, out description));
        }

        [TestMethod]
        public void AnEmptyReplyIsNotAFault()
        {
            // Not every router puts a body in a successful reply, and treating an empty
            // body as an error would reject working devices.
            int code;
            string description;

            Assert.IsFalse(UpnpProtocol.IsSoapFault("", out code, out description));
            Assert.IsFalse(UpnpProtocol.IsSoapFault(null, out code, out description));
        }

        [TestMethod]
        public void TheExternalAddressIsRead()
        {
            var response =
                "<s:Envelope><s:Body><u:GetExternalIPAddressResponse " +
                "xmlns:u=\"urn:schemas-upnp-org:service:WANIPConnection:1\">" +
                "<NewExternalIPAddress>203.0.113.7</NewExternalIPAddress>" +
                "</u:GetExternalIPAddressResponse></s:Body></s:Envelope>";

            string address;

            Assert.IsTrue(UpnpProtocol.TryReadExternalAddress(response, out address));
            Assert.AreEqual("203.0.113.7", address);
        }

        [TestMethod]
        public void AnEmptyExternalAddressIsNotAnAddress()
        {
            // 0.0.0.0 or a blank field means the gateway's WAN link is down; reporting it
            // as an address would tell a friend to connect to nothing.
            var response =
                "<NewExternalIPAddress></NewExternalIPAddress>";

            string address;

            Assert.IsFalse(UpnpProtocol.TryReadExternalAddress(response, out address));

            Assert.IsFalse(UpnpProtocol.TryReadExternalAddress("<a></a>", out address));
        }

        // ------------------------------------------------------------ the local address

        [TestMethod]
        public void TheLocalAddressLooksLikeAnAddress()
        {
            // Not a fixed value: the machine running the test has whatever address it has.
            // What matters is that the routing table was asked successfully and the answer
            // is a private IPv4 address - a public one would mean the machine is directly
            // on the internet, and a loopback would mean no route at all.
            var address = UpnpPortMapper.FindLocalAddress();

            if (address == null)
            {
                Assert.Inconclusive("this machine has no IPv4 route");
            }

            Assert.IsTrue(address.StartsWith("127.") || address.StartsWith("10.") ||
                          address.StartsWith("192.168.") ||
                          IsPrivate172(address) ||
                          !address.Contains("."),
                "the local address should be a LAN address, got " + address);
        }

        private static bool IsPrivate172(string address)
        {
            if (!address.StartsWith("172."))
            {
                return false;
            }

            var dot = address.IndexOf('.', 4);

            if (dot < 0)
            {
                return false;
            }

            int second;

            if (!int.TryParse(address.Substring(4, dot - 4), out second))
            {
                return false;
            }

            return second >= 16 && second <= 31;
        }
    }
}
