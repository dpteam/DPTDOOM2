# A TCP proxy that adds a fixed one-way delay to every chunk it forwards.
#
#   powershell -NoProfile -File tools/latency-proxy.ps1 -Listen 5051 -Target 127.0.0.1:5050 -DelayMs 50
#
# Why this exists: DPT.DOOM is lockstep, so a tic completes only when the peer's
# command for that tic has arrived. The achievable tic rate is therefore bounded
# by the network round trip, not by bandwidth - at 100 ms of ping a 35-tic-per-
# second session physically cannot keep up, no matter how few bytes it sends.
#
# This proxy makes that measurable on one machine: put it in front of a host and
# the session behaves as if the peer were far away. Used to size the input delay.

param(
    [Parameter(Mandatory = $true)][int]$Listen,
    [Parameter(Mandatory = $true)][string]$Target,
    [int]$DelayMs = 50,
    [int]$JitterMs = 0,
    [int]$LossPercent = 0
)

$ErrorActionPreference = "Stop"

$parts = $Target.Split(':')
$targetHost = $parts[0]
$targetPort = [int]$parts[1]

Add-Type @"
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

public sealed class DelayProxy : IDisposable {
    private readonly TcpListener listener;
    private readonly string targetHost;
    private readonly int targetPort;
    private readonly int delayMs;
    private readonly int jitterMs;
    private readonly int lossPercent;
    private volatile bool stopping;

    public DelayProxy(int listenPort, string host, int port, int delay, int jitter, int loss) {
        targetHost = host;
        targetPort = port;
        delayMs = delay;
        jitterMs = jitter;
        lossPercent = loss;
        listener = new TcpListener(IPAddress.Loopback, listenPort);
    }

    public void Start() {
        listener.Start(32);
        var thread = new Thread(AcceptLoop);
        thread.IsBackground = true;
        thread.Start();
    }

    private void AcceptLoop() {
        while (!stopping) {
            TcpClient client;
            try { client = listener.AcceptTcpClient(); }
            catch { return; }

            var state = client;
            ThreadPool.QueueUserWorkItem(new WaitCallback(HandleWork), state);
        }
    }

    private void HandleWork(object state) { Handle((TcpClient)state); }

    private void Handle(TcpClient client) {
        TcpClient server = new TcpClient();
        try {
            client.NoDelay = true;
            server.NoDelay = true;
            server.Connect(targetHost, targetPort);
        } catch {
            try { client.Close(); } catch { }
            try { server.Close(); } catch { }
            return;
        }

        var forward = new Thread(new ThreadStart(delegate { Pump(client, server); }));
        forward.IsBackground = true;
        forward.Start();

        Pump(server, client);
    }

    private void Pump(TcpClient from, TcpClient to) {
        var buffer = new byte[4096];
        var random = new Random(Environment.TickCount);
        try {
            var input = from.GetStream();
            var output = to.GetStream();

            while (!stopping) {
                var read = input.Read(buffer, 0, buffer.Length);
                if (read <= 0) { break; }

                if (lossPercent > 0 && random.Next(100) < lossPercent) {
                    // Dropping bytes here would corrupt the stream rather than
                    // emulate loss: TCP already acknowledged them to the sender.
                    // What packet loss looks like to an application is a stall of
                    // roughly one retransmission timeout, so that is what is
                    // emulated.
                    var stall = delayMs * 2 + random.Next(50);
                    if (stall > 0) { Thread.Sleep(stall); }
                }

                // Delay before forwarding, which is what a long path does: the
                // bytes spend time in flight. Jitter varies it per chunk, the
                // way a real path does.
                var delay = delayMs;
                if (jitterMs > 0) { delay += random.Next(jitterMs + 1); }
                if (delay > 0) { Thread.Sleep(delay); }

                output.Write(buffer, 0, read);
                output.Flush();
            }
        } catch {
        } finally {
            try { to.Client.Shutdown(SocketShutdown.Send); } catch { }
        }
    }

    public void Stop() {
        stopping = true;
        try { listener.Stop(); } catch { }
    }

    public void Dispose() { Stop(); }
}
"@

$proxy = New-Object DelayProxy $Listen, $targetHost, $targetPort, $DelayMs, $JitterMs, $LossPercent
$proxy.Start()

Write-Host "Latency proxy: 127.0.0.1:$Listen -> ${targetHost}:${targetPort}, ${DelayMs}ms +- ${JitterMs}ms one way, ${LossPercent}% loss"

try {
    Write-Host "Press Ctrl+C to stop."
    while ($true) { Start-Sleep -Seconds 1 }
}
finally {
    $proxy.Dispose()
}
