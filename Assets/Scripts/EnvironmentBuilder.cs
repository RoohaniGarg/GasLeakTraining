#if UNITY_EDITOR
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Phase 3.6: Builds recognisable, low-poly props for the training scene out of
/// simple shapes (no downloads, no licences). Run from the menu:
///   SIH > Build Realistic Environment
/// Safe to run again - it rebuilds everything from scratch each time.
///
/// Each interactable (GasPipe, GasCylinder, EmergencyShutoff, WorkerPlaceholder,
/// ConfinedSpace, RetrievalTripod) becomes a clean unit-scale object with ONE
/// collider (the tap hitbox) and visual children with NO colliders.
/// </summary>
public static partial class EnvironmentBuilder
{
    const string MatFolder = "Assets/Materials/Props";

    // Materials
    static Material pipeYellow, steel, darkSteel, lpgRed, valveRed, white, concrete,
        holeBlack, vestOrange, reflective, navy, skin, helmetYellow, bootBlack,
        shirtBlue, gloveTan, hairBlack, eyeDark, visorBlue,
        safeGreen, signYellow, signRed, ropeOrange, floorLine,
        gasParticle, zoneRed, zoneAmber, markerRed;

    [MenuItem("SIH/Build Realistic Environment")]
    public static void Build()
    {
        Transform root = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(t => t.name == "TrainingEnvironmentRoot" && t.parent == null);
        if (root == null)
        {
            Debug.LogError("EnvironmentBuilder: TrainingEnvironmentRoot not found in the scene.");
            return;
        }
        if (TMP_Settings.defaultFontAsset == null)
        {
            Debug.LogError("EnvironmentBuilder: Import TMP Essentials first (Window > TextMeshPro > Import TMP Essential Resources).");
            return;
        }

        CreateMaterials();

        BuildFloorMarkings(root);
        BuildGasPipe(root);
        BuildGasCylinder(root);
        BuildShutoff(root);
        BuildWorker(root);
        BuildConfinedSpace(root);
        BuildTripod(root);
        BuildSafeZone(root);
        BuildHazardZones(root);
        SetupInteraction(root);

        // Hook the Phase 4 hazards into ScenarioManager
        var sm = Object.FindObjectsByType<ScenarioManager>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
        if (sm != null)
        {
            var so = new SerializedObject(sm);
            so.FindProperty("gasLeak").objectReferenceValue = root.GetComponentInChildren<GasLeakController>(true);
            so.FindProperty("hazardZones").objectReferenceValue = root.GetComponentInChildren<HazardZoneVisual>(true);
            so.ApplyModifiedPropertiesWithoutUndo();

            // Phase 5: tap input lives on the ScenarioManager object
            GetOrAdd<TapInputManager>(sm.gameObject);

            // Phase 6/7: the real training flow replaces the temporary Phase 5 test driver
            foreach (var c in sm.GetComponents<MonoBehaviour>())
                if (c != null && c.GetType().Name == "Phase5TestDriver")
                    Object.DestroyImmediate(c);
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(sm.gameObject);
            EditorUtility.SetDirty(sm.gameObject);
        }
        else Debug.LogWarning("EnvironmentBuilder: ScenarioManager not found - assign gasLeak / hazardZones manually.");

        EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
        EditorSceneManager.SaveScene(root.gameObject.scene);
        AssetDatabase.SaveAssets();
        Debug.Log("EnvironmentBuilder: Realistic environment built and scene saved.");
    }

    // ---------------------------------------------------------------- objects

    static void BuildFloorMarkings(Transform root)
    {
        Transform g = Group(root, "FloorMarkings", Vector3.zero, 0f);
        Part(g, "Line_Back", PrimitiveType.Cube, new Vector3(0, 0.001f, 0.485f), new Vector3(0.99f, 0.002f, 0.02f), floorLine);
        Part(g, "Line_Front", PrimitiveType.Cube, new Vector3(0, 0.001f, -0.485f), new Vector3(0.99f, 0.002f, 0.02f), floorLine);
        Part(g, "Line_Left", PrimitiveType.Cube, new Vector3(-0.485f, 0.001f, 0), new Vector3(0.02f, 0.002f, 0.99f), floorLine);
        Part(g, "Line_Right", PrimitiveType.Cube, new Vector3(0.485f, 0.001f, 0), new Vector3(0.02f, 0.002f, 0.99f), floorLine);
    }

