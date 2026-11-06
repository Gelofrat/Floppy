using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Pigeon.Movement;
using UnityEngine;

namespace Horse.Floppy;

internal static class ExchangeRules
{
    private static readonly FieldInfo UpgradeBufferField = AccessTools.Field(typeof(PlayerData), "upgradeBuffer");
    private static readonly FieldInfo UpgradeDupeBufferField = AccessTools.Field(typeof(PlayerData), "upgradeDupeBuffer");

    private sealed class EquippedSlot
    {
        internal IUpgradable Gear;
        internal sbyte X, Y;
        internal byte Rotation;
    }
    internal static bool CanSubmit(UpgradeInstance input, out string reason)
    {
        reason = null;
        if (input?.Upgrade == null || !input.IsCollected || input.Upgrade.IsSkin())
            reason = "Select a module from your inventory.";
        else if (!input.RemoveAfterMission)
            reason = "Only temporary Ouroboros modules are accepted.";
        return reason == null;
    }

    internal static List<ResourceCost> Cost(UpgradeInstance input)
        // Use the shop's Contraband Scrap price. Snapshot its reusable buffer so
        // reward callbacks cannot change the amount refunded after a failure.
        => new List<ResourceCost>(input.Upgrade.GetOuroborosCost());

    internal static UpgradeInstance Prepare(UpgradeInstance input, int seed)
    {
        if (!CanSubmit(input, out _) || input.Gear == null) return null;
        bool characterPool = input.Gear.GearType == GearType.Character;
        var filter = new PlayerData.UpgradeFilterParams(input.Upgrade.Rarity, characterPool ? null : input.Gear,
            PlayerData.DuplicateMode.AllowDuplicates, allowGearWeighting: false)
        {
            isTempMissionUpgrade = true
        };
        // Let the game apply its current Ouroboros eligibility rules. With no
        // explicit gear, its temporary pool includes the current character and
        // shared Player gear, together with the equipped weapons.
        PlayerData.FindRandomUpgrade(filter, false, seed);
        var available = UpgradeBufferField?.GetValue(null) as List<PlayerData.FilterResult>;
        var duplicates = UpgradeDupeBufferField?.GetValue(null) as List<PlayerData.FilterResult>;
        if (available == null || duplicates == null)
        {
            FloppyPlugin.Log.LogError("Floppy could not read the game's eligible module pool.");
            return null;
        }
        // Read both native buffers without changing them. Non-stackable owned
        // modules are in the duplicate buffer and remain valid exchange rewards.
        var pool = new List<PlayerData.FilterResult>();
        var seen = new HashSet<Tuple<int, Upgrade>>();
        Add(available);
        Add(duplicates);
        if (pool.Count == 0) return null;
        var found = pool[new System.Random(seed).Next(pool.Count)];
        var result = PlayerData.CreateUpgradeInstance(found.gear, found.info.Upgrade, rent: false);
        result.RemoveAfterMission = true;
        return result;

        void Add(List<PlayerData.FilterResult> source)
        {
            foreach (var candidate in source)
            {
                var upgrade = candidate.info?.Upgrade;
                if (candidate.gear == null || upgrade == null || upgrade == input.Upgrade
                    || upgrade.Rarity != input.Upgrade.Rarity || upgrade.IsSkin()
                    || !IsAllowedGear(input.Gear, candidate.gear)) continue;
                if (seen.Add(Tuple.Create(candidate.gear.Info.ID, upgrade))) pool.Add(candidate);
            }
        }
    }

    private static bool SameGear(IUpgradable first, IUpgradable second)
        => first != null && second != null && first.Info.ID == second.Info.ID;

    private static bool IsAllowedGear(IUpgradable input, IUpgradable output)
    {
        if (input == null || output == null) return false;
        if (input.GearType != GearType.Character) return SameGear(input, output);
        // Global is the shared Player module category. Other characters never
        // enter this union, even if the native finder falls back to other gear.
        var character = Player.LocalPlayer != null ? Player.LocalPlayer.Character : null;
        return SameGear(output, Global.Instance) || SameGear(output, character);
    }

    internal static bool Commit(UpgradeInstance input, UpgradeInstance output, out string reason)
    {
        if (!CanSubmit(input, out reason)) return false;
        if (output?.Upgrade == null || output.Gear == null || output.IsCollected || !output.RemoveAfterMission
            || !IsAllowedGear(input.Gear, output.Gear) || output.Upgrade == input.Upgrade
            || output.Upgrade.IsSkin() || output.Upgrade.Rarity != input.Upgrade.Rarity)
        {
            reason = "No eligible module of the same rarity could be selected.";
            return false;
        }
        var price = Cost(input);
        var outputInfo = PlayerData.GetUpgradeInfo(output.Gear, output.Upgrade);
        int outputTotalCollected = outputInfo.TotalInstancesCollected;
        var equipped = new List<EquippedSlot>();
        foreach (var data in PlayerData.Instance.collectedGear.Values)
            if (input.GetPosition(data.Gear, out sbyte x, out sbyte y, out byte rotation))
                equipped.Add(new EquippedSlot { Gear = data.Gear, X = x, Y = y, Rotation = rotation });
        if (!PlayerData.Instance.TryRemoveResources(price))
        {
            reason = "Not enough currency for this exchange.";
            return false;
        }
        bool inputRemoved = false;
        try
        {
            input.UnequipFromAll();
            // Collection can immediately reroll the entire inventory (Fake ID)
            // or change character. Consume the submitted instance first so it
            // cannot participate in those native reward effects.
            inputRemoved = input.Destroy();
            if (!inputRemoved) throw new InvalidOperationException("Input not removed");
            PlayerData.CollectInstance(output);
            if (!output.IsCollected) throw new InvalidOperationException("Reward not collected");
            Reapply();
            return true;
        }
        catch (Exception error)
        {
            // Native AddInstance counts temporary Contraband modules before
            // notifying listeners. Failed collection must not leave that count.
            outputInfo.SetTotalInstancesCollected(outputTotalCollected);
            // Destroy() can remove membership before an event subscriber throws.
            if (!input.IsCollected)
                PlayerData.GetUpgradeInfo(input.Gear, input.Upgrade).Instances.Add(input);
            if (output.IsCollected)
            {
                output.UnequipFromAll();
                PlayerData.GetUpgradeInfo(output.Gear, output.Upgrade).Instances.Remove(output);
            }
            foreach (var slot in equipped) input.Equip(slot.Gear, slot.X, slot.Y, slot.Rotation);
            foreach (var cost in price) PlayerData.Instance.AddResource(cost.resource, cost.count, invokeMissionUpdate: false);
            Reapply();
            FloppyPlugin.Log.LogError("Exchange rolled back: " + error);
            reason = "Exchange cancelled. Your module and payment were returned.";
            return false;
        }
    }
    private static void Reapply()
    {
        try { GearUtil.ReapplyUpgrades(); }
        catch (Exception error) { FloppyPlugin.Log.LogError("Could not refresh equipment after exchange: " + error); }
    }
}
