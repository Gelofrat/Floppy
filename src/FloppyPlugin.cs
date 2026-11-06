using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace Horse.Floppy;

// Exchanges change inventory and progression. This plugin is not client-side.
[BepInPlugin(Guid, "Floppy", "0.4.1")]
[MycoMod(null, ModFlags.IsSandbox)]
public sealed class FloppyPlugin : BaseUnityPlugin
{
    public const string Guid = "horse.mycopunk.floppy";
    internal static FloppyPlugin Instance;
    internal static ManualLogSource Log;
    internal ConfigEntry<bool> Enabled;
    internal ConfigEntry<float> Volume;
    private Harmony harmony;

    private void Awake()
    {
        Instance = this;
        Log = Logger;
        Enabled = Config.Bind("General", "Enabled", true, "Replace Ouroboros shops with Floppy.");
        Volume = Config.Bind("Audio", "Volume", 0.6f,
            new ConfigDescription("Spatial retro PC sound volume.", new AcceptableValueRange<float>(0f, 1f)));
        try
        {
            ModelAssets.Load();
            PcAudio.Load();
            harmony = new Harmony(Guid);
            harmony.PatchAll(typeof(FloppyPlugin).Assembly);
            Logger.LogInfo("Floppy loaded. Three exchanges per player per shop appearance.");
        }
        catch (Exception error)
        {
            harmony?.UnpatchSelf();
            Logger.LogError("Floppy could not load; vanilla shops remain available: " + error);
        }
    }

    private void OnDestroy()
    {
        FloppyMissionToggle.DisposeAll();
        FloppyAvailability.Dispose();
        harmony?.UnpatchSelf();
        FloppyInventorySelection.Cancel();
        foreach (var station in UnityEngine.Object.FindObjectsByType<FloppyStation>(FindObjectsInactive.Include))
        {
            station.gameObject.SetActive(false);
            station.RestoreVanilla();
            Destroy(station.gameObject);
        }
        FloppyDeferredShop.DisposeAll();
        ModelAssets.Dispose();
        PcAudio.Dispose();
        Instance = null;
    }

    private void Update() => FloppyAvailability.Tick(Time.unscaledTime);
}

[HarmonyPatch(typeof(OuroStoreRoom), nameof(OuroStoreRoom.Setup))]
internal static class ReplaceShop
{
    private static bool Prefix(OuroStoreRoom __instance, OuroObjective objective,
        OuroItemPositions itemPositions, OuroRoom room, int seed)
    {
        if (FloppyPlugin.Instance == null || !ModelAssets.Ready)
            return true;
        FloppyStation station = __instance.GetComponentInChildren<FloppyStation>(true);
        var deferred = __instance.GetComponent<FloppyDeferredShop>();
        if (!FloppyAvailability.IsSynchronized)
        {
            if (station != null) station.gameObject.SetActive(false);
            if (deferred == null) deferred = __instance.gameObject.AddComponent<FloppyDeferredShop>();
            deferred.Defer(__instance, objective, itemPositions, room, seed);
            return false;
        }
        deferred?.Clear();
        if (!FloppyAvailability.Enabled)
        {
            if (station != null)
            {
                station.gameObject.SetActive(false);
                station.RestoreVanilla();
            }
            return true;
        }
        try
        {
            if (station == null)
            {
                var model = ModelAssets.Instantiate(__instance.transform);
                station = model.AddComponent<FloppyStation>();
            }
            station.Setup(__instance, objective, itemPositions, room, seed);
            return false;
        }
        catch (Exception error)
        {
            FloppyPlugin.Log.LogError("Floppy setup failed; restoring vanilla shop: " + error);
            if (station != null)
            {
                station.RestoreVanilla();
                UnityEngine.Object.Destroy(station.gameObject);
            }
            return true;
        }
    }
}
