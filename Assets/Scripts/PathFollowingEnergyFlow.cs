using UnityEngine;
using System.Collections.Generic;

public class PathFollowingEnergyFlow : MonoBehaviour
{
    [Header("Path Settings")]
    [SerializeField] private List<Transform> pathPoints = new List<Transform>();
    [SerializeField] private float pipeRadius = 0.5f;
    [SerializeField] private int interpolationSteps = 20;
    
    [Header("Path Setup Helper")]
    [Tooltip("Clicking this will automatically add all child GameObjects as path points in order")]
    [SerializeField] private bool autoPopulateFromChildren = false;
    
    [Header("Particle Settings")]
    [SerializeField] private int particleCount = 50;
    [SerializeField] private float flowSpeed = 2f;
    [SerializeField] private GameObject particlePrefab;
    [SerializeField] private float particleSize = 0.1f;
    
    [Header("Rotation & Bending Settings")]
    [SerializeField] private bool enableRotation = true;
    [Tooltip("How smoothly particles rotate to face direction (higher = smoother)")]
    [SerializeField] private float rotationSmoothness = 10f;
    [SerializeField] private bool enableMeshBending = false;
    [Tooltip("How much the mesh bends around curves (0-1)")]
    [SerializeField] private float bendStrength = 0.5f;
    [Tooltip("Update mesh bending every N frames (higher = better performance, lower = smoother bending). 1 = every frame, 3 = every 3rd frame.")]
    [SerializeField] private int bendUpdateInterval = 3;
    
    [Header("Simulation Control")]
    [SerializeField] private bool simulationEnabled = true;
    
    [Header("Fill Animation Settings")]
    [SerializeField] private bool useFillAnimation = true;
    
    [Header("Start Delay Settings")]
    [SerializeField] private bool useStartDelay = false;
    [Tooltip("Delay in seconds before simulation starts")]
    [SerializeField] private float startDelay = 1f;
    
    [Header("Cyclic Toggle Settings")]
    [SerializeField] private bool enableCyclicToggle = false;
    [Tooltip("Time in seconds after simulation is fully running before the first hide. 0 = hide immediately.")]
    [SerializeField] private float initialDelayBeforeToggle = 5f;
    [Tooltip("Duration in seconds the particles stay hidden")]
    [SerializeField] private float toggleOffDuration = 2f;
    [Tooltip("Duration in seconds the particles stay visible")]
    [SerializeField] private float toggleOnDuration = 3f;
    
    [Header("Visual Settings")]
    [SerializeField] private bool singleFileLine = false;
    [SerializeField] private bool spiralMotion = true;
    [SerializeField] private float spiralSpeed = 1f;
    [SerializeField] private bool debugDrawPath = true;
    
    [Header("Performance Mode")]
    [Tooltip("Reduce per-particle calculations. Good for straight paths or when extreme performance is needed.")]
    [SerializeField] private bool performanceMode = false;
    [Tooltip("In performance mode, only update every Nth particle's full transform (others interpolate). Higher = faster.")]
    [SerializeField] private int performanceModeSkip = 2;
    
    // ── Core state ────────────────────────────────────────────────────
    private bool wasSimulationEnabled;
    private Transform[] particles;
    private float[] particleProgress;
    private float[] spiralAngles;
    private List<Vector3> smoothPath;
    private float totalPathLength;
    private List<float> segmentLengths;
    private List<float> cumulativeLengths;
    
    // Fill animation state
    private bool isFilling = false;
    private float fillTimer = 0f;
    private float[] targetProgress;
    private int visibleParticleCount = 0;
    private bool[] revealedByFill;  // tracks which particles the fill wave has reached (independent of SetActive)
    
    // Start delay state
    private bool isWaitingToStart = false;
    private float startDelayTimer = 0f;
    
    // Rotation tracking
    private Quaternion[] particleRotations;
    private Vector3[] previousPositions;
    
    // Mesh bending data
    private MeshFilter[] particleMeshFilters;
    private Mesh[] originalMeshes;
    private Mesh[] deformedMeshes;
    private int bendFrameCounter = 0;  // for skipping mesh updates
    private Vector3[][] cachedDeformedVertices;  // reusable vertex arrays per particle
    
