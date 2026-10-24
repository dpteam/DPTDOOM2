//
// Copyright (C) 2026 DartPower Team
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//

using System;
using System.Globalization;
using System.Text;

namespace DPTDOOM.Net
{
    /// <summary>
    /// The two text protocols UPnP port forwarding is made of: SSDP discovery and SOAP
    /// calls.
    ///
    /// Everything here is pure string work, deliberately separated from the sockets in
    /// <see cref="UpnpPortMapper"/>. A router cannot be put in a unit test, but every
    /// byte a router sends can - and the parsers are where the bugs live. The wire is
    /// filled with devices that format their XML differently, wrap it in a namespace
    /// nobody expected, or answer in lower case; a parser that is only ever tested
    /// against one real router is a parser that works for that router.
    ///
    /// No XML library is used, and that is not laziness either. The parts needed are
    /// "find this element's text" and "escape these five characters" - both a few lines -
    /// while a full DOM would bring a dependency, allocation and an entity-expansion
    /// surface to a program that reads text from a device it does not trust.
    /// </summary>
    public static class UpnpProtocol
    {
        /// <summary>SSDP's multicast group and port, fixed by the specification.</summary>
        public const string SsdpAddress = "239.255.255.250";
        public const int SsdpPort = 1900;

        /// <summary>
        /// The search target for an internet gateway device.
        ///
        /// Three are tried in order because a router may answer to any of them, and the
        /// version numbers are not a hierarchy: some devices answer 2 and not 1, a few
        /// answer "upnp:rootdevice" and nothing else.
        /// </summary>
        public static readonly string[] SearchTargets =
        {
            "urn:schemas-upnp-org:device:InternetGatewayDevice:1",
            "urn:schemas-upnp-org:device:InternetGatewayDevice:2",
            "upnp:rootdevice"
        };

        /// <summary>
        /// The service types that can forward a port, in the order worth trying.
        ///
        /// WANIPConnection is the normal one. WANPPPConnection appears on routers that
        /// own the PPPoE session themselves - common on DSL - and a device may expose
        /// either, or both.
        /// </summary>
        public static readonly string[] ServiceTypes =
        {
            "urn:schemas-upnp-org:service:WANIPConnection:1",
            "urn:schemas-upnp-org:service:WANIPConnection:2",
            "urn:schemas-upnp-org:service:WANPPPConnection:1"
        };

        // ------------------------------------------------------------ SSDP

