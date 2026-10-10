using UnityEngine;

// A power-up lying in the world: used the moment it is picked up.
public class PowerUpPickup : Pickup
{
    [Tooltip("Used when no definition is assigned.")]
    public PowerUpType type;

    protected override string DefaultPrompt => "[E] Use power-up";

    protected override bool Grant(out string message)
    {
        return PickupRewards.GivePowerUp(definition != null ? definition.reward.powerUp : type, out message);
    }
}