    // ── Cyclic toggle state ───────────────────────────────────────────
    // Completely independent of simulationEnabled.
    // Simulation keeps running every frame; this layer only calls SetActive on particles.
    private bool cyclicActive = false;          // Are we currently cycling?
    private bool cyclicHasHiddenOnce = false;   // Have we done the first hide yet? (distinguishes initial delay from ON duration)
    private bool cyclicHidden = false;          // Are particles currently hidden by cyclic toggle?
    private float cyclicTimer = 0f;             // Time elapsed in the current phase

    // ── Performance optimizations ─────────────────────────────────────
    private Vector3[] cachedPathPositions;      // Cache path lookups for particles
    private Vector3[] cachedPathForwards;       // Cache forward vectors
    private float[] cachedPathProgress;         // Track last progress value to detect if cache is valid
    private const float PATH_CACHE_EPSILON = 0.001f;  // Only recalc path if progress changed by this much
    
    private float[] cachedSpiralCos;            // Cache spiral cos values
    private float[] cachedSpiralSin;            // Cache spiral sin values
    private float[] lastSpiralAngles;           // Track last angle to know when to recalc

    // ─────────────────────────────────────────────────────────────────

    void Start()
    {
        if (pathPoints == null || pathPoints.Count < 2)
        {
            Debug.LogError("Need at least 2 path points assigned!");
            return;
        }
        
        GenerateSmoothPath();
        InitializeParticles();
        
        wasSimulationEnabled = simulationEnabled;
        UpdateSimulationState();
    }

    // ── Path ──────────────────────────────────────────────────────────
    void GenerateSmoothPath()
    {
        smoothPath = new List<Vector3>();
        
        for (int i = 0; i < pathPoints.Count - 1; i++)
        {
            Vector3 startPos = pathPoints[i].position;
            Vector3 endPos = pathPoints[i + 1].position;
            
            for (int j = 0; j < interpolationSteps; j++)
            {
                float t = (float)j / interpolationSteps;
                smoothPath.Add(Vector3.Lerp(startPos, endPos, t));
            }
        }
        smoothPath.Add(pathPoints[pathPoints.Count - 1].position);

        segmentLengths = new List<float>();
        cumulativeLengths = new List<float>();
        totalPathLength = 0f;
        
        for (int i = 0; i < smoothPath.Count - 1; i++)
        {
            float len = Vector3.Distance(smoothPath[i], smoothPath[i + 1]);
            segmentLengths.Add(len);
            cumulativeLengths.Add(totalPathLength);
            totalPathLength += len;
        }
        cumulativeLengths.Add(totalPathLength);
    }

    // ── Particles ─────────────────────────────────────────────────────
    void InitializeParticles()
    {
        particles = new Transform[particleCount];
        particleProgress = new float[particleCount];
        spiralAngles = new float[particleCount];
        targetProgress = new float[particleCount];
        particleRotations = new Quaternion[particleCount];
        previousPositions = new Vector3[particleCount];
        revealedByFill = new bool[particleCount];
        particleMeshFilters = new MeshFilter[particleCount];
        originalMeshes = new Mesh[particleCount];
        deformedMeshes = new Mesh[particleCount];
        cachedDeformedVertices = new Vector3[particleCount][];

        // Allocate path caching arrays
        cachedPathPositions = new Vector3[particleCount];
        cachedPathForwards = new Vector3[particleCount];
        cachedPathProgress = new float[particleCount];
        cachedSpiralCos = new float[particleCount];
        cachedSpiralSin = new float[particleCount];
        lastSpiralAngles = new float[particleCount];
        for (int i = 0; i < particleCount; i++)
        {
            cachedPathProgress[i] = -1f;  // Initialize to invalid value to force first calculation
            lastSpiralAngles[i] = -9999f; // Initialize to invalid value
        }

        for (int i = 0; i < particleCount; i++)
        {
            GameObject particle = particlePrefab != null 
                ? Instantiate(particlePrefab) 
                : GameObject.CreatePrimitive(PrimitiveType.Sphere);
            
            // Set scale BEFORE parenting to avoid inheritance issues
            particle.transform.localScale = Vector3.one * particleSize;
            
            // Now parent it
            particle.transform.SetParent(transform, true);  // worldPositionStays = true
            
            particles[i] = particle.transform;
            targetProgress[i] = (float)i / particleCount;
            particleProgress[i] = 0f;
            spiralAngles[i] = Random.Range(0f, 360f);
            particleRotations[i] = Quaternion.identity;
            previousPositions[i] = Vector3.zero;
            
            if (enableMeshBending)
            {
                MeshFilter mf = particle.GetComponent<MeshFilter>();
                if (mf != null && mf.mesh != null)
                {
                    particleMeshFilters[i] = mf;
                    originalMeshes[i] = mf.mesh;
                    deformedMeshes[i] = Instantiate(mf.mesh);
                    mf.mesh = deformedMeshes[i];
                    
                    // Allocate vertex array once
                    cachedDeformedVertices[i] = new Vector3[originalMeshes[i].vertexCount];
                }
            }
            
            particle.SetActive(false);
        }
    }

