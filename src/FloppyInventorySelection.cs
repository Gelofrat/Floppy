using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;

namespace Horse.Floppy;

// Same inventory path as CoinflipColin. Only our own selection callback
// bypasses CanStack; the coin and ordinary inventory keep vanilla behavior.
internal static class FloppyInventorySelection
{
    private static FloppyStation owner;
    private static Action<UpgradeInstance> selected;
    private static readonly Action<UpgradeInstance> receive = Receive;
    internal static bool Active => owner != null && OuroGearWindow.OpenAsUpgradeSelect
        && OuroGearWindow.OnUpgradeSelected == receive;
    internal static void Open(FloppyStation station, Action<UpgradeInstance> callback)
    {
        Cancel();
        // OuroMission.OnMenuOpened clears the static selection flags.
        GameManager.Instance.OpenMenu(instantOpen: true);
        owner = station;
        selected = callback;
        OuroGearWindow.OpenAsUpgradeSelect = true;
        OuroGearWindow.OnUpgradeSelected = receive;
    }
    private static void Receive(UpgradeInstance upgrade)
    {
        if (!Active) return;
        var callback = selected;
        Cancel();
        callback?.Invoke(upgrade);
    }
    internal static bool AllowNonStackable(UpgradeInstance upgrade) => Active || upgrade.CanStack;
    internal static void Cancel(FloppyStation station = null)
    {
        if (station != null && owner != station) return;
        if (OuroGearWindow.OnUpgradeSelected == receive)
        {
            OuroGearWindow.OnUpgradeSelected = null;
            OuroGearWindow.OpenAsUpgradeSelect = false;
        }
        owner = null;
        selected = null;
    }
}

[HarmonyPatch(typeof(OuroGearWindow), "AddUpgrade")]
internal static class FloppyInventoryStackFilter
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var getter = AccessTools.PropertyGetter(typeof(UpgradeInstance), nameof(UpgradeInstance.CanStack));
        var replacement = AccessTools.Method(typeof(FloppyInventorySelection), nameof(FloppyInventorySelection.AllowNonStackable));
        int replaced = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(getter))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = replacement;
                replaced++;
            }
            yield return instruction;
        }
        if (replaced != 1) throw new InvalidOperationException("OuroGearWindow stack filter changed; Floppy patch was not applied.");
    }
}

[HarmonyPatch(typeof(Menu), "OnClose")]
internal static class FloppyInventoryClose
{
    private static void Postfix() => FloppyInventorySelection.Cancel();
}
