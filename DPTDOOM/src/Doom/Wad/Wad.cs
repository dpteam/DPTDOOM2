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
using System.Linq;
using System.Runtime.ExceptionServices;

namespace DPTDOOM
{
    public sealed class Wad : IDisposable
    {
        private List<string> names;
        private List<Stream> streams;
        private List<LumpInfo> lumpInfos;
        private GameVersion gameVersion;
        private GameMode gameMode;
        private MissionPack missionPack;

        public Wad(params string[] fileNames)
        {
            try
            {
                ConsoleUi.Begin("wad");

                names = new List<string>();
                streams = new List<Stream>();
                lumpInfos = new List<LumpInfo>();

                foreach (var fileName in fileNames)
                {
                    AddFile(fileName);
                }

                gameMode = GetGameMode(names);
                missionPack = GetMissionPack(names);
                gameVersion = GetGameVersion(names);

                if (gameMode == GameMode.Indetermined)
                {
                    gameMode = DetectGameModeFromLumps();
                }

                if (gameMode == GameMode.Indetermined)
                {
                    throw new Exception(
                        "Cannot tell which game this is: no known IWAD name and no maps found.");
                }

                ConsoleUi.Ok(string.Join(", ", fileNames.Select(x => Path.GetFileName(x))));
            }
            catch (Exception e)
            {
                ConsoleUi.Failed("see below");
                Dispose();
                ExceptionDispatchInfo.Throw(e);
            }
        }

        private void AddFile(string fileName)
        {
            var extension = Path.GetExtension(fileName).ToLowerInvariant();

            if (extension == ".pk3" || extension == ".zip")
            {
                AddZip(fileName);
                return;
            }

            names.Add(Path.GetFileNameWithoutExtension(fileName).ToLower());

            var stream = new FileStream(fileName, FileMode.Open, FileAccess.Read);
            streams.Add(stream);

            AddWad(stream);
        }

        /// <summary>
        /// Reads a PK3 or ZIP container.
        ///
        /// A PK3 is a ZIP. Two kinds of content inside it are useful here:
        ///
        ///   - a WAD, which is loaded exactly as if it had been passed on the
        ///     command line. Plenty of PK3s ship their maps this way, and it is
        ///     the case the roadmap asked for.
        ///
        ///   - loose lumps with a Doom lump name, which older archives use
        ///     instead of a WAD. Each is registered under its upper-case base name
        ///     so the existing lookups find it.
        ///
        /// What is **not** supported yet: PNG graphics under `flats/`,
        /// `textures/` and `sprites/`. Those need a PNG decoder, which is a
        /// separate piece of work; a PK3 that only carries its graphics that way
        /// will load its maps and use the IWAD's artwork. That limitation is
        /// recorded in docs/ROADMAP.md rather than papered over.
        /// </summary>
        private void AddZip(string fileName)
        {
            names.Add(Path.GetFileNameWithoutExtension(fileName).ToLower());

            var archive = new ZipArchiveReader(fileName);

            // GZDoom reads every `textures/*.txt` as a TEXTURES lump, and mods are written that
            // way: one file per texture set, split for tidiness. They are joined into one lump
            // here, because the texture builder wants one text and the split is not part of the
            // format's meaning.
            MemoryStream textureText = null;

            foreach (var entry in archive.Entries)
            {
                var entryExtension = Path.GetExtension(entry.Name).ToLowerInvariant();

                if (entryExtension == ".wad")
                {
                    // The entry's own name counts for game detection. A PK3 that
                    // carries tnt.wad should still be recognised as TNT, even
                    // though the container is called something else.
                    names.Add(Path.GetFileNameWithoutExtension(entry.Name).ToLowerInvariant());

                    var data = archive.ReadEntry(entry);

                    var memory = new MemoryStream(data, false);
                    streams.Add(memory);

                    AddWad(memory);
                    continue;
                }

                if (entryExtension == ".png")
                {
                    // Kept as a graphic, not as a lump: the pixels need decoding
                    // and converting to the palette first, which the flat lookup
                    // does. Registering the raw PNG as a lump would only put
                    // something the renderer cannot use into the lump list.
                    graphics.Add(new Graphic { Path = entry.Name, Data = archive.ReadEntry(entry) });
                    continue;
                }

                if (IsTextureDefinition(entry.Name))
                {
                    if (textureText == null)
                    {
                        textureText = new MemoryStream();
                    }

                    var text = archive.ReadEntry(entry);
                    textureText.Write(text, 0, text.Length);

                    // A newline between files, so a definition that ended without one does not
                    // run into the next file's first keyword.
                    textureText.WriteByte((byte)'\n');
                    continue;
                }

                var lumpName = Path.GetFileNameWithoutExtension(entry.Name).ToUpperInvariant();

                if (lumpName.Length == 0 || lumpName.Length > 8)
                {
                    continue;
                }

                var lumpData = archive.ReadEntry(entry);

                if (lumpData.Length == 0)
                {
                    continue;
                }

                var lumpStream = new MemoryStream(lumpData, false);
                streams.Add(lumpStream);

                lumpInfos.Add(new LumpInfo(lumpName, lumpStream, 0, lumpData.Length));
            }

            if (textureText != null)
            {
                var data = textureText.ToArray();

                if (data.Length > 0)
                {
                    streams.Add(textureText);

                    lumpInfos.Add(new LumpInfo("TEXTURES", textureText, 0, data.Length));
                }
            }
        }