    // ── Validation ────────────────────────────────────────────────────
    void OnValidate()
    {
        if (autoPopulateFromChildren)
        {
            autoPopulateFromChildren = false;
            PopulatePathPointsFromChildren();
        }
        if (Application.isPlaying && particles != null)
            UpdateSimulationState();
    }
    
    void PopulatePathPointsFromChildren()
    {
        pathPoints.Clear();
        if (transform.childCount == 0)
        {
            Debug.LogWarning("No child objects found! Add empty GameObjects as children to use as waypoints.");
            return;
        }
        for (int i = 0; i < transform.childCount; i++)
            pathPoints.Add(transform.GetChild(i));
        
        Debug.Log($"Auto-populated {pathPoints.Count} path points from child objects!");
        if (Application.isPlaying && smoothPath != null)
            GenerateSmoothPath();
    }

    // ── Update ────────────────────────────────────────────────────────
    void Update()
    {
        // Manual simulationEnabled toggle (inspector or code)
        if (simulationEnabled != wasSimulationEnabled)
        {
            wasSimulationEnabled = simulationEnabled;
            UpdateSimulationState();
            
            if (simulationEnabled)
            {
                if (useStartDelay)
                    StartDelayedSimulation();
                else if (useFillAnimation)
                    StartFillAnimation();
            }
        }
        
        if (!simulationEnabled || particles == null || smoothPath == null || smoothPath.Count == 0)
            return;
        
        if (isWaitingToStart)
        {
            UpdateStartDelay();
            return;  // don't run simulation or cyclic toggle until start delay is done
        }
        
        // ── Simulation runs every frame — always, regardless of cyclic toggle ──
        if (isFilling)
            UpdateFillAnimation();
        else
            UpdateNormalFlow();
        
        // ── Cyclic toggle runs after simulation — purely a visibility layer ──
        if (enableCyclicToggle && cyclicActive)
            UpdateCyclicToggle();
        
        // Increment frame counter for mesh bending skip logic
        bendFrameCounter++;
    }

    // ── Start delay ───────────────────────────────────────────────────
    void StartDelayedSimulation()
    {
        isWaitingToStart = true;
        startDelayTimer = 0f;
        for (int i = 0; i < particleCount; i++)
        {
            if (particles[i] != null)
                particles[i].gameObject.SetActive(false);
            previousPositions[i] = Vector3.zero;
        }
    }

    void UpdateStartDelay()
    {
        startDelayTimer += Time.deltaTime;
        if (startDelayTimer < startDelay) return;
        
        // Start delay finished
        isWaitingToStart = false;
        
        if (useFillAnimation)
        {
            StartFillAnimation();
        }
        else
        {
            for (int i = 0; i < particleCount; i++)
            {
                if (particles[i] != null)
                    particles[i].gameObject.SetActive(true);
                particleProgress[i] = targetProgress[i];
                previousPositions[i] = Vector3.zero;
            }
        }
        
        // Start delay is done — kick off cyclic toggle now
        if (enableCyclicToggle)
            StartCyclicToggle();
    }

