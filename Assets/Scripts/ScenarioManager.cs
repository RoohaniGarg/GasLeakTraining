using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The stages of the gas leak / confined space training (v3 design).
/// </summary>
public enum TrainingState
{
    Placement,     // scanning the floor and placing the work site
    Briefing,      // story + START
    HazardHunt,    // Step 1 - find the 3 hazards with the detector
    PPESelection,  // Step 2 - pick Ramesh's protective equipment
    BuddySetup,    // Step 3 - lifeline, air test, attendant question, send Ramesh in
    Emergency,     // Step 4 - alarm + non-entry rescue
    Isolate,       // Step 5 - shut off the gas
    ReEntryQuiz,   // Step 5 - re-entry question
    Results        // score screen
}

/// <summary>
/// Phase 6/7: Runs the whole training. Lives on its own always-active
/// "ScenarioManager" GameObject (with TapInputManager). At start it creates
/// the on-screen UI, the gas detector, the score keeper and the hint arrow,
/// then walks the trainee through every step, checking each tap.
/// </summary>
public class ScenarioManager : MonoBehaviour
{
    enum BuddyStep { AttachLifeline, AirTest, Question, SendIn, Inside }

    [Header("References")]
    [Tooltip("The ARPlacementManager script (on XR Origin)")]
    [SerializeField] private ARPlacementManager placementManager;
    [Tooltip("The TrainingEnvironmentRoot GameObject (starts disabled)")]
    [SerializeField] private GameObject trainingEnvironmentRoot;
    [Tooltip("The AR camera. Leave empty to use Main Camera automatically.")]
    [SerializeField] private Transform arCamera;

    [Header("Hazards (auto-assigned by SIH > Build Realistic Environment)")]
    [SerializeField] private GasLeakController gasLeak;
    [SerializeField] private HazardZoneVisual hazardZones;

    [Header("Timing")]
    [SerializeField] private float spawnAnimationDuration = 0.6f;
    [Tooltip("Seconds of scanning before the 'Place in front of me' button appears")]
    [SerializeField] private float placeFallbackDelay = 10f;
    [Tooltip("Seconds without progress before the hint arrow appears")]
    [SerializeField] private float hintDelay = 12f;

    [Header("Air test / danger zones (tabletop metres)")]
    [SerializeField] private float airTestRadius = 0.3f;
    [SerializeField] private float airTestDuration = 3f;
    [SerializeField] private float pipeDangerRadius = 0.12f;
    [SerializeField] private float manholeDangerRadius = 0.15f;

    public TrainingState CurrentState { get; private set; } = TrainingState.Placement;
    public GasLeakController GasLeak => gasLeak;
    public HazardZoneVisual HazardZones => hazardZones;

    // Created at start
    private TrainingUI ui;
    private GasDetectorHUD hud;
    private ScoreManager score;
    private HintArrow hint;
    private TapInputManager tapInput;

    // Scene objects
    private BuddyController buddy;
    private Lifeline lifeline;
    private Transform manhole;
    private readonly Dictionary<InteractableId, InteractableObject> objects = new Dictionary<InteractableId, InteractableObject>();

    // Placement
    private bool environmentSpawned;
    private Vector3 environmentOriginalScale = Vector3.one;
    private float placementTimer;
    private bool placeButtonShown;

    // Progress / hints
    private float lastProgressTime;
    private bool hintsActive;

    // Step 1
    private readonly HashSet<InteractableId> found = new HashSet<InteractableId>();
    private int huntWrongTaps;

    // Step 3
    private BuddyStep buddyStep;
    private bool airTestMistake;

    // Step 4
    private bool alarmRaised, winchStarted, rescued, rescueScored;
    private int rescueMistakes;
    private float emergencyStartTime;

    // Step 5
    private int isolateAttempts;

    // Danger zones
    private bool dangerMonitorOn, inDanger;
    private float lastDangerWarning = -99f;

    // ================================================================ start

    void OnEnable() { InteractableObject.OnTapped += OnObjectTapped; }
    void OnDisable() { InteractableObject.OnTapped -= OnObjectTapped; }

