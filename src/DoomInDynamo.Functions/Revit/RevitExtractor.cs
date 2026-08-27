using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using RevitServices.Persistence;

namespace DoomInDynamo.Revit
{
    /// <summary>
    /// The only file in this assembly that touches Autodesk.Revit.DB / RevitServices.
    /// Its one entry point deliberately takes and returns Revit-free types
    /// (see BuildingModel) so the assembly can be imported by Dynamo Sandbox, where
    /// the Revit DLLs don't exist - the CLR only tries to load them when this method
    /// actually runs, which only happens inside Dynamo for Revit.
    /// </summary>
    internal static class RevitExtractor
    {
        /// <summary>Sub-segments shorter than this (feet) are noise from tessellation
        /// or door cuts landing near a vertex - too small to matter at Doom scale.</summary>
        private const double MinSegmentFt = 0.25;

        /// <summary>Extra clearance added to a door's width when cutting its opening,
        /// so the gap survives the later snap to integer Doom map units.</summary>
        private const double DoorGapExtraFt = 0.5;

        private const double FallbackThicknessFt = 0.5;
        private const double FallbackHeightFt = 10.0;
        private const double FallbackDoorWidthFt = 3.0;

        /// <summary>
        /// Reads walls, hosted doors and rooms from the active Revit document and
        /// flattens them into the pure-data <see cref="WadGen.BuildingModel"/>.
        /// Door openings are cut out of the wall centerlines here, so WadGen never
        /// has to know that doors exist. Read-only: no transactions.
        /// </summary>
        /// <param name="levelName">Base level to export (case-insensitive), or empty
        /// to auto-pick the level that hosts the most walls.</param>
        internal static WadGen.BuildingModel ExtractCurrentDocument(string levelName)
        {
            Document doc = DocumentManager.Instance != null
                ? DocumentManager.Instance.CurrentDBDocument
                : null;
            if (doc == null)
            {
                throw new InvalidOperationException(
                    "No active Revit document - this node needs to run inside Dynamo for Revit with a project open.");
            }

            // Only walls driven by a location curve can become map geometry; walls
            // without one (some in-place/stacked oddities) have nothing to trace.
            // Design options: a bare collector returns elements from EVERY option,
            // primary and secondary alike - keep only what the user actually sees
            // (main model + primary options), or mutually-exclusive layouts would
            // export on top of each other.
            List<Wall> allWalls = new FilteredElementCollector(doc)
                .OfClass(typeof(Wall))
                .OfType<Wall>()
                .Where(w => w.Location is LocationCurve && IsMainModelOrPrimary(w))
                .ToList();

            Dictionary<string, LevelBucket> buckets = BucketByBaseLevel(doc, allWalls);

            string chosenName;
            List<Wall> keptWalls;
            SelectLevel(buckets, (levelName ?? "").Trim(), out chosenName, out keptWalls);

            Dictionary<long, List<FamilyInstance>> doorsByHost = CollectDoorsByHostWall(doc, chosenName);

            var model = new WadGen.BuildingModel();
            foreach (Wall wall in keptWalls)
            {
                ExtractWall(wall, doorsByHost, model.Walls, model.Doors);
            }

            if (model.Walls.Count == 0)
            {
                throw new InvalidOperationException(
                    "No walls found on level '" + chosenName + "' - nothing to convert.");
            }

            model.Rooms = CollectRoomPoints(doc);
            model.LevelName = chosenName;
            model.DocumentTitle = string.IsNullOrEmpty(doc.Title) ? "Untitled" : doc.Title;
            return model;
        }

        /// <summary>Walls grouped under one base-level name. Elevation is the lowest
        /// elevation seen for that name (levels can share names across phases/links)
        /// and is only used as the tie-breaker when auto-picking a level.</summary>
        private sealed class LevelBucket
        {
            public readonly List<Wall> Walls = new List<Wall>();
            public double Elevation = double.PositiveInfinity;
        }

