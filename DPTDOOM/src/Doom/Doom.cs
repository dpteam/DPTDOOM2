//
// Copyright (C) 1993-1996 Id Software, Inc.
// Copyright (C) 2019-2020 Nobuaki Tanaka
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
using System.IO;
using DPTDOOM.Audio;
using DPTDOOM.Net;
using DPTDOOM.Video;
using DPTDOOM.UserInput;

namespace DPTDOOM
{
    public class Doom
    {
        private CommandLineArgs args;
        private Config config;
        private GameContent content;
        private IVideo video;
        private ISound sound;
        private IMusic music;
        private IUserInput userInput;

        private List<DoomEvent> events;

        private GameOptions options;

        private DoomMenu menu;

        private OpeningSequence opening;

        private DemoPlayback demoPlayback;

        private TicCmd[] cmds;
        private DoomGame game;

        private WipeEffect wipeEffect;
        private bool wiping;

        private DoomState currentState;
        private DoomState nextState;
        private bool needWipe;

        private bool sendPause;

        /// <summary>
        /// In-game text entry. Per machine, not per world: what one player is
        /// typing must never be part of the shared state.
        /// </summary>
        private readonly Chat chat = new Chat();

        private bool quit;
        private string quitMessage;

        private bool mouseGrabbed;

        private INetDriver netDriver;

        /// <summary>
        /// Vertical look, in virtual pixels of a 320x200 screen; positive is up.
        ///
        /// Client-side state, like the chat line: it is not in the world, not in a
        /// TicCmd, not in a demo and not in a save. Two players in the same session
        /// can look at different things without the simulation noticing, which is
        /// exactly why it lives here and not on the player.
        /// </summary>
        private Fixed viewPitch;

        /// <summary>
        /// How far the view may tilt, in virtual pixels.
        ///
        /// A third of the screen. Beyond that the shear starts to show as stretched
        /// flats near the edge, and the pitch stops reading as "looking up" and
        /// starts reading as a broken projection.
        /// </summary>
        public static readonly int PitchLimit = 70;

        public Fixed ViewPitch => viewPitch;

        /// <summary>
        /// True while the status bar is showing the reconnect countdown, so it
        /// can be cleared once instead of being rewritten every frame.
        /// </summary>
        private bool reconnectMessageShown;

        private DemoRecorder recorder;
        private string recordPath;

        private DoomBot bot;
        private bool botMode;

        // Local co-op bots: extra players in the same world, driven by the same
        // DoomBot, so one person can test a co-op session alone.
        private DoomBot[] coopBots;
        private int coopBotCount;
        private World botWorld;

        // What the bots have learned about this map, shared by all of them.
        private BotBrain botBrain;
        private string botBrainKey;

        // Optional per-tic record of where the bots went, for -botdump.
        private BotTrace botTrace;
        private int botTraceIndex;

        public Doom(CommandLineArgs args, Config config, GameContent content, IVideo video, ISound sound, IMusic music, IUserInput userInput)
            : this(args, config, content, video, sound, music, userInput, null)
        {
        }

        public Doom(
            CommandLineArgs args,
            Config config,
            GameContent content,
            IVideo video,
            ISound sound,
            IMusic music,
            IUserInput userInput,
            INetDriver netDriver)
        {
            video = video ?? NullVideo.GetInstance();
            sound = sound ?? NullSound.GetInstance();
            music = music ?? NullMusic.GetInstance();
            userInput = userInput ?? NullUserInput.GetInstance();

            this.args = args;
            this.config = config;
            this.content = content;
            this.video = video;
            this.sound = sound;
            this.music = music;
            this.userInput = userInput;
            this.netDriver = netDriver;

            botMode = args.bots.Present;

            // Presentation switches from the command line. Applied here because this
            // is the one place that has both the arguments and the config: a saved
            // config keeps the player's own choice whenever the flag is absent.
            if (args.crosshair.Present)
            {
                config.hud_crosshair = MathEx.Clamp(args.crosshair.Value, 0, 3);
            }

            if (args.freelook.Present)
            {
                config.view_freelook = true;
            }

            // A starting tilt, for screenshots and for reproducing a bug report.
            // Setting it implies free look: a tilt with the setting off would be
            // undone by the first frame.
            if (args.pitch.Present)
            {
                config.view_freelook = true;
                viewPitch = Fixed.FromInt(MathEx.Clamp(args.pitch.Value, -PitchLimit, PitchLimit));
            }

            if (args.coopbots.Present)
            {
                coopBotCount = MathEx.Clamp(args.coopbots.Value, 1, Player.MaxPlayerCount - 1);

                if (netDriver != null && netDriver.LocalPlayer != 0)
                {
                    // A guest cannot drive bots: they would run here and on the
                    // host both, and the two would disagree the moment anything
                    // random or timing-dependent happened. Only the host owns them.
                    throw new Exception("-coopbots can only be started by the host.");
                }

                if (botMode)
                {
                    throw new Exception("-bots and -coopbots cannot be combined.");
                }
            }

            if (args.record.Present)
            {
                // A demo is a deterministic record of tic commands; a networked
                // game has no single authoritative command stream, so recording
                // it would produce something that does not replay.
                if (netDriver != null)
                {
                    throw new Exception("-record cannot be used in a network game.");
                }

                recordPath = ResolveRecordPath(args.record.Value);
            }

            events = new List<DoomEvent>();

            options = new GameOptions(args, content);
            options.Video = video;
            options.Sound = sound;
            options.Music = music;
            options.UserInput = userInput;

            menu = new DoomMenu(this);

            opening = new OpeningSequence(content, options);

            cmds = new TicCmd[Player.MaxPlayerCount];
            for (var i = 0; i < Player.MaxPlayerCount; i++)
            {
                cmds[i] = new TicCmd();
            }
            game = new DoomGame(content, options);

            wipeEffect = new WipeEffect(video.WipeBandCount, video.WipeHeight);
            wiping = false;

            currentState = DoomState.None;
            nextState = DoomState.Opening;
            needWipe = false;

            sendPause = false;

            quit = false;
            quitMessage = null;

            mouseGrabbed = false;

            CheckGameArgs();
        }

