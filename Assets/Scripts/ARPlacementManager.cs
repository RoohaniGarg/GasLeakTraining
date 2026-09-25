using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
/// <summary>
/// Manages AR plane detection and placement reticle.
/// Phase 2: Shows reticle on detected surfaces, locks position on tap.
/// Phase 3 will extend this to spawn the training environment.
/// </summary>
public class ARPlacementManager : MonoBehaviour
{
    [Header("AR Components")]
    [Tooltip("The ARRaycastManager on XR Origin")]
    [SerializeField] private ARRaycastManager arRaycastManager;
    [Tooltip("The ARPlaneManager on XR Origin")]
    [SerializeField] private ARPlaneManager arPlaneManager;
    [Header("Reticle")]
    [Tooltip("The PlacementReticle GameObject in the scene")]
    [SerializeField] private GameObject reticle;
    [Header("Settings")]
    [Tooltip("Speed at which reticle moves to new position")]
    [SerializeField] private float reticleMoveSpeed = 10f;
    // Internal state
    private List<ARRaycastHit> hits = new List<ARRaycastHit>();
    private bool hasValidSurface = false;
    private bool placementDone = false;
    // Pulsing animation
    private Vector3 reticleBaseScale;
    private float pulseTimer = 0f;
    // Public — other scripts read this after placement
    [HideInInspector] public Vector3 placedPosition;
    [HideInInspector] public Quaternion placedRotation;
    [HideInInspector] public bool isPlacementComplete = false;
    void Start()
    {
        // Store the original scale for pulse animation
        reticleBaseScale = reticle.transform.localScale;
        // Hide reticle until a surface is detected
        reticle.SetActive(false);
        Debug.Log("ARPlacementManager: Ready. Point camera at a flat surface.");
    }
    void Update()
    {
        // Stop updating after user has placed
        if (placementDone) return;
        UpdateReticlePosition();
        AnimateReticlePulse();
        CheckForTap();
    }
    /// <summary>
    /// Casts a ray from screen center onto AR planes.
    /// Moves reticle to the hit point if found.
    /// </summary>
    void UpdateReticlePosition()
    {
        // Cast ray from center of screen
        Vector2 screenCenter = new Vector2(Screen.width / 2f, Screen.height / 2f);
        if (arRaycastManager.Raycast(screenCenter, hits, TrackableType.PlaneWithinPolygon))
        {
            // Surface found — show and move reticle
            Pose hitPose = hits[0].pose;
            reticle.SetActive(true);
            hasValidSurface = true;
            // Smoothly move reticle to new position
            reticle.transform.position = Vector3.Lerp(
                reticle.transform.position,
                hitPose.position,
                Time.deltaTime * reticleMoveSpeed
            );
            // Align reticle rotation with surface normal
            reticle.transform.rotation = Quaternion.Lerp(
                reticle.transform.rotation,
                hitPose.rotation,
                Time.deltaTime * reticleMoveSpeed
            );
        }
        else
        {
            // No surface — hide reticle
            reticle.SetActive(false);
            hasValidSurface = false;
        }
    }
    /// <summary>
    /// Makes the reticle pulse in and out to look alive.
    /// </summary>
    void AnimateReticlePulse()
    {
        if (!reticle.activeSelf) return;
        pulseTimer += Time.deltaTime * 2f;
        float pulse = 1f + Mathf.Sin(pulseTimer) * 0.1f;
        reticle.transform.localScale = new Vector3(
            reticleBaseScale.x * pulse,
            reticleBaseScale.y,
            reticleBaseScale.z * pulse
        );
    }
    /// <summary>
    /// Detects screen tap. If valid surface found, locks placement.
    /// </summary>
    void CheckForTap()
    {
        if (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)
        {
            if (hasValidSurface)
            {
                ConfirmPlacement();
            }
            else
            {
                Debug.Log("ARPlacementManager: No surface detected yet. Keep scanning.");
            }
        }
    }
    /// <summary>
    /// Locks the placement position and disables plane scanning.
    /// Phase 3 ScenarioManager will listen for isPlacementComplete.
    /// </summary>
    void ConfirmPlacement()
    {
        // Record the confirmed position and rotation
        placedPosition = reticle.transform.position;
        placedRotation = reticle.transform.rotation;
        // Hide reticle
        reticle.SetActive(false);
        placementDone = true;
        isPlacementComplete = true;
        // Disable plane visuals and detection to save performance
        foreach (var plane in arPlaneManager.trackables)
        {
            plane.gameObject.SetActive(false);
        }
        arPlaneManager.enabled = false;
        Debug.Log("ARPlacementManager: Placement confirmed at " + placedPosition);
        // Phase 3: ScenarioManager will detect isPlacementComplete and spawn environment
    }
}