    // ── Fill animation ────────────────────────────────────────────────
    void StartFillAnimation()
    {
        isFilling = true;
        fillTimer = 0f;
        visibleParticleCount = 0;
        for (int i = 0; i < particleCount; i++)
        {
            particleProgress[i] = 0f;
            revealedByFill[i] = false;
            if (particles[i] != null)
                particles[i].gameObject.SetActive(false);
            previousPositions[i] = Vector3.zero;
        }
        
        // If there's no start delay, cyclic toggle starts once fill is done —
        // we kick it off at the end of UpdateFillAnimation instead.
        // If there IS a start delay, it was already started in UpdateStartDelay.
    }

    void UpdateFillAnimation()
    {
        fillTimer += Time.deltaTime;
        float frontProgress = (flowSpeed / totalPathLength) * fillTimer;
        
        // Mark particles as revealed by the fill wave — independent of visibility
        for (int i = 0; i < particleCount; i++)
        {
            if (!revealedByFill[i] && frontProgress >= targetProgress[i])
            {
                revealedByFill[i] = true;
                visibleParticleCount++;
                Vector3 dummyForward;
                Vector3 properPos = GetPointOnPath(targetProgress[i], out dummyForward);
                particles[i].position = properPos;
                previousPositions[i] = properPos;
                
                // Only actually show if cyclic toggle isn't hiding right now
                if (!cyclicHidden)
                    particles[i].gameObject.SetActive(true);
            }
        }
        
        // Update progress on all revealed particles (regardless of whether they're visible)
        float deltaProgress = (flowSpeed / totalPathLength) * Time.deltaTime;
        for (int i = 0; i < particleCount; i++)
        {
            if (revealedByFill[i])
            {
                float newProgress = particleProgress[i] + deltaProgress;
                bool wrapped = false;
                if (newProgress > 1f) { newProgress -= 1f; wrapped = true; }
                particleProgress[i] = newProgress;

                if (wrapped)
                {
                    Vector3 dummyForward;
                    Vector3 newPos = GetPointOnPath(particleProgress[i], out dummyForward);
                    previousPositions[i] = newPos;
                    particles[i].position = newPos;
                    // Force recalc on next frame after wrapping
                    cachedPathProgress[i] = -1f;
                }
                
                // Performance mode: only update full transform for every Nth particle
                if (performanceMode && i % performanceModeSkip != 0 && !wrapped)
                {
                    // Smoothly interpolate position to avoid "stuck" appearance
                    if (cachedPathProgress[i] >= 0f)
                    {
                        float progressDelta = particleProgress[i] - cachedPathProgress[i];
                        float moveDist = progressDelta * totalPathLength;
                        particles[i].position += cachedPathForwards[i] * moveDist;
                    }
                    continue;
                }
                
                UpdateParticleTransform(i);
            }
        }
        
        if (frontProgress >= 1f)
        {
            isFilling = false;
            // Fill just finished and start delay was not used — start cyclic toggle now
            if (enableCyclicToggle && !cyclicActive && !useStartDelay)
                StartCyclicToggle();
        }
    }

