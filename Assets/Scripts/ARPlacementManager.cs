using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Manages AR plane detection and the placement reticle.
/// Phase 2: shows reticle on detected surfaces, locks position on tap.
/// Phase 3.5 fix: places at the exact surface point being aimed at (not the
/// smoothed reticle, which lags behind), and only on horizontal floors/tables.
/// Phase 6: ignores taps on UI buttons, adds ResetPlacement() and the
/// PlaceInFrontOfCamera() fallback for floors ARCore can't detect.
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
    private float reticleAppear = 0f;   // 0..1 scale-in
    private float lostTimer = 99f;      // seconds since the surface was last seen

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
            lostTimer = 0f;
            if (!reticle.activeSelf)
            {
                reticle.SetActive(true);
                reticleAppear = 0f;
            }

            if (!reticleSnapped)
            {
                // First time we see a surface: jump straight there (no slide from old spot)
                reticle.transform.SetPositionAndRotation(hitPose.position, hitPose.rotation);
                reticleSnapped = true;
            }
            else
            {
                float k = Easing.Damp(reticleMoveSpeed, Time.deltaTime);
                reticle.transform.position = Vector3.Lerp(reticle.transform.position, hitPose.position, k);
                reticle.transform.rotation = Quaternion.Slerp(reticle.transform.rotation, hitPose.rotation, k);
            }
        }
        else
        {
            hasValidSurface = false;
            // Keep the reticle on screen briefly so a single lost frame doesn't make it flicker
            lostTimer += Time.deltaTime;
            if (lostTimer > 0.3f) reticleSnapped = false;
        }

        // Fade the reticle in/out by scale
        float targetAppear = lostTimer > 0.3f ? 0f : 1f;
        reticleAppear = Mathf.MoveTowards(reticleAppear, targetAppear, Time.deltaTime / (targetAppear > 0f ? 0.3f : 0.2f));
        if (reticleAppear <= 0f && targetAppear <= 0f && reticle.activeSelf) reticle.SetActive(false);
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
        float pulse = (1f + Mathf.Sin(pulseTimer) * 0.08f) * Easing.OutBack(reticleAppear, 2f);
        reticle.transform.localScale = new Vector3(
            reticleBaseScale.x * pulse,
            reticleBaseScale.y,
            reticleBaseScale.z * pulse
        );
    }

    /// <summary>After placing: the ring squeezes in and disappears where the site lands.</summary>
    System.Collections.IEnumerator ReticleOut()
    {
        if (!reticle.activeSelf) yield break;
        reticle.transform.position = placedPosition;
        Vector3 s0 = reticle.transform.localScale;
        float t = 0f;
        while (t < 0.25f)
        {
            t += Time.deltaTime;
            float k = Easing.InCubic(t / 0.25f);
            reticle.transform.localScale = new Vector3(s0.x * (1f - k), s0.y, s0.z * (1f - k));
            yield return null;
        }
        reticle.SetActive(false);
        reticle.transform.localScale = reticleBaseScale;
    }

    void CheckForTap()
    {
        if (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)
        {
            // Taps on on-screen buttons are not placement taps
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(Input.GetTouch(0).fingerId))
                return;

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
        StartCoroutine(ReticleOut());
        placementDone = true;
        isPlacementComplete = true;

        foreach (var plane in arPlaneManager.trackables)
            plane.gameObject.SetActive(false);
        arPlaneManager.enabled = false;

        Debug.Log("ARPlacementManager: Placement confirmed at " + placedPosition);
    }

    /// <summary>True while the reticle is on a usable floor.</summary>
    public bool HasSurface => hasValidSurface;

    /// <summary>Phase 6: go back to scanning so the scene can be placed somewhere else.</summary>
    public void ResetPlacement()
    {
        placementDone = false;
        isPlacementComplete = false;
        hasValidSurface = false;
        reticleSnapped = false;
        lostTimer = 99f;
        reticleAppear = 0f;
        StopAllCoroutines();
        reticle.transform.localScale = reticleBaseScale;
        reticle.SetActive(false);
        arPlaneManager.enabled = true;
        foreach (var plane in arPlaneManager.trackables)
            plane.gameObject.SetActive(true);
        Debug.Log("ARPlacementManager: Placement reset - scanning again.");
    }

    /// <summary>
    /// Phase 6 fallback: place the scene about 'distance' metres in front of the
    /// camera on the floor, even if no plane was found. Floor height = the lowest
    /// detected horizontal plane, or camera height minus 'assumedCameraHeight'.
    /// </summary>
    public void PlaceInFrontOfCamera(float distance = 1.0f, float assumedCameraHeight = 1.3f)
    {
        Transform cam = Camera.main != null ? Camera.main.transform : null;
        if (cam == null) return;

        Vector3 forward = cam.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
        forward.Normalize();

        float floorY = cam.position.y - assumedCameraHeight;
        bool foundPlane = false;
        foreach (var plane in arPlaneManager.trackables)
        {
            if (plane.alignment != PlaneAlignment.HorizontalUp) continue;
            float y = plane.transform.position.y;
            if (y > cam.position.y - 0.3f) continue;          // ignore tables above ~waist
            if (!foundPlane || y < floorY) { floorY = y; foundPlane = true; }
        }

        lastHitPose = new Pose(cam.position + forward * distance, Quaternion.LookRotation(forward, Vector3.up));
        lastHitPose.position.y = floorY;
        ConfirmPlacement();
    }
}
