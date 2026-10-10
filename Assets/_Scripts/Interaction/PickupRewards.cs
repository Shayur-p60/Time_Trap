using UnityEngine;

// Hands rewards to the player. Shared by the pickup classes and by Chest (which gives another definition's reward).
// allowRefuse: a pickup lying in the world is left alone when it would be wasted ("Ammo full"); a chest always pays out.
public static class PickupRewards
{
    static FlashlightController flashlight;

    static FlashlightController Flashlight
    {
        get
        {
            if (flashlight == null && PlayerInventory.Instance != null)
                flashlight = PlayerInventory.Instance.GetComponentInChildren<FlashlightController>(true);
            if (flashlight == null)
            {
                var player = GameObject.FindGameObjectWithTag("Player");
                if (player != null) flashlight = player.GetComponentInChildren<FlashlightController>(true);
            }
            return flashlight;
        }
    }

    public static bool Grant(PickupReward reward, bool allowRefuse, out string message)
    {
        switch (reward.kind)
        {
            case PickupKind.Ammo: return GiveAmmo(reward.ammoAmount, allowRefuse, out message);
            case PickupKind.Battery: return GiveBattery(reward.batteryAmount, allowRefuse, out message);
            case PickupKind.PowerUp: return GivePowerUp(reward.powerUp, out message);
            case PickupKind.Pistol: return GivePistol(out message);
            default:
                message = "";
                return false;
        }
    }

    public static bool GiveAmmo(int amount, bool allowRefuse, out string message)
    {
        var inventory = PlayerInventory.Instance;
        int added = inventory != null ? inventory.AddReserveAmmo(amount) : 0;
        if (added == 0 && (allowRefuse || inventory == null))
        {
            message = inventory != null ? "Ammo full" : "";
            return false;
        }
        message = added > 0 ? "+" + added + " ammo" : "Ammo full";
        return true;
    }

    public static bool GiveBattery(float amount, bool allowRefuse, out string message)
    {
        var light = Flashlight;
        if (light == null)
        {
            message = "";
            return false;
        }
        if (allowRefuse && light.GetBatteryPercent() >= 0.995f)
        {
            message = "Battery full";
            return false;
        }
        light.AddBattery(amount);
        message = "Flashlight battery +" + Mathf.RoundToInt(amount);
        return true;
    }

    public static bool GivePowerUp(PowerUpType type, out string message)
    {
        if (PlayerPowerUps.Instance == null)
        {
            message = "";
            return false;
        }
        PlayerPowerUps.Instance.Activate(type);
        message = PowerUps.DisplayName(type);
        return true;
    }

    public static bool GivePistol(out string message)
    {
        var inventory = PlayerInventory.Instance;
        if (inventory == null)
        {
            message = "";
            return false;
        }
        if (inventory.GivePistol())
        {
            message = "Pistol  (LMB fire, R reload)";
            return true;
        }
        // already armed: a second pistol is worth a magazine
        return GiveAmmo(PlayerInventory.MagazineSize, true, out message);
    }
}
