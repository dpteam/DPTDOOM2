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
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using DPTDOOM.Shared;

namespace DPTDOOM.Launcher
{
    /// <summary>
    /// The launcher shell.
    ///
    /// The interface is real HTML/CSS/JS rendered by the WebBrowser control,
    /// which is MSHTML — the engine that ships with Windows itself. That is a
    /// deliberate choice for a port that still targets Windows XP: WebView2
    /// cannot be installed there, while MSHTML needs no runtime download, no
    /// third-party DLL and no network access.
    ///
    /// JavaScript calls window.external.Post(verb, payload); the host answers
    /// by invoking dptReceive(verb, payload) in the page.
    /// </summary>
    public sealed class LauncherForm : Form
    {
        /// <summary>
        /// Private URL scheme used by the page to call into the host. The
        /// navigation is always cancelled; it is only a message carrier.
        /// </summary>
        private const string BridgeScheme = "dptdoom://";

        private readonly LauncherSettings settings;

        private WebBrowser browser;
        private LauncherState state;
        private Process gameProcess;
        private string lastLogPath;

        public LauncherForm()
        {
            settings = LauncherSettings.Load();
            state = settings.State;

            Text = "DPT.DOOM Launcher";
            ClientSize = new Size(1000, 720);
            MinimumSize = new Size(860, 600);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(0x13, 0x15, 0x1D);

            try
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch
            {
            }

            browser = new WebBrowser();
            browser.Dock = DockStyle.Fill;
            browser.IsWebBrowserContextMenuEnabled = false;
            browser.AllowWebBrowserDrop = false;
            browser.WebBrowserShortcutsEnabled = true;
            browser.ScriptErrorsSuppressed = true;
            browser.DocumentCompleted += OnDocumentCompleted;
            browser.Navigating += OnNavigating;
            Controls.Add(browser);

            LoadPage();
        }

        ////////////////////////////////////////////////////////////
        // Page assembly
        ////////////////////////////////////////////////////////////

        /// <summary>
        /// Merges the three embedded assets into one document. Keeping the CSS
        /// and the script in separate files during development keeps them
        /// readable; at runtime the page is written to disk as one file.
        /// </summary>
        public static string AssemblePage()
        {
            var html = ReadAsset("ui.html");
            html = html.Replace("/*__STYLE__*/", ReadAsset("theme.css"));
            html = html.Replace("/*__SCRIPT__*/", ReadAsset("app.js"));
            return html;
        }

