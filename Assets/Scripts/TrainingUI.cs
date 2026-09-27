using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Phase 6: The whole on-screen interface, built from code at start-up
/// (no hand-made prefabs). English only. Portrait phone layout.
///
///   Top bar      - step label + instruction + small info line (counter / timer)
///   Detector     - bottom-left gas detector (O2 / LEL / status / air-test bar)
///   Buttons      - "PLACE IN FRONT OF ME" (placement fallback), "RAISE ALARM"
///   Toast        - short right/wrong message that disappears by itself
///   Modal card   - briefing, questions, PPE picker, messages, results
///   Danger edge  - pulsing red screen edge when the phone is in a danger zone
///
/// Phase 9 (polish): every panel fades + slides in/out, buttons are springy,
/// modal cards pop in with their content appearing one line after another,
/// wrong answers shake, the score counts up, and the alarm flashes the screen.
/// </summary>
public class TrainingUI : MonoBehaviour
{
    // ------------------------------------------------------------ colours
    public static readonly Color Dark = new Color32(28, 32, 38, 235);
    public static readonly Color Orange = new Color32(255, 122, 26, 255);
    public static readonly Color Green = new Color32(46, 184, 114, 255);
    public static readonly Color Red = new Color32(229, 72, 77, 255);
    public static readonly Color Amber = new Color32(245, 165, 36, 255);
    public static readonly Color Light = new Color32(242, 242, 242, 255);
    public static readonly Color TextDark = new Color32(34, 38, 46, 255);
    public static readonly Color Muted = new Color32(170, 178, 189, 255);

    public enum ToastKind { Good, Bad, Info, Warning }

    public class PPEItem
    {
        public string label;
        public string icon;      // file name in Resources/PPE
        public bool correct;
        public string why;
    }

    /// <summary>References to the detector widgets (GasDetectorHUD writes into these).</summary>
    public class DetectorView
    {
        public GameObject root;
        public TextMeshProUGUI o2Value, lelValue, status;
        public Image statusPill;
        public GameObject progressRoot;
        public Image progressFill;
        public TextMeshProUGUI progressLabel;
    }

    public DetectorView Detector { get; private set; }
    public bool ModalOpen => modalRoot != null && IsShown(modalRoot);

    // Built objects
    private RectTransform canvasRect;
    private Sprite rounded, circle;
    private GameObject topBar;
    private Image topBarBg;
    private TextMeshProUGUI stepText, instructionText, infoText;
    private GameObject placeButton, alarmButton;
    private Action placeAction, alarmAction;
    private GameObject toastRoot;
    private Image toastBg;
    private RectTransform toastTimer;
    private TextMeshProUGUI toastTitle, toastBody;
    private Coroutine toastRoutine;
    private GameObject modalRoot;
    private RectTransform modalCard;
    private CanvasGroup modalCardGroup;
    private int modalToken;
    private RawImage dangerEdge;
    private Image screenTint;
    private bool dangerOn;
    private float dangerK;
    private float flashAlpha;
    private Color flashColor = Color.red;

    // Detector animation state
    private Color pillTarget = Green;
    private string lastStatus = "SAFE";
    private float airTarget, airShown;
    private string lastAirLabel = "";

    // Panel animation bookkeeping
    private readonly Dictionary<GameObject, Coroutine> panelAnims = new Dictionary<GameObject, Coroutine>();
    private readonly HashSet<GameObject> hiding = new HashSet<GameObject>();
    private readonly Dictionary<RectTransform, Vector2> homePos = new Dictionary<RectTransform, Vector2>();
    private readonly Dictionary<Graphic, Coroutine> colorAnims = new Dictionary<Graphic, Coroutine>();
    private Coroutine instructionFade;

    // ================================================================= set-up

    void Awake()
    {
        Screen.orientation = ScreenOrientation.Portrait;
        rounded = MakeRoundedSprite(128, 36);
        circle = MakeRoundedSprite(128, 64);
        EnsureEventSystem();
        BuildCanvas();
    }

    static void EnsureEventSystem()
    {
        if (EventSystem.current != null || FindAnyObjectByType<EventSystem>() != null) return;
        var es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        es.AddComponent<StandaloneInputModule>();
    }

    void BuildCanvas()
    {
        var go = new GameObject("TrainingCanvas");
        go.transform.SetParent(transform, false);
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 0f;   // keep widths identical on every portrait phone
        go.AddComponent<GraphicRaycaster>();
        canvasRect = go.GetComponent<RectTransform>();

        BuildDangerEdge();
        BuildTopBar();
        BuildDetector();
        BuildButtons();
        BuildToast();
        BuildModal();
    }

    // ---------------------------------------------------------------- top bar

    void BuildTopBar()
    {
        var rt = NewRect("TopBar", canvasRect);
        rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(0.5f, 1);
        rt.offsetMin = new Vector2(28, 0); rt.offsetMax = new Vector2(-28, 0);
        rt.anchoredPosition = new Vector2(0, -70);
        topBarBg = AddImage(rt.gameObject, Dark);
        topBarBg.raycastTarget = false;
        var v = rt.gameObject.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(40, 40, 28, 30);
        v.spacing = 10;
        v.childControlWidth = true; v.childControlHeight = true;
        v.childForceExpandWidth = true; v.childForceExpandHeight = false;
        var fit = rt.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        stepText = Label(rt, "", 30, Orange, TextAlignmentOptions.Left, true);
        stepText.characterSpacing = 4;
        instructionText = Label(rt, "", 42, Color.white, TextAlignmentOptions.Left, false);
        infoText = Label(rt, "", 34, Amber, TextAlignmentOptions.Left, true);
        topBar = rt.gameObject;
        topBar.SetActive(false);
    }

