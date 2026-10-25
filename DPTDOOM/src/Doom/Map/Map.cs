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
using System.Runtime.ExceptionServices;

namespace DPTDOOM
{
    public sealed class Map
    {
        private ITextureLookup textures;
        private IFlatLookup flats;
        private TextureAnimation animation;

        private World world;

        private Vertex[] vertices;
        private Sector[] sectors;
        private SideDef[] sides;
        private LineDef[] lines;
        private Seg[] segs;
        private Subsector[] subsectors;
        private Node[] nodes;
        private MapThing[] things;
        private BlockMap blockMap;
        private Reject reject;

        private Texture skyTexture;

        /// <summary>
        /// The music lump this map asked for, or null to use the engine's own choice.
        ///
        /// A lump name rather than a <see cref="Bgm"/>: a mod ships "D_MYSONG", which is not in
        /// the engine's enumeration and never could be.
        /// </summary>
        private string mapInfoMusic;

        private string title;

        public Map(GameContent resorces, World world)
            : this(resorces.Wad, resorces.Textures, resorces.Flats, resorces.Animation, world)
        {
        }

        public Map(Wad wad, ITextureLookup textures, IFlatLookup flats, TextureAnimation animation, World world)
        {
            try
            {
                this.textures = textures;
                this.flats = flats;
                this.animation = animation;
                this.world = world;

                var options = world.Options;

                string name;
                if (wad.GameMode == GameMode.Commercial)
                {
                    name = "MAP" + options.Map.ToString("00");
                }
                else
                {
                    name = "E" + options.Episode + "M" + options.Map;
                }

                // The map name is part of the label, the way a kernel log prefixes a device: the
                // line has to say which map loaded, and a generic "map:" would need the
                // name in the message to be useful at all.
                ConsoleUi.Begin("map " + name);

                var map = wad.GetLumpNumber(name);

                if (map == -1)
                {
                    throw new Exception("Map '" + name + "' was not found!");
                }

                vertices = Vertex.FromWad(wad, map + 4);
                sectors = Sector.FromWad(wad, map + 8, flats);
                sides = SideDef.FromWad(wad, map + 3, textures, sectors);
                lines = LineDef.FromWad(wad, map + 2, vertices, sides);
                segs = Seg.FromWad(wad, map + 5, vertices, lines);
                subsectors = Subsector.FromWad(wad, map + 6, segs);
                nodes = Node.FromWad(wad, map + 7, subsectors);
                things = MapThing.FromWad(wad, map + 1);
                blockMap = BlockMap.FromWad(wad, map + 10, lines);
                reject = Reject.FromWad(wad, map + 9, sectors);

                // Every one of those tolerates a truncated lump through
                // LoadReport.WholeRecords and reports the loss there. A map whose last
                // line is missing still plays; refusing to load it is what used to
                // happen and what the report exists to replace.

                GroupLines();

                skyTexture = GetSkyTextureByMapName(name);

                if (options.GameMode == GameMode.Commercial)
                {
                    switch (options.MissionPack)
                    {
                        case MissionPack.Plutonia:
                            title = DoomInfo.MapTitles.Plutonia[options.Map - 1];
                            break;
                        case MissionPack.Tnt:
                            title = DoomInfo.MapTitles.Tnt[options.Map - 1];
                            break;
                        default:
                            title = DoomInfo.MapTitles.Doom2[options.Map - 1];
                            break;
                    }
                }
                else
                {
                    title = DoomInfo.MapTitles.Doom[options.Episode - 1][options.Map - 1];
                }

                // MAPINFO last, so a mod's own name and sky replace the engine's guess rather
                // than being replaced by it. The entry is per map lump name, which is why this
                // sits after the defaults are worked out: an entry that sets only the sky must
                // not blank the title.
                var info = MapInfo.For(name);

                if (info != null)
                {
                    if (!string.IsNullOrEmpty(info.Title))
                    {
                        title = new DoomString(info.Title);
                    }

                    if (!string.IsNullOrEmpty(info.SkyTexture))
                    {
                        // GetNumber returns -1 for a name that is not a texture, and that is the
                        // only safe way to ask: the indexer throws a KeyNotFoundException, which
                        // would take the whole level down over a typo in a mod's sky name.
                        var skyNumber = textures.GetNumber(info.SkyTexture);

                        if (skyNumber >= 0)
                        {
                            skyTexture = textures[skyNumber];
                        }
                        else
                        {
                            LoadReport.Add(
                                "unknown sky",
                                info.SkyTexture + " for " + name,
                                "the level keeps the engine's own sky");
                        }
                    }

                    mapInfoMusic = info.MusicLump;
                }

                // The level's name, not the word "loaded": the line already says it succeeded, and
                // which level it is happens to be the one fact the log was missing. It is also what
                // makes MAPINFO verifiable from outside - a name that only reaches the automap
                // cannot be checked by a headless run.
                ConsoleUi.Ok(title.ToString());
            }
            catch (Exception e)
            {
                ConsoleUi.Failed(e.Message);
                ExceptionDispatchInfo.Throw(e);
            }
        }

