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

    // One entry per material slot, so multi-material models (Ramesh) keep their own colours
    private readonly List<Renderer> renderers = new List<Renderer>();
    private readonly List<int> slots = new List<int>();
    private readonly List<Color> baseColors = new List<Color>();
    private MaterialPropertyBlock block;
    private Coroutine flashRoutine;
    private Vector3 baseScale;

    void Awake()
    {
        block = new MaterialPropertyBlock();
        baseScale = transform.localScale;

        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;   // no ropes / particles
            if (r.GetComponent<TMP_Text>() != null) continue;          // skip sign text
            if (IsUnder(r.transform, "HazardMarker")) continue;         // skip warning diamond
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null || !mats[i].HasProperty("_BaseColor")) continue;
                renderers.Add(r);
                slots.Add(i);
                baseColors.Add(mats[i].GetColor("_BaseColor"));
            }
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
        // Glow: quick rise, soft fade. Shape: springy squash-and-stretch from the base
        // (pivots sit on the floor), dying out smoothly.
        float duration = Mathf.Max(highlightTime, 0.75f);
        const float rise = 0.07f;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float glow = t < rise ? Easing.OutCubic(t / rise) : 1f - Easing.InOutSine((t - rise) / (duration - rise));
            ApplyTint(glow * 0.7f);

            float wobble = Mathf.Exp(-t * 7f) * Mathf.Sin(t * 26f);
            float up = 1f + popAmount * 1.6f * wobble;
            float side = 1f - popAmount * 0.6f * wobble;
            transform.localScale = Vector3.Scale(baseScale, new Vector3(side, up, side));
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
            renderers[i].GetPropertyBlock(block, slots[i]);
            block.SetColor("_BaseColor", Color.Lerp(baseColors[i], highlightColor, amount));
            renderers[i].SetPropertyBlock(block, slots[i]);
        }
    }

    void ClearTint()
    {
        for (int i = 0; i < renderers.Count; i++)
        {
            if (renderers[i] == null) continue;
            block.Clear();
            renderers[i].SetPropertyBlock(block, slots[i]);
        }
    }

    static bool IsUnder(Transform t, string name)
    {
        for (var p = t; p != null; p = p.parent)
            if (p.name == name) return true;
        return false;
    }
}
