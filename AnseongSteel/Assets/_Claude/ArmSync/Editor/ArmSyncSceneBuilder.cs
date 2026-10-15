using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace AnseongSteel.ArmSync.EditorTools
{
    // Builds the controller -> robot arm verification scene from scratch.
    // Only creates files under Assets/_Claude/ArmSync; existing scenes are not touched.
    public static class ArmSyncSceneBuilder
    {
        const string Root = "Assets/_Claude/ArmSync";
        const string ScenePath = Root + "/ArmSyncTest.unity";
        const string MaterialFolder = Root + "/Generated";
        const string ModelPath = "Assets/_Project/02_Art/01_Models/mecha-finger-rig-mixamo-v026.fbx";
        const float RobotScale = 6f; // same scale as the robot in sunwoo_IntegratedPlay

        [MenuItem("Anseong Steel/Claude Arm Sync/Build test scene")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (!model) { EditorUtility.DisplayDialog("Arm Sync", "Robot model not found:\n" + ModelPath, "OK"); return; }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EnsureFolder(MaterialFolder);
            var ghostMat = UnlitMaterial("Ghost", new Color(.92f, .92f, .92f));
            var expectedMat = UnlitMaterial("Expected", new Color(.1f, .85f, 1f));
            var errorMat = UnlitMaterial("Error", new Color(1f, .2f, .15f));
            var targetMat = UnlitMaterial("Target", new Color(1f, .75f, .1f));
            var floorMat = LitMaterial("Floor", new Color(.32f, .34f, .37f));

            // Light and floor.
            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.2f;
            light.transform.rotation = Quaternion.Euler(50, -30, 0);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            var floor = Primitive(PrimitiveType.Plane, "Floor", null, floorMat);
            floor.transform.localScale = Vector3.one * 8;

            // Robot.
            var robot = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
            robot.name = "Robot (v026)";
            robot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            robot.transform.localScale = Vector3.one * RobotScale;
            foreach (var animator in robot.GetComponentsInChildren<Animator>()) animator.enabled = false;

            var arm = new GameObject("Arm Sync");
            var input = arm.AddComponent<ArmSyncInput>();
            var sync = arm.AddComponent<ArmSyncRig>();
            sync.input = input;
            sync.leftArm = Arm(robot.transform, "Left");
            sync.rightArm = Arm(robot.transform, "Right");
            sync.robotHeadBone = RobotFistPose.Find(robot.transform, "mixamorig:Head");
            if (sync.leftArm == null || sync.rightArm == null)
            {
                EditorUtility.DisplayDialog("Arm Sync", "mixamorig arm bones were not found in the robot model.", "OK");
                return;
            }

            // Tracking space. ArmSyncRig scales and places it on recenter.
            var rig = new GameObject("Tracking Space (scaled)").transform;
            var cameraGo = new GameObject("Head Camera");
            cameraGo.tag = "MainCamera";
            cameraGo.transform.SetParent(rig, false);
            var cam = cameraGo.AddComponent<Camera>();
            cam.nearClipPlane = .05f; cam.farClipPlane = 500f;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.11f, .12f, .14f);
            cameraGo.AddComponent<AudioListener>();
            sync.rig = rig; sync.head = cameraGo.transform;
            sync.leftController = ControllerShape("Left controller (real)", rig, ghostMat);
            sync.rightController = ControllerShape("Right controller (real)", rig, ghostMat);

            // HUD in front of the eyes (inherits the rig scale, so its apparent size is fixed).
            var hudGo = new GameObject("HUD");
            hudGo.transform.SetParent(cameraGo.transform, false);
            hudGo.transform.localPosition = new Vector3(-.2f, -.05f, .5f);
            var text = hudGo.AddComponent<TextMesh>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.font = font; text.fontSize = 64; text.characterSize = .0014f;
            text.anchor = TextAnchor.UpperLeft; text.color = Color.white; text.text = "ARM SYNC";
            hudGo.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            sync.hud = text;

            // Markers: controller replicas where the fists should be, and error lines.
            var markers = new GameObject("Verification markers");
            sync.markersRoot = markers;
            sync.leftExpected = ControllerShape("Left expected (replica)", markers.transform, expectedMat);
            sync.rightExpected = ControllerShape("Right expected (replica)", markers.transform, expectedMat);
            sync.leftErrorLine = ErrorLine("Left error", markers.transform, errorMat);
            sync.rightErrorLine = ErrorLine("Right error", markers.transform, errorMat);

            // Side view monitor (off by default; left stick click or V).
            var rt = new RenderTexture(512, 512, 24) { name = "ArmSyncSideView" };
            string rtPath = MaterialFolder + "/ArmSyncSideView.renderTexture";
            AssetDatabase.DeleteAsset(rtPath); AssetDatabase.CreateAsset(rt, rtPath);
            var sideCam = new GameObject("Side view camera").AddComponent<Camera>();
            sideCam.targetTexture = rt; sideCam.stereoTargetEye = StereoTargetEyeMask.None;
            sideCam.clearFlags = CameraClearFlags.SolidColor; sideCam.backgroundColor = new Color(.08f, .09f, .1f);
            sideCam.fieldOfView = 50; sideCam.enabled = false;
            var monitorMat = UnlitMaterial("SideViewMonitor", Color.white);
            monitorMat.SetTexture("_BaseMap", rt);
            var monitor = Primitive(PrimitiveType.Quad, "Side view monitor", cameraGo.transform, monitorMat);
            monitor.transform.localPosition = new Vector3(.2f, -.13f, .5f);
            monitor.transform.localScale = Vector3.one * .16f;
            monitor.SetActive(false);
            sync.sideCamera = sideCam; sync.sideMonitor = monitor;

            // Punch targets at 90% reach: left, centre, right.
            var shoulders = (sync.leftArm.upper.position + sync.rightArm.upper.position) * .5f;
            float reach = (Vector3.Distance(sync.leftArm.upper.position, sync.leftArm.lower.position)
                + Vector3.Distance(sync.leftArm.lower.position, sync.leftArm.hand.position));
            Vector3 right = Vector3.ProjectOnPlane(sync.rightArm.upper.position - sync.leftArm.upper.position, Vector3.up).normalized;
            Vector3 forward = Vector3.Cross(right, Vector3.up);
            var targets = new GameObject("Punch targets").transform;
            foreach (var x in new[] { -.45f, 0f, .45f })
            {
                var t = Primitive(PrimitiveType.Sphere, "Target", targets, targetMat);
                t.transform.position = shoulders + forward * reach * .95f + right * x * reach;
                t.transform.localScale = Vector3.one * reach * .08f;
            }

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = arm;
            Debug.Log("Arm Sync test scene saved: " + ScenePath);
        }

        [MenuItem("Anseong Steel/Claude Arm Sync/Open test scene")]
        public static void Open()
        {
            if (!System.IO.File.Exists(ScenePath)) { Build(); return; }
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath);
        }

        // Changes ProjectSettings/EditorBuildSettings.asset, so it is a separate, explicit step.
        [MenuItem("Anseong Steel/Claude Arm Sync/Make test scene the first build scene")]
        public static void UseForBuild()
        {
            if (!System.IO.File.Exists(ScenePath)) Build();
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log("Arm Sync: " + ScenePath + " is now build scene 0. Undo by reordering in Build Profiles.");
        }

        static ArmSyncArm Arm(Transform robot, string side)
        {
            var upper = RobotFistPose.Find(robot, "mixamorig:" + side + "Arm");
            var lower = RobotFistPose.Find(robot, "mixamorig:" + side + "ForeArm");
            var hand = RobotFistPose.Find(robot, "mixamorig:" + side + "Hand");
            if (!upper || !lower || !hand) return null;
            var fist = hand.gameObject.AddComponent<RobotFistPose>();
            fist.side = side;
            return new ArmSyncArm { upper = upper, lower = lower, hand = hand, fist = fist };
        }

        // About the size of a Quest controller; +Z is the controller's pointing direction.
        static Transform ControllerShape(string name, Transform parent, Material mat)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            var body = Primitive(PrimitiveType.Cube, "Body", root, mat);
            body.transform.localPosition = new Vector3(0, 0, -.02f);
            body.transform.localScale = new Vector3(.045f, .04f, .1f);
            var pointer = Primitive(PrimitiveType.Cylinder, "Pointer +Z", root, mat);
            pointer.transform.localPosition = new Vector3(0, 0, .07f);
            pointer.transform.localRotation = Quaternion.Euler(90, 0, 0);
            pointer.transform.localScale = new Vector3(.008f, .04f, .008f);
            var top = Primitive(PrimitiveType.Cube, "Top +Y", root, mat);
            top.transform.localPosition = new Vector3(0, .03f, -.02f);
            top.transform.localScale = new Vector3(.01f, .02f, .01f);
            return root;
        }

        static LineRenderer ErrorLine(string name, Transform parent, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = mat; line.widthMultiplier = .03f; line.positionCount = 2;
            line.shadowCastingMode = ShadowCastingMode.Off;
            return line;
        }

        static GameObject Primitive(PrimitiveType type, string name, Transform parent, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            if (parent) go.transform.SetParent(parent, false);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        static Material UnlitMaterial(string name, Color color) => SavedMaterial(name, "Universal Render Pipeline/Unlit", color);
        static Material LitMaterial(string name, Color color) => SavedMaterial(name, "Universal Render Pipeline/Lit", color);

        static Material SavedMaterial(string name, string shaderName, Color color)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find(shaderName) ?? Shader.Find("Unlit/Color");
            if (!mat) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, path); }
            mat.shader = shader;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
