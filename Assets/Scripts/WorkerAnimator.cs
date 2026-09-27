using UnityEngine;

/// <summary>What Ramesh's body is doing. BuddyController picks the mode.</summary>
public enum WorkerPose
{
    Normal,      // stands / walks (walk cycle follows his real speed)
    Weak,        // after the rescue: slow, head down, hand on chest, coughing
    Climb,       // going down the manhole ladder
    Collapsed,   // slumped on his knees inside the chamber
    Hanging,     // limp on the lifeline while being winched up
    Lying,       // lying on his back next to the manhole
    Recovering   // at the assembly point: bent over, hands on knees, breathing hard
}

/// <summary>
/// Procedural animation for Ramesh (no animation files needed).
/// Works on the jointed body built by SIH > Build Realistic Environment:
///   WorkerPlaceholder / Body (pelvis) / Hip_L, Hip_R / Knee
///                                     / Spine / Neck
///                                             / Shoulder_L, Shoulder_R / Elbow
/// Every frame it works out a target pose for the current mode and eases
/// each joint towards it, so every change of pose blends smoothly.
///
/// Angle conventions (character faces +Z): hip/shoulder X negative = limb forward,
/// knee X positive = bend, elbow X negative = bend, spine/neck X positive = lean/nod forward.
/// </summary>
[DefaultExecutionOrder(-50)]   // before Lifeline, so the rope follows the animated back
public class WorkerAnimator : MonoBehaviour
{
    public WorkerPose Mode { get; private set; } = WorkerPose.Normal;

    [Tooltip("Walk speed (tabletop m/s) that gives a full-size stride")]
    public float nominalWalkSpeed = 0.1f;
    [Tooltip("Footsteps per second at nominal speed")]
    public float stepsPerSecond = 1.7f;
    [Tooltip("How quickly joints follow the walk cycle")]
    public float followRate = 14f;
    [Tooltip("How quickly the body blends into a new mode")]
    public float transitionRate = 3.5f;
    public float transitionTime = 1.2f;

    struct Pose
    {
        public Vector3 bodyPos, bodyRot, spine, neck, shL, shR, elL, elR, hipL, hipR, knL, knR;
    }

    private Transform body, spine, neck, shL, shR, elL, elR, hipL, hipR, knL, knR;
    private Vector3 bodyRest;
    private Vector3 lastPos;
    private float speed, vSpeed, gait, climb, modeTime, nextCough, coughTime = -9f;
    private Transform cam;

    public bool IsRigged => body != null && spine != null && neck != null;

    // ---- Phase 9: real rigged model (Mixamo) driven by its Animator
    [Header("Mixamo model")]
    [Tooltip("Tabletop speed at which the walk clip's feet don't slide (1.3 m/s real at 1:5)")]
    public float walkClipSpeed = 0.26f;
    public float weakClipSpeed = 0.2f;
    private Animator model;
    private Transform headBone, hipsBone;
    private string modelState;
    private float hangWeight, lookWeight, weakWeight;
    private Vector2 look;
    // Keeps his body where BuddyController puts him, even if a clip moves the hips away
    private Vector3 modelBase, hipsRest;
    private float pinXZ, pinY;

    public bool HasModel => model != null && model.runtimeAnimatorController != null;

    void Awake()
    {
        var m = transform.Find("Model");
        if (m != null) model = m.GetComponent<Animator>();
        if (HasModel)
        {
            headBone = model.GetBoneTransform(HumanBodyBones.Head);
            hipsBone = model.GetBoneTransform(HumanBodyBones.Hips);
            if (hipsBone != null && hipsBone.parent != model.transform) hipsBone = null;   // expects Mixamo layout
            modelBase = model.transform.localPosition;
            if (hipsBone != null) hipsRest = hipsBone.localPosition;   // bind pose = standing
            lastPos = transform.localPosition;
            return;
        }

        body = transform.Find("Body");
        if (body == null) return;
        spine = body.Find("Spine");
        neck = spine != null ? spine.Find("Neck") : null;
        shL = spine != null ? spine.Find("Shoulder_L") : null;
        shR = spine != null ? spine.Find("Shoulder_R") : null;
        elL = shL != null ? shL.Find("Elbow") : null;
        elR = shR != null ? shR.Find("Elbow") : null;
        hipL = body.Find("Hip_L");
        hipR = body.Find("Hip_R");
        knL = hipL != null ? hipL.Find("Knee") : null;
        knR = hipR != null ? hipR.Find("Knee") : null;
        bodyRest = body.localPosition;
        lastPos = transform.localPosition;
    }