    public void SetStep(string label)
    {
        bool changed = stepText.text != label;
        stepText.text = label;
        stepText.gameObject.SetActive(!string.IsNullOrEmpty(label));
        bool wasShown = IsShown(topBar);
        ShowPanel(topBar, true, new Vector2(0, 90), 0.35f);
        if (changed && wasShown)
        {
            // New step: warm flash of the bar + a bounce of the step label
            topBarBg.color = Color.Lerp(Dark, Orange, 0.55f);
            ColorTo(topBarBg, Dark, 0.6f);
            Spring(stepText.gameObject).Punch(0.12f);
        }
    }

    public void SetInstruction(string text)
    {
        if (instructionText.text != text)
        {
            instructionText.text = text;
            if (instructionFade != null) StopCoroutine(instructionFade);
            instructionFade = StartCoroutine(FadeTextIn(instructionText, 0.3f));
        }
        ShowPanel(topBar, true, new Vector2(0, 90), 0.35f);
    }

    /// <summary>Small info line. 'emphasize' bounces it (e.g. "Found 2/3").</summary>
    public void SetInfo(string text, bool emphasize = false)
    {
        if (infoText.text != text)
        {
            infoText.text = text;
            if (emphasize) Spring(infoText.gameObject).Punch(0.18f);
        }
        infoText.gameObject.SetActive(!string.IsNullOrEmpty(text));
    }

    public void ShowTopBar(bool show) => ShowPanel(topBar, show, new Vector2(0, 90), 0.3f);

    // --------------------------------------------------------------- detector

    void BuildDetector()
    {
        var rt = NewRect("GasDetector", canvasRect);
        rt.anchorMin = rt.anchorMax = new Vector2(0, 0); rt.pivot = new Vector2(0, 0);
        rt.sizeDelta = new Vector2(500, 330);
        rt.anchoredPosition = new Vector2(28, 40);
        AddImage(rt.gameObject, Dark).raycastTarget = false;

        var d = new DetectorView { root = rt.gameObject };

        var title = Label(rt, "GAS DETECTOR", 26, Muted, TextAlignmentOptions.Left, true);
        Place(title.rectTransform, 0, 1, 0, 1, new Vector2(30, -22), new Vector2(300, 40), new Vector2(0, 1));

        var pillRt = NewRect("StatusPill", rt);
        Place(pillRt, 1, 1, 1, 1, new Vector2(-24, -16), new Vector2(170, 52), new Vector2(1, 1));
        d.statusPill = AddImage(pillRt.gameObject, Green);
        d.statusPill.raycastTarget = false;
        d.status = Label(pillRt, "SAFE", 28, Color.white, TextAlignmentOptions.Center, true);
        Stretch(d.status.rectTransform);

        var o2Label = Label(rt, "O2", 36, Color.white, TextAlignmentOptions.Left, true);
        Place(o2Label.rectTransform, 0, 1, 0, 1, new Vector2(30, -92), new Vector2(120, 70), new Vector2(0, 1));
        d.o2Value = Label(rt, "20.9 %", 58, Color.white, TextAlignmentOptions.Right, true);
        Place(d.o2Value.rectTransform, 1, 1, 1, 1, new Vector2(-30, -82), new Vector2(320, 80), new Vector2(1, 1));

        var lelLabel = Label(rt, "LEL", 36, Color.white, TextAlignmentOptions.Left, true);
        Place(lelLabel.rectTransform, 0, 1, 0, 1, new Vector2(30, -182), new Vector2(120, 70), new Vector2(0, 1));
        d.lelValue = Label(rt, "0 %", 58, Color.white, TextAlignmentOptions.Right, true);
        Place(d.lelValue.rectTransform, 1, 1, 1, 1, new Vector2(-30, -172), new Vector2(320, 80), new Vector2(1, 1));

        // Air-test progress bar (hidden until Step 3)
        var prog = NewRect("AirTest", rt);
        Place(prog, 0, 0, 1, 0, new Vector2(0, 18), new Vector2(-60, 50), new Vector2(0.5f, 0));
        var track = AddImage(prog.gameObject, new Color(1, 1, 1, 0.15f));
        track.raycastTarget = false;
        var fillRt = NewRect("Fill", prog);
        fillRt.anchorMin = new Vector2(0, 0); fillRt.anchorMax = new Vector2(0, 1); fillRt.pivot = new Vector2(0, 0.5f);
        fillRt.offsetMin = Vector2.zero; fillRt.offsetMax = Vector2.zero;
        d.progressFill = AddImage(fillRt.gameObject, Green);
        d.progressFill.raycastTarget = false;
        d.progressLabel = Label(prog, "", 26, Color.white, TextAlignmentOptions.Center, true);
        Stretch(d.progressLabel.rectTransform);
        d.progressRoot = prog.gameObject;
        prog.gameObject.SetActive(false);

        Detector = d;
        rt.gameObject.SetActive(false);
    }