    // ── Normal flow ───────────────────────────────────────────────────
    void UpdateNormalFlow()
    {
        float deltaProgress = (flowSpeed / totalPathLength) * Time.deltaTime;
        for (int i = 0; i < particleCount; i++)
        {
            float newProgress = particleProgress[i] + deltaProgress;
            bool wrapped = false;
            if (newProgress > 1f) { newProgress -= 1f; wrapped = true; }
            particleProgress[i] = newProgress;

            if (wrapped)
            {
                Vector3 dummyForward;
                Vector3 newPos = GetPointOnPath(particleProgress[i], out dummyForward);
                previousPositions[i] = newPos;
                particles[i].position = newPos;
                // Force recalc on next frame after wrapping
                cachedPathProgress[i] = -1f;
            }
            
            // Performance mode: only update full transform for every Nth particle
            if (performanceMode && i % performanceModeSkip != 0 && !wrapped)
            {
                // Don't stay stuck — smoothly interpolate position based on progress change
                // This keeps particles moving even when full transform is skipped
                if (cachedPathProgress[i] >= 0f)
                {
                    // Calculate how much progress changed since last full update
                    float progressDelta = particleProgress[i] - cachedPathProgress[i];
                    
                    // Simple linear interpolation along the path
                    // Get approximate new position by moving along cached forward direction
                    float moveDist = progressDelta * totalPathLength;
                    particles[i].position += cachedPathForwards[i] * moveDist;
                }
                continue;
            }
            
            UpdateParticleTransform(i);
        }
    }

    // ── Cyclic Toggle ─────────────────────────────────────────────────
    // Completely decoupled from simulationEnabled. Simulation keeps running.
    // This only calls SetActive on particles.
    //
    // State machine:
    //   VISIBLE (initial delay) ──► HIDDEN ──► VISIBLE (on duration) ──► HIDDEN ──► ...
    //       cyclicTimer counts up      counts up        counts up           counts up
    //       threshold = initialDelay   threshold =      threshold =         threshold =
    //                                  offDuration      onDuration          offDuration
    
    void StartCyclicToggle()
    {
        cyclicActive = true;
        cyclicHasHiddenOnce = false;
        cyclicHidden = false;
        cyclicTimer = 0f;
        
        // If initial delay is 0, hide immediately — don't wait for next frame
        if (initialDelayBeforeToggle <= 0f)
        {
            cyclicHasHiddenOnce = true;
            cyclicHidden = true;
            SetAllParticlesVisible(false);
        }
    }
    
    void UpdateCyclicToggle()
    {
        cyclicTimer += Time.deltaTime;
        
        if (!cyclicHidden)
        {
            // Currently VISIBLE.
            // First time through: wait initialDelayBeforeToggle.
            // After that: wait toggleOnDuration.
            float threshold = cyclicHasHiddenOnce ? toggleOnDuration : initialDelayBeforeToggle;
            
            if (cyclicTimer >= threshold)
            {
                cyclicHasHiddenOnce = true;
                cyclicHidden = true;
                cyclicTimer = 0f;
                SetAllParticlesVisible(false);
            }
        }
        else
        {
            // Currently HIDDEN — wait toggleOffDuration then show again.
            if (cyclicTimer >= toggleOffDuration)
            {
                cyclicHidden = false;
                cyclicTimer = 0f;
                SetAllParticlesVisible(true);
            }
        }
    }
    
    void SetAllParticlesVisible(bool visible)
    {
        for (int i = 0; i < particles.Length; i++)
        {
            if (particles[i] != null)
                particles[i].gameObject.SetActive(visible);
        }
    }
    
    /// <summary>Resets cyclic toggle back to the initial-delay phase. Particles become visible.</summary>
    public void ResetCyclicToggle()
    {
        if (!cyclicActive) return;
        cyclicHasHiddenOnce = false;
        cyclicHidden = false;
        cyclicTimer = 0f;
        SetAllParticlesVisible(true);
    }
    
    /// <summary>Stops cyclic toggling. Particles stay in whatever visibility state they're in.</summary>
    public void StopCyclicToggle()
    {
        cyclicActive = false;
        cyclicTimer = 0f;
    }
    // ──────────────────────────────────────────────────────────────────

