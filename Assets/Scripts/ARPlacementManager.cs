using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Manages AR plane detection and the placement reticle.
/// Phase 2: shows reticle on detected surfaces, locks position on tap.
/// Phase 3.5 fix: places at the exact surface point being aimed at (not the
/// smoothed reticle, which lags behind), and only on horizontal floors/tables.
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
    private Pose lastHitPose;          // exact point the camera is aiming at
    private bool reticleSnapped = false; // snap instantly the first time a surface is found

    // Pulsing animation
    private Vector3 reticleBaseScale;
    private float pulseTimer = 0f;

    // Public - other scripts read this after placement
    [HideInInspector] public Vector3 placedPosition;
    [HideInInspector] public Quaternion placedRotation;
    [HideInInspector] public bool isPlacementComplete = false;

    void Start()
    {
        reticleBaseScale = reticle.transform.localScale;
        reticle.SetActive(false);
        Debug.Log("ARPlacementManager: Ready. Point camera at a flat surface.");
    }

    void Update()
    {
        if (placementDone) return;
        UpdateReticlePosition();
        AnimateReticlePulse();
        CheckForTap();
    }

    void UpdateReticlePosition()
    {
        Vector2 screenCenter = new Vector2(Screen.width / 2f, Screen.height / 2f);

        if (arRaycastManager.Raycast(screenCenter, hits, TrackableType.PlaneWithinPolygon)
            && TryGetFloorHit(out Pose hitPose))
        {
            lastHitPose = hitPose;
            hasValidSurface = true;
            reticle.SetActive(true);

            if (!reticleSnapped)
            {
                // First time we see a surface: jump straight there (no slide from old spot)
                reticle.transform.SetPositionAndRotation(hitPose.position, hitPose.rotation);
                reticleSnapped = true;
            }
            else
            {
                reticle.transform.position = Vector3.Lerp(
                    reticle.transform.position, hitPose.position, Time.deltaTime * reticleMoveSpeed);
                reticle.transform.rotation = Quaternion.Lerp(
                    reticle.transform.rotation, hitPose.rotation, Time.deltaTime * reticleMoveSpeed);
            }
        }
        else
        {
            reticle.SetActive(false);
            hasValidSurface = false;
            reticleSnapped = false;
        }
    }

    /// <summary>
    /// Picks the first hit that is on an upward-facing horizontal plane
    /// (ignores walls, bed sides, etc.).
    /// </summary>
    bool TryGetFloorHit(out Pose pose)
    {
        foreach (var hit in hits)
        {
            ARPlane plane = arPlaneManager.GetPlane(hit.trackableId);
            if (plane == null || plane.alignment == PlaneAlignment.HorizontalUp)
            {
                pose = hit.pose;
                return true;
            }
        }
        pose = default;
        return false;
    }

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

    void CheckForTap()
    {
        if (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)
        {
            if (hasValidSurface)
                ConfirmPlacement();
            else
                Debug.Log("ARPlacementManager: No surface detected yet. Keep scanning.");
        }
    }

    void ConfirmPlacement()
    {
        // Use the exact aimed point, not the smoothed reticle (which can lag behind)
        placedPosition = lastHitPose.position;
        placedRotation = lastHitPose.rotation;
        reticle.SetActive(false);
        placementDone = true;
        isPlacementComplete = true;

        foreach (var plane in arPlaneManager.trackables)
            plane.gameObject.SetActive(false);
        arPlaneManager.enabled = false;

        Debug.Log("ARPlacementManager: Placement confirmed at " + placedPosition);
    }
}