    /// <summary>0..1 fill of the air-test bar. The bar glides to the value (animated in Update).</summary>
    public void SetAirTestProgress(float k, string label)
    {
        if (!IsShown(Detector.progressRoot))
        {
            airShown = 0f;
            ShowPanel(Detector.progressRoot, true, new Vector2(0, -20), 0.25f);
        }
        airTarget = Mathf.Clamp01(k);
        if (label != lastAirLabel)
        {
            lastAirLabel = label;
            Detector.progressLabel.text = label;
            if (label == "AIR SAFE")
            {
                airShown = airTarget;
                Spring(Detector.progressRoot).Punch(0.15f);
            }
        }
    }

    public void HideAirTestProgress()
    {
        lastAirLabel = "";
        ShowPanel(Detector.progressRoot, false, new Vector2(0, -20), 0.25f);
    }

    /// <summary>Status pill text + colour. The colour blends, and the pill bounces when the level changes.</summary>
    public void SetDetectorStatus(string text, Color color)
    {
        pillTarget = color;
        if (text == lastStatus) return;
        bool worse = text == "DANGER" || (text == "WARNING" && lastStatus == "SAFE");
        lastStatus = text;
        Detector.status.text = text;
        var s = Spring(Detector.statusPill.gameObject);
        s.Punch(worse ? 0.22f : 0.1f);
        if (text == "DANGER") Spring(Detector.root).Wiggle(2.5f);
    }

    // ---------------------------------------------------------------- buttons

    void BuildButtons()
    {
        var p = MakeButton(canvasRect, "PLACE IN FRONT OF ME", Orange, Color.white, 40, () => placeAction?.Invoke());
        var prt = (RectTransform)p.transform;
        Place(prt, 0.5f, 0, 0.5f, 0, new Vector2(0, 90), new Vector2(760, 150), new Vector2(0.5f, 0));
        placeButton = p.gameObject;
        placeButton.SetActive(false);

        var a = MakeButton(canvasRect, "RAISE\nALARM", Red, Color.white, 54, () => alarmAction?.Invoke());
        var art = (RectTransform)a.transform;
        Place(art, 1, 0, 1, 0, new Vector2(-28, 40), new Vector2(480, 330), new Vector2(1, 0));
        alarmButton = a.gameObject;
        alarmButton.SetActive(false);
    }

    public void ShowPlaceButton(bool show, Action onClick = null)
    {
        placeAction = onClick;
        ShowPanel(placeButton, show, new Vector2(0, -80), 0.35f, 0.15f);
    }

    public void ShowAlarmButton(bool show, Action onClick = null)
    {
        alarmAction = onClick;
        ShowPanel(alarmButton, show, new Vector2(120, 0), 0.3f, 0.3f);
    }

    public void ShowDetector(bool show) => ShowPanel(Detector.root, show, new Vector2(-140, 0), 0.4f);

    // ------------------------------------------------------------------ toast

    void BuildToast()
    {
        var rt = NewRect("Toast", canvasRect);
        rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(1, 0); rt.pivot = new Vector2(0.5f, 0);
        rt.offsetMin = new Vector2(28, 0); rt.offsetMax = new Vector2(-28, 0);
        rt.anchoredPosition = new Vector2(0, 400);
        toastBg = AddImage(rt.gameObject, Green);
        toastBg.raycastTarget = false;
        var v = rt.gameObject.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(40, 40, 26, 30);
        v.spacing = 6;
        v.childControlWidth = true; v.childControlHeight = true;
        v.childForceExpandWidth = true; v.childForceExpandHeight = false;
        rt.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        toastTitle = Label(rt, "", 42, Color.white, TextAlignmentOptions.Left, true);
        toastBody = Label(rt, "", 34, Color.white, TextAlignmentOptions.Left, false);

        // Thin bar along the bottom that shrinks while the toast is showing
        toastTimer = NewRect("Timer", rt);
        toastTimer.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        toastTimer.anchorMin = new Vector2(0, 0); toastTimer.anchorMax = new Vector2(1, 0);
        toastTimer.pivot = new Vector2(0, 0);
        toastTimer.offsetMin = new Vector2(30, 10); toastTimer.offsetMax = new Vector2(-30, 16);
        var bar = toastTimer.gameObject.AddComponent<Image>();
        bar.color = new Color(1, 1, 1, 0.45f);
        bar.raycastTarget = false;

        toastRoot = rt.gameObject;
        toastRoot.SetActive(false);
    }

    public void Toast(string title, string body, ToastKind kind, float seconds = 3f)
    {
        Color c = kind == ToastKind.Good ? Green : kind == ToastKind.Bad ? Red : kind == ToastKind.Warning ? Amber : Dark;
        c.a = 0.96f;
        toastTitle.text = title;
        toastBody.text = body;
        toastBody.gameObject.SetActive(!string.IsNullOrEmpty(body));

        var spring = Spring(toastRoot);
        if (IsShown(toastRoot))
        {
            ColorTo(toastBg, c, 0.15f);
            spring.Punch(0.06f);
        }
        else
        {
            toastBg.color = c;
            ShowPanel(toastRoot, true, new Vector2(0, -70), 0.3f, 0.12f);
        }
        if (kind == ToastKind.Bad) spring.Wiggle(4f);

        var fx = EffectsManager.Instance;
        if (fx != null)
        {
            if (kind == ToastKind.Good) fx.Chime();
            else if (kind == ToastKind.Bad) fx.Buzz();
        }

        if (toastRoutine != null) StopCoroutine(toastRoutine);
        toastRoutine = StartCoroutine(HideToastLater(seconds));
    }

