using UnityEngine;

// Recharges the flashlight through FlashlightController.AddBattery. Left in place when the battery is already full.
public class BatteryPickup : Pickup
{
    [Tooltip("Used when no definition is assigned.")]
    public float amount = 35f;

    protected override string DefaultPrompt => "[E] Take battery";

    protected override bool Grant(out string message)
    {
        return PickupRewards.GiveBattery(definition != null ? definition.reward.batteryAmount : amount, true, out message);
    }
}