        /// <summary>
        /// True for a PK3 entry that carries ZDoom TEXTURES definitions: `textures/*.txt`, or a
        /// file whose own name is TEXTURES.
        /// </summary>
        private static bool IsTextureDefinition(string path)
        {
            if (path == null)
            {
                return false;
            }

            var lower = path.Replace('\\', '/').ToLowerInvariant();

            if (!lower.EndsWith(".txt"))
            {
                return false;
            }

            if (lower.StartsWith("textures/"))
            {
                return true;
            }

            var slash = lower.LastIndexOf('/');
            var name = slash >= 0 ? lower.Substring(slash + 1) : lower;

            return name == "textures.txt";
        }

        /// <summary>
        /// Reads one WAD out of an open stream and appends its lumps.
        /// </summary>
        private void AddWad(Stream stream)
        {
            string identification;
            int lumpCount;
            int lumpInfoTableOffset;
            {
                var data = new byte[12];
                if (stream.Read(data, 0, data.Length) != data.Length)
                {
                    throw new Exception("Failed to read the WAD file.");
                }

                identification = DoomInterop.ToString(data, 0, 4);
                lumpCount = BitConverter.ToInt32(data, 4);
                lumpInfoTableOffset = BitConverter.ToInt32(data, 8);
                if (identification != "IWAD" && identification != "PWAD")
                {
                    throw new Exception("The file is not a WAD file.");
                }
            }

            {
                var data = new byte[LumpInfo.DataSize * lumpCount];
                stream.Seek(lumpInfoTableOffset, SeekOrigin.Begin);
                if (stream.Read(data, 0, data.Length) != data.Length)
                {
                    throw new Exception("Failed to read the WAD file.");
                }

                for (var i = 0; i < lumpCount; i++)
                {
                    var offset = LumpInfo.DataSize * i;
                    var lumpInfo = new LumpInfo(
                        DoomInterop.ToString(data, offset + 8, 8),
                        stream,
                        BitConverter.ToInt32(data, offset),
                        BitConverter.ToInt32(data, offset + 4));
                    lumpInfos.Add(lumpInfo);
                }
            }
        }