        /// <summary>
        /// One line per bot for the on-screen overlay, or null when no bots are
        /// playing. Kept short: the overlay sits over the game.
        /// </summary>
        public string BotReport
        {
            get
            {
                if (coopBots == null && bot == null)
                {
                    return null;
                }

                var text = "";

                if (coopBots != null)
                {
                    for (var i = 1; i < coopBots.Length; i++)
                    {
                        var runner = coopBots[i];

                        if (runner == null)
                        {
                            continue;
                        }

                        if (text.Length > 0)
                        {
                            text += "\n";
                        }

                        text += "Бот " + i + ": " + runner.Style +
                                "  " + runner.VisitedSectors + "/" + runner.TotalSectors;
                    }
                }

                if (bot != null)
                {
                    if (text.Length > 0)
                    {
                        text += "\n";
                    }

                    text += "Бот (игрок): " + bot.Style +
                            "  " + bot.VisitedSectors + "/" + bot.TotalSectors;
                }

                if (botBrain != null)
                {
                    text += "\nЛучшее время: " + botBrain.Describe();
                }

                return text;
            }
        }

        ////////////////////////////////////////////////////////////
        // Bot brain
        ////////////////////////////////////////////////////////////

        /// <summary>
        /// Knowledge of the current map, shared by every bot in the game.
        /// </summary>
        private BotBrain GetBotBrain()
        {
            var key = CurrentMapKey();

            if (botBrain != null && botBrainKey == key)
            {
                return botBrain;
            }

            botBrainKey = key;

            if (recordPath != null || netDriver != null)
            {
                // A demo has to replay the same commands every time, and a brain
                // that learns between runs would make the recording depend on
                // the state of a text file. A network game needs the same
                // commands on every machine, which a local brain cannot promise.
                botBrain = BotBrain.Disabled;
                return botBrain;
            }

            var folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bots");

            botBrain = new BotBrain(folder, key);

            return botBrain;
        }

        private BotStyle ChooseBotStyle(int index)
        {
            // -botstyle forces one style. Used for comparing the styles against
            // each other and for traces, where a run that changes its mind
            // halfway through is hard to read.
            if (args.botstyle.Present)
            {
                return (BotStyle)MathEx.Clamp(args.botstyle.Value, 0, BotBrain.StyleCount - 1);
            }

            return GetBotBrain().ChooseStyle(index * 7919 + 1);
        }

        /// <summary>
        /// Key that identifies a level: its name plus a fingerprint of its
        /// geometry, so a PWAD that also calls its level MAP01 does not inherit
        /// a brain trained on a different map with that name.
        /// </summary>
        private string CurrentMapKey()
        {
            var name = options.GameMode == GameMode.Commercial
                ? "MAP" + options.Map.ToString("00")
                : "E" + options.Episode + "M" + options.Map;

            var map = game.World != null ? game.World.Map : null;

            if (map == null)
            {
                return name;
            }

            long fingerprint = map.Sectors.Length * 1000003L +
                               map.Lines.Length * 31L +
                               map.Vertices.Length;

            if (map.Vertices.Length > 0)
            {
                fingerprint = fingerprint * 31 + map.Vertices[0].X.Data;
                fingerprint = fingerprint * 31 + map.Vertices[map.Vertices.Length - 1].Y.Data;
            }

            return name + "-" + (fingerprint & 0xFFFFFF).ToString("x6");
        }

