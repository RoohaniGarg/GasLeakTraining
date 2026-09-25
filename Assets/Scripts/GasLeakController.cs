using System.Collections;
using UnityEngine;

/// <summary>
/// Phase 4: Controls the gas leak - particle cloud, 3D hiss sound and the
/// blinking warning marker. Lives on GasPipe/LeakPoint/GasLeakFX.
///   StartLeak(false) = faint leak (hazard hunt: hiss + light gas, no marker)
///   StartLeak(true)  = major leak (big cloud, loud hiss, blinking marker)
///   StopLeak()       = fades everything out
/// </summary>
[RequireComponent(typeof(ParticleSystem))]
[RequireComponent(typeof(AudioSource))]
public class GasLeakController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Warning diamond above the pipe (shown only for a major leak)")]
    public GameObject hazardMarker;

    [Header("Minor leak (hazard hunt)")]
    public float minorRate = 6f;
    public float minorVolume = 0.35f;

    [Header("Major leak (emergency)")]
    public float majorRate = 55f;
    public float majorVolume = 1f;

    [Header("Timing")]
    public float fadeOutTime = 3f;
    public float blinkInterval = 0.4f;

    public bool IsLeaking { get; private set; }
    public bool IsMajor { get; private set; }

    private ParticleSystem ps;
    private AudioSource audioSrc;
    private Transform cam;
    private Renderer[] markerRenderers;
    private float blinkTimer;
    private bool markerVisible = true;
    private Coroutine fadeRoutine;

    void Awake()
    {
        ps = GetComponent<ParticleSystem>();
        audioSrc = GetComponent<AudioSource>();

        var main = ps.main;
        main.playOnAwake = false;
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        audioSrc.playOnAwake = false;
        audioSrc.loop = true;
        if (audioSrc.clip == null)
            audioSrc.clip = CreateHissClip();

        if (hazardMarker != null)
        {
            markerRenderers = hazardMarker.GetComponentsInChildren<Renderer>(true);
            hazardMarker.SetActive(false);
        }
    }

    void Start()
    {
        if (Camera.main != null)
        {
            cam = Camera.main.transform;
            // 3D sound needs a listener on the camera
            if (FindAnyObjectByType<AudioListener>() == null)
                Camera.main.gameObject.AddComponent<AudioListener>();
        }
    }

    public void StartLeak(bool major)
    {
        if (fadeRoutine != null)
        {
            StopCoroutine(fadeRoutine);
            fadeRoutine = null;
        }

        IsLeaking = true;
        IsMajor = major;

        var emission = ps.emission;
        emission.rateOverTime = major ? majorRate : minorRate;
        if (!ps.isPlaying) ps.Play();

        audioSrc.volume = major ? majorVolume : minorVolume;
        if (!audioSrc.isPlaying) audioSrc.Play();

        if (hazardMarker != null) hazardMarker.SetActive(major);

        Debug.Log("GasLeakController: " + (major ? "MAJOR" : "minor") + " leak started");
    }

    public void StopLeak()
    {
        if (!IsLeaking) return;
        IsLeaking = false;
        IsMajor = false;
        if (hazardMarker != null) hazardMarker.SetActive(false);
        fadeRoutine = StartCoroutine(FadeOut());
        Debug.Log("GasLeakController: leak stopping");
    }

    IEnumerator FadeOut()
    {
        var emission = ps.emission;
        float startRate = emission.rateOverTime.constant;
        float startVolume = audioSrc.volume;
        float t = 0f;
        while (t < fadeOutTime)
        {
            t += Time.deltaTime;
            float k = 1f - Mathf.Clamp01(t / fadeOutTime);
            emission.rateOverTime = startRate * k;
            audioSrc.volume = startVolume * k;
            yield return null;
        }
        ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        audioSrc.Stop();
        fadeRoutine = null;
    }

    void Update()
    {
        if (hazardMarker == null || !hazardMarker.activeSelf) return;

        if (cam == null && Camera.main != null) cam = Camera.main.transform;

        // Always face the camera (turn around the vertical axis only)
        if (cam != null)
        {
            Vector3 away = hazardMarker.transform.position - cam.position;
            away.y = 0f;
            if (away.sqrMagnitude > 0.0001f)
                hazardMarker.transform.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
        }

        // Blink
        blinkTimer += Time.deltaTime;
        if (blinkTimer >= blinkInterval)
        {
            blinkTimer = 0f;
            markerVisible = !markerVisible;
            foreach (var r in markerRenderers)
                if (r != null) r.enabled = markerVisible;
        }
    }

    /// <summary>
    /// Makes a looping hiss sound in code (high-passed noise with a slight flutter),
    /// so no audio file needs to be downloaded.
    /// </summary>
    static AudioClip CreateHissClip()
    {
        const int sampleRate = 22050;
        int total = sampleRate * 2;
        int fade = sampleRate / 10;
        float[] data = new float[total];
        var rng = new System.Random(7);
        float low = 0f;

        for (int i = 0; i < total; i++)
        {
            float white = (float)(rng.NextDouble() * 2.0 - 1.0);
            low = 0.97f * low + 0.03f * white;            // low-passed copy
            float hiss = white - low;                      // keep the high "sss" part
            float flutter = 1f + 0.15f * Mathf.Sin(2f * Mathf.PI * 5f * i / sampleRate);
            data[i] = hiss * 0.5f * flutter;
        }

        // Cross-fade the end into the start so the loop has no click
        int length = total - fade;
        float[] loop = new float[length];
        System.Array.Copy(data, loop, length);
        for (int i = 0; i < fade; i++)
        {
            float k = (float)i / fade;
            loop[i] = data[i] * k + data[length + i] * (1f - k);
        }

        var clip = AudioClip.Create("GasHiss", length, 1, sampleRate, false);
        clip.SetData(loop, 0);
        return clip;
    }
}
