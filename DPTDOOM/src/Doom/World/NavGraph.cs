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

namespace DPTDOOM
{
    /// <summary>
    /// Navigation mesh for a level, built from the map at load time.
    ///
    /// The nodes are **points inside sectors** and the edges are **walkable
    /// straight lines** between them. This is the same idea as the nav mesh a
    /// Half-Life or Counter-Strike compiler bakes into a .nav file, with one
    /// important difference: it is generated from the map data already in
    /// memory, so it works on any PWAD with no preprocessing step.
    ///
    /// Two simpler designs were tried first and both failed in ways that are
    /// worth recording:
    ///
    ///  - **BSP subsectors as nodes.** Appealing, because a subsector is convex,
    ///    so its centre is always walkable. But a subsector's segs are not a
    ///    complete record of which linedefs touch it once the BSP has split long
    ///    lines, and the graph connected 18 of 194 nodes.
    ///
    ///  - **Sectors as nodes.** Correct adjacency, since a two-sided line is by
    ///    definition a way through. But a sector is not convex: an L-shaped
    ///    corridor is one sector, and the straight line to the next portal goes
    ///    through the corner. The bot then walks into the wall and grinds.
    ///
    /// The mesh here fixes both: several points per sector, connected only when
    /// the straight line between them stays inside that sector.
    ///
    /// Pathfinding is Dijkstra. On a thousand nodes it is instant, and it avoids
    /// any question about whether a straight-line heuristic is admissible.
    /// </summary>
    public sealed class NavGraph
    {
        /// <summary>Points closer than this are treated as one.</summary>
        private const float MergeDistance = 24F;

        /// <summary>How far inside a sector a doorway point sits.</summary>
        private const float DoorInset = 20F;

        /// <summary>Sampling step used to test whether a line stays in a sector.</summary>
        private const float VisibilityStep = 8F;

        private readonly Map map;

        private float[] nodeX = new float[0];
        private float[] nodeY = new float[0];
        private int[] nodeSector = new int[0];
        private bool[] nodeDoor = new bool[0];

        private int[] edgeStart = new int[0];
        private int[] edgeTarget = new int[0];
        private float[] edgeCost = new float[0];

        private int nodeCount;

        // Scratch space reused by the search, so pathfinding allocates nothing.
        private float[] cost = new float[0];
        private int[] cameFrom = new int[0];
        private int[] visitStamp = new int[0];
        private bool[] closed = new bool[0];
        private int stamp;

        private readonly BinaryHeap open = new BinaryHeap(4096);

        public NavGraph(Map map)
        {
            this.map = map;

            Build();
        }

        ////////////////////////////////////////////////////////////
        // Building
        ////////////////////////////////////////////////////////////