    IEnumerator HideToastLater(float seconds)
    {
        float t = 0f;
        while (t < seconds)
        {
            t += Time.unscaledDeltaTime;
            toastTimer.localScale = new Vector3(1f - Mathf.Clamp01(t / seconds), 1f, 1f);
            yield return null;
        }
        ShowPanel(toastRoot, false, new Vector2(0, -70), 0.3f);
        toastRoutine = null;
    }

    public void HideToast()
    {
        if (toastRoutine != null) StopCoroutine(toastRoutine);
        toastRoutine = null;
        ShowPanel(toastRoot, false, new Vector2(0, -70), 0.25f);
    }

    // ------------------------------------------------------------------ modal

    void BuildModal()
    {
        var scrim = NewRect("Modal", canvasRect);
        Stretch(scrim);
        AddImage(scrim.gameObject, new Color(0, 0, 0, 0.55f), false);   // blocks taps on the 3D scene

        modalCard = NewRect("Card", scrim);
        modalCard.anchorMin = modalCard.anchorMax = new Vector2(0.5f, 0.5f);
        modalCard.pivot = new Vector2(0.5f, 0.5f);
        modalCard.sizeDelta = new Vector2(1000, 0);
        AddImage(modalCard.gameObject, new Color32(30, 34, 41, 250));
        var v = modalCard.gameObject.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(50, 50, 50, 50);
        v.spacing = 26;
        v.childAlignment = TextAnchor.UpperCenter;
        v.childControlWidth = true; v.childControlHeight = true;
        v.childForceExpandWidth = true; v.childForceExpandHeight = false;
        modalCard.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        modalCardGroup = modalCard.gameObject.AddComponent<CanvasGroup>();

        modalRoot = scrim.gameObject;
        modalRoot.SetActive(false);
    }

    RectTransform OpenModal()
    {
        for (int i = modalCard.childCount - 1; i >= 0; i--)
        {
            var child = modalCard.GetChild(i);
            child.SetParent(null, false);     // out of the layout right away
            Destroy(child.gameObject);
        }
        modalCardGroup.alpha = 0f;            // stays hidden until the content is built
        ShowPanel(modalRoot, true, Vector2.zero, 0.25f);
        modalToken++;
        StartCoroutine(ModalIn(modalToken));
        return modalCard;
    }

    /// <summary>Card pops in, then its lines fade in one after another.</summary>
    IEnumerator ModalIn(int token)
    {
        yield return null;                    // the caller fills the card this frame
        if (token != modalToken) yield break;

        var kids = new List<Transform>();
        foreach (Transform k in modalCard) kids.Add(k);
        foreach (var k in kids) Group(k.gameObject).alpha = 0f;

        Spring(modalCard.gameObject).SetScale(0.88f);
        StartCoroutine(FadeGroup(modalCardGroup, 1f, 0.22f, 0f));
        for (int i = 0; i < kids.Count; i++)
            StartCoroutine(FadeChildIn(kids[i], 0.08f + i * 0.05f));
    }

    IEnumerator FadeChildIn(Transform child, float delay)
    {
        float t = 0f;
        while (t < delay) { t += Time.unscaledDeltaTime; yield return null; }
        if (child == null) yield break;
        if (child.gameObject.activeInHierarchy) Spring(child.gameObject).SetScale(0.94f);
        yield return FadeGroup(Group(child.gameObject), 1f, 0.25f, 0f);
    }

    public void CloseModal()
    {
        modalToken++;
        Spring(modalCard.gameObject).Punch(-0.06f);
        ShowPanel(modalRoot, false, Vector2.zero, 0.22f);
    }

    /// <summary>Simple message card with one button.</summary>
    public void ShowMessage(string title, string body, string buttonLabel, Color accent, Action onContinue)
    {
        var card = OpenModal();
        var t = Label(card, title, 52, accent, TextAlignmentOptions.Center, true);
        t.textWrappingMode = TextWrappingModes.Normal;
        Label(card, body, 38, Color.white, TextAlignmentOptions.Left, false);
        AddButton(card, buttonLabel, accent, 150, () => { CloseModal(); onContinue?.Invoke(); });
    }

    /// <summary>Briefing: title, story, START and a small "move the site" button.</summary>
    public void ShowBriefing(string title, string body, Action onStart, Action onReplace)
    {
        var card = OpenModal();
        AddLogo(card, 84);
        Label(card, title, 58, Color.white, TextAlignmentOptions.Center, true);
        Label(card, body, 38, Color.white, TextAlignmentOptions.Left, false);
        AddButton(card, "START", Orange, 160, () => { CloseModal(); onStart?.Invoke(); });
        AddButton(card, "Re-place", new Color(1, 1, 1, 0.12f), 110, () => { CloseModal(); onReplace?.Invoke(); }, 34);
    }

