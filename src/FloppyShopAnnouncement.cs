using HarmonyLib;

namespace Horse.Floppy;

// Floppy's global startup replaces the stock "a shop is nearby" voice line.
// The native RPC and shop spawn still run, including the late-join path.
[HarmonyPatch(typeof(OuroObjective), "SpawnShop_ClientRpc")]
internal static class FloppyShopAnnouncement
{
    private static void Prefix(ref bool playVoiceline)
    {
        if (FloppyPlugin.Instance != null && ModelAssets.Ready &&
            FloppyAvailability.IsSynchronized && FloppyAvailability.Enabled)
            playVoiceline = false;
    }
}