        private static Dictionary<string, LevelBucket> BucketByBaseLevel(Document doc, List<Wall> walls)
        {
            // Case-insensitive so a user-typed level name doesn't have to match
            // Revit's capitalization exactly.
            var buckets = new Dictionary<string, LevelBucket>(StringComparer.OrdinalIgnoreCase);
            foreach (Wall wall in walls)
            {
                // WALL_BASE_CONSTRAINT is the "Base Constraint" parameter; walls
                // whose level can't be resolved (deleted level, exotic wall kinds)
                // land in the "" bucket rather than being dropped outright.
                string name = "";
                double elevation = double.PositiveInfinity;
                Parameter baseParam = wall.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT);
                if (baseParam != null)
                {
                    ElementId levelId = baseParam.AsElementId();
                    if (levelId != null && levelId != ElementId.InvalidElementId)
                    {
                        Level level = doc.GetElement(levelId) as Level;
                        if (level != null)
                        {
                            name = level.Name ?? "";
                            elevation = level.Elevation;
                        }
                    }
                }

                LevelBucket bucket;
                if (!buckets.TryGetValue(name, out bucket))
                {
                    bucket = new LevelBucket();
                    buckets[name] = bucket;
                }
                bucket.Walls.Add(wall);
                if (elevation < bucket.Elevation)
                {
                    bucket.Elevation = elevation;
                }
            }
            return buckets;
        }

        private static void SelectLevel(Dictionary<string, LevelBucket> buckets, string requested,
            out string chosenName, out List<Wall> keptWalls)
        {
            if (requested.Length > 0)
            {
                // Explicit level: report the level's own spelling back, not the
                // user's, so downstream messages match what Revit shows.
                foreach (KeyValuePair<string, LevelBucket> kvp in buckets)
                {
                    if (kvp.Key.Length > 0 && string.Equals(kvp.Key, requested, StringComparison.OrdinalIgnoreCase))
                    {
                        chosenName = kvp.Key;
                        keptWalls = kvp.Value.Walls;
                        return;
                    }
                }

                List<string> available = buckets.Keys
                    .Where(k => k.Length > 0)
                    .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                throw new InvalidOperationException(available.Count > 0
                    ? "No walls found on level '" + requested + "'. Levels that do have walls: "
                        + string.Join(", ", available) + "."
                    : "No walls found on level '" + requested
                        + "' - no wall in the document has a resolvable base level.");
            }

            // Auto-pick: most walls wins; ties go to the lowest level so a model
            // with equal floors exports its ground floor. The "" (unresolved-level)
            // bucket is a last resort only - it's usually junk walls.
            chosenName = null;
            LevelBucket chosen = null;
            foreach (KeyValuePair<string, LevelBucket> kvp in buckets)
            {
                if (kvp.Key.Length == 0)
                {
                    continue;
                }
                if (chosen == null
                    || kvp.Value.Walls.Count > chosen.Walls.Count
                    || (kvp.Value.Walls.Count == chosen.Walls.Count
                        && (kvp.Value.Elevation < chosen.Elevation
                            || (kvp.Value.Elevation == chosen.Elevation
                                && string.CompareOrdinal(kvp.Key, chosenName) < 0))))
                {
                    chosenName = kvp.Key;
                    chosen = kvp.Value;
                }
            }

            LevelBucket unleveled;
            if (chosen == null && buckets.TryGetValue("", out unleveled))
            {
                chosenName = "";
                chosen = unleveled;
            }

            keptWalls = chosen != null ? chosen.Walls : new List<Wall>();
            if (chosenName == null)
            {
                chosenName = "";
            }
        }