        /// <summary>
        /// Hands the finished run of every bot to the brain.
        ///
        /// Only a run the bot finished itself counts as a completion. If the
        /// human player left the level first, the level ends under the bot's
        /// feet and its time says nothing about how well it played — scoring
        /// that would teach the brain a false lesson.
        /// </summary>
        private void RecordBotRuns()
        {
            var brain = GetBotBrain();

            if (!brain.Enabled)
            {
                return;
            }

            // A level that goes back to Level after the wipe was restarted, not
            // finished: in single player that only happens when the player died
            // and pressed Use. A real exit moves the game to the intermission.
            var completed = game.State != GameState.Level;

            // Persist what the bots learned about danger on this map before the
            // runs are scored, because scoring writes the brain file: setting the
            // heat afterwards would leave it out of the save entirely, which is
            // exactly what happened on the first attempt.
            var worst = (BotDanger)null;

            if (coopBots != null)
            {
                for (var i = 1; i < coopBots.Length; i++)
                {
                    if (coopBots[i] != null && coopBots[i].Danger != null)
                    {
                        worst = coopBots[i].Danger;
                    }
                }
            }

            if (bot != null && bot.Danger != null)
            {
                worst = bot.Danger;
            }

            if (worst != null)
            {
                brain.SetDanger(worst);
            }

            if (coopBots != null)
            {
                for (var i = 1; i < coopBots.Length; i++)
                {
                    RecordBotRun(brain, coopBots[i], completed);
                }
            }

            if (bot != null)
            {
                RecordBotRun(brain, bot, completed);
            }
        }

        private static void RecordBotRun(BotBrain brain, DoomBot runner, bool completed)
        {
            if (runner == null)
            {
                return;
            }

            // A walkover exit ends the level without any button being pressed, so
            // a bot standing at the exit when it ended has finished too.
            var finished = completed && (runner.Finished || runner.AtExit);
            var tics = runner.Finished ? runner.FinishedTics : (finished ? runner.LevelTime : 0);

            if (!finished && completed && runner.Health > 0)
            {
                // The level was completed, but not by this bot: the human player
                // or a faster bot reached the exit first. Its time says nothing
                // about how good its style is, so the run is skipped rather than
                // scored - the rule the owner asked for.
                Console.WriteLine(
                    "Bot: " + runner.Style + " run not counted (level ended before it finished)");

                return;
            }

            brain.RecordRun(
                runner.Style, tics, finished,
                runner.RouteToExit, runner.RouteToExitLength);

            Console.WriteLine(
                "Bot: " + runner.Style + " " +
                (finished ? "reached the exit in " + BotBrain.FormatTime(tics) : "did not finish (died)") +
                " | best " + brain.Describe());
        }
        /// <summary>
        /// Writes the bot trace if one is being recorded and has not been written
        /// yet. Called when a level ends, and by the headless runner when the tic
        /// budget runs out first — otherwise a run that never finishes would
        /// leave no trace, which is exactly the run worth looking at.
        /// </summary>
        public void FlushBotTrace()
        {
            if (botTrace == null)
            {
                return;
            }

            // Each level gets its own file. A trace that spans levels would mix
            // two different maps into one picture, and the interesting level is
            // usually the one the bot struggled with, not the last one.
            var target = args.botdump.Value;

            if (botTraceIndex > 0)
            {
                var directory = Path.GetDirectoryName(target);
                var name = Path.GetFileNameWithoutExtension(target);
                var extension = Path.GetExtension(target);

                var numbered = name + ".level" + (botTraceIndex + 1) + extension;

                target = string.IsNullOrEmpty(directory)
                    ? numbered
                    : Path.Combine(directory, numbered);
            }

            botTraceIndex++;

            botTrace.Write(target);
            Console.WriteLine("Bot trace: " + target);

            botTrace = null;
        }

        /// <summary>
        /// The bot's remembered danger for this map, ready to use.
        ///
        /// Danger is per map, not per bot, because it is knowledge about the
        /// level: if one bot learned that the crusher room hurts, the others
        /// benefit too. That is also why it lives in the brain file next to the
        /// route.
        /// </summary>
        private BotDanger LoadDanger(int sectorCount)
        {
            var danger = new BotDanger(sectorCount);
            var brain = GetBotBrain();

            if (brain.Enabled)
            {
                danger.Deserialize(brain.DangerText);
            }

            return danger;
        }