        private static string ReadAsset(string name)
        {
            var assembly = typeof(LauncherForm).Assembly;

            using (var stream = assembly.GetManifestResourceStream("DPTDOOM.Launcher.Assets." + name))
            {
                if (stream == null)
                {
                    throw new Exception("Missing embedded asset: " + name);
                }

                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        /// <summary>
        /// Writes the page into the data folder and navigates to it as a file.
        ///
        /// Loading a real file matters for the bridge: a document assigned
        /// through DocumentText lives at about:blank, and MSHTML does not hand
        /// the host object to that origin on every Windows version. A file in
        /// the Local Machine zone always gets it.
        /// </summary>
        private void LoadPage()
        {
            var directory = Path.Combine(LauncherSettings.GetDataDirectory(), "webview");

            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var path = Path.Combine(directory, "launcher.html");
            File.WriteAllText(path, AssemblePage(), new UTF8Encoding(false));

            browser.Navigate(path);
        }

        private void OnDocumentCompleted(object sender, WebBrowserDocumentCompletedEventArgs e)
        {

            if (Environment.GetEnvironmentVariable("DPTDOOM_LAUNCHER_DEBUG") == "1")
            {
                try
                {
                    var diag = browser.Document.InvokeScript(
                        "eval",
                        new object[] { "'mode=' + document.documentMode + ' url=' + location.href" });

                    File.WriteAllText(
                        Path.Combine(LauncherSettings.GetDataDirectory(), "launcher-debug.txt"),
                        Convert.ToString(diag));
                }
                catch (Exception ex)
                {
                    File.WriteAllText(
                        Path.Combine(LauncherSettings.GetDataDirectory(), "launcher-debug.txt"),
                        "diag failed: " + ex);
                }
            }
        }

        /// <summary>
        /// The page calls the host by navigating to dptdoom://&lt;verb&gt;. The
        /// navigation is cancelled and the verb dispatched; the payload is read
        /// back from the page because it can be arbitrarily long.
        /// </summary>
        private void OnNavigating(object sender, WebBrowserNavigatingEventArgs e)
        {
            var url = e.Url == null ? "" : e.Url.AbsoluteUri;

            if (Environment.GetEnvironmentVariable("DPTDOOM_LAUNCHER_DEBUG") == "1")
            {
                File.AppendAllText(
                    Path.Combine(LauncherSettings.GetDataDirectory(), "launcher-nav.txt"),
                    url + Environment.NewLine);
            }

            if (!url.StartsWith(BridgeScheme, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            e.Cancel = true;

            var verb = url.Substring(BridgeScheme.Length).Trim('/');
            var query = verb.IndexOf('?');
            if (query >= 0)
            {
                verb = verb.Substring(0, query);
            }

            var payload = TakePayload();

            try
            {
                HandleVerb(verb, payload);
            }
            catch (Exception ex)
            {
                SendStatus("Ошибка лаунчера: " + ex.Message, true);
            }
        }

        private string TakePayload()
        {
            if (browser.Document == null)
            {
                return "";
            }

            try
            {
                var result = browser.Document.InvokeScript("dptdoomTakePayload");
                return Convert.ToString(result) ?? "";
            }
            catch
            {
                return "";
            }
        }

        ////////////////////////////////////////////////////////////
        // Host -> page
        ////////////////////////////////////////////////////////////

        public void Send(string verb, string payload)
        {
            if (browser.Document == null)
            {
                return;
            }

            try
            {
                browser.Document.InvokeScript(
                    "dptReceive",
                    new object[] { verb, payload ?? "" });
            }
            catch
            {
                // The document is navigating or has been replaced.
            }
        }

        private void SendStatus(string text, bool error)
        {
            Send("status", (text ?? "") + "\n" + (error ? "1" : "0"));
        }

        private void SendState()
        {
            Send("state", state.Encode());
        }

        private void SendRunning()
        {
            Send("running", IsGameRunning() ? "1" : "0");
        }

        private bool IsGameRunning()
        {
            if (gameProcess == null)
            {
                return false;
            }

            try
            {
                return !gameProcess.HasExited;
            }
            catch
            {
                return false;
            }
        }

        ////////////////////////////////////////////////////////////
        // Verbs from the page
        ////////////////////////////////////////////////////////////

        /// <summary>
        /// Single entry point for everything the page asks for.
        ///
        /// The comparison is **case-insensitive**, and that is not defensive
        /// coding: MSHTML lower-cases the authority part of a URI, so
        /// <c>dptdoom://masterRefresh</c> arrives here as <c>masterrefresh</c>.
        /// A case-sensitive switch silently ignored every camel-case verb —
        /// "save master", "refresh servers" and "open log" all did nothing at all,
        /// which is why the server browser sat on "Querying master…" forever.
        ///
        /// An unknown verb is logged rather than dropped, because a verb that
        /// arrives and is not recognised is exactly the symptom this produced.
        /// </summary>
        public void HandleVerb(string verb, string payload)
        {
            var name = (verb ?? "").Trim();

            Log("verb " + name + " (payload " + (payload == null ? 0 : payload.Length) + " chars)");

            switch (name.ToLowerInvariant())
            {
                case "ready":
                    OnReady();
                    break;

                case "preview":
                    OnPreview(payload);
                    break;

                case "launch":
                    OnLaunch(payload);
                    break;

                case "browse":
                    OnBrowse(payload);
                    break;

                case "profile":
                    OnProfile(payload);
                    break;

                case "themesave":
                    OnThemeSave(payload);
                    break;

                case "mastersave":
                    OnMasterSave(payload);
                    break;

                case "masterrefresh":
                    OnMasterRefresh(payload);
                    break;

                case "openlog":
                    OnOpenLog();
                    break;

                default:
                    Log("  unknown verb '" + name + "' — ignored");
                    break;
            }
        }

        private void OnReady()
        {
            Send("theme", settings.Theme);
            SendState();
            Send("hasLog", lastLogPath != null ? "1" : "0");
            Send("storage",
                (LauncherSettings.IsPortable ? "PORTABLE · " : "APPDATA · ") +
                LauncherSettings.GetDataDirectory() + " · лог: launcher.log");

            Send("running", IsGameRunning() ? "1" : "0");
            SendStatus("Готов. Выбери IWAD и нажми «Запустить игру».", false);
        }

        private void OnPreview(string payload)
        {
            state = LauncherState.Decode(payload);

            if (string.IsNullOrEmpty(state.Iwad))
            {
                Send("preview", "Выбери IWAD, чтобы увидеть аргументы запуска.");
                return;
            }

            try
            {
                Send("preview", GameRunner.BuildArguments(state));
            }
            catch (Exception e)
            {
                Send("preview", e.Message);
            }
        }

        private void OnBrowse(string kind)
        {
            if (kind == "exe")
            {
                var path = Browse("Исполняемый файл", "Программы (*.exe)|*.exe", state.Executable, false);
                if (path != null)
                {
                    state.Executable = path;
                }
            }
            else if (kind == "iwad")
            {
                var path = Browse("Основной IWAD", "Doom WAD или PK3 (*.wad;*.pk3;*.zip)|*.wad;*.pk3;*.zip|Все файлы (*.*)|*.*", state.Iwad, false);
                if (path != null)
                {
                    state.Iwad = path;
                }
            }
            else if (kind == "pwad")
            {
                var paths = Browse("PWAD", "Doom WAD или PK3 (*.wad;*.pk3;*.zip)|*.wad;*.pk3;*.zip|Все файлы (*.*)|*.*", null, true);
                if (paths != null)
                {
                    foreach (var path in paths.Split('\n'))
                    {
                        if (path.Length > 0 && state.Pwads.Count < 16 && !state.Pwads.Contains(path))
                        {
                            state.Pwads.Add(path);
                        }
                    }
                }
            }

            Persist();
            SendState();
        }

        private string Browse(string title, string filter, string current, bool multi)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = title;
                dialog.Filter = filter;
                dialog.CheckFileExists = true;
                dialog.Multiselect = multi;
                dialog.InitialDirectory = GuessDirectory(current);

                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return null;
                }

                return multi ? string.Join("\n", dialog.FileNames) : dialog.FileName;
            }
        }

        private string GuessDirectory(string current)
        {
            try
            {
                var path = !string.IsNullOrEmpty(current) ? current : state.Iwad;
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    return Path.GetDirectoryName(path);
                }
            }
            catch
            {
            }

            var exe = GameRunner.FindGameExe(state);
            if (exe != null)
            {
                return Path.GetDirectoryName(exe);
            }

            return AppDomain.CurrentDomain.BaseDirectory;
        }

