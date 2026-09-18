using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using AnseongSteel.PilotV04;

public static class PilotV04Builder
{
    public const string Root="Assets/_Project/02_Art/PilotV04";
    public const string ScenePath=Root+"/Scenes/PilotWristReview_v04.unity";
    [MenuItem("Anseong Steel/Pilot V04/Build model and rig review")]
    public static void Build()
    {
        foreach(var d in new[]{"Materials","Prefabs","Scenes","Validation"})Directory.CreateDirectory(Root+"/"+d);
        AssetDatabase.Refresh();
        var materials=new Dictionary<string,Material>{
            {"AS_CeramicWhite",MakeMaterial("AS_CeramicWhite",new Color(.89f,.915f,.91f),.05f,.45f)},
            {"AS_FlexGraphite",MakeMaterial("AS_FlexGraphite",new Color(.09f,.11f,.13f),.02f,.36f)},
            {"AS_AmberVisor",MakeMaterial("AS_AmberVisor",new Color(.96f,.64f,.22f),.72f,.78f)},
            {"AS_TeamAccent",MakeMaterial("AS_TeamAccent",new Color(.15f,.56f,.60f),.22f,.55f)}
        };
        string master=Root+"/AS_Pilot_v04.fbx", candidate=Root+"/AS_Pilot_v04_QuestCandidate.fbx";
        int masterMapped=PrepareModel(master,materials),candidateMapped=PrepareModel(candidate,materials);
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var a=CreatePilot(master,"Pilot_Detailed",new Vector3(-.65f,0,0));
        var b=CreatePilot(candidate,"Pilot_Reduced",new Vector3(.65f,0,0));
        var floorMat=MakeMaterial("ReviewFloor",new Color(.045f,.065f,.085f),0,.2f);
        var floor=GameObject.CreatePrimitive(PrimitiveType.Plane);floor.name="Review floor";floor.transform.localScale=Vector3.one*20;floor.GetComponent<Renderer>().sharedMaterial=floorMat;
        RenderSettings.ambientMode=AmbientMode.Trilight;RenderSettings.ambientSkyColor=new Color(.58f,.65f,.74f);RenderSettings.ambientEquatorColor=new Color(.28f,.31f,.35f);RenderSettings.ambientGroundColor=new Color(.12f,.13f,.14f);
        Light key=new GameObject("Key light").AddComponent<Light>();key.type=LightType.Directional;key.intensity=2.0f;key.transform.rotation=Quaternion.Euler(40,-32,0);key.shadows=LightShadows.Soft;
        Light fill=new GameObject("Fill light").AddComponent<Light>();fill.type=LightType.Directional;fill.intensity=.65f;fill.transform.rotation=Quaternion.Euler(20,150,0);
        var cam=new GameObject("Review Camera").AddComponent<Camera>();cam.tag="MainCamera";cam.nearClipPlane=.025f;cam.fieldOfView=32;cam.backgroundColor=new Color(.025f,.04f,.055f);cam.clearFlags=CameraClearFlags.SolidColor;
        cam.transform.position=new Vector3(2.1f,1.8f,4.6f);cam.transform.LookAt(new Vector3(0,.97f,0));
        cam.GetUniversalAdditionalCameraData().renderPostProcessing=false;
        var controls=new GameObject("PC review controls").AddComponent<PilotReviewControls>();controls.rig=a;controls.reviewCamera=cam;
        var probe=new GameObject("Studio reflection").AddComponent<ReflectionProbe>();probe.mode=ReflectionProbeMode.Realtime;probe.refreshMode=ReflectionProbeRefreshMode.ViaScripting;probe.size=Vector3.one*8;probe.resolution=128;probe.transform.position=new Vector3(0,1,0);probe.clearFlags=ReflectionProbeClearFlags.SolidColor;probe.backgroundColor=new Color(.42f,.46f,.52f);probe.cullingMask=0;probe.RenderProbe();
        var report=new Report { unityVersion=Application.unityVersion,branch="feat/pilot-v03-rig-review",scene=ScenePath,master=Validate(a,masterMapped),candidate=Validate(b,candidateMapped) };
        a.SetPose(0);b.SetPose(0);
        a.transform.parent.position=Vector3.zero;
        PrefabUtility.SaveAsPrefabAsset(a.transform.parent.gameObject,Root+"/Prefabs/AS_Pilot_Detailed_Review.prefab");
        a.transform.parent.position=new Vector3(-.65f,0,0);
        b.transform.parent.position=Vector3.zero;
        PrefabUtility.SaveAsPrefabAsset(b.transform.parent.gameObject,Root+"/Prefabs/AS_Pilot_Reduced_Review.prefab");
        b.transform.parent.position=new Vector3(.65f,0,0);
        EditorSceneManager.SaveScene(scene,ScenePath);
        AssetDatabase.SaveAssets();
        Capture(cam,Root+"/Validation/01_two_models.png");
        Vector3 cp=cam.transform.position;Quaternion cr=cam.transform.rotation;
        b.transform.parent.gameObject.SetActive(false);
        cam.transform.position=a.transform.position+new Vector3(1.7f,1.6f,3.6f);cam.transform.LookAt(a.transform.position+new Vector3(0,1.0f,0));
        string[] poses={"neutral","guard","grip","raised","wrist","reach_limit"};
        for(int i=0;i<poses.Length;i++){a.SetPose(i);Capture(cam,Root+"/Validation/pose_"+poses[i]+".png");}
        a.SetPose(2);cam.transform.position=a.transform.position+new Vector3(.55f,1.4f,1.25f);cam.transform.LookAt(a.left.hand.position);cam.fieldOfView=40;
        Capture(cam,Root+"/Validation/hand_grip.png");
        foreach(int roll in new[]{-60,0,60})
        {
            a.SetWristReview(roll,0);Capture(cam,Root+"/Validation/hand_open_roll_"+roll+".png");
        }
        a.SetWristReview(0,.85f);Capture(cam,Root+"/Validation/hand_closed_aligned.png");
        a.SetPose(0);b.transform.parent.gameObject.SetActive(true);cam.transform.SetPositionAndRotation(cp,cr);cam.fieldOfView=32;
        var local=a.GetComponent<PilotLocalView>();
        int mask=cam.cullingMask,layer=local.Head.gameObject.layer;
        local.ConfigureLocalCamera(cam,30);
        report.localHeadHidden=(cam.cullingMask&(1<<local.Head.gameObject.layer))==0;
        report.comparisonHeadVisible=(cam.cullingMask&(1<<b.GetComponent<PilotLocalView>().Head.gameObject.layer))!=0;
        local.Restore();report.localMaskRestored=cam.cullingMask==mask&&local.Head.gameObject.layer==layer;
        report.pass=report.master.pass&&report.candidate.pass&&report.localHeadHidden&&report.comparisonHeadVisible&&report.localMaskRestored;
        File.WriteAllText(Root+"/Validation/unity_validation.json",JsonUtility.ToJson(report,true));
        if(!report.pass)throw new Exception("Pilot V04 review validation failed. See unity_validation.json");
        EditorSceneManager.SaveScene(scene,ScenePath);AssetDatabase.SaveAssets();AssetDatabase.Refresh();
        AssetDatabase.ExportPackage(Root,Path.GetFullPath("PilotV04_Review.unitypackage"),ExportPackageOptions.Recurse);
        Debug.Log("PILOT_V04_REVIEW_PASS "+JsonUtility.ToJson(report));
    }
    static int PrepareModel(string path,Dictionary<string,Material> materials)
    {
        var importer=(ModelImporter)AssetImporter.GetAtPath(path);
        importer.animationType=ModelImporterAnimationType.Generic;importer.importAnimation=false;importer.isReadable=true;importer.optimizeGameObjects=false;
        importer.meshCompression=ModelImporterMeshCompression.Off;importer.importNormals=ModelImporterNormals.Import;importer.importTangents=ModelImporterTangents.CalculateMikk;
        importer.skinWeights=ModelImporterSkinWeights.Custom;importer.maxBonesPerVertex=4;importer.minBoneWeight=.00001f;
        importer.materialImportMode=ModelImporterMaterialImportMode.ImportStandard;importer.SaveAndReimport();
        var model=AssetDatabase.LoadAssetAtPath<GameObject>(path);var ts=model.GetComponentsInChildren<Transform>(true);var names=new HashSet<string>(ts.Select(t=>t.name));
        var d=new HumanDescription { human=HumanTrait.BoneName.Where(n=>names.Contains(n.Replace(" ",""))).Select(n=>new HumanBone { humanName=n,boneName=n.Replace(" ",""),limit=new HumanLimit{useDefaultValues=true}}).ToArray(),
            skeleton=ts.Select(t=>new SkeletonBone{name=t.name,position=t.localPosition,rotation=t.localRotation,scale=t.localScale}).ToArray(),
            upperArmTwist=.5f,lowerArmTwist=.5f,upperLegTwist=.5f,lowerLegTwist=.5f,armStretch=0,legStretch=0,feetSpacing=0,hasTranslationDoF=false };
        importer.animationType=ModelImporterAnimationType.Human;importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;importer.humanDescription=d;
        foreach(var m in materials)importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material),m.Key),m.Value);
        importer.SaveAndReimport();
        var avatar=AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
        if(!avatar||!avatar.isValid||!avatar.isHuman)throw new Exception("Invalid Humanoid: "+path);
        return d.human.Length;
    }
    static PilotReviewRig CreatePilot(string path,string name,Vector3 position)
    {
        var wrapper=new GameObject(name);wrapper.transform.position=position;
        var model=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path),wrapper.transform);
        var rig=model.AddComponent<PilotReviewRig>();
        var controls=new GameObject("Pose targets").transform;controls.SetParent(wrapper.transform,false);
        Func<string,Transform> target=n=>{var t=new GameObject(n).transform;t.SetParent(controls,false);return t;};
        rig.Configure(model.GetComponent<Animator>(),target("Left hand"),target("Right hand"),target("Left elbow"),target("Right elbow"),target("Head orientation"));
        foreach(var arm in new[]{rig.left,rig.right})
        {
            var gripPoint=new GameObject(arm==rig.left?"GripReference_Left":"GripReference_Right").transform;
            gripPoint.SetParent(arm.hand,false);
            Quaternion frame=rig.HandFrame(arm);
            gripPoint.SetPositionAndRotation(arm.hand.position+frame*new Vector3(0,-.02f,.045f),frame);
        }
        foreach(var r in model.GetComponentsInChildren<SkinnedMeshRenderer>())
        {r.quality=SkinQuality.Bone4;r.localBounds=new Bounds(new Vector3(0,.9f,0),Vector3.one*3);r.updateWhenOffscreen=true;}
        var head=model.GetComponentsInChildren<SkinnedMeshRenderer>().Single(r=>r.name.StartsWith("Pilot_Head"));
        model.AddComponent<PilotLocalView>().SetHead(head);
        rig.SetPose(0);return rig;
    }
    static ModelReport Validate(PilotReviewRig rig,int mapped)
    {
        var animator=rig.GetComponent<Animator>();var rs=rig.GetComponentsInChildren<SkinnedMeshRenderer>();
        var report=new ModelReport {name=rig.transform.parent.name,avatarValid=animator.avatar.isValid&&animator.avatar.isHuman,mappedBones=mapped,renderers=rs.Length,
            triangles=rs.Sum(r=>r.sharedMesh.triangles.Length/3),fingerBones=Enumerable.Range((int)HumanBodyBones.LeftThumbProximal,30).Count(i=>animator.GetBoneTransform((HumanBodyBones)i)),
            helperBones=rig.GetComponentsInChildren<Transform>().Count(t=>t.name.EndsWith("Twist")||t.name.EndsWith("ShoulderArmor"))};
        foreach(var r in rs) foreach(var w in r.sharedMesh.boneWeights)
        {
            float sum=w.weight0+w.weight1+w.weight2+w.weight3;
            if(sum<.0001f)report.unweighted++;if(Mathf.Abs(sum-1)>.0001f)report.badWeightSums++;
            report.maxWeights=Mathf.Max(report.maxWeights,new[]{w.weight0,w.weight1,w.weight2,w.weight3}.Count(v=>v>0));
        }
        var lf=animator.GetBoneTransform(HumanBodyBones.LeftFoot);var rf=animator.GetBoneTransform(HumanBodyBones.RightFoot);Vector3 fl=lf.position,fr=rf.position;
        report.poses=new PoseReport[6];
        for(int i=0;i<6;i++)
        {
            rig.SetPose(i);var p=new PoseReport{index=i,leftError=rig.left.targetError,rightError=rig.right.targetError,leftClamped=rig.left.clampedDistance,rightClamped=rig.right.clampedDistance};
            report.maxFootDrift=Mathf.Max(report.maxFootDrift,Vector3.Distance(fl,lf.position),Vector3.Distance(fr,rf.position));
            foreach(var r in rs)
            {
                var mesh=new Mesh();r.BakeMesh(mesh);
                foreach(var v in mesh.vertices)if(!float.IsFinite(v.x)||!float.IsFinite(v.y)||!float.IsFinite(v.z))report.nonFiniteVertices++;
                UnityEngine.Object.DestroyImmediate(mesh);
            }
            p.pass=Mathf.Abs(p.leftError-p.leftClamped)<.001f&&Mathf.Abs(p.rightError-p.rightClamped)<.001f;
            report.poses[i]=p;
        }
        rig.SetPose(2);Quaternion before=rig.left.upper.rotation;
        for(int i=0;i<100;i++)rig.Evaluate();report.repeatDriftDegrees=Quaternion.Angle(before,rig.left.upper.rotation);
        report.wrists=new[]{ValidateWrist(rig,rig.left),ValidateWrist(rig,rig.right)};
        rig.SetPose(0);var head=animator.GetBoneTransform(HumanBodyBones.Head);Quaternion h=head.rotation;
        rig.headTarget.rotation=Quaternion.AngleAxis(35,rig.transform.up)*rig.headTarget.rotation;rig.Evaluate();report.headResponseDegrees=Quaternion.Angle(h,head.rotation);
        rig.SetPose(2);rig.leftGrip=rig.rightGrip=0;rig.Evaluate();
        var li=animator.GetBoneTransform(HumanBodyBones.LeftIndexDistal);var ri=animator.GetBoneTransform(HumanBodyBones.RightIndexDistal);Vector3 l0=li.position,r0=ri.position;
        rig.leftGrip=1;rig.Evaluate();report.leftFingerTravel=Vector3.Distance(l0,li.position);report.rightFingerDrift=Vector3.Distance(r0,ri.position);
        rig.SetPose(0);
        report.pass=report.avatarValid&&mapped==52&&report.fingerBones==30&&report.helperBones==6&&report.renderers==2&&report.unweighted==0&&report.badWeightSums==0&&report.nonFiniteVertices==0
            &&report.poses.All(p=>p.pass)&&report.wrists.All(w=>w.pass)&&report.maxFootDrift<.0001f&&report.repeatDriftDegrees<.05f&&Mathf.Abs(report.headResponseDegrees-35)<.2f&&report.leftFingerTravel>.02f&&report.rightFingerDrift<.0001f;
        return report;
    }
    static WristReport ValidateWrist(PilotReviewRig rig,PilotReviewRig.Arm arm)
    {
        rig.SetPose(2);Quaternion neutral=arm.neutralFrame;arm.target.rotation=neutral;arm.rollInitialized=false;rig.Evaluate();
        Quaternion shaft=arm.lower.rotation,distal=arm.forearmTwist.rotation,hand=arm.hand.rotation;
        arm.target.rotation=neutral*Quaternion.AngleAxis(72,Vector3.forward);rig.Evaluate();
        var w=new WristReport { side=arm==rig.left?"Left":"Right",shaftRoll=Quaternion.Angle(shaft,arm.lower.rotation),distalRoll=Quaternion.Angle(distal,arm.forearmTwist.rotation),handRoll=Quaternion.Angle(hand,arm.hand.rotation),straightWristError=Vector3.Angle(rig.HandFrame(arm)*Vector3.forward,arm.hand.position-arm.lower.position),targetError=arm.targetError };
        arm.target.rotation=neutral*Quaternion.Euler(80,60,0);rig.Evaluate();
        w.limitedFlex=arm.wristFlex;w.limitedDeviation=arm.wristDeviation;w.rejectedOrientation=arm.orientationError;
        arm.rollInitialized=false;arm.target.rotation=neutral*Quaternion.AngleAxis(170,Vector3.forward);rig.Evaluate();Quaternion prior=arm.hand.rotation;
        for(int roll=171;roll<=190;roll++)
        {arm.target.rotation=neutral*Quaternion.AngleAxis(roll,Vector3.forward);rig.Evaluate();w.wrapMaxStep=Mathf.Max(w.wrapMaxStep,Quaternion.Angle(prior,arm.hand.rotation));prior=arm.hand.rotation;}
        w.pass=Mathf.Abs(w.shaftRoll-25.2f)<.2f&&Mathf.Abs(w.distalRoll-64.8f)<.2f&&Mathf.Abs(w.handRoll-72)<.2f&&w.straightWristError<.1f&&w.targetError<.001f&&Mathf.Abs(w.limitedFlex)<=rig.maxWristFlex+.01f&&Mathf.Abs(w.limitedDeviation)<=rig.maxWristDeviation+.01f&&w.rejectedOrientation>10&&w.wrapMaxStep<.2f;
        rig.SetPose(0);return w;
    }
    static Material MakeMaterial(string name,Color color,float metallic,float smoothness)
    {
        string path=Root+"/Materials/"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);
        var shader=Shader.Find("Universal Render Pipeline/Lit");if(!shader)throw new Exception("URP Lit missing");
        if(!m){m=new Material(shader);AssetDatabase.CreateAsset(m,path);}else m.shader=shader;
        m.SetColor("_BaseColor",color);m.SetFloat("_Metallic",metallic);m.SetFloat("_Smoothness",smoothness);EditorUtility.SetDirty(m);return m;
    }
    public static void Capture(Camera camera,string path,bool bakeCurrentPose=true)
    {
        // Batch captures happen in one editor frame; snapshot the current CPU skin pose
        // so the graphics skinning cache cannot reuse the preceding pose.
        var source=UnityEngine.Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None).Where(r=>r.enabled).ToArray();
        var temporary=new List<GameObject>();var meshes=new List<Mesh>();
        var rt=new RenderTexture(1800,1400,24){antiAliasing=4};rt.Create();var old=RenderTexture.active;
        Texture2D tex=null;
        try
        {
            if(bakeCurrentPose)foreach(var r in source)
            {
                var mesh=Snapshot(r);meshes.Add(mesh);
                var o=new GameObject("Temporary pose snapshot");temporary.Add(o);o.layer=r.gameObject.layer;
                o.transform.SetParent(r.transform,false);o.AddComponent<MeshFilter>().sharedMesh=mesh;
                o.AddComponent<MeshRenderer>().sharedMaterials=r.sharedMaterials;r.enabled=false;
            }
            var request=new UniversalRenderPipeline.SingleCameraRequest{destination=rt};RenderPipeline.SubmitRenderRequest(camera,request);
            RenderTexture.active=rt;tex=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);tex.Apply();File.WriteAllBytes(path,tex.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active=old;if(tex)UnityEngine.Object.DestroyImmediate(tex);rt.Release();UnityEngine.Object.DestroyImmediate(rt);
            foreach(var o in temporary)UnityEngine.Object.DestroyImmediate(o);foreach(var m in meshes)UnityEngine.Object.DestroyImmediate(m);
            if(bakeCurrentPose)foreach(var r in source)r.enabled=true;
        }
    }
    static Mesh Snapshot(SkinnedMeshRenderer r)
    {
        var source=r.sharedMesh;var mesh=UnityEngine.Object.Instantiate(source);
        var vertices=source.vertices;var normals=source.normals;var tangents=source.tangents;var weights=source.boneWeights;var binds=source.bindposes;
        var matrices=new Matrix4x4[binds.Length];
        for(int i=0;i<matrices.Length;i++)matrices[i]=r.transform.worldToLocalMatrix*r.bones[i].localToWorldMatrix*binds[i];
        for(int i=0;i<vertices.Length;i++)
        {
            var w=weights[i];Vector3 v=vertices[i],n=normals[i];Vector4 t=tangents.Length>i?tangents[i]:Vector4.zero;
            vertices[i]=matrices[w.boneIndex0].MultiplyPoint3x4(v)*w.weight0+matrices[w.boneIndex1].MultiplyPoint3x4(v)*w.weight1+matrices[w.boneIndex2].MultiplyPoint3x4(v)*w.weight2+matrices[w.boneIndex3].MultiplyPoint3x4(v)*w.weight3;
            normals[i]=(matrices[w.boneIndex0].MultiplyVector(n)*w.weight0+matrices[w.boneIndex1].MultiplyVector(n)*w.weight1+matrices[w.boneIndex2].MultiplyVector(n)*w.weight2+matrices[w.boneIndex3].MultiplyVector(n)*w.weight3).normalized;
            if(tangents.Length>i)
            {
                Vector3 st=(matrices[w.boneIndex0].MultiplyVector(t)*w.weight0+matrices[w.boneIndex1].MultiplyVector(t)*w.weight1+matrices[w.boneIndex2].MultiplyVector(t)*w.weight2+matrices[w.boneIndex3].MultiplyVector(t)*w.weight3).normalized;
                tangents[i]=new Vector4(st.x,st.y,st.z,t.w);
            }
        }
        mesh.vertices=vertices;mesh.normals=normals;if(tangents.Length>0)mesh.tangents=tangents;mesh.RecalculateBounds();return mesh;
    }
    [Serializable] public class PoseReport { public int index; public float leftError,rightError,leftClamped,rightClamped;public bool pass; }
    [Serializable] public class WristReport {public string side;public bool pass;public float shaftRoll,distalRoll,handRoll,straightWristError,targetError,limitedFlex,limitedDeviation,rejectedOrientation,wrapMaxStep;}
    [Serializable] public class ModelReport
    {
        public string name;public bool avatarValid,pass;public int mappedBones,fingerBones,helperBones,renderers,triangles,maxWeights,unweighted,badWeightSums,nonFiniteVertices;
        public float maxFootDrift,repeatDriftDegrees,headResponseDegrees,leftFingerTravel,rightFingerDrift;public PoseReport[] poses;public WristReport[] wrists;
    }
    [Serializable] public class Report
    {
        public string unityVersion,branch,scene;public ModelReport master,candidate;
        public bool localHeadHidden,comparisonHeadVisible,localMaskRestored,pass;
        public bool headsetTested=false,networkIncluded=false,animationClipsCreated=false;
    }
}