        private void CheckGameArgs()
        {
            if (netDriver != null)
            {
                netDriver.Configure(options);

                nextState = DoomState.Game;
                game.DeferedInitNew();
                return;
            }

            if (coopBotCount > 0)
            {
                // Extra players have to exist before the level is built: the
                // spawn pass only creates a body for a player already marked as
                // in the game.
                for (var i = 1; i <= coopBotCount; i++)
                {
                    options.Players[i].InGame = true;
                }

                options.NetGame = false;

                nextState = DoomState.Game;

                if (args.warp.Present)
                {
                    options.Episode = args.warp.Value.Item1;
                    options.Map = args.warp.Value.Item2;
                }
                else if (args.episode.Present)
                {
                    options.Episode = args.episode.Value;
                    options.Map = 1;
                }

                game.DeferedInitNew();
                return;
            }

            if (args.warp.Present)
            {
                nextState = DoomState.Game;
                options.Episode = args.warp.Value.Item1;
                options.Map = args.warp.Value.Item2;
                game.DeferedInitNew();
            }
            else if (args.episode.Present)
            {
                nextState = DoomState.Game;
                options.Episode = args.episode.Value;
                options.Map = 1;
                game.DeferedInitNew();
            }

            if (args.skill.Present)
            {
                options.Skill = (GameSkill)(args.skill.Value - 1);
            }

            if (args.deathmatch.Present)
            {
                options.Deathmatch = 1;
            }

            if (args.altdeath.Present)
            {
                options.Deathmatch = 2;
            }

            if (args.fast.Present)
            {
                options.FastMonsters = true;
            }

            if (args.respawn.Present)
            {
                options.RespawnMonsters = true;
            }

            if (args.nomonsters.Present)
            {
                options.NoMonsters = true;
            }

            if (args.loadgame.Present)
            {
                nextState = DoomState.Game;
                game.LoadGame(args.loadgame.Value);
            }

            if (args.playdemo.Present)
            {
                nextState = DoomState.DemoPlayback;
                demoPlayback = new DemoPlayback(args, content, options, args.playdemo.Value);
            }

            if (args.timedemo.Present)
            {
                nextState = DoomState.DemoPlayback;
                demoPlayback = new DemoPlayback(args, content, options, args.timedemo.Value);
            }
        }

        public void NewGame(GameSkill skill, int episode, int map)
        {
            game.DeferedInitNew(skill, episode, map);
            nextState = DoomState.Game;
        }

        public void EndGame()
        {
            nextState = DoomState.Opening;
        }

        private void DoEvents()
        {
            if (wiping)
            {
                return;
            }

            foreach (var e in events)
            {
                // Chat first, and before the menu: while a player is typing, every
                // key belongs to the message. This is also what keeps "idkfa" typed
                // into the chat from enabling cheats.
                if (chat.Handle(e))
                {
                    if (chat.Submitted)
                    {
                        SendChat(chat.Text);
                        chat.Close();
                    }

                    continue;
                }

                if (menu.DoEvent(e))
                {
                    continue;
                }

                if (e.Type == EventType.KeyDown)
                {
                    if (CheckFunctionKey(e.Key))
                    {
                        continue;
                    }
                }

                if (currentState == DoomState.Game)
                {
                    if (netDriver != null && e.Key == DoomKey.T && e.Type == EventType.KeyDown)
                    {
                        // Chat needs somebody to talk to. A single-player session
                        // has no transport, so the key stays inert rather than
                        // opening a text box that cannot send anything.
                        chat.Open();
                        continue;
                    }

                    if (e.Key == DoomKey.Pause && e.Type == EventType.KeyDown)
                    {
                        sendPause = true;
                        continue;
                    }

                    if (game.DoEvent(e))
                    {
                        continue;
                    }
                }
                else if (currentState == DoomState.DemoPlayback)
                {
                    demoPlayback.DoEvent(e);
                }
            }

            events.Clear();
        }

        /// <summary>
        /// Sends a chat line and shows it locally.
        ///
        /// The local echo does not come back over the network: a player who sends
        /// a message expects to see it immediately, and a round trip would also
        /// make the message appear twice on the host, which is both a sender and a
        /// relay.
        /// </summary>
        private void SendChat(string text)
        {
            if (string.IsNullOrEmpty(text) || netDriver == null)
            {
                return;
            }

            netDriver.SendChat(text);

            ShowChat("You: " + text);
        }

        /// <summary>
        /// Displays a chat line the way a pickup message appears.
        ///
        /// Reusing the message line rather than adding a new HUD element is the
        /// smallest change that is still readable, and it inherits the existing
        /// font, scaling and positioning - which is the part that would otherwise
        /// have to be written and tested from scratch.
        /// </summary>
        private void ShowChat(string text)
        {
            if (game == null || game.World == null)
            {
                return;
            }

            var player = game.World.ConsolePlayer;

            player.SendMessage(text);

            // Longer than a pickup message: 4 seconds is enough for a medikit and
            // too short to read a sentence.
            player.MessageTime = 8 * GameConst.TicRate;
        }