    // ── Particle transform ────────────────────────────────────────────
    void UpdateParticleTransform(int i)
    {
        float progress = particleProgress[i];

        if (previousPositions[i] == Vector3.zero)
            previousPositions[i] = particles[i].position;
        
        // Use cached path position if progress hasn't changed much
        Vector3 pathPosition;
        Vector3 forward;
        
        if (Mathf.Abs(progress - cachedPathProgress[i]) < PATH_CACHE_EPSILON)
        {
            // Use cached values
            pathPosition = cachedPathPositions[i];
            forward = cachedPathForwards[i];
        }
        else
        {
            // Recalculate and cache
            pathPosition = GetPointOnPath(progress, out forward);
            cachedPathPositions[i] = pathPosition;
            cachedPathForwards[i] = forward;
            cachedPathProgress[i] = progress;
        }
        
        if (float.IsNaN(pathPosition.x) || float.IsNaN(pathPosition.y) || float.IsNaN(pathPosition.z))
            return;
        
        Vector3 finalPosition;

        if (singleFileLine)
        {
            finalPosition = pathPosition;
        }
        else
        {
            float angle = spiralAngles[i];
            if (spiralMotion)
                angle += progress * 360f * spiralSpeed;

            // Cache sin/cos calculation
            float cosVal, sinVal;
            if (Mathf.Abs(angle - lastSpiralAngles[i]) < 0.1f)
            {
                // Use cached values
                cosVal = cachedSpiralCos[i];
                sinVal = cachedSpiralSin[i];
            }
            else
            {
                // Recalculate and cache
                float angleRad = angle * Mathf.Deg2Rad;
                cosVal = Mathf.Cos(angleRad);
                sinVal = Mathf.Sin(angleRad);
                cachedSpiralCos[i] = cosVal;
                cachedSpiralSin[i] = sinVal;
                lastSpiralAngles[i] = angle;
            }
            
            Vector3 right, up;
            GetPerpendicularVectors(forward, out right, out up);
            finalPosition = pathPosition + (cosVal * right + sinVal * up) * pipeRadius;
        }
        
        if (float.IsNaN(finalPosition.x) || float.IsNaN(finalPosition.y) || float.IsNaN(finalPosition.z))
            return;
        
        if (enableRotation)
            UpdateParticleRotation(i, forward, finalPosition);
        
        // Only update mesh bending every Nth frame AND only if strength > 0.01
        if (enableMeshBending && particleMeshFilters[i] != null && bendStrength > 0.01f)
        {
            // Use modulo to check if this particle should update this frame
            // Spread particles across frames so not all update at once
            if ((bendFrameCounter + i) % bendUpdateInterval == 0)
                BendMesh(i, progress);
        }
        
        particles[i].position = finalPosition;
        previousPositions[i] = finalPosition;
    }

    void UpdateParticleRotation(int i, Vector3 pathForward, Vector3 currentPosition)
    {
        Vector3 movementDirection = currentPosition - previousPositions[i];
        float mag = movementDirection.magnitude;
        if (mag > 0f) movementDirection /= mag;
        if (mag < 0.001f) movementDirection = pathForward;
        
        if (movementDirection != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(movementDirection);
            particleRotations[i] = Quaternion.Slerp(particleRotations[i], targetRotation, Time.deltaTime * rotationSmoothness);
            particles[i].rotation = particleRotations[i];
        }
    }