        private static Dictionary<long, List<FamilyInstance>> CollectDoorsByHostWall(Document doc, string chosenLevelName)
        {
            // ElementId.Value (long) as the key - IntegerValue is deprecated in
            // current Revit versions.
            var doorsByHost = new Dictionary<long, List<FamilyInstance>>();
            IList<Element> doors = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Doors)
                .WhereElementIsNotElementType()
                .ToElements();
            foreach (Element element in doors)
            {
                FamilyInstance door = element as FamilyInstance;
                if (door == null || door.Host == null || !IsMainModelOrPrimary(door))
                {
                    continue;
                }

                // A multi-storey wall based on the exported level can host doors on
                // OTHER storeys too; cutting those would punch walkable holes where
                // this storey is solid. Only doors on the exported level cut gaps
                // (a door with an unresolvable level only matches the "" bucket,
                // same rule the walls follow).
                Level doorLevel = doc.GetElement(door.LevelId) as Level;
                string doorLevelName = doorLevel != null ? (doorLevel.Name ?? "") : "";
                if (!string.Equals(doorLevelName, chosenLevelName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                long hostId = door.Host.Id.Value;
                List<FamilyInstance> list;
                if (!doorsByHost.TryGetValue(hostId, out list))
                {
                    list = new List<FamilyInstance>();
                    doorsByHost[hostId] = list;
                }
                list.Add(door);
            }
            return doorsByHost;
        }

        private static void ExtractWall(Wall wall, Dictionary<long, List<FamilyInstance>> doorsByHost,
            List<WadGen.WallSegment> output, List<WadGen.DoorOpening> doorOutput)
        {
            Curve curve = ((LocationCurve)wall.Location).Curve;
            if (curve == null)
            {
                return;
            }

            // Tessellate covers straight and curved walls alike: a Line yields just
            // its two endpoints, an Arc a fan of short chords. Everything downstream
            // works on the flat X/Y polyline (plan projection).
            IList<XYZ> raw;
            try
            {
                raw = curve.Tessellate();
            }
            catch
            {
                return; // degenerate/unbound curve - nothing worth exporting
            }
            if (raw == null || raw.Count < 2)
            {
                return;
            }

            var pts = new List<(double X, double Y)>(raw.Count);
            foreach (XYZ p in raw)
            {
                pts.Add((p.X, p.Y));
            }
            double[] stations = CumulativeStations(pts);
            double totalLength = stations[stations.Length - 1];

            // Wall.Width throws for wall kinds with no compound structure (curtain
            // walls being the classic case), so it gets the defensive treatment.
            double thicknessFt;
            try
            {
                thicknessFt = wall.Width;
            }
            catch
            {
                thicknessFt = FallbackThicknessFt;
            }
            if (thicknessFt <= 0.0)
            {
                thicknessFt = FallbackThicknessFt;
            }

            // WALL_USER_HEIGHT_PARAM is "Unconnected Height". Top-constrained walls
            // still report a usable value here; anything missing or absurdly small
            // becomes a plain 10 ft wall - close enough for Doom.
            double heightFt = FallbackHeightFt;
            Parameter heightParam = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);
            if (heightParam != null)
            {
                double h = heightParam.AsDouble();
                if (h > 1.0)
                {
                    heightFt = h;
                }
            }

            // Every hosted door becomes a cut interval along the centerline's arc
            // length; the complement of the merged intervals is what remains solid.
            var cuts = new List<(double Start, double End)>();
            List<FamilyInstance> hostedDoors;
            if (doorsByHost.TryGetValue(wall.Id.Value, out hostedDoors))
            {
                foreach (FamilyInstance door in hostedDoors)
                {
                    // Doors are point-hosted families; one without a LocationPoint
                    // (or with the point unset) can't be placed on the centerline,
                    // so it simply doesn't get an opening.
                    LocationPoint location = door.Location as LocationPoint;
                    XYZ doorPoint = location != null ? location.Point : null;
                    if (doorPoint == null)
                    {
                        continue;
                    }

                    double station = StationOfClosestPoint(pts, stations, doorPoint.X, doorPoint.Y);
                    double halfGap = (GetDoorWidthFt(door) + DoorGapExtraFt) / 2.0;
                    cuts.Add((station - halfGap, station + halfGap));
                }
            }

            List<(double Start, double End)> merged = MergeCuts(cuts, totalLength);

            foreach ((double Start, double End) keep in KeepIntervals(merged, totalLength))
            {
                EmitRun(pts, stations, keep.Start, keep.End, thicknessFt, heightFt, output);
            }

            // Each merged opening also becomes a working in-game door standing in
            // the gap: report where it sits, which way the wall runs there, and how
            // wide the hole is - WadGen builds the door sector from that.
            foreach ((double Start, double End) cut in merged)
            {
                double centerStation = (cut.Start + cut.End) / 2.0;
                (double X, double Y) center = PointAtStation(pts, stations, centerStation);
                (double X, double Y) dir = DirectionAtStation(pts, stations, centerStation);
                if (dir.X == 0.0 && dir.Y == 0.0)
                {
                    continue;
                }

                doorOutput.Add(new WadGen.DoorOpening
                {
                    CX = center.X,
                    CY = center.Y,
                    DirX = dir.X,
                    DirY = dir.Y,
                    WidthFt = cut.End - cut.Start,
                    ThicknessFt = thicknessFt,
                });
            }
        }

        /// <summary>Unit direction of the polyline segment containing the station
        /// (zero vector if the polyline is degenerate there).</summary>
        private static (double X, double Y) DirectionAtStation(List<(double X, double Y)> pts,
            double[] stations, double station)
        {
            int last = pts.Count - 1;
            int segment = last - 1;
            for (int i = 0; i < last; i++)
            {
                if (station <= stations[i + 1])
                {
                    segment = i;
                    break;
                }
            }

            double dx = pts[segment + 1].X - pts[segment].X;
            double dy = pts[segment + 1].Y - pts[segment].Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            return length > 1e-9 ? (dx / length, dy / length) : (0.0, 0.0);
        }