        private void GroupLines()
        {
            var sectorLines = new List<LineDef>();
            var boundingBox = new Fixed[4];

            foreach (var line in lines)
            {
                if (line.Special != 0)
                {
                    var so = new Mobj(world);
                    so.X = (line.Vertex1.X + line.Vertex2.X) / 2;
                    so.Y = (line.Vertex1.Y + line.Vertex2.Y) / 2;
                    line.SoundOrigin = so;
                }
            }

            foreach (var sector in sectors)
            {
                sectorLines.Clear();
                Box.Clear(boundingBox);

                foreach (var line in lines)
                {
                    if (line.FrontSector == sector || line.BackSector == sector)
                    {
                        sectorLines.Add(line);
                        Box.AddPoint(boundingBox, line.Vertex1.X, line.Vertex1.Y);
                        Box.AddPoint(boundingBox, line.Vertex2.X, line.Vertex2.Y);
                    }
                }

                sector.Lines = sectorLines.ToArray();

                // Set the degenmobj_t to the middle of the bounding box.
                sector.SoundOrigin = new Mobj(world);
                sector.SoundOrigin.X = (boundingBox[Box.Right] + boundingBox[Box.Left]) / 2;
                sector.SoundOrigin.Y = (boundingBox[Box.Top] + boundingBox[Box.Bottom]) / 2;

                sector.BlockBox = new int[4];
                int block;

                // Adjust bounding box to map blocks.
                block = (boundingBox[Box.Top] - blockMap.OriginY + GameConst.MaxThingRadius).Data >> BlockMap.FracToBlockShift;
                block = block >= blockMap.Height ? blockMap.Height - 1 : block;
                sector.BlockBox[Box.Top] = block;

                block = (boundingBox[Box.Bottom] - blockMap.OriginY - GameConst.MaxThingRadius).Data >> BlockMap.FracToBlockShift;
                block = block < 0 ? 0 : block;
                sector.BlockBox[Box.Bottom] = block;

                block = (boundingBox[Box.Right] - blockMap.OriginX + GameConst.MaxThingRadius).Data >> BlockMap.FracToBlockShift;
                block = block >= blockMap.Width ? blockMap.Width - 1 : block;
                sector.BlockBox[Box.Right] = block;

                block = (boundingBox[Box.Left] - blockMap.OriginX - GameConst.MaxThingRadius).Data >> BlockMap.FracToBlockShift;
                block = block < 0 ? 0 : block;
                sector.BlockBox[Box.Left] = block;
            }
        }

        private Texture GetSkyTextureByMapName(string name)
        {
            if (name.Length == 4)
            {
                switch (name[1])
                {
                    case '1':
                        return textures["SKY1"];
                    case '2':
                        return textures["SKY2"];
                    case '3':
                        return textures["SKY3"];
                    default:
                        return textures["SKY4"];
                }
            }
            else
            {
                var number = int.Parse(name.Substring(3));
                if (number <= 11)
                {
                    return textures["SKY1"];
                }
                else if (number <= 21)
                {
                    return textures["SKY2"];
                }
                else
                {
                    return textures["SKY3"];
                }
            }
        }

        public ITextureLookup Textures => textures;
        public IFlatLookup Flats => flats;
        public TextureAnimation Animation => animation;

        public Vertex[] Vertices => vertices;
        public Sector[] Sectors => sectors;
        public SideDef[] Sides => sides;
        public LineDef[] Lines => lines;
        public Seg[] Segs => segs;
        public Subsector[] Subsectors => subsectors;
        public Node[] Nodes => nodes;
        public MapThing[] Things => things;
        public BlockMap BlockMap => blockMap;
        public Reject Reject => reject;
        public Texture SkyTexture => skyTexture;

        /// <summary>
        /// The music lump MAPINFO asked for, or null.
        ///
        /// The caller decides what to do with it, because playing a named lump is a property of
        /// the music backend and not of the map.
        /// </summary>
        public string MapInfoMusic => mapInfoMusic;
        public int SkyFlatNumber => flats.SkyFlatNumber;
        public string Title => title;



        private static readonly Bgm[] e4BgmList = new Bgm[]
        {
            Bgm.E3M4, // American   e4m1
            Bgm.E3M2, // Romero     e4m2
            Bgm.E3M3, // Shawn      e4m3
            Bgm.E1M5, // American   e4m4
            Bgm.E2M7, // Tim        e4m5
            Bgm.E2M4, // Romero     e4m6
            Bgm.E2M6, // J.Anderson e4m7 CHIRON.WAD
            Bgm.E2M5, // Shawn      e4m8
            Bgm.E1M9  // Tim        e4m9
        };

        public static Bgm GetMapBgm(GameOptions options)
        {
            Bgm bgm;
            if (options.GameMode == GameMode.Commercial)
            {
                bgm = Bgm.RUNNIN + options.Map - 1;
            }
            else
            {
                if (options.Episode < 4)
                {
                    bgm = Bgm.E1M1 + (options.Episode - 1) * 9 + options.Map - 1;
                }
                else
                {
                    bgm = e4BgmList[options.Map - 1];
                }
            }

            return bgm;
        }
    }
}