        private void Build()
        {
            var xs = new List<float>();
            var ys = new List<float>();
            var sectors = new List<int>();
            var doors = new List<bool>();

            // One list of points per sector, so portal points can be linked to
            // the interior points of their own sector.
            var pointsOfSector = new List<int>[map.Sectors.Length];
            for (var i = 0; i < pointsOfSector.Length; i++)
            {
                pointsOfSector[i] = new List<int>();
            }

            // 1. Interior points: a grid over each sector's bounding box, keeping
            //    only points that really are inside it.
            for (var s = 0; s < map.Sectors.Length; s++)
            {
                AddSectorPoints(s, xs, ys, sectors, doors, pointsOfSector);
            }

            // 2. Doorway points, one on each side of every passable two-sided
            //    line. These have to be created *before* the arrays are sized:
            //    adding points later was an off-by-one that crashed the build.
            var frontDoor = new int[map.Lines.Length];
            var backDoor = new int[map.Lines.Length];

            for (var i = 0; i < map.Lines.Length; i++)
            {
                frontDoor[i] = -1;
                backDoor[i] = -1;
            }

            for (var i = 0; i < map.Lines.Length; i++)
            {
                var line = map.Lines[i];

                if (line.BackSector == null || line.FrontSector == null)
                {
                    continue;
                }

                var front = line.FrontSector.Number;
                var back = line.BackSector.Number;

                if (front == back)
                {
                    continue;
                }

                if (!CanWalk(line, line.FrontSector, line.BackSector) &&
                    !CanWalk(line, line.BackSector, line.FrontSector))
                {
                    continue;
                }

                frontDoor[i] = DoorPoint(front, line, xs, ys, sectors, doors, pointsOfSector);
                backDoor[i] = DoorPoint(back, line, xs, ys, sectors, doors, pointsOfSector);
            }

            // 3. Now that every point exists, the graph can be sized.
            nodeX = xs.ToArray();
            nodeY = ys.ToArray();
            nodeSector = sectors.ToArray();
            nodeDoor = doors.ToArray();
            nodeCount = nodeX.Length;

            var edges = new List<int>[nodeCount];
            var costs = new List<float>[nodeCount];
            for (var i = 0; i < nodeCount; i++)
            {
                edges[i] = new List<int>();
                costs[i] = new List<float>();
            }

            // 4. Intra-sector edges: two points in the same sector are connected
            //    when the straight line between them never leaves it.
            for (var s = 0; s < pointsOfSector.Length; s++)
            {
                var list = pointsOfSector[s];

                for (var a = 0; a < list.Count; a++)
                {
                    for (var b = a + 1; b < list.Count; b++)
                    {
                        if (!Visible(list[a], list[b]))
                        {
                            continue;
                        }

                        Connect(edges, costs, xs, ys, list[a], list[b]);
                    }
                }
            }

            // 5. Portal edges: connect the doorway point on one side to the one
            //    on the other.
            for (var i = 0; i < map.Lines.Length; i++)
            {
                if (frontDoor[i] >= 0 && backDoor[i] >= 0)
                {
                    Connect(edges, costs, xs, ys, frontDoor[i], backDoor[i]);
                }
            }

            // 6. Flatten to compressed sparse rows.
            edgeStart = new int[nodeCount + 1];
            for (var i = 0; i < nodeCount; i++)
            {
                edgeStart[i + 1] = edgeStart[i] + edges[i].Count;
            }

            edgeTarget = new int[edgeStart[nodeCount]];
            edgeCost = new float[edgeStart[nodeCount]];

            for (var i = 0; i < nodeCount; i++)
            {
                var offset = edgeStart[i];
                for (var e = 0; e < edges[i].Count; e++)
                {
                    edgeTarget[offset + e] = edges[i][e];
                    edgeCost[offset + e] = costs[i][e];
                }
            }

            cost = new float[nodeCount];
            cameFrom = new int[nodeCount];
            visitStamp = new int[nodeCount];
            closed = new bool[nodeCount];

            EdgeCount = edgeTarget.Length;
        }

        /// <summary>
        /// Grid of points inside one sector. The step adapts to the sector size,
        /// and points that fall outside (a concave sector) are dropped by the
        /// containment test.
        /// </summary>
        private void AddSectorPoints(
            int sectorNumber,
            List<float> xs, List<float> ys, List<int> sectors, List<bool> doors,
            List<int>[] pointsOfSector)
        {
            var sector = map.Sectors[sectorNumber];

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;

            foreach (var line in sector.Lines)
            {
                var x1 = line.Vertex1.X.ToFloat();
                var y1 = line.Vertex1.Y.ToFloat();
                var x2 = line.Vertex2.X.ToFloat();
                var y2 = line.Vertex2.Y.ToFloat();

                if (x1 < minX) minX = x1;
                if (x2 < minX) minX = x2;
                if (y1 < minY) minY = y1;
                if (y2 < minY) minY = y2;
                if (x1 > maxX) maxX = x1;
                if (x2 > maxX) maxX = x2;
                if (y1 > maxY) maxY = y1;
                if (y2 > maxY) maxY = y2;
            }

            if (minX > maxX)
            {
                return;
            }

            var width = maxX - minX;
            var height = maxY - minY;
            var extent = Math.Max(width, height);

            // Roughly 96 units between points: fine enough for a corridor bend,
            // coarse enough that a big hall does not produce thousands of nodes.
            var steps = (int)Math.Ceiling(extent / 96F);
            if (steps < 1) steps = 1;
            if (steps > 5) steps = 5;

            for (var ix = 0; ix <= steps; ix++)
            {
                for (var iy = 0; iy <= steps; iy++)
                {
                    var x = steps == 0 ? minX : minX + width * ix / steps;
                    var y = steps == 0 ? minY : minY + height * iy / steps;

                    if (!InsideSector(x, y, sectorNumber))
                    {
                        continue;
                    }

                    var index = AddPoint(x, y, sectorNumber, false, xs, ys, sectors, doors);
                    if (index >= 0)
                    {
                        pointsOfSector[sectorNumber].Add(index);
                    }
                }
            }
        }

