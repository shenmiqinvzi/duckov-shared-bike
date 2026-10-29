using System;
using System.Collections.Generic;

namespace DuckovSharedBike.Domain
{
    public static class WorldValidation
    {
        public static void Validate(WorldState state, string saveId)
        {
            if (state == null || state.SchemaVersion != 1 || state.SaveId != saveId)
                throw new InvalidOperationException("World state belongs to another save or schema.");
            ValidateStations(state.Stations);
            var vehicle = state.Vehicle;
            if (vehicle != null && (string.IsNullOrWhiteSpace(vehicle.VehicleId)
                || string.IsNullOrWhiteSpace(vehicle.MapId) || string.IsNullOrWhiteSpace(vehicle.SceneName)
                || vehicle.Position == null || !vehicle.Position.IsFinite
                || float.IsNaN(vehicle.Yaw) || float.IsInfinity(vehicle.Yaw)))
                throw new InvalidOperationException("Invalid vehicle placement data.");
        }

        public static void ValidateStations(IReadOnlyList<StationRecord> stations)
        {
            if (stations == null) throw new InvalidOperationException("Station list is missing.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var station in stations)
                if (station == null || string.IsNullOrWhiteSpace(station.Id) || !ids.Add(station.Id)
                    || string.IsNullOrWhiteSpace(station.MapId) || string.IsNullOrWhiteSpace(station.SceneName)
                    || station.Position == null || !station.Position.IsFinite || float.IsNaN(station.Yaw) || float.IsInfinity(station.Yaw))
                    throw new InvalidOperationException("Invalid or duplicate station placement data.");
        }
    }
}
