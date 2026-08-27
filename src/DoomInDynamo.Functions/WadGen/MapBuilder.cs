using System;
using System.Collections.Generic;
using System.Globalization;

namespace DoomInDynamo.WadGen
{
    /// <summary>
    /// Turns a <see cref="BuildingModel"/> into the editing-level Doom map: every
    /// wall segment becomes a thin solid "pillar" (a closed loop of one-sided
    /// linedefs standing in a single big sector), an outer boundary box seals the
    /// map, and items/monsters are scattered by rejection sampling. Running
    /// <see cref="BspBuilder"/> afterwards makes the result playable.
    /// </summary>
    internal sealed class MapBuilder
    {
        // 16 map units per foot puts a 3 ft door at 48 units (the 32-unit-radius
        // player fits) and a 9 ft ceiling at 144 units (between Doom's usual 128
        // and 160) - buildings come out feeling like Doom levels, not dollhouses.
        private const double UnitsPerFoot = 16.0;

        // The binding size constraint is NOT the int16 coordinate range but the
        // BLOCKMAP: its offset table alone spends (span/128+1)^2 words of a lump
        // that BlockMap.FromWad reads via signed int16 word offsets (32767 words
        // total). A 10000-unit half extent keeps the offset table around 25k words
        // with headroom for the block lists; huge buildings get scaled down to it
        // instead of failing in BlockmapBuilder.
        private const double MaxHalfExtent = 10000.0;
        private const int BoundaryMargin = 192;

        private static readonly string[] WallTextures = { "STARTAN3", "STONE2", "BROWN1", "SLADWALL" };
        private const string BoundaryTexture = "BROWNGRN";
        private const string FloorFlat = "FLOOR4_8";
        private const string CeilingFlat = "CEIL3_5";

        private sealed class ThingDef
        {
            public readonly int Type;
            public readonly int Weight;
            public readonly double Clearance;
            public readonly bool Blocking;
            public readonly double Radius;

            // The name is documentation for the tables below; the game only sees Type.
            public ThingDef(int type, int weight, double clearance, bool blocking, double radius, string name)
            {
                Type = type;
                Weight = weight;
                Clearance = clearance;
                Blocking = blocking;
                Radius = radius;
            }
        }

        // Every type here exists in shareware Doom 1, registered Doom, Doom 2 and
        // Freedoom - no Doom-2-only things (no super shotgun, no chaingunners), so
        // the map works with whatever IWAD the player browses.
        // Radii are the engine's own (DoomInfo.MobjInfos): they matter because Doom
        // collides things per-axis (square boxes), so spawn spacing has to be
        // measured the same way - see IsFree.
        private static readonly ThingDef[] ItemPool =
        {
            new ThingDef(2014, 12, 24, false, 20, "health bonus"),
            new ThingDef(2015, 12, 24, false, 20, "armor bonus"),
            new ThingDef(2011, 10, 24, false, 20, "stimpack"),
            new ThingDef(2012, 6, 24, false, 20, "medikit"),
            new ThingDef(2018, 3, 24, false, 20, "green armor"),
            new ThingDef(2007, 10, 24, false, 20, "ammo clip"),
            new ThingDef(2008, 8, 24, false, 20, "shell pack"),
            new ThingDef(2048, 4, 24, false, 20, "ammo box"),
            new ThingDef(2049, 3, 24, false, 20, "shell box"),
            new ThingDef(2001, 3, 24, false, 20, "shotgun"),
            new ThingDef(2002, 2, 24, false, 20, "chaingun"),
            new ThingDef(8, 2, 24, false, 20, "backpack"),
            new ThingDef(2035, 6, 26, true, 10, "barrel"),
        };

        private static readonly ThingDef[] MonsterPool =
        {
            new ThingDef(3004, 10, 33, true, 20, "zombieman"),
            new ThingDef(9, 6, 33, true, 20, "shotgun guy"),
            new ThingDef(3001, 8, 33, true, 20, "imp"),
            new ThingDef(3002, 3, 46, true, 30, "demon"),
        };

