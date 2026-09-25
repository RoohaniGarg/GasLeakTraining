using System.Collections;
using UnityEngine;

/// <summary>
/// Phase 4: Red / amber danger zones painted on the floor.
/// Hidden at start; Show() fades them in, Hide() fades them out.
/// Lives on TrainingEnvironmentRoot/HazardZones.
/// </summary>
public class HazardZoneVisual : MonoBehaviour
{
    public float fadeTime = 1f;
    [Tooltip("Gentle pulse of the zones while shown (0 = off)")]
    public float pulseAmount = 0.25f;
    public float pulseSpeed = 2.5f;

    public bool IsShown { get; private set; }

    private Renderer[] renderers;
    private Material[] materials;
    private Color[] targetColors;
    private float visibility;   // 0..1 from fading
    private bool fading;

    void Awake()
    {
        renderers = GetComponentsInChildren<Renderer>(true);
        materials = new Material[renderers.Length];
        targetColors = new Color[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            materials[i] = renderers[i].material; // own copy so we can fade it
            targetColors[i] = materials[i].GetColor("_BaseColor");
            renderers[i].enabled = false;
        }
    }

    public void Show()
    {
        if (IsShown) return;
        IsShown = true;
        StopAllCoroutines();
        StartCoroutine(Fade(visibility, 1f));
    }

    public void Hide()
    {
        if (!IsShown) return;
        IsShown = false;
        StopAllCoroutines();
        StartCoroutine(Fade(visibility, 0f));
    }

    IEnumerator Fade(float from, float to)
    {
        fading = true;
        foreach (var r in renderers) r.enabled = true;
        float t = 0f;
        while (t < fadeTime)
        {
            t += Time.deltaTime;
            visibility = Mathf.Lerp(from, to, Mathf.Clamp01(t / fadeTime));
            Apply(visibility);
            yield return null;
        }
        visibility = to;
        Apply(visibility);
        if (to <= 0f)
            foreach (var r in renderers) r.enabled = false;
        fading = false;
    }

    void Update()
    {
        if (!IsShown || fading || pulseAmount <= 0f) return;
        float pulse = 1f - pulseAmount * 0.5f * (1f + Mathf.Sin(Time.time * pulseSpeed));
        Apply(pulse);
    }

    void Apply(float k)
    {
        for (int i = 0; i < materials.Length; i++)
        {
            Color c = targetColors[i];
            c.a *= k;
            materials[i].SetColor("_BaseColor", c);
        }
    }
}
