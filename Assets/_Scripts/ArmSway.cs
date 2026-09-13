using UnityEngine;

public class ArmSway : MonoBehaviour
{
    [Header("Sway Settings")]
    public float idleSwaySpeed = 2f;
    public float idleSwayAmount = 0.01f;
    
    public float walkSwaySpeed = 4f;
    public float walkSwayAmount = 0.03f;
    
    public float sprintSwaySpeed = 7f;
    public float sprintSwayAmount = 0.06f;

    [Header("References")]
    public CharacterController playerController; // We need this to check player speed

    private float timer;
    private Vector3 startPos;

    void Start()
    {
        startPos = transform.localPosition; 
    }

    void Update()
    {
        float currentSpeed = idleSwaySpeed;
        float currentAmount = idleSwayAmount;

        // Check how fast the player is actually moving
        if (playerController != null)
        {
            float speed = playerController.velocity.magnitude;

            if (speed > 4f) // Sprinting (Sprint speed is 5 in your controller)
            {
                currentSpeed = sprintSwaySpeed;
                currentAmount = sprintSwayAmount;
            }
            else if (speed > 0.1f) // Walking (Walk speed is 3)
            {
                currentSpeed = walkSwaySpeed;
                currentAmount = walkSwayAmount;
            }
        }

        // Advance the timer based on our current speed state
        timer += Time.deltaTime * currentSpeed;

        // Calculate the figure-8 motion
        float xOffset = Mathf.Cos(timer / 2) * currentAmount;
        float yOffset = Mathf.Sin(timer) * currentAmount;

        // Apply it smoothly
        transform.localPosition = Vector3.Lerp(transform.localPosition, startPos + new Vector3(xOffset, yOffset, 0), Time.deltaTime * 10f);
    }
}