    void Start()
    {
        if (placementManager == null)
            Debug.LogError("ScenarioManager: Placement Manager is not assigned in the Inspector!");
        if (trainingEnvironmentRoot == null)
        {
            Debug.LogError("ScenarioManager: Training Environment Root is not assigned in the Inspector!");
            return;
        }
        if (arCamera == null && Camera.main != null)
            arCamera = Camera.main.transform;

        ui = gameObject.AddComponent<TrainingUI>();
        score = gameObject.AddComponent<ScoreManager>();
        hud = gameObject.AddComponent<GasDetectorHUD>();
        tapInput = GetComponent<TapInputManager>();
        if (tapInput == null) tapInput = gameObject.AddComponent<TapInputManager>();

        Transform root = trainingEnvironmentRoot.transform;
        buddy = root.GetComponentInChildren<BuddyController>(true);
        lifeline = root.GetComponentInChildren<Lifeline>(true);
        foreach (var io in root.GetComponentsInChildren<InteractableObject>(true))
            objects[io.id] = io;
        manhole = objects.ContainsKey(InteractableId.ConfinedSpace) ? objects[InteractableId.ConfinedSpace].transform : null;

        hud.Init(ui, root, gasLeak, manhole);

        // Hint arrow uses the pipe's yellow material as a template (always included in the build)
        Material template = null;
        Transform riser = root.Find("GasPipe/RiserLower");
        if (riser != null && riser.GetComponent<Renderer>() != null) template = riser.GetComponent<Renderer>().sharedMaterial;
        hint = HintArrow.Create(template, root);

        environmentOriginalScale = root.localScale;
        trainingEnvironmentRoot.SetActive(false);

        EnterPlacement();
    }

    // =============================================================== update

    void Update()
    {
        if (trainingEnvironmentRoot == null || ui == null) return;

        if (CurrentState == TrainingState.Placement)
        {
            UpdatePlacement();
            return;
        }

        UpdateHints();
        UpdateDangerZones();

        if (CurrentState == TrainingState.Emergency || CurrentState == TrainingState.Isolate)
            ui.SetInfo("Time: " + Mathf.FloorToInt(Time.time - emergencyStartTime) + " s");
    }

    void SetState(TrainingState s)
    {
        CurrentState = s;
        lastProgressTime = Time.time;
        if (hint != null) hint.Hide();
        Debug.Log("ScenarioManager: State -> " + s);
    }

    void Progress()
    {
        lastProgressTime = Time.time;
        hint.Hide();
    }

    // ============================================================ placement

    void EnterPlacement()
    {
        SetState(TrainingState.Placement);
        environmentSpawned = false;
        placementTimer = 0f;
        placeButtonShown = false;
        hintsActive = false;
        dangerMonitorOn = false;
        hud.Show(false);
        ui.ShowAlarmButton(false);
        ui.ShowPlaceButton(false);
        ui.SetStep("SET UP");
        ui.SetInfo("");
        ui.SetInstruction("Scan the floor slowly.");
    }

    void UpdatePlacement()
    {
        if (environmentSpawned || placementManager == null) return;

        if (placementManager.isPlacementComplete)
        {
            SpawnEnvironment();
            return;
        }

        placementTimer += Time.deltaTime;
        ui.SetInstruction(placementManager.HasSurface
            ? "Floor found. Tap to place the site."
            : "Scan the floor slowly.");

        if (!placeButtonShown && placementTimer >= placeFallbackDelay)
        {
            placeButtonShown = true;
            ui.ShowPlaceButton(true, () => placementManager.PlaceInFrontOfCamera());
        }
    }

    void SpawnEnvironment()
    {
        environmentSpawned = true;
        ui.ShowPlaceButton(false);

        Vector3 spawnPosition = placementManager.placedPosition;
        Quaternion spawnRotation = placementManager.placedRotation;

        // Turn the site so its front (worker + shut-off valve) faces the user
        if (arCamera != null)
        {
            Vector3 away = spawnPosition - arCamera.position;
            away.y = 0f;
            if (away.sqrMagnitude > 0.0001f)
                spawnRotation = Quaternion.LookRotation(away.normalized, Vector3.up);
        }

        Transform root = trainingEnvironmentRoot.transform;
        root.SetPositionAndRotation(spawnPosition, spawnRotation);
        root.localScale = Vector3.zero;
        trainingEnvironmentRoot.SetActive(true);
        StartCoroutine(GrowIn());
    }