        private void OnLaunch(string payload)
        {
            state = LauncherState.Decode(payload);

            if (IsGameRunning())
            {
                SendStatus("Игра уже запущена.", true);
                return;
            }

            try
            {
                lastLogPath = Path.Combine(GameRunner.GetSessionDirectory(), "game.log");
                gameProcess = GameRunner.Start(state, lastLogPath);

                gameProcess.EnableRaisingEvents = true;
                gameProcess.Exited += OnGameExited;

                Persist();
                Send("hasLog", "1");
                Send("running", "1");

                var mode = state.Mode == 1 ? "хост" : (state.Mode == 2 ? "подключение" : "одиночная");
                SendStatus("Игра запущена (" + mode + "). Лог: " + lastLogPath, false);
            }
            catch (Exception e)
            {
                Send("running", "0");
                SendStatus(e.Message, true);
            }
        }

        private void OnGameExited(object sender, EventArgs e)
        {
            try
            {
                BeginInvoke((MethodInvoker)(() =>
                {
                    Send("running", "0");
                    SendStatus("Игра завершена. Лог: " + lastLogPath, false);
                }));
            }
            catch
            {
                // The launcher is closing.
            }
        }

        private void OnProfile(string action)
        {
            if (action == "save")
            {
                settings.State = state;
                settings.SaveProfile(state.Slot);
                SendStatus("Профиль " + state.Slot + " сохранён.", false);
            }
            else if (action == "load")
            {
                if (settings.LoadProfile(state.Slot))
                {
                    state = settings.State;
                    SendState();
                    SendStatus("Профиль " + state.Slot + " загружен.", false);
                }
                else
                {
                    SendStatus("Профиль " + state.Slot + " пуст. Сначала сохрани его.", true);
                }
            }
            else if (action == "select")
            {
                SendStatus(
                    "Выбран профиль " + state.Slot +
                    ". «Загрузить» прочитает его, «Сохранить» запишет текущие поля.",
                    false);
            }
        }

        private void OnThemeSave(string theme)
        {
            settings.Theme = theme == "dark" ? "dark" : "light";
            settings.State = state;
            settings.Save();
        }

        /// <summary>
        /// Appends one line to the launcher log.
        ///
        /// A launcher is a program whose failures are invisible: the window stays
        /// up, a status line stays on whatever it said last, and there is nothing
        /// to look at afterwards. The master query hanging on "Querying master…"
        /// was exactly that — a verb arriving with the wrong case and being
        /// dropped without a word. So every verb, every network attempt and every
        /// error goes to a file with a timestamp.
        ///
        /// The log is deliberately plain text and small: it is meant to be opened
        /// by a person after something went wrong, not parsed by a tool.
        /// </summary>
        private static void Log(string message)
        {
            try
            {
                var path = Path.Combine(LauncherSettings.GetDataDirectory(), "launcher.log");

                // Start fresh once the file gets big. A launcher log that grows
                // without limit is a support problem of its own.
                if (File.Exists(path) && new FileInfo(path).Length > 512 * 1024)
                {
                    File.Delete(path);
                }

                File.AppendAllText(
                    path,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + message +
                    Environment.NewLine);
            }
            catch
            {
                // Logging must never be the reason the launcher fails.
            }
        }

