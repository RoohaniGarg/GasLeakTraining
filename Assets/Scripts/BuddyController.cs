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
/// Phase 5: Moves the buddy "Ramesh" (WorkerPlaceholder) around the scene.
/// All positions are LOCAL to TrainingEnvironmentRoot (Ramesh is its direct child),
/// so everything still works wherever the scene is placed and however it is turned.
/// </summary>
public class BuddyController : MonoBehaviour
{
    [Header("Positions (local to TrainingEnvironmentRoot)")]
    public Vector3 manholeCenter = new Vector3(0.2f, 0f, 0.15f);
    [Tooltip("Where Ramesh stands before climbing in (front gap between tripod legs)")]
    public Vector3 manholeApproach = new Vector3(0.2f, 0f, -0.03f);
    public Vector3 safeZone = new Vector3(-0.36f, 0f, -0.34f);

    [Header("Motion")]
    public float walkSpeed = 0.12f;        // m/s at tabletop scale
    public float turnSpeed = 360f;         // degrees/s
    public float bobHeight = 0.006f;
    public float bobFrequency = 9f;
    public float descendDepth = 0.38f;     // deeper than his height, so he disappears
    public float climbTime = 2.5f;
    public float pullTime = 3f;

    public BuddyState State { get; private set; } = BuddyState.Idle;
    public event Action<BuddyState> OnStateChanged;

    private Vector3 startPos;
    private Quaternion startRot;
    private Coroutine routine;

    void Awake()
    {
        startPos = transform.localPosition;
        startRot = transform.localRotation;
    }

    // ------------------------------------------------------------ public API

    public void ResetBuddy()
    {
        StopRoutine();
        transform.localPosition = startPos;
        transform.localRotation = startRot;
        SetState(BuddyState.Idle);
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
        yield return Walk(target);
        SetState(BuddyState.Idle);
        onArrive?.Invoke();
    }

    IEnumerator EnterRoutine()
    {
        SetState(BuddyState.Walking);
        yield return Walk(manholeApproach);
        yield return TurnTowards(manholeCenter);

        // Step over the collar and climb down the ladder
        Vector3 from = transform.localPosition;
        float t = 0f;
        while (t < climbTime)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / climbTime);
            Vector3 horizontal = Vector3.Lerp(from, manholeCenter, Mathf.Clamp01(k * 2f));
            float y = Mathf.Lerp(0f, -descendDepth, Mathf.Clamp01((k - 0.3f) / 0.7f));
            transform.localPosition = new Vector3(horizontal.x, y, horizontal.z);
            yield return null;
        }
        transform.localPosition = new Vector3(manholeCenter.x, -descendDepth, manholeCenter.z);
        SetState(BuddyState.InConfinedSpace);
    }

    IEnumerator CollapseRoutine()
    {
        Quaternion from = transform.localRotation;
        Quaternion to = from * Quaternion.Euler(80f, 0f, 0f);   // tips over forward from the feet
        float t = 0f;
        while (t < 0.6f)
        {
            t += Time.deltaTime;
            transform.localRotation = Quaternion.Slerp(from, to, Mathf.Clamp01(t / 0.6f));
            yield return null;
        }
        SetState(BuddyState.Collapsed);
    }

    IEnumerator PullOutRoutine()
    {
        SetState(BuddyState.BeingPulledOut);

        // Hanging upright on the lifeline, straight under the tripod
        float yaw = transform.localEulerAngles.y;
        transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        float startY = transform.localPosition.y;
        transform.localPosition = new Vector3(manholeCenter.x, startY, manholeCenter.z);

        float t = 0f;
        while (t < pullTime)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / pullTime));
            transform.localPosition = new Vector3(manholeCenter.x, Mathf.Lerp(startY, 0.06f, k), manholeCenter.z);
            yield return null;
        }

        // Swing him out of the opening and lay him down at the entry
        Vector3 a = transform.localPosition;
        Quaternion upright = transform.localRotation;
        Quaternion lying = upright * Quaternion.Euler(-80f, 0f, 0f);
        t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t);
            transform.localPosition = Vector3.Lerp(a, manholeApproach, k);
            transform.localRotation = Quaternion.Slerp(upright, lying, k);
            yield return null;
        }
        transform.localPosition = manholeApproach;
        SetState(BuddyState.Rescued);
    }

    IEnumerator SafeZoneRoutine()
    {
        // Get back up, then walk (assisted) to the assembly point
        Quaternion from = transform.localRotation;
        Quaternion up = Quaternion.Euler(0f, transform.localEulerAngles.y, 0f);
        float t = 0f;
        while (t < 0.6f)
        {
            t += Time.deltaTime;
            transform.localRotation = Quaternion.Slerp(from, up, Mathf.Clamp01(t / 0.6f));
            Vector3 p = transform.localPosition;
            transform.localPosition = new Vector3(p.x, 0f, p.z);
            yield return null;
        }

        SetState(BuddyState.Walking);
        yield return Walk(safeZone);
        SetState(BuddyState.AtSafeZone);
    }

    // ------------------------------------------------------------ helpers

    IEnumerator Walk(Vector3 target)
    {
        target.y = 0f;
        float t0 = Time.time;
        while (true)
        {
            Vector3 p = transform.localPosition;
            Vector3 flat = new Vector3(p.x, 0f, p.z);
            Vector3 to = target - flat;
            float dist = to.magnitude;
            if (dist < 0.005f) break;

            Quaternion look = Quaternion.LookRotation(to / dist, Vector3.up);
            transform.localRotation = Quaternion.RotateTowards(transform.localRotation, look, turnSpeed * Time.deltaTime);

            flat += (to / dist) * Mathf.Min(dist, walkSpeed * Time.deltaTime);
            float bob = Mathf.Abs(Mathf.Sin((Time.time - t0) * bobFrequency)) * bobHeight;
            transform.localPosition = new Vector3(flat.x, bob, flat.z);
            yield return null;
        }
        transform.localPosition = target;
    }

    IEnumerator TurnTowards(Vector3 localPoint)
    {
        Vector3 dir = localPoint - transform.localPosition;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) yield break;
        Quaternion look = Quaternion.LookRotation(dir.normalized, Vector3.up);
        while (Quaternion.Angle(transform.localRotation, look) > 1f)
        {
            transform.localRotation = Quaternion.RotateTowards(transform.localRotation, look, turnSpeed * Time.deltaTime);
            yield return null;
        }
        transform.localRotation = look;
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
