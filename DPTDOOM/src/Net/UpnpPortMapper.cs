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
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace DPTDOOM.Net
{
    /// <summary>
    /// A forwarded port that exists on a router right now.
    ///
    /// Kept so the mapping can be taken down again. A mapping nobody removes stays in
    /// the router's table until it reboots, which means a port open to the internet for
    /// a game that ended a week ago - the kind of leftover that is invisible until
    /// somebody finds it.
    /// </summary>
    public sealed class UpnpMapping
    {
        public string ControlUrl;
        public string ServiceType;
        public int Port;
        public string Protocol;

        /// <summary>The address the router thinks the internet sees, if it said.</summary>
        public string ExternalAddress;
    }

    /// <summary>
    /// Asks the router to forward a port, using UPnP.
    ///
    /// Written against the protocols with raw sockets rather than through the Windows
    /// UPnP COM object, and the reasons are the project's own rules:
    ///
    ///  - the COM object needs `[ComImport]` declarations, and the only copy available
    ///    here came out of a decompiled commercial game - copying it would be taking
    ///    somebody else's code, and the licensing is not ours to give;
    ///  - it also wants CustomMarshalers, an extra assembly next to the executable, and
    ///    this project ships one file;
    ///  - COM registration and apartment threading on Windows XP is a support surface
    ///    with no upside when SSDP is a UDP datagram and SOAP is an HTTP POST.
    ///
    /// Everything it does is best-effort, and it says so. A router that does not answer,
    /// answers slowly, or refuses the mapping is not an error the game can fix: the host
    /// still works on a LAN and still works through the relay. What must never happen is
    /// the game waiting on it, or claiming a forward that does not exist.
    /// </summary>
    public sealed class UpnpPortMapper
    {
        /// <summary>
        /// How long to wait for an SSDP reply.
        ///
        /// Devices are allowed to delay by the MX value; three seconds is the common
        /// answer for a LAN and short enough that a failed attempt does not feel like a
        /// hang at startup.
        /// </summary>
        private const int DiscoverySeconds = 3;

        /// <summary>Timeout for one HTTP exchange with the router.</summary>
        private const int HttpTimeoutMs = 4000;

        /// <summary>
        /// The address UPnP calls the "internal client": this machine on the LAN.
        ///
        /// Found by asking the routing table rather than by enumerating interfaces:
        /// connecting a UDP socket sends nothing, but it does make the OS choose the
        /// outgoing address, which is exactly the one the router can reach. Enumerating
        /// interfaces gives every address on the machine including virtual adapters and
        /// IPv6, and picking the wrong one produces a mapping that silently forwards
        /// nowhere.
        /// </summary>
        public static string FindLocalAddress()
        {
            try
            {
                using (var probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    // A TEST-NET-1 address: guaranteed not to be routed on the internet,
                    // so this cannot send anything anywhere. It only fills in the route.
                    probe.Connect(new IPEndPoint(IPAddress.Parse("192.0.2.1"), 9));

                    var local = probe.LocalEndPoint as IPEndPoint;
                    return local == null ? null : local.Address.ToString();
                }
            }
            catch (Exception)
            {
                // No route, no sockets, or a machine with only IPv6. Not fatal: the
                // caller reports that it could not forward.
                return null;
            }
        }

        /// <summary>
        /// Finds a gateway and returns the URL of its port-forwarding service.
        ///
        /// All search targets are asked at once and the answers are collected in one
        /// window. Asking them one after another is the obvious way to write this and
        /// costs nine seconds when there is no gateway - three targets times the three
        /// second wait - on every launch of a host who asked for forwarding on a network
        /// that cannot provide it. Three datagrams are three UDP packets; sending them
        /// together and listening once is the same information in a third of the time.
        ///
        /// A device that answers but cannot forward - a printer, a media server - is
        /// skipped rather than treated as failure, because a home network has several
        /// devices answering SSDP.
        /// </summary>
        public static bool TryDiscover(out string controlUrl, out string serviceType)
        {
            controlUrl = null;
            serviceType = null;

            foreach (var location in Search())
            {
                string description;

                if (!TryGet(location, out description))
                {
                    continue;
                }

                string type;
                string url;

                if (!UpnpProtocol.TryFindControlUrl(description, out type, out url))
                {
                    continue;
                }

                // The control URL is usually relative to the description's own URL, and
                // those two can be on different hosts on a router with a WAN-side
                // description. Resolving against the description is what the
                // specification says and what every client does.
                controlUrl = Resolve(location, url);
                serviceType = type;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Sends every search target at once and returns the distinct locations that
        /// answer within the discovery window.
        /// </summary>
        private static List<string> Search()
        {
            var found = new List<string>();

            try
            {
                using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    // ReuseAddress so a second instance, or a client left behind by
                    // another program, does not make discovery fail outright.
                    socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    socket.ReceiveTimeout = 500;

                    var group = new IPEndPoint(
                        IPAddress.Parse(UpnpProtocol.SsdpAddress), UpnpProtocol.SsdpPort);

                    foreach (var target in UpnpProtocol.SearchTargets)
                    {
                        var payload = Encoding.ASCII.GetBytes(
                            UpnpProtocol.BuildSearch(target, DiscoverySeconds));

                        socket.SendTo(payload, group);
                    }

                    var buffer = new byte[4096];
                    var deadline = Environment.TickCount + DiscoverySeconds * 1000;

                    while (unchecked(Environment.TickCount - deadline) < 0)
                    {
                        try
                        {
                            EndPoint from = new IPEndPoint(IPAddress.Any, 0);
                            var read = socket.ReceiveFrom(buffer, ref from);

                            var reply = Encoding.ASCII.GetString(buffer, 0, read);

                            string location;

                            if (UpnpProtocol.TryParseSearchReply(reply, out location) &&
                                !found.Contains(location))
                            {
                                found.Add(location);
                            }
                        }
                        catch (SocketException)
                        {
                            // The per-read timeout: either the window closed or a device
                            // is still thinking. The outer loop decides.
                        }
                    }
                }
            }
            catch (Exception)
            {
                // No multicast route, no permission, no network. Discovery failed and
                // the caller says so.
            }

            return found;
        }

        private static List<string> Search(string searchTarget)
        {
            var found = new List<string>();

            try
            {
                using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    // ReuseAddress so a second instance, or a client left behind by
                    // another program, does not make discovery fail outright.
                    socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    socket.ReceiveTimeout = 500;

                    var payload = Encoding.ASCII.GetBytes(UpnpProtocol.BuildSearch(searchTarget, DiscoverySeconds));

                    socket.SendTo(
                        payload,
                        new IPEndPoint(IPAddress.Parse(UpnpProtocol.SsdpAddress), UpnpProtocol.SsdpPort));

                    var buffer = new byte[4096];
                    var deadline = Environment.TickCount + DiscoverySeconds * 1000;

                    while (unchecked(Environment.TickCount - deadline) < 0)
                    {
                        try
                        {
                            EndPoint from = new IPEndPoint(IPAddress.Any, 0);
                            var read = socket.ReceiveFrom(buffer, ref from);

                            var reply = Encoding.ASCII.GetString(buffer, 0, read);

                            string location;

                            if (UpnpProtocol.TryParseSearchReply(reply, out location))
                            {
                                found.Add(location);
                            }
                        }
                        catch (SocketException)
                        {
                            // The per-read timeout: either the window closed or a device
                            // is still thinking. The outer loop decides.
                        }
                    }
                }
            }
            catch (Exception)
            {
                // No multicast route, no permission, no network. Discovery failed and
                // the caller says so.
            }

            return found;
        }

        /// <summary>
        /// Asks the gateway to forward <paramref name="port"/> to this machine.
        ///
        /// Returns null on success and a sentence on failure, because every failure here
        /// is something the host may want to read: "another mapping owns this port" is a
        /// different problem from "this router does not do UPnP".
        /// </summary>
        public static UpnpMapping TryMap(int port, string description, out string reason)
        {
            reason = null;

            var local = FindLocalAddress();

            if (local == null)
            {
                reason = "could not determine this machine's address on the local network";
                return null;
            }

            string controlUrl;
            string serviceType;

            if (!TryDiscover(out controlUrl, out serviceType))
            {
                reason = "no router answered UPnP; the port has to be forwarded by hand";
                return null;
            }

            var mapping = new UpnpMapping();
            mapping.ControlUrl = controlUrl;
            mapping.ServiceType = serviceType;
            mapping.Port = port;
            mapping.Protocol = "TCP";

            string response;

            var added = Post(
                controlUrl,
                serviceType,
                "AddPortMapping",
                UpnpProtocol.BuildAddPortArguments(port, "TCP", port, local, description, 0),
                out response);

            if (!added)
            {
                reason = "the router did not answer the port-forward request";
                return null;
            }

            int code;
            string text;

            if (UpnpProtocol.IsSoapFault(response, out code, out text))
            {
                // 718 is the one worth naming: it means another mapping already holds the
                // port, which is usually a previous run of this same game on a router
                // that did not honour the removal. Saying "wrong port, try another" is
                // more useful than relaying a number.
                reason = DescribeFault(code, text);
                return null;
            }

            // The external address is worth having: it is what a friend has to type, and
            // on a double-NAT connection it is not the address the machine sees.
            if (Post(controlUrl, serviceType, "GetExternalIPAddress", "", out response))
            {
                UpnpProtocol.TryReadExternalAddress(response, out mapping.ExternalAddress);
            }

            return mapping;
        }

        /// <summary>
        /// Takes a mapping down again.
        ///
        /// Best-effort and silent: the game is closing, and a router that will not remove
        /// the mapping is not something to interrupt a player about. It is still worth
        /// attempting, because the alternative is a port left open.
        /// </summary>
        public static bool TryUnmap(UpnpMapping mapping)
        {
            if (mapping == null || mapping.ControlUrl == null)
            {
                return false;
            }

            string response;

            return Post(
                mapping.ControlUrl,
                mapping.ServiceType,
                "DeletePortMapping",
                UpnpProtocol.BuildDeletePortArguments(mapping.Port, mapping.Protocol),
                out response);
        }

        private static string DescribeFault(int code, string text)
        {
            var meaning = text;

            switch (code)
            {
                case 718:
                    meaning = "another program already forwards this port";
                    break;

                case 501:
                    meaning = "this router does not support port forwarding over UPnP";
                    break;

                case 606:
                    meaning = "the router refused the request";
                    break;

                case 725:
                    meaning = "this router only allows forwarding to a fixed address";
                    break;
            }

            return meaning + " (UPnP error " + code.ToString(CultureInfo.InvariantCulture) + ")";
        }

        // ------------------------------------------------------------ HTTP

        private static bool Post(string controlUrl, string serviceType, string action,
                                 string arguments, out string body)
        {
            var envelope = UpnpProtocol.BuildEnvelope(serviceType, action, arguments);
            var soapAction = UpnpProtocol.BuildSoapAction(serviceType, action);

            var headers = new StringBuilder();
            headers.Append("Content-Type: text/xml; charset=\"utf-8\"\r\n");
            headers.Append("SOAPAction: ").Append(soapAction).Append("\r\n");

            return TryHttp(controlUrl, "POST", headers.ToString(), envelope, out body);
        }

        private static bool TryGet(string url, out string body)
        {
            return TryHttp(url, "GET", "", null, out body);
        }

        /// <summary>
        /// One HTTP exchange, deliberately small.
        ///
        /// No keep-alive, no redirects, no cookies, no chunked decoding: a UPnP device is
        /// a router's embedded web server answering one short request. What it does need
        /// is to close when it fails, which is why every step is bounded by the socket
        /// timeout rather than by a loop that trusts the peer.
        /// </summary>
        private static bool TryHttp(string url, string method, string extraHeaders,
                                    string body, out string responseBody)
        {
            responseBody = null;

            Uri uri;

            if (!Uri.TryCreate(url, UriKind.Absolute, out uri))
            {
                return false;
            }

            try
            {
                using (var client = new TcpClient())
                {
                    var connect = client.BeginConnect(uri.Host, uri.Port, null, null);

                    if (!connect.AsyncWaitHandle.WaitOne(HttpTimeoutMs, false))
                    {
                        return false;
                    }

                    client.EndConnect(connect);
                    client.ReceiveTimeout = HttpTimeoutMs;
                    client.SendTimeout = HttpTimeoutMs;

                    using (var stream = client.GetStream())
                    {
                        var payload = body == null ? null : Encoding.UTF8.GetBytes(body);

                        var request = new StringBuilder();
                        request.Append(method).Append(' ').Append(uri.PathAndQuery).Append(" HTTP/1.1\r\n");
                        request.Append("Host: ").Append(uri.Host).Append(':').Append(uri.Port).Append("\r\n");
                        request.Append("Connection: close\r\n");
                        request.Append("User-Agent: DPTDOOM\r\n");
                        request.Append(extraHeaders);

                        if (payload != null)
                        {
                            request.Append("Content-Length: ").Append(payload.Length).Append("\r\n");
                        }

                        request.Append("\r\n");

                        var head = Encoding.ASCII.GetBytes(request.ToString());
                        stream.Write(head, 0, head.Length);

                        if (payload != null)
                        {
                            stream.Write(payload, 0, payload.Length);
                        }

                        stream.Flush();

                        responseBody = ReadAll(stream);
                        return responseBody != null;
                    }
                }
            }
            catch (Exception)
            {
                // Anything at all: refused, reset, timed out, DNS. All of them mean the
                // same thing to the caller.
                return false;
            }
        }

        /// <summary>
        /// Reads an HTTP response and returns only its body.
        ///
        /// Reads to the end of the stream rather than trusting Content-Length, because
        /// "Connection: close" was requested and a router that lies about the length is
        /// worse than one that closes properly. The headers are stripped at the first
        /// blank line.
        /// </summary>
        private static string ReadAll(Stream stream)
        {
            var buffer = new byte[4096];
            var text = new MemoryStream();

            while (true)
            {
                int read;

                try
                {
                    read = stream.Read(buffer, 0, buffer.Length);
                }
                catch (IOException)
                {
                    break;
                }

                if (read <= 0)
                {
                    break;
                }

                text.Write(buffer, 0, read);

                // A response has no size limit worth enforcing here, but a device that
                // streams for ever would otherwise hold the caller: 1 MB is far beyond
                // any description document and still bounded.
                if (text.Length > 1024 * 1024)
                {
                    break;
                }
            }

            var raw = Encoding.UTF8.GetString(text.ToArray());
            var split = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);

            if (split < 0)
            {
                return raw;
            }

            return raw.Substring(split + 4);
        }

        /// <summary>
        /// Resolves a possibly relative URL against the document it came from.
        /// </summary>
        private static string Resolve(string baseUrl, string relative)
        {
            Uri absolute;
            Uri result;

            if (Uri.TryCreate(relative, UriKind.Absolute, out absolute))
            {
                return absolute.ToString();
            }

            if (Uri.TryCreate(new Uri(baseUrl), relative, out result))
            {
                return result.ToString();
            }

            return relative;
        }

        /// <summary>
        /// Runs the whole attempt on a background thread and waits a bounded time.
        ///
        /// Startup must not depend on a router's mood. The work is done off-thread and
        /// abandoned if it takes too long - the thread is a background one, so it cannot
        /// keep the process alive, and the mapping object it might have created is simply
        /// never used.
        /// </summary>
        public static UpnpMapping TryMapWithDeadline(int port, string description,
                                                     int seconds, out string reason)
        {
            UpnpMapping mapping = null;
            string failure = "the router did not answer in time";

            var thread = new Thread(() =>
            {
                string inner;

                try
                {
                    mapping = TryMap(port, description, out inner);
                    failure = inner;
                }
                catch (Exception e)
                {
                    failure = e.Message;
                }
            });

            thread.IsBackground = true;
            thread.Name = "DPTDOOM UPnP";
            thread.Start();

            if (!thread.Join(seconds * 1000))
            {
                reason = "the router did not answer in time";
                return null;
            }

            reason = failure;
            return mapping;
        }
    }
}
