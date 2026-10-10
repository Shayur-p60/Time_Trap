using System.Collections.Generic;
using UnityEngine;

// Opens once and gives one random reward from its definition's chest table: ammo, a battery or a power-up.
// Entries marked "requires power" (the Compass) are only in the draw once the power is on.
// The chest stays in the world after it has been opened.
public class Chest : Pickup
{
    [Tooltip("Seed for the reward draw. 0 = different every time. The spawner sets this so a fixed-seed layout also has fixed contents.")]
    public int rewardSeed;

    // The definition whose reward the chest gave (null until opened).
    public PickupDefinition LastReward { get; private set; }

    protected override string DefaultPrompt => "[E] Open chest";

    public override string PromptText => collected ? "Empty" : base.PromptText;

    // The reward the chest would give right now, without opening it.
    public PickupDefinition RollReward()
    {
        if (definition == null || definition.reward.chestRewards == null) return null;

        var options = new List<ChestRewardEntry>();
        float total = 0f;
        foreach (var entry in definition.reward.chestRewards)
        {
            if (entry == null || entry.definition == null || entry.weight <= 0f) continue;
            if (entry.definition.reward.kind == PickupKind.Chest) continue;
            if (entry.requiresPower && !PowerState.IsPowered) continue;
            options.Add(entry);
            total += entry.weight;
        }
        if (options.Count == 0) return null;

        double roll = (rewardSeed != 0 ? new System.Random(rewardSeed).NextDouble() : Random.value) * total;
        foreach (var entry in options)
        {
            roll -= entry.weight;
            if (roll < 0.0) return entry.definition;
        }
        return options[options.Count - 1].definition;
    }

    protected override bool Grant(out string message)
    {
        LastReward = RollReward();
        if (LastReward == null)
        {
            message = "The chest is empty";
            return true;
        }
        PickupRewards.Grant(LastReward.reward, false, out string detail);
        message = "Chest: " + LastReward.displayName + (string.IsNullOrEmpty(detail) || detail == LastReward.displayName ? "" : "  (" + detail + ")");
        return true;
    }

    protected override void OnCollected()
    {
        // stays where it is, but goes dark so an opened chest is easy to tell from a closed one
        Transform glow = transform.Find("Glow");
        if (glow != null) glow.gameObject.SetActive(false);
    }
}