    public void SetMode(WorkerPose mode)
    {
        if (mode == Mode) return;
        Mode = mode;
        modeTime = 0f;
    }

    /// <summary>Snap straight to the standing pose (used on reset).</summary>
    public void ResetPose()
    {
        Mode = WorkerPose.Normal;
        modeTime = transitionTime;
        if (HasModel)
        {
            model.Play("Move", 0, 0f);
            modelState = "Move";
            pinXZ = pinY = 0f;
            model.transform.localPosition = modelBase;
            return;
        }
        if (!IsRigged) return;
        body.localPosition = bodyRest;
        foreach (var j in new[] { body, spine, neck, shL, shR, elL, elR, hipL, hipR, knL, knR })
            if (j != null) j.localRotation = Quaternion.identity;
    }

    void LateUpdate()
    {
        if (HasModel) { DriveModel(); return; }
        if (!IsRigged) return;
        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        modeTime += dt;

        // --- how fast is he actually moving? (drives the walk / climb cycles)
        Vector3 p = transform.localPosition;
        Vector3 d = p - lastPos;
        lastPos = p;
        float h = new Vector2(d.x, d.z).magnitude / dt;
        float v = d.y / dt;
        speed = Mathf.Lerp(speed, h, 1f - Mathf.Exp(-dt * 8f));
        vSpeed = Mathf.Lerp(vSpeed, v, 1f - Mathf.Exp(-dt * 8f));

        float move = Mathf.Clamp01(speed / nominalWalkSpeed);
        gait += dt * stepsPerSecond * Mathf.PI * Mathf.Clamp(speed / nominalWalkSpeed, 0.4f, 1.4f) * (move > 0.02f ? 1f : 0f);
        climb += dt * Mathf.Clamp(Mathf.Abs(vSpeed) / 0.12f, 0.3f, 1.5f) * 6f;

        Pose target;
        switch (Mode)
        {
            case WorkerPose.Weak: target = WalkPose(move, 0.55f, true); break;
            case WorkerPose.Climb: target = ClimbPose(); break;
            case WorkerPose.Collapsed: target = CollapsedPose(); break;
            case WorkerPose.Hanging: target = HangingPose(); break;
            case WorkerPose.Lying: target = LyingPose(); break;
            case WorkerPose.Recovering: target = RecoveringPose(); break;
            default: target = WalkPose(move, 1f, false); break;
        }

        float rate = modeTime < transitionTime ? transitionRate : followRate;
        float k = 1f - Mathf.Exp(-rate * dt);
        body.localPosition = Vector3.Lerp(body.localPosition, bodyRest + target.bodyPos, k);
        Ease(body, target.bodyRot, k);
        Ease(spine, target.spine, k);
        Ease(neck, target.neck, k);
        Ease(shL, target.shL, k);
        Ease(shR, target.shR, k);
        Ease(elL, target.elL, k);
        Ease(elR, target.elR, k);
        Ease(hipL, target.hipL, k);
        Ease(hipR, target.hipR, k);
        Ease(knL, target.knL, k);
        Ease(knR, target.knR, k);
    }

