using System.Collections;
using UnityEngine;

/// <summary>
/// Phase 4: Red / amber danger zones painted on the floor.
/// Hidden at start; Show() fades them in, Hide() fades them out.
/// Lives on TrainingEnvironmentRoot/HazardZones.
///
/// Phase 9: the fade is eased and blends into the pulse (no jump when the pulse
/// starts), the discs spread out from their centre, and each red zone sends out
/// a ripple ring when the zones appear.
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
    private Vector3[] baseScales;
    private float visibility;   // 0..1 from fading
    private bool anyVisible;

    void Awake()
    {
        renderers = GetComponentsInChildren<Renderer>(true);
        materials = new Material[renderers.Length];
        targetColors = new Color[renderers.Length];
        baseScales = new Vector3[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            materials[i] = renderers[i].material; // own copy so we can fade it
            targetColors[i] = materials[i].GetColor("_BaseColor");
            baseScales[i] = renderers[i].transform.localScale;
            renderers[i].enabled = false;
        }
    }

    public void Show()
    {
        if (IsShown) return;
        IsShown = true;
        StopAllCoroutines();
        StartCoroutine(Fade(visibility, 1f));
        StartCoroutine(Ripples());
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
        SetEnabled(true);
        float t = 0f;
        while (t < fadeTime)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / fadeTime);
            visibility = Mathf.Lerp(from, to, to > from ? Easing.OutCubic(k) : Easing.InOutSine(k));

            // Discs spread out when appearing, draw in a little when disappearing
            float s = to > from ? Mathf.LerpUnclamped(0.55f, 1f, Easing.OutBack(k, 1.2f)) : Mathf.Lerp(1f, 0.85f, Easing.InQuad(k));
            for (int i = 0; i < renderers.Length; i++)
            {
                Vector3 b = baseScales[i];
                renderers[i].transform.localScale = new Vector3(b.x * s, b.y, b.z * s);
            }
            yield return null;
        }
        visibility = to;
        for (int i = 0; i < renderers.Length; i++) renderers[i].transform.localScale = baseScales[i];
        if (to <= 0f) SetEnabled(false);
    }

    IEnumerator Ripples()
    {
        yield return new WaitForSeconds(0.15f);
        var fx = EffectsManager.Instance;
        if (fx == null) yield break;
        for (int i = 0; i < renderers.Length; i++)
        {
            if (!renderers[i].name.StartsWith("Red_")) continue;
            float radius = 0.5f * baseScales[i].x * 1.3f;   // cylinder disc: radius = half its scale
            fx.Ring(renderers[i].transform.position, new Color(1f, 0.25f, 0.2f, 0.8f), radius, 1.2f, 2, 0.3f);
        }
    }

    void Update()
    {
        if (!anyVisible) return;
        float pulse = pulseAmount > 0f ? 1f - pulseAmount * 0.5f * (1f + Mathf.Sin(Time.time * pulseSpeed)) : 1f;
        Apply(visibility * pulse);
    }

    void SetEnabled(bool on)
    {
        anyVisible = on;
        foreach (var r in renderers) r.enabled = on;
        if (!on) Apply(0f);
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
