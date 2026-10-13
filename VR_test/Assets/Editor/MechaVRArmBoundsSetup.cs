using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class MechaVRArmBoundsSetup
{
    const string Request="Library/ArmBoundsFix.request";
    static MechaVRArmBoundsSetup(){EditorApplication.update+=Tick;}
    static void Tick()
    {
        if(!File.Exists(Request)||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)return;
        File.Delete(Request);Configure();
    }
    static void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    // Skinned renderer localBounds use the root bone's frame, not the source mesh vertex frame.
    public static void FitRenderer(SkinnedMeshRenderer renderer,TwoBoneIKConstraint[] arms)
    {
        Transform frame=renderer.rootBone?renderer.rootBone:renderer.transform;
        Bounds local=default; bool started=false;
        foreach(var arm in arms)
        {
            var d=arm.data;
            if(!renderer.bones.Any(b=>b && (b==d.root||b.IsChildOf(d.root))))continue;
            float reach=Vector3.Distance(d.root.position,d.mid.position)+Vector3.Distance(d.mid.position,d.tip.position);
            float handRadius=.15f;
            foreach(var finger in d.tip.GetComponentsInChildren<Transform>(true))
            {
                if(finger.name=="RightSwordGrip"||finger.name.StartsWith("EquippedSword"))continue;
                handRadius=Mathf.Max(handRadius,Vector3.Distance(finger.position,d.tip.position));
            }
            float radius=reach+handRadius+.15f;
            for(int i=0;i<8;i++)
            {
                Vector3 corner=d.root.position+new Vector3((i&1)==0?-radius:radius,(i&2)==0?-radius:radius,(i&4)==0?-radius:radius);
                Vector3 p=frame.InverseTransformPoint(corner);
                if(!started){local=new Bounds(p,Vector3.zero);started=true;}else local.Encapsulate(p);
            }
        }
        Check(started,"No arm chain matched "+renderer.name);
        // Set the conservative initial bound after enabling automatic updates.
        // Recalculate actual skinned bounds every frame, including after the renderer leaves the view.
        renderer.updateWhenOffscreen=true; renderer.localBounds=local;
    }
    [MenuItem("Tools/Mecha VR/Fix Arm Visibility Bounds")]
    public static void Configure()
    {
        var report=new StringBuilder();
        try
        {
            var scene=SceneManager.GetSceneByPath("Assets/Scenes/MainVRScene_BodyTracking.unity");
            Check(scene.IsValid()&&scene.isLoaded,"Open the body tracking scene first.");
            var body=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MechaVRBodyFollower>(true)).Single();
            var renderers=body.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var arms=body.GetComponentsInChildren<TwoBoneIKConstraint>(true);
            var oldBounds=renderers.ToDictionary(r=>r.name,r=>r.bounds);
            string backup="ModelBackups/ArmVisibility_"+DateTime.Now.ToString("yyyyMMdd_HHmmss");Directory.CreateDirectory(backup);
            File.Copy(scene.path,backup+"/SceneOnDisk.unity");File.Copy(scene.path+".meta",backup+"/SceneOnDisk.unity.meta");
            Check(EditorSceneManager.SaveScene(scene,backup+"/SceneWithCurrentEdits.unity",true),"Could not back up editor scene.");
            foreach(var r in renderers)
            {
                Undo.RecordObject(r,"Fix moving arm visibility");FitRenderer(r,arms);
                PrefabUtility.RecordPrefabInstancePropertyModifications(r);EditorUtility.SetDirty(r);
                report.AppendLine(r.name+" rootBone="+r.rootBone.name+" updateWhenOffscreen="+r.updateWhenOffscreen+" localBounds="+r.localBounds.ToString("F3"));
            }
            foreach(var animator in body.GetComponentsInChildren<Animator>(true))
            {
                Undo.RecordObject(animator,"Keep VR arm animation updating");animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                PrefabUtility.RecordPrefabInstancePropertyModifications(animator);
            }
            Validate(body.gameObject,oldBounds,report);
            EditorSceneManager.MarkSceneDirty(scene);Check(EditorSceneManager.SaveScene(scene),"Scene save failed.");
            File.WriteAllText("Library/ArmBoundsFix.txt","PASS\nBackup="+backup+"\n"+report);
            Debug.Log("[MechaVR Arm Bounds] PASS\n"+report);
        }
        catch(Exception e){File.WriteAllText("Library/ArmBoundsFix.txt","FAIL\n"+report+e);Debug.LogException(e);}
    }
    static void Validate(GameObject body,Dictionary<string,Bounds> oldBounds,StringBuilder report)
    {
        var preview=EditorSceneManager.NewPreviewScene(); var renderTarget=new RenderTexture(64,64,24);
        try
        {
            var clone=UnityEngine.Object.Instantiate(body);SceneManager.MoveGameObjectToScene(clone,preview);
            foreach(var b in clone.GetComponentsInChildren<Behaviour>())b.enabled=false;
            var arms=clone.GetComponentsInChildren<TwoBoneIKConstraint>();
            var originals=arms.Select(a=>new[]{a.data.root.localRotation,a.data.mid.localRotation}).ToArray();
            var rendererList=clone.GetComponentsInChildren<SkinnedMeshRenderer>();
            var head=body.GetComponent<MechaVRBodyFollower>().head;
            var cameraObject=new GameObject("VisibilityTestCamera");SceneManager.MoveGameObjectToScene(cameraObject,preview);
            var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;
            if(head&&head.GetComponent<Camera>())camera.CopyFrom(head.GetComponent<Camera>());
            camera.scene=preview; camera.targetTexture=renderTarget; camera.enabled=false;camera.transform.position=head?head.position:body.transform.position+Vector3.up*.83f;
            int outside=0,oldFalseCulls=0,newFalseCulls=0,visibleSamples=0;
            for(int pose=0;pose<12;pose++)
            {
                for(int i=0;i<arms.Length;i++)
                {
                    var d=arms[i].data;
                    d.root.localRotation=originals[i][0]*Quaternion.Euler((pose%3-1)*50,(pose/3%2)*90,(pose/6)*60);
                    d.mid.localRotation=originals[i][1]*Quaternion.Euler(0,0,(pose%3)*45);
                }
                camera.Render(); // Run Unity skinning/bounds update, as a real frame does.
                foreach(var r in rendererList)
                {
                    var mesh=new Mesh();r.BakeMesh(mesh);var vertices=mesh.vertices.Select(v=>r.transform.TransformPoint(v)).ToArray();
                    var actual=new Bounds(vertices[0],Vector3.zero);foreach(var v in vertices)actual.Encapsulate(v);
                    Bounds declared=r.bounds; Bounds tolerance=declared; tolerance.Expand(.002f); outside+=vertices.Count(v=>!tolerance.Contains(v));
                    for(int pitch=-20;pitch<=85;pitch+=5)for(int yaw=-90;yaw<=90;yaw+=30)
                    {
                        camera.transform.rotation=Quaternion.Euler(-pitch,body.transform.eulerAngles.y+yaw,0);
                        var planes=GeometryUtility.CalculateFrustumPlanes(camera);
                        if(!GeometryUtility.TestPlanesAABB(planes,actual))continue;
                        visibleSamples++;
                        if(!GeometryUtility.TestPlanesAABB(planes,oldBounds[r.name]))oldFalseCulls++;
                        if(!GeometryUtility.TestPlanesAABB(planes,declared))newFalseCulls++;
                    }
                    UnityEngine.Object.DestroyImmediate(mesh);
                }
            }
            Check(visibleSamples>0,"No visible arm samples tested.");
            Check(outside==0,"Arm vertices outside corrected bounds: "+outside);
            Check(newFalseCulls==0,"Incorrect culling remains: "+newFalseCulls);
            Check(rendererList.All(r=>r.updateWhenOffscreen),"Off-screen updates disabled.");
            report.AppendLine("12 arm poses; head pitch -20..85 deg; yaw -90..90 deg; visible samples="+visibleSamples);
            report.AppendLine("Incorrect visibility rejection before="+oldFalseCulls+" after="+newFalseCulls+"; vertices outside corrected bounds="+outside);
        }
        finally{renderTarget.Release();UnityEngine.Object.DestroyImmediate(renderTarget);EditorSceneManager.ClosePreviewScene(preview);}
    }
}