using System;
using UnityEngine;

// Base class for things the player picks up with E. A pickup in the world is the generic pickup prefab plus a
// PickupDefinition: the definition supplies the prompt, the visual (spawned as a child named "Visual"), the optional
// floating icon, the glow colour and the reward numbers, so looks and logic stay separate.
public abstract class Pickup : Interactable
{
    public PickupDefinition definition;

    [Header("Glow light")]
    [Tooltip("0 = no glow light.")]
    public float glowIntensity = 1.5f;
    public float glowRange = 2.5f;
    [Tooltip("Height of the icon above the pickup (metres).")]
    public float iconHeight = 0.7f;

    protected bool collected;
    public bool Collected => collected;

    protected virtual string DefaultPrompt => "[E] Pick up";

    public override string PromptText
    {
        get
        {
            if (collected) return "";
            return definition != null && !string.IsNullOrEmpty(definition.promptText) ? definition.promptText : DefaultPrompt;
        }
    }

    public override bool CanInteract => !collected;

    public override void Interact()
    {
        if (collected) return;
        bool given = Grant(out string message);
        GameHUD.Message(message);
        if (!given) return;
        collected = true;
        PlayCollectSound();
        PickupHighlight.Collect(gameObject);   // the outline fades out by itself, even if this object is destroyed now
        OnCollected();
    }

    protected virtual void PlayCollectSound()
    {
        GameAudio.Pickup();
    }

    // Give the reward. Return false to leave the pickup where it is (for example "Ammo full").
    protected abstract bool Grant(out string message);

    protected virtual void OnCollected()
    {
        if (Application.isPlaying) Destroy(gameObject);
        else DestroyImmediate(gameObject);
    }

    protected virtual void Start()
    {
        BuildVisual();
    }

    // Spawns the definition's visual, icon and glow under this object. Safe to call more than once.
    public void BuildVisual()
    {
        if (definition == null) return;

        Transform visual = transform.Find("Visual");
        if (visual == null && definition.visualPrefab != null)
        {
            visual = Instantiate(definition.visualPrefab, transform).transform;
            visual.name = "Visual";
            visual.localPosition = Vector3.zero;
            visual.localRotation = Quaternion.identity;
        }

        if (definition.icon != null && transform.Find("Icon") == null)
        {
            var icon = new GameObject("Icon");
            icon.transform.SetParent(transform, false);
            icon.transform.localPosition = Vector3.up * iconHeight;
            icon.AddComponent<SpriteRenderer>().sprite = definition.icon;
            icon.AddComponent<PickupIcon>();
        }

        Color glow = definition.glowColour;
        if (glowIntensity > 0f && glow.maxColorComponent > 0.01f && transform.Find("Glow") == null)
        {
            var go = new GameObject("Glow");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.up * 0.35f;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = glow;
            light.intensity = glowIntensity;
            light.range = glowRange;
            light.shadows = LightShadows.None;
        }

        var idle = GetComponent<PickupIdle>();
        if (idle == null && definition.idleSpinBob && visual != null) idle = gameObject.AddComponent<PickupIdle>();
        if (idle != null)
        {
            idle.target = visual;
            idle.enabled = definition.idleSpinBob && visual != null;
        }

        // the outline (shaders part B); does nothing outside Play mode
        if (!collected) PickupHighlight.Ensure(gameObject);
    }

    // ------------------------------------------------------------------ creation by definition

    // The component that implements a definition's kind.
    public static Type ComponentFor(PickupKind kind)
    {
        switch (kind)
        {
            case PickupKind.Ammo: return typeof(AmmoPickup);
            case PickupKind.Battery: return typeof(BatteryPickup);
            case PickupKind.PowerUp: return typeof(PowerUpPickup);
            case PickupKind.Chest: return typeof(Chest);
            default: return typeof(PistolPickup);
        }
    }

    // Makes a pickup in the world from a definition: an instance of the generic pickup prefab (or a bare trigger sphere
    // when no prefab is given) with the component for the definition's kind and the definition's visuals.
    public static Pickup Create(PickupDefinition definition, GameObject genericPrefab, Vector3 position, Quaternion rotation, Transform parent)
    {
        if (definition == null) return null;

        GameObject go;
        if (genericPrefab != null)
        {
            go = Instantiate(genericPrefab, position, rotation, parent);
        }
        else
        {
            go = new GameObject();
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, rotation);
            var trigger = go.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = 0.45f;
            trigger.center = Vector3.up * 0.3f;
            int layer = LayerMask.NameToLayer(LayerName);
            if (layer >= 0) go.layer = layer;
        }
        go.name = "Pickup_" + definition.displayName;

        var pickup = go.GetComponent<Pickup>();
        if (pickup == null) pickup = (Pickup)go.AddComponent(ComponentFor(definition.reward.kind));
        pickup.definition = definition;
        pickup.BuildVisual();
        return pickup;
    }
}
