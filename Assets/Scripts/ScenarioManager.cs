using System.Collections;
using UnityEngine;

/// <summary>
/// All the stages of the gas leak training, in order.
/// (Phase 7 will replace these with the v3 states.)
/// </summary>
public enum TrainingState
{
    Placement,  // User is scanning and tapping to place
    Briefing,   // Environment has appeared, short pause before the leak
    GasLeak,    // Gas leak has started
    Step1,
    Step2,
    Step3,
    Step4,
    Results     // Final score screen
}

/// <summary>
/// Watches for placement, spawns the training environment at the tapped spot
/// (facing the user), and runs the training state machine.
/// Lives on its own always-active GameObject.
/// </summary>
public class ScenarioManager : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The ARPlacementManager script (on XR Origin)")]
    [SerializeField] private ARPlacementManager placementManager;

    [Tooltip("The TrainingEnvironmentRoot GameObject (starts disabled)")]
    [SerializeField] private GameObject trainingEnvironmentRoot;

    [Tooltip("The AR camera. Leave empty to use Main Camera automatically.")]
    [SerializeField] private Transform arCamera;

    [Header("Phase 4 - hazards (auto-assigned by SIH > Build Realistic Environment)")]
    [SerializeField] private GasLeakController gasLeak;
    [SerializeField] private HazardZoneVisual hazardZones;

    [Header("Timing")]
    [Tooltip("Seconds after the environment appears before the gas leak starts")]
    [SerializeField] private float gasLeakDelay = 3f;

    [Tooltip("How long the environment takes to grow into view (seconds)")]
    [SerializeField] private float spawnAnimationDuration = 0.6f;

    // Other scripts can read the current stage
    public TrainingState CurrentState { get; private set; } = TrainingState.Placement;

    public GasLeakController GasLeak => gasLeak;
    public HazardZoneVisual HazardZones => hazardZones;

    private bool environmentSpawned = false;
    private Vector3 environmentOriginalScale = Vector3.one;

    void Start()
    {
        if (placementManager == null)
            Debug.LogError("ScenarioManager: Placement Manager is not assigned in the Inspector!");

        if (trainingEnvironmentRoot == null)
        {
            Debug.LogError("ScenarioManager: Training Environment Root is not assigned in the Inspector!");
            return;
        }

        if (arCamera == null && Camera.main != null)
            arCamera = Camera.main.transform;

        // Remember the root's scale, then hide it until placement
        environmentOriginalScale = trainingEnvironmentRoot.transform.localScale;
        trainingEnvironmentRoot.SetActive(false);

        SetState(TrainingState.Placement);
    }

    void Update()
    {
        if (environmentSpawned) return;
        if (placementManager == null || trainingEnvironmentRoot == null) return;

        if (placementManager.isPlacementComplete)
            SpawnEnvironment();
    }

    void SpawnEnvironment()
    {
        environmentSpawned = true;

        Vector3 spawnPosition = placementManager.placedPosition;
        Quaternion spawnRotation = placementManager.placedRotation;

        // Turn the environment so its front side (worker + shutoff) faces the user
        if (arCamera != null)
        {
            Vector3 directionAwayFromCamera = spawnPosition - arCamera.position;
            directionAwayFromCamera.y = 0f;

            if (directionAwayFromCamera.sqrMagnitude > 0.0001f)
                spawnRotation = Quaternion.LookRotation(directionAwayFromCamera.normalized, Vector3.up);
        }

        Transform root = trainingEnvironmentRoot.transform;
        root.SetPositionAndRotation(spawnPosition, spawnRotation);
        root.localScale = Vector3.zero;
        trainingEnvironmentRoot.SetActive(true);

        Debug.Log("ScenarioManager: Environment spawned at " + spawnPosition);

        StartCoroutine(SpawnSequence());
    }

    IEnumerator SpawnSequence()
    {
        Transform root = trainingEnvironmentRoot.transform;

        // Smooth "grow in" animation
        float elapsed = 0f;
        while (elapsed < spawnAnimationDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / spawnAnimationDuration);
            float eased = 1f - Mathf.Pow(1f - t, 3f); // ease-out
            root.localScale = environmentOriginalScale * eased;
            yield return null;
        }
        root.localScale = environmentOriginalScale;

        SetState(TrainingState.Briefing);

        // Short pause, then the leak begins
        yield return new WaitForSeconds(gasLeakDelay);
        SetState(TrainingState.GasLeak);
    }

    /// <summary>
    /// Changes the training stage. Later phases hook their logic in here.
    /// </summary>
    public void SetState(TrainingState newState)
    {
        CurrentState = newState;
        Debug.Log("ScenarioManager: State changed to " + newState);

        // Phase 4 test hooks (Phase 7 replaces these with the full v3 flow)
        switch (newState)
        {
            case TrainingState.Briefing:
                if (gasLeak != null) gasLeak.StartLeak(false);   // faint hiss + wisps
                break;
            case TrainingState.GasLeak:
                if (gasLeak != null) gasLeak.StartLeak(true);    // big cloud + marker
                if (hazardZones != null) hazardZones.Show();
                break;
        }
    }

    /// <summary>
    /// Moves to the next training stage in order.
    /// </summary>
    public void AdvanceToNextState()
    {
        if (CurrentState == TrainingState.Results) return;
        SetState((TrainingState)((int)CurrentState + 1));
    }
}