        private void OnMasterSave(string endpoint)
        {
            settings.Master = (endpoint ?? "").Trim();
            state.Master = settings.Master;
            settings.State = state;
            settings.Save();

            SendState();
            SendStatus("Master сохранён: " + settings.Master, false);
        }

        private void OnMasterRefresh(string endpoint)
        {
            string host;
            int port;

            Log("master refresh requested: '" + endpoint + "'");

            if (!TryParseEndpoint(endpoint, out host, out port))
            {
                Log("  rejected: cannot parse the endpoint");
                SendStatus("Некорректный адрес master: " + (endpoint ?? "(пусто)") +
                           ". Нужен вид host:port, например play.example.com:25135.", true);
                return;
            }

            Log("  parsed " + host + ":" + port);

            var thread = new Thread(() =>
            {
                var started = Environment.TickCount;

                try
                {
                    var servers = MasterClient.List(host, port, MasterClient.DefaultTimeoutMs);

                    var elapsed = Environment.TickCount - started;

                    Log("  ok: " + servers.Count + " session(s) in " + elapsed + " ms");

                    // Probed after the listing, on this worker thread: the page
                    // stays responsive and the numbers arrive with the rows.
                    var pings = MeasurePings(servers);

                    Log("  probed " + servers.Count + " game port(s)");

                    BeginInvoke((MethodInvoker)(() =>
                    {
                        Send("servers", EncodeServers(servers, pings));
                        SendStatus(servers.Count == 0
                            ? "Список пуст. Хост мог ещё не опубликоваться."
                            : "Получено сессий: " + servers.Count + " (" + elapsed + " мс).", false);
                    }));
                }
                catch (Exception e)
                {
                    var elapsed = Environment.TickCount - started;

                    Log("  failed after " + elapsed + " ms: " + e.GetType().Name + ": " + e.Message);

                    BeginInvoke((MethodInvoker)(() =>
                        SendStatus(DescribeMasterFailure(host, port, e), true)));
                }
            });

            thread.IsBackground = true;
            thread.Name = "DPTDOOM launcher master query";
            thread.Start();
        }

        /// <summary>
        /// Turns a master failure into something a person can act on.
        ///
        /// "Master недоступен: Connection refused" is technically true and
        /// useless. The three failures a launcher actually sees are a wrong
        /// address, a closed port (usually a firewall or a master that is not
        /// running) and a name that does not resolve, and each has a different
        /// thing for the user to check.
        /// </summary>
        private static string DescribeMasterFailure(string host, int port, Exception error)
        {
            var socket = error as SocketException;

            if (socket != null)
            {
                switch (socket.SocketErrorCode)
                {
                    case SocketError.ConnectionRefused:
                        return "Master " + host + ":" + port + " отклонил подключение. " +
                               "Похоже, сервер не запущен или порт закрыт.";

                    case SocketError.TimedOut:
                    case SocketError.WouldBlock:
                        return "Master " + host + ":" + port + " не ответил за " +
                               (MasterClient.DefaultTimeoutMs / 1000) + " с. " +
                               "Порт закрыт файрволом, или адрес неверный.";

                    case SocketError.HostNotFound:
                    case SocketError.NoData:
                        return "Имя " + host + " не находится в DNS. Проверь адрес.";

                    case SocketError.NetworkUnreachable:
                    case SocketError.HostUnreachable:
                        return "Сеть до " + host + " недоступна. Проверь подключение.";
                }
            }

            return "Master " + host + ":" + port + ": " + error.Message;
        }

        /// <summary>
        /// How long to wait for one game port to answer a probe, in milliseconds.
        ///
        /// Short on purpose: this runs for every listed session at once while the
        /// player watches a list, and a server that cannot complete a TCP handshake
        /// in a second is not worth showing a number for. An unanswered probe
        /// displays as "--" rather than as a slow server.
        /// </summary>
        private const int PingTimeoutMs = 1000;

