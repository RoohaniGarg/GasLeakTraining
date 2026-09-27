using System;
using UnityEngine;

/// <summary>
/// Phase 6: The trainee's gas detector. Readings come from where the PHONE is:
/// the closer the camera gets (horizontally) to the leaking pipe or the manhole,
/// the more the readings change - just like walking around with a real detector.
///
///   O2  normal 20.9 %  - below 19.5 % is dangerous (oxygen-deficient)
///   LEL normal 0 %     - 10 % or more of the Lower Explosive Limit is dangerous
///
/// Also runs the "test the air before entry" check (Step 3): hold the phone
/// over the manhole opening until the bar fills.
/// Distances are in tabletop-model metres (the 1 m x 1 m scene).
/// </summary>
public class GasDetectorHUD : MonoBehaviour
{
    public enum Level { Safe, Warning, Danger }

    [Header("Leak (near the pipe joint)")]
    public float minorLeakPeakLEL = 22f;
    public float majorLeakPeakLEL = 65f;
    public float pipeNear = 0.2f, pipeFar = 0.9f;

    [Header("Manhole (confined space)")]
    public float holeNear = 0.15f, holeFar = 0.8f;
    [Tooltip("O2 drop right at the opening before ventilation")]
    public float unventilatedO2Drop = 2.6f;
    public float unventilatedLEL = 6f;
    [Tooltip("Extra O2 drop / LEL at the opening during the emergency")]
    public float emergencyO2Drop = 4f;
    public float emergencyLEL = 40f;

    public bool Ventilated { get; set; }
    public bool Emergency { get; set; }
    public float O2 { get; private set; } = 20.9f;
    public float LEL { get; private set; }
    public Level CurrentLevel { get; private set; }
    public bool AirTestRunning => airTest;

    private TrainingUI ui;
    private Transform root, leakPoint, manhole, cam;
    private GasLeakController leak;
    private bool visible;

    // Air test
    private bool airTest;
    private float airProgress, airRadius, airDuration;
    private Action airDone;
    private float frozenUntil;   // show the "clean" result for a moment after the test

    // Beeper
    private AudioSource beeper;
    private float beepTimer;

    public void Init(TrainingUI ui, Transform root, GasLeakController leak, Transform manhole)
    {
        this.ui = ui;
        this.root = root;
        this.leak = leak;
        this.manhole = manhole;
        leakPoint = leak != null ? leak.transform : null;

        beeper = gameObject.AddComponent<AudioSource>();
        beeper.playOnAwake = false;
        beeper.spatialBlend = 0f;
        beeper.volume = 0.5f;
        beeper.clip = MakeBeep();
    }

    public void Show(bool show)
    {
        visible = show;
        if (ui != null) ui.ShowDetector(show);
    }

    /// <summary>Start the air test: hold the phone within 'radius' of the manhole for 'duration' seconds.</summary>
    public void StartAirTest(float radius, float duration, Action onDone)
    {
        airTest = true;
        airProgress = 0f;
        airRadius = radius;
        airDuration = duration;
        airDone = onDone;
        ui.SetAirTestProgress(0f, "Move over opening");
    }

    public void CancelAirTest()
    {
        airTest = false;
        ui.HideAirTestProgress();
    }