        /// <summary>
        /// A point just inside a sector, in front of a doorway, and an edge from
        /// it to the interior points of the same sector.
        /// </summary>
        private int DoorPoint(
            int sectorNumber, LineDef line,
            List<float> xs, List<float> ys, List<int> sectors, List<bool> doors,
            List<int>[] pointsOfSector)
        {
            var sector = map.Sectors[sectorNumber];

            // Inward normal: from the line's midpoint toward the sector centre.
            var midX = (line.Vertex1.X.ToFloat() + line.Vertex2.X.ToFloat()) * 0.5F;
            var midY = (line.Vertex1.Y.ToFloat() + line.Vertex2.Y.ToFloat()) * 0.5F;

            var centreX = 0.0F;
            var centreY = 0.0F;
            var count = 0;

            foreach (var other in sector.Lines)
            {
                centreX += (other.Vertex1.X.ToFloat() + other.Vertex2.X.ToFloat()) * 0.5F;
                centreY += (other.Vertex1.Y.ToFloat() + other.Vertex2.Y.ToFloat()) * 0.5F;
                count++;
            }

            if (count == 0)
            {
                return -1;
            }

            centreX /= count;
            centreY /= count;

            var dx = centreX - midX;
            var dy = centreY - midY;
            var length = (float)Math.Sqrt(dx * dx + dy * dy);

            if (length < 0.001F)
            {
                return -1;
            }

            var px = midX + dx / length * DoorInset;
            var py = midY + dy / length * DoorInset;

            if (!InsideSector(px, py, sectorNumber))
            {
                // The inset point fell into a wall; try the line midpoint itself,
                // which is on the boundary but still crossable.
                px = midX;
                py = midY;
            }

            var index = AddPoint(px, py, sectorNumber, IsDoorSpecial((int)line.Special), xs, ys, sectors, doors);

            if (index >= 0)
            {
                pointsOfSector[sectorNumber].Add(index);
            }

            return index;
        }

        private static void Connect(
            List<int>[] edges, List<float>[] costs,
            List<float> xs, List<float> ys,
            int a, int b)
        {
            if (a == b || a < 0 || b < 0)
            {
                return;
            }

            var dx = xs[a] - xs[b];
            var dy = ys[a] - ys[b];
            var distance = (float)Math.Sqrt(dx * dx + dy * dy);

            edges[a].Add(b);
            costs[a].Add(distance);
            edges[b].Add(a);
            costs[b].Add(distance);
        }

        private int AddPoint(
            float x, float y, int sectorNumber, bool door,
            List<float> xs, List<float> ys, List<int> sectors, List<bool> doors)
        {
            // Merge with an existing point of the same sector that is close by.
            for (var i = 0; i < xs.Count; i++)
            {
                if (sectors[i] != sectorNumber)
                {
                    continue;
                }

                var dx = xs[i] - x;
                var dy = ys[i] - y;

                if (dx * dx + dy * dy < MergeDistance * MergeDistance)
                {
                    return i;
                }
            }

            xs.Add(x);
            ys.Add(y);
            sectors.Add(sectorNumber);
            doors.Add(door);

            return xs.Count - 1;
        }

        private bool InsideSector(float x, float y, int sectorNumber)
        {
            var subsector = Geometry.PointInSubsector(
                Fixed.FromDouble(x), Fixed.FromDouble(y), map);

            return subsector.Sector.Number == sectorNumber;
        }