        private readonly DoomMap map = new DoomMap();
        private readonly Dictionary<long, int> vertexLookup = new Dictionary<long, int>();

        // Un-rounded pillar outlines, kept for the placement tests.
        private readonly List<double[][]> pillarPolygons = new List<double[][]>();

        private int pillarMinX = int.MaxValue, pillarMinY = int.MaxValue;
        private int pillarMaxX = int.MinValue, pillarMaxY = int.MinValue;

        // The sealed play area, captured when the boundary is added (AddLoop keeps
        // growing the pillar bbox afterwards, so placement must not derive from it).
        private int boundaryLeft, boundaryBottom, boundaryRight, boundaryTop;
        private int ringAnchorX, ringAnchorY;

        public string Report { get; private set; } = "";

        public static DoomMap Build(BuildingModel model, int seed, int itemCount, bool includeMonsters, out string report)
        {
            var builder = new MapBuilder();
            builder.BuildCore(model, seed, itemCount, includeMonsters);
            report = builder.Report;
            return builder.map;
        }

        private void BuildCore(BuildingModel model, int seed, int itemCount, bool includeMonsters)
        {
            var walls = new List<WallSegment>();
            foreach (var wall in model.Walls)
            {
                var lengthFt = Math.Sqrt(
                    (wall.X2 - wall.X1) * (wall.X2 - wall.X1) +
                    (wall.Y2 - wall.Y1) * (wall.Y2 - wall.Y1));
                if (lengthFt > 0.05)
                {
                    walls.Add(wall);
                }
            }

            if (walls.Count == 0)
            {
                throw new InvalidOperationException("The model contains no usable wall geometry.");
            }

            double minXFt = double.MaxValue, minYFt = double.MaxValue;
            double maxXFt = double.MinValue, maxYFt = double.MinValue;
            var maxHeightFt = 0.0;
            foreach (var wall in walls)
            {
                minXFt = Math.Min(minXFt, Math.Min(wall.X1, wall.X2));
                minYFt = Math.Min(minYFt, Math.Min(wall.Y1, wall.Y2));
                maxXFt = Math.Max(maxXFt, Math.Max(wall.X1, wall.X2));
                maxYFt = Math.Max(maxYFt, Math.Max(wall.Y1, wall.Y2));
                maxHeightFt = Math.Max(maxHeightFt, wall.HeightFt);
            }

            var centerX = (minXFt + maxXFt) / 2;
            var centerY = (minYFt + maxYFt) / 2;
            var halfExtentFt = Math.Max(
                Math.Max(maxXFt - centerX, centerX - minXFt),
                Math.Max(maxYFt - centerY, centerY - minYFt)) + 2.0;

            var scale = Math.Min(
                UnitsPerFoot,
                (MaxHalfExtent - BoundaryMargin - 64) / halfExtentFt);

            map.Sectors.Add(new MapSector
            {
                FloorHeight = 0,
                CeilingHeight = Clamp((int)Math.Round(maxHeightFt * scale), 64, 512),
                FloorFlat = FloorFlat,
                CeilingFlat = CeilingFlat,
                LightLevel = 192,
                Special = 0,
                Tag = 0
            });

            var pillarCount = 0;
            foreach (var wall in walls)
            {
                if (AddWallPillar(wall, centerX, centerY, scale, pillarCount))
                {
                    pillarCount++;
                }
            }

            if (pillarCount == 0)
            {
                throw new InvalidOperationException("All walls were too short to convert at this scale.");
            }

            AddBoundary();

            var rng = new Random(seed);
            var placed = new List<PlacedThing>();
            var playerNote = PlacePlayerStart(model, centerX, centerY, scale, rng, placed);
            var itemNote = PlaceItems(itemCount, includeMonsters, rng, placed);

            Report =
                "Exported level '" + model.LevelName + "' from '" + model.DocumentTitle + "': " +
                walls.Count + " wall segments -> " + pillarCount + " pillars, " +
                map.Linedefs.Count + " linedefs, ceiling " + map.Sectors[0].CeilingHeight +
                " units, scale " + scale.ToString("0.##", CultureInfo.InvariantCulture) +
                " units/ft. " + playerNote + " " + itemNote +
                " An exit switch hides on the outer boundary - hug the edge and press Space.";
        }