    /// <summary>
    /// Mixamo model: cross-fades to the clip for the current mode, matches the walk
    /// cycle to his real speed (so feet don't slide), then adds small touches on top
    /// of the animation: his head drops while hanging on the rope, and he glances at
    /// the trainee while standing.
    /// </summary>
    void DriveModel()
    {
        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        modeTime += dt;

        Vector3 p = transform.localPosition;
        Vector3 d = p - lastPos;
        lastPos = p;
        float h = new Vector2(d.x, d.z).magnitude / dt;
        speed = Mathf.Lerp(speed, h, 1f - Mathf.Exp(-dt * 8f));

        string state;
        switch (Mode)
        {
            case WorkerPose.Weak: state = "WeakMove"; break;
            case WorkerPose.Climb: state = "Climb"; break;
            case WorkerPose.Collapsed: state = "Collapse"; break;
            case WorkerPose.Hanging: state = "Hang"; break;
            case WorkerPose.Lying: state = "Lying"; break;
            case WorkerPose.Recovering: state = "Recover"; break;
            default: state = "Move"; break;
        }
        if (state != modelState)
        {
            // Getting up from the floor takes longer than other changes
            bool fromFloor = modelState == "Lying" || modelState == "Collapse";
            model.CrossFadeInFixedTime(state, fromFloor ? 0.9f : 0.35f);
            modelState = state;
        }

        float clipSpeed = Mode == WorkerPose.Weak ? weakClipSpeed : walkClipSpeed;
        float move = Mathf.Clamp01(speed / Mathf.Max(0.001f, clipSpeed * 0.45f));
        float rate = Mathf.Lerp(1f, Mathf.Clamp(speed / clipSpeed, 0.45f, 1.3f), move);
        model.SetFloat("Speed", move, 0.12f, dt);
        model.SetFloat("WalkRate", rate);

        // Pin the hips over his position: the ladder clip otherwise lifts him ~1 m and drifts backwards,
        // and lying / hanging must stay exactly where the code lays or hangs him.
        if (hipsBone != null)
        {
            bool pinned = Mode == WorkerPose.Climb || Mode == WorkerPose.Hanging || Mode == WorkerPose.Lying || Mode == WorkerPose.Collapsed;
            bool upright = Mode == WorkerPose.Climb || Mode == WorkerPose.Hanging;
            pinXZ = Mathf.MoveTowards(pinXZ, pinned ? 1f : 0f, dt * 3f);
            pinY = Mathf.MoveTowards(pinY, upright ? 1f : 0f, dt * 3f);
            Vector3 delta = hipsRest - hipsBone.localPosition;
            Vector3 fix = new Vector3(delta.x * pinXZ, delta.y * pinY, delta.z * pinXZ);
            model.transform.localPosition = modelBase + model.transform.localRotation * Vector3.Scale(fix, model.transform.localScale);
        }

        if (headBone == null) return;

        // Tired after the gas: head hangs a little while he walks to the assembly point
        weakWeight = Mathf.MoveTowards(weakWeight, Mode == WorkerPose.Weak || Mode == WorkerPose.Recovering ? 1f : 0f, dt * 1.5f);
        if (weakWeight > 0f)
            headBone.rotation = Quaternion.AngleAxis(18f * weakWeight, transform.right) * headBone.rotation;

        // Limp head while hanging on the lifeline
        hangWeight = Mathf.MoveTowards(hangWeight, Mode == WorkerPose.Hanging ? 1f : 0f, dt * 2f);
        if (hangWeight > 0f)
            headBone.rotation = Quaternion.AngleAxis(40f * hangWeight, transform.right) * headBone.rotation;

        // Glance at the trainee while standing still
        bool canLook = Mode == WorkerPose.Normal && move < 0.1f;
        lookWeight = Mathf.MoveTowards(lookWeight, canLook ? 1f : 0f, dt * 1.5f);
        Vector3 target = LookAtCamera();
        look = Vector2.Lerp(look, new Vector2(target.x, target.y), 1f - Mathf.Exp(-dt * 4f));
        if (lookWeight > 0f)
        {
            float w = lookWeight * 0.8f;
            headBone.rotation = Quaternion.AngleAxis(look.y * w, transform.up)
                              * Quaternion.AngleAxis(look.x * w, transform.right) * headBone.rotation;
        }
    }

    static void Ease(Transform j, Vector3 euler, float k)
    {
        if (j == null) return;
        j.localRotation = Quaternion.Slerp(j.localRotation, Quaternion.Euler(euler), k);
    }

    // ------------------------------------------------------------ poses

    Pose StandPose()
    {
        float t = Time.time;
        float breathe = Mathf.Sin(t * 1.6f);
        var p = new Pose
        {
            spine = new Vector3(2f + 1.2f * breathe, 0f, 0f),
            shL = new Vector3(2f, 0f, -6f - breathe),
            shR = new Vector3(2f, 0f, 6f + breathe),
            elL = new Vector3(-10f, 0f, 0f),
            elR = new Vector3(-10f, 0f, 0f),
            knL = new Vector3(3f, 0f, 0f),
            knR = new Vector3(3f, 0f, 0f),
            hipL = new Vector3(-1f, 0f, -2f),
            hipR = new Vector3(-1f, 0f, 2f),
            neck = LookAtCamera(),
        };
        return p;
    }