        /// <summary>
        /// True when the straight line between two nodes never leaves the sector
        /// they share. Sampled, which is exact enough at 16-unit steps for a
        /// player who is 32 units wide.
        /// </summary>
        private bool Visible(int a, int b)
        {
            var ax = nodeX[a];
            var ay = nodeY[a];
            var bx = nodeX[b];
            var by = nodeY[b];
            var sector = nodeSector[a];

            var dx = bx - ax;
            var dy = by - ay;
            var length = (float)Math.Sqrt(dx * dx + dy * dy);

            if (length < 0.001F)
            {
                return false;
            }

            var steps = (int)(length / VisibilityStep);
            if (steps < 1) steps = 1;

            // Sample a band, not a hairline. A player is 32 units wide, so a
            // straight line that fits through a gap narrower than that is not
            // walkable even though the line itself is clear. Checking offsets
            // to either side is what stops the bot from being routed through a
            // gap it cannot physically fit into.
            var nx = -dy / length * 16F;
            var ny = dx / length * 16F;

            for (var i = 1; i < steps; i++)
            {
                var t = (float)i / steps;
                var px = ax + dx * t;
                var py = ay + dy * t;

                if (!InsideSector(px, py, sector))
                {
                    return false;
                }

                if (!InsideSector(px + nx, py + ny, sector))
                {
                    return false;
                }

                if (!InsideSector(px - nx, py - ny, sector))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsDoorSpecial(int special)
        {
            switch (special)
            {
                case 1:
                case 26:
                case 27:
                case 28:
                case 31:
                case 32:
                case 33:
                case 34:
                case 117:
                case 118:
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Whether a player can step from one side of a line to the other.
        ///
        /// The limits are the engine's own: a 24-unit step up, and 56 units of
        /// head room. Doors and lifts are exempt because they move out of the
        /// way once used.
        /// </summary>
        private static bool CanWalk(LineDef line, Sector from, Sector to)
        {
            if (IsDoorSpecial((int)line.Special))
            {
                return true;
            }

            var step = (to.FloorHeight - from.FloorHeight).ToFloat();
            if (step > 24F)
            {
                return false;
            }

            var ceiling = Math.Min(from.CeilingHeight.ToFloat(), to.CeilingHeight.ToFloat());
            var floor = Math.Max(from.FloorHeight.ToFloat(), to.FloorHeight.ToFloat());

            return ceiling - floor >= 56F;
        }

        ////////////////////////////////////////////////////////////
        // Queries
        ////////////////////////////////////////////////////////////

        /// <summary>Closest node to a world position, or -1.</summary>
        public int NodeAt(Fixed x, Fixed y)
        {
            var px = x.ToFloat();
            var py = y.ToFloat();

            var sector = Geometry.PointInSubsector(x, y, map).Sector.Number;

            var best = -1;
            var bestDistance = float.MaxValue;

            for (var i = 0; i < nodeCount; i++)
            {
                if (nodeSector[i] != sector)
                {
                    continue;
                }

                var dx = nodeX[i] - px;
                var dy = nodeY[i] - py;
                var distance = dx * dx + dy * dy;

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }

            return best;
        }

        /// <summary>
        /// Shortest route between two nodes. Returns the number of nodes written
        /// to <paramref name="path"/>, or 0 when there is no route.
        /// </summary>
        public int FindPath(int start, int goal, int[] path)
        {
            if (start < 0 || goal < 0 || start >= nodeCount || goal >= nodeCount)
            {
                return 0;
            }

            if (start == goal)
            {
                path[0] = start;
                return 1;
            }

            stamp++;
            open.Clear();

            cost[start] = 0;
            cameFrom[start] = -1;
            visitStamp[start] = stamp;
            closed[start] = false;

            open.Push(start, 0);

            while (open.Count > 0)
            {
                var current = open.Pop();

                if (current == goal)
                {
                    return Reconstruct(goal, path);
                }

                if (closed[current])
                {
                    continue;
                }

                closed[current] = true;

                for (var e = edgeStart[current]; e < edgeStart[current + 1]; e++)
                {
                    var next = edgeTarget[e];

                    if (visitStamp[next] != stamp)
                    {
                        visitStamp[next] = stamp;
                        cost[next] = float.MaxValue;
                        closed[next] = false;
                        cameFrom[next] = -1;
                    }

                    if (closed[next])
                    {
                        continue;
                    }

                    var candidate = cost[current] + edgeCost[e];
                    if (candidate >= cost[next])
                    {
                        continue;
                    }

                    cost[next] = candidate;
                    cameFrom[next] = current;

                    open.Push(next, candidate);
                }
            }

            return 0;
        }

        private int Reconstruct(int goal, int[] path)
        {
            var length = 0;
            var node = goal;

            while (node >= 0)
            {
                path[length++] = node;
                node = cameFrom[node];
            }

            Array.Reverse(path, 0, length);
            return length;
        }

        /// <summary>How many nodes are reachable from a given one.</summary>
        public int ReachableCount(int from)
        {
            if (from < 0 || from >= nodeCount)
            {
                return 0;
            }

            var seen = new bool[nodeCount];
            var queue = new Queue<int>();

            seen[from] = true;
            queue.Enqueue(from);

            var count = 0;

            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                count++;

                for (var e = edgeStart[node]; e < edgeStart[node + 1]; e++)
                {
                    var next = edgeTarget[e];
                    if (!seen[next])
                    {
                        seen[next] = true;
                        queue.Enqueue(next);
                    }
                }
            }

            return count;
        }

        /// <summary>Nodes belonging to a sector, for picking a goal.</summary>
        public int FirstNodeOfSector(int sector)
        {
            for (var i = 0; i < nodeCount; i++)
            {
                if (nodeSector[i] == sector)
                {
                    return i;
                }
            }

            return -1;
        }

        public float GetX(int node) { return nodeX[node]; }
        public float GetY(int node) { return nodeY[node]; }
        public int GetSector(int node) { return nodeSector[node]; }
        public bool IsDoor(int node) { return nodeDoor[node]; }

        public int NodeCount { get { return nodeCount; } }
        public int EdgeCount { get; private set; }

        /// <summary>
        /// Minimal binary min-heap. The framework has none, and a sorted list
        /// would be O(n) per insert.
        /// </summary>
        private sealed class BinaryHeap
        {
            private int[] items;
            private float[] keys;
            private int count;

            public BinaryHeap(int capacity)
            {
                items = new int[Math.Max(capacity, 16)];
                keys = new float[items.Length];
            }

            public int Count { get { return count; } }

            public void Clear()
            {
                count = 0;
            }

            public void Push(int item, float key)
            {
                if (count == items.Length)
                {
                    Array.Resize(ref items, items.Length * 2);
                    Array.Resize(ref keys, keys.Length * 2);
                }

                var i = count++;
                items[i] = item;
                keys[i] = key;

                while (i > 0)
                {
                    var parent = (i - 1) / 2;
                    if (keys[parent] <= keys[i])
                    {
                        break;
                    }

                    Swap(parent, i);
                    i = parent;
                }
            }

            public int Pop()
            {
                var result = items[0];
                count--;

                if (count > 0)
                {
                    items[0] = items[count];
                    keys[0] = keys[count];

                    var i = 0;
                    while (true)
                    {
                        var left = i * 2 + 1;
                        var right = left + 1;
                        var smallest = i;

                        if (left < count && keys[left] < keys[smallest]) smallest = left;
                        if (right < count && keys[right] < keys[smallest]) smallest = right;

                        if (smallest == i)
                        {
                            break;
                        }

                        Swap(i, smallest);
                        i = smallest;
                    }
                }

                return result;
            }

            private void Swap(int a, int b)
            {
                var item = items[a];
                items[a] = items[b];
                items[b] = item;

                var key = keys[a];
                keys[a] = keys[b];
                keys[b] = key;
            }
        }
    }
}
