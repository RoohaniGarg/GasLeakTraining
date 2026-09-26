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
    public bool ModalOpen => modalRoot != null && modalRoot.activeSelf;

    // Built objects
    private RectTransform canvasRect;
    private Sprite rounded, circle;
    private GameObject topBar;
    private TextMeshProUGUI stepText, instructionText, infoText;
    private GameObject placeButton, alarmButton;
    private Action placeAction, alarmAction;
    private GameObject toastRoot;
    private Image toastBg;
    private TextMeshProUGUI toastTitle, toastBody;
    private Coroutine toastRoutine;
    private GameObject modalRoot;
    private RectTransform modalCard;
    private RawImage dangerEdge;
    private bool dangerOn;
    private float flashAlpha;
    private Color flashColor = Color.red;

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
        var bg = AddImage(rt.gameObject, Dark);
        bg.raycastTarget = false;
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
        stepText.text = label;
        stepText.gameObject.SetActive(!string.IsNullOrEmpty(label));
        topBar.SetActive(true);
    }

    public void SetInstruction(string text)
    {
        if (instructionText.text != text) instructionText.text = text;
        topBar.SetActive(true);
    }

    public void SetInfo(string text)
    {
        if (infoText.text != text) infoText.text = text;
        infoText.gameObject.SetActive(!string.IsNullOrEmpty(text));
    }

    public void ShowTopBar(bool show) => topBar.SetActive(show);

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

    /// <summary>0..1 fill of the air-test bar (RectTransform width based).</summary>
    public void SetAirTestProgress(float k, string label)
    {
        Detector.progressRoot.SetActive(true);
        var parent = (RectTransform)Detector.progressRoot.transform;
        var fill = Detector.progressFill.rectTransform;
        fill.sizeDelta = new Vector2(parent.rect.width * Mathf.Clamp01(k), 0);
        Detector.progressLabel.text = label;
    }

    public void HideAirTestProgress() => Detector.progressRoot.SetActive(false);

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
        placeButton.SetActive(show);
    }

    public void ShowAlarmButton(bool show, Action onClick = null)
    {
        alarmAction = onClick;
        alarmButton.SetActive(show);
    }

    public void ShowDetector(bool show) => Detector.root.SetActive(show);

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
        toastRoot = rt.gameObject;
        toastRoot.SetActive(false);
    }

    public void Toast(string title, string body, ToastKind kind, float seconds = 3f)
    {
        Color c = kind == ToastKind.Good ? Green : kind == ToastKind.Bad ? Red : kind == ToastKind.Warning ? Amber : Dark;
        c.a = 0.96f;
        toastBg.color = c;
        toastTitle.text = title;
        toastBody.text = body;
        toastBody.gameObject.SetActive(!string.IsNullOrEmpty(body));
        toastRoot.SetActive(true);
        if (toastRoutine != null) StopCoroutine(toastRoutine);
        toastRoutine = StartCoroutine(HideToastLater(seconds));
    }

    IEnumerator HideToastLater(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        toastRoot.SetActive(false);
        toastRoutine = null;
    }

    public void HideToast()
    {
        if (toastRoutine != null) StopCoroutine(toastRoutine);
        toastRoutine = null;
        toastRoot.SetActive(false);
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

        modalRoot = scrim.gameObject;
        modalRoot.SetActive(false);
    }

    RectTransform OpenModal()
    {
        for (int i = modalCard.childCount - 1; i >= 0; i--)
            Destroy(modalCard.GetChild(i).gameObject);
        modalRoot.SetActive(true);
        return modalCard;
    }

    public void CloseModal() => modalRoot.SetActive(false);

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
        Label(card, "SAFETY TRAINING", 30, Orange, TextAlignmentOptions.Center, true).characterSpacing = 6;
        Label(card, title, 58, Color.white, TextAlignmentOptions.Center, true);
        Label(card, body, 38, Color.white, TextAlignmentOptions.Left, false);
        AddButton(card, "START", Orange, 160, () => { CloseModal(); onStart?.Invoke(); });
        AddButton(card, "Move the work site", new Color(1, 1, 1, 0.12f), 110, () => { CloseModal(); onReplace?.Invoke(); }, 34);
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
                b.GetComponent<Image>().color = right ? Green : Red;
                b.interactable = false;
                explain.gameObject.SetActive(true);
                explain.color = right ? new Color32(140, 235, 180, 255) : new Color32(255, 170, 170, 255);
                explain.text = (right ? "Correct! " : "Not quite. ") + explanations[index];
                if (right)
                {
                    solved = true;
                    foreach (var other in buttons) other.interactable = false;
                    cont.SetActive(true);
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
                cardImages[k].color = selected[k] ? picked : normal;
                ticks[k].SetActive(selected[k]);
            }
            counter.text = n + " of " + needed + " selected";
        };

        for (int i = 0; i < items.Length; i++)
        {
            int index = i;
            var cell = NewRect("Card_" + items[i].icon, gridRt);
            cardImages[i] = AddImage(cell.gameObject, normal);
            var btn = cell.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;

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

            btn.onClick.AddListener(() =>
            {
                if (done) return;
                selected[index] = !selected[index];
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
                    cardImages[k].color = wrong;
                    lines.Add("<b>" + items[k].label + "</b> - " + items[k].why);
                }
                else if (!selected[k] && items[k].correct) missing++;
            }

            feedback.gameObject.SetActive(true);
            if (wrongPicks == 0 && missing == 0)
            {
                done = true;
                feedback.color = new Color32(140, 235, 180, 255);
                var ok = new List<string> { "<b>All correct!</b>" };
                foreach (var it in items) if (it.correct) ok.Add("<b>" + it.label + "</b> - " + it.why);
                feedback.text = string.Join("\n", ok);
                confirm.GetComponentInChildren<TextMeshProUGUI>().text = "CONTINUE";
                return;
            }

            mistakes += wrongPicks + missing;
            if (missing > 0)
                lines.Add(missing == 1 ? "One item Ramesh needs is still missing." : missing + " items Ramesh needs are still missing.");
            feedback.color = new Color32(255, 185, 185, 255);
            feedback.text = string.Join("\n", lines) + "\nChange your choice and CONFIRM again.";
        });

        refresh();
    }

    /// <summary>Final score card with RETRY.</summary>
    public void ShowResults(int score, string rating, string responseTime, List<string> lines, Action onRetry)
    {
        var card = OpenModal();
        Label(card, "TRAINING COMPLETE", 32, Orange, TextAlignmentOptions.Center, true).characterSpacing = 6;
        Label(card, score + " / 100", 110, Color.white, TextAlignmentOptions.Center, true);
        Color rc = rating == "EXCELLENT" ? Green : rating == "GOOD" ? Amber : Red;
        Label(card, rating, 52, rc, TextAlignmentOptions.Center, true);
        if (!string.IsNullOrEmpty(responseTime))
            Label(card, responseTime, 34, Muted, TextAlignmentOptions.Center, false);
        Label(card, string.Join("\n", lines), 32, Color.white, TextAlignmentOptions.Left, false).lineSpacing = 12;
        AddButton(card, "RETRY", Orange, 150, () => { CloseModal(); onRetry?.Invoke(); });
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
    }

    public void SetDanger(bool on) => dangerOn = on;

    /// <summary>A quick full-edge flash (e.g. when the alarm goes off).</summary>
    public void Flash(Color c)
    {
        flashColor = c;
        flashAlpha = 1f;
    }

    void Update()
    {
        float a = 0f;
        Color c = Red;
        if (dangerOn) a = 0.55f + 0.35f * Mathf.Sin(Time.time * 8f);
        if (flashAlpha > 0f)
        {
            flashAlpha = Mathf.Max(0f, flashAlpha - Time.deltaTime * 1.2f);
            if (flashAlpha > a) { a = flashAlpha; c = flashColor; }
        }
        c.a = a;
        dangerEdge.color = c;
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
        b.colors = colors;
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