        private void PumpChat()
        {
            if (netDriver == null || game == null || game.World == null)
            {
                return;
            }

            string text;

            while (netDriver.TryTakeChat(out text))
            {
                ShowChat(text);
            }

            // Show what is being typed. Doing it here, once per tic, keeps the
            // message line honest: the alternative is an overlay element that has
            // to be cleared on every path out of chat mode.
            if (chat.Active && game.World != null)
            {
                var player = game.World.ConsolePlayer;

                player.SendMessage("Say: " + chat.Text + "_");
                player.MessageTime = 8 * GameConst.TicRate;
            }

            // Movement is read from key state rather than from events, so typing
            // "wasd" would walk the player around. Suppressing the whole input is
            // the only place that can be handled, since the polling happens below
            // the interface.
            if (userInput != null && userInput.Suppressed != chat.Active)
            {
                userInput.Suppressed = chat.Active;
            }
        }

        /// <summary>
        /// Applies vertical look for this frame.
        ///
        /// Drained once per frame, not once per tic, so the view moves at the frame
        /// rate even though the input is polled per tic - a 35 Hz tilt looks stepped
        /// next to a 70 Hz render.
        ///
        /// Turning free look off recentres immediately. Leaving the view tilted
        /// after the switch would strand the player looking at the ceiling with no
        /// key that brings it back.
        /// </summary>
        private void UpdateViewPitch()
        {
            if (userInput == null || !config.view_freelook)
            {
                viewPitch = Fixed.Zero;
                return;
            }

            bool center;
            var delta = userInput.TakeViewPitch(out center);

            if (center)
            {
                viewPitch = Fixed.Zero;
                return;
            }

            if (delta != 0)
            {
                var limit = Fixed.FromInt(PitchLimit);
                var next = viewPitch + Fixed.FromInt(delta);

                if (next > limit)
                {
                    next = limit;
                }
                else if (next < -limit)
                {
                    next = -limit;
                }

                viewPitch = next;
            }
        }

        private bool CheckFunctionKey(DoomKey key)
        {
            switch (key)
            {
                case DoomKey.F1:
                    menu.ShowHelpScreen();
                    return true;

                case DoomKey.F2:
                    menu.ShowSaveScreen();
                    return true;

                case DoomKey.F3:
                    menu.ShowLoadScreen();
                    return true;

                case DoomKey.F4:
                    menu.ShowVolumeControl();
                    return true;

                case DoomKey.F6:
                    menu.QuickSave();
                    return true;

                case DoomKey.F7:
                    if (currentState == DoomState.Game)
                    {
                        menu.EndGame();
                    }
                    else
                    {
                        options.Sound.StartSound(Sfx.OOF);
                    }
                    return true;

                case DoomKey.F8:
                    video.DisplayMessage = !video.DisplayMessage;
                    if (currentState == DoomState.Game && game.State == GameState.Level)
                    {
                        string msg;
                        if (video.DisplayMessage)
                        {
                            msg = DoomInfo.Strings.MSGON;
                        }
                        else
                        {
                            msg = DoomInfo.Strings.MSGOFF;
                        }
                        game.World.ConsolePlayer.SendMessage(msg);
                    }
                    menu.StartSound(Sfx.SWTCHN);
                    return true;

                case DoomKey.F9:
                    menu.QuickLoad();
                    return true;

                case DoomKey.F10:
                    menu.Quit();
                    return true;

                case DoomKey.F11:
                    var gcl = video.GammaCorrectionLevel;
                    gcl++;
                    if (gcl > video.MaxGammaCorrectionLevel)
                    {
                        gcl = 0;
                    }
                    video.GammaCorrectionLevel = gcl;
                    if (currentState == DoomState.Game && game.State == GameState.Level)
                    {
                        string msg;
                        if (gcl == 0)
                        {
                            msg = DoomInfo.Strings.GAMMALVL0;
                        }
                        else
                        {
                            msg = "Gamma correction level " + gcl;
                        }
                        game.World.ConsolePlayer.SendMessage(msg);
                    }
                    return true;

                case DoomKey.Add:
                case DoomKey.Quote:
                case DoomKey.Equal:
                    if (currentState == DoomState.Game &&
                        game.State == GameState.Level &&
                        game.World.AutoMap.Visible)
                    {
                        return false;
                    }
                    else
                    {
                        video.WindowSize = Math.Min(video.WindowSize + 1, video.MaxWindowSize);
                        sound.StartSound(Sfx.STNMOV);
                        return true;
                    }

                case DoomKey.Subtract:
                case DoomKey.Hyphen:
                case DoomKey.Semicolon:
                    if (currentState == DoomState.Game &&
                        game.State == GameState.Level &&
                        game.World.AutoMap.Visible)
                    {
                        return false;
                    }
                    else
                    {
                        video.WindowSize = Math.Max(video.WindowSize - 1, 0);
                        sound.StartSound(Sfx.STNMOV);
                        return true;
                    }

                default:
                    return false;
            }
        }

