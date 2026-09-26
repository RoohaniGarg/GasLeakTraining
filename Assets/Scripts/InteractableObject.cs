using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Which object in the scene this is. ScenarioManager (Phase 7) decides
/// whether a tap on it is right or wrong for the current step.
/// </summary>
public enum InteractableId
{
    GasSource,        // GasPipe (leaking flange)
    GasCylinder,      // LPG cylinder
    ConfinedSpace,    // manhole opening
    RetrievalTripod,  // rescue tripod + winch
    EmergencyShutoff, // red handwheel valve
    Worker            // buddy "Ramesh"
}

/// <summary>
/// Phase 5: Put on each tappable object (on the object that has the collider).
/// TapInputManager calls NotifyTapped(); this flashes the object and raises OnTapped.
/// </summary>
public class InteractableObject : MonoBehaviour
{
    [Header("Identity")]
    public InteractableId id;
    public string displayName = "Object";

    [Header("Tap feedback")]
    public Color highlightColor = new Color(1f, 0.92f, 0.25f);
    public float highlightTime = 0.6f;
    [Tooltip("How much the object 'pops' when tapped (0.06 = 6%)")]
    public float popAmount = 0.06f;

    [Tooltip("If false, taps on this object are ignored")]
    public bool interactable = true;

    /// <summary>Raised whenever any interactable is tapped.</summary>
    public static event Action<InteractableObject> OnTapped;

    private readonly List<Renderer> renderers = new List<Renderer>();
    private readonly List<Color> baseColors = new List<Color>();
    private MaterialPropertyBlock block;
    private Coroutine flashRoutine;
    private Vector3 baseScale;

    void Awake()
    {
        block = new MaterialPropertyBlock();
        baseScale = transform.localScale;

        foreach (var r in GetComponentsInChildren<MeshRenderer>(true))
        {
            if (r.GetComponent<TMP_Text>() != null) continue;          // skip sign text
            if (IsUnder(r.transform, "HazardMarker")) continue;         // skip warning diamond
            var mat = r.sharedMaterial;
            if (mat == null || !mat.HasProperty("_BaseColor")) continue;
            renderers.Add(r);
            baseColors.Add(mat.GetColor("_BaseColor"));
        }
    }

    public void NotifyTapped()
    {
        if (!interactable) return;
        Flash();
        Debug.Log("InteractableObject: tapped " + displayName + " (" + id + ")");
        OnTapped?.Invoke(this);
    }

    /// <summary>Short yellow glow + small pop. Can also be called by hint systems.</summary>
    public void Flash()
    {
        if (flashRoutine != null) StopCoroutine(flashRoutine);
        flashRoutine = StartCoroutine(FlashRoutine());
    }

    IEnumerator FlashRoutine()
    {
        float t = 0f;
        while (t < highlightTime)
        {
            t += Time.deltaTime;
            float k = Mathf.Sin(Mathf.Clamp01(t / highlightTime) * Mathf.PI); // 0 -> 1 -> 0
            ApplyTint(k * 0.7f);
            transform.localScale = baseScale * (1f + popAmount * k);
            yield return null;
        }
        ClearTint();
        transform.localScale = baseScale;
        flashRoutine = null;
    }

    void ApplyTint(float amount)
    {
        for (int i = 0; i < renderers.Count; i++)
        {
            if (renderers[i] == null) continue;
            renderers[i].GetPropertyBlock(block);
            block.SetColor("_BaseColor", Color.Lerp(baseColors[i], highlightColor, amount));
            renderers[i].SetPropertyBlock(block);
        }
    }

    void ClearTint()
    {
        foreach (var r in renderers)
            if (r != null) r.SetPropertyBlock(null);
    }

    static bool IsUnder(Transform t, string name)
    {
        for (var p = t; p != null; p = p.parent)
            if (p.name == name) return true;
        return false;
    }
}