    /// <summary>
    /// Multiple-choice question. Wrong answers turn red and show why; the trainee
    /// can try again. The right answer turns green, then CONTINUE returns the
    /// number of attempts (1 = first try).
    /// </summary>
    public void ShowMCQ(string header, string question, string[] options, int correctIndex, string[] explanations, Action<int> onDone)
    {
        var card = OpenModal();
        Label(card, header, 30, Orange, TextAlignmentOptions.Center, true).characterSpacing = 4;
        Label(card, question, 44, Color.white, TextAlignmentOptions.Left, true);

        var buttons = new List<Button>();
        TextMeshProUGUI explain = null;
        GameObject cont = null;
        int attempts = 0;
        bool solved = false;
        string[] letters = { "A", "B", "C", "D", "E" };

        for (int i = 0; i < options.Length; i++)
        {
            int index = i;
            var b = AddButton(card, letters[i] + ")  " + options[i], new Color(1, 1, 1, 0.1f), 0, null, 36, TextAlignmentOptions.Left);
            buttons.Add(b);
            b.onClick.AddListener(() =>
            {
                if (solved) return;
                attempts++;
                bool right = index == correctIndex;
                ColorTo(b.GetComponent<Image>(), right ? Green : Red, 0.18f);
                b.interactable = false;
                var spring = Spring(b.gameObject);
                if (right) spring.Punch(0.1f); else spring.Wiggle(4f);

                explain.gameObject.SetActive(true);
                explain.color = right ? new Color32(140, 235, 180, 255) : new Color32(255, 170, 170, 255);
                explain.text = (right ? "Correct. " : "Wrong. ") + explanations[index];
                StartCoroutine(FadeTextIn(explain, 0.3f));

                var fx = EffectsManager.Instance;
                if (fx != null) { if (right) fx.Chime(); else fx.Buzz(); }

                if (right)
                {
                    solved = true;
                    foreach (var other in buttons) other.interactable = false;
                    PopIn(cont, 0.15f);
                }
            });
        }

        explain = Label(card, "", 36, Color.white, TextAlignmentOptions.Left, false);
        explain.gameObject.SetActive(false);
        cont = AddButton(card, "CONTINUE", Orange, 150, () => { CloseModal(); onDone?.Invoke(attempts); }).gameObject;
        cont.SetActive(false);
    }

    /// <summary>
    /// PPE picker: big icon cards in a 2-column grid. CONFIRM checks the choice;
    /// explanations appear only for mistakes. Returns the total number of
    /// mistakes (wrong picks + missing items, summed over every CONFIRM).
    /// </summary>
    public void ShowPPE(string header, string question, string subtitle, PPEItem[] items, int needed, Action<int> onDone)
    {
        var card = OpenModal();
        Label(card, header, 30, Orange, TextAlignmentOptions.Center, true).characterSpacing = 4;
        Label(card, question, 46, Color.white, TextAlignmentOptions.Center, true);
        Label(card, subtitle, 32, Muted, TextAlignmentOptions.Center, false);

        var gridRt = NewRect("Grid", card);
        var grid = gridRt.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(430, 330);
        grid.spacing = new Vector2(24, 24);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 2;
        grid.childAlignment = TextAnchor.UpperCenter;
        int rows = Mathf.CeilToInt(items.Length / 2f);
        var le = gridRt.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = rows * 330 + (rows - 1) * 24;

        bool[] selected = new bool[items.Length];
        var cardImages = new Image[items.Length];
        var ticks = new GameObject[items.Length];
        Color normal = Light;
        Color picked = new Color32(205, 243, 222, 255);
        Color wrong = new Color32(250, 205, 205, 255);
        TextMeshProUGUI feedback = null;
        TextMeshProUGUI counter = null;
        int mistakes = 0;
        bool done = false;

        Action refresh = () =>
        {
            int n = 0;
            for (int k = 0; k < items.Length; k++)
            {
                if (selected[k]) n++;
                ColorTo(cardImages[k], selected[k] ? picked : normal, 0.15f);
                if (ticks[k].activeSelf != selected[k])
                {
                    if (selected[k]) PopIn(ticks[k], 0.4f);
                    else ticks[k].SetActive(false);
                }
            }
            string text = n + " / " + needed;
            if (counter.text != text)
            {
                counter.text = text;
                Spring(counter.gameObject).Punch(0.15f);
            }
        };

        for (int i = 0; i < items.Length; i++)
        {
            int index = i;
            var cell = NewRect("Card_" + items[i].icon, gridRt);
            cardImages[i] = AddImage(cell.gameObject, normal);
            var btn = cell.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            Spring(cell.gameObject).pressable = true;

            var iconRt = NewRect("Icon", cell);
            Place(iconRt, 0.5f, 1, 0.5f, 1, new Vector2(0, -24), new Vector2(210, 210), new Vector2(0.5f, 1));
            var icon = iconRt.gameObject.AddComponent<RawImage>();
            icon.texture = Resources.Load<Texture2D>("PPE/" + items[i].icon);
            icon.raycastTarget = false;

            var label = Label(cell, items[i].label, 34, TextDark, TextAlignmentOptions.Center, true);
            Place(label.rectTransform, 0, 0, 1, 0, new Vector2(0, 14), new Vector2(-30, 86), new Vector2(0.5f, 0));

            var tickRt = NewRect("Tick", cell);
            Place(tickRt, 1, 1, 1, 1, new Vector2(-14, -14), new Vector2(76, 76), new Vector2(1, 1));
            var tick = tickRt.gameObject.AddComponent<RawImage>();
            tick.texture = Resources.Load<Texture2D>("PPE/tick");
            tick.raycastTarget = false;
            ticks[i] = tickRt.gameObject;
            ticks[i].SetActive(false);

            btn.onClick.AddListener(() =>
            {
                if (done) return;
                selected[index] = !selected[index];
                Spring(cell.gameObject).Punch(0.06f);
                feedback.gameObject.SetActive(false);
                refresh();
            });
        }

        counter = Label(card, "", 32, Muted, TextAlignmentOptions.Center, true);
        feedback = Label(card, "", 34, Color.white, TextAlignmentOptions.Left, false);
        feedback.gameObject.SetActive(false);

        Button confirm = null;
        confirm = AddButton(card, "CONFIRM", Orange, 150, () =>
        {
            if (done)
            {
                CloseModal();
                onDone?.Invoke(mistakes);
                return;
            }

            int wrongPicks = 0, missing = 0;
            var lines = new List<string>();
            for (int k = 0; k < items.Length; k++)
            {
                if (selected[k] && !items[k].correct)
                {
                    wrongPicks++;
                    ColorTo(cardImages[k], wrong, 0.2f);
                    Spring(cardImages[k].gameObject).Wiggle(5f);
                    lines.Add("<b>" + items[k].label + "</b> - " + items[k].why);
                }
                else if (!selected[k] && items[k].correct) missing++;
            }

            feedback.gameObject.SetActive(true);
            StartCoroutine(FadeTextIn(feedback, 0.3f));
            var fx = EffectsManager.Instance;
            if (wrongPicks == 0 && missing == 0)
            {
                done = true;
                if (fx != null) fx.Chime();
                for (int k = 0; k < items.Length; k++)
                    if (items[k].correct) Spring(cardImages[k].gameObject).Punch(0.1f);
                feedback.color = new Color32(140, 235, 180, 255);
                var ok = new List<string> { "<b>Correct.</b>" };
                foreach (var it in items) if (it.correct) ok.Add("<b>" + it.label + "</b> - " + it.why);
                feedback.text = string.Join("\n", ok);
                confirm.GetComponentInChildren<TextMeshProUGUI>().text = "CONTINUE";
                Spring(confirm.gameObject).Punch(0.1f);
                return;
            }

            if (fx != null) fx.Buzz();
            if (wrongPicks == 0) Spring(counter.gameObject).Wiggle(5f);
            mistakes += wrongPicks + missing;
            if (missing > 0)
                lines.Add(missing == 1 ? "1 item missing." : missing + " items missing.");
            feedback.color = new Color32(255, 185, 185, 255);
            feedback.text = string.Join("\n", lines);
        });

        refresh();
    }

