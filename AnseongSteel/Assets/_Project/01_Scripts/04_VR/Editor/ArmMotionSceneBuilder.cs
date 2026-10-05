using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AnseongSteel.PlayerMotion.Editor
{
    public static class ArmMotionSceneBuilder
    {
        const string Art = "Assets/_Project/02_Art/";
        public const string ScenePath = "Assets/_Project/04_Scenes/ArmMotionTest.unity";
        public const string PrefabPath = "Assets/_Project/05_Prefabs/TrackedRobotArms.prefab";
        const string ModelPath = Art + "01_Models/mecha-red-points-mixamo-v025.fbx";
        [MenuItem("Anseong Steel/Player Motion/Create controller tracking scene")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var frame = new GameObject("Tracked Robot Arms").transform; frame.position = new Vector3(0, 0, 100);
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (!asset) throw new Exception("Uploaded rig not found");
            var model = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            model.transform.SetParent(frame, false); model.transform.localScale = Vector3.one * 6; model.transform.localPosition = new Vector3(0, -3.15f, 0);
            foreach (var animator in model.GetComponentsInChildren<Animator>()) animator.enabled = false;
            var bones = model.GetComponentsInChildren<Transform>();
            Transform Bone(string suffix) => bones.First(t => t.name.EndsWith(suffix, StringComparison.Ordinal));
            if (Bone("LeftArm").position.x > Bone("RightArm").position.x) model.transform.localRotation = Quaternion.Euler(0, 180, 0);
            var followers = new ControllerArmFollower[2];
            var cyan = Material("ArmMotion_Cyan", new Color(.1f, .9f, 1), true);
            var yellow = Material("ArmMotion_Yellow", new Color(1, .7f, .08f), true);
            for (int i = 0; i < 2; i++)
            {
                bool left = i == 0; string side = left ? "Left" : "Right";
                var follower = new GameObject(side + " Controller Follower").AddComponent<ControllerArmFollower>(); follower.transform.SetParent(frame, false);
                follower.upper = Bone(side + "Arm"); follower.forearm = Bone(side + "ForeArm"); follower.hand = Bone(side + "Hand"); follower.robotFrame = frame;
                follower.neutralTarget = new Vector3(left ? -.65f : .65f, 2.1f, .95f);
                follower.bendDirection = new Vector3(left ? -1 : 1, -.5f, -.2f);
                follower.neutralWrist = Quaternion.FromToRotation((follower.hand.position - follower.forearm.position).normalized, Vector3.forward) * follower.hand.rotation;
                follower.targetMarker = Shape(side + " raw controller goal", PrimitiveType.Sphere, frame, follower.neutralTarget, Vector3.one * .075f, cyan).transform;
                follower.actualMarker = Shape(side + " solved wrist", PrimitiveType.Sphere, frame, follower.neutralTarget, Vector3.one * .04f, yellow).transform;
                followers[i] = follower;
            }
            CombineArmSkin(model, followers);
            model.name = "Player - blue - uploaded Mixamo model";
            var skin = model.GetComponentsInChildren<SkinnedMeshRenderer>().First(r => r.enabled);
            // Imported renderer bounds may still cache the previous generated mesh.
            // Use current rest vertices for deterministic physical view heights.
            var bounds = new Bounds(model.transform.TransformPoint(skin.sharedMesh.vertices[0]), Vector3.zero);
            foreach (var vertex in skin.sharedMesh.vertices) bounds.Encapsulate(model.transform.TransformPoint(vertex));
            if (bounds.size.y < 5.5f || bounds.size.y > 6.5f) throw new Exception("Unexpected robot height: " + bounds);
            var boss = UnityEngine.Object.Instantiate(model, frame);
            boss.name = "Boss - red - same model and scale";
            boss.transform.localPosition += Vector3.forward * 7;
            boss.transform.localRotation = Quaternion.Euler(0, 180, 0) * model.transform.localRotation;
            boss.transform.SetParent(null, true);
            foreach (var renderer in boss.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r => r.enabled))
                renderer.sharedMaterials = renderer.sharedMaterials.Select(m => m.name.Contains("CE121D") ? Material("ArmMotion_Boss_Red", new Color(.8f, .045f, .025f), false) : m).ToArray();
            skin.sharedMaterials = skin.sharedMaterials.Select(m => m.name.Contains("CE121D") ? Material("ArmMotion_Player_Blue", new Color(.02f, .4f, 1f), false)
                : m.name.Contains("7C7C7C") || m.name.Contains("E7E7E7") ? Material("ArmMotion_Player_Armor_Blue", new Color(.035f, .18f, .52f), false) : m).ToArray();
            // Pose the same arm bones on the opponent into a comparable neutral guard.
            foreach (var source in followers)
            {
                var f = boss.AddComponent<ControllerArmFollower>();
                var bb = boss.GetComponentsInChildren<Transform>();
                f.upper = bb.First(t => t.name == source.upper.name);
                f.forearm = bb.First(t => t.name == source.forearm.name);
                f.hand = bb.First(t => t.name == source.hand.name);
                var bossFrame = new GameObject("Boss pose reference").transform;
                bossFrame.SetParent(frame, false); bossFrame.localPosition = Vector3.forward * 7; bossFrame.localRotation = Quaternion.Euler(0, 180, 0);
                bossFrame.SetParent(null, true);
                f.robotFrame = bossFrame; f.neutralTarget = source.neutralTarget; f.bendDirection = source.bendDirection;
                f.neutralWrist = source.neutralWrist; f.Initialize(); f.Follow(Vector3.zero, Quaternion.identity);
            }
            foreach (var f in followers) { f.Initialize(); f.Follow(Vector3.zero, Quaternion.identity); }
            var firstPersonArms = CreateFirstPersonArms(skin, followers);
            PrefabUtility.SaveAsPrefabAssetAndConnect(frame.gameObject, PrefabPath, InteractionMode.AutomatedAction);

            var cockpitAsset = AssetDatabase.LoadAssetAtPath<GameObject>(Art + "CockpitV02/Prefabs/Cockpit_V02.prefab");
            if (!cockpitAsset) throw new Exception("Existing Cockpit V02 prefab not found");
            var cockpit = (GameObject)PrefabUtility.InstantiatePrefab(cockpitAsset);
            cockpit.name = "Existing Cockpit V02 - live display";
            var input = new GameObject("Real Controller Motion Test").AddComponent<ArmMotionInput>();
            input.leftArm = followers[0]; input.rightArm = followers[1];
            var cockpitTransforms = cockpit.GetComponentsInChildren<Transform>();
            input.leftSeat = cockpitTransforms.First(t => t.name == "Pilot_Left_Floor");
            input.rightSeat = cockpitTransforms.First(t => t.name == "Pilot_Right_Floor");
            input.pilotSeat = new GameObject("Local Pilot Tracking Origin").transform;
            input.pilotSeat.position = input.leftSeat.position;
            var head = CameraObject("Cockpit HMD Camera", input.pilotSeat, Vector3.up * 1.65f, 75, 25);
            head.tag = "MainCamera"; head.gameObject.AddComponent<AudioListener>(); input.cockpitCamera = head;
            var exterior = CameraObject("Robot optical mount - independent of pilot HMD", frame, new Vector3(0, 1.8f, -.35f), 70, 40);
            var leftCamera = CameraObject("Robot left panorama sector", exterior.transform, Vector3.zero, 70, 40);
            var rightCamera = CameraObject("Robot right panorama sector", exterior.transform, Vector3.zero, 70, 40);
            leftCamera.transform.localRotation = Quaternion.Euler(0, -CockpitLiveView.SectorDegrees, 0);
            rightCamera.transform.localRotation = Quaternion.Euler(0, CockpitLiveView.SectorDegrees, 0);
            float tanH = Mathf.Tan(CockpitLiveView.SectorDegrees * .5f * Mathf.Deg2Rad), tanV = Mathf.Tan(35 * Mathf.Deg2Rad);
            foreach (var camera in new[] { exterior, leftCamera, rightCamera }) { camera.aspect = tanH / tanV; camera.stereoTargetEye = StereoTargetEyeMask.None; camera.depth = -10; }
            var mounts = cockpit.AddComponent<CockpitViewPosition>(); mounts.feedCamera = exterior;
            Transform Mount(string name, float height)
            {
                var t = new GameObject(name + " view anchor").transform; t.SetParent(frame, false);
                // View from behind the guard. Camera-local body hiding prevents the
                // player's own helmet/chest from obstructing an internal view mount.
                t.localPosition = new Vector3(0, height - frame.position.y, -.35f);
                t.localRotation = Quaternion.Euler(6, 0, 0); return t;
            }
            mounts.head = Mount("Head", Mathf.Lerp(bounds.min.y, bounds.max.y, .94f));
            mounts.neck = Mount("Neck", Mathf.Lerp(bounds.min.y, bounds.max.y, .82f));
            mounts.chest = Mount("Chest", Mathf.Lerp(bounds.min.y, bounds.max.y, .69f));
            mounts.neck.localRotation = Quaternion.identity;
            mounts.chest.localRotation = Quaternion.Euler(-20, 0, 0);
            mounts.Select(CockpitViewPosition.Mount.Head);
            mounts.status = Label("View position", cockpit.transform, new Vector3(.3f, 1.5f, 1.5f), .10f);
            var screen = cockpitTransforms.First(t => t.name == "10_Display").GetComponent<Renderer>();
            var slots = screen.sharedMaterials; int slot = Array.FindIndex(slots, m => m && m.name.Contains("Display_Feed"));
            if (slot < 0) throw new Exception("Cockpit display feed material slot not found");
            var liveMaterial = Material("ArmMotion_LiveFeed", Color.white, true);
            liveMaterial.shader = Shader.Find("AnseongSteel/Fixed Robot Panorama");
            var centerFeed = PanoramaFeed("Center"); var leftFeed = PanoramaFeed("Left"); var rightFeed = PanoramaFeed("Right");
            liveMaterial.SetTexture("_CenterFeed", centerFeed); liveMaterial.SetTexture("_LeftFeed", leftFeed); liveMaterial.SetTexture("_RightFeed", rightFeed);
            liveMaterial.SetFloat("_TanHalfHorizontal", tanH); liveMaterial.SetFloat("_TanHalfVertical", tanV); EditorUtility.SetDirty(liveMaterial);
            slots[slot] = liveMaterial; screen.sharedMaterials = slots;
            var view = cockpit.AddComponent<CockpitLiveView>(); view.exteriorCamera = exterior; view.cockpitCamera = head;
            view.leftCamera = leftCamera; view.rightCamera = rightCamera; view.screen = screen; view.materialSlot = slot;
            view.centerFeed = centerFeed; view.leftFeed = leftFeed; view.rightFeed = rightFeed;
            exterior.targetTexture = centerFeed; leftCamera.targetTexture = leftFeed; rightCamera.targetTexture = rightFeed;
            view.playerBody = skin; view.firstPersonArms = firstPersonArms;
            input.cockpitStatus = Label("Tracking diagnostics", cockpit.transform, new Vector3(-1.8f, .8f, 1.5f), .065f);
            var trace = new GameObject("Measured controller trajectory").AddComponent<LineRenderer>(); trace.transform.SetParent(frame, false);
            trace.useWorldSpace = true; trace.positionCount = 0; trace.widthMultiplier = .018f; trace.sharedMaterial = cyan; input.trail = trace;
            var grey = Material("ArmMotion_Arena", new Color(.07f, .11f, .14f), false);
            float floorY = bounds.min.y - frame.position.y;
            Shape("Exterior test floor", PrimitiveType.Cube, frame, new Vector3(0, floorY - .05f, 5), new Vector3(25, .1f, 25), grey);
            for (int i = -3; i <= 3; i++) Shape("Distance grid " + i, PrimitiveType.Cube, frame, new Vector3(i, floorY + .006f, 4), new Vector3(.012f, .006f, 10), cyan);
            var light = new GameObject("Key light").AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.5f; light.transform.rotation = Quaternion.Euler(45, -30, 0);
            RenderSettings.ambientLight = new Color(.45f, .5f, .6f);
            EditorSceneManager.SaveScene(scene, ScenePath); AssetDatabase.SaveAssets();
            Directory.CreateDirectory("ArmMotionValidation");
            File.WriteAllText("ArmMotionValidation/scene-build.txt", $"Scene: {ScenePath}\nCockpit panorama: {screen.name}, material slot {slot}\nThree 2048x2048 angular sectors, fixed robot camera; independent pilot head\nTwo bone-controlled arms, no animation input\n");
            Debug.Log("ARM_MOTION_SCENE_BUILT");
        }

        static RenderTexture PanoramaFeed(string side)
        {
            string path = Art + "02_Textures/ArmMotionPanorama" + side + ".renderTexture";
            var texture = AssetDatabase.LoadAssetAtPath<RenderTexture>(path);
            if (!texture) { texture = new RenderTexture(2048, 2048, 24) { name = "Robot panorama " + side, antiAliasing = 4 }; AssetDatabase.CreateAsset(texture, path); }
            texture.filterMode = FilterMode.Bilinear; texture.wrapMode = TextureWrapMode.Clamp; texture.useMipMap = false;
            return texture;
        }

        static SkinnedMeshRenderer CreateFirstPersonArms(SkinnedMeshRenderer full, ControllerArmFollower[] arms)
        {
            var source = full.sharedMesh;
            bool ArmBone(int index) => full.bones[index].IsChildOf(arms[0].upper) || full.bones[index].IsChildOf(arms[1].upper);
            var weights = source.boneWeights;
            var included = weights.Select(w => (w.weight0 > .2f && ArmBone(w.boneIndex0)) || (w.weight1 > .2f && ArmBone(w.boneIndex1))
                || (w.weight2 > .2f && ArmBone(w.boneIndex2)) || (w.weight3 > .2f && ArmBone(w.boneIndex3))).ToArray();
            string path = Art + "01_Models/ArmMotionFirstPersonArms.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path); bool created = !mesh;
            if (created) mesh = new Mesh(); else mesh.Clear();
            mesh.name = "Same tracked geometry - arms only for internal feed camera"; mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertices = source.vertices; mesh.normals = source.normals; mesh.uv = source.uv;
            mesh.boneWeights = weights; mesh.bindposes = source.bindposes; mesh.subMeshCount = source.subMeshCount;
            for (int sub = 0; sub < source.subMeshCount; sub++)
            {
                var triangles = source.GetTriangles(sub); var selected = new List<int>();
                for (int i = 0; i < triangles.Length; i += 3)
                    if (included[triangles[i]] && included[triangles[i + 1]] && included[triangles[i + 2]])
                    { selected.Add(triangles[i]); selected.Add(triangles[i + 1]); selected.Add(triangles[i + 2]); }
                mesh.SetTriangles(selected, sub);
            }
            mesh.RecalculateBounds(); if (created) AssetDatabase.CreateAsset(mesh, path); else EditorUtility.SetDirty(mesh);
            var display = new GameObject("First person tracked arms - feed camera only").AddComponent<SkinnedMeshRenderer>();
            display.transform.SetParent(full.transform.parent, false); display.sharedMesh = mesh;
            display.bones = full.bones; display.rootBone = full.rootBone; display.sharedMaterials = full.sharedMaterials;
            display.localBounds = full.localBounds; display.updateWhenOffscreen = true; display.enabled = false;
            return display;
        }

        static void CombineArmSkin(GameObject model, ControllerArmFollower[] arms)
        {
            // Derive a compact display mesh; the supplied rig/model asset stays untouched.
            var vertices = new List<Vector3>(); var normals = new List<Vector3>(); var uv = new List<Vector2>(); var weights = new List<BoneWeight>();
            var bones = new List<Transform>(); var groups = new Dictionary<string, List<int>>(); var materials = new Dictionary<string, Material>();
            foreach (var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var oldBones = renderer.bones;
                var mesh = renderer.sharedMesh; var oldWeights = mesh.boneWeights;
                renderer.enabled = false;
                int Remap(int index)
                { if (index >= oldBones.Length || !oldBones[index]) return 0; var bone = oldBones[index]; int mapped = bones.IndexOf(bone); if (mapped < 0) { mapped = bones.Count; bones.Add(bone); } return mapped; }
                // Evaluate the source skin in model space explicitly. BakeMesh includes
                // inherited FBX scale on this rig, which would apply the robot scale twice.
                var bindposes = mesh.bindposes;
                var matrices = oldBones.Select((b, i) => model.transform.worldToLocalMatrix * b.localToWorldMatrix * bindposes[i]).ToArray();
                var oldVertices = mesh.vertices; var oldNormals = mesh.normals; var oldUv = mesh.uv; int offset = vertices.Count;
                for (int i = 0; i < oldVertices.Length; i++)
                {
                    var w = oldWeights[i]; var v = oldVertices[i]; var n = oldNormals[i];
                    Vector3 vertex = Vector3.zero, normal = Vector3.zero;
                    void Add(int bone, float weight) { if (weight <= 0) return; vertex += matrices[bone].MultiplyPoint3x4(v) * weight; normal += matrices[bone].MultiplyVector(n) * weight; }
                    Add(w.boneIndex0, w.weight0); Add(w.boneIndex1, w.weight1); Add(w.boneIndex2, w.weight2); Add(w.boneIndex3, w.weight3);
                    vertices.Add(vertex); normals.Add(normal.normalized);
                    uv.Add(i < oldUv.Length ? oldUv[i] : Vector2.zero);
                    weights.Add(new BoneWeight { boneIndex0 = Remap(w.boneIndex0), boneIndex1 = Remap(w.boneIndex1), boneIndex2 = Remap(w.boneIndex2), boneIndex3 = Remap(w.boneIndex3), weight0 = w.weight0, weight1 = w.weight1, weight2 = w.weight2, weight3 = w.weight3 });
                }
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    var sourceMaterial = renderer.sharedMaterials[Mathf.Min(sub, renderer.sharedMaterials.Length - 1)];
                    Color color = sourceMaterial && sourceMaterial.HasProperty("_Color") ? sourceMaterial.color : Color.gray;
                    string key = ColorUtility.ToHtmlStringRGB(color);
                    if (!groups.ContainsKey(key)) { groups[key] = new List<int>(); materials[key] = Material("ArmMotion_Skin_" + key, color, false); }
                    groups[key].AddRange(mesh.GetTriangles(sub).Select(t => t + offset));
                }
            }
            if (vertices.Count == 0) throw new Exception("No skinned arm geometry found");
            string path = Art + "01_Models/ArmMotionArms.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            var combined = existing ? existing : new Mesh();
            combined.Clear(); combined.name = "Controller tracked robot"; combined.indexFormat = IndexFormat.UInt32;
            combined.SetVertices(vertices); combined.SetNormals(normals); combined.SetUVs(0, uv); combined.boneWeights = weights.ToArray();
            combined.bindposes = bones.Select(b => b.worldToLocalMatrix * model.transform.localToWorldMatrix).ToArray();
            var keys = groups.Keys.ToArray(); combined.subMeshCount = keys.Length;
            for (int i = 0; i < keys.Length; i++) combined.SetTriangles(groups[keys[i]], i);
            combined.RecalculateBounds();
            if (existing) EditorUtility.SetDirty(existing);
            else AssetDatabase.CreateAsset(combined, path);
            var display = new GameObject("Combined tracked arm skin").AddComponent<SkinnedMeshRenderer>(); display.transform.SetParent(model.transform, false);
            display.sharedMesh = combined; display.bones = bones.ToArray(); display.rootBone = model.transform; display.sharedMaterials = keys.Select(k => materials[k]).ToArray(); display.updateWhenOffscreen = true;
            display.localBounds = combined.bounds;
            Debug.Log($"ARM_SKIN vertices={vertices.Count}, material groups={keys.Length}, bones={bones.Count}");
        }
        static Material Material(string name, Color color, bool unlit)
        {
            string path = Art + name + ".mat"; var result = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!result) { result = new Material(Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(result, path); }
            result.color = color; return result;
        }
        static GameObject Shape(string name, PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Material material)
        { var g = GameObject.CreatePrimitive(type); g.name = name; g.transform.SetParent(parent, false); g.transform.localPosition = position; g.transform.localScale = scale; UnityEngine.Object.DestroyImmediate(g.GetComponent<Collider>()); g.GetComponent<Renderer>().sharedMaterial = material; return g; }
        static Camera CameraObject(string name, Transform parent, Vector3 p, float fov, float far)
        { var c = new GameObject(name).AddComponent<Camera>(); c.transform.SetParent(parent, false); c.transform.localPosition = p; c.nearClipPlane = .03f; c.farClipPlane = far; c.fieldOfView = fov; c.clearFlags = CameraClearFlags.SolidColor; c.backgroundColor = new Color(.025f, .045f, .07f); return c; }
        static TextMesh Label(string name, Transform parent, Vector3 p, float scale)
        { var t = new GameObject(name).AddComponent<TextMesh>(); t.transform.SetParent(parent, false); t.transform.localPosition = p; t.transform.localScale = Vector3.one * scale; t.anchor = TextAnchor.UpperLeft; t.fontSize = 48; t.characterSize = .12f; t.color = Color.cyan; return t; }
    }
}
