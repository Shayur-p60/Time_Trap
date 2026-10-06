using UnityEngine;

// Adds rounds to the reserve. Left in place when the reserve is already full.
public class AmmoPickup : Pickup
{
    [Tooltip("Used when no definition is assigned.")]
    public int amount = 8;

    protected override string DefaultPrompt => "[E] Take ammo";

    protected override bool Grant(out string message)
    {
        return PickupRewards.GiveAmmo(definition != null ? definition.reward.ammoAmount : amount, true, out message);
    }
}