        private bool AddWallPillar(WallSegment wall, double centerX, double centerY, double scale, int pillarIndex)
        {
            var x1 = (wall.X1 - centerX) * scale;
            var y1 = (wall.Y1 - centerY) * scale;
            var x2 = (wall.X2 - centerX) * scale;
            var y2 = (wall.Y2 - centerY) * scale;

            var dx = x2 - x1;
            var dy = y2 - y1;
            var length = Math.Sqrt(dx * dx + dy * dy);
            if (length < 4.0)
            {
                return false;
            }

            dx /= length;
            dy /= length;

            // Left-hand normal; half-thickness floored at 3 units so a rounded
            // pillar can never collapse into a zero-area sliver.
            var nx = -dy;
            var ny = dx;
            var halfW = Math.Max(wall.ThicknessFt * scale / 2.0, 3.0);

            var corners = new[]
            {
                new[] { x1 + nx * halfW, y1 + ny * halfW },
                new[] { x2 + nx * halfW, y2 + ny * halfW },
                new[] { x2 - nx * halfW, y2 - ny * halfW },
                new[] { x1 - nx * halfW, y1 - ny * halfW },
            };

            // Counter-clockwise winding puts each edge's right-hand (front) side on
            // the OUTSIDE of the loop, which is where the surrounding sector is.
            if (SignedArea(corners) < 0)
            {
                Array.Reverse(corners);
            }

            var texture = WallTextures[pillarIndex % WallTextures.Length];
            AddLoop(corners, texture, 0);
            pillarPolygons.Add(corners);
            return true;
        }

        private void AddBoundary()
        {
            var left = boundaryLeft = pillarMinX - BoundaryMargin;
            var bottom = boundaryBottom = pillarMinY - BoundaryMargin;
            var right = boundaryRight = pillarMaxX + BoundaryMargin;
            var top = boundaryTop = pillarMaxY + BoundaryMargin;

            // A guaranteed-free spot in the pillar-free ring between the building
            // and the boundary - the player-start fallback of last resort.
            ringAnchorX = pillarMinX - 96;
            ringAnchorY = pillarMinY - 96;

            // Clockwise winding: the fronts of the boundary walls face INTO the map.
            var corners = new[]
            {
                new[] { (double)left, (double)bottom },
                new[] { (double)left, (double)top },
                new[] { (double)right, (double)top },
                new[] { (double)right, (double)bottom },
            };

            // The first boundary linedef becomes an S1 exit switch - a small easter
            // egg, and the only way to actually finish the level.
            AddLoop(corners, BoundaryTexture, DoomConst.SpecialS1Exit);
        }

        /// <summary>Adds a closed loop of one-sided linedefs (given corner order is
        /// trusted - CCW for solids, CW for the boundary). The exit special, if any,
        /// goes on the loop's first line only.</summary>
        private void AddLoop(double[][] corners, string texture, int firstLineSpecial)
        {
            var indices = new int[corners.Length];
            for (var i = 0; i < corners.Length; i++)
            {
                var x = (int)Math.Round(corners[i][0]);
                var y = (int)Math.Round(corners[i][1]);
                indices[i] = GetVertex(x, y);

                pillarMinX = Math.Min(pillarMinX, x);
                pillarMinY = Math.Min(pillarMinY, y);
                pillarMaxX = Math.Max(pillarMaxX, x);
                pillarMaxY = Math.Max(pillarMaxY, y);
            }

            for (var i = 0; i < corners.Length; i++)
            {
                var next = (i + 1) % corners.Length;
                if (indices[i] == indices[next])
                {
                    continue;
                }

                map.Sidedefs.Add(new MapSidedef { Middle = texture, Sector = 0 });
                map.Linedefs.Add(new MapLinedef
                {
                    V1 = indices[i],
                    V2 = indices[next],
                    Flags = DoomConst.LineFlagBlocking,
                    Special = i == 0 ? firstLineSpecial : 0,
                    Tag = 0,
                    FrontSide = map.Sidedefs.Count - 1,
                    BackSide = -1
                });
            }
        }

