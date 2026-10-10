using System;
using UnityEngine;

// What the player is carrying: the pistol and its ammo. Goes on the FirstPersonController.
public class PlayerInventory : MonoBehaviour
{
    public const int MagazineSize = 8;
    public const int ReserveMax = 24;

    public static PlayerInventory Instance { get; private set; }

    public bool hasPistol;
    public int magazineAmmo;
    public int reserveAmmo;

    [Tooltip("The Pistol object on the player. Kept inactive until the pistol is picked up.")]
    public GameObject pistolObject;

    public event Action OnChanged;

    void Awake()
    {
        Instance = this;
        magazineAmmo = Mathf.Clamp(magazineAmmo, 0, MagazineSize);
        reserveAmmo = Mathf.Clamp(reserveAmmo, 0, ReserveMax);
        if (pistolObject != null) pistolObject.SetActive(hasPistol);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // Gives the pistol with a full magazine. False if the player already had it.
    public bool GivePistol()
    {
        if (hasPistol) return false;
        hasPistol = true;
        magazineAmmo = MagazineSize;
        if (pistolObject != null) pistolObject.SetActive(true);
        OnChanged?.Invoke();
        return true;
    }

    // Adds to the reserve, up to the maximum. Returns how many rounds were actually added.
    public int AddReserveAmmo(int amount)
    {
        int added = Mathf.Clamp(amount, 0, ReserveMax - reserveAmmo);
        if (added == 0) return 0;
        reserveAmmo += added;
        OnChanged?.Invoke();
        return added;
    }

    public bool TryUseRound()
    {
        if (!hasPistol || magazineAmmo <= 0) return false;
        magazineAmmo--;
        OnChanged?.Invoke();
        return true;
    }

    public bool CanReload => hasPistol && magazineAmmo < MagazineSize && reserveAmmo > 0;

    // Tops the magazine up from the reserve. False if nothing was loaded.
    public bool Reload()
    {
        int loaded = Mathf.Min(MagazineSize - magazineAmmo, reserveAmmo);
        if (!hasPistol || loaded <= 0) return false;
        magazineAmmo += loaded;
        reserveAmmo -= loaded;
        OnChanged?.Invoke();
        return true;
    }
}