    void Update()
    {
        if (!visible || ui == null || root == null) return;
        if (cam == null && Camera.main != null) cam = Camera.main.transform;
        if (cam == null) return;

        float scale = Mathf.Max(0.0001f, root.lossyScale.x);
        float dPipe = leakPoint != null ? HorizontalDistance(cam.position, leakPoint.position) / scale : 99f;
        float dHole = manhole != null ? HorizontalDistance(cam.position, manhole.position) / scale : 99f;

        // ---- target readings
        float fPipe = Falloff(dPipe, pipeNear, pipeFar);
        float fHole = Falloff(dHole, holeNear, holeFar);
        float peak = (leak == null || !leak.IsLeaking) ? 0f : (leak.IsMajor ? majorLeakPeakLEL : minorLeakPeakLEL);

        // After ventilation the blower disperses the small leak, so the air test at the manhole reads clean
        if (Ventilated && !Emergency) peak *= 0.3f;

        float lel = peak * fPipe;
        float o2 = 20.9f - peak * 0.02f * fPipe;
        if (!Ventilated) { o2 -= unventilatedO2Drop * fHole; lel += unventilatedLEL * fHole; }
        if (Emergency) { o2 -= emergencyO2Drop * fHole; lel += emergencyLEL * fHole; }

        if (Time.time < frozenUntil) { o2 = 20.9f; lel = 0f; }

        // ---- smooth like a real sensor (+ a tiny flicker when gas is present)
        float k = 1f - Mathf.Exp(-Time.deltaTime * 3f);
        O2 = Mathf.Lerp(O2, o2, k);
        LEL = Mathf.Lerp(LEL, lel, k);
        float shownO2 = O2 + (O2 < 20.8f ? (Mathf.PerlinNoise(Time.time * 2f, 0.3f) - 0.5f) * 0.1f : 0f);
        float shownLEL = LEL < 0.5f ? 0f : LEL + (Mathf.PerlinNoise(0.7f, Time.time * 2f) - 0.5f) * 0.6f;

        // ---- level
        if (shownO2 < 19.5f || shownLEL >= 10f) CurrentLevel = Level.Danger;
        else if (shownO2 < 20.5f || shownLEL >= 5f) CurrentLevel = Level.Warning;
        else CurrentLevel = Level.Safe;

        // ---- write to the screen
        var d = ui.Detector;
        d.o2Value.text = shownO2.ToString("0.0") + " %";
        d.lelValue.text = Mathf.Max(0f, shownLEL).ToString("0") + " %";
        float blend = 1f - Mathf.Exp(-Time.deltaTime * 8f);
        d.o2Value.color = Color.Lerp(d.o2Value.color, shownO2 < 19.5f ? TrainingUI.Red : shownO2 < 20.5f ? TrainingUI.Amber : Color.white, blend);
        d.lelValue.color = Color.Lerp(d.lelValue.color, shownLEL >= 10f ? TrainingUI.Red : shownLEL >= 5f ? TrainingUI.Amber : Color.white, blend);
        switch (CurrentLevel)
        {
            case Level.Danger: ui.SetDetectorStatus("DANGER", TrainingUI.Red); break;
            case Level.Warning: ui.SetDetectorStatus("WARNING", TrainingUI.Amber); break;
            default: ui.SetDetectorStatus("SAFE", TrainingUI.Green); break;
        }

        UpdateAirTest(dHole);
        UpdateBeeper();
    }

    void UpdateAirTest(float dHole)
    {
        if (!airTest) return;
        bool inPlace = dHole <= airRadius;
        if (inPlace) airProgress += Time.deltaTime / airDuration;
        else airProgress = 0f;

        if (airProgress >= 1f)
        {
            airTest = false;
            frozenUntil = Time.time + 4f;
            O2 = 20.9f; LEL = 0f;
            ui.SetAirTestProgress(1f, "AIR SAFE");
            var cb = airDone;
            airDone = null;
            cb?.Invoke();
            return;
        }
        ui.SetAirTestProgress(airProgress, inPlace ? "Sampling..." : "Move over opening");
    }

    void UpdateBeeper()
    {
        float interval = CurrentLevel == Level.Danger ? 0.25f : CurrentLevel == Level.Warning ? 0.9f : -1f;
        if (interval < 0f) { beepTimer = 0f; return; }
        beepTimer -= Time.deltaTime;
        if (beepTimer <= 0f)
        {
            beepTimer = interval;
            if (beeper != null) beeper.Play();
        }
    }

    static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f; b.y = 0f;
        return Vector3.Distance(a, b);
    }

    /// <summary>1 when closer than 'near', 0 beyond 'far', smooth in between.</summary>
    static float Falloff(float d, float near, float far)
    {
        float t = Mathf.Clamp01((d - near) / (far - near));
        return 1f - t * t * (3f - 2f * t);
    }

    static AudioClip MakeBeep()
    {
        int rate = 22050;
        int n = (int)(rate * 0.07f);
        var data = new float[n];
        for (int i = 0; i < n; i++)
        {
            float env = Mathf.Min(1f, Mathf.Min(i, n - i) / (rate * 0.005f));
            data[i] = Mathf.Sin(2f * Mathf.PI * 2900f * i / rate) * 0.6f * env;
        }
        var clip = AudioClip.Create("DetectorBeep", n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