        private static double GetDoorWidthFt(FamilyInstance door)
        {
            // Width lives on the type for standard door families, but some families
            // make it an instance parameter, and purely custom ones only have the
            // generic family width - try all three before giving up.
            FamilySymbol symbol = door.Symbol;
            double width = PositiveDoubleOrZero(symbol != null
                ? symbol.get_Parameter(BuiltInParameter.DOOR_WIDTH) : null);
            if (width <= 0.0)
            {
                width = PositiveDoubleOrZero(door.get_Parameter(BuiltInParameter.DOOR_WIDTH));
            }
            if (width <= 0.0)
            {
                width = PositiveDoubleOrZero(symbol != null
                    ? symbol.get_Parameter(BuiltInParameter.FAMILY_WIDTH_PARAM) : null);
            }
            return width > 0.0 ? width : FallbackDoorWidthFt;
        }

        private static double PositiveDoubleOrZero(Parameter parameter)
        {
            if (parameter == null || !parameter.HasValue)
            {
                return 0.0;
            }
            double value = parameter.AsDouble();
            return value > 0.0 ? value : 0.0;
        }

        /// <summary>Cut intervals clamped to [0, totalLength], sorted and merged -
        /// overlapping doors (double doors, close pairs) become one opening instead
        /// of producing slivers between them.</summary>
        private static List<(double Start, double End)> MergeCuts(
            List<(double Start, double End)> cuts, double totalLength)
        {
            var merged = new List<(double Start, double End)>();
            if (totalLength <= 0.0)
            {
                return merged;
            }

            cuts.Sort((a, b) => a.Start.CompareTo(b.Start));
            foreach ((double Start, double End) cut in cuts)
            {
                double start = Math.Max(0.0, cut.Start);
                double end = Math.Min(totalLength, cut.End);
                if (end <= start)
                {
                    continue; // gap lies entirely off this wall (door projected to an end)
                }
                if (merged.Count > 0 && start <= merged[merged.Count - 1].End)
                {
                    merged[merged.Count - 1] = (merged[merged.Count - 1].Start,
                        Math.Max(merged[merged.Count - 1].End, end));
                }
                else
                {
                    merged.Add((start, end));
                }
            }

            return merged;
        }

        /// <summary>Complement of the merged cut intervals within [0, totalLength] -
        /// i.e. the stretches of centerline that stay solid wall.</summary>
        private static List<(double Start, double End)> KeepIntervals(
            List<(double Start, double End)> merged, double totalLength)
        {
            var keeps = new List<(double Start, double End)>();
            if (totalLength <= 0.0)
            {
                return keeps;
            }

            double cursor = 0.0;
            foreach ((double Start, double End) cut in merged)
            {
                if (cut.Start > cursor)
                {
                    keeps.Add((cursor, cut.Start));
                }
                cursor = Math.Max(cursor, cut.End);
            }
            if (cursor < totalLength)
            {
                keeps.Add((cursor, totalLength));
            }
            return keeps;
        }

        /// <summary>Emits the [start, end] stretch of the polyline as one WallSegment
        /// per straight sub-segment, skipping slivers under <see cref="MinSegmentFt"/>.</summary>
        private static void EmitRun(List<(double X, double Y)> pts, double[] stations,
            double start, double end, double thicknessFt, double heightFt,
            List<WadGen.WallSegment> output)
        {
            const double eps = 1e-6;
            if (end - start < eps)
            {
                return;
            }

            var run = new List<(double X, double Y)>();
            run.Add(PointAtStation(pts, stations, start));
            for (int i = 0; i < pts.Count; i++)
            {
                if (stations[i] > start + eps && stations[i] < end - eps)
                {
                    run.Add(pts[i]);
                }
            }
            run.Add(PointAtStation(pts, stations, end));

            // Accumulate from an anchor rather than testing chords independently:
            // Curve.Tessellate() on a small-radius arc yields uniformly sub-0.25 ft
            // chords, and dropping each one on its own would silently erase the
            // whole wall (or leave micro-gaps mid-run). Coalescing until the span
            // from the anchor reaches the threshold slightly straightens tight arcs,
            // which is invisible at Doom scale (0.25 ft = 4 map units).
            int anchor = 0;
            int lastEmitted = -1;
            for (int i = 1; i < run.Count; i++)
            {
                double dx = run[i].X - run[anchor].X;
                double dy = run[i].Y - run[anchor].Y;
                if (Math.Sqrt(dx * dx + dy * dy) < MinSegmentFt)
                {
                    if (i == run.Count - 1 && lastEmitted >= 0)
                    {
                        // Fold a sub-threshold tail into the previous segment so
                        // the run still ends exactly at the keep-interval boundary.
                        output[lastEmitted].X2 = run[i].X;
                        output[lastEmitted].Y2 = run[i].Y;
                    }
                    continue;
                }
                output.Add(new WadGen.WallSegment
                {
                    X1 = run[anchor].X,
                    Y1 = run[anchor].Y,
                    X2 = run[i].X,
                    Y2 = run[i].Y,
                    ThicknessFt = thicknessFt,
                    HeightFt = heightFt,
                });
                lastEmitted = output.Count - 1;
                anchor = i;
            }
        }