        public UpdateResult Update()
        {
            DoEvents();

            // Chat arrives on the network thread and is displayed here, on the
            // game thread, so the message line is only ever touched in one place.
            PumpChat();

            UpdateViewPitch();

            if (!wiping)
            {
                menu.Update();

                if (nextState != currentState)
                {
                    if (nextState != DoomState.Opening)
                    {
                        opening.Reset();
                    }

                    if (nextState != DoomState.DemoPlayback)
                    {
                        demoPlayback = null;
                    }

                    currentState = nextState;

                    if (currentState == DoomState.Game && recordPath != null && recorder == null)
                    {
                        // Created here rather than in the constructor because
                        // the options are only final once the game actually
                        // starts (skill, episode and map come from the command
                        // line or from a loaded save).
                        recorder = new DemoRecorder(options);
                    }
                }

                // Bots are created here rather than at the state change above:
                // the world does not exist yet when the state flips, it is built
                // by the first Update of the level. Doing it at the transition
                // left coopBots null and the bots simply never moved — and the
                // same mistake left -bots with no bot at all, so the player
                // stood still while the flag claimed it was being played.
                if (currentState == DoomState.Game && (coopBotCount > 0 || botMode))
                {
                    if (!ReferenceEquals(game.World, botWorld))
                    {
                        // A new level builds a new World, so bots that point at
                        // the old one are stale. Rebuild them for the new map.
                        botWorld = game.World;
                        coopBots = null;
                        bot = null;
                    }

                    if (game.World != null && botMode && bot == null)
                    {
                        // A new level invalidates the bot's map knowledge.
                        bot = new DoomBot(
                            game.World, game.World.ConsolePlayer, null,
                            GetBotBrain(), ChooseBotStyle(0),
                            BotTemperament.FromSeed(BotTemperament.SeedFor(CurrentMapKey(), 0)),
                            LoadDanger(game.World.Map.Sectors.Length));
                    }

                    if (game.World != null && coopBots == null && coopBotCount > 0)
                    {
                        // One graph for the level, shared by every bot: it
                        // depends only on the map.
                        var nav = new NavGraph(game.World.Map);

                        // One brain too. What one bot learns about the map, the
                        // others know, which is what makes a second attempt on
                        // the same level faster than the first.
                        var brain = GetBotBrain();

                        coopBots = new DoomBot[Player.MaxPlayerCount];

                        // In a network game the bots take the **last** slots, so
                        // that guests fill the ones before them in join order.
                        // A guest always becomes the next free player after the
                        // host, and putting a bot there would make the first guest
                        // take a slot a bot already drives.
                        var slots = netDriver != null ? netDriver.ExpectedPlayers : Player.MaxPlayerCount;

                        for (var i = 1; i <= coopBotCount; i++)
                        {
                            var index = netDriver != null ? slots - i : i;

                            if (index < 1 || index >= Player.MaxPlayerCount)
                            {
                                continue;
                            }

                            var player = options.Players[index];
                            if (player.InGame)
                            {
                                // Different seeds, so the bots do not all pick
                                // the same style and the run tests more than one
                                // idea at a time.
                                coopBots[index] = new DoomBot(
                                    game.World, player, nav, brain, ChooseBotStyle(index),
                                    BotTemperament.FromSeed(BotTemperament.SeedFor(CurrentMapKey(), index)),
                                    LoadDanger(game.World.Map.Sectors.Length));
                            }
                        }

                        if (netDriver != null && netDriver.IsHost)
                        {
                            netDriver.AttachBots(coopBots);
                        }

                        Console.WriteLine("Co-op bots: " + coopBotCount + " joined. Brain: " + brain.Describe());
                    }
                }

                if (game.World != null && botTrace == null && args.botdump.Present && currentState == DoomState.Game)
                {
                    botTrace = new BotTrace(args.botdump.Value, game.World);

                    if (!botTrace.Active)
                    {
                        // No bots in this game: nothing to trace.
                        botTrace = null;
                    }
                }

                if (quit)
                {
                    return UpdateResult.Completed;
                }

                if (needWipe)
                {
                    needWipe = false;
                    StartWipe();
                }
            }

            if (!wiping)
            {
                switch (currentState)
                {
                    case DoomState.Opening:
                        if (opening.Update() == UpdateResult.NeedWipe)
                        {
                            StartWipe();
                        }
                        break;

                    case DoomState.DemoPlayback:
                        var result = demoPlayback.Update();
                        if (result == UpdateResult.NeedWipe)
                        {
                            StartWipe();
                        }
                        else if (result == UpdateResult.Completed)
                        {
                            Quit("FPS: " + demoPlayback.Fps.ToString("0.0"));
                        }
                        break;

                    case DoomState.Game:
                        if (netDriver != null)
                        {
                            if (netDriver.Faulted)
                            {
                                Quit("Network: " + netDriver.FaultReason);
                                break;
                            }

                            if (netDriver.Finished)
                            {
                                Quit("The other player left the game.");
                                break;
                            }

                            // A dropped link is not the end of the game any
                            // more: both sides stopped at the same tic, so the
                            // only thing missing is a few commands, and the
                            // session can re-send those once the peer is back.
                            // Freezing with an explanation is far better than
                            // losing the level to a two-second router hiccup.
                            if (netDriver.Reconnecting)
                            {
                                ShowReconnectStatus();
                                break;
                            }

                            if (reconnectMessageShown)
                            {
                                ClearReconnectStatus();
                            }

                            // The world is built by the first game.Update, and the
                            // bots cannot produce a command before the world
                            // exists - so waiting for a bot tic here would wait
                            // forever. Load the level first, then start the
                            // lockstep. Commands are irrelevant on this call:
                            // game.Update only performs the level load.
                            if (game.World == null)
                            {
                                var firstResult = game.Update(cmds);

                                if (firstResult == UpdateResult.NeedWipe)
                                {
                                    StartWipe();
                                }

                                break;
                            }

                            if (netDriver.IsHost && coopBots != null)
                            {
                                // Attached every tic rather than once at
                                // construction: the bot array is rebuilt on every
                                // level, and a stale attach would leave the driver
                                // holding bots that belong to a world that no
                                // longer exists - or holding nothing at all while
                                // the session waits for a bot slot that no one
                                // fills.
                                netDriver.AttachBots(coopBots);
                            }

                            if (!netDriver.TryAdvance(cmds, userInput, ref sendPause))
                            {
                                // Waiting for a remote command: the world stays
                                // where it is and the last frame keeps showing.
                                break;
                            }

                            var netResult = game.Update(cmds);
                            netDriver.Commit();

                            if (netResult == UpdateResult.NeedWipe)
                            {
                                StartWipe();
                            }

                            break;
                        }

                        if (bot != null)
                        {
                            bot.BuildTicCmd(cmds[options.ConsolePlayer]);
                        }
                        else
                        {
                            userInput.BuildTicCmd(cmds[options.ConsolePlayer]);
                        }

                        if (coopBots != null)
                        {
                            // Every extra player is driven by its own bot, from
                            // the same world state, so the co-op session behaves
                            // exactly as if those players were present.
                            for (var i = 0; i < coopBots.Length; i++)
                            {
                                if (coopBots[i] != null)
                                {
                                    coopBots[i].BuildTicCmd(cmds[i]);
                                }
                            }
                        }
                        if (sendPause)
                        {
                            sendPause = false;
                            cmds[options.ConsolePlayer].Buttons |= (byte)(TicCmdButtons.Special | TicCmdButtons.Pause);
                        }

                        var gameResult = game.Update(cmds);

                        if (botTrace != null)
                        {
                            // Sampled after the update, so the position is where
                            // the bot actually ended up and the command is the
                            // one that moved it there.
                            botTrace.Sample(cmds, bot, coopBots);
                        }

                        if (recorder != null)
                        {
                            // Record what the game actually consumed, after the
                            // pause flag was folded into the commands.
                            recorder.WriteCmd(cmds);
                        }

                        if (gameResult == UpdateResult.NeedWipe)
                        {
                            // The level is over. Let the bots learn from it
                            // before the world is thrown away.
                            RecordBotRuns();

                            FlushBotTrace();

                            // A demo covers one level; the next one starts a new
                            // recording rather than concatenating levels.
                            StopRecording();
                            StartWipe();
                        }
                        break;

                    default:
                        throw new Exception("Invalid application state!");
                }
            }

            if (wiping)
            {
                if (wipeEffect.Update() == UpdateResult.Completed)
                {
                    wiping = false;
                }
            }

            sound.Update();

            CheckMouseState();

            return UpdateResult.None;
        }

