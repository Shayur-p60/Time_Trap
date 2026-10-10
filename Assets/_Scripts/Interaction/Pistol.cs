using System;
using UnityEngine;

// The player's pistol. Lives on a child of the player camera that stays inactive until PlayerInventory.GivePistol().
// Left mouse fires a hitscan shot from the screen centre, R reloads. It only fires while PlayerWeaponState says the
// pistol is drawn (with no PlayerWeaponState on the player it is always ready, as before).
// The gun model, the muzzle flash position and the weapon light are handled by PlayerWeaponState and the markers
// under WeaponModelSlot; this script is the shooting itself.
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
    [Tooltip("Seconds a reload takes. The rounds go in at the end; no firing meanwhile.")]
    public float reloadTime = 1.2f;
    [Tooltip("Layers a shot can hit. Left empty: everything except Ignore Raycast, Player, UI and TransparentFX.")]
    public LayerMask hitMask;
    [Tooltip("Optional. Created automatically when empty.")]
    public Light muzzleFlash;
    [Tooltip("The flash appears here: MuzzlePoint at the tip of the gun's barrel. Left empty, the flash stays where it is.")]
    public Transform muzzlePoint;
    [Tooltip("Optional. Left empty, the camera above this object (or the main camera) is used.")]
    public Camera aimCamera;

    PlayerInventory inventory;
    PlayerWeaponState weaponState;
    float nextFireTime;
    float flashOffTime;
    float reloadEndTime;
    bool hudShown;

    // Result of the most recent shot.
    public bool LastShotHit { get; private set; }
    public RaycastHit LastHit { get; private set; }
    public int ShotsFired { get; private set; }

    public bool IsReloading { get; private set; }
    // 0 at the start of a reload, 1 at the end (0 when not reloading).
    public float ReloadProgress => IsReloading && reloadTime > 0f ? Mathf.Clamp01(1f - (reloadEndTime - Time.time) / reloadTime) : 0f;

    public event Action OnFired;
    public event Action OnReloadStarted;
    public event Action OnReloadFinished;

    // The pistol is in the hands and ready (always, when the player has no PlayerWeaponState).
    bool Ready => weaponState == null || weaponState.CanFire;
    bool Drawn => weaponState == null || weaponState.IsPistolOut;

    void Awake()
    {
        inventory = GetComponentInParent<PlayerInventory>();
        weaponState = GetComponentInParent<PlayerWeaponState>();
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
        ShowHud(Drawn);
    }

    void OnDisable()
    {
        if (inventory != null) inventory.OnChanged -= RefreshHud;
        if (muzzleFlash != null) muzzleFlash.enabled = false;
        IsReloading = false;
        ShowHud(false);
    }

    void Update()
    {
        if (muzzleFlash != null && muzzleFlash.enabled && Time.time >= flashOffTime)
            muzzleFlash.enabled = false;

        if (hudShown != Drawn) ShowHud(Drawn);

        if (IsReloading)
        {
            if (!Drawn) CancelReload();
            else if (Time.time >= reloadEndTime) FinishReload();
        }

        if (GameManager.InputBlocked) return;

        // a click while the cursor is free only re-locks it (PlayerMovementController); it does not fire
        if (Input.GetMouseButtonDown(0) && Cursor.lockState == CursorLockMode.Locked)
            Fire();

        if (Input.GetKeyDown(KeyCode.R))
            StartReload();
    }

    // Fires one shot from the screen centre. False if the pistol is not drawn and ready, is reloading or between
    // shots, or there was no round to fire.
    public bool Fire()
    {
        if (inventory == null || !Ready || IsReloading || Time.time < nextFireTime) return false;
        if (!inventory.TryUseRound()) return false;
        nextFireTime = Time.time + fireInterval;
        ShotsFired++;

        if (muzzleFlash != null)
        {
            if (muzzlePoint != null) muzzleFlash.transform.position = muzzlePoint.position;
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

        OnFired?.Invoke();
        return true;
    }

    // Starts a reload. False if the pistol is not drawn and ready, is already reloading, or there is nothing to load.
    public bool StartReload()
    {
        if (inventory == null || !Ready || IsReloading || !inventory.CanReload) return false;
        if (reloadTime <= 0f)
        {
            inventory.Reload();
            return true;
        }
        IsReloading = true;
        reloadEndTime = Time.time + reloadTime;
        OnReloadStarted?.Invoke();
        return true;
    }

    void FinishReload()
    {
        IsReloading = false;
        if (inventory != null) inventory.Reload();
        OnReloadFinished?.Invoke();
    }

    // The pistol was put away mid-reload: nothing is loaded.
    void CancelReload()
    {
        IsReloading = false;
        OnReloadFinished?.Invoke();
    }

    // Crosshair and ammo counter: only while the pistol is out.
    void ShowHud(bool show)
    {
        hudShown = show;
        GameHUD.Crosshair(show);
        if (show) RefreshHud();
        else GameHUD.Ammo("");
    }

    void RefreshHud()
    {
        if (inventory != null && isActiveAndEnabled && hudShown)
            GameHUD.Ammo(inventory.magazineAmmo + " / " + inventory.reserveAmmo);
    }
}
