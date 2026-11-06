using DuckovSharedBike;
using DuckovSharedBike.Domain;

internal static class RuntimeSettingsTests
{
    internal static void Register(System.Action<string, System.Action> add)
    {
        add("runtime settings default to a 20 second save, right panel and F5-F10",()=>
        {
            var settings=StationSettingsFile.Parse(null);
            Equal(20,settings.Persistence.FullSaveSeconds);
            Equal("right",settings.Ui.InvoicePanelSide);
            True(settings.Ui.ShowWorldLabels);
            True(settings.Ui.Sounds);
            Equal("F6",settings.Keys.Station); Equal("F",settings.Keys.Mount); Equal("F8",settings.Keys.Loss);
            Equal("F9",settings.Keys.Diagnostics); Equal("F10",settings.Keys.Snapshot);
            Equal("F5",settings.Keys.Summon);
            Equal(100L,settings.Summon.Cost);
            Equal(3f,settings.Summon.Seconds);
        });
        add("runtime settings accept explicit overrides",()=>
        {
            var settings=StationSettingsFile.Parse("{\"Persistence\":{\"FullSaveSeconds\":45},\"Ui\":{\"InvoicePanelSide\":\" LEFT \",\"ShowWorldLabels\":false,\"Sounds\":false},\"Keys\":{\"Station\":\"E\",\"Mount\":\"Q\",\"Loss\":\"R\",\"Diagnostics\":\"F9\",\"Snapshot\":\"F10\",\"Summon\":\"G\"},\"Summon\":{\"Cost\":250,\"Seconds\":8}}");
            Equal(45,settings.Persistence.FullSaveSeconds);
            Equal(" LEFT ",settings.Ui.InvoicePanelSide);
            False(settings.Ui.ShowWorldLabels);
            False(settings.Ui.Sounds);
            Equal("E",settings.Keys.Station);
            Equal("G",settings.Keys.Summon);
            Equal(250L,settings.Summon.Cost);
            Equal(8f,settings.Summon.Seconds);
        });
        add("unsafe save cadence and unknown panel side are rejected",()=>
        {
            Reject(()=>StationSettingsFile.Parse("{\"Persistence\":{\"FullSaveSeconds\":4}}"));
            Reject(()=>StationSettingsFile.Parse("{\"Persistence\":{\"FullSaveSeconds\":301}}"));
            Reject(()=>StationSettingsFile.Parse("{\"Ui\":{\"InvoicePanelSide\":\"middle\"}}"));
            Reject(()=>StationSettingsFile.Parse("{\"Persistence\":null}"));
            Reject(()=>StationSettingsFile.Parse("{\"Ui\":null}"));
            Reject(()=>StationSettingsFile.Parse("{\"Keys\":null}"));
        });
        add("duplicate or blank key bindings are rejected",()=>
        {
            Reject(()=>StationSettingsFile.Parse("{\"Keys\":{\"Station\":\"F6\",\"Mount\":\"F6\",\"Loss\":\"F8\",\"Diagnostics\":\"F9\",\"Snapshot\":\"F10\"}}"));
            Reject(()=>StationSettingsFile.Parse("{\"Keys\":{\"Station\":\" \",\"Mount\":\"F7\",\"Loss\":\"F8\",\"Diagnostics\":\"F9\",\"Snapshot\":\"F10\"}}"));
        });
        add("duplicate keys are compared case-insensitively",()=>
            Reject(()=>StationSettingsFile.Parse("{\"Keys\":{\"Station\":\"f6\",\"Mount\":\"F6\",\"Loss\":\"F8\",\"Diagnostics\":\"F9\",\"Snapshot\":\"F10\"}}")));
        add("summon bounds are enforced",()=>
        {
            Reject(()=>StationSettingsFile.Parse("{\"Summon\":{\"Cost\":-1}}"));
            Reject(()=>StationSettingsFile.Parse("{\"Summon\":{\"Seconds\":0.5}}"));
            Reject(()=>StationSettingsFile.Parse("{\"Summon\":{\"Seconds\":121}}"));
            Reject(()=>StationSettingsFile.Parse("{\"Summon\":null}"));
        });
    }

    private static void Equal<T>(T expected,T actual)
    {
        if (!System.Collections.Generic.EqualityComparer<T>.Default.Equals(expected,actual))
            throw new System.Exception($"Expected {expected}, got {actual}");
    }
    private static void True(bool value) { if (!value) throw new System.Exception("Expected true"); }
    private static void False(bool value) { if (value) throw new System.Exception("Expected false"); }
    private static void Reject(System.Action action)
    {
        try { action(); } catch (System.InvalidOperationException) { return; }
        catch (System.ArgumentException) { return; }
        throw new System.Exception("Expected settings validation to fail.");
    }
}