    /// <summary>ARmour logo (Resources/armour_logo), centred, given height.</summary>
    void AddLogo(Transform parent, float height)
    {
        var tex = Resources.Load<Texture2D>("armour_logo");
        if (tex == null) return;
        var holder = NewRect("Logo", parent);
        holder.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
        var img = NewRect("Image", holder);
        Place(img, 0.5f, 0.5f, 0.5f, 0.5f, Vector2.zero, new Vector2(height * tex.width / tex.height, height), new Vector2(0.5f, 0.5f));
        var raw = img.gameObject.AddComponent<RawImage>();
        raw.texture = tex;
        raw.raycastTarget = false;
    }

    /// <summary>Final score card with RETRY and EXIT. The score counts up, then the rating pops in.</summary>
    public void ShowResults(int score, string rating, string responseTime, List<string> lines, Action onRetry, Action onExit)
    {
        var card = OpenModal();
        AddLogo(card, 60);
        Label(card, "RESULT", 30, Muted, TextAlignmentOptions.Center, true).characterSpacing = 6;
        var scoreLabel = Label(card, "0 / 100", 110, Color.white, TextAlignmentOptions.Center, true);
        Color rc = rating == "EXCELLENT" ? Green : rating == "GOOD" ? Amber : Red;
        var ratingLabel = Label(card, rating, 52, rc, TextAlignmentOptions.Center, true);
        ratingLabel.alpha = 0f;
        if (!string.IsNullOrEmpty(responseTime))
            Label(card, responseTime, 34, Muted, TextAlignmentOptions.Center, false);
        Label(card, string.Join("\n", lines), 32, Color.white, TextAlignmentOptions.Left, false).lineSpacing = 12;
        AddButton(card, "RETRY", Orange, 150, () => { CloseModal(); onRetry?.Invoke(); });
        AddButton(card, "EXIT", new Color(1, 1, 1, 0.12f), 120, () => { CloseModal(); onExit?.Invoke(); }, 38);

        StartCoroutine(CountUp(scoreLabel, ratingLabel, score));
    }

    IEnumerator CountUp(TextMeshProUGUI scoreLabel, TextMeshProUGUI ratingLabel, int score)
    {
        float t = 0f;
        while (t < 0.45f) { t += Time.unscaledDeltaTime; yield return null; }

        float dur = Mathf.Lerp(0.6f, 1.6f, score / 100f);
        int shown = -1;
        t = 0f;
        while (t < dur && scoreLabel != null)
        {
            t += Time.unscaledDeltaTime;
            int v = Mathf.RoundToInt(score * Easing.OutCubic(t / dur));
            if (v != shown)
            {
                shown = v;
                scoreLabel.text = v + " / 100";
            }
            yield return null;
        }
        if (scoreLabel == null) yield break;
        scoreLabel.text = score + " / 100";
        Spring(scoreLabel.gameObject).Punch(0.12f);

        ratingLabel.alpha = 1f;
        Spring(ratingLabel.gameObject).SetScale(0.4f);
        var fx = EffectsManager.Instance;
        if (fx != null) { if (score >= 50) fx.Chime(); else fx.Buzz(); }
    }

    // ------------------------------------------------------- danger edge/flash