        private void CheckMouseState()
        {
            bool mouseShouldBeGrabbed;
            if (!video.HasFocus())
            {
                mouseShouldBeGrabbed = false;
            }
            else if (config.video_fullscreen)
            {
                mouseShouldBeGrabbed = true;
            }
            else
            {
                mouseShouldBeGrabbed = currentState == DoomState.Game && !menu.Active;
            }

            if (mouseGrabbed)
            {
                if (!mouseShouldBeGrabbed)
                {
                    userInput.ReleaseMouse();
                    mouseGrabbed = false;
                }
            }
            else
            {
                if (mouseShouldBeGrabbed)
                {
                    userInput.GrabMouse();
                    mouseGrabbed = true;
                }
            }
        }

        private void StartWipe()
        {
            wipeEffect.Start();
            video.InitializeWipe();
            wiping = true;
        }

        public void PauseGame()
        {
            if (currentState == DoomState.Game &&
                game.State == GameState.Level &&
                !game.Paused && !sendPause)
            {
                sendPause = true;
            }
        }

        public void ResumeGame()
        {
            if (currentState == DoomState.Game &&
                game.State == GameState.Level &&
                game.Paused && !sendPause)
            {
                sendPause = true;
            }
        }

        public bool SaveGame(int slotNumber, string description)
        {
            if (currentState == DoomState.Game && game.State == GameState.Level)
            {
                game.SaveGame(slotNumber, description);
                return true;
            }

            // Attract demo: the moment being shown can be saved as an ordinary
            // single-player save. The demo keeps playing, and loading that slot
            // hands control to the player, because a save is a plain level
            // snapshot with no demo state in it.
            if (currentState == DoomState.Opening &&
                opening.State == OpeningSequenceState.Demo &&
                opening.DemoGame != null &&
                opening.DemoGame.State == GameState.Level)
            {
                opening.DemoGame.SaveGame(slotNumber, description);
                return true;
            }

            return false;
        }