        /// <summary>
        /// Works out the game from the maps the WAD contains.
        ///
        /// File names are the usual way — DOOM2.WAD, TNT.WAD — but they are only
        /// a convention, and a container breaks it: a PK3 called "my-stuff.pk3"
        /// that happens to carry doom2.wad has no useful name at all. The maps
        /// cannot lie, though: a commercial game has MAP01, and Doom 1 has E1M1.
        /// </summary>
        private GameMode DetectGameModeFromLumps()
        {
            if (GetLumpNumber("MAP01") >= 0)
            {
                return GameMode.Commercial;
            }

            if (GetLumpNumber("E1M1") < 0)
            {
                return GameMode.Indetermined;
            }

            // Shareware ships one episode; registered ships three; retail ships
            // four. That is the only difference the engine cares about.
            if (GetLumpNumber("E4M1") >= 0)
            {
                return GameMode.Retail;
            }

            if (GetLumpNumber("E2M1") >= 0)
            {
                return GameMode.Registered;
            }

            return GameMode.Shareware;
        }

        /// <summary>
        /// Raw bytes of a PK3 entry that is a graphic, keyed by its path inside
        /// the archive. Filled by the ZIP loader and read by the graphic lookups.
        /// </summary>
        public sealed class Graphic
        {
            public string Path;
            public byte[] Data;
        }

        private readonly List<Graphic> graphics = new List<Graphic>();

        public IList<Graphic> Graphics { get { return graphics; } }

        public int GetLumpNumber(string name)
        {
            for (var i = lumpInfos.Count - 1; i >= 0; i--)
            {
                if (lumpInfos[i].Name == name)
                {
                    return i;
                }
            }

            return -1;
        }

        public int GetLumpSize(int number)
        {
            return lumpInfos[number].Size;
        }

        public byte[] ReadLump(int number)
        {
            var lumpInfo = lumpInfos[number];

            var data = new byte[lumpInfo.Size];

            lumpInfo.Stream.Seek(lumpInfo.Position, SeekOrigin.Begin);
            var read = lumpInfo.Stream.Read(data, 0, lumpInfo.Size);
            if (read != lumpInfo.Size)
            {
                throw new Exception("Failed to read the lump " + number + ".");
            }

            return data;
        }

        public byte[] ReadLump(string name)
        {
            var lumpNumber = GetLumpNumber(name);

            if (lumpNumber == -1)
            {
                throw new Exception("The lump '" + name + "' was not found.");
            }

            return ReadLump(lumpNumber);
        }

        public void Dispose()
        {
            Console.WriteLine("Close WAD files.");

            foreach (var stream in streams)
            {
                stream.Dispose();
            }

            streams.Clear();
        }

        private static GameVersion GetGameVersion(IList<string> names)
        {
            foreach (var name in names)
            {
                switch (name.ToLower())
                {
                    case "doom2":
                    case "freedoom2":
                        return GameVersion.Version109;
                    case "doom":
                    case "doom1":
                    case "freedoom1":
                        return GameVersion.Ultimate;
                    case "plutonia":
                    case "tnt":
                        return GameVersion.Final;
                }
            }

            return GameVersion.Version109;
        }

        private static GameMode GetGameMode(IList<string> names)
        {
            foreach (var name in names)
            {
                switch (name.ToLower())
                {
                    case "doom2":
                    case "plutonia":
                    case "tnt":
                    case "freedoom2":
                        return GameMode.Commercial;
                    case "doom":
                    case "freedoom1":
                        return GameMode.Retail;
                    case "doom1":
                        return GameMode.Shareware;
                }
            }

            return GameMode.Indetermined;
        }

        private static MissionPack GetMissionPack(IList<string> names)
        {
            foreach (var name in names)
            {
                switch (name.ToLower())
                {
                    case "plutonia":
                        return MissionPack.Plutonia;
                    case "tnt":
                        return MissionPack.Tnt;
                }
            }

            return MissionPack.Doom2;
        }

        public IList<string> Names => names;
        public IList<LumpInfo> LumpInfos => lumpInfos;
        public GameVersion GameVersion => gameVersion;
        public GameMode GameMode => gameMode;
        public MissionPack MissionPack => missionPack;
    }
}
