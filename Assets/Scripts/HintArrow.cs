using UnityEngine;

/// <summary>
/// Phase 6: A bright yellow arrow that bobs above an object to show the
/// trainee where to look / tap. Built from code (no prefab needed).
/// ScenarioManager creates one and calls PointAt(target) / Hide().
///
/// Phase 9: drops in with a bounce, shrinks away when hidden, glides between
/// targets instead of jumping, and sends soft yellow rings out under the target.
/// </summary>
public class HintArrow : MonoBehaviour
{
    [Tooltip("Arrow size at tabletop scale (metres)")]
    public float size = 0.07f;
    public float bobHeight = 0.025f;
    public float bobSpeed = 4f;
    public float spinSpeed = 90f;
    [Tooltip("Seconds between the rings under the target (0 = off)")]
    public float ringInterval = 1.2f;

    private Transform target;
    private Transform scaleReference;   // TrainingEnvironmentRoot (follows its scale)
    private float topOffset;
    private GameObject visual;

    private float appear;               // 0 = hidden, 1 = fully shown
    private bool hiding, snap;
    private Vector3 lastTargetPos;
    private float ringTimer;
    private float ringRadius = 0.12f;

    public bool IsShowing => target != null;

    /// <summary>Creates the arrow. 'template' is any opaque lit material in the scene (colour is replaced).</summary>
    public static HintArrow Create(Material template, Transform scaleReference)
    {
        var go = new GameObject("HintArrow");
        var arrow = go.AddComponent<HintArrow>();
        arrow.scaleReference = scaleReference;
        arrow.BuildVisual(template);
        go.SetActive(false);
        return arrow;
    }

    void BuildVisual(Material template)
    {
        Material mat = template != null ? new Material(template) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
        Color yellow = new Color(1f, 0.85f, 0.05f);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", yellow);
        if (mat.HasProperty("_EmissionColor"))
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", yellow * 0.6f);
        }

        visual = new GameObject("Visual");
        visual.transform.SetParent(transform, false);

        // Arrow head: a 4-sided pyramid pointing down (tip at local origin)
        var head = new GameObject("Head");
        head.transform.SetParent(visual.transform, false);
        head.AddComponent<MeshFilter>().sharedMesh = BuildPyramid();
        head.AddComponent<MeshRenderer>().sharedMaterial = mat;

        // Shaft: a thin box above the head
        var shaft = GameObject.CreatePrimitive(PrimitiveType.Cube);
        shaft.name = "Shaft";
        Destroy(shaft.GetComponent<Collider>());   // must never block taps
        shaft.transform.SetParent(visual.transform, false);
        shaft.transform.localPosition = new Vector3(0f, 0.9f, 0f);
        shaft.transform.localScale = new Vector3(0.28f, 0.8f, 0.28f);
        shaft.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }

    static Mesh BuildPyramid()
    {
        // Tip at (0,0,0), square base at y = 0.5
        Vector3 tip = Vector3.zero;
        float h = 0.5f, w = 0.3f;
        Vector3 a = new Vector3(-w, h, -w), b = new Vector3(w, h, -w), c = new Vector3(w, h, w), d = new Vector3(-w, h, w);
        Vector3[] v =
        {
            tip, a, b,   tip, b, c,   tip, c, d,   tip, d, a,   // sides (facing out)
            a, c, b,     a, d, c                                // base (facing up)
        };
        int[] tris = new int[v.Length];
        for (int i = 0; i < tris.Length; i++) tris[i] = i;
        var mesh = new Mesh { name = "HintArrowHead" };
        mesh.vertices = v;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>Show the arrow above 't' (uses its collider to find the top).</summary>
    public void PointAt(Transform t)
    {
        if (t == null) { Hide(); return; }
        target = t;
        var col = t.GetComponent<Collider>();
        float scale = scaleReference != null ? scaleReference.lossyScale.y : 1f;
        topOffset = col != null ? (col.bounds.max.y - t.position.y) : 0.3f * scale;
        topOffset += 0.03f * scale;
        ringRadius = col != null
            ? Mathf.Clamp(Mathf.Max(col.bounds.extents.x, col.bounds.extents.z) / Mathf.Max(0.0001f, scale) * 1.4f, 0.08f, 0.2f)
            : 0.12f;
        hiding = false;
        if (!gameObject.activeSelf)
        {
            appear = 0f;
            snap = true;
            ringTimer = 0.35f;              // first ring shortly after the arrow lands
            gameObject.SetActive(true);
        }
    }

    public void Hide()
    {
        if (target != null) lastTargetPos = target.position;
        target = null;
        if (gameObject.activeSelf) hiding = true;
    }

    void LateUpdate()
    {
        if (target != null && !target.gameObject.activeInHierarchy) Hide();

        float dt = Time.deltaTime;
        if (hiding)
        {
            appear = Mathf.MoveTowards(appear, 0f, dt / 0.2f);
            if (appear <= 0f)
            {
                hiding = false;
                gameObject.SetActive(false);
                return;
            }
        }
        else appear = Mathf.MoveTowards(appear, 1f, dt / 0.45f);

        float scale = scaleReference != null ? scaleReference.lossyScale.y : 1f;
        Vector3 basePos = target != null ? target.position : lastTargetPos;
        float bob = (0.5f + 0.5f * Mathf.Sin(Time.time * bobSpeed)) * bobHeight * scale;
        float drop = hiding ? 0f : (1f - Easing.OutCubic(appear)) * 0.12f * scale;   // falls in from above
        Vector3 desired = basePos + Vector3.up * (topOffset + bob + drop);

        if (snap) { transform.position = desired; snap = false; }
        else transform.position = Vector3.Lerp(transform.position, desired, Easing.Damp(10f, dt));

        float grow = hiding ? Easing.InQuad(appear) : Easing.OutBack(appear, 2.2f);
        transform.localScale = Vector3.one * size * scale * Mathf.Max(0f, grow);
        visual.transform.Rotate(0f, spinSpeed * dt, 0f, Space.Self);

        // Soft rings on the floor under the target
        if (target != null && ringInterval > 0f && EffectsManager.Instance != null)
        {
            ringTimer -= dt;
            if (ringTimer <= 0f)
            {
                ringTimer = ringInterval;
                EffectsManager.Instance.Ring(target.position, new Color(1f, 0.85f, 0.1f, 0.75f), ringRadius, 1.1f);
            }
        }
    }
}
