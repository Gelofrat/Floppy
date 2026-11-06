using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Pigeon.Movement;

namespace Horse.Floppy;

internal static class FloppyExchangeDiagnostics
{
    private static readonly FieldInfo GridStealerField = AccessTools.Field(typeof(OuroObjective), "gridStealerUpgrade");

    internal static void LogState(string phase, UpgradeInstance input, UpgradeInstance output)
    {
        // Read native grid sizes, including their modifiers. Diagnostics must
        // never interrupt an exchange or alter the game's grid rules.
        try
        {
            var player = Player.LocalPlayer;
            if (player == null) return;
            var objective = MissionManager.Instance != null ? MissionManager.Instance.GetObjective<OuroObjective>() : null;
            var gridStealer = objective != null ? GridStealerField?.GetValue(objective) as Upgrade : null;
            var seen = new HashSet<int>();
            var grids = new StringBuilder();
            Add(player.Character);
            if (player.Gear != null)
                foreach (var gear in player.Gear)
                    if (gear != null) Add(gear.Prefab);
            FloppyPlugin.Log.LogInfo("Exchange " + phase + ": input=" + Describe(input) +
                "; reward=" + Describe(output) + "; autoApply=" + OuroObjective.AutoApplyUpgrades +
                "; grids=[" + grids + "]");

            void Add(IUpgradable gear)
            {
                if (gear == null || !gear.Info.HasUpgradeGrid || !seen.Add(gear.Info.ID)) return;
                gear.Info.GetUpgradeGridSize(out int width, out int height);
                int stealers = 0;
                if (gridStealer != null)
                {
                    var equipped = new PlayerData.EquippedUpgradeEnumerator(gear);
                    while (equipped.MoveNext())
                        if (equipped.Upgrade != null && equipped.Upgrade.Upgrade == gridStealer) stealers++;
                }
                if (grids.Length > 0) grids.Append("; ");
                grids.Append(gear.Info.APIName).Append('#').Append(gear.Info.ID).Append('=')
                    .Append(width).Append('x').Append(height).Append("/gridStealers=").Append(stealers);
            }
        }
        catch (Exception error)
        {
            FloppyPlugin.Log.LogDebug("Exchange grid diagnostics unavailable: " + error.Message);
        }
    }

    private static string Describe(UpgradeInstance module)
        => module?.Upgrade == null ? "none" : module.InstanceID + "/" + module.Upgrade.NumberID +
            "/" + module.Upgrade.GetInstanceName(module) + "/gear=" + module.Gear?.Info.ID;
}
