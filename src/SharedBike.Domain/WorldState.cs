using System;
using System.Collections.Generic;

namespace DuckovSharedBike.Domain
{
    public sealed class WorldPoint
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public bool IsFinite => !(float.IsNaN(X) || float.IsNaN(Y) || float.IsNaN(Z)
            || float.IsInfinity(X) || float.IsInfinity(Y) || float.IsInfinity(Z));
        public WorldPoint Copy() => new WorldPoint { X = X, Y = Y, Z = Z };
    }
    public sealed class StationRecord
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string MapId { get; set; } = "";
        public string SceneName { get; set; } = "";
        public WorldPoint Position { get; set; } = new WorldPoint();
        public float Yaw { get; set; }
        public StationRecord Copy() => new StationRecord
            { Id = Id, Name = Name, MapId = MapId, SceneName = SceneName, Position = Position.Copy(),Yaw=Yaw };
    }
    public sealed class VehiclePose
    {
        public string VehicleId { get; set; } = "";
        public string MapId { get; set; } = "";
        public string SceneName { get; set; } = "";
        public WorldPoint Position { get; set; } = new WorldPoint();
        public float Yaw { get; set; }
    }
    public sealed class WorldState
    {
        public int SchemaVersion { get; set; } = 1;
        public string SaveId { get; set; } = "";
        public List<StationRecord> Stations { get; set; } = new List<StationRecord>();
        public VehiclePose? Vehicle { get; set; }
    }
    public static class SpatialRules
    {
        public static bool Within(WorldPoint a, WorldPoint b, float radius)
        {
            if (a == null || b == null || !a.IsFinite || !b.IsFinite || float.IsNaN(radius) || float.IsInfinity(radius) || radius <= 0) return false;
            var dx = (double)a.X - b.X; var dy = (double)a.Y - b.Y; var dz = (double)a.Z - b.Z;
            return dx * dx + dy * dy + dz * dz <= (double)radius * radius;
        }
        public static bool CanReturn(WorldPoint player, WorldPoint vehicle, WorldPoint station, float radius, bool clearPath)
            => clearPath && Within(player, station, radius) && Within(vehicle, station, radius);
    }
}
