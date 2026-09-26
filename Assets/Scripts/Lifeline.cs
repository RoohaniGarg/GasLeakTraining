using UnityEngine;

/// <summary>
/// Phase 5: The orange rescue line from the tripod winch to Ramesh's harness.
/// Lives on RetrievalTripod (with a LineRenderer). Attach() shows it, Detach() hides it.
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

    public bool IsAttached { get; private set; }

    private LineRenderer line;

    void Awake()
    {
        line = GetComponent<LineRenderer>();
        line.positionCount = 2;
        line.useWorldSpace = true;
        line.enabled = false;
    }

    public void Attach()
    {
        IsAttached = true;
        line.enabled = true;
        UpdateLine();
        Debug.Log("Lifeline: attached");
    }

    public void Detach()
    {
        IsAttached = false;
        line.enabled = false;
        Debug.Log("Lifeline: detached");
    }

    void LateUpdate()
    {
        if (IsAttached) UpdateLine();
    }

    void UpdateLine()
    {
        if (buddy == null) return;
        float w = width * transform.lossyScale.x;   // follows the grow-in animation
        line.startWidth = w;
        line.endWidth = w;
        line.SetPosition(0, transform.TransformPoint(anchorLocal));
        line.SetPosition(1, buddy.TransformPoint(buddyAttachLocal));
    }
}