    IEnumerator GrowIn()
    {
        Transform root = trainingEnvironmentRoot.transform;
        float t = 0f;
        while (t < spawnAnimationDuration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / spawnAnimationDuration);
            root.localScale = environmentOriginalScale * (1f - Mathf.Pow(1f - k, 3f));
            yield return null;
        }
        root.localScale = environmentOriginalScale;
        EnterBriefing();
    }

    void MoveSite()
    {
        StopAllCoroutines();
        trainingEnvironmentRoot.SetActive(false);
        placementManager.ResetPlacement();
        EnterPlacement();
    }

    // ============================================================= briefing

    void EnterBriefing()
    {
        SetState(TrainingState.Briefing);
        ui.ShowTopBar(false);
        if (gasLeak != null) gasLeak.StartLeak(false);   // small leak - find it with the detector

        ui.ShowBriefing("GAS LEAK &\nCONFINED SPACE",
            "Job: inspect the valve chamber next to the gas line.\n\n" +
            "Ramesh goes in. You are the <b>attendant</b> - you stay outside.\n\n" +
            "Score and response time are recorded.",
            EnterHazardHunt, MoveSite);
    }

    // ======================================================= step 1 - hunt

    void EnterHazardHunt()
    {
        SetState(TrainingState.HazardHunt);
        score.ResetAll();
        found.Clear();
        huntWrongTaps = 0;
        hintsActive = true;

        hud.Ventilated = false;
        hud.Emergency = false;
        hud.Show(true);

        ui.SetStep("STEP 1/5   HAZARDS");
        ui.SetInstruction("Walk around. Watch the detector.\nTap the 3 hazards.");
        ui.SetInfo("Found 0/3");
    }

    void HandleHunt(InteractableId id)
    {
        // The tripod stands over the manhole - a tap on it counts as the manhole
        InteractableId key = id == InteractableId.RetrievalTripod ? InteractableId.ConfinedSpace : id;

        if (key == InteractableId.GasSource || key == InteractableId.ConfinedSpace || key == InteractableId.GasCylinder)
        {
            if (found.Contains(key))
            {
                ui.Toast("Already found", "", TrainingUI.ToastKind.Info, 1.5f);
                return;
            }
            found.Add(key);
            Progress();
            ui.SetInfo("Found " + found.Count + "/3");

            switch (key)
            {
                case InteractableId.GasSource:
                    ui.Toast("Gas leak", "Pipe joint. LEL rising.", TrainingUI.ToastKind.Good);
                    break;
                case InteractableId.ConfinedSpace:
                    ui.Toast("Confined space", "Low O2 at the opening.", TrainingUI.ToastKind.Good);
                    break;
                default:
                    ui.Toast("LPG cylinder", "Flammable, close to the leak.", TrainingUI.ToastKind.Good);
                    break;
            }

            if (found.Count == 3) StartCoroutine(FinishHunt());
            return;
        }

        huntWrongTaps++;
        string body = id == InteractableId.EmergencyShutoff ? "Shut-off valve - safety equipment."
                    : id == InteractableId.Worker ? "That's Ramesh."
                    : "Check the detector.";
        ui.Toast("Not a hazard", body, TrainingUI.ToastKind.Bad);
    }

    IEnumerator FinishHunt()
    {
        hintsActive = false;
        yield return new WaitForSeconds(1.8f);

        int pts = 15 - 4 * huntWrongTaps;
        score.SetStep("Hazard hunt", pts, 15, huntWrongTaps == 0
            ? "No wrong taps"
            : huntWrongTaps + " wrong tap" + (huntWrongTaps > 1 ? "s" : ""));

        if (hazardZones != null) hazardZones.Show();
        dangerMonitorOn = true;

        ui.ShowMessage("HAZARD ZONES",
            "<color=#E5484D><b>RED</b></color>  -  danger, keep out\n" +
            "<color=#F5A524><b>AMBER</b></color>  -  caution\n" +
            "<color=#2EB872><b>GREEN</b></color>  -  assembly point",
            "NEXT", TrainingUI.Orange, EnterPPE);
    }

    // ======================================================== step 2 - PPE

    void EnterPPE()
    {
        SetState(TrainingState.PPESelection);
        ui.SetStep("STEP 2/5   PPE");
        ui.SetInstruction("Pick Ramesh's PPE.");
        ui.SetInfo("");

        var items = new[]
        {
            new TrainingUI.PPEItem { label = "Breathing apparatus", icon = "scba", correct = true,
                why = "own air supply - works in low O2." },
            new TrainingUI.PPEItem { label = "Dust mask", icon = "dustmask", correct = false,
                why = "dust only - no gas protection." },
            new TrainingUI.PPEItem { label = "Harness + lifeline", icon = "harness", correct = true,
                why = "pull him out without entering." },
            new TrainingUI.PPEItem { label = "Filter gas mask", icon = "gasmask", correct = false,
                why = "gives no oxygen." },
            new TrainingUI.PPEItem { label = "Ordinary torch", icon = "torch", correct = false,
                why = "can spark - use a flameproof lamp." },
            new TrainingUI.PPEItem { label = "Gas detector", icon = "detector", correct = true,
                why = "warns him when the air turns bad." },
        };

        ui.ShowPPE("STEP 2/5   PPE",
            "Pick 3 items for chamber entry",
            "Hard hat and boots already on.",
            items, 3, mistakes =>
            {
                score.SetStep("PPE selection", 15 - 5 * mistakes, 15, mistakes == 0
                    ? "First try"
                    : mistakes + " mistake" + (mistakes > 1 ? "s" : ""));
                EnterBuddySetup();
            });
    }

    // ============================================= step 3 - buddy system

    void EnterBuddySetup()
    {
        SetState(TrainingState.BuddySetup);
        buddyStep = BuddyStep.AttachLifeline;
        airTestMistake = false;
        hintsActive = true;
        hud.Ventilated = true;   // a blower has cleared the chamber

        ui.SetStep("STEP 3/5   BUDDY SYSTEM");
        ui.SetInstruction("Chamber ventilated.\nTap the <b>tripod</b> to attach Ramesh's lifeline.");
        ui.SetInfo("");
    }

    void HandleBuddy(InteractableId id)
    {
        switch (buddyStep)
        {
            case BuddyStep.AttachLifeline:
                if (id == InteractableId.RetrievalTripod || id == InteractableId.ConfinedSpace)
                {
                    Progress();
                    if (lifeline != null) lifeline.Attach();
                    if (buddy != null) buddy.WalkTo(buddy.manholeApproach);
                    ui.Toast("Lifeline attached", "", TrainingUI.ToastKind.Good);
                    StartAirTest();
                }
                else if (id == InteractableId.Worker)
                {
                    airTestMistake = true;
                    ui.Toast("STOP", "Lifeline and air test first.", TrainingUI.ToastKind.Bad, 3f);
                }
                else ui.Toast("Tap the tripod", "", TrainingUI.ToastKind.Info);
                break;

            case BuddyStep.AirTest:
                if (id == InteractableId.Worker)
                {
                    airTestMistake = true;
                    ui.Toast("STOP", "Test the air first.", TrainingUI.ToastKind.Bad, 3f);
                }
                else ui.Toast("Hold, don't tap", "Keep the phone over the opening.", TrainingUI.ToastKind.Info);
                break;

            case BuddyStep.SendIn:
                if (id == InteractableId.Worker)
                {
                    Progress();
                    buddyStep = BuddyStep.Inside;
                    hintsActive = false;
                    if (buddy != null) buddy.EnterConfinedSpace();
                    ui.SetInstruction("Ramesh is going down.\nStay at the entry. Watch the detector.");
                    StartCoroutine(EmergencyAfterEntry());
                }
                else ui.Toast("Tap Ramesh", "", TrainingUI.ToastKind.Info);
                break;
        }
    }

    void StartAirTest()
    {
        buddyStep = BuddyStep.AirTest;
        ui.SetInstruction("Air test: hold the phone over the manhole until the bar fills.");
        if (manhole != null) hint.PointAt(manhole);
        hud.StartAirTest(airTestRadius, airTestDuration, OnAirTestDone);
    }

    void OnAirTestDone()
    {
        Progress();
        buddyStep = BuddyStep.Question;
        ui.Toast("AIR SAFE", "O2 20.9%   LEL 0%", TrainingUI.ToastKind.Good, 2.5f);
        StartCoroutine(AttendantQuestionLater());
    }

    IEnumerator AttendantQuestionLater()
    {
        yield return new WaitForSeconds(2.2f);
        hud.CancelAirTest();

        ui.ShowMCQ("STEP 3/5   BUDDY SYSTEM",
            "As attendant, your job is to:",
            new[]
            {
                "Go in with him",
                "Stay at the entry, keep contact, watch readings",
                "Fetch tools while he works",
                "Check on him every 15 min",
            },
            1,
            new[]
            {
                "Two people inside = two victims.",
                "Never enter, never leave the entry.",
                "Never leave the entry.",
                "Contact must be constant.",
            },
            attempts =>
            {
                int mcq = attempts == 1 ? 10 : attempts == 2 ? 5 : 0;
                int air = airTestMistake ? 5 : 10;
                score.SetStep("Buddy system", air + mcq, 20,
                    (airTestMistake ? "Sent Ramesh before air test" : "Air tested first") +
                    (attempts == 1 ? ", MCQ first try" : ", MCQ try " + attempts));

                buddyStep = BuddyStep.SendIn;
                lastProgressTime = Time.time;
                ui.SetInstruction("Tap <b>Ramesh</b> to send him in.");
            });
    }

    IEnumerator EmergencyAfterEntry()
    {
        while (buddy != null && buddy.State != BuddyState.InConfinedSpace) yield return null;
        yield return new WaitForSeconds(3f);
        EnterEmergency();
    }

    // ============================================== step 4 - emergency

    void EnterEmergency()
    {
        SetState(TrainingState.Emergency);
        alarmRaised = false;
        winchStarted = false;
        rescued = false;
        rescueScored = false;
        rescueMistakes = 0;
        emergencyStartTime = Time.time;
        hintsActive = true;

        if (gasLeak != null) gasLeak.StartLeak(true);
        if (hazardZones != null) hazardZones.Show();
        hud.Emergency = true;
        if (buddy != null) buddy.Collapse();
        ui.Flash(TrainingUI.Red);
        Vibrate();

        ui.SetStep("STEP 4/5   EMERGENCY");
        ui.SetInstruction("<color=#E5484D><b>GAS ALARM</b></color>  -  Ramesh is down.\nRaise the alarm. Winch him out. Don't go in.");
        ui.ShowAlarmButton(true, OnAlarmPressed);
    }

    void OnAlarmPressed()
    {
        if (CurrentState != TrainingState.Emergency || alarmRaised) return;
        alarmRaised = true;
        Progress();
        ui.ShowAlarmButton(false);
        ui.Toast("Alarm raised", winchStarted ? "Rescue team called." : "Now winch him out.", TrainingUI.ToastKind.Good);
        CheckRescueDone();
    }

    void HandleEmergency(InteractableId id)
    {
        switch (id)
        {
            case InteractableId.RetrievalTripod:
                if (winchStarted) return;
                winchStarted = true;
                Progress();
                if (buddy != null) buddy.PullOut();
                ui.Toast("Winching out", "", TrainingUI.ToastKind.Good);
                StartCoroutine(RescueRoutine());
                break;

            case InteractableId.ConfinedSpace:
                rescueMistakes++;
                ui.Flash(TrainingUI.Red);
                Vibrate();
                ui.Toast("DON'T GO IN", "Most confined-space deaths are rescuers. Use the winch.", TrainingUI.ToastKind.Bad, 3.5f);
                break;

            case InteractableId.EmergencyShutoff:
                ui.Toast("Not yet", "Alarm and rescue first.", TrainingUI.ToastKind.Info);
                break;

            default:
                ui.Toast("Focus", "Raise the alarm. Use the winch.", TrainingUI.ToastKind.Info);
                break;
        }
    }

    IEnumerator RescueRoutine()
    {
        while (buddy != null && buddy.State != BuddyState.Rescued) yield return null;
        yield return new WaitForSeconds(1f);
        if (lifeline != null) lifeline.Detach();
        if (buddy != null) buddy.GoToSafeZone();
        rescued = true;
        if (!alarmRaised)
            ui.SetInstruction("Ramesh is out. <b>Raise the alarm.</b>");
        CheckRescueDone();
    }

    void CheckRescueDone()
    {
        if (!alarmRaised || !rescued || rescueScored) return;
        rescueScored = true;
        int pts = rescueMistakes == 0 ? 20 : rescueMistakes == 1 ? 10 : 0;
        score.SetStep("Emergency rescue", pts, 20, rescueMistakes == 0
            ? "Winch rescue, alarm raised"
            : "Tried to enter " + rescueMistakes + " time" + (rescueMistakes > 1 ? "s" : ""));
        EnterIsolate();
    }

    // ================================================= step 5 - isolate

    void EnterIsolate()
    {
        SetState(TrainingState.Isolate);
        isolateAttempts = 0;
        hintsActive = true;
        ui.SetStep("STEP 5/5   ISOLATE");
        ui.SetInstruction("Shut off the gas.\nTap the red <b>shut-off valve</b>.");
    }

    void HandleIsolate(InteractableId id)
    {
        isolateAttempts++;
        if (id != InteractableId.EmergencyShutoff)
        {
            ui.Toast("Wrong", "Find the red handwheel.", TrainingUI.ToastKind.Bad);
            return;
        }

        Progress();
        hintsActive = false;
        float t = Time.time - emergencyStartTime;
        score.ResponseTime = t;

        if (gasLeak != null) gasLeak.StopLeak();
        if (hazardZones != null) hazardZones.Hide();
        hud.Emergency = false;
        dangerMonitorOn = false;
        inDanger = false;
        ui.SetDanger(false);

        int pts = isolateAttempts == 1 ? 10 : isolateAttempts == 2 ? 5 : 0;
        score.SetStep("Stop the leak", pts, 10, isolateAttempts == 1 ? "First try" : "Try " + isolateAttempts);
        int bonus = t <= 30f ? 10 : t <= 100f ? 5 : 0;   // full marks within 30 s, -5 after 30 s, -10 after 100 s
        score.SetStep("Response time", bonus, 10, "Alarm to shut-off: " + Mathf.RoundToInt(t) + " s");

        ui.SetInfo("Time: " + Mathf.RoundToInt(t) + " s");
        ui.Toast("Gas shut off", "", TrainingUI.ToastKind.Good);
        StartCoroutine(ReEntryQuizLater());
    }

    IEnumerator ReEntryQuizLater()
    {
        yield return new WaitForSeconds(2.5f);
        SetState(TrainingState.ReEntryQuiz);
        ui.ShowMCQ("RE-ENTRY",
            "Gas is off. Before anyone goes back in:",
            new[]
            {
                "Switch on the exhaust fan",
                "Ventilate, re-test, new permit + attendant",
                "Go in once you can't smell gas",
                "Send Ramesh back in",
            },
            1,
            new[]
            {
                "Switches can spark.",
                "Must be proven safe again.",
                "Smell fades. Some gases have none.",
                "Nobody enters untested air.",
            },
            attempts =>
            {
                int pts = attempts == 1 ? 10 : attempts == 2 ? 5 : 0;
                score.SetStep("Re-entry rules", pts, 10, attempts == 1 ? "First try" : "Try " + attempts);
                EnterResults();
            });
    }

    // ============================================================ results

    void EnterResults()
    {
        SetState(TrainingState.Results);
        hintsActive = false;
        hud.Show(false);
        ui.ShowTopBar(false);
        ui.HideToast();

        var lines = new List<string>();
        foreach (var s in score.Steps)
        {
            string colour = s.points == s.max ? "#8CEBB4" : s.points == 0 ? "#FFB9B9" : "#F5C56B";
            lines.Add("<b>" + s.title + "</b>  <color=" + colour + ">" + s.points + "/" + s.max + "</color>\n<size=85%><color=#AAB2BD>" + s.note + "</color></size>");
        }
        if (score.DangerPenalty > 0)
            lines.Add("<b>Danger zone</b>  <color=#FFB9B9>-" + score.DangerPenalty + "</color>\n<size=85%><color=#AAB2BD>In a red zone " + score.DangerHits + " time" + (score.DangerHits > 1 ? "s" : "") + "</color></size>");

        string time = score.ResponseTime >= 0f ? "Response time: " + Mathf.RoundToInt(score.ResponseTime) + " s" : "";
        ui.ShowResults(score.Total, score.Rating, time, lines, Retry, ExitApp);
    }

    void ExitApp()
    {
        Debug.Log("ScenarioManager: Exit pressed");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void Retry()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    // ============================================================== taps

    void OnObjectTapped(InteractableObject obj)
    {
        if (ui == null || ui.ModalOpen) return;
        switch (CurrentState)
        {
            case TrainingState.HazardHunt: HandleHunt(obj.id); break;
            case TrainingState.BuddySetup: HandleBuddy(obj.id); break;
            case TrainingState.Emergency: HandleEmergency(obj.id); break;
            case TrainingState.Isolate: HandleIsolate(obj.id); break;
        }
    }

    // ============================================================= hints

    void UpdateHints()
    {
        if (!hintsActive || hint.IsShowing || ui.ModalOpen) return;
        if (Time.time - lastProgressTime < hintDelay) return;

        Transform target = CurrentHintTarget();
        if (target == null) return;
        hint.PointAt(target);
        score.HintsUsed++;
    }

    Transform CurrentHintTarget()
    {
        switch (CurrentState)
        {
            case TrainingState.HazardHunt:
                Transform best = null;
                float bestDist = float.MaxValue;
                foreach (var id in new[] { InteractableId.GasSource, InteractableId.ConfinedSpace, InteractableId.GasCylinder })
                {
                    if (found.Contains(id) || !objects.ContainsKey(id)) continue;
                    float d = arCamera != null ? Vector3.Distance(arCamera.position, objects[id].transform.position) : 0f;
                    if (d < bestDist) { bestDist = d; best = objects[id].transform; }
                }
                return best;
            case TrainingState.BuddySetup:
                if (buddyStep == BuddyStep.AttachLifeline) return Obj(InteractableId.RetrievalTripod);
                if (buddyStep == BuddyStep.AirTest) return manhole;
                if (buddyStep == BuddyStep.SendIn) return Obj(InteractableId.Worker);
                return null;
            case TrainingState.Emergency:
                return winchStarted ? null : Obj(InteractableId.RetrievalTripod);
            case TrainingState.Isolate:
                return Obj(InteractableId.EmergencyShutoff);
        }
        return null;
    }

    Transform Obj(InteractableId id) => objects.ContainsKey(id) ? objects[id].transform : null;

    // ======================================================= danger zones

    void UpdateDangerZones()
    {
        bool danger = false;
        if (dangerMonitorOn && arCamera != null && gasLeak != null && gasLeak.IsLeaking && !ui.ModalOpen)
        {
            float scale = trainingEnvironmentRoot.transform.lossyScale.x;
            if (Flat(arCamera.position, gasLeak.transform.position) < pipeDangerRadius * scale) danger = true;
            if (CurrentState == TrainingState.Emergency && manhole != null &&
                Flat(arCamera.position, manhole.position) < manholeDangerRadius * scale) danger = true;
        }

        if (danger && !inDanger && Time.time - lastDangerWarning > 3f)
        {
            lastDangerWarning = Time.time;
            Vibrate();
            bool penalised = score.AddDangerPenalty(5, 10);
            ui.Toast("DANGER ZONE", penalised ? "Step back.  -5" : "Step back.", TrainingUI.ToastKind.Bad);
        }
        inDanger = danger;
        ui.SetDanger(danger);
    }

    static float Flat(Vector3 a, Vector3 b)
    {
        a.y = 0f; b.y = 0f;
        return Vector3.Distance(a, b);
    }

    static void Vibrate()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        Handheld.Vibrate();
#endif
    }
}
