using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AnseongSteel.Bosses.Editor
{
    // Builds once after import. Existing scenes/assets are never rebuilt automatically.
    [InitializeOnLoad]
    public static class BossModelTestBuilder
    {
        private const string Root = "Assets/_BossPrototype";
        public const string ScenePath = Root + "/Scenes/BossModelTest.unity";
        private const string Generated = Root + "/Generated";

        static BossModelTestBuilder()
        {
            if (!Application.isBatchMode) EditorApplication.delayCall += BuildIfMissing;
        }

        private static void BuildIfMissing()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += BuildIfMissing;
                return;
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode || File.Exists(ScenePath) ||
                !File.Exists(Root + "/Animation/Idle.fbx")) return;
            try { Build(); }
            catch (Exception exception) { Debug.LogException(exception); }
        }

        [MenuItem("Tools/Boss Prototype/Create Model Test Scene")]
        public static void Build()
        {
            if (File.Exists(ScenePath))
            {
                // Recover an interrupted first build whose controller was not saved yet.
                var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(Generated + "/BossAnimator.controller");
                if (existing != null && existing.layers.Length == 0)
                    PopulateController(existing, LoadClip("Idle"), LoadClip("Walk"), LoadClip("Punch"));
                Debug.Log("BossModelTest already exists; keeping your scene and settings.");
                return;
            }
            EnsureFolder(Generated);
            EnsureFolder(Root + "/Scenes");
            var idle = ImportClip("Idle", true);
            var walk = ImportClip("Walk", true);
            var punch = ImportClip("Punch", false);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Animation/Idle.fbx");
            if (model == null) throw new InvalidOperationException("Idle.fbx model was not imported.");

            var controllerPath = Generated + "/BossAnimator.controller";
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
                PopulateController(controller, idle, walk, punch);
            }
            else if (controller.layers.Length == 0) PopulateController(controller, idle, walk, punch);

            var armor = Material("Armor", new Color(0.20f, 0.22f, 0.25f), 0.7f);
            var trim = Material("Ring", new Color(0.10f, 0.11f, 0.13f), 0.85f);
            var red = Material("RedEmission", new Color(0.7f, 0.015f, 0.025f), 0.4f);
            red.EnableKeyword("_EMISSION");
            red.SetColor("_EmissionColor", new Color(1.5f, 0.015f, 0.015f));
            EditorUtility.SetDirty(red);
            var floor = Material("Floor", new Color(0.13f, 0.17f, 0.22f), 0f);
            var dummyMat = Material("Target", new Color(0.15f, 0.75f, 0.85f), 0.2f);
            var lineMat = Material("Markings", new Color(0.25f, 0.37f, 0.45f), 0f);

            var previousScene = SceneManager.GetActiveScene();
            bool hasSavedScene = !string.IsNullOrEmpty(previousScene.path);
            if (!hasSavedScene && previousScene.isDirty && !Application.isBatchMode)
                throw new InvalidOperationException("Save the current untitled scene, then use Tools > Boss Prototype > Create Model Test Scene.");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                hasSavedScene ? NewSceneMode.Additive : NewSceneMode.Single);
            SceneManager.SetActiveScene(scene);
            try
            {
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.55f, 0.59f, 0.66f);
                var boss = new GameObject("Boss_Mecha_v026");
                boss.transform.SetPositionAndRotation(new Vector3(-1.2f, 0f, -3f), Quaternion.Euler(0, -25, 0));
                var movement = boss.AddComponent<BossMovement>();
                var rotation = boss.AddComponent<BossRotation>();
                var attack = boss.AddComponent<BossHeavyPunch>();
                var body = boss.AddComponent<CapsuleCollider>();
                body.center = new Vector3(0f, 1.25f, 0f);
                body.height = 2.5f;
                body.radius = 0.55f;
                var visual = new GameObject("Visual");
                visual.transform.SetParent(boss.transform, false);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
                instance.name = "Mecha";
                instance.transform.SetParent(visual.transform, false);
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;

                // Fit the imported bind-pose model to three metres, independent of FBX units.
                var renderers = instance.GetComponentsInChildren<SkinnedMeshRenderer>();
                if (renderers.Length == 0) throw new InvalidOperationException("No skinned meshes in model.");
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                float scale = 3f / bounds.size.y;
                instance.transform.localScale *= scale;
                bounds = renderers[0].bounds;
                foreach (var renderer in renderers)
                {
                    bounds.Encapsulate(renderer.bounds);
                    renderer.updateWhenOffscreen = true;
                    renderer.sharedMaterials = renderer.sharedMaterials.Select(m =>
                        m != null && m.name.StartsWith("RED", StringComparison.OrdinalIgnoreCase) ? red :
                        m != null && m.name.Contains("Ring_Metal") ? trim : armor).ToArray();
                }
                visual.transform.localPosition = new Vector3(0f, boss.transform.position.y - bounds.min.y, 0f);

                var animator = instance.GetComponent<Animator>() ?? instance.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var hand = instance.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "mixamorig:RightHand");
                if (hand == null) throw new InvalidOperationException("Right hand bone not found.");
                var hitPoint = new GameObject("HitPoint_RightPunch").transform;
                hitPoint.SetParent(hand, false);
                PositionAtFist(hitPoint, instance.transform);

                var driver = boss.AddComponent<BossAnimationDriver>();
                driver.animator = animator;
                driver.movement = movement;
                driver.rotation = rotation;
                driver.attack = attack;
                driver.hitPoint = hitPoint;
                driver.hitRadius = 0.22f;
                driver.punchDuration = punch.length;
                instance.AddComponent<BossAnimationEvents>().driver = driver;
                var serializedAttack = new SerializedObject(attack);
                serializedAttack.FindProperty("hitPoint").objectReferenceValue = hitPoint;
                serializedAttack.FindProperty("hitRadius").floatValue = driver.hitRadius;
                serializedAttack.ApplyModifiedPropertiesWithoutUndo();

                var ground = Primitive("Ground", PrimitiveType.Cube, new Vector3(0, -0.1f, 0), new Vector3(14, 0.2f, 14), floor);
                for (int i = -6; i <= 6; i++)
                {
                    var x = Primitive("FloorLine_X_" + i, PrimitiveType.Cube, new Vector3(i, 0.003f, 0), new Vector3(0.012f, 0.005f, 12), lineMat);
                    UnityEngine.Object.DestroyImmediate(x.GetComponent<Collider>());
                    var z = Primitive("FloorLine_Z_" + i, PrimitiveType.Cube, new Vector3(0, 0.003f, i), new Vector3(12, 0.005f, 0.012f), lineMat);
                    UnityEngine.Object.DestroyImmediate(z.GetComponent<Collider>());
                }
                var target = Primitive("Target_Dummy", PrimitiveType.Capsule, new Vector3(0, 1.25f, 1.4f), new Vector3(0.7f, 1.25f, 0.7f), dummyMat);
                var demo = new GameObject("DemoControls").AddComponent<BossModelDemo>();
                demo.driver = driver;
                demo.target = target.transform;
                demo.targetRenderer = target.GetComponent<Renderer>();

                var camera = new GameObject("Main Camera").AddComponent<Camera>();
                camera.tag = "MainCamera";
                camera.transform.position = new Vector3(6.5f, 4.2f, 5.5f);
                camera.transform.LookAt(new Vector3(0, 1.15f, -0.6f));
                camera.fieldOfView = 48;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.035f, 0.045f, 0.065f);
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = 100;
                camera.gameObject.AddComponent<AudioListener>();
                var sun = new GameObject("Key Light").AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.intensity = 2f;
                sun.shadows = LightShadows.Soft;
                sun.transform.rotation = Quaternion.Euler(45, -35, 0);
                var fill = new GameObject("Fill Light").AddComponent<Light>();
                fill.type = LightType.Directional;
                fill.intensity = 0.25f;
                fill.color = new Color(0.7f, 0.8f, 1f);
                fill.transform.rotation = Quaternion.Euler(25, 145, 0);

                PrefabUtility.SaveAsPrefabAsset(boss, Generated + "/BossMecha.prefab");
                EditorSceneManager.SaveScene(scene, ScenePath);
                AssetDatabase.SaveAssets();
                Debug.Log("Created " + ScenePath + ". Open the scene and press Play. Demo runs automatically.");
            }
            finally
            {
                if (hasSavedScene && previousScene.IsValid() && previousScene.isLoaded)
                {
                    SceneManager.SetActiveScene(previousScene);
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        private static AnimationClip ImportClip(string name, bool loop)
        {
            string path = Root + "/Animation/" + name + ".fbx";
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            if (importer == null) throw new FileNotFoundException(path);
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            var importedModel = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var hips = importedModel.GetComponentsInChildren<Transform>().First(t => t.name == "mixamorig:Hips");
            importer.motionNodeName = AnimationUtility.CalculateTransformPath(hips, importedModel.transform);
            importer.importAnimation = true;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            importer.optimizeGameObjects = false;
            var clips = importer.defaultClipAnimations;
            if (clips.Length == 0) throw new InvalidOperationException("No animation take in " + path);
            var clip = clips[0];
            clip.name = name;
            clip.loopTime = loop;
            clip.loopPose = loop;
            clip.keepOriginalOrientation = true;
            clip.lockRootRotation = name != "TurnLeft" && name != "TurnRight";
            clip.keepOriginalPositionY = true;
            clip.lockRootHeightY = true;
            clip.keepOriginalPositionXZ = true;
            clip.lockRootPositionXZ = false;
            if (name == "Punch")
            {
                // Full 308-frame take at 60fps. The left hand is the guard;
                // the right-hand strike peaks at Blender frame 125 / FBX frame 124.
                float frameCount = clip.lastFrame - clip.firstFrame;
                clip.events = new[]
                {
                    // ModelImporter events use normalized clip time, not seconds.
                    new AnimationEvent { time = 124f / frameCount, functionName = "PunchImpact" },
                    new AnimationEvent { time = (frameCount - 1f) / frameCount, functionName = "PunchFinished" }
                };
            }
            importer.clipAnimations = new[] { clip };
            importer.SaveAndReimport();
            var result = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => c.name == name);
            if (result == null) throw new InvalidOperationException("Missing imported clip " + name);
            return result;
        }

        private static AnimationClip LoadClip(string name) =>
            AssetDatabase.LoadAllAssetsAtPath(Root + "/Animation/" + name + ".fbx")
                .OfType<AnimationClip>().First(c => c.name == name);

        private static void PopulateController(AnimatorController controller, AnimationClip idle, AnimationClip walk, AnimationClip punch)
        {
            if (controller.layers.Length == 0) controller.AddLayer("Base Layer");
            var machine = controller.layers[0].stateMachine;
            var idleState = machine.AddState("Idle", new Vector3(240, 60));
            idleState.motion = idle;
            machine.defaultState = idleState;
            var walkState = machine.AddState("Walk", new Vector3(240, 140));
            walkState.motion = walk;
            var punchState = machine.AddState("Punch", new Vector3(240, 220));
            punchState.motion = punch;
            EditorUtility.SetDirty(idleState);
            EditorUtility.SetDirty(walkState);
            EditorUtility.SetDirty(punchState);
            EditorUtility.SetDirty(machine);
            EditorUtility.SetDirty(controller);
            // Subsequent material imports can refresh the database; persist before them.
            AssetDatabase.SaveAssets();
        }

        private static Material Material(string name, Color color, float metallic)
        {
            string path = Generated + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) throw new InvalidOperationException("No supported material shader found.");
            material = new Material(shader) { name = name, color = color };
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", 0.45f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static GameObject Primitive(string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.position = position;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = path.Substring(0, path.LastIndexOf('/'));
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(path.LastIndexOf('/') + 1));
        }

        public static void UpdatePresentationAndTest()
        {
            EditorSceneManager.OpenScene(ScenePath);
            var camera = Camera.main;
            camera.transform.position = new Vector3(6.5f, 4.2f, 5.5f);
            camera.transform.LookAt(new Vector3(0, 1.15f, -0.6f));
            var target = GameObject.Find("Target_Dummy").transform;
            target.position = new Vector3(0, 1.25f, 1.4f);
            target.localScale = new Vector3(0.7f, 1.25f, 0.7f);
            var fill = GameObject.Find("Fill Light").GetComponent<Light>();
            fill.intensity = 0.25f;
            fill.color = new Color(0.7f, 0.8f, 1f);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            BossModelSmokeTest.Run();
        }

        [MenuItem("Tools/Boss Prototype/Apply Right Punch Fix")]
        public static void ApplyRightPunchFix()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play mode before applying the fix.");
            var clip = ImportClip("Punch", false);
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Generated + "/BossAnimator.controller");
            var state = controller.layers[0].stateMachine.states.Single(s => s.state.name == "Punch").state;
            state.motion = clip;
            EditorUtility.SetDirty(state);
            AssetDatabase.SaveAssets();

            string prefabPath = Generated + "/BossMecha.prefab";
            var contents = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                FixPunchBinding(contents.GetComponent<BossAnimationDriver>(), clip.length);
                PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }

            EditorSceneManager.OpenScene(ScenePath);
            var demo = UnityEngine.Object.FindFirstObjectByType<BossModelDemo>();
            FixPunchBinding(demo.driver, clip.length);
            // The right fist is higher than the guard hand; raise the test target
            // instead of inflating the hit sphere to compensate for a wrong hand.
            var p = demo.target.position;
            p.y = 1.25f;
            demo.target.position = p;
            demo.target.localScale = new Vector3(0.7f, 1.25f, 0.7f);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            BossModelSmokeTest.Run();
        }

        private static void FixPunchBinding(BossAnimationDriver driver, float duration)
        {
            var hand = driver.animator.GetComponentsInChildren<Transform>()
                .Single(t => t.name == "mixamorig:RightHand");
            driver.hitPoint.SetParent(hand, false);
            driver.hitPoint.name = "HitPoint_RightPunch";
            driver.hitPoint.localPosition = Vector3.zero;
            driver.hitPoint.localRotation = Quaternion.identity;
            PositionAtFist(driver.hitPoint, driver.animator.transform);
            driver.hitRadius = 0.22f;
            driver.punchDuration = duration;
            var attack = new SerializedObject(driver.attack);
            attack.FindProperty("hitPoint").objectReferenceValue = driver.hitPoint;
            attack.FindProperty("hitRadius").floatValue = driver.hitRadius;
            attack.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(driver);
        }

        private static void PositionAtFist(Transform point, Transform model)
        {
            var middleKnuckle = model.GetComponentsInChildren<Transform>()
                .Single(t => t.name == "mixamorig:RightHandMiddle1");
            point.position = Vector3.Lerp(point.parent.position, middleKnuckle.position, 0.75f);
        }

        [MenuItem("Tools/Boss Prototype/Apply Turn Animations")]
        public static void ApplyTurnAnimations()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play mode first.");
            var leftSource = ImportClip("TurnLeft", false);
            var rightSource = ImportClip("TurnRight", false);
            var left = BakeTurn(leftSource, out var leftProgress);
            var right = BakeTurn(rightSource, out var rightProgress);
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Generated + "/BossAnimator.controller");
            var machine = controller.layers[0].stateMachine;
            foreach (var child in machine.states)
                if (child.state.name == "TurnLeftInPlace" || child.state.name == "TurnRightInPlace") machine.RemoveState(child.state);
            foreach (var clip in new[] { left, right })
            {
                var state = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == clip.name)
                    ?? machine.AddState(clip.name);
                state.motion = clip;
                EditorUtility.SetDirty(state);
            }
            EditorUtility.SetDirty(machine);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            string path = Generated + "/BossMecha.prefab";
            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                ConfigureTurns(contents.GetComponent<BossAnimationDriver>(), leftProgress, rightProgress);
                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
            EditorSceneManager.OpenScene(ScenePath);
            ConfigureTurns(UnityEngine.Object.FindFirstObjectByType<BossModelDemo>().driver, leftProgress, rightProgress);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            BossModelSmokeTest.Run();
        }

        private static void ConfigureTurns(BossAnimationDriver driver, AnimationCurve left, AnimationCurve right)
        {
            driver.leftTurnProgress = left;
            driver.rightTurnProgress = right;
            EditorUtility.SetDirty(driver);
        }

        private static AnimationClip BakeTurn(AnimationClip source, out AnimationCurve progress)
        {
            // Normalize the source BEFORE Animator blending. Correcting an already
            // blended skeleton twists the transition between differently facing poses.
            var model = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Animation/Idle.fbx"));
            model.hideFlags = HideFlags.HideAndDontSave;
            var baked = UnityEngine.Object.Instantiate(source);
            baked.name = source.name;
            try
            {
                var hips = model.GetComponentsInChildren<Transform>().Single(t => t.name == "mixamorig:Hips");
                string path = AnimationUtility.CalculateTransformPath(hips, model.transform);
                LoadClip("Idle").SampleAnimation(model, 0);
                Quaternion reference = hips.rotation;
                int frames = Mathf.CeilToInt(source.length * 60f);
                var rotations = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
                var yaw = new float[frames + 1];
                Quaternion previous = Quaternion.identity;
                for (int i = 0; i <= frames; i++)
                {
                    float time = source.length * i / frames;
                    source.SampleAnimation(model, time);
                    var delta = hips.rotation * Quaternion.Inverse(reference);
                    Vector3 forward = delta * Vector3.forward;
                    float angle = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
                    yaw[i] = i == 0 ? angle : yaw[i - 1] + Mathf.DeltaAngle(yaw[i - 1], angle);
                    Quaternion q = Quaternion.Inverse(hips.parent.rotation) * Quaternion.AngleAxis(-angle, Vector3.up) * hips.rotation;
                    if (i > 0 && Quaternion.Dot(previous, q) < 0) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                    previous = q;
                    rotations[0].AddKey(time, q.x); rotations[1].AddKey(time, q.y);
                    rotations[2].AddKey(time, q.z); rotations[3].AddKey(time, q.w);
                }
                foreach (var binding in AnimationUtility.GetCurveBindings(baked))
                    if (binding.path == path && (binding.propertyName.StartsWith("m_LocalRotation") || binding.propertyName.StartsWith("localEulerAngles")))
                        AnimationUtility.SetEditorCurve(baked, binding, null);
                var axes = new[] { "x", "y", "z", "w" };
                for (int i = 0; i < 4; i++)
                    AnimationUtility.SetEditorCurve(baked, EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalRotation." + axes[i]), rotations[i]);
                baked.EnsureQuaternionContinuity();
                float total = yaw[frames] - yaw[0];
                if (Mathf.Abs(total) < 60 || Mathf.Abs(total) > 120) throw new InvalidOperationException("Unexpected source turn yaw: " + total);
                progress = new AnimationCurve();
                for (int i = 0; i <= frames; i++) progress.AddKey((float)i / frames, (yaw[i] - yaw[0]) / total);
                for (int i = 0; i <= frames; i++)
                {
                    AnimationUtility.SetKeyLeftTangentMode(progress, i, AnimationUtility.TangentMode.Linear);
                    AnimationUtility.SetKeyRightTangentMode(progress, i, AnimationUtility.TangentMode.Linear);
                }
                string assetPath = Generated + "/" + source.name + "InPlace.anim";
                var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
                if (existing == null) AssetDatabase.CreateAsset(baked, assetPath);
                else { EditorUtility.CopySerialized(baked, existing); UnityEngine.Object.DestroyImmediate(baked); baked = existing; }
                baked.name = source.name;
                EditorUtility.SetDirty(baked);
                AssetDatabase.SaveAssets();
                Debug.Log($"Baked {source.name}: {frames + 1} samples, source yaw={total:F3}");
                return baked;
            }
            finally { UnityEngine.Object.DestroyImmediate(model); }
        }

        private static AnimationCurve TurnCurve(float[] values)
        {
            var curve = new AnimationCurve(values.Select((v, i) => new Keyframe(Mathf.Min(i * 4, 62) / 62f, v)).ToArray());
            for (int i = 0; i < curve.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
            }
            return curve;
        }
    }
}