        private sealed class PlacedThing
        {
            public double X;
            public double Y;
            public bool Blocking;
            public double Radius;
        }

        private string PlacePlayerStart(BuildingModel model, double centerX, double centerY, double scale, Random rng, List<PlacedThing> placed)
        {
            // Prefer spawning inside an actual room (Revit room location points are
            // reliable interior points); fall back to random sampling; and if the
            // building is wall-to-wall solid, the boundary ring is pillar-free by
            // construction, so the final fallback always works.
            var candidates = new List<double[]>();
            foreach (var room in model.Rooms)
            {
                candidates.Add(new[] { (room.X - centerX) * scale, (room.Y - centerY) * scale });
            }
            Shuffle(candidates, rng);

            double[] spot = null;
            var note = "";
            foreach (var candidate in candidates)
            {
                if (IsFree(candidate[0], candidate[1], 40, true, PlayerRadius, placed))
                {
                    spot = candidate;
                    note = "Player start in a room.";
                    break;
                }
            }

            if (spot == null)
            {
                for (var attempt = 0; attempt < 3000 && spot == null; attempt++)
                {
                    var p = RandomPoint(rng, 40);
                    if (IsFree(p[0], p[1], 40, true, PlayerRadius, placed))
                    {
                        spot = p;
                        note = "Player start at a random open spot.";
                    }
                }
            }

            if (spot == null)
            {
                spot = new[] { (double)ringAnchorX, (double)ringAnchorY };
                note = "Player start on the boundary ring (no open interior spot found).";
            }

            var angle = SnapAngle(Math.Atan2(-spot[1], -spot[0]));
            map.Things.Add(new MapThingRec
            {
                X = (int)Math.Round(spot[0]),
                Y = (int)Math.Round(spot[1]),
                AngleDegrees = angle,
                Type = 1,
                Flags = DoomConst.ThingAllSkills
            });
            placed.Add(new PlacedThing { X = spot[0], Y = spot[1], Blocking = true, Radius = PlayerRadius });
            return note;
        }

        private string PlaceItems(int itemCount, bool includeMonsters, Random rng, List<PlacedThing> placed)
        {
            itemCount = Clamp(itemCount, 0, 500);

            var pool = new List<ThingDef>(ItemPool);
            if (includeMonsters)
            {
                pool.AddRange(MonsterPool);
            }

            var totalWeight = 0;
            foreach (var def in pool)
            {
                totalWeight += def.Weight;
            }

            var placedCount = 0;
            var monsters = 0;
            var attempts = 0;
            var maxAttempts = itemCount * 60;

            while (placedCount < itemCount && attempts < maxAttempts)
            {
                attempts++;

                var pick = rng.Next(totalWeight);
                ThingDef def = null;
                foreach (var candidate in pool)
                {
                    pick -= candidate.Weight;
                    if (pick < 0)
                    {
                        def = candidate;
                        break;
                    }
                }

                var p = RandomPoint(rng, def.Clearance);
                if (!IsFree(p[0], p[1], def.Clearance, def.Blocking, def.Radius, placed))
                {
                    continue;
                }

                map.Things.Add(new MapThingRec
                {
                    X = (int)Math.Round(p[0]),
                    Y = (int)Math.Round(p[1]),
                    AngleDegrees = 45 * rng.Next(8),
                    Type = def.Type,
                    Flags = DoomConst.ThingAllSkills
                });
                placed.Add(new PlacedThing { X = p[0], Y = p[1], Blocking = def.Blocking, Radius = def.Radius });
                placedCount++;

                if (Array.IndexOf(MonsterPool, def) >= 0)
                {
                    monsters++;
                }
            }

            return "Placed " + placedCount + " things (" + monsters + " monsters) with seed-driven randomness.";
        }

        private double[] RandomPoint(Random rng, double clearance)
        {
            var margin = clearance + 8;
            var left = boundaryLeft + margin;
            var right = boundaryRight - margin;
            var bottom = boundaryBottom + margin;
            var top = boundaryTop - margin;

            return new[]
            {
                left + rng.NextDouble() * (right - left),
                bottom + rng.NextDouble() * (top - bottom)
            };
        }