    // Yellow gas line: vertical riser with a bolted flange joint (leak point),
    // elbow, and a horizontal run on supports along the back edge.
    static void BuildGasPipe(Transform root)
    {
        Transform t = Hitbox(root, "GasPipe", new Vector3(-0.3f, 0, 0.3f), 0f);
        var col = t.gameObject.AddComponent<CapsuleCollider>();
        col.center = new Vector3(0, 0.29f, 0); col.radius = 0.05f; col.height = 0.6f; col.direction = 1;

        Part(t, "BasePlate", PrimitiveType.Cylinder, new Vector3(0, 0.005f, 0), new Vector3(0.09f, 0.005f, 0.09f), darkSteel);
        Part(t, "RiserLower", PrimitiveType.Cylinder, new Vector3(0, 0.14f, 0), new Vector3(0.05f, 0.13f, 0.05f), pipeYellow);
        Part(t, "FlangeA", PrimitiveType.Cylinder, new Vector3(0, 0.275f, 0), new Vector3(0.085f, 0.008f, 0.085f), steel);
        Part(t, "FlangeB", PrimitiveType.Cylinder, new Vector3(0, 0.297f, 0), new Vector3(0.085f, 0.008f, 0.085f), steel);
        for (int i = 0; i < 6; i++)
        {
            float a = i * 60f * Mathf.Deg2Rad;
            Part(t, "Bolt" + i, PrimitiveType.Cylinder, new Vector3(0.035f * Mathf.Cos(a), 0.286f, 0.035f * Mathf.Sin(a)),
                new Vector3(0.008f, 0.02f, 0.008f), darkSteel);
        }
        Part(t, "RiserUpper", PrimitiveType.Cylinder, new Vector3(0, 0.43f, 0), new Vector3(0.05f, 0.125f, 0.05f), pipeYellow);
        Part(t, "Elbow", PrimitiveType.Sphere, new Vector3(0, 0.555f, 0), new Vector3(0.05f, 0.05f, 0.05f), pipeYellow);
        Part(t, "HorizontalRun", PrimitiveType.Cylinder, new Vector3(0.375f, 0.555f, 0), new Vector3(0.05f, 0.375f, 0.05f), pipeYellow, new Vector3(0, 0, 90));
        Part(t, "EndCap", PrimitiveType.Cylinder, new Vector3(0.75f, 0.555f, 0), new Vector3(0.062f, 0.008f, 0.062f), steel, new Vector3(0, 0, 90));
        foreach (float x in new[] { 0.4f, 0.7f })
        {
            Part(t, "Support_" + x, PrimitiveType.Cylinder, new Vector3(x, 0.265f, 0), new Vector3(0.018f, 0.265f, 0.018f), darkSteel);
            Part(t, "Clamp_" + x, PrimitiveType.Cylinder, new Vector3(x, 0.555f, 0), new Vector3(0.058f, 0.012f, 0.058f), steel, new Vector3(0, 0, 90));
        }

        // Marker for Phase 4: gas comes out of the flange joint
        var leak = new GameObject("LeakPoint").transform;
        leak.SetParent(t, false);
        leak.localPosition = new Vector3(0, 0.286f, 0);

        BuildGasFX(t, leak);
    }

