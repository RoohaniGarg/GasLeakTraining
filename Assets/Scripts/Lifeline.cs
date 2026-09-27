using UnityEngine;

/// <summary>
/// Phase 5: The orange rescue line from the tripod winch to Ramesh's harness.
/// Lives on RetrievalTripod (with a LineRenderer). Attach() shows it, Detach() hides it.
///
/// Phase 9: the rope is drawn as a curve. It shoots out to Ramesh on Attach(),
/// hangs with a slight sag that sways, pulls taut while he is winched out,
/// and reels back to the tripod on Detach().
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class Lifeline : MonoBehaviour
{
    public Transform buddy;
    [Tooltip("Tripod apex, local to the tripod")]
    public Vector3 anchorLocal = new Vector3(0f, 0.32f, 0f);
    [Tooltip("Harness point on Ramesh (upper back), local to Ramesh")]
    public Vector3 buddyAttachLocal = new Vector3(0f, 0.25f, -0.02f);
    [Tooltip("Rope width at tabletop scale")]
    public float width = 0.004f;
    [Tooltip("How far the slack rope hangs down in the middle (tabletop metres)")]
    public float sag = 0.05f;
    public int segments = 16;

    public bool IsAttached { get; private set; }

    private LineRenderer line;
    private BuddyController buddyCtrl;
    private float reach;          // 0 = rope at the tripod, 1 = reaches Ramesh
    private float currentSag;

    void Awake()
    {
        line = GetComponent<LineRenderer>();
        line.positionCount = segments + 1;
        line.useWorldSpace = true;
        line.enabled = false;
    }

    public void Attach()
    {
        IsAttached = true;
        line.enabled = true;
        reach = 0f;
        currentSag = 0f;
        UpdateLine();
        Debug.Log("Lifeline: attached");
    }

    public void Detach()
    {
        IsAttached = false;       // LateUpdate reels it in, then hides it
        Debug.Log("Lifeline: detached");
    }

    void LateUpdate()
    {
        if (!line.enabled) return;
        float dt = Time.deltaTime;
        if (IsAttached) reach = Mathf.MoveTowards(reach, 1f, dt / 0.5f);
        else
        {
            reach = Mathf.MoveTowards(reach, 0f, dt / 0.45f);
            if (reach <= 0f) { line.enabled = false; return; }
        }
        UpdateLine();
    }

    void UpdateLine()
    {
        if (buddy == null) return;
        if (buddyCtrl == null) buddyCtrl = buddy.GetComponentInParent<BuddyController>();

        float scale = transform.lossyScale.x;
        float w = width * scale;                    // follows the grow-in animation
        line.startWidth = w;
        line.endWidth = w;

        Vector3 a = transform.TransformPoint(anchorLocal);
        Vector3 fullEnd = buddy.TransformPoint(buddyAttachLocal);
        float k = IsAttached ? Easing.OutCubic(reach) : Easing.InQuad(reach);
        Vector3 b = Vector3.Lerp(a, fullEnd, k);

        // Slack rope sags; taut while winching. Sways gently.
        bool taut = buddyCtrl != null && buddyCtrl.State == BuddyState.BeingPulledOut;
        float horizontal = new Vector2(b.x - a.x, b.z - a.z).magnitude / Mathf.Max(0.0001f, scale);
        float targetSag = taut ? 0f : sag * Mathf.Clamp01(horizontal / 0.15f) * k;
        currentSag = Mathf.Lerp(currentSag, targetSag, Easing.Damp(6f, Time.deltaTime));
        float sway = 1f + 0.12f * Mathf.Sin(Time.time * 1.7f);

        Vector3 down = -transform.up;
        int n = line.positionCount - 1;
        for (int i = 0; i <= n; i++)
        {
            float s = (float)i / n;
            Vector3 p = Vector3.Lerp(a, b, s) + down * (currentSag * sway * scale * 4f * s * (1f - s));
            line.SetPosition(i, p);
        }
    }
}
