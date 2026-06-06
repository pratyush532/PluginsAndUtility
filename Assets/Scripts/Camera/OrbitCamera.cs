using UnityEngine;

public class OrbitCamera : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("The object to orbit around")]
    public Transform target;

    [Header("Initial Settings")]
    [Tooltip("Initial distance from target")]
    public float initialDistance = 5f;
    [Tooltip("Initial horizontal angle (degrees, 0=forward, 90=right)")]
    public float initialHorizontalAngle = 0f;
    [Tooltip("Initial vertical angle (degrees, 0=level, positive=look down)")]
    public float initialVerticalAngle = 20f;

    [Header("Distance Settings")]
    [Tooltip("Minimum distance from target")]
    public float minDistance = 2f;
    [Tooltip("Maximum distance from target")]
    public float maxDistance = 15f;

    [Header("Height Limits")]
    [Tooltip("Minimum vertical angle (degrees)")]
    public float minVerticalAngle = -80f;
    [Tooltip("Maximum vertical angle (degrees)")]
    public float maxVerticalAngle = 80f;

    [Header("Rotation Settings")]
    [Tooltip("Horizontal rotation speed")]
    public float horizontalSpeed = 4f;
    [Tooltip("Vertical rotation speed")]
    public float verticalSpeed = 4f;
    [Tooltip("How tightly the camera tracks the mouse while dragging (higher = snappier)")]
    [Range(1f, 30f)]
    public float dragSmoothing = 12f;

    [Header("Momentum")]
    [Tooltip("How much spin carries over after releasing the mouse. " +
             "0 = instant stop, 0.85 = natural glide, 0.99 = barely decelerates.")]
    [Range(0f, 0.99f)]
    public float momentumDecay = 0.85f;

    [Header("Zoom Settings")]
    [Tooltip("Zoom speed (mouse wheel)")]
    public float zoomSpeed = 2f;
    [Tooltip("Zoom smoothing factor")]
    public float zoomSmoothing = 8f;

    [Header("Input Settings")]
    [Tooltip("Mouse button for orbit (0=Left, 1=Right, 2=Middle)")]
    public int orbitMouseButton = 0;
    [Tooltip("Enable touch input for touch panels")]
    public bool enableTouchInput = true;

    [Header("Screen Offset (for UI panels)")]
    [Tooltip("Enable to shift model horizontally for UI")]
    public bool offsetForUI = false;
    [Tooltip("Horizontal offset amount (-1 to 1, negative=left, positive=right)")]
    [Range(-1f, 1f)]
    public float screenOffsetX = 0.25f;
    [Tooltip("Transition speed for offset changes")]
    public float offsetTransitionSpeed = 3f;

    // Private variables
    private float currentDistance;
    private float targetDistance;
    private float currentX = 0f;
    private float currentY = 0f;

    // Momentum velocities (degrees per second)
    private float velocityX = 0f;
    private float velocityY = 0f;

    private Vector2 lastTouchPosition;
    private bool isTouching = false;
    private Camera cam;
    private float currentScreenOffsetX = 0f;
    private float targetScreenOffsetX = 0f;

    void Start()
    {
        if (target == null)
        {
            GameObject targetObj = new GameObject("OrbitTarget");
            targetObj.transform.position = Vector3.zero;
            target = targetObj.transform;
        }

        cam = GetComponent<Camera>();
        if (cam == null)
            cam = Camera.main;

        currentDistance = initialDistance;
        targetDistance  = initialDistance;

        currentX = initialHorizontalAngle;
        currentY = initialVerticalAngle;

        Quaternion rotation = Quaternion.Euler(currentY, currentX, 0);
        transform.position  = rotation * new Vector3(0, 0, -currentDistance) + target.position;
        transform.rotation  = rotation;

        currentScreenOffsetX = offsetForUI ? screenOffsetX : 0f;
        targetScreenOffsetX  = currentScreenOffsetX;
    }

    void LateUpdate()
    {
        HandleInput();
        UpdateScreenOffset();
        UpdateCamera();
    }

    void HandleInput()
    {
        bool isDragging = Input.GetMouseButton(orbitMouseButton);

        // ── Mouse drag ──────────────────────────────────────────────────
        if (isDragging)
        {
            float rawX =  Input.GetAxis("Mouse X") * horizontalSpeed;
            float rawY = -Input.GetAxis("Mouse Y") * verticalSpeed;

            // Smoothly track raw input so the camera doesn't feel glued to the cursor
            float t = dragSmoothing * Time.deltaTime;
            velocityX = Mathf.Lerp(velocityX, rawX, t);
            velocityY = Mathf.Lerp(velocityY, rawY, t);
        }
        else
        {
            // Mouse released — decay velocity exponentially (framerate-independent)
            float decayThisFrame = Mathf.Pow(momentumDecay, Time.deltaTime * 60f);
            velocityX *= decayThisFrame;
            velocityY *= decayThisFrame;

            // Kill micro-drift
            if (Mathf.Abs(velocityX) < 0.01f) velocityX = 0f;
            if (Mathf.Abs(velocityY) < 0.01f) velocityY = 0f;
        }

        currentX += velocityX;
        currentY += velocityY;

        // ── Touch input ──────────────────────────────────────────────────
        if (enableTouchInput && Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);

            if (touch.phase == TouchPhase.Began)
            {
                lastTouchPosition = touch.position;
                isTouching = true;
            }
            else if (touch.phase == TouchPhase.Moved && isTouching)
            {
                Vector2 delta = touch.position - lastTouchPosition;
                lastTouchPosition = touch.position;

                currentX += delta.x * horizontalSpeed * 0.1f;
                currentY -= delta.y * verticalSpeed   * 0.1f;
            }
            else if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
            {
                isTouching = false;
            }

            // Pinch to zoom
            if (Input.touchCount == 2)
            {
                Touch touch0 = Input.GetTouch(0);
                Touch touch1 = Input.GetTouch(1);

                Vector2 prev0 = touch0.position - touch0.deltaPosition;
                Vector2 prev1 = touch1.position - touch1.deltaPosition;

                float prevMag = (prev0 - prev1).magnitude;
                float currMag = (touch0.position - touch1.position).magnitude;

                targetDistance += (prevMag - currMag) * zoomSpeed * 0.01f;
            }
        }

        // ── Scroll zoom ──────────────────────────────────────────────────
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.01f)
            targetDistance -= scroll * zoomSpeed;

        // ── Clamp ────────────────────────────────────────────────────────
        currentY       = Mathf.Clamp(currentY, minVerticalAngle, maxVerticalAngle);
        targetDistance = Mathf.Clamp(targetDistance, minDistance, maxDistance);
    }

    void UpdateScreenOffset()
    {
        targetScreenOffsetX  = offsetForUI ? screenOffsetX : 0f;
        currentScreenOffsetX = Mathf.Lerp(currentScreenOffsetX, targetScreenOffsetX, Time.deltaTime * offsetTransitionSpeed);
    }

    void UpdateCamera()
    {
        // Smooth zoom
        currentDistance = Mathf.Lerp(currentDistance, targetDistance, Time.deltaTime * zoomSmoothing);

        // Build transform from current angles (already smoothed via velocity in HandleInput)
        Quaternion rotation = Quaternion.Euler(currentY, currentX, 0);
        Vector3    position = rotation * new Vector3(0, 0, -currentDistance) + target.position;

        // Screen-space offset for UI panels
        if (Mathf.Abs(currentScreenOffsetX) > 0.001f && cam != null)
            position += transform.right * currentScreenOffsetX * currentDistance;

        transform.rotation = rotation;
        transform.position = position;
    }

    // ── Public API ───────────────────────────────────────────────────────

    public void SetDistance(float distance)
    {
        targetDistance = Mathf.Clamp(distance, minDistance, maxDistance);
    }

    public void SetRotation(float horizontal, float vertical)
    {
        currentX  = horizontal;
        currentY  = Mathf.Clamp(vertical, minVerticalAngle, maxVerticalAngle);
        velocityX = 0f;
        velocityY = 0f;
    }

    public void ResetCamera()
    {
        currentDistance = initialDistance;
        targetDistance  = initialDistance;
        currentX        = initialHorizontalAngle;
        currentY        = initialVerticalAngle;
        velocityX       = 0f;
        velocityY       = 0f;

        Quaternion rotation = Quaternion.Euler(currentY, currentX, 0);
        transform.position  = rotation * new Vector3(0, 0, -currentDistance) + target.position;
        transform.rotation  = rotation;
    }

    public void SetUIOffsetMode(bool enable)    => offsetForUI  = enable;
    public void SetScreenOffset(float offset)   => screenOffsetX = Mathf.Clamp(offset, -1f, 1f);
}