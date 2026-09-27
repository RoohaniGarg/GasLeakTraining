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
    private Coroutine fadeRoutine;
    private Vector3 markerBaseScale = Vector3.one, markerBasePos;
    private float markerAge;
    private bool markerLeaving;

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
            markerBaseScale = hazardMarker.transform.localScale;
            markerBasePos = hazardMarker.transform.localPosition;
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

        // Ramp up from whatever is running now (a burst for the major leak, a gentle start for the small one)
        var emission = ps.emission;
        float fromRate = ps.isPlaying ? emission.rateOverTime.constant : 0f;
        float fromVolume = audioSrc.isPlaying ? audioSrc.volume : 0f;
        if (!ps.isPlaying) { emission.rateOverTime = 0f; ps.Play(); }
        if (!audioSrc.isPlaying) { audioSrc.volume = 0f; audioSrc.Play(); }
        if (major) ps.Emit(25);   // sudden puff as the joint gives way
        fadeRoutine = StartCoroutine(Ramp(fromRate, major ? majorRate : minorRate,
                                          fromVolume, major ? majorVolume : minorVolume,
                                          major ? 1.2f : 1.5f, false));

        if (hazardMarker != null)
        {
            if (major && !hazardMarker.activeSelf) { markerAge = 0f; hazardMarker.SetActive(true); }
            else if (!major) hazardMarker.SetActive(false);
            markerLeaving = false;
        }

        Debug.Log("GasLeakController: " + (major ? "MAJOR" : "minor") + " leak started");
    }

    public void StopLeak()
    {
        if (!IsLeaking) return;
        IsLeaking = false;
        IsMajor = false;
        if (hazardMarker != null && hazardMarker.activeSelf) { markerLeaving = true; markerAge = 0f; }
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(Ramp(ps.emission.rateOverTime.constant, 0f, audioSrc.volume, 0f, fadeOutTime, true));
        Debug.Log("GasLeakController: leak stopping");
    }

    IEnumerator Ramp(float rate0, float rate1, float vol0, float vol1, float duration, bool stopAtEnd)
    {
        var emission = ps.emission;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = stopAtEnd ? Easing.InOutSine(t / duration) : Easing.OutCubic(t / duration);
            emission.rateOverTime = Mathf.Lerp(rate0, rate1, k);
            audioSrc.volume = Mathf.Lerp(vol0, vol1, k);
            yield return null;
        }
        emission.rateOverTime = rate1;
        audioSrc.volume = vol1;
        if (stopAtEnd)
        {
            ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            audioSrc.Stop();
        }
        fadeRoutine = null;
    }

    void Update()
    {
        if (hazardMarker == null || !hazardMarker.activeSelf) return;

        if (cam == null && Camera.main != null) cam = Camera.main.transform;

        // Turn smoothly to face the camera (around the vertical axis only)
        if (cam != null)
        {
            Vector3 away = hazardMarker.transform.position - cam.position;
            away.y = 0f;
            if (away.sqrMagnitude > 0.0001f)
                hazardMarker.transform.rotation = Quaternion.Slerp(hazardMarker.transform.rotation,
                    Quaternion.LookRotation(away.normalized, Vector3.up), Easing.Damp(10f, Time.deltaTime));
        }

        // Pop in, then a heartbeat pulse and a gentle float; shrink away when the leak stops
        markerAge += Time.deltaTime;
        float grow;
        if (markerLeaving)
        {
            grow = 1f - Easing.InQuad(markerAge / 0.35f);
            if (grow <= 0f)
            {
                markerLeaving = false;
                hazardMarker.transform.localScale = markerBaseScale;
                hazardMarker.transform.localPosition = markerBasePos;
                hazardMarker.SetActive(false);
                return;
            }
        }
        else grow = Easing.OutBack(markerAge / 0.45f, 2.5f);

        float beat = 1f + 0.1f * Mathf.Pow(0.5f + 0.5f * Mathf.Sin(markerAge * Mathf.PI / Mathf.Max(0.1f, blinkInterval)), 3f);
        hazardMarker.transform.localScale = markerBaseScale * Mathf.Max(0f, grow) * beat;
        hazardMarker.transform.localPosition = markerBasePos + Vector3.up * (0.012f * Mathf.Sin(markerAge * 2.2f));
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
