using UnityEngine;

/// <summary>
/// Attach to any GameObject in the scene.
/// Continuously rotates the scene Skybox material, making a stationary
/// object appear to drift through space.
/// Requires the Skybox material to use a shader that exposes _Rotation
/// (Unity's built-in Skybox/Panoramic and Skybox/Cubemap shaders both do).
/// </summary>
public class SkyboxDrift : MonoBehaviour
{
    [Header("Drift Speed")]
    [Tooltip("Degrees per second the skybox rotates. Subtle values (0.5–3) feel most believable.")]
    public float driftSpeed = 1.5f;

    [Header("Drift Axis Blend")]
    [Tooltip("Primary horizontal drift (yaw). Keep this the dominant axis for a 'flying forward' feel.")]
    [Range(0f, 1f)]
    public float yawAmount = 1f;

    [Tooltip("Gentle up/down tilt over time. Small values add life without feeling chaotic.")]
    [Range(0f, 1f)]
    public float pitchAmount = 0.15f;

    [Tooltip("Slow roll. Even a tiny value (0.05) adds a sense of tumbling through space.")]
    [Range(0f, 1f)]
    public float rollAmount = 0.05f;

    [Header("Subtle Oscillation (optional)")]
    [Tooltip("Adds a gentle sine-wave wobble to the drift speed, so it never feels perfectly mechanical.")]
    public bool oscillate = true;
    [Tooltip("How much the speed varies (as a fraction of driftSpeed).")]
    [Range(0f, 1f)]
    public float oscillationAmount = 0.3f;
    [Tooltip("How slowly the oscillation cycles (seconds per full cycle).")]
    public float oscillationPeriod = 20f;

    // accumulated rotation angles
    private float _yaw   = 0f;
    private float _pitch = 0f;
    private float _roll  = 0f;

    private void OnDestroy()
    {
        // Reset skybox rotation when the object is destroyed / play mode ends
        RenderSettings.skybox?.SetFloat("_Rotation", 0f);
    }

    private void Update()
    {
        // Optional sine-wave speed variation
        float speed = driftSpeed;
        if (oscillate && oscillationPeriod > 0f)
        {
            float wave = Mathf.Sin((Time.time / oscillationPeriod) * Mathf.PI * 2f);
            speed *= 1f + wave * oscillationAmount;
        }

        float delta = speed * Time.deltaTime;

        _yaw   += delta * yawAmount;
        _pitch += delta * pitchAmount;
        _roll  += delta * rollAmount;

        // Build a rotation from our three accumulated angles
        Quaternion skyRot = Quaternion.Euler(_pitch, _yaw, _roll);

        // Unity's skybox _Rotation is a single float (degrees around Y).
        // For full 3-axis control we bake the quaternion into the material's
        // rotation matrix instead, which works with Panoramic & Cubemap shaders.
        if (RenderSettings.skybox != null)
        {
            // Simple single-axis path: works with ALL Unity skybox shaders
            if (pitchAmount < 0.001f && rollAmount < 0.001f)
            {
                RenderSettings.skybox.SetFloat("_Rotation", _yaw % 360f);
            }
            else
            {
                // Multi-axis path: set the float for yaw and apply the full
                // rotation via the matrix property (Panoramic shader supports this)
                RenderSettings.skybox.SetFloat("_Rotation", _yaw % 360f);
                Matrix4x4 rotMatrix = Matrix4x4.Rotate(skyRot);
                RenderSettings.skybox.SetMatrix("_RotationMatrix", rotMatrix);
            }
        }
    }

    // ── Public helpers ────────────────────────────────────────────────
    public void SetSpeed(float speed)   => driftSpeed = speed;
    public void Pause()                 => enabled = false;
    public void Resume()                => enabled = true;
}