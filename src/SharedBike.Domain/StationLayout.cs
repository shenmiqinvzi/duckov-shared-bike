using System;
using System.Collections.Generic;
using System.Linq;

namespace DuckovSharedBike.Domain
{
    public sealed class StationLayoutSettings
    {
        public bool Enabled { get; set; } = true;
        public bool AutomaticTestStations { get; set; } = true;
        public List<string> TargetDisplayKeys { get; set; } = new List<string>
            { "Level_GroundZero", "HiddenWarehouse", "Level_Farm", "Level_Farm_South" };
        public int StationsPerScene { get; set; } = 2;
        public float MinimumStationSeparation { get; set; } = 6;
        public float PlacementRetrySeconds { get; set; } = 10;
        public List<StationRecord> FixedStations { get; set; } = new List<StationRecord>();
        public SummonSettings Summon { get; set; } = new SummonSettings();
        public RidingPresentationSettings Riding { get; set; } = new RidingPresentationSettings();
        public PersistenceSettings Persistence { get; set; } = new PersistenceSettings();
        public UiSettings Ui { get; set; } = new UiSettings();
        public KeyBindingSettings Keys { get; set; } = new KeyBindingSettings();

        public void Validate()
        {
            if (TargetDisplayKeys == null || TargetDisplayKeys.Any(string.IsNullOrWhiteSpace)
                || TargetDisplayKeys.Distinct(StringComparer.Ordinal).Count() != TargetDisplayKeys.Count
                || StationsPerScene < 2 || StationsPerScene > 4
                || !Finite(MinimumStationSeparation) || MinimumStationSeparation < 4 || MinimumStationSeparation > 30
                || !Finite(PlacementRetrySeconds) || PlacementRetrySeconds < 2 || PlacementRetrySeconds > 120)
                throw new InvalidOperationException("Invalid station layout settings.");
            WorldValidation.ValidateStations(FixedStations);
            if (Summon==null) throw new InvalidOperationException("Missing summon settings.");
            Summon.Validate();
            if (Riding==null) throw new InvalidOperationException("Missing riding settings.");
            Riding.Validate();
            if (Persistence==null || Ui==null || Keys==null) throw new InvalidOperationException("Missing runtime settings.");
            Persistence.Validate(); Ui.Validate(); Keys.Validate();
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    public sealed class SummonSettings
    {
        public long Cost { get; set; } = 100;
        public float Seconds { get; set; } = 3;

        public void Validate()
        {
            if (Cost < 0) throw new InvalidOperationException("Summon cost cannot be negative.");
            if (!float.IsFinite(Seconds) || Seconds < 1 || Seconds > 120)
                throw new InvalidOperationException("Summon seconds must be between 1 and 120.");
        }
    }

    public static class StationRouting
    {
        // Never infer an underground scene's eligibility from its outdoor parent map.
        public static string? TargetKey(StationLayoutSettings settings, string mainKey, string sceneKey, bool isMainScene)
        {
            if (!settings.Enabled) return null;
            if (settings.TargetDisplayKeys.Contains(sceneKey, StringComparer.Ordinal)) return sceneKey;
            return isMainScene && settings.TargetDisplayKeys.Contains(mainKey, StringComparer.Ordinal) ? mainKey : null;
        }

        public static string DisplayName(string key)
        {
            switch (key)
            {
                case "Level_GroundZero": return "零号区";
                case "HiddenWarehouse": return "仓库区";
                case "Level_Farm": return "农场镇";
                case "Level_Farm_South": return "农场镇南部";
                default: return "地图";
            }
        }
    }
}
