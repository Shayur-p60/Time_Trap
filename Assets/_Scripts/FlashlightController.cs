using UnityEngine;

public class FlashlightController : MonoBehaviour
{
    [Header("References")]
    public Light flashlightLight;

    [Header("Battery Settings")]
    public float maxBattery = 100f;
    public float drainRate = 5f; // battery used per second while on

    private float currentBattery;
    private bool isOn = true;

    void Start()
    {
        currentBattery = maxBattery;
        if (flashlightLight != null)
            flashlightLight.enabled = isOn;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F))
        {
            if (isOn || currentBattery > 0f)
            {
                isOn = !isOn;
                flashlightLight.enabled = isOn;
            }
        }

        if (isOn && currentBattery > 0f)
        {
            currentBattery -= drainRate * Time.deltaTime;
            currentBattery = Mathf.Max(currentBattery, 0f);

            if (currentBattery <= 0f)
            {
                isOn = false;
                flashlightLight.enabled = false;
            }
        }
    }

    // Call this from your chest/pickup system when the player finds a battery reward
    public void AddBattery(float amount)
    {
        currentBattery = Mathf.Min(currentBattery + amount, maxBattery);
    }

    public float GetBatteryPercent() => currentBattery / maxBattery;
}