        public void LoadGame(int slotNumber)
        {
            game.LoadGame(slotNumber);
            nextState = DoomState.Game;
        }

        public void Quit()
        {
            StopRecording();
            quit = true;
        }

        /// <summary>
        /// Puts the reconnect countdown on the status bar.
        ///
        /// Written every frame rather than once, because a player message expires
        /// after a couple of seconds and the countdown has to survive a wait that
        /// can last half a minute. The message is local display only - it is not
        /// in a tic, a demo or the world hash - so this cannot desynchronise the
        /// session it is reporting on.
        /// </summary>
        private void ShowReconnectStatus()
        {
            var world = game.World;
            if (world == null)
            {
                return;
            }

            var status = netDriver.ReconnectStatus;
            if (status == null)
            {
                return;
            }

            var player = world.ConsolePlayer;

            // The message timer is refreshed here on purpose. A player message
            // normally expires after four seconds, and this one has to survive a
            // wait that can last thirty; refreshing it per frame is the simplest
            // way to keep it on screen without a second overlay mechanism.
            //
            // This is display only: messages are not in the world hash, not in a
            // demo and not in a save that travels over the wire, so touching them
            // from the frame loop cannot desynchronise the session it describes.
            player.Message = status;
            player.MessageTime = 4 * GameConst.TicRate;
            player.MessageAlwaysShown = true;

            reconnectMessageShown = true;
        }

        private void ClearReconnectStatus()
        {
            reconnectMessageShown = false;

            var world = game.World;
            if (world == null)
            {
                return;
            }

            world.ConsolePlayer.Message = null;
            world.ConsolePlayer.MessageTime = 0;
            world.ConsolePlayer.MessageAlwaysShown = false;
            Console.WriteLine("Net: the session is back.");
        }

        /// <summary>
        /// Finishes and writes the current demo recording, if any.
        /// </summary>
        private void StopRecording()
        {
            if (recorder == null)
            {
                return;
            }

            try
            {
                recorder.Save(recordPath);
                Console.WriteLine("Demo recorded: " + recordPath + " (" + recorder.TicCount + " tics)");
            }
            catch (Exception e)
            {
                Console.WriteLine("Could not write the demo: " + e.Message);
            }

            recorder = null;
        }

        private static string ResolveRecordPath(string name)
        {
            var path = name ?? "";

            if (path.Length == 0)
            {
                path = "demo";
            }

            if (Path.GetExtension(path).Length == 0)
            {
                path = path + ".lmp";
            }

            if (!Path.IsPathRooted(path))
            {
                path = Path.Combine(ConfigUtilities.GetDataDirectory(), path);
            }

            return path;
        }

        public void Quit(string message)
        {
            quit = true;
            quitMessage = message;
        }

        public void PostEvent(DoomEvent e)
        {
            if (events.Count < 64)
            {
                events.Add(e);
            }
        }

        public DoomState State => currentState;
        public OpeningSequence Opening => opening;
        public DemoPlayback DemoPlayback => demoPlayback;
        public GameOptions Options => options;
        public DoomGame Game => game;
        public DoomMenu Menu => menu;
        public WipeEffect WipeEffect => wipeEffect;
        public bool Wiping => wiping;
        public string QuitMessage => quitMessage;
    }
}