    // Phase 4: particle gas cloud + 3D hiss at the flange, warning marker above the pipe
    static void BuildGasFX(Transform pipe, Transform leak)
    {
        var go = new GameObject("GasLeakFX");
        go.transform.SetParent(leak, false);
        // Spray out of the joint toward the front and slightly up
        go.transform.localRotation = Quaternion.LookRotation(new Vector3(0.35f, 0.3f, -1f).normalized, Vector3.up);

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = 5f;
        main.loop = true;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2.2f, 3.4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.06f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
        main.startColor = Color.white;
        main.gravityModifier = 0.004f;              // LPG is heavier than air: drifts down and spreads
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.maxParticles = 400;

        var emission = ps.emission;
        emission.rateOverTime = 0f;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 28f;
        shape.radius = 0.012f;
        shape.rotation = Vector3.zero;

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 3.5f));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(new Color(0.88f, 0.96f, 0.55f), 0f), new GradientColorKey(new Color(0.72f, 0.82f, 0.45f), 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.6f, 0.15f), new GradientAlphaKey(0.3f, 0.6f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.03f;
        noise.frequency = 1.5f;
        noise.scrollSpeed = 0.5f;

        var rend = go.GetComponent<ParticleSystemRenderer>();
        rend.sharedMaterial = gasParticle;
        rend.renderMode = ParticleSystemRenderMode.Billboard;

        var audio = go.AddComponent<AudioSource>();
        audio.playOnAwake = false;
        audio.loop = true;
        audio.spatialBlend = 1f;                    // fully 3D: louder when the phone is closer
        audio.rolloffMode = AudioRolloffMode.Logarithmic;
        audio.minDistance = 0.3f;
        audio.maxDistance = 3f;
        audio.dopplerLevel = 0f;

        // Warning diamond (red border, white face, red "!")
        var marker = new GameObject("HazardMarker").transform;
        marker.SetParent(pipe, false);
        marker.localPosition = new Vector3(0, 0.72f, 0);
        Part(marker, "Diamond", PrimitiveType.Cube, Vector3.zero, new Vector3(0.075f, 0.075f, 0.006f), markerRed, new Vector3(0, 0, 45));
        Part(marker, "DiamondFace", PrimitiveType.Cube, new Vector3(0, 0, -0.004f), new Vector3(0.056f, 0.056f, 0.002f), white, new Vector3(0, 0, 45));
        Label(marker, "Text_Warning", "!", new Vector3(0, 0.002f, -0.007f), new Vector2(0.05f, 0.07f), new Color(0.85f, 0.08f, 0.08f), 0.6f);

        var ctrl = go.AddComponent<GasLeakController>();
        ctrl.hazardMarker = marker.gameObject;
    }

    // Red LPG cylinder with dome, neck, valve and label
    static void BuildGasCylinder(Transform root)
    {
        Transform t = Hitbox(root, "GasCylinder", new Vector3(-0.12f, 0, 0.3f), 0f);
        var col = t.gameObject.AddComponent<CapsuleCollider>();
        col.center = new Vector3(0, 0.19f, 0); col.radius = 0.08f; col.height = 0.38f; col.direction = 1;

        Part(t, "FootRing", PrimitiveType.Cylinder, new Vector3(0, 0.01f, 0), new Vector3(0.13f, 0.01f, 0.13f), darkSteel);
        Part(t, "Body", PrimitiveType.Cylinder, new Vector3(0, 0.15f, 0), new Vector3(0.15f, 0.13f, 0.15f), lpgRed);
        Part(t, "Dome", PrimitiveType.Sphere, new Vector3(0, 0.28f, 0), new Vector3(0.15f, 0.09f, 0.15f), lpgRed);
        Part(t, "Band", PrimitiveType.Cylinder, new Vector3(0, 0.235f, 0), new Vector3(0.152f, 0.01f, 0.152f), white);
        Part(t, "Neck", PrimitiveType.Cylinder, new Vector3(0, 0.33f, 0), new Vector3(0.045f, 0.015f, 0.045f), steel);
        Part(t, "Valve", PrimitiveType.Cylinder, new Vector3(0, 0.355f, 0), new Vector3(0.028f, 0.018f, 0.028f), steel);
        Part(t, "ValveHandle", PrimitiveType.Cube, new Vector3(0, 0.378f, 0), new Vector3(0.05f, 0.008f, 0.012f), darkSteel);

        Label(t, "Text_LPG", "LPG", new Vector3(0, 0.165f, -0.078f), new Vector2(0.11f, 0.05f), Color.white, 0.35f);
        Label(t, "Text_Flammable", "FLAMMABLE", new Vector3(0, 0.12f, -0.078f), new Vector2(0.11f, 0.02f), Color.white, 0.12f);
    }

    // Emergency shut-off: yellow post, valve body, red handwheel, sign
    static void BuildShutoff(Transform root)
    {
        Transform t = Hitbox(root, "EmergencyShutoff", new Vector3(0.3f, 0, -0.25f), 0f);
        var col = t.gameObject.AddComponent<BoxCollider>();
        col.center = new Vector3(0, 0.16f, 0); col.size = new Vector3(0.13f, 0.32f, 0.13f);

        Part(t, "BasePlate", PrimitiveType.Cube, new Vector3(0, 0.005f, 0), new Vector3(0.08f, 0.01f, 0.08f), darkSteel);
        Part(t, "Post", PrimitiveType.Cylinder, new Vector3(0, 0.105f, 0), new Vector3(0.04f, 0.095f, 0.04f), pipeYellow);
        Part(t, "ValveBody", PrimitiveType.Cube, new Vector3(0, 0.215f, 0), new Vector3(0.07f, 0.05f, 0.07f), steel);
        Part(t, "Bonnet", PrimitiveType.Cylinder, new Vector3(0, 0.255f, 0), new Vector3(0.035f, 0.02f, 0.035f), steel);
        Part(t, "Stem", PrimitiveType.Cylinder, new Vector3(0, 0.285f, 0), new Vector3(0.008f, 0.02f, 0.008f), darkSteel);

        // Handwheel: ring of segments + spokes + hub
        const float r = 0.05f, y = 0.3f;
        for (int i = 0; i < 16; i++)
        {
            float deg = i * 22.5f;
            float a = deg * Mathf.Deg2Rad;
            Part(t, "Rim" + i, PrimitiveType.Cube, new Vector3(r * Mathf.Cos(a), y, r * Mathf.Sin(a)),
                new Vector3(0.022f, 0.008f, 0.008f), valveRed, new Vector3(0, -deg + 90f, 0));
        }
        for (int i = 0; i < 3; i++)
            Part(t, "Spoke" + i, PrimitiveType.Cube, new Vector3(0, y, 0), new Vector3(0.1f, 0.006f, 0.006f), valveRed, new Vector3(0, i * 60f, 0));
        Part(t, "Hub", PrimitiveType.Cylinder, new Vector3(0, y, 0), new Vector3(0.018f, 0.008f, 0.018f), valveRed);

        // Sign on a rod beside the valve
        Part(t, "SignRod", PrimitiveType.Cylinder, new Vector3(0.07f, 0.31f, 0.02f), new Vector3(0.008f, 0.11f, 0.008f), darkSteel);
        Part(t, "SignPlate", PrimitiveType.Cube, new Vector3(0.07f, 0.43f, 0.02f), new Vector3(0.17f, 0.055f, 0.006f), signYellow);
        Label(t, "Text_Shutoff", "EMERGENCY\nSHUT-OFF", new Vector3(0.07f, 0.43f, 0.014f), new Vector2(0.16f, 0.05f), Color.black, 0.18f);
    }

    // Worker "Ramesh": boots, trousers, hi-vis vest with stripes, arms, head, hard hat
    // Ramesh: a jointed low-poly worker (hips, knees, spine, neck, shoulders, elbows)
    // so WorkerAnimator can walk, climb, collapse and lie him down naturally.
    // Height ~0.36 (1.8 m at 1:5 scale). Faces +Z (towards the manhole).
    static void BuildWorker(Transform root)
    {
        Transform t = Hitbox(root, "WorkerPlaceholder", new Vector3(-0.25f, 0, -0.15f), 56f);
        var col = t.gameObject.AddComponent<CapsuleCollider>();
        col.center = new Vector3(0, 0.18f, 0); col.radius = 0.05f; col.height = 0.36f; col.direction = 1;

        // Phase 9: real rigged character (Mixamo) if its files are in Assets/Characters/Ramesh
        if (TryBuildRameshModel(t)) return;

        // Fallback: jointed figure made of simple shapes
        // Pelvis pivot - moving/rotating it moves the whole body
        Transform body = Joint(t, "Body", new Vector3(0, 0.18f, 0));
        Part(body, "Pelvis", PrimitiveType.Cube, new Vector3(0, 0.002f, 0), new Vector3(0.072f, 0.036f, 0.044f), navy);
        Part(body, "Belt", PrimitiveType.Cube, new Vector3(0, 0.02f, 0), new Vector3(0.069f, 0.008f, 0.043f), bootBlack);
        Part(body, "Buckle", PrimitiveType.Cube, new Vector3(0, 0.02f, 0.022f), new Vector3(0.01f, 0.007f, 0.003f), steel);

        foreach (float s in new[] { -1f, 1f })
        {
            string side = s < 0 ? "L" : "R";
            Transform hip = Joint(body, "Hip_" + side, new Vector3(0.019f * s, -0.008f, 0));
            Part(hip, "Thigh", PrimitiveType.Capsule, new Vector3(0, -0.041f, 0), new Vector3(0.034f, 0.045f, 0.035f), navy);
            Transform knee = Joint(hip, "Knee", new Vector3(0, -0.082f, 0));
            Part(knee, "Shin", PrimitiveType.Capsule, new Vector3(0, -0.037f, 0), new Vector3(0.029f, 0.041f, 0.03f), navy);
            Part(knee, "Boot", PrimitiveType.Cube, new Vector3(0, -0.08f, 0.002f), new Vector3(0.03f, 0.02f, 0.036f), bootBlack);
            Part(knee, "BootToe", PrimitiveType.Cube, new Vector3(0, -0.0835f, 0.021f), new Vector3(0.029f, 0.013f, 0.02f), bootBlack);
        }

        Transform spine = Joint(body, "Spine", new Vector3(0, 0.02f, 0));
        Part(spine, "Belly", PrimitiveType.Capsule, new Vector3(0, 0.033f, 0), new Vector3(0.064f, 0.033f, 0.042f), vestOrange);
        Part(spine, "Chest", PrimitiveType.Capsule, new Vector3(0, 0.068f, 0), new Vector3(0.076f, 0.034f, 0.048f), vestOrange);
        Part(spine, "StripeLow", PrimitiveType.Cube, new Vector3(0, 0.042f, 0), new Vector3(0.068f, 0.007f, 0.046f), reflective);
        Part(spine, "StripeHigh", PrimitiveType.Cube, new Vector3(0, 0.078f, 0), new Vector3(0.078f, 0.007f, 0.051f), reflective);
        Part(spine, "Collar", PrimitiveType.Cylinder, new Vector3(0, 0.099f, 0), new Vector3(0.034f, 0.005f, 0.03f), shirtBlue);
        Part(spine, "Pocket", PrimitiveType.Cube, new Vector3(-0.018f, 0.064f, 0.024f), new Vector3(0.016f, 0.016f, 0.003f), vestOrange);

        Transform neck = Joint(spine, "Neck", new Vector3(0, 0.102f, 0));
        Part(neck, "NeckSkin", PrimitiveType.Cylinder, new Vector3(0, 0.006f, 0), new Vector3(0.018f, 0.008f, 0.018f), skin);
        Part(neck, "Head", PrimitiveType.Sphere, new Vector3(0, 0.029f, 0.002f), new Vector3(0.039f, 0.046f, 0.042f), skin);
        Part(neck, "Hair", PrimitiveType.Sphere, new Vector3(0, 0.033f, -0.005f), new Vector3(0.04f, 0.04f, 0.04f), hairBlack);
        Part(neck, "Ear_L", PrimitiveType.Sphere, new Vector3(-0.02f, 0.029f, 0), new Vector3(0.006f, 0.011f, 0.008f), skin);
        Part(neck, "Ear_R", PrimitiveType.Sphere, new Vector3(0.02f, 0.029f, 0), new Vector3(0.006f, 0.011f, 0.008f), skin);
        Part(neck, "Eye_L", PrimitiveType.Sphere, new Vector3(-0.008f, 0.033f, 0.022f), new Vector3(0.0045f, 0.0045f, 0.004f), eyeDark);
        Part(neck, "Eye_R", PrimitiveType.Sphere, new Vector3(0.008f, 0.033f, 0.022f), new Vector3(0.0045f, 0.0045f, 0.004f), eyeDark);
        Part(neck, "Brow", PrimitiveType.Cube, new Vector3(0, 0.039f, 0.021f), new Vector3(0.024f, 0.003f, 0.003f), hairBlack);
        Part(neck, "Nose", PrimitiveType.Cube, new Vector3(0, 0.027f, 0.022f), new Vector3(0.005f, 0.01f, 0.005f), skin);
        Part(neck, "Moustache", PrimitiveType.Cube, new Vector3(0, 0.02f, 0.0215f), new Vector3(0.014f, 0.003f, 0.003f), hairBlack);
        Part(neck, "Helmet", PrimitiveType.Sphere, new Vector3(0, 0.045f, 0), new Vector3(0.047f, 0.033f, 0.05f), helmetYellow);
        Part(neck, "HelmetBrim", PrimitiveType.Cylinder, new Vector3(0, 0.043f, 0.004f), new Vector3(0.056f, 0.003f, 0.06f), helmetYellow);
        Part(neck, "HelmetRidge", PrimitiveType.Cube, new Vector3(0, 0.06f, 0), new Vector3(0.006f, 0.005f, 0.046f), helmetYellow);

        foreach (float s in new[] { -1f, 1f })
        {
            string side = s < 0 ? "L" : "R";
            Transform sh = Joint(spine, "Shoulder_" + side, new Vector3(0.042f * s, 0.086f, 0));
            Part(sh, "UpperArm", PrimitiveType.Capsule, new Vector3(0, -0.027f, 0), new Vector3(0.026f, 0.031f, 0.026f), shirtBlue);
            Part(sh, "Shoulder", PrimitiveType.Sphere, new Vector3(0, -0.002f, 0), new Vector3(0.026f, 0.024f, 0.026f), vestOrange);
            Transform el = Joint(sh, "Elbow", new Vector3(0, -0.056f, 0));
            Part(el, "Forearm", PrimitiveType.Capsule, new Vector3(0, -0.024f, 0), new Vector3(0.023f, 0.027f, 0.023f), shirtBlue);
            Part(el, "Glove", PrimitiveType.Sphere, new Vector3(0, -0.053f, 0.002f), new Vector3(0.017f, 0.022f, 0.015f), gloveTan);
        }

        // Protective equipment - hidden until the trainee picks the right PPE (BuddyController.ShowPPE)
        Part(spine, "PPE_Tank", PrimitiveType.Capsule, new Vector3(0, 0.06f, -0.037f), new Vector3(0.026f, 0.036f, 0.026f), pipeYellow);
        Part(spine, "PPE_TankValve", PrimitiveType.Cylinder, new Vector3(0, 0.1f, -0.037f), new Vector3(0.009f, 0.006f, 0.009f), steel);
        Part(spine, "PPE_StrapL", PrimitiveType.Cube, new Vector3(-0.02f, 0.06f, 0.001f), new Vector3(0.008f, 0.085f, 0.054f), bootBlack, new Vector3(0, 0, -8));
        Part(spine, "PPE_StrapR", PrimitiveType.Cube, new Vector3(0.02f, 0.06f, 0.001f), new Vector3(0.008f, 0.085f, 0.054f), bootBlack, new Vector3(0, 0, 8));
        Part(spine, "PPE_DRing", PrimitiveType.Cylinder, new Vector3(0, 0.086f, -0.058f), new Vector3(0.011f, 0.002f, 0.011f), steel, new Vector3(90, 0, 0));
        Part(spine, "PPE_Detector", PrimitiveType.Cube, new Vector3(-0.021f, 0.066f, 0.028f), new Vector3(0.014f, 0.02f, 0.006f), signYellow);
        Part(neck, "PPE_Mask", PrimitiveType.Cube, new Vector3(0, 0.024f, 0.021f), new Vector3(0.03f, 0.024f, 0.008f), darkSteel);
        Part(neck, "PPE_Visor", PrimitiveType.Cube, new Vector3(0, 0.034f, 0.0235f), new Vector3(0.026f, 0.01f, 0.003f), visorBlue);
        Part(neck, "PPE_Regulator", PrimitiveType.Cylinder, new Vector3(0, 0.017f, 0.027f), new Vector3(0.01f, 0.004f, 0.01f), steel, new Vector3(90, 0, 0));
    }

    static Transform Joint(Transform parent, string name, Vector3 pos)
    {
        var j = new GameObject(name).transform;
        j.SetParent(parent, false);
        j.localPosition = pos;
        j.localRotation = Quaternion.identity;
        j.localScale = Vector3.one;
        return j;
    }

    // Manhole / valve chamber opening with open lid and danger sign
    static void BuildConfinedSpace(Transform root)
    {
        Transform t = Hitbox(root, "ConfinedSpace", new Vector3(0.2f, 0, 0.15f), 0f);
        var col = t.gameObject.AddComponent<BoxCollider>();
        col.center = new Vector3(0, 0.02f, 0); col.size = new Vector3(0.2f, 0.04f, 0.2f);

        Part(t, "Collar", PrimitiveType.Cylinder, new Vector3(0, 0.02f, 0), new Vector3(0.2f, 0.02f, 0.2f), concrete);
        Part(t, "Hole", PrimitiveType.Cylinder, new Vector3(0, 0.041f, 0), new Vector3(0.15f, 0.001f, 0.15f), holeBlack);
        Part(t, "Lid", PrimitiveType.Cylinder, new Vector3(0.16f, 0.006f, -0.02f), new Vector3(0.14f, 0.006f, 0.14f), darkSteel);

        Part(t, "SignRod", PrimitiveType.Cylinder, new Vector3(-0.13f, 0.1f, -0.08f), new Vector3(0.008f, 0.1f, 0.008f), darkSteel);
        Part(t, "SignPlate", PrimitiveType.Cube, new Vector3(-0.13f, 0.23f, -0.08f), new Vector3(0.14f, 0.075f, 0.006f), signRed);
        Label(t, "Text_Danger", "DANGER\nCONFINED SPACE\nPERMIT REQUIRED", new Vector3(-0.13f, 0.23f, -0.086f), new Vector2(0.13f, 0.065f), Color.white, 0.14f);
    }

    // Rescue tripod with winch and rope over the opening
    static void BuildTripod(Transform root)
    {
        Transform t = Hitbox(root, "RetrievalTripod", new Vector3(0.2f, 0, 0.15f), 0f);
        var col = t.gameObject.AddComponent<BoxCollider>();
        col.center = new Vector3(0, 0.2f, 0); col.size = new Vector3(0.16f, 0.26f, 0.16f);

        Vector3 apex = new Vector3(0, 0.32f, 0);
        Vector3 firstFoot = Vector3.zero;
        for (int i = 0; i < 3; i++)
        {
            float a = (90f + i * 120f) * Mathf.Deg2Rad;
            Vector3 foot = new Vector3(0.13f * Mathf.Cos(a), 0, 0.13f * Mathf.Sin(a));
            if (i == 0) firstFoot = foot;
            Vector3 dir = apex - foot;
            var leg = Part(t, "Leg" + i, PrimitiveType.Cylinder, (apex + foot) / 2f,
                new Vector3(0.014f, dir.magnitude / 2f, 0.014f), darkSteel);
            leg.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dir.normalized);
            Part(t, "Foot" + i, PrimitiveType.Cube, foot + new Vector3(0, 0.004f, 0), new Vector3(0.025f, 0.008f, 0.025f), darkSteel);
        }
        Part(t, "Head", PrimitiveType.Cylinder, new Vector3(0, 0.325f, 0), new Vector3(0.04f, 0.012f, 0.04f), darkSteel);
        Vector3 winchPos = Vector3.Lerp(firstFoot, apex, 0.45f) + new Vector3(0, 0, 0.02f);
        Part(t, "Winch", PrimitiveType.Cube, winchPos, new Vector3(0.035f, 0.035f, 0.035f), signYellow);
        Part(t, "WinchDrum", PrimitiveType.Cylinder, winchPos + new Vector3(0, 0, -0.022f), new Vector3(0.03f, 0.008f, 0.03f), steel, new Vector3(90, 0, 0));
        Part(t, "Rope", PrimitiveType.Cylinder, new Vector3(0, 0.18f, 0), new Vector3(0.004f, 0.14f, 0.004f), ropeOrange);
    }

    // Green assembly point with sign (no collider)
    static void BuildSafeZone(Transform root)
    {
        Transform t = Hitbox(root, "SafeZone", new Vector3(-0.36f, 0, -0.38f), 0f);
        Part(t, "Disc", PrimitiveType.Cylinder, new Vector3(0, 0.002f, 0), new Vector3(0.16f, 0.002f, 0.16f), safeGreen);
        Part(t, "SignRod", PrimitiveType.Cylinder, new Vector3(0, 0.07f, 0.06f), new Vector3(0.006f, 0.07f, 0.006f), darkSteel);
        Part(t, "SignPlate", PrimitiveType.Cube, new Vector3(0, 0.16f, 0.06f), new Vector3(0.12f, 0.05f, 0.005f), safeGreen);
        Label(t, "Text_Assembly", "ASSEMBLY\nPOINT", new Vector3(0, 0.16f, 0.054f), new Vector2(0.11f, 0.045f), Color.white, 0.17f);
    }

    // Phase 4: red (danger) and amber (warning) zones around the leak and the manhole.
    // Hidden at start; HazardZoneVisual.Show() fades them in.
    static void BuildHazardZones(Transform root)
    {
        Transform t = Group(root, "HazardZones", Vector3.zero, 0f);
        Part(t, "Amber_Pipe", PrimitiveType.Cylinder, new Vector3(-0.3f, 0.003f, 0.3f), new Vector3(0.38f, 0.001f, 0.38f), zoneAmber);
        Part(t, "Red_Pipe", PrimitiveType.Cylinder, new Vector3(-0.3f, 0.004f, 0.3f), new Vector3(0.24f, 0.001f, 0.24f), zoneRed);
        Part(t, "Amber_Manhole", PrimitiveType.Cylinder, new Vector3(0.2f, 0.003f, 0.15f), new Vector3(0.44f, 0.001f, 0.44f), zoneAmber);
        Part(t, "Red_Manhole", PrimitiveType.Cylinder, new Vector3(0.2f, 0.004f, 0.15f), new Vector3(0.3f, 0.001f, 0.3f), zoneRed);
        if (t.GetComponent<HazardZoneVisual>() == null)
            t.gameObject.AddComponent<HazardZoneVisual>();
    }

    // Phase 5: make objects tappable, give Ramesh movement, add the lifeline
    static void SetupInteraction(Transform root)
    {
        MakeInteractable(root, "GasPipe", InteractableId.GasSource, "Gas pipe (leaking joint)");
        MakeInteractable(root, "GasCylinder", InteractableId.GasCylinder, "LPG cylinder");
        MakeInteractable(root, "ConfinedSpace", InteractableId.ConfinedSpace, "Confined space (manhole)");
        MakeInteractable(root, "RetrievalTripod", InteractableId.RetrievalTripod, "Rescue tripod & winch");
        MakeInteractable(root, "EmergencyShutoff", InteractableId.EmergencyShutoff, "Emergency shut-off valve");
        MakeInteractable(root, "WorkerPlaceholder", InteractableId.Worker, "Ramesh (your buddy)");

        Transform worker = root.Find("WorkerPlaceholder");
        var buddyCtl = GetOrAdd<BuddyController>(worker.gameObject);
        buddyCtl.walkSpeed = 0.14f;         // Phase 9: a bit quicker (was 0.1)
        buddyCtl.weakWalkSpeed = 0.09f;     // was 0.055 - took ~15 s to reach the assembly point
        buddyCtl.turnSpeed = 220f;
        buddyCtl.collarHeight = 0.045f;
        buddyCtl.climbTime = 3.2f;
        buddyCtl.pullTime = 3.5f;
        buddyCtl.lieBack = 0.22f;           // lay him well clear of the manhole collar
        // Assembly point: on the green disc (centre -0.36, -0.38, radius 0.08) but clear of
        // the sign pole at z -0.32 - he used to stop right on top of it
        buddyCtl.safeZone = new Vector3(-0.34f, 0f, -0.42f);
        EditorUtility.SetDirty(buddyCtl);
        GetOrAdd<WorkerAnimator>(worker.gameObject);

        Transform tripod = root.Find("RetrievalTripod");
        var line = GetOrAdd<LineRenderer>(tripod.gameObject);
        line.sharedMaterial = ropeOrange;
        line.positionCount = 2;
        line.useWorldSpace = true;
        line.numCapVertices = 2;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.enabled = false;
        var life = GetOrAdd<Lifeline>(tripod.gameObject);
        // Clip the rope to the D-ring on his upper back (follows his animated body)
        Transform back = worker.Find("Body/Spine");
        life.buddy = back != null ? back : worker;
        life.buddyAttachLocal = back != null ? new Vector3(0, 0.086f, -0.06f) : new Vector3(0, 0.25f, -0.02f);
        if (modelBackBone != null)
        {
            life.buddy = modelBackBone;             // Mixamo model: chest bone
            life.buddyAttachLocal = modelBackLocal;
        }
        life.width = 0.008f;   // Phase 6: thicker so it reads on a phone
        EditorUtility.SetDirty(life);
    }

    static void MakeInteractable(Transform root, string objectName, InteractableId id, string displayName)
    {
        Transform t = root.Find(objectName);
        if (t == null) { Debug.LogError("EnvironmentBuilder: missing " + objectName); return; }
        var io = GetOrAdd<InteractableObject>(t.gameObject);
        io.id = id;
        io.displayName = displayName;
        EditorUtility.SetDirty(io);
    }

    static T GetOrAdd<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        return c != null ? c : go.AddComponent<T>();
    }

    // ---------------------------------------------------------------- helpers

    /// Makes (or cleans) a named object under root: unit scale, no mesh, no collider, no children.
    static Transform Hitbox(Transform root, string name, Vector3 pivot, float yaw)
    {
        Transform t = root.Find(name);
        if (t == null)
        {
            t = new GameObject(name).transform;
            t.SetParent(root, false);
        }
        for (int i = t.childCount - 1; i >= 0; i--)
            Object.DestroyImmediate(t.GetChild(i).gameObject);
        foreach (var c in t.GetComponents<Collider>())
            Object.DestroyImmediate(c);
        var mr = t.GetComponent<MeshRenderer>();
        if (mr != null) Object.DestroyImmediate(mr);
        var mf = t.GetComponent<MeshFilter>();
        if (mf != null) Object.DestroyImmediate(mf);

        t.localPosition = pivot;
        t.localRotation = Quaternion.Euler(0, yaw, 0);
        t.localScale = Vector3.one;
        return t;
    }

    static Transform Group(Transform root, string name, Vector3 pos, float yaw)
    {
        return Hitbox(root, name, pos, yaw);
    }

    static GameObject Part(Transform parent, string name, PrimitiveType type, Vector3 pos, Vector3 scale, Material mat, Vector3 euler = default)
    {
        GameObject g = GameObject.CreatePrimitive(type);
        g.name = name;
        Object.DestroyImmediate(g.GetComponent<Collider>());
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos;
        g.transform.localEulerAngles = euler;
        g.transform.localScale = scale;
        g.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return g;
    }

    static void Label(Transform parent, string name, string text, Vector3 pos, Vector2 size, Color color, float fontSize)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.text = text;
        tmp.color = color;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.enableAutoSizing = false;
        tmp.fontSize = fontSize;          // ~0.1 m line height per 1.0
        tmp.margin = Vector4.zero;
        tmp.rectTransform.sizeDelta = size;
    }

    static void CreateMaterials()
    {
        if (!AssetDatabase.IsValidFolder(MatFolder))
            AssetDatabase.CreateFolder("Assets/Materials", "Props");

        pipeYellow   = Mat("Prop_PipeYellow",   new Color(0.95f, 0.75f, 0.10f), 0.3f, 0.55f);
        steel        = Mat("Prop_Steel",        new Color(0.62f, 0.64f, 0.67f), 0.8f, 0.6f);
        darkSteel    = Mat("Prop_DarkSteel",    new Color(0.22f, 0.23f, 0.25f), 0.7f, 0.4f);
        lpgRed       = Mat("Prop_LPGRed",       new Color(0.72f, 0.07f, 0.07f), 0.2f, 0.65f);
        valveRed     = Mat("Prop_ValveRed",     new Color(0.85f, 0.10f, 0.10f), 0.2f, 0.5f);
        white        = Mat("Prop_White",        new Color(0.95f, 0.95f, 0.95f), 0.0f, 0.4f);
        concrete     = Mat("Prop_Concrete",     new Color(0.55f, 0.55f, 0.52f), 0.0f, 0.1f);
        holeBlack    = Mat("Prop_HoleBlack",    new Color(0.02f, 0.02f, 0.02f), 0.0f, 0.0f);
        vestOrange   = Mat("Prop_VestOrange",   new Color(1.00f, 0.45f, 0.00f), 0.0f, 0.35f);
        reflective   = Mat("Prop_Reflective",   new Color(0.85f, 0.90f, 0.85f), 0.5f, 0.8f);
        navy         = Mat("Prop_Navy",         new Color(0.10f, 0.15f, 0.30f), 0.0f, 0.2f);
        skin         = Mat("Prop_Skin",         new Color(0.78f, 0.57f, 0.42f), 0.0f, 0.3f);
        helmetYellow = Mat("Prop_HelmetYellow", new Color(1.00f, 0.85f, 0.10f), 0.1f, 0.7f);
        bootBlack    = Mat("Prop_BootBlack",    new Color(0.08f, 0.08f, 0.08f), 0.0f, 0.3f);
        shirtBlue    = Mat("Prop_ShirtBlue",    new Color(0.24f, 0.32f, 0.45f), 0.0f, 0.2f);
        gloveTan     = Mat("Prop_GloveTan",     new Color(0.62f, 0.48f, 0.30f), 0.0f, 0.2f);
        hairBlack    = Mat("Prop_HairBlack",    new Color(0.05f, 0.04f, 0.04f), 0.0f, 0.3f);
        eyeDark      = Mat("Prop_EyeDark",      new Color(0.03f, 0.03f, 0.03f), 0.0f, 0.8f);
        visorBlue    = Mat("Prop_VisorBlue",    new Color(0.45f, 0.70f, 0.85f), 0.3f, 0.9f);
        safeGreen    = Mat("Prop_SafeGreen",    new Color(0.10f, 0.65f, 0.25f), 0.0f, 0.3f);
        signYellow   = Mat("Prop_SignYellow",   new Color(1.00f, 0.80f, 0.00f), 0.0f, 0.3f);
        signRed      = Mat("Prop_SignRed",      new Color(0.80f, 0.05f, 0.05f), 0.0f, 0.3f);
        ropeOrange   = Mat("Prop_RopeOrange",   new Color(0.90f, 0.50f, 0.10f), 0.0f, 0.2f);
        floorLine    = Mat("Prop_FloorLine",    new Color(1.00f, 0.80f, 0.00f), 0.0f, 0.2f);
        markerRed    = Mat("Prop_MarkerRed",    new Color(0.90f, 0.06f, 0.06f), 0.0f, 0.4f);
        gasParticle  = TransparentMat("Prop_GasParticle", "Universal Render Pipeline/Particles/Unlit", new Color(1f, 1f, 1f, 1f), SoftParticleTexture());
        zoneRed      = TransparentMat("Prop_ZoneRed",   "Universal Render Pipeline/Unlit", new Color(1.00f, 0.08f, 0.05f, 0.40f), null);
        zoneAmber    = TransparentMat("Prop_ZoneAmber", "Universal Render Pipeline/Unlit", new Color(1.00f, 0.60f, 0.00f, 0.30f), null);
    }

    static Material TransparentMat(string name, string shaderName, Color c, Texture tex)
    {
        string path = MatFolder + "/" + name + ".mat";
        Shader shader = Shader.Find(shaderName);
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(shader);
            AssetDatabase.CreateAsset(m, path);
        }
        else m.shader = shader;

        m.SetFloat("_Surface", 1f);   // Transparent
        m.SetFloat("_Blend", 0f);     // Alpha
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f);
        m.SetOverrideTag("RenderType", "Transparent");
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)RenderQueue.Transparent;
        m.SetColor("_BaseColor", c);
        if (tex != null) m.SetTexture("_BaseMap", tex);
        EditorUtility.SetDirty(m);
        return m;
    }

    // Soft round puff texture for the gas particles (generated, saved as an asset)
    static Texture2D SoftParticleTexture()
    {
        string path = MatFolder + "/Prop_SoftParticle.asset";
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (tex != null) return tex;

        const int size = 64;
        tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f;
                float dy = (y + 0.5f) / size * 2f - 1f;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
            }
        tex.Apply();
        AssetDatabase.CreateAsset(tex, path);
        return tex;
    }

    static Material Mat(string name, Color c, float metallic, float smoothness)
    {
        string path = MatFolder + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Smoothness", smoothness);
        EditorUtility.SetDirty(m);
        return m;
    }
}
#endif