        // The engine's player radius (GameConst / DoomInfo): what the player-start
        // spawn must keep clear of other blocking things.
        private const double PlayerRadius = 16.0;

        private bool IsFree(double x, double y, double clearance, bool blocking, double radius, List<PlacedThing> placed)
        {
            if (x < boundaryLeft + clearance + 8 ||
                x > boundaryRight - clearance - 8 ||
                y < boundaryBottom + clearance + 8 ||
                y > boundaryTop - clearance - 8)
            {
                return false;
            }

            foreach (var polygon in pillarPolygons)
            {
                if (PointInPolygon(polygon, x, y))
                {
                    return false;
                }
                if (DistanceToPolygon(polygon, x, y) < clearance)
                {
                    return false;
                }
            }

            foreach (var other in placed)
            {
                var dx = other.X - x;
                var dy = other.Y - y;
                if (blocking && other.Blocking)
                {
                    // Doom collision is per-axis (ThingMovement.CheckThing blocks
                    // when BOTH |dx| and |dy| are under the radii sum), so spacing
                    // must be Chebyshev, not Euclidean - a demon pair 51 units
                    // apart diagonally would otherwise spawn interlocked and stand
                    // frozen for the whole game. +4 keeps boxes from being flush.
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) < radius + other.Radius + 4)
                    {
                        return false;
                    }
                }
                else if (dx * dx + dy * dy < 24.0 * 24.0)
                {
                    return false;
                }
            }

            return true;
        }

        private int GetVertex(int x, int y)
        {
            var key = ((long)x << 32) ^ (uint)y;
            int index;
            if (vertexLookup.TryGetValue(key, out index))
            {
                return index;
            }

            map.Vertices.Add(new MapVertex(x, y));
            index = map.Vertices.Count - 1;
            vertexLookup[key] = index;
            return index;
        }

        private static double SignedArea(double[][] polygon)
        {
            var sum = 0.0;
            for (var i = 0; i < polygon.Length; i++)
            {
                var j = (i + 1) % polygon.Length;
                sum += polygon[i][0] * polygon[j][1] - polygon[j][0] * polygon[i][1];
            }
            return sum / 2;
        }

        private static bool PointInPolygon(double[][] polygon, double x, double y)
        {
            var inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                if ((polygon[i][1] > y) != (polygon[j][1] > y) &&
                    x < (polygon[j][0] - polygon[i][0]) * (y - polygon[i][1]) /
                        (polygon[j][1] - polygon[i][1]) + polygon[i][0])
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        private static double DistanceToPolygon(double[][] polygon, double x, double y)
        {
            var best = double.MaxValue;
            for (var i = 0; i < polygon.Length; i++)
            {
                var j = (i + 1) % polygon.Length;
                best = Math.Min(best, DistanceToSegment(
                    x, y, polygon[i][0], polygon[i][1], polygon[j][0], polygon[j][1]));
            }
            return best;
        }

        private static double DistanceToSegment(double px, double py, double x1, double y1, double x2, double y2)
        {
            var dx = x2 - x1;
            var dy = y2 - y1;
            var lengthSquared = dx * dx + dy * dy;
            var t = lengthSquared < 1e-12
                ? 0
                : Math.Max(0, Math.Min(1, ((px - x1) * dx + (py - y1) * dy) / lengthSquared));
            var cx = x1 + t * dx;
            var cy = y1 + t * dy;
            return Math.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
        }

        private static int SnapAngle(double radians)
        {
            var degrees = radians * 180.0 / Math.PI;
            var snapped = (int)Math.Round(degrees / 45.0) * 45;
            return ((snapped % 360) + 360) % 360;
        }

        private static void Shuffle<T>(List<T> list, Random rng)
        {
            for (var i = list.Count - 1; i > 0; i--)
            {
                var j = rng.Next(i + 1);
                var tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
        }

        private static int Clamp(int value, int min, int max)
        {
            return value < min ? min : value > max ? max : value;
        }
    }
}
