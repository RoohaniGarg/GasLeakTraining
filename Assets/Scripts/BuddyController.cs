using System;
using System.Collections;
using UnityEngine;

public enum BuddyState
{
    Idle,
    Walking,
    InConfinedSpace,
    Collapsed,
    BeingPulledOut,
    Rescued,
    AtSafeZone
}

/// <summary>
/// Moves the buddy "Ramesh" around the site; WorkerAnimator animates his body.
/// All positions are LOCAL to TrainingEnvironmentRoot (Ramesh is its direct child),
/// so everything works wherever the site is placed and however it is turned.
/// Walking: turns on the spot first, then speeds up and slows down smoothly.
/// </summary>
public class BuddyController : MonoBehaviour
{
    [Header("Positions (local to TrainingEnvironmentRoot)")]
    public Vector3 manholeCenter = new Vector3(0.2f, 0f, 0.15f);
    [Tooltip("Where Ramesh stands before climbing in (front gap between tripod legs)")]
    public Vector3 manholeApproach = new Vector3(0.2f, 0f, -0.03f);
    public Vector3 safeZone = new Vector3(-0.34f, 0f, -0.42f);   // on the green disc, clear of the sign pole

    [Header("Motion (tabletop metres / seconds)")]
    public float walkSpeed = 0.14f;
    public float weakWalkSpeed = 0.09f;
    public float turnSpeed = 220f;           // degrees per second
    public float collarHeight = 0.045f;      // he steps up onto the concrete collar
    public float descendDepth = 0.38f;       // deeper than his height, so he disappears
    public float climbTime = 3.2f;
    public float pullTime = 3.5f;
    [Tooltip("How far back from the entry he is laid down (so he isn't lying over the hole)")]
    public float lieBack = 0.22f;

    public BuddyState State { get; private set; } = BuddyState.Idle;
    public event Action<BuddyState> OnStateChanged;

    private Vector3 startPos;
    private Quaternion startRot;
    private Coroutine routine;
    private WorkerAnimator anim;
    private GameObject[] ppe;

    void Awake()
    {
        startPos = transform.localPosition;
        startRot = transform.localRotation;
        anim = GetComponent<WorkerAnimator>();
        if (anim == null) anim = gameObject.AddComponent<WorkerAnimator>();

        // Protective equipment parts (hidden until the PPE step is done)
        var list = new System.Collections.Generic.List<GameObject>();
        foreach (var t in GetComponentsInChildren<Transform>(true))
            if (t.name.StartsWith("PPE_")) list.Add(t.gameObject);
        ppe = list.ToArray();
        ShowPPE(false);
    }

    // ------------------------------------------------------------ public API

    public void ResetBuddy()
    {
        StopRoutine();
        transform.localPosition = startPos;
        transform.localRotation = startRot;
        anim.ResetPose();
        ShowPPE(false);
        SetState(BuddyState.Idle);
    }

    /// <summary>Shows / hides his breathing apparatus, harness and gas detector.</summary>
    public void ShowPPE(bool show)
    {
        if (ppe == null) return;
        foreach (var g in ppe) if (g != null) g.SetActive(show);
    }

    public void WalkTo(Vector3 localTarget, Action onArrive = null) => Run(WalkRoutine(localTarget, onArrive));
    public void EnterConfinedSpace() => Run(EnterRoutine());
    public void Collapse() => Run(CollapseRoutine());
    public void PullOut() => Run(PullOutRoutine());
    public void GoToSafeZone() => Run(SafeZoneRoutine());

    // ------------------------------------------------------------ routines

    IEnumerator WalkRoutine(Vector3 target, Action onArrive)
    {
        SetState(BuddyState.Walking);
        anim.SetMode(WorkerPose.Normal);
        yield return Walk(target, walkSpeed);
        SetState(BuddyState.Idle);
        onArrive?.Invoke();
    }

