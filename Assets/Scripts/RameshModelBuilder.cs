#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Phase 9: Ramesh as a real rigged person (Mixamo "Pete" + Mixamo animations)
/// instead of the figure made of shapes. Part of SIH > Build Realistic Environment.
///
/// Files expected in Assets/Characters/Ramesh (renamed from the Mixamo downloads):
///   Ramesh.fbx (character, with skin)  Idle / Walk / WalkInjured / ClimbLadder /
///   Collapse / Lying / Recover .fbx     (animations, without skin)
///
/// What it does:
///   1. Import settings: Humanoid rig, textures extracted (max 1024 for phones),
///      URP/Lit materials instead of the FBX's Standard ones (which render pink in URP),
///      animation clips looped where needed, all motion kept in place.
///   2. Builds Ramesh.controller (Move / WeakMove blend by speed, Climb, Collapse, Hang, Lying, Recover).
///   3. Puts the model under WorkerPlaceholder, scaled to 0.36 (1.8 m at 1:5),
///      adds his PPE (air tank, mask, detector - hidden until Step 2) on his bones,
///      and finds the lifeline clip point on his back.
/// If Ramesh.fbx is missing, the old shape-built figure is used.
/// </summary>
public static partial class EnvironmentBuilder
{
    const string RameshFolder = "Assets/Characters/Ramesh";
    const string RameshModel = RameshFolder + "/Ramesh.fbx";
    const string RameshController = RameshFolder + "/Ramesh.controller";
    const float RameshHeight = 0.36f;

    // Filled by TryBuildRameshModel, used by SetupInteraction for the lifeline
    static Transform modelBackBone;
    static Vector3 modelBackLocal;

    static readonly (string file, bool loop, bool keepHeightInPose)[] RameshClips =
    {
        ("Idle", true, true),
        ("Walk", true, true),
        ("WalkInjured", true, true),
        ("ClimbLadder", true, false),   // his height is moved by code, so drop the clip's own climb
        ("Collapse", false, true),
        ("Lying", true, true),
        ("Recover", true, true),
    };

    static bool TryBuildRameshModel(Transform worker)
    {
        modelBackBone = null;
        if (AssetDatabase.LoadAssetAtPath<GameObject>(RameshModel) == null) return false;

        // The site root is disabled in the scene; renderer bounds are only valid while it is active
        GameObject siteRoot = worker.root.gameObject;
        bool wasActive = siteRoot.activeSelf;
        siteRoot.SetActive(true);
        try { return BuildRameshModelInto(worker); }
        finally { siteRoot.SetActive(wasActive); }
    }