    void BuildDangerEdge()
    {
        var rt = NewRect("DangerEdge", canvasRect);
        Stretch(rt);
        dangerEdge = rt.gameObject.AddComponent<RawImage>();
        dangerEdge.texture = MakeVignette(128);
        dangerEdge.raycastTarget = false;
        dangerEdge.color = new Color(1, 0, 0, 0);

        // Soft full-screen tint used by Flash() (under all panels)
        var tint = NewRect("ScreenTint", canvasRect);
        Stretch(tint);
        screenTint = tint.gameObject.AddComponent<Image>();
        screenTint.raycastTarget = false;
        screenTint.color = new Color(1, 0, 0, 0);
    }

    public void SetDanger(bool on) => dangerOn = on;

    /// <summary>A quick full-edge flash (e.g. when the alarm goes off). 'pulses' repeats it.</summary>
    public void Flash(Color c, int pulses = 1)
    {
        StartCoroutine(FlashRoutine(c, Mathf.Max(1, pulses)));
    }

    IEnumerator FlashRoutine(Color c, int pulses)
    {
        for (int i = 0; i < pulses; i++)
        {
            flashColor = c;
            flashAlpha = 1f;
            float t = 0f;
            while (t < 0.32f) { t += Time.unscaledDeltaTime; yield return null; }
        }
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;

        // Danger edge: eases in/out instead of switching on and off
        dangerK = Mathf.MoveTowards(dangerK, dangerOn ? 1f : 0f, dt * 3f);
        float a = dangerK * (0.55f + 0.35f * Mathf.Sin(Time.time * 8f));
        Color c = Red;
        if (flashAlpha > 0f)
        {
            flashAlpha = Mathf.Max(0f, flashAlpha - dt * 1.6f);
            float f = Easing.OutCubic(flashAlpha);
            if (f > a) { a = f; c = flashColor; }
        }
        c.a = a;
        dangerEdge.color = c;

        Color tc = flashColor;
        tc.a = 0.28f * flashAlpha * flashAlpha;
        screenTint.color = tc;

        // Detector pill colour blends; in DANGER it breathes brighter/darker
        if (Detector != null && Detector.root.activeSelf)
        {
            Color target = pillTarget;
            if (lastStatus == "DANGER")
                target = Color.Lerp(pillTarget, new Color(1f, 0.55f, 0.55f), 0.25f + 0.25f * Mathf.Sin(Time.time * 10f));
            Detector.statusPill.color = Color.Lerp(Detector.statusPill.color, target, Easing.Damp(12f, dt));

            // Air-test bar glides to its value; fills amber -> green
            if (Detector.progressRoot.activeSelf)
            {
                airShown = Mathf.Lerp(airShown, airTarget, Easing.Damp(airTarget < airShown ? 14f : 9f, dt));
                var parent = (RectTransform)Detector.progressRoot.transform;
                Detector.progressFill.rectTransform.sizeDelta = new Vector2(parent.rect.width * airShown, 0);
                Detector.progressFill.color = Color.Lerp(Amber, Green, airShown);
            }
        }

        // RAISE ALARM breathes to draw the eye
        if (alarmButton.activeSelf)
            Spring(alarmButton).restScale = 1f + 0.045f * Mathf.Sin(Time.time * 7f);
    }

    // ============================================================ animation

    bool IsShown(GameObject go) => go.activeSelf && !hiding.Contains(go);

    /// <summary>
    /// Fades a panel in/out with a short slide from 'slide' (pixels, relative to its
    /// home position). 'pop' > 0 also springs the scale up from (1 - pop).
    /// </summary>
    void ShowPanel(GameObject go, bool show, Vector2 slide, float duration, float pop = 0f)
    {
        if (show == IsShown(go)) return;
        var rt = (RectTransform)go.transform;
        if (!homePos.ContainsKey(rt)) homePos[rt] = rt.anchoredPosition;
        if (panelAnims.TryGetValue(go, out var running) && running != null) StopCoroutine(running);
        panelAnims[go] = StartCoroutine(PanelRoutine(go, rt, show, slide, duration, pop));
    }

