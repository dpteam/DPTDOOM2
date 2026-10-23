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
    /// Navigation graph for a level, built from the map at load time.
    ///
    /// The nodes are **sectors** and the edges are **two-sided linedefs**. That
    /// sounds obvious, and it is: a two-sided line is by definition a way to
    /// walk from the sector on one side to the sector on the other, so the
    /// adjacency is exactly the map data and cannot be got wrong.
    ///
    /// A first attempt used BSP subsectors as nodes, reasoning that a subsector
    /// is convex so its centre is always walkable. The convexity part is true,
    /// but a subsector's segs are not a complete record of which linedefs touch
    /// it once the BSP has split long lines, and the resulting graph connected
    /// only 18 of 194 nodes. Sector adjacency has no such failure mode.
    ///
    /// Costs are straight-line distances between sector centres. The centres are
    /// only used to pick a route and to order candidates; the bot never walks to
    /// one. It walks to **portal midpoints** (the middle of the shared linedef),
    /// which is a point it can actually stand on.
    ///
    /// Pathfinding is Dijkstra, not A*: with a few hundred sectors it is
    /// instant, and it removes any question about whether the heuristic is
    /// admissible when a sector centre can fall outside its own sector.
    /// </summary>
    public sealed class NavGraph
    {
        private readonly Map map;
        private readonly int nodeCount;

        private readonly float[] nodeX;
        private readonly float[] nodeY;

        // Adjacency in compressed sparse row form: edges of node i are
        // edgeTarget[edgeStart[i] .. edgeStart[i + 1]).
        private readonly int[] edgeStart;
        private readonly int[] edgeTarget;
        private readonly float[] edgeCost;

        // Scratch space reused by the search, so pathfinding allocates nothing.
        private readonly float[] cost;
        private readonly int[] cameFrom;
        private readonly int[] visitStamp;
        private readonly bool[] closed;
        private int stamp;

        private readonly BinaryHeap open;

        public NavGraph(Map map)
        {
            this.map = map;

            nodeCount = map.Sectors.Length;
            nodeX = new float[nodeCount];
            nodeY = new float[nodeCount];

            for (var i = 0; i < nodeCount; i++)
            {
                ComputeCentre(map.Sectors[i], out nodeX[i], out nodeY[i]);
            }

            BuildEdges(out edgeStart, out edgeTarget, out edgeCost);

            cost = new float[nodeCount];
            cameFrom = new int[nodeCount];
            visitStamp = new int[nodeCount];
            closed = new bool[nodeCount];
            open = new BinaryHeap(nodeCount * 4);

            EdgeCount = edgeTarget.Length;
        }

        ////////////////////////////////////////////////////////////
        // Building
        ////////////////////////////////////////////////////////////

        /// <summary>
        /// Rough centre of a sector: the average of its line midpoints.
        ///
        /// Not guaranteed to be inside a concave sector, and deliberately not
        /// used as a walking target for that reason. It only anchors the search.
        /// </summary>
        private void ComputeCentre(Sector sector, out float x, out float y)
        {
            var sumX = 0.0;
            var sumY = 0.0;
            var count = 0;

            foreach (var line in sector.Lines)
            {
                sumX += (line.Vertex1.X.ToFloat() + line.Vertex2.X.ToFloat()) * 0.5;
                sumY += (line.Vertex1.Y.ToFloat() + line.Vertex2.Y.ToFloat()) * 0.5;
                count++;
            }

            if (count == 0)
            {
                x = 0;
                y = 0;
            }
            else
            {
                x = (float)(sumX / count);
                y = (float)(sumY / count);
            }
        }

        private void BuildEdges(out int[] start, out int[] target, out float[] costOut)
        {
            // Edges are directional. A sector graph that ignores heights happily
            // routes the bot up a 64-unit ledge, which it then walks into
            // forever; the bot really can drop down such a ledge but not climb
            // it. Doors and lifts are exempt because they open.
            var walkable = new bool[map.Lines.Length, 2];
            var degree = new int[nodeCount];
            var links = 0;

            for (var i = 0; i < map.Lines.Length; i++)
            {
                var line = map.Lines[i];

                if (line.BackSector == null || line.FrontSector == null)
                {
                    continue;
                }

                var a = line.FrontSector.Number;
                var b = line.BackSector.Number;

                if (a == b || a < 0 || b < 0 || a >= nodeCount || b >= nodeCount)
                {
                    continue;
                }

                var forward = CanWalk(line, line.FrontSector, line.BackSector);
                var backward = CanWalk(line, line.BackSector, line.FrontSector);

                walkable[i, 0] = forward;
                walkable[i, 1] = backward;

                if (forward)
                {
                    degree[a]++;
                    links++;
                }

                if (backward)
                {
                    degree[b]++;
                    links++;
                }
            }

            start = new int[nodeCount + 1];
            for (var i = 0; i < nodeCount; i++)
            {
                start[i + 1] = start[i] + degree[i];
            }

            PortalCount = links;
            target = new int[links];
            costOut = new float[links];

            var fill = new int[nodeCount];
            Array.Copy(start, fill, nodeCount);

            for (var i = 0; i < map.Lines.Length; i++)
            {
                var line = map.Lines[i];

                if (line.BackSector == null || line.FrontSector == null)
                {
                    continue;
                }

                var a = line.FrontSector.Number;
                var b = line.BackSector.Number;

                if (a == b || a < 0 || b < 0 || a >= nodeCount || b >= nodeCount)
                {
                    continue;
                }

                var distance = Distance(a, b);

                if (walkable[i, 0])
                {
                    target[fill[a]] = b;
                    costOut[fill[a]] = distance;
                    fill[a]++;
                }

                if (walkable[i, 1])
                {
                    target[fill[b]] = a;
                    costOut[fill[b]] = distance;
                    fill[b]++;
                }
            }
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
            var special = (int)line.Special;

            if (IsDoorSpecial(special))
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

        private float Distance(int a, int b)
        {
            var dx = nodeX[a] - nodeX[b];
            var dy = nodeY[a] - nodeY[b];
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        ////////////////////////////////////////////////////////////
        // Queries
        ////////////////////////////////////////////////////////////

        /// <summary>Sector containing a world position.</summary>
        public int NodeAt(Fixed x, Fixed y)
        {
            return Geometry.PointInSubsector(x, y, map).Sector.Number;
        }

        /// <summary>
        /// Shortest route between two sectors. Returns the number of nodes
        /// written to <paramref name="path"/>, or 0 when there is no route.
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

        /// <summary>
        /// The point on the linedef joining two sectors that is closest to a
        /// reference position, which is where the bot should walk to cross.
        ///
        /// Not the midpoint: a long linedef can span a wall with only part of it
        /// open, and its midpoint then sits inside solid geometry. Projecting
        /// the bot onto the segment gives the nearest point of the opening, and
        /// picks the right one when a doorway is made of several lines.
        /// </summary>
        public bool FindPortal(int from, int to, float nearX, float nearY, out float x, out float y, out LineDef line)
        {
            x = 0;
            y = 0;
            line = null;

            if (from < 0 || from >= nodeCount)
            {
                return false;
            }

            var best = float.MaxValue;

            foreach (var candidate in map.Sectors[from].Lines)
            {
                if (candidate.BackSector == null || candidate.FrontSector == null)
                {
                    continue;
                }

                var other = candidate.FrontSector.Number == from
                    ? candidate.BackSector.Number
                    : candidate.FrontSector.Number;

                if (other != to)
                {
                    continue;
                }

                var ax = candidate.Vertex1.X.ToFloat();
                var ay = candidate.Vertex1.Y.ToFloat();
                var bx = candidate.Vertex2.X.ToFloat();
                var by = candidate.Vertex2.Y.ToFloat();

                var dx = bx - ax;
                var dy = by - ay;
                var lengthSquared = dx * dx + dy * dy;

                float t;
                if (lengthSquared <= 0.0001F)
                {
                    t = 0;
                }
                else
                {
                    t = ((nearX - ax) * dx + (nearY - ay) * dy) / lengthSquared;
                    if (t < 0) t = 0;
                    else if (t > 1) t = 1;
                }

                var px = ax + dx * t;
                var py = ay + dy * t;

                var distance = (px - nearX) * (px - nearX) + (py - nearY) * (py - nearY);

                if (distance < best)
                {
                    best = distance;
                    x = px;
                    y = py;
                    line = candidate;
                }
            }

            return line != null;
        }

        /// <summary>How many sectors are reachable from a given one.</summary>
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

        public float GetX(int node) { return nodeX[node]; }
        public float GetY(int node) { return nodeY[node]; }
        public int GetSector(int node) { return node; }

        public int NodeCount { get { return nodeCount; } }
        public int EdgeCount { get; private set; }
        public int PortalCount { get; private set; }

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
