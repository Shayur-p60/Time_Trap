using System;
using UnityEngine;

public enum PickupKind { Ammo, Battery, PowerUp, Chest, Pistol }

// What collecting a pickup gives. Only the fields for the chosen kind are used.
[Serializable]
public class PickupReward
{
    public PickupKind kind;
    [Tooltip("Ammo: rounds added to the reserve.")]
    public int ammoAmount = 8;
    [Tooltip("Battery: charge passed to FlashlightController.AddBattery.")]
    public float batteryAmount = 35f;
    [Tooltip("PowerUp: which one.")]
    public PowerUpType powerUp;
    [Tooltip("Chest: what it can contain. One entry is drawn at random (by weight) when the chest is opened.")]
    public ChestRewardEntry[] chestRewards = new ChestRewardEntry[0];
}

[Serializable]
public class ChestRewardEntry
{
    [Tooltip("The chest gives this definition's reward.")]
    public PickupDefinition definition;
    [Min(0f)] public float weight = 1f;
    [Tooltip("Only possible once the power is on (the Compass).")]
    public bool requiresPower;
}

// Everything about one type of pickup that is not logic: its name, prompt, looks and reward numbers.
// The world object is always the same generic pickup prefab; it reads one of these. To change how a pickup looks,
// assign a different Visual Prefab (or an Icon) here. A new pickup type is a new asset in Assets/_Data/Pickups.
[CreateAssetMenu(menuName = "Time Trap/Pickup Definition", fileName = "PickupDefinition")]
public class PickupDefinition : ScriptableObject
{
    public string displayName = "Pickup";
    [Tooltip("Full on-screen prompt, for example \"[E] Take battery\".")]
    public string promptText = "[E] Pick up";

    [Header("Visuals")]
    [Tooltip("Spawned under the pickup as a child named 'Visual'.")]
    public GameObject visualPrefab;
    [Tooltip("Optional. When set, a floating icon that faces the camera is shown above the pickup.")]
    public Sprite icon;
    [Tooltip("Colour of the small glow light. Black = no glow.")]
    public Color glowColour = Color.white;
    [Tooltip("Slow spin and bob (on for power-ups, off for chests).")]
    public bool idleSpinBob;

    [Header("Reward")]
    public PickupReward reward = new PickupReward();

    // True if collecting this can give a battery (a battery itself, or a chest that may hold one).
    public bool CanYieldBattery => CanYield(PickupKind.Battery);
    // True if collecting this can give ammo (ammo itself, or a chest that may hold some).
    public bool CanYieldAmmo => CanYield(PickupKind.Ammo);

    bool CanYield(PickupKind kind)
    {
        if (reward.kind == kind) return true;
        if (reward.kind != PickupKind.Chest || reward.chestRewards == null) return false;
        foreach (var entry in reward.chestRewards)
            if (entry != null && entry.definition != null && entry.weight > 0f && !entry.requiresPower && entry.definition.reward.kind == kind)
                return true;
        return false;
    }
}
