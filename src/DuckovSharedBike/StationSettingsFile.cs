using System;
using System.IO;
using DuckovSharedBike.Domain;
using Newtonsoft.Json;

namespace DuckovSharedBike
{
    internal static class StationSettingsFile
    {
        internal static StationLayoutSettings Read(string path)
            => Parse(File.Exists(path) ? File.ReadAllText(path) : null);
        internal static StationBlueprints ReadBlueprints(string path)
        {
            var data=JsonConvert.DeserializeObject<StationBlueprints>(File.ReadAllText(path),new JsonSerializerSettings
                { TypeNameHandling=TypeNameHandling.None,ObjectCreationHandling=ObjectCreationHandling.Replace,MaxDepth=32 })
                ?? throw new InvalidOperationException("Missing station blueprints.");
            data.Validate(); return data;
        }

        internal static StationLayoutSettings Parse(string? json)
        {
            var settings=json==null ? new StationLayoutSettings()
                : JsonConvert.DeserializeObject<StationLayoutSettings>(json,new JsonSerializerSettings
                {
                    TypeNameHandling=TypeNameHandling.None,
                    ObjectCreationHandling=ObjectCreationHandling.Replace,
                    MaxDepth=32
                }) ?? throw new InvalidOperationException("Empty station settings.");
            settings.Validate();
            return settings;
        }
    }
}