    Pose WalkPose(float move, float stride, bool weak)
    {
        Pose stand = StandPose();
        float s = Mathf.Sin(gait), c = Mathf.Cos(gait);
        float hip = 24f * stride, arm = 20f * stride;

        Pose walk = stand;
        walk.hipL = new Vector3(-hip * s, 0f, 0f);
        walk.hipR = new Vector3(hip * s, 0f, 0f);
        walk.knL = new Vector3(6f + 42f * stride * Mathf.Max(0f, c), 0f, 0f);    // bends while that leg swings forward
        walk.knR = new Vector3(6f + 42f * stride * Mathf.Max(0f, -c), 0f, 0f);
        walk.shL = new Vector3(arm * s, 0f, -6f);                               // arms swing opposite to legs
        walk.shR = new Vector3(-arm * s, 0f, 6f);
        walk.elL = new Vector3(-14f - 14f * Mathf.Max(0f, -s), 0f, 0f);
        walk.elR = new Vector3(-14f - 14f * Mathf.Max(0f, s), 0f, 0f);
        walk.spine = new Vector3(4f, 4f * s, 0f);
        walk.neck = new Vector3(0f, -3f * s, 0f);
        walk.bodyPos = new Vector3(0f, -0.004f * Mathf.Abs(s), 0f);             // dips as each foot lands

        Pose p = Blend(stand, walk, move);

        if (weak)
        {
            // Dazed after the gas: bent forward, head down, right hand on chest, coughing now and then
            float cough = 0f;
            if (Time.time > nextCough)
            {
                coughTime = Time.time;
                nextCough = Time.time + Random.Range(1.8f, 3.2f);
            }
            float since = Time.time - coughTime;
            if (since < 0.35f) cough = Mathf.Sin(since / 0.35f * Mathf.PI);

            p.spine += new Vector3(16f + 14f * cough, 0f, 0f);
            p.neck = new Vector3(24f + 8f * cough, p.neck.y * 0.3f, 0f);
            p.shR = new Vector3(-48f, 0f, 12f);
            p.elR = new Vector3(-118f, 0f, 0f);
            p.bodyPos += new Vector3(0f, -0.004f, 0f);
        }
        return p;
    }

    Pose ClimbPose()
    {
        float a = Mathf.Sin(climb);
        return new Pose
        {
            spine = new Vector3(12f, 0f, 0f),
            neck = new Vector3(22f, 0f, 0f),                 // watching his footing
            shL = new Vector3(-118f + 16f * a, 0f, -8f),     // hands on the ladder rungs
            shR = new Vector3(-118f - 16f * a, 0f, 8f),
            elL = new Vector3(-38f, 0f, 0f),
            elR = new Vector3(-38f, 0f, 0f),
            hipL = new Vector3(-40f - 24f * a, 0f, -3f),
            hipR = new Vector3(-40f + 24f * a, 0f, 3f),
            knL = new Vector3(62f + 26f * a, 0f, 0f),
            knR = new Vector3(62f - 26f * a, 0f, 0f),
        };
    }

    Pose CollapsedPose()
    {
        float breathe = Mathf.Sin(Time.time * 0.9f);
        return new Pose
        {
            bodyPos = new Vector3(0f, -0.078f, -0.01f),       // down on his knees
            spine = new Vector3(58f + breathe, 0f, 6f),
            neck = new Vector3(38f, 18f, 0f),
            shL = new Vector3(-22f, 0f, -14f),
            shR = new Vector3(-10f, 0f, 16f),
            elL = new Vector3(-12f, 0f, 0f),
            elR = new Vector3(-20f, 0f, 0f),
            hipL = new Vector3(-8f, 0f, -4f),
            hipR = new Vector3(-12f, 0f, 4f),
            knL = new Vector3(118f, 0f, 0f),
            knR = new Vector3(112f, 0f, 0f),
        };
    }

