using System.Collections.Generic;

namespace DoomInDynamo.WadGen
{
    /// <summary>
    /// One straight run of wall centerline. The Revit extractor tessellates curved
    /// walls into several of these and cuts door openings out beforehand, so by the
    /// time WadGen sees a WallSegment it is always "solid wall for its whole length".
    /// Coordinates are Revit internal units (feet), in world coordinates.
    /// </summary>
    internal sealed class WallSegment
    {
        public double X1;
        public double Y1;
        public double X2;
        public double Y2;
        public double ThicknessFt;
        public double HeightFt;
    }

    /// <summary>A point known to lie inside a room - used only as a placement hint
    /// (the player start prefers to spawn near one), in feet.</summary>
    internal sealed class RoomPoint
    {
        public double X;
        public double Y;
    }

    /// <summary>
    /// Everything WadGen needs to build a map, extracted from Revit (or synthesized
    /// by the smoke test). Deliberately free of any Revit API types so the whole
    /// geometry/binary pipeline compiles and runs without Revit present.
    /// </summary>
    internal sealed class BuildingModel
    {
        public List<WallSegment> Walls = new List<WallSegment>();
        public List<RoomPoint> Rooms = new List<RoomPoint>();
        public string LevelName = "";
        public string DocumentTitle = "";
    }
}
