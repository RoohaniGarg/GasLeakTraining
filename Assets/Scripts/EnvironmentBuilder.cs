#if UNITY_EDITOR
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

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
public static class EnvironmentBuilder
{
    const string MatFolder = "Assets/Materials/Props";

    // Materials
    static Material pipeYellow, steel, darkSteel, lpgRed, valveRed, white, concrete,
        holeBlack, vestOrange, reflective, navy, skin, helmetYellow, bootBlack,
        safeGreen, signYellow, signRed, ropeOrange, floorLine;

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
    static void BuildWorker(Transform root)
    {
        // Faces the confined space (+X, +Z direction)
        Transform t = Hitbox(root, "WorkerPlaceholder", new Vector3(-0.25f, 0, -0.15f), 56f);
        var col = t.gameObject.AddComponent<CapsuleCollider>();
        col.center = new Vector3(0, 0.17f, 0); col.radius = 0.05f; col.height = 0.34f; col.direction = 1;

        foreach (float s in new[] { -1f, 1f })
        {
            string side = s < 0 ? "L" : "R";
            Part(t, "Boot" + side, PrimitiveType.Cube, new Vector3(0.022f * s, 0.01f, 0.008f), new Vector3(0.03f, 0.02f, 0.05f), bootBlack);
            Part(t, "Leg" + side, PrimitiveType.Cylinder, new Vector3(0.022f * s, 0.09f, 0), new Vector3(0.032f, 0.07f, 0.032f), navy);
            Part(t, "Arm" + side, PrimitiveType.Capsule, new Vector3(0.055f * s, 0.215f, 0), new Vector3(0.024f, 0.045f, 0.024f), vestOrange);
            Part(t, "Hand" + side, PrimitiveType.Sphere, new Vector3(0.055f * s, 0.165f, 0), new Vector3(0.022f, 0.022f, 0.022f), skin);
        }
        Part(t, "Hips", PrimitiveType.Cube, new Vector3(0, 0.165f, 0), new Vector3(0.075f, 0.03f, 0.045f), navy);
        Part(t, "Torso", PrimitiveType.Capsule, new Vector3(0, 0.225f, 0), new Vector3(0.085f, 0.055f, 0.055f), vestOrange);
        Part(t, "StripeLow", PrimitiveType.Cube, new Vector3(0, 0.205f, 0), new Vector3(0.088f, 0.008f, 0.058f), reflective);
        Part(t, "StripeHigh", PrimitiveType.Cube, new Vector3(0, 0.24f, 0), new Vector3(0.088f, 0.008f, 0.058f), reflective);
        Part(t, "Neck", PrimitiveType.Cylinder, new Vector3(0, 0.285f, 0), new Vector3(0.02f, 0.01f, 0.02f), skin);
        Part(t, "Head", PrimitiveType.Sphere, new Vector3(0, 0.305f, 0), new Vector3(0.045f, 0.045f, 0.045f), skin);
        Part(t, "Helmet", PrimitiveType.Sphere, new Vector3(0, 0.318f, 0), new Vector3(0.052f, 0.036f, 0.052f), helmetYellow);
        Part(t, "HelmetBrim", PrimitiveType.Cylinder, new Vector3(0, 0.315f, 0.005f), new Vector3(0.064f, 0.003f, 0.064f), helmetYellow);
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
        safeGreen    = Mat("Prop_SafeGreen",    new Color(0.10f, 0.65f, 0.25f), 0.0f, 0.3f);
        signYellow   = Mat("Prop_SignYellow",   new Color(1.00f, 0.80f, 0.00f), 0.0f, 0.3f);
        signRed      = Mat("Prop_SignRed",      new Color(0.80f, 0.05f, 0.05f), 0.0f, 0.3f);
        ropeOrange   = Mat("Prop_RopeOrange",   new Color(0.90f, 0.50f, 0.10f), 0.0f, 0.2f);
        floorLine    = Mat("Prop_FloorLine",    new Color(1.00f, 0.80f, 0.00f), 0.0f, 0.2f);
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