        /// <summary>
        /// Measures the round trip to each listed game port.
        ///
        /// There is no ICMP ping here and there cannot be: a raw socket needs
        /// administrator rights, and the game speaks TCP. What a player wants to
        /// know is "how far does a tic have to travel", and the TCP handshake is
        /// exactly that number - it is the same path and the same round trip the
        /// session will use.
        ///
        /// Connecting and closing is harmless: the host sees a peer that sent no
        /// handshake and closes it (HostRobustnessTest pins that), so a browser
        /// refresh cannot disturb a running game.
        ///
        /// Probes run in parallel. Sequentially this would take a second per
        /// unreachable server, and a full list would take longer than the refresh
        /// itself.
        /// </summary>
        private static int[] MeasurePings(List<MasterServerEntry> servers)
        {
            var results = new int[servers.Count];

            for (var i = 0; i < results.Length; i++)
            {
                results[i] = -1;
            }

            if (servers.Count == 0)
            {
                return results;
            }

            var threads = new Thread[servers.Count];

            for (var index = 0; index < servers.Count; index++)
            {
                var slot = index;
                var address = servers[slot].AddressText;
                var port = servers[slot].Info.Port;

                threads[slot] = new Thread(() =>
                {
                    results[slot] = Probe(address, port);
                });

                threads[slot].IsBackground = true;
                threads[slot].Start();
            }

            foreach (var thread in threads)
            {
                thread.Join(PingTimeoutMs + 500);
            }

            return results;
        }

        /// <summary>Round trip in milliseconds, or -1 when the port does not answer.</summary>
        private static int Probe(string host, int port)
        {
            var watch = Stopwatch.StartNew();

            using (var client = new TcpClient())
            {
                try
                {
                    var result = client.BeginConnect(host, port, null, null);

                    if (!result.AsyncWaitHandle.WaitOne(PingTimeoutMs, false))
                    {
                        return -1;
                    }

                    client.EndConnect(result);

                    watch.Stop();

                    // A floor of 1: zero would be indistinguishable from "not
                    // measured" in the payload, and a local server genuinely is
                    // about a millisecond.
                    return Math.Max(1, (int)watch.ElapsedMilliseconds);
                }
                catch (Exception)
                {
                    return -1;
                }
            }
        }

        private static string EncodeServers(List<MasterServerEntry> servers, int[] pings)
        {
            var builder = new StringBuilder();

            for (var i = 0; i < servers.Count; i++)
            {
                var entry = servers[i];

                if (builder.Length > 0)
                {
                    builder.Append('\n');
                }

                builder.Append(Sanitize(entry.Info.Name)).Append('\t');
                builder.Append(entry.AddressText).Append('\t');
                builder.Append(entry.Info.Port).Append('\t');
                builder.Append(Sanitize(entry.Info.MapText)).Append('\t');
                builder.Append(entry.Info.Skill).Append('\t');
                builder.Append(entry.LeaseSecondsLeft).Append('\t');
                builder.Append(MasterProtocol.IdToHex(entry.Id)).Append('\t');
                builder.Append(entry.RelayAvailable ? "1" : "0").Append('\t');
                builder.Append(pings != null && i < pings.Length ? pings[i] : -1);
            }

            return builder.ToString();
        }

        /// <summary>
        /// Master strings come from other players and are untrusted. The page
        /// inserts them as text, and control characters are stripped here so
        /// they cannot break the field encoding either.
        /// </summary>
        private static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "";
            }

            var builder = new StringBuilder(value.Length);

            foreach (var ch in value)
            {
                builder.Append(ch < 0x20 || ch > 0x7E ? '?' : ch);
            }

            return builder.ToString();
        }

        private void OnOpenLog()
        {
            if (lastLogPath == null)
            {
                SendStatus("Лога пока нет: игра ещё не запускалась.", true);
                return;
            }

            try
            {
                if (File.Exists(lastLogPath))
                {
                    Process.Start("notepad.exe", "\"" + lastLogPath + "\"");
                }
                else
                {
                    Process.Start("explorer.exe", "\"" + Path.GetDirectoryName(lastLogPath) + "\"");
                }
            }
            catch (Exception e)
            {
                SendStatus("Не удалось открыть лог: " + e.Message, true);
            }
        }

        private static bool TryParseEndpoint(string text, out string host, out int port)
        {
            host = (text ?? "").Trim();
            port = MasterProtocol.DefaultPort;

            var colon = host.LastIndexOf(':');
            if (colon > 0)
            {
                int parsed;
                if (int.TryParse(host.Substring(colon + 1), out parsed))
                {
                    port = parsed;
                    host = host.Substring(0, colon);
                }
            }

            return host.Length > 0 && port > 0 && port <= 65535;
        }

        private void Persist()
        {
            settings.State = state;
            settings.Save();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            Persist();
            base.OnFormClosing(e);
        }
    }
}