        private static double[] CumulativeStations(List<(double X, double Y)> pts)
        {
            var stations = new double[pts.Count];
            for (int i = 1; i < pts.Count; i++)
            {
                double dx = pts[i].X - pts[i - 1].X;
                double dy = pts[i].Y - pts[i - 1].Y;
                stations[i] = stations[i - 1] + Math.Sqrt(dx * dx + dy * dy);
            }
            return stations;
        }

        /// <summary>Arc-length station of the closest point on the polyline to (px, py) -
        /// how a door's insertion point (which sits on the wall face plane, not exactly
        /// on the centerline) is mapped back onto the centerline.</summary>
        private static double StationOfClosestPoint(List<(double X, double Y)> pts,
            double[] stations, double px, double py)
        {
            double bestDistSq = double.MaxValue;
            double bestStation = 0.0;
            for (int i = 0; i < pts.Count - 1; i++)
            {
                double ax = pts[i].X;
                double ay = pts[i].Y;
                double dx = pts[i + 1].X - ax;
                double dy = pts[i + 1].Y - ay;
                double lenSq = dx * dx + dy * dy;
                double t = 0.0;
                if (lenSq > 1e-12)
                {
                    t = ((px - ax) * dx + (py - ay) * dy) / lenSq;
                    if (t < 0.0)
                    {
                        t = 0.0;
                    }
                    else if (t > 1.0)
                    {
                        t = 1.0;
                    }
                }
                double cx = ax + t * dx;
                double cy = ay + t * dy;
                double distSq = (px - cx) * (px - cx) + (py - cy) * (py - cy);
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    bestStation = stations[i] + t * Math.Sqrt(lenSq);
                }
            }
            return bestStation;
        }

        private static (double X, double Y) PointAtStation(List<(double X, double Y)> pts,
            double[] stations, double station)
        {
            if (station <= 0.0)
            {
                return pts[0];
            }
            int last = pts.Count - 1;
            if (station >= stations[last])
            {
                return pts[last];
            }
            for (int i = 0; i < last; i++)
            {
                if (station <= stations[i + 1])
                {
                    double segLen = stations[i + 1] - stations[i];
                    double t = segLen > 1e-12 ? (station - stations[i]) / segLen : 0.0;
                    return (pts[i].X + t * (pts[i + 1].X - pts[i].X),
                            pts[i].Y + t * (pts[i + 1].Y - pts[i].Y));
                }
            }
            return pts[last];
        }

        /// <summary>Main-model elements report a null DesignOption; anything else is
        /// visible to the user only if its option is the set's primary one.</summary>
        private static bool IsMainModelOrPrimary(Element element)
        {
            DesignOption option = element.DesignOption;
            return option == null || option.IsPrimary;
        }

        private static List<WadGen.RoomPoint> CollectRoomPoints(Document doc)
        {
            // Rooms are only placement hints, so any failure here (rooms are a
            // frequent source of API surprises in linked/partially-loaded models)
            // just means the WAD gets its things placed without them.
            var rooms = new List<WadGen.RoomPoint>();
            try
            {
                // Room can't be used with OfClass directly (ElementClassFilter
                // rejects it) - collect the SpatialElement base class and filter.
                IList<Element> spatial = new FilteredElementCollector(doc)
                    .OfClass(typeof(SpatialElement))
                    .ToElements();
                foreach (Element element in spatial)
                {
                    Room room = element as Room;
                    if (room == null || room.Area <= 0.0 || !IsMainModelOrPrimary(room))
                    {
                        continue; // unplaced/unbounded rooms have no meaningful point
                    }
                    LocationPoint location = room.Location as LocationPoint;
                    XYZ point = location != null ? location.Point : null;
                    if (point == null)
                    {
                        continue;
                    }
                    rooms.Add(new WadGen.RoomPoint { X = point.X, Y = point.Y });
                }
            }
            catch
            {
                rooms.Clear();
            }
            return rooms;
        }
    }
}