    static bool BuildRameshModelInto(Transform worker)
    {
        Avatar avatar = ConfigureRameshImports();
        if (avatar == null || !avatar.isHuman)
        {
            Debug.LogWarning("EnvironmentBuilder: Ramesh.fbx did not give a humanoid avatar - using the shape-built figure.");
            return false;
        }
        var controller = BuildRameshController();

        // ---- model instance
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RameshModel);
        var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, worker);
        model.name = "Model";
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        model.transform.localScale = Vector3.one;

        var animator = model.GetComponent<Animator>();
        if (animator == null) animator = model.AddComponent<Animator>();
        animator.avatar = avatar;
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        foreach (var r in model.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(r);

        // Scale to tabletop height, feet on the floor (measured in worker-local space, bind pose)
        Bounds b = LocalBounds(model, worker);
        if (b.size.y < 0.0001f)
        {
            Object.DestroyImmediate(model);
            Debug.LogWarning("EnvironmentBuilder: could not measure Ramesh.fbx - using the shape-built figure.");
            return false;
        }
        float s = RameshHeight / b.size.y;
        model.transform.localScale = Vector3.one * s;
        b = LocalBounds(model, worker);
        model.transform.localPosition = new Vector3(0f, -b.min.y, 0f);
        Physics.SyncTransforms();

        AddRameshPPE(model, animator, worker);
        EditorUtility.SetDirty(model);
        Debug.Log("EnvironmentBuilder: Ramesh uses the Mixamo model (scale " + s.ToString("0.0000") + ").");
        return true;
    }

    // ------------------------------------------------------------ imports

    static Avatar ConfigureRameshImports()
    {
        var mi = (ModelImporter)AssetImporter.GetAtPath(RameshModel);
        bool changed = false;
        if (mi.animationType != ModelImporterAnimationType.Human || mi.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
        {
            mi.animationType = ModelImporterAnimationType.Human;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            changed = true;
        }
        if (mi.importAnimation) { mi.importAnimation = false; changed = true; }
        if (mi.materialImportMode != ModelImporterMaterialImportMode.ImportStandard)
        {
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            changed = true;
        }
        if (changed) mi.SaveAndReimport();

        // Pull the embedded textures out once, and keep them phone-sized
        string texFolder = RameshFolder + "/Textures";
        if (!AssetDatabase.IsValidFolder(texFolder))
        {
            AssetDatabase.CreateFolder(RameshFolder, "Textures");
            mi.ExtractTextures(texFolder);
            AssetDatabase.Refresh();
            mi.SaveAndReimport();
        }
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { texFolder }))
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid));
            if (ti == null) continue;
            bool normal = Path.GetFileName(ti.assetPath).ToLower().Contains("normal");
            bool dirty = false;
            if (ti.maxTextureSize > 1024) { ti.maxTextureSize = 1024; dirty = true; }
            if (normal && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; dirty = true; }
            if (dirty) ti.SaveAndReimport();
        }

        // URP/Lit copies of the FBX materials, remapped so the model uses them
        string matFolder = RameshFolder + "/Materials";
        if (!AssetDatabase.IsValidFolder(matFolder)) AssetDatabase.CreateFolder(RameshFolder, "Materials");
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        bool remapped = false;
        var embedded = AssetDatabase.LoadAllAssetsAtPath(RameshModel).OfType<Material>().ToArray();
        foreach (var src in embedded)
        {
            string path = matFolder + "/" + Safe(src.name) + ".mat";
            var urp = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (urp == null)
            {
                urp = new Material(lit);
                AssetDatabase.CreateAsset(urp, path);
            }
            Texture albedo = src.HasProperty("_MainTex") ? src.GetTexture("_MainTex") : null;
            Texture bump = src.HasProperty("_BumpMap") ? src.GetTexture("_BumpMap") : null;
            Color c = src.HasProperty("_Color") ? src.GetColor("_Color") : Color.white;
            c.a = 1f;
            urp.SetTexture("_BaseMap", albedo);
            urp.SetColor("_BaseColor", albedo != null ? Color.white : c);
            urp.SetFloat("_Smoothness", 0.25f);
            urp.SetFloat("_Metallic", 0f);
            if (bump != null)
            {
                urp.SetTexture("_BumpMap", bump);
                urp.EnableKeyword("_NORMALMAP");
            }
            EditorUtility.SetDirty(urp);
            mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), src.name), urp);
            remapped = true;
        }
        if (remapped)
        {
            AssetDatabase.SaveAssets();
            mi.SaveAndReimport();
        }

        // Hair / eyelash cards have see-through edges: cut them out instead of drawing solid blocks
        foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { matFolder }))
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            string lower = m.name.ToLower();
            bool cutout = lower.Contains("hair") || lower.Contains("lash") || lower.Contains("brow");
            m.SetFloat("_AlphaClip", cutout ? 1f : 0f);
            m.SetFloat("_Cutoff", 0.4f);
            m.SetFloat("_Cull", cutout ? 0f : 2f);          // hair cards visible from both sides
            if (cutout) m.EnableKeyword("_ALPHATEST_ON"); else m.DisableKeyword("_ALPHATEST_ON");
            m.renderQueue = cutout ? (int)UnityEngine.Rendering.RenderQueue.AlphaTest : -1;
            EditorUtility.SetDirty(m);
        }
        AssetDatabase.SaveAssets();

        Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(RameshModel).OfType<Avatar>().FirstOrDefault();
        if (avatar == null) return null;

        // Animations: humanoid, same avatar, in place
        foreach (var (file, loop, keepHeight) in RameshClips)
        {
            string p = RameshFolder + "/" + file + ".fbx";
            var ai = AssetImporter.GetAtPath(p) as ModelImporter;
            if (ai == null) continue;
            ai.animationType = ModelImporterAnimationType.Human;
            ai.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
            ai.sourceAvatar = avatar;
            ai.importAnimation = true;
            ai.materialImportMode = ModelImporterMaterialImportMode.None;
            ai.SaveAndReimport();

            var clips = ai.defaultClipAnimations;
            foreach (var c in clips)
            {
                c.name = file;
                c.loopTime = loop;
                c.lockRootRotation = true;
                c.keepOriginalOrientation = true;
                c.lockRootHeightY = keepHeight;
                c.keepOriginalPositionY = true;
                c.lockRootPositionXZ = true;
                c.keepOriginalPositionXZ = true;
            }
            ai.clipAnimations = clips;
            ai.SaveAndReimport();
        }
        return avatar;
    }

    static string Safe(string n)
    {
        foreach (char ch in Path.GetInvalidFileNameChars()) n = n.Replace(ch, '_');
        return "Ramesh_" + n.Replace(':', '_');
    }

    static AnimationClip RameshClip(string file)
    {
        return AssetDatabase.LoadAllAssetsAtPath(RameshFolder + "/" + file + ".fbx")
            .OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
    }

    // ------------------------------------------------------------ animator controller

    static RuntimeAnimatorController BuildRameshController()
    {
        AssetDatabase.DeleteAsset(RameshController);
        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(RameshController);
        ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
        ctrl.AddParameter(new AnimatorControllerParameter { name = "WalkRate", type = AnimatorControllerParameterType.Float, defaultFloat = 1f });

        var idle = RameshClip("Idle");
        var walk = RameshClip("Walk") ?? idle;
        // WalkInjured.fbx (Mixamo "Old Man Walk") looked hunched and walked at an angle on the phone,
        // so the tired walk is the normal walk played slower, with his head lowered in code.
        var weak = walk;
        var sm = ctrl.layers[0].stateMachine;

        var move = ctrl.CreateBlendTreeInController("Move", out BlendTree moveTree, 0);
        moveTree.blendParameter = "Speed";
        moveTree.AddChild(idle, 0f);
        moveTree.AddChild(walk, 1f);
        move.speedParameterActive = true;
        move.speedParameter = "WalkRate";

        var weakMove = ctrl.CreateBlendTreeInController("WeakMove", out BlendTree weakTree, 0);
        weakTree.blendParameter = "Speed";
        weakTree.AddChild(idle, 0f);
        weakTree.AddChild(weak, 1f);
        weakMove.speedParameterActive = true;
        weakMove.speedParameter = "WalkRate";

        AddState(sm, "Climb", RameshClip("ClimbLadder") ?? idle, -1f);    // played backwards = climbing down
        AddState(sm, "Collapse", RameshClip("Collapse") ?? idle, 1f);
        AddState(sm, "Hang", idle, 0.3f);                                 // limp; head droop added in code
        AddState(sm, "Lying", RameshClip("Lying") ?? idle, 1f);
        AddState(sm, "Recover", idle, 0.8f);                              // Recover.fbx (sitting) needs a seat - standing, breathing hard instead

        sm.defaultState = move;
        EditorUtility.SetDirty(ctrl);
        AssetDatabase.SaveAssets();
        return ctrl;
    }

    static void AddState(AnimatorStateMachine sm, string name, Motion motion, float speed)
    {
        var st = sm.AddState(name);
        st.motion = motion;
        st.speed = speed;
    }

    // ------------------------------------------------------------ PPE + lifeline point

    /// <summary>
    /// Finds his real body surface (by ray-casting against a baked copy of the mesh)
    /// so the air tank sits on his back, the mask on his face, the detector on his chest.
    /// </summary>
    static void AddRameshPPE(GameObject model, Animator animator, Transform worker)
    {
        Transform chest = animator.GetBoneTransform(HumanBodyBones.UpperChest) ?? animator.GetBoneTransform(HumanBodyBones.Chest)
                          ?? animator.GetBoneTransform(HumanBodyBones.Spine);
        Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
        if (chest == null || head == null) return;

        // Temporary colliders from the baked (bind-pose) mesh
        var temps = new List<GameObject>();
        foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var baked = new Mesh();
            smr.BakeMesh(baked, true);
            var tmp = new GameObject("_tmpCollider");
            tmp.transform.SetPositionAndRotation(smr.transform.position, smr.transform.rotation);
            tmp.AddComponent<MeshCollider>().sharedMesh = baked;
            temps.Add(tmp);
        }
        Physics.SyncTransforms();

        Vector3 fwd = worker.forward, up = worker.up;
        float u = worker.lossyScale.y;                      // world units per tabletop metre
        Vector3 chestPos = chest.position;
        Vector3 facePos = head.position + up * (0.025f * u);

        Vector3 back = SurfacePoint(chestPos, -fwd, u, chestPos - fwd * (0.03f * u));
        Vector3 front = SurfacePoint(chestPos - up * (0.01f * u), fwd, u, chestPos + fwd * (0.03f * u));
        Vector3 face = SurfacePoint(facePos, fwd, u, facePos + fwd * (0.022f * u));
        foreach (var t in temps) { Object.DestroyImmediate(t.GetComponent<MeshCollider>().sharedMesh); Object.DestroyImmediate(t); }

        // Parts are made in worker space (tabletop metres) then attached to the bones
        Quaternion rot = worker.rotation;
        GameObject Put(Transform bone, string name, PrimitiveType type, Vector3 at, Vector3 size, Material mat, Vector3 euler = default)
        {
            var g = GameObject.CreatePrimitive(type);
            g.name = name;
            Object.DestroyImmediate(g.GetComponent<Collider>());
            g.GetComponent<MeshRenderer>().sharedMaterial = mat;
            g.transform.SetPositionAndRotation(at, rot * Quaternion.Euler(euler));
            g.transform.localScale = size * u;
            g.transform.SetParent(bone, true);
            return g;
        }

        // Breathing apparatus on his back, straps over the vest, D-ring for the lifeline
        Put(chest, "PPE_Tank", PrimitiveType.Capsule, back - fwd * (0.014f * u) - up * (0.012f * u), new Vector3(0.026f, 0.034f, 0.026f), pipeYellow);
        Put(chest, "PPE_TankValve", PrimitiveType.Cylinder, back - fwd * (0.014f * u) + up * (0.026f * u), new Vector3(0.009f, 0.006f, 0.009f), steel);
        Vector3 ring = back + up * (0.012f * u) - fwd * (0.002f * u);
        Put(chest, "PPE_DRing", PrimitiveType.Cylinder, ring, new Vector3(0.011f, 0.002f, 0.011f), steel, new Vector3(90, 0, 0));
        // Personal gas detector clipped on the chest
        Put(chest, "PPE_Detector", PrimitiveType.Cube, front + fwd * (0.003f * u) - worker.right * (0.014f * u),
            new Vector3(0.014f, 0.02f, 0.006f), signYellow);
        // Face mask with visor and regulator
        Put(head, "PPE_Mask", PrimitiveType.Cube, face + fwd * (0.003f * u) - up * (0.006f * u), new Vector3(0.028f, 0.024f, 0.008f), darkSteel);
        Put(head, "PPE_Visor", PrimitiveType.Cube, face + fwd * (0.0075f * u) + up * (0.003f * u), new Vector3(0.024f, 0.009f, 0.003f), visorBlue);
        Put(head, "PPE_Regulator", PrimitiveType.Cylinder, face + fwd * (0.009f * u) - up * (0.013f * u), new Vector3(0.01f, 0.004f, 0.01f), steel, new Vector3(90, 0, 0));

        modelBackBone = chest;
        modelBackLocal = chest.InverseTransformPoint(ring - fwd * (0.004f * u));
    }

    /// <summary>First mesh surface hit when coming from outside towards 'target' along 'outward'.</summary>
    static Vector3 SurfacePoint(Vector3 target, Vector3 outward, float u, Vector3 fallback)
    {
        Vector3 from = target + outward * (0.3f * u);
        var hits = Physics.RaycastAll(from, -outward, 0.3f * u);
        float best = float.MaxValue;
        Vector3 p = fallback;
        foreach (var h in hits)
        {
            if (h.collider == null || h.collider.gameObject.name != "_tmpCollider") continue;
            if (h.distance < best) { best = h.distance; p = h.point; }
        }
        return p;
    }

    /// <summary>Renderer bounds of 'go' expressed in 'space' local coordinates.</summary>
    static Bounds LocalBounds(GameObject go, Transform space)
    {
        bool any = false;
        var b = new Bounds();
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            var wb = r.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = wb.center + Vector3.Scale(wb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 l = space.InverseTransformPoint(c);
                if (!any) { b = new Bounds(l, Vector3.zero); any = true; }
                else b.Encapsulate(l);
            }
        }
        return b;
    }
}
#endif