        /// <summary>
        /// The M-SEARCH request.
        ///
        /// The three "MX" lines are not three attempts at one thing: MX is how long a
        /// device may wait before answering, and devices that ignore the line answer at
        /// once. The trailing blank line is part of the message - without it the device
        /// waits for more headers and never replies.
        /// </summary>
        public static string BuildSearch(string searchTarget, int waitSeconds)
        {
            var text = new StringBuilder();

            text.Append("M-SEARCH * HTTP/1.1\r\n");
            text.Append("HOST: ").Append(SsdpAddress).Append(':').Append(SsdpPort).Append("\r\n");
            text.Append("MAN: \"ssdp:discover\"\r\n");
            text.Append("MX: ").Append(waitSeconds.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
            text.Append("ST: ").Append(searchTarget).Append("\r\n");
            text.Append("\r\n");

            return text.ToString();
        }

        /// <summary>
        /// Pulls the LOCATION header out of an SSDP reply.
        ///
        /// Header names are matched case-insensitively because the specification says
        /// they are case-insensitive and devices take that literally: LOCATION, Location
        /// and location all appear in the wild.
        /// </summary>
        public static bool TryParseSearchReply(string reply, out string location)
        {
            location = null;

            if (string.IsNullOrEmpty(reply))
            {
                return false;
            }

            foreach (var rawLine in reply.Replace("\r\n", "\n").Split('\n'))
            {
                var line = rawLine.Trim();

                if (line.Length == 0)
                {
                    continue;
                }

                // The status line of the reply is "HTTP/1.1 200 OK" and has no colon
                // before any header; splitting on the first colon is safe because a URL
                // only appears after one.
                var colon = line.IndexOf(':');

                if (colon <= 0)
                {
                    continue;
                }

                var name = line.Substring(0, colon).Trim();

                if (!string.Equals(name, "LOCATION", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var value = line.Substring(colon + 1).Trim();

                if (value.Length == 0)
                {
                    return false;
                }

                location = value;
                return true;
            }

            return false;
        }

        // ------------------------------------------------------------ device description

        /// <summary>
        /// Finds where the port-forwarding service of a gateway can be called.
        ///
        /// The description document describes a tree of devices and services; the one
        /// wanted is whichever service has one of <see cref="ServiceTypes"/> as its type,
        /// and its <c>controlURL</c> is the path to post to. That URL is usually relative
        /// and has to be resolved against the document's own URL, which is the caller's
        /// job because it knows where the document came from.
        /// </summary>
        public static bool TryFindControlUrl(string description, out string serviceType,
                                             out string controlUrl)
        {
            serviceType = null;
            controlUrl = null;

            if (string.IsNullOrEmpty(description))
            {
                return false;
            }

            var position = 0;

            while (true)
            {
                string block;

                if (!TryExtractBlock(description, "service", ref position, out block))
                {
                    return false;
                }

                string type;

                if (!TryReadElement(block, "serviceType", out type))
                {
                    continue;
                }

                var known = false;

                foreach (var candidate in ServiceTypes)
                {
                    if (string.Equals(type.Trim(), candidate, StringComparison.OrdinalIgnoreCase))
                    {
                        known = true;
                        break;
                    }
                }

                if (!known)
                {
                    continue;
                }

                string url;

                if (!TryReadElement(block, "controlURL", out url))
                {
                    continue;
                }

                // A few devices write an empty controlURL on a service they advertise
                // but do not implement. Skipping it is better than posting to nowhere.
                if (url.Trim().Length == 0)
                {
                    continue;
                }

                serviceType = type.Trim();
                controlUrl = url.Trim();
                return true;
            }
        }

        /// <summary>
        /// Reads the text of the first element with this local name.
        ///
        /// Local name, not qualified: the prefix is whatever the device felt like
        /// ("u:", "ns0:", or none at all), and matching on it would fail on half the
        /// routers in existence.
        /// </summary>
        public static bool TryReadElement(string xml, string name, out string value)
        {
            value = null;

            var position = 0;
            string inner;

            if (!TryExtractBlock(xml, name, ref position, out inner))
            {
                return false;
            }

            // An element whose content has markup in it is a container, not a value.
            // Returning its contents as text would hand the caller a fragment of XML and
            // let it travel into a SOAP request.
            if (inner.IndexOf('<') >= 0)
            {
                return false;
            }

            value = Decode(inner);
            return true;
        }

        /// <summary>
        /// Pulls out the contents of the next element with this local name.
        ///
        /// The closing tag is matched by local name rather than found by "the next
        /// <c>&lt;/</c>", which is the difference between working and not: inside
        /// <c>&lt;service&gt;</c> the first closing tag belongs to
        /// <c>&lt;serviceType&gt;</c>, and taking it would cut the block one line in.
        ///
        /// Nesting of the same name is not handled, and does not occur in a UPnP
        /// description: a service never contains a service.
        /// </summary>
        private static bool TryExtractBlock(string xml, string name, ref int position, out string inner)
        {
            inner = null;

            if (string.IsNullOrEmpty(xml))
            {
                return false;
            }

            var open = IndexOfElement(xml, name, position);

            if (open < 0)
            {
                return false;
            }

            var endOpen = xml.IndexOf('>', open);

            if (endOpen < 0)
            {
                return false;
            }

            if (endOpen > 0 && xml[endOpen - 1] == '/')
            {
                // Self-closing: there is a block, and it is empty.
                inner = "";
                position = endOpen + 1;
                return true;
            }

            var scan = endOpen + 1;

            while (true)
            {
                var close = xml.IndexOf("</", scan, StringComparison.Ordinal);

                if (close < 0)
                {
                    return false;
                }

                var nameStart = close + 2;

                if (nameStart >= xml.Length)
                {
                    return false;
                }

                var endName = 0;
                var localStart = LocalNameStart(xml, nameStart, out endName);

                if (endName < 0)
                {
                    return false;
                }

                if (endName - localStart == name.Length &&
                    string.Compare(xml, localStart, name, 0, name.Length,
                                   StringComparison.OrdinalIgnoreCase) == 0)
                {
                    inner = xml.Substring(endOpen + 1, close - endOpen - 1);
                    position = endName + 1;
                    return true;
                }

                // Some other element's closing tag: keep looking for ours.
                scan = endName + 1;
            }
        }

        /// <summary>
        /// The index of an opening tag whose local name matches, or -1.
        ///
        /// The name is compared from where it starts in the tag, and the token is the
        /// whole run up to a delimiter. Both halves matter: comparing at the wrong offset
        /// made <c>&lt;serviceList&gt;</c> match <c>service</c> because the following
        /// whitespace happened to satisfy the "ends here" check, and a prefix such as
        /// <c>u:</c> has to be skipped or half the routers stop being understood.
        /// </summary>
        private static int IndexOfElement(string xml, string name, int start)
        {
            var position = start;

            while (true)
            {
                var open = xml.IndexOf('<', position);

                if (open < 0)
                {
                    return -1;
                }

                position = open + 1;

                if (position >= xml.Length)
                {
                    return -1;
                }

                var first = xml[position];

                // A closing tag, a declaration or a comment: none of them opens a block.
                if (first == '/' || first == '?' || first == '!')
                {
                    continue;
                }

                var afterName = 0;
                var localStart = LocalNameStart(xml, position, out afterName);

                if (afterName < 0)
                {
                    continue;
                }

                if (afterName - localStart == name.Length &&
                    string.Compare(xml, localStart, name, 0, name.Length,
                                   StringComparison.OrdinalIgnoreCase) == 0)
                {
                    return open;
                }
            }
        }

        /// <summary>
        /// Where the local name begins in a tag starting at <paramref name="nameStart"/>,
        /// and where it ends.
        ///
        /// Returns -1 as <paramref name="afterName"/> when the tag is malformed.
        /// </summary>
        private static int LocalNameStart(string xml, int nameStart, out int afterName)
        {
            var scan = nameStart;

            while (scan < xml.Length && !IsDelimiter(xml[scan]))
            {
                scan++;
            }

            if (scan >= xml.Length)
            {
                afterName = -1;
                return nameStart;
            }

            afterName = scan;

            // A namespace prefix runs up to the last colon before the delimiter. The
            // local name is what follows it, or the whole token when there is no prefix.
            var colon = -1;

            for (var i = nameStart; i < scan; i++)
            {
                if (xml[i] == ':')
                {
                    colon = i;
                }
            }

            return colon >= 0 ? colon + 1 : nameStart;
        }

        private static bool IsDelimiter(char c)
        {
            return c == '>' || c == ' ' || c == '/' || c == '\t' || c == '\r' || c == '\n';
        }

        /// <summary>
        /// Turns the five XML entities back into characters.
        ///
        /// Only five, and done before any use: a device that puts "&amp;" in a
        /// description is normal, and an expanded "&lt;" would end up inside a SOAP
        /// request and break it. The reverse direction is <see cref="Escape"/>.
        /// </summary>
        public static string Decode(string text)
        {
            if (text == null || text.IndexOf('&') < 0)
            {
                return text;
            }

            return text
                .Replace("&lt;", "<")
                .Replace("&gt;", ">")
                .Replace("&quot;", "\"")
                .Replace("&apos;", "'")
                .Replace("&amp;", "&");
        }

        public static string Escape(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }

            return text
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&apos;");
        }

        // ------------------------------------------------------------ SOAP

        public static string BuildEnvelope(string serviceType, string action, string arguments)
        {
            var text = new StringBuilder();

            text.Append("<?xml version=\"1.0\"?>\r\n");
            text.Append("<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" ");
            text.Append("s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">\r\n");
            text.Append("<s:Body>\r\n");
            text.Append("<u:").Append(action).Append(" xmlns:u=\"").Append(serviceType).Append("\">\r\n");
            text.Append(arguments);
            text.Append("</u:").Append(action).Append(">\r\n");
            text.Append("</s:Body>\r\n");
            text.Append("</s:Envelope>\r\n");

            return text.ToString();
        }

        /// <summary>
        /// The SOAPAction header, which some routers require and others ignore.
        ///
        /// The outer quotes are part of the value, not string quotes in the source: the
        /// header is literally <c>SOAPAction: "urn:...#AddPortMapping"</c>.
        /// </summary>
        public static string BuildSoapAction(string serviceType, string action)
        {
            return "\"" + serviceType + "#" + action + "\"";
        }

        public static string BuildAddPortArguments(int externalPort, string protocol,
                                                   int internalPort, string internalClient,
                                                   string description, int leaseSeconds)
        {
            var text = new StringBuilder();

            // RemoteHost empty means "from anywhere": the alternative is restricting the
            // mapping to one source, which is not what a game host wants.
            text.Append("<NewRemoteHost></NewRemoteHost>\r\n");
            text.Append("<NewExternalPort>").Append(externalPort.ToString(CultureInfo.InvariantCulture)).Append("</NewExternalPort>\r\n");
            text.Append("<NewProtocol>").Append(Escape(protocol)).Append("</NewProtocol>\r\n");
            text.Append("<NewInternalPort>").Append(internalPort.ToString(CultureInfo.InvariantCulture)).Append("</NewInternalPort>\r\n");
            text.Append("<NewInternalClient>").Append(Escape(internalClient)).Append("</NewInternalClient>\r\n");
            text.Append("<NewEnabled>1</NewEnabled>\r\n");
            text.Append("<NewPortMappingDescription>").Append(Escape(description)).Append("</NewPortMappingDescription>\r\n");
            text.Append("<NewLeaseDuration>").Append(leaseSeconds.ToString(CultureInfo.InvariantCulture)).Append("</NewLeaseDuration>\r\n");

            return text.ToString();
        }

        public static string BuildDeletePortArguments(int externalPort, string protocol)
        {
            var text = new StringBuilder();

            text.Append("<NewRemoteHost></NewRemoteHost>\r\n");
            text.Append("<NewExternalPort>").Append(externalPort.ToString(CultureInfo.InvariantCulture)).Append("</NewExternalPort>\r\n");
            text.Append("<NewProtocol>").Append(Escape(protocol)).Append("</NewProtocol>\r\n");

            return text.ToString();
        }

        /// <summary>
        /// Whether a SOAP reply is a failure, and what the device said about it.
        ///
        /// A UPnP error comes back as HTTP 500 with an errorCode and errorDescription in
        /// the body, which is the most useful thing a router ever tells you: 718 means
        /// "another mapping already owns this port", 501 means "the router does not
        /// implement this action".
        /// </summary>
        public static bool IsSoapFault(string response, out int errorCode, out string description)
        {
            errorCode = 0;
            description = "";

            if (string.IsNullOrEmpty(response))
            {
                return false;
            }

            string code;

            if (!TryReadElement(response, "errorCode", out code))
            {
                return false;
            }

            if (!int.TryParse(code.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out errorCode))
            {
                errorCode = 0;
                return false;
            }

            string text;

            if (TryReadElement(response, "errorDescription", out text))
            {
                description = text.Trim();
            }

            return true;
        }

        /// <summary>
        /// Reads the external address a gateway reports.
        ///
        /// It can genuinely be 0.0.0.0 on a router whose WAN link is down, and that is
        /// worth reporting honestly rather than treating as a working forward.
        /// </summary>
        public static bool TryReadExternalAddress(string response, out string address)
        {
            address = null;

            string text;

            if (!TryReadElement(response, "NewExternalIPAddress", out text))
            {
                return false;
            }

            text = text.Trim();

            if (text.Length == 0)
            {
                return false;
            }

            address = text;
            return true;
        }
    }
}