    IEnumerator PanelRoutine(GameObject go, RectTransform rt, bool show, Vector2 slide, float duration, float pop)
    {
        var cg = Group(go);
        Vector2 home = homePos[rt];
        if (show)
        {
            hiding.Remove(go);
            if (!go.activeSelf)
            {
                cg.alpha = 0f;
                rt.anchoredPosition = home + slide;
                go.SetActive(true);
            }
            cg.blocksRaycasts = true;
            cg.interactable = true;
            if (pop > 0f) Spring(go).SetScale(1f - pop);
        }
        else
        {
            hiding.Add(go);
            cg.blocksRaycasts = false;      // taps go straight through while it fades
            cg.interactable = false;
        }

        float a0 = cg.alpha, a1 = show ? 1f : 0f;
        Vector2 p0 = rt.anchoredPosition, p1 = show ? home : home + slide * 0.6f;
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float k = Easing.OutCubic(t / duration);
            cg.alpha = Mathf.Lerp(a0, a1, k);
            rt.anchoredPosition = Vector2.LerpUnclamped(p0, p1, k);
            yield return null;
        }
        cg.alpha = a1;
        rt.anchoredPosition = home;
        if (!show)
        {
            go.SetActive(false);
            hiding.Remove(go);
        }
        panelAnims.Remove(go);
    }

    IEnumerator FadeGroup(CanvasGroup cg, float to, float duration, float delay)
    {
        float t = 0f;
        while (t < delay) { t += Time.unscaledDeltaTime; yield return null; }
        float from = cg.alpha;
        t = 0f;
        while (t < duration && cg != null)
        {
            t += Time.unscaledDeltaTime;
            cg.alpha = Mathf.Lerp(from, to, Easing.OutCubic(t / duration));
            yield return null;
        }
        if (cg != null) cg.alpha = to;
    }

    IEnumerator FadeTextIn(TMP_Text text, float duration)
    {
        float t = 0f;
        text.alpha = 0f;
        while (t < duration && text != null)
        {
            t += Time.unscaledDeltaTime;
            text.alpha = Easing.OutCubic(t / duration);
            yield return null;
        }
        if (text != null) text.alpha = 1f;
    }

    /// <summary>Activates an element and springs it up from a smaller size.</summary>
    void PopIn(GameObject go, float from)
    {
        go.SetActive(true);
        Group(go).alpha = 1f;
        Spring(go).SetScale(from);
    }

    /// <summary>Smoothly blends a UI colour (restarts if called again for the same graphic).</summary>
    void ColorTo(Graphic g, Color to, float duration)
    {
        if (g == null) return;
        if (colorAnims.TryGetValue(g, out var running) && running != null) StopCoroutine(running);
        colorAnims[g] = StartCoroutine(ColorRoutine(g, to, duration));
    }

    IEnumerator ColorRoutine(Graphic g, Color to, float duration)
    {
        Color from = g.color;
        float t = 0f;
        while (t < duration && g != null)
        {
            t += Time.unscaledDeltaTime;
            g.color = Color.Lerp(from, to, Easing.OutCubic(t / duration));
            yield return null;
        }
        if (g != null) g.color = to;
        colorAnims.Remove(g);
    }

    static UISpring Spring(GameObject go)
    {
        var s = go.GetComponent<UISpring>();
        return s != null ? s : go.AddComponent<UISpring>();
    }

    static CanvasGroup Group(GameObject go)
    {
        var g = go.GetComponent<CanvasGroup>();
        return g != null ? g : go.AddComponent<CanvasGroup>();
    }

    // ================================================================ helpers

    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    static void Place(RectTransform rt, float minX, float minY, float maxX, float maxY, Vector2 pos, Vector2 size, Vector2 pivot)
    {
        rt.anchorMin = new Vector2(minX, minY);
        rt.anchorMax = new Vector2(maxX, maxY);
        rt.pivot = pivot;
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
    }

    Image AddImage(GameObject go, Color color, bool roundedCorners = true)
    {
        var img = go.AddComponent<Image>();
        if (roundedCorners)
        {
            img.sprite = rounded;
            img.type = Image.Type.Sliced;
        }
        img.color = color;
        return img;
    }

    static TextMeshProUGUI Label(Transform parent, string text, float size, Color color, TextAlignmentOptions align, bool bold)
    {
        var rt = NewRect("Text", parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        t.textWrappingMode = TextWrappingModes.Normal;
        t.richText = true;
        t.raycastTarget = false;
        return t;
    }

    Button MakeButton(Transform parent, string label, Color bg, Color fg, float fontSize, Action onClick,
                      TextAlignmentOptions align = TextAlignmentOptions.Center)
    {
        var rt = NewRect("Button", parent);
        var img = AddImage(rt.gameObject, bg);
        var b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        var colors = b.colors;
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        colors.disabledColor = Color.white;   // keep our red/green tint when disabled
        colors.fadeDuration = 0.08f;
        b.colors = colors;
        Spring(rt.gameObject).pressable = true;
        var t = Label(rt, label, fontSize, fg, align, true);
        Stretch(t.rectTransform);
        t.margin = new Vector4(36, 12, 36, 12);
        if (onClick != null) b.onClick.AddListener(() => onClick());
        return b;
    }

    /// <summary>Button inside a vertical layout (height 0 = fit to its text).</summary>
    Button AddButton(Transform parent, string label, Color bg, float height, Action onClick, float fontSize = 44,
                     TextAlignmentOptions align = TextAlignmentOptions.Center)
    {
        var b = MakeButton(parent, label, bg, Color.white, fontSize, onClick, align);
        var le = b.gameObject.AddComponent<LayoutElement>();
        if (height > 0) le.preferredHeight = height;
        else
        {
            // Size to the wrapped text: measure against the card's inner width
            var t = b.GetComponentInChildren<TextMeshProUGUI>();
            Vector2 pref = t.GetPreferredValues(label, 900 - 72, 0);
            le.preferredHeight = Mathf.Max(120, pref.y + 50);
        }
        return b;
    }

    static Sprite MakeRoundedSprite(int size, int radius)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float cx = Mathf.Clamp(x + 0.5f, radius, size - radius);
                float cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                float a = Mathf.Clamp01(radius - dist + 0.5f);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
        }
        tex.SetPixels32(px);
        tex.Apply();
        float b = Mathf.Min(radius, size / 2 - 1);
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                             SpriteMeshType.FullRect, new Vector4(b, b, b, b));
    }

    static Texture2D MakeVignette(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = Mathf.Abs((x + 0.5f) / size * 2f - 1f);
                float v = Mathf.Abs((y + 0.5f) / size * 2f - 1f);
                float edge = Mathf.Max(u, v);                      // square-ish edge
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.72f, 1f, edge));
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
        }
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }
}
