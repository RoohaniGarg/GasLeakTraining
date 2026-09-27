using System.Collections;
using UnityEngine;

/// <summary>
/// Phase 9: "After effects" - short visual and sound feedback that follows an action.
///   Ring()   - a soft ring that expands on the floor and fades (tap result, placement, alarm)
///   Siren()  - wailing siren after RAISE ALARM
///   Chime() / Buzz() / Tap() / Pop() - small UI sounds
/// Everything is generated in code (no downloads). ScenarioManager adds it at start.
/// The ring material is cloned from a transparent material that is already in the
/// scene (the hazard-zone material), so it is always included in the build.
/// </summary>
public class EffectsManager : MonoBehaviour
{
    public static EffectsManager Instance { get; private set; }

    [Range(0f, 1f)] public float volume = 0.5f;

    private Material ringTemplate;
    private Transform site;          // TrainingEnvironmentRoot: gives floor orientation + scale
    private Mesh ringMesh;
    private AudioSource sfx, siren;
    private AudioClip chimeClip, buzzClip, tapClip, popClip, sirenClip;
    private Coroutine sirenRoutine;

    void Awake()
    {
        Instance = this;
        sfx = gameObject.AddComponent<AudioSource>();
        sfx.playOnAwake = false;
        sfx.spatialBlend = 0f;

        siren = gameObject.AddComponent<AudioSource>();
        siren.playOnAwake = false;
        siren.loop = true;
        siren.spatialBlend = 0f;

        chimeClip = MakeChime();
        buzzClip = MakeBuzz();
        tapClip = MakeTap();
        popClip = MakePop();
        sirenClip = MakeSiren();
        siren.clip = sirenClip;
        ringMesh = BuildRingMesh(0.8f, 48);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void Init(Material transparentTemplate, Transform siteRoot)
    {
        ringTemplate = transparentTemplate;
        site = siteRoot;
    }

    // ================================================================ rings

    /// <summary>
    /// Expanding ring on the floor. 'radius' is in tabletop metres (scaled with the site).
    /// 'count' rings follow each other 'gap' seconds apart.
    /// </summary>
    public void Ring(Vector3 worldPos, Color color, float radius = 0.15f, float duration = 0.9f, int count = 1, float gap = 0.18f)
    {
        if (ringTemplate == null) return;
        for (int i = 0; i < count; i++)
            StartCoroutine(RingRoutine(worldPos, color, radius, duration, i * gap));
    }

    IEnumerator RingRoutine(Vector3 pos, Color color, float radius, float duration, float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);

        float scale = site != null ? site.lossyScale.x : 1f;
        Quaternion rot = site != null ? site.rotation : Quaternion.identity;
        Vector3 up = rot * Vector3.up;

        var go = new GameObject("FX_Ring");
        go.transform.SetPositionAndRotation(pos + up * (0.006f * scale), rot);
        go.AddComponent<MeshFilter>().sharedMesh = ringMesh;
        var mr = go.AddComponent<MeshRenderer>();
        var mat = new Material(ringTemplate);
        mat.renderQueue = 3010;           // over the hazard zones
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / duration);
            float r = radius * scale * Mathf.Lerp(0.2f, 1f, Easing.OutCubic(k));
            go.transform.localScale = new Vector3(r, 1f, r);
            Color c = color;
            c.a = color.a * Mathf.Clamp01(k / 0.08f) * Mathf.Pow(1f - k, 1.6f);
            mat.SetColor("_BaseColor", c);
            yield return null;
        }
        Destroy(mat);
        Destroy(go);
    }

    /// <summary>Flat annulus (inner radius 'inner', outer 1), visible from both sides.</summary>
    static Mesh BuildRingMesh(float inner, int segments)
    {
        var v = new Vector3[segments * 2];
        for (int i = 0; i < segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments;
            float c = Mathf.Cos(a), s = Mathf.Sin(a);
            v[i * 2] = new Vector3(c * inner, 0f, s * inner);
            v[i * 2 + 1] = new Vector3(c, 0f, s);
        }
        var tris = new int[segments * 12];
        int n = 0;
        for (int i = 0; i < segments; i++)
        {
            int j = (i + 1) % segments;
            int a0 = i * 2, b0 = i * 2 + 1, a1 = j * 2, b1 = j * 2 + 1;
            // one side
            tris[n++] = a0; tris[n++] = b0; tris[n++] = a1;
            tris[n++] = a1; tris[n++] = b0; tris[n++] = b1;
            // other side
            tris[n++] = a0; tris[n++] = a1; tris[n++] = b0;
            tris[n++] = a1; tris[n++] = b1; tris[n++] = b0;
        }
        var mesh = new Mesh { name = "FX_RingMesh" };
        mesh.vertices = v;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // ================================================================ sounds

    public void Chime() => Play(chimeClip, 0.55f);
    public void Buzz() => Play(buzzClip, 0.5f);
    public void Tap() => Play(tapClip, 0.35f);
    public void Pop() => Play(popClip, 0.6f);

    void Play(AudioClip clip, float v)
    {
        if (clip != null && sfx != null) sfx.PlayOneShot(clip, v * volume);
    }

    /// <summary>Wailing siren for 'seconds', fading in and out.</summary>
    public void Siren(float seconds)
    {
        if (sirenRoutine != null) StopCoroutine(sirenRoutine);
        sirenRoutine = StartCoroutine(SirenRoutine(seconds));
    }

    IEnumerator SirenRoutine(float seconds)
    {
        float peak = 0.45f * volume;
        if (!siren.isPlaying) { siren.volume = 0f; siren.Play(); }
        float t = 0f;
        while (t < seconds)
        {
            t += Time.deltaTime;
            float fadeIn = Mathf.Clamp01(t / 0.25f);
            float fadeOut = Mathf.Clamp01((seconds - t) / 1.2f);
            siren.volume = peak * Mathf.Min(fadeIn, fadeOut);
            yield return null;
        }
        siren.Stop();
        sirenRoutine = null;
    }

    // ---- procedural clips (22 kHz mono)

    const int Rate = 22050;

    static AudioClip Build(string name, float seconds, System.Func<float, float> sample)
    {
        int n = Mathf.CeilToInt(seconds * Rate);
        var data = new float[n];
        for (int i = 0; i < n; i++) data[i] = sample((float)i / Rate);
        var clip = AudioClip.Create(name, n, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    static float Note(float t, float freq, float start, float decay)
    {
        float u = t - start;
        if (u < 0f) return 0f;
        float attack = Mathf.Clamp01(u / 0.004f);
        return Mathf.Sin(2f * Mathf.PI * freq * u) * Mathf.Exp(-u * decay) * attack;
    }

    // Two rising notes - "correct"
    static AudioClip MakeChime() => Build("FX_Chime", 0.45f, t =>
        0.45f * (Note(t, 880f, 0f, 16f) + Note(t, 1318.5f, 0.09f, 11f) + 0.25f * Note(t, 2637f, 0.09f, 20f)));

    // Low double buzz - "wrong"
    static AudioClip MakeBuzz() => Build("FX_Buzz", 0.36f, t =>
    {
        float f = 150f;
        float tone = Mathf.Sin(2f * Mathf.PI * f * t) + 0.5f * Mathf.Sin(4f * Mathf.PI * f * t) + 0.3f * Mathf.Sin(6f * Mathf.PI * f * t);
        float gate = (t < 0.13f || (t > 0.18f && t < 0.33f)) ? 1f : 0f;
        float edge = Mathf.Clamp01(Mathf.Min(t, Mathf.Abs(t - 0.13f), Mathf.Abs(t - 0.18f), Mathf.Abs(0.33f - t)) / 0.006f);
        return tone * 0.3f * gate * edge;
    });

    // Short soft click - button press
    static AudioClip MakeTap() => Build("FX_Tap", 0.05f, t =>
        Mathf.Sin(2f * Mathf.PI * 1700f * t) * Mathf.Exp(-t * 110f) * 0.5f);

    // Rising "bloop" - site placed
    static AudioClip MakePop() => Build("FX_Pop", 0.3f, t =>
    {
        const float d = 0.14f, f0 = 280f, f1 = 900f;
        float u = Mathf.Min(t, d);
        float phase = 2f * Mathf.PI * (f0 * u + (f1 - f0) * u * u / (2f * d)) + (t > d ? 2f * Mathf.PI * f1 * (t - d) : 0f);
        return Mathf.Sin(phase) * Mathf.Exp(-t * 11f) * Mathf.Clamp01(t / 0.005f) * 0.5f;
    });

    // Looping siren wail: 1.6 s sweep 450..1050 Hz (whole number of cycles, so it loops without a click)
    static AudioClip MakeSiren()
    {
        const float period = 1.6f, mid = 750f, swing = 300f;
        int n = Mathf.RoundToInt(period * Rate);
        var data = new float[n];
        double phase = 0.0;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            float f = mid - swing * Mathf.Cos(2f * Mathf.PI * t / period);
            phase += 2.0 * System.Math.PI * f / Rate;
            float p = (float)phase;
            data[i] = (Mathf.Sin(p) + 0.3f * Mathf.Sin(3f * p) + 0.15f * Mathf.Sin(5f * p)) * 0.35f;
        }
        var clip = AudioClip.Create("FX_Siren", n, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
