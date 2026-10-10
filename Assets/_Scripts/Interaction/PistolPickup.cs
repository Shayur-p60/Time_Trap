// The pistol on the Armory centre table. Works without a definition.
public class PistolPickup : Pickup
{
    protected override string DefaultPrompt => "[E] Take pistol";

    protected override bool Grant(out string message)
    {
        return PickupRewards.GivePistol(out message);
    }
}
