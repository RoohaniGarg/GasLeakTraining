using System.Collections;
using UnityEngine;

/// <summary>
/// TEMPORARY (Phase 5 only): lets you test tapping + Ramesh's behaviour on the phone
/// before the real UI (Phase 6) and training flow (Phase 7) exist.
/// Shows a message bar at the top of the screen. Remove/disable in Phase 7.
///
/// Try this order:
///   tap any object          -> it glows + its name appears
///   tap the TRIPOD          -> lifeline attaches, Ramesh walks to the manhole
///   tap RAMESH              -> he climbs in; 3 s later he collapses
///   tap the TRIPOD again    -> winch pulls him out, he goes to the assembly point
///   tap the RED VALVE       -> gas shuts off, zones fade
///   tap the PIPE            -> leak starts again
/// </summary>
public class Phase5TestDriver : MonoBehaviour
{
    public ScenarioManager scenario;
    public BuddyController buddy;
    public Lifeline lifeline;
    public float collapseDelay = 3f;

    private string message = "Phase 5 test: tap any object";

    void OnEnable()  { InteractableObject.OnTapped += HandleTap; }
    void OnDisable() { InteractableObject.OnTapped -= HandleTap; }

    void HandleTap(InteractableObject obj)
    {
        string result = "";

        switch (obj.id)
        {
            case InteractableId.RetrievalTripod:
                if (!lifeline.IsAttached && buddy.State == BuddyState.Idle)
                {
                    lifeline.Attach();
                    buddy.WalkTo(buddy.manholeApproach);
                    result = "  ->  lifeline attached, Ramesh goes to the entry";
                }
                else if (buddy.State == BuddyState.InConfinedSpace || buddy.State == BuddyState.Collapsed)
                {
                    buddy.PullOut();
                    StartCoroutine(AfterRescue());
                    result = "  ->  winching Ramesh out";
                }
                break;

            case InteractableId.Worker:
                if (!lifeline.IsAttached)
                    result = "  ->  attach his lifeline first (tap the tripod)";
                else if (buddy.State == BuddyState.Idle)
                {
                    buddy.EnterConfinedSpace();
                    StartCoroutine(CollapseLater());
                    result = "  ->  Ramesh enters the confined space";
                }
                break;

            case InteractableId.EmergencyShutoff:
                if (scenario.GasLeak != null) scenario.GasLeak.StopLeak();
                if (scenario.HazardZones != null) scenario.HazardZones.Hide();
                result = "  ->  gas supply shut off";
                break;

            case InteractableId.GasSource:
                if (scenario.GasLeak != null) scenario.GasLeak.StartLeak(true);
                if (scenario.HazardZones != null) scenario.HazardZones.Show();
                result = "  ->  leak restarted";
                break;
        }

        Show(obj.displayName + result);
    }

    IEnumerator CollapseLater()
    {
        while (buddy.State != BuddyState.InConfinedSpace) yield return null;
        yield return new WaitForSeconds(collapseDelay);
        buddy.Collapse();
        Show("!! GAS ALARM - Ramesh is not responding! Tap the tripod winch");
    }

    IEnumerator AfterRescue()
    {
        while (buddy.State != BuddyState.Rescued) yield return null;
        yield return new WaitForSeconds(1f);
        buddy.GoToSafeZone();
        while (buddy.State != BuddyState.AtSafeZone) yield return null;
        lifeline.Detach();
        Show("Ramesh is safe at the assembly point");
    }

    void Show(string text)
    {
        message = text;
        Debug.Log("Phase5TestDriver: " + text);
    }

    void OnGUI()
    {
        if (string.IsNullOrEmpty(message)) return;
        var style = new GUIStyle(GUI.skin.box)
        {
            fontSize = Mathf.RoundToInt(Screen.height * 0.022f),
            wordWrap = true,
            alignment = TextAnchor.MiddleCenter
        };
        style.normal.textColor = Color.white;
        float w = Screen.width * 0.92f;
        float h = Screen.height * 0.08f;
        GUI.Box(new Rect((Screen.width - w) / 2f, Screen.height * 0.05f, w, h), message, style);
    }
}