    IEnumerator EnterRoutine()
    {
        SetState(BuddyState.Walking);
        anim.SetMode(WorkerPose.Normal);
        yield return Walk(manholeApproach, walkSpeed);
        yield return TurnTowards(manholeCenter);
        yield return new WaitForSeconds(0.4f);

        // Step up onto the collar, over to the ladder, then climb down out of sight
        anim.SetMode(WorkerPose.Climb);
        Vector3 from = transform.localPosition;
        float t = 0f;
        while (t < climbTime)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / climbTime);
            float step = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(k / 0.3f));
            Vector3 flat = Vector3.Lerp(from, manholeCenter, step);
            float y = k < 0.3f
                ? collarHeight * Mathf.Sin(step * Mathf.PI * 0.5f)
                : Mathf.Lerp(collarHeight, -descendDepth, (k - 0.3f) / 0.7f);
            transform.localPosition = new Vector3(flat.x, y, flat.z);
            yield return null;
        }
        transform.localPosition = new Vector3(manholeCenter.x, -descendDepth, manholeCenter.z);
        anim.SetMode(WorkerPose.Normal);
        SetState(BuddyState.InConfinedSpace);
    }

    IEnumerator CollapseRoutine()
    {
        anim.SetMode(WorkerPose.Collapsed);
        yield return new WaitForSeconds(0.8f);
        SetState(BuddyState.Collapsed);
    }

    IEnumerator PullOutRoutine()
    {
        SetState(BuddyState.BeingPulledOut);

        Vector3 away = manholeApproach - manholeCenter;
        away.y = 0f;
        // The real (Mixamo) model is set down on his feet just clear of the opening -
        // the lying clip floated and slanted on the phone, so it is not used.
        bool standUp = anim.HasModel;
        Vector3 lieSpot = manholeApproach + away.normalized * (standUp ? 0.1f : lieBack);

        // Hanging limp on the lifeline, straight under the tripod, facing where he'll be laid down
        anim.SetMode(WorkerPose.Hanging);
        transform.localRotation = Quaternion.LookRotation(-away.normalized, Vector3.up);
        float startY = transform.localPosition.y;
        float t = 0f;
        while (t < pullTime)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / pullTime));
            transform.localPosition = new Vector3(manholeCenter.x, Mathf.Lerp(startY, 0.07f, k), manholeCenter.z);
            yield return null;
        }

        // Swing him clear of the opening, then lay him on his back (shape figure)
        // or set him down on his feet, dazed (real model)
        Vector3 a = transform.localPosition;
        t = 0f;
        const float swing = 1.6f;
        while (t < swing)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / swing));
            Vector3 flat = Vector3.Lerp(a, lieSpot, k);
            float y = Mathf.Lerp(a.y, 0f, k) + 0.02f * Mathf.Sin(k * Mathf.PI);
            transform.localPosition = new Vector3(flat.x, y, flat.z);
            if (standUp)
            {
                if (k > 0.85f && anim.Mode != WorkerPose.Weak) anim.SetMode(WorkerPose.Weak);   // feet touch down
            }
            else if (k > 0.35f && anim.Mode != WorkerPose.Lying) anim.SetMode(WorkerPose.Lying);
            yield return null;
        }
        transform.localPosition = lieSpot;
        SetState(BuddyState.Rescued);
    }

    IEnumerator SafeZoneRoutine()
    {
        // Gets up slowly (the real model is already on his feet, just catches his breath)...
        if (anim.HasModel) yield return new WaitForSeconds(0.6f);
        else
        {
            anim.SetMode(WorkerPose.Normal);
            yield return new WaitForSeconds(1.6f);
        }

        // ...and walks unsteadily to the assembly point, then turns to face the site and rests
        SetState(BuddyState.Walking);
        anim.SetMode(WorkerPose.Weak);
        yield return Walk(safeZone, weakWalkSpeed);
        yield return TurnTowards(Vector3.zero, 3f);
        anim.SetMode(WorkerPose.Recovering);
        SetState(BuddyState.AtSafeZone);
    }

    // ------------------------------------------------------------ helpers

    IEnumerator Walk(Vector3 target, float maxSpeed)
    {
        target.y = 0f;
        Vector3 start = transform.localPosition;
        start.y = 0f;
        transform.localPosition = start;

        // Turn on the spot first if the target is off to the side
        yield return TurnTowards(target, 40f);

        float speed = 0f;
        while (true)
        {
            Vector3 p = transform.localPosition;
            Vector3 to = target - p;
            to.y = 0f;
            float dist = to.magnitude;
            if (dist < 0.004f) break;

            // Ease in, ease out
            float wanted = Mathf.Min(maxSpeed, dist * 2.5f + 0.01f);
            speed = Mathf.MoveTowards(speed, wanted, maxSpeed * 3f * Time.deltaTime);

            Quaternion look = Quaternion.LookRotation(to / dist, Vector3.up);
            transform.localRotation = Quaternion.RotateTowards(transform.localRotation, look, turnSpeed * Time.deltaTime);

            transform.localPosition = p + (to / dist) * Mathf.Min(dist, speed * Time.deltaTime);
            yield return null;
        }
        transform.localPosition = target;
    }

    IEnumerator TurnTowards(Vector3 localPoint, float tolerance = 1f)
    {
        Vector3 dir = localPoint - transform.localPosition;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) yield break;
        Quaternion look = Quaternion.LookRotation(dir.normalized, Vector3.up);
        while (Quaternion.Angle(transform.localRotation, look) > tolerance)
        {
            transform.localRotation = Quaternion.RotateTowards(transform.localRotation, look, turnSpeed * 0.7f * Time.deltaTime);
            yield return null;
        }
    }

    void Run(IEnumerator r)
    {
        StopRoutine();
        routine = StartCoroutine(r);
    }

    void StopRoutine()
    {
        if (routine != null) StopCoroutine(routine);
        routine = null;
    }

    void SetState(BuddyState s)
    {
        State = s;
        Debug.Log("BuddyController: " + s);
        OnStateChanged?.Invoke(s);
    }
}