    // ── Mesh bending ──────────────────────────────────────────────────
    void BendMesh(int particleIndex, float progress)
    {
        if (originalMeshes[particleIndex] == null || deformedMeshes[particleIndex] == null || cachedDeformedVertices[particleIndex] == null)
            return;
        
        progress = Mathf.Clamp01(progress);
        
        Mesh originalMesh = originalMeshes[particleIndex];
        Mesh deformedMesh = deformedMeshes[particleIndex];
        Vector3[] originalVertices = originalMesh.vertices;
        Vector3[] deformedVertices = cachedDeformedVertices[particleIndex];  // REUSE cached array
        
        Bounds bounds = originalMesh.bounds;
        float meshLength = bounds.size.z;
        if (meshLength < 0.0001f) return;
        
        Vector3 scale = particles[particleIndex].localScale;
        float particleWorldLength = meshLength * scale.z;
        if (totalPathLength < 0.0001f) return;
        
        float pathProgressSpan = particleWorldLength / totalPathLength;
        float minSample = progress - pathProgressSpan * 0.5f;
        float maxSample = progress + pathProgressSpan * 0.5f;
        bool spansLoop = minSample < 0f || maxSample > 1f;
        float effectiveBend = spansLoop ? 0f : bendStrength;
        
        Vector3 centerPos = GetPointOnPath(progress, out Vector3 centerForward);
        
        // OPTIMIZATION: Early out if no bending needed
        if (effectiveBend < 0.001f)
        {
            // Just copy original vertices
            for (int i = 0; i < originalVertices.Length; i++)
                deformedVertices[i] = originalVertices[i];
            
            deformedMesh.vertices = deformedVertices;
            return;
        }
        
        for (int i = 0; i < originalVertices.Length; i++)
        {
            Vector3 localVertex = originalVertices[i];
            float vertexNormalized = Mathf.InverseLerp(bounds.min.z, bounds.max.z, localVertex.z);
            float vertexPathProgress = Mathf.Clamp01(progress + (vertexNormalized - 0.5f) * pathProgressSpan);
            
            Vector3 curvePos = GetPointOnPath(vertexPathProgress, out Vector3 pathForward);
            if (float.IsNaN(curvePos.x) || float.IsNaN(curvePos.y) || float.IsNaN(curvePos.z))
            { deformedVertices[i] = localVertex; continue; }
            
            Vector3 right, up;
            GetPerpendicularVectors(pathForward, out right, out up);
            Vector3 worldOffset = localVertex.x * scale.x * right + localVertex.y * scale.y * up;
            
            float distFromCenter = (vertexNormalized - 0.5f) * particleWorldLength;
            Vector3 straightPos = centerPos + centerForward * distFromCenter;
            Vector3 blended = Vector3.Lerp(straightPos, curvePos, effectiveBend);
            Vector3 worldPos = blended + worldOffset;
            
            if (float.IsNaN(worldPos.x) || float.IsNaN(worldPos.y) || float.IsNaN(worldPos.z))
            { deformedVertices[i] = localVertex; continue; }
            
            deformedVertices[i] = particles[particleIndex].InverseTransformPoint(worldPos);
        }
        
        deformedMesh.vertices = deformedVertices;
        deformedMesh.RecalculateNormals();
        deformedMesh.RecalculateBounds();
    }

    // ── Path sampling ─────────────────────────────────────────────────
    Vector3 GetPointOnPath(float progress, out Vector3 forward)
    {
        progress = Mathf.Clamp01(progress);
        
        if (smoothPath == null || smoothPath.Count < 2 || segmentLengths == null || cumulativeLengths == null)
        { forward = Vector3.forward; return Vector3.zero; }
        
        float targetDistance = progress * totalPathLength;
        
        // Binary search for segment
        int left = 0, right = cumulativeLengths.Count - 1;
        while (left < right - 1)
        {
            int mid = (left + right) / 2;
            if (cumulativeLengths[mid] <= targetDistance) left = mid;
            else right = mid;
        }
        int segmentIndex = Mathf.Clamp(left, 0, smoothPath.Count - 2);
        if (segmentIndex >= segmentLengths.Count)
            segmentIndex = Mathf.Max(0, segmentLengths.Count - 1);
        
        float segLen = segmentLengths[segmentIndex];
        float t = segLen > 0.0001f ? (targetDistance - cumulativeLengths[segmentIndex]) / segLen : 0f;
        t = Mathf.Clamp01(t);
        
        Vector3 position = Vector3.Lerp(smoothPath[segmentIndex], smoothPath[segmentIndex + 1], t);
        Vector3 forwardDir = smoothPath[segmentIndex + 1] - smoothPath[segmentIndex];
        forward = forwardDir.magnitude > 0.0001f ? forwardDir.normalized : Vector3.forward;
        return position;
    }

    void GetPerpendicularVectors(Vector3 forward, out Vector3 right, out Vector3 up)
    {
        Vector3 worldUp = Vector3.up;
        if (Mathf.Abs(Vector3.Dot(forward, worldUp)) > 0.99f)
            worldUp = Vector3.forward;
        right = Vector3.Cross(worldUp, forward).normalized;
        up = Vector3.Cross(forward, right).normalized;
    }