    Pose HangingPose()
    {
        float t = Time.time;
        return new Pose
        {
            bodyRot = new Vector3(3f * Mathf.Sin(t * 1.7f), 0f, 4f * Mathf.Sin(t * 2.2f)),   // swinging on the rope
            spine = new Vector3(14f, 0f, 0f),
            neck = new Vector3(46f, 12f, 0f),                // head flopped forward
            shL = new Vector3(8f, 0f, -12f),                 // arms dangling
            shR = new Vector3(4f, 0f, 14f),
            elL = new Vector3(-14f, 0f, 0f),
            elR = new Vector3(-10f, 0f, 0f),
            hipL = new Vector3(-18f, 0f, -3f),
            hipR = new Vector3(-10f, 0f, 3f),
            knL = new Vector3(28f, 0f, 0f),
            knR = new Vector3(20f, 0f, 0f),
        };
    }

    Pose LyingPose()
    {
        float breathe = Mathf.Sin(Time.time * 1.1f);
        float pelvisHeight = 0.022f;
        return new Pose
        {
            bodyPos = new Vector3(0f, pelvisHeight - bodyRest.y, 0f),
            bodyRot = new Vector3(-88f, 0f, 0f),              // on his back, face up
            spine = new Vector3(-2f + 1.5f * breathe, 0f, 0f),
            neck = new Vector3(-6f, 32f, 0f),                 // head turned to one side
            shL = new Vector3(0f, 0f, -16f),
            shR = new Vector3(0f, 0f, 22f),
            elL = new Vector3(-10f, 0f, 0f),
            elR = new Vector3(-24f, 0f, 0f),
            hipL = new Vector3(-4f, 0f, -5f),
            hipR = new Vector3(-10f, 0f, 6f),
            knL = new Vector3(8f, 0f, 0f),
            knR = new Vector3(20f, 0f, 0f),
        };
    }

    Pose RecoveringPose()
    {
        float pant = Mathf.Sin(Time.time * 5f);
        return new Pose
        {
            bodyPos = new Vector3(0f, -0.028f, -0.04f),     // hips back, knees bent
            spine = new Vector3(42f + 3f * pant, 0f, 0f),
            neck = new Vector3(-18f, 0f, 0f),                 // looks up / ahead
            shL = new Vector3(-52f, 0f, -8f),                 // hands resting on his knees
            shR = new Vector3(-52f, 0f, 8f),
            elL = new Vector3(-12f, 0f, 0f),
            elR = new Vector3(-12f, 0f, 0f),
            hipL = new Vector3(-42f, 0f, -6f),
            hipR = new Vector3(-42f, 0f, 6f),
            knL = new Vector3(38f, 0f, 0f),
            knR = new Vector3(38f, 0f, 0f),
        };
    }

    /// <summary>Head turns to look at the trainee (the phone) when he can see them.</summary>
    Vector3 LookAtCamera()
    {
        if (cam == null && Camera.main != null) cam = Camera.main.transform;
        if (cam == null) return Vector3.zero;
        Vector3 local = transform.InverseTransformPoint(cam.position);
        float yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
        if (Mathf.Abs(yaw) > 115f) return Vector3.zero;       // camera behind him: look ahead
        float flat = new Vector2(local.x, local.z).magnitude;
        float pitch = -Mathf.Atan2(local.y - 0.33f, Mathf.Max(flat, 0.01f)) * Mathf.Rad2Deg;
        return new Vector3(Mathf.Clamp(pitch, -25f, 15f), Mathf.Clamp(yaw, -60f, 60f), 0f);
    }

    static Pose Blend(Pose a, Pose b, float t)
    {
        return new Pose
        {
            bodyPos = Vector3.Lerp(a.bodyPos, b.bodyPos, t),
            bodyRot = Vector3.Lerp(a.bodyRot, b.bodyRot, t),
            spine = Vector3.Lerp(a.spine, b.spine, t),
            neck = Vector3.Lerp(a.neck, b.neck, t),
            shL = Vector3.Lerp(a.shL, b.shL, t),
            shR = Vector3.Lerp(a.shR, b.shR, t),
            elL = Vector3.Lerp(a.elL, b.elL, t),
            elR = Vector3.Lerp(a.elR, b.elR, t),
            hipL = Vector3.Lerp(a.hipL, b.hipL, t),
            hipR = Vector3.Lerp(a.hipR, b.hipR, t),
            knL = Vector3.Lerp(a.knL, b.knL, t),
            knR = Vector3.Lerp(a.knR, b.knR, t),
        };
    }
}
