using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using AnseongSteel.PilotV01;

public static class PilotV01Builder
{
    private const string Root = "Assets/_Project/02_Art/PilotV01";
    private const string ModelPath = Root + "/AS_Pilot_v01.fbx";

    [MenuItem("Anseong Steel/Pilot V01/Build isolated review scene")]
    public static void Build()
    {
        Directory.CreateDirectory(Root + "/Materials");
        Directory.CreateDirectory(Root + "/Prefabs");
        Directory.CreateDirectory(Root + "/Scenes");
        Directory.CreateDirectory(Root + "/Validation");
        AssetDatabase.Refresh();
        var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.importAnimation = false;
        importer.isReadable = true;
        importer.optimizeGameObjects = false;
        importer.meshCompression = ModelImporterMeshCompression.Off;
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        importer.SaveAndReimport();

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        var transforms = model.GetComponentsInChildren<Transform>(true);
        var names = new HashSet<string>(transforms.Select(t => t.name));
        var description = new HumanDescription
        {
            human = HumanTrait.BoneName.Where(n => names.Contains(n.Replace(" ", "")))
                .Select(n => new HumanBone { humanName = n, boneName = n.Replace(" ", ""), limit = new HumanLimit { useDefaultValues = true } }).ToArray(),
            skeleton = transforms.Select(t => new SkeletonBone { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale }).ToArray(),
            upperArmTwist = .5f, lowerArmTwist = .5f, upperLegTwist = .5f, lowerLegTwist = .5f,
            armStretch = .05f, legStretch = .05f, feetSpacing = 0, hasTranslationDoF = false
        };
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.humanDescription = description;
        importer.SaveAndReimport();
        model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        var avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().FirstOrDefault();
        if (!avatar || !avatar.isValid || !avatar.isHuman) throw new Exception("Humanoid Avatar validation failed. See Unity import log.");

        var mats = new Dictionary<string, Material>();
        mats["AS_CeramicWhite"] = MakeMaterial("AS_CeramicWhite", new Color(.89f,.915f,.91f), .18f,.32f);
        mats["AS_FlexGraphite"] = MakeMaterial("AS_FlexGraphite", new Color(.172f,.215f,.229f), .05f,.61f);
        mats["AS_AmberVisor"] = MakeMaterial("AS_AmberVisor", new Color(.906f,.575f,.183f), .78f,.22f);
        mats["AS_TeamAccent"] = MakeMaterial("AS_TeamAccent", new Color(.172f,.584f,.61f), .35f,.32f);
        var rightAccent = MakeMaterial("AS_TeamAccent_Right",new Color(.9f,.36f,.12f),.35f,.32f);
        foreach(var entry in mats)
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), entry.Key),entry.Value);
        importer.SaveAndReimport();
        model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var left = (GameObject)PrefabUtility.InstantiatePrefab(model);
        left.name = "Pilot_Left";
        var head = left.GetComponentsInChildren<SkinnedMeshRenderer>().Single(r=>r.name=="Pilot_Head");
        left.AddComponent<PilotLocalView>().SetHead(head);
        var leftAnimator=left.GetComponent<Animator>();
        AddSocket(leftAnimator,HumanBodyBones.Chest,"Mount_Back",new Vector3(0,1.335f,-.155f));
        AddSocket(leftAnimator,HumanBodyBones.LeftLowerArm,"Mount_LeftArm",new Vector3(-.55f,1.445f,-.075f));
        AddSocket(leftAnimator,HumanBodyBones.RightLowerArm,"Mount_RightArm",new Vector3(.55f,1.445f,-.075f));
        foreach(var r in left.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            // Conservative bounds for overhead, side and forward VR reaches.
            r.localBounds = new Bounds(r.localBounds.center,new Vector3(3f,3f,3f));
            r.updateWhenOffscreen = false;
            r.quality = SkinQuality.Bone2;
        }
        PrefabUtility.SaveAsPrefabAsset(left,Root+"/Prefabs/AS_Pilot_Left.prefab");
        var right = UnityEngine.Object.Instantiate(left);
        right.name = "Pilot_Right";
        foreach(var r in right.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            r.sharedMaterials = r.sharedMaterials.Select(m => m && m.name == "AS_TeamAccent" ? rightAccent : m).ToArray();
        }
        PrefabUtility.SaveAsPrefabAsset(right,Root+"/Prefabs/AS_Pilot_Right.prefab");
        left.transform.position = new Vector3(-.64f,0,0);
        right.transform.position = new Vector3(.64f,0,0);
        SetNeutral(left); SetNeutral(right);

        var groundMat = MakeMaterial("ReviewFloor",new Color(.065f,.09f,.12f),0,.8f);
        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name="Review floor (not cockpit)";floor.transform.localScale=Vector3.one*2;
        floor.GetComponent<Renderer>().sharedMaterial=groundMat;
        foreach(float x in new[]{-.64f,.64f})
        {
            var pad=GameObject.CreatePrimitive(PrimitiveType.Cylinder);pad.name="Standing reference pad";
            pad.transform.position=new Vector3(x,-.005f,0);pad.transform.localScale=new Vector3(.85f,.015f,.85f);
            pad.GetComponent<Renderer>().sharedMaterial=groundMat;
        }
        RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.48f,.5f,.54f);
        var sun=new GameObject("Review key light").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.8f;sun.transform.rotation=Quaternion.Euler(35,-30,0);
        var fill=new GameObject("Review fill light").AddComponent<Light>();fill.type=LightType.Directional;fill.intensity=.7f;fill.transform.rotation=Quaternion.Euler(20,145,0);
        var camera=new GameObject("Review Camera").AddComponent<Camera>();camera.tag="MainCamera";
        camera.transform.position=new Vector3(2.9f,2.0f,4.9f);camera.transform.LookAt(new Vector3(0,.95f,0));
        camera.nearClipPlane=.03f;camera.backgroundColor=new Color(.025f,.04f,.06f);camera.clearFlags=CameraClearFlags.SolidColor;
        camera.fieldOfView=30;
        EditorSceneManager.SaveScene(scene,Root+"/Scenes/PilotReview_v01.unity");
        AssetDatabase.SaveAssets();

        // Verify rest skeleton, all imported weights and posed baked-mesh finite coordinates.
        var renderers=left.GetComponentsInChildren<SkinnedMeshRenderer>();
        int triangles=0, vertices=0, unweighted=0, badSums=0, nonFinite=0;
        foreach(var r in renderers)
        {
            var m=r.sharedMesh;triangles+=m.triangles.Length/3;vertices+=m.vertexCount;
            foreach(var w in m.boneWeights)
            {
                var sum=w.weight0+w.weight1+w.weight2+w.weight3;
                if(sum<.0001f)unweighted++;
                if(Mathf.Abs(sum-1)>.0001f)badSums++;
            }
            var baked=new Mesh();r.BakeMesh(baked);
            foreach(var v in baked.vertices)if(float.IsNaN(v.x)||float.IsInfinity(v.x)||float.IsNaN(v.y)||float.IsInfinity(v.y)||float.IsNaN(v.z)||float.IsInfinity(v.z))nonFinite++;
            UnityEngine.Object.DestroyImmediate(baked);
        }
        var animator=left.GetComponent<Animator>();
        int fingerBones=Enumerable.Range((int)HumanBodyBones.LeftThumbProximal,30).Count(i=>animator.GetBoneTransform((HumanBodyBones)i));
        // Prove head hiding is local-camera specific and reversible, without changing remote head.
        int originalMask=camera.cullingMask;int originalLayer=head.gameObject.layer;
        var localView=left.GetComponent<PilotLocalView>();localView.ConfigureLocalCamera(camera,30);
        var remoteHead=right.GetComponentsInChildren<SkinnedMeshRenderer>().Single(r=>r.name=="Pilot_Head");
        bool hidden=(camera.cullingMask&(1<<head.gameObject.layer))==0;
        bool remoteVisible=(camera.cullingMask&(1<<remoteHead.gameObject.layer))!=0;
        localView.Restore();
        bool restored=camera.cullingMask==originalMask && head.gameObject.layer==originalLayer;
        var report=new Validation {
            unityVersion=Application.unityVersion,avatarValid=avatar.isValid,avatarHuman=avatar.isHuman,
            mappedHumanBones=description.human.Length,fingerBones=fingerBones,skinnedRenderers=renderers.Length,
            importedVertices=vertices,triangles=triangles,materialSlots=renderers.Sum(r=>r.sharedMaterials.Length),
            unweightedVertices=unweighted,badWeightSums=badSums,nonFinitePosedVertices=nonFinite,
            headBonePosition=animator.GetBoneTransform(HumanBodyBones.Head).position-left.transform.position,
            mountTransforms=left.GetComponentsInChildren<Transform>().Count(t=>t.name.StartsWith("Mount_")),
            localHeadExcluded=hidden,remoteHeadIncluded=remoteVisible,maskRestored=restored,
            runtimeIKConnected=false,headsetPerformanceTested=false,
            scene=Root+"/Scenes/PilotReview_v01.unity"
        };
        File.WriteAllText(Root+"/Validation/unity_validation.json",JsonUtility.ToJson(report,true));
        if(unweighted!=0||badSums!=0||nonFinite!=0||fingerBones!=30||!hidden||!remoteVisible||!restored||report.mountTransforms!=3||Mathf.Abs(report.headBonePosition.y-1.57f)>.01f)
            throw new Exception("Pilot validation metrics failed.");
        Capture(camera,Root+"/Validation/unity_two_pilots.png");
        var oldPosition=camera.transform.position;var oldRotation=camera.transform.rotation;var oldFov=camera.fieldOfView;
        camera.transform.position=animator.GetBoneTransform(HumanBodyBones.Head).position+new Vector3(0,.11f,.115f);
        camera.transform.rotation=Quaternion.Euler(84,0,0);camera.fieldOfView=85;
        localView.ConfigureLocalCamera(camera,30);
        Capture(camera,Root+"/Validation/unity_local_lookdown.png");
        localView.Restore();camera.transform.SetPositionAndRotation(oldPosition,oldRotation);camera.fieldOfView=oldFov;
        AssetDatabase.Refresh();
        AssetDatabase.ExportPackage(Root,Path.GetFullPath(Path.Combine(Application.dataPath,"../PilotV01_Export.unitypackage")),ExportPackageOptions.Recurse);
        Debug.Log("AS_PILOT_VALIDATION_PASS "+JsonUtility.ToJson(report));
    }

    static Material MakeMaterial(string name,Color color,float metal,float rough)
    {
        string path=Root+"/Materials/"+name+".mat";
        var shader=Shader.Find("Universal Render Pipeline/Simple Lit") ?? Shader.Find("Standard");
        if(!shader)throw new Exception("No supported lit shader is available.");
        var m=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!m){m=new Material(shader);AssetDatabase.CreateAsset(m,path);}else m.shader=shader;
        m.color=color;if(m.HasProperty("_BaseColor"))m.SetColor("_BaseColor",color);
        if(m.HasProperty("_Metallic"))m.SetFloat("_Metallic",metal);
        if(m.HasProperty("_Smoothness"))m.SetFloat("_Smoothness",1-rough);
        if(m.HasProperty("_Glossiness"))m.SetFloat("_Glossiness",1-rough);
        EditorUtility.SetDirty(m);return m;
    }
    static void SetNeutral(GameObject o)
    {
        var a=o.GetComponent<Animator>();
        Aim(a.GetBoneTransform(HumanBodyBones.LeftUpperArm),a.GetBoneTransform(HumanBodyBones.LeftLowerArm),new Vector3(-.36f,-.93f,0));
        Aim(a.GetBoneTransform(HumanBodyBones.RightUpperArm),a.GetBoneTransform(HumanBodyBones.RightLowerArm),new Vector3(.36f,-.93f,0));
    }
    static void Aim(Transform bone,Transform child,Vector3 desired)
    {bone.rotation=Quaternion.FromToRotation((child.position-bone.position).normalized,desired.normalized)*bone.rotation;}
    static void AddSocket(Animator animator,HumanBodyBones bone,string name,Vector3 bindPosition)
    {
        var socket=new GameObject(name).transform;
        socket.SetParent(animator.GetBoneTransform(bone),false);
        socket.position=animator.transform.TransformPoint(bindPosition);
        socket.rotation=animator.transform.rotation;
    }
    static void Capture(Camera camera,string path)
    {
        var rt=new RenderTexture(1400,1000,24);rt.Create();camera.targetTexture=rt;
        var old=RenderTexture.active;
        if(GraphicsSettings.currentRenderPipeline)
        {
            camera.GetUniversalAdditionalCameraData();
            var request=new UniversalRenderPipeline.SingleCameraRequest { destination=rt };
            RenderPipeline.SubmitRenderRequest(camera,request);
        }
        else camera.Render();
        RenderTexture.active=rt;
        var tex=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);tex.Apply();
        File.WriteAllBytes(path,tex.EncodeToPNG());RenderTexture.active=old;camera.targetTexture=null;
        UnityEngine.Object.DestroyImmediate(tex);rt.Release();UnityEngine.Object.DestroyImmediate(rt);
    }
    [Serializable] class Validation
    {
        public string unityVersion,scene;
        public Vector3 headBonePosition;
        public bool avatarValid,avatarHuman,localHeadExcluded,remoteHeadIncluded,maskRestored,runtimeIKConnected,headsetPerformanceTested;
        public int mappedHumanBones,fingerBones,skinnedRenderers,importedVertices,triangles,materialSlots,unweightedVertices,badWeightSums,nonFinitePosedVertices,mountTransforms;
    }
}