    // ── Simulation state ──────────────────────────────────────────────
    void UpdateSimulationState()
    {
        if (particles == null) return;
        
        if (!simulationEnabled)
        {
            foreach (Transform p in particles)
                if (p != null) p.gameObject.SetActive(false);
            
            // Full reset
            isFilling = false;
            fillTimer = 0f;
            visibleParticleCount = 0;
            isWaitingToStart = false;
            startDelayTimer = 0f;
            for (int i = 0; i < particleCount; i++)
                if (i < previousPositions.Length) previousPositions[i] = Vector3.zero;
            
            // Stop cyclic toggle too
            StopCyclicToggle();
        }
        else if (!useFillAnimation && !useStartDelay)
        {
            for (int i = 0; i < particleCount; i++)
            {
                if (particles[i] != null)
                    particles[i].gameObject.SetActive(true);
                particleProgress[i] = targetProgress[i];
                previousPositions[i] = Vector3.zero;
            }
            // No fill, no start delay — start cyclic toggle immediately
            if (enableCyclicToggle)
                StartCyclicToggle();
        }
    }
    
    // ── Public API ────────────────────────────────────────────────────
    public void StartSimulation()
    {
        simulationEnabled = true;
        wasSimulationEnabled = false;
        UpdateSimulationState();
        if (useStartDelay)
            StartDelayedSimulation();
        else if (useFillAnimation)
            StartFillAnimation();
    }
    
    public void StopSimulation()
    {
        simulationEnabled = false;
        wasSimulationEnabled = true;
        UpdateSimulationState();
    }
    
    public void ToggleSimulation()
    {
        simulationEnabled = !simulationEnabled;
        wasSimulationEnabled = !simulationEnabled;
        UpdateSimulationState();
        if (simulationEnabled)
        {
            if (useStartDelay)
                StartDelayedSimulation();
            else if (useFillAnimation)
                StartFillAnimation();
        }
    }

    public bool IsFilling() { return isFilling; }

    // ── Gizmos ────────────────────────────────────────────────────────
    void OnDrawGizmos()
    {
        if (!debugDrawPath) return;

        if (pathPoints != null && pathPoints.Count > 0)
        {
            Gizmos.color = Color.green;
            for (int i = 0; i < pathPoints.Count; i++)
            {
                if (pathPoints[i] != null)
                {
                    Gizmos.DrawWireSphere(pathPoints[i].position, 0.1f);
                    if (i < pathPoints.Count - 1 && pathPoints[i + 1] != null)
                        Gizmos.DrawLine(pathPoints[i].position, pathPoints[i + 1].position);
                }
            }
        }

        if (smoothPath != null && smoothPath.Count > 0)
        {
            Gizmos.color = Color.yellow;
            for (int i = 0; i < smoothPath.Count - 1; i++)
                Gizmos.DrawLine(smoothPath[i], smoothPath[i + 1]);

            Gizmos.color = Color.cyan;
            int circleSegments = 16;
            for (int i = 0; i < smoothPath.Count; i += Mathf.Max(1, smoothPath.Count / 10))
            {
                Vector3 pos = smoothPath[i];
                Vector3 fwd = i < smoothPath.Count - 1 
                    ? (smoothPath[i + 1] - smoothPath[i]).normalized 
                    : (smoothPath[i] - smoothPath[i - 1]).normalized;
                Vector3 right, up;
                GetPerpendicularVectors(fwd, out right, out up);

                for (int j = 0; j < circleSegments; j++)
                {
                    float a1 = (float)j / circleSegments * 2f * Mathf.PI;
                    float a2 = (float)(j + 1) / circleSegments * 2f * Mathf.PI;
                    Gizmos.DrawLine(
                        pos + (Mathf.Cos(a1) * right + Mathf.Sin(a1) * up) * pipeRadius,
                        pos + (Mathf.Cos(a2) * right + Mathf.Sin(a2) * up) * pipeRadius);
                }
            }
        }
    }
    
    void OnDestroy()
    {
        if (deformedMeshes != null)
            foreach (Mesh mesh in deformedMeshes)
                if (mesh != null) Destroy(mesh);
    }
}