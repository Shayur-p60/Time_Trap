using UnityEngine;

// The player's pistol. Lives on a child of the player camera that stays inactive until PlayerInventory.GivePistol().
// Left mouse fires a hitscan shot, R reloads. There is no viewmodel yet: just the muzzle flash, crosshair and ammo counter.
public class Pistol : MonoBehaviour
{
    public float range = 40f;
    public float damage = 25f;
    [Tooltip("How far the gunshot carries (metres).")]
    public float noiseRadius = 25f;
    [Tooltip("Seconds the muzzle flash light stays on.")]
    public float flashDuration = 0.05f;
    [Tooltip("Minimum seconds between shots.")]
    public float fireInterval = 0.2f;
    [Tooltip("Layers a shot can hit. Left empty: everything except Ignore Raycast, Player, UI and TransparentFX.")]
    public LayerMask hitMask;
    [Tooltip("Optional. Created automatically when empty.")]
    public Light muzzleFlash;
    [Tooltip("Optional. Left empty, the camera above this object (or the main camera) is used.")]
    public Camera aimCamera;

    PlayerInventory inventory;
    float nextFireTime;
    float flashOffTime;

    // Result of the most recent shot.
    public bool LastShotHit { get; private set; }
    public RaycastHit LastHit { get; private set; }

    void Awake()
    {
        inventory = GetComponentInParent<PlayerInventory>();
        if (aimCamera == null) aimCamera = GetComponentInParent<Camera>();
        if (hitMask.value == 0) hitMask = ~LayerMask.GetMask("Ignore Raycast", "Player", "UI", "TransparentFX");

        if (muzzleFlash == null)
        {
            var go = new GameObject("MuzzleFlash");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0.15f, -0.1f, 0.5f);
            muzzleFlash = go.AddComponent<Light>();
            muzzleFlash.type = LightType.Point;
            muzzleFlash.color = new Color(1f, 0.85f, 0.55f);
            muzzleFlash.range = 10f;
            muzzleFlash.intensity = 8f;
            muzzleFlash.shadows = LightShadows.None;
        }
        muzzleFlash.enabled = false;
    }

    void OnEnable()
    {
        if (inventory == null) inventory = PlayerInventory.Instance;
        if (inventory != null) inventory.OnChanged += RefreshHud;
        GameHUD.Crosshair(true);
        RefreshHud();
    }

    void OnDisable()
    {
        if (inventory != null) inventory.OnChanged -= RefreshHud;
        if (muzzleFlash != null) muzzleFlash.enabled = false;
        GameHUD.Crosshair(false);
        GameHUD.Ammo("");
    }

    void Update()
    {
        if (muzzleFlash != null && muzzleFlash.enabled && Time.time >= flashOffTime)
            muzzleFlash.enabled = false;

        // a click while the cursor is free only re-locks it (PlayerMovementController); it does not fire
        if (Input.GetMouseButtonDown(0) && Cursor.lockState == CursorLockMode.Locked)
            Fire();

        if (Input.GetKeyDown(KeyCode.R) && inventory != null)
            inventory.Reload();
    }

    // Fires one shot from the screen centre. False if there was no round to fire or the pistol is between shots.
    public bool Fire()
    {
        if (inventory == null || Time.time < nextFireTime) return false;
        if (!inventory.TryUseRound()) return false;
        nextFireTime = Time.time + fireInterval;

        if (muzzleFlash != null)
        {
            muzzleFlash.enabled = true;
            flashOffTime = Time.time + flashDuration;
        }

        Transform origin = aimCamera != null ? aimCamera.transform : (Camera.main != null ? Camera.main.transform : transform);
        NoiseSystem.MakeNoise(origin.position, noiseRadius);

        LastShotHit = Physics.Raycast(origin.position, origin.forward, out RaycastHit hit, range, hitMask, QueryTriggerInteraction.Ignore);
        LastHit = hit;
        if (LastShotHit)
        {
            var target = hit.collider.GetComponentInParent<IDamageable>();
            if (target != null) target.TakeHit(damage, hit.point);
        }
        return true;
    }

    void RefreshHud()
    {
        if (inventory != null && isActiveAndEnabled)
            GameHUD.Ammo(inventory.magazineAmmo + " / " + inventory.reserveAmmo);
    }
}
