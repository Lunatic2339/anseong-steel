using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class MechaVRArmLengthSetup
{
    const string Request="Library/ArmLength20.request";
    const string Folder="Assets/Models/ExtendedArms20";
    const string Report="Library/ArmLength20.txt";
    static MechaVRArmLengthSetup(){EditorApplication.update+=Tick;}
    static void Tick()
    {
        if(!File.Exists(Request)||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)return;
        File.Delete(Request); Extend();
    }
    static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    static Matrix4x4 Stretch(Vector3 axis)
    {
        axis.Normalize(); var result=Matrix4x4.identity;
        for(int row=0;row<3;row++)for(int col=0;col<3;col++)result[row,col]+=.2f*axis[row]*axis[col];
        return result;
    }
    [MenuItem("Tools/Mecha VR/Extend Both Arms 20 Percent")]
    public static void Extend()
    {
        var report=new StringBuilder();
        try
        {
            Check(!EditorApplication.isPlayingOrWillChangePlaymode,"Stop Play mode before resizing arms.");
            var scene=SceneManager.GetSceneByPath("Assets/Scenes/MainVRScene_BodyTracking.unity");
            Check(scene.IsValid()&&scene.isLoaded,"Open MainVRScene_BodyTracking first.");
            var body=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MechaVRBodyFollower>(true)).Single();
            var arms=body.GetComponentsInChildren<TwoBoneIKConstraint>(true).Where(c=>c.data.tip&&(c.data.tip.name=="Hand.L"||c.data.tip.name=="Hand.R")).ToArray();
            Check(arms.Length==2,"Expected two arm chains.");
            var renderers=body.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Check(renderers.Length>0,"No skinned arm meshes.");
            Check(!renderers.Any(r=>AssetDatabase.GetAssetPath(r.sharedMesh).StartsWith(Folder)),"Arms already extended; refusing to apply twice.");
            Check(renderers.All(r=>r.sharedMesh.blendShapeCount==0),"Unexpected blend shapes.");
            foreach(var renderer in renderers)
            {
                using(var influences=renderer.sharedMesh.GetBonesPerVertex())
                    Check(influences.All(n=>n<=4),"More than four weights per vertex.");
            }
            string backup="ModelBackups/ArmLength20_"+DateTime.Now.ToString("yyyyMMdd_HHmmss"); Directory.CreateDirectory(backup);
            File.Copy(scene.path,backup+"/SceneOnDisk.unity"); File.Copy(scene.path+".meta",backup+"/SceneOnDisk.unity.meta");
            Check(EditorSceneManager.SaveScene(scene,backup+"/SceneWithCurrentEdits.unity",true),"Could not back up current editor scene.");
            Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
            Undo.IncrementCurrentGroup(); int group=Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Extend robot arms 20 percent");
            Undo.RegisterFullObjectHierarchyUndo(body.gameObject,"Extend robot arms 20 percent");
            var stretches=new Dictionary<Transform,Matrix4x4>();
            var lengths=new Dictionary<TwoBoneIKConstraint,Vector2>();
            var hands=new Dictionary<TwoBoneIKConstraint,Vector3>();
            var elbows=new Dictionary<TwoBoneIKConstraint,Vector3>();
            var fingerOffsets=arms.SelectMany(a=>a.data.tip.GetComponentsInChildren<Transform>(true)).ToDictionary(t=>t,t=>t.localPosition);
            var bakedBefore=new Dictionary<SkinnedMeshRenderer,Vector3[]>();
            foreach(var r in renderers){var baked=new Mesh();r.BakeMesh(baked);bakedBefore[r]=baked.vertices;UnityEngine.Object.DestroyImmediate(baked);}
            foreach(var arm in arms)
            {
                var d=arm.data; Check(d.mid.parent==d.root&&d.tip.parent==d.mid,"Unexpected arm hierarchy.");
                lengths[arm]=new Vector2(Vector3.Distance(d.root.position,d.mid.position),Vector3.Distance(d.mid.position,d.tip.position));
                hands[arm]=d.tip.position; elbows[arm]=d.mid.position;
                stretches[d.root]=Stretch(d.mid.localPosition); stretches[d.mid]=Stretch(d.tip.localPosition);
            }
            Directory.CreateDirectory("MechaVR_Validation");
            var framing=renderers[0].bounds;foreach(var r in renderers)framing.Encapsulate(r.bounds);
            Render(body.gameObject,framing,"MechaVR_Validation/Arms20_Before.png");
            foreach(var arm in arms)
            {
                var d=arm.data; d.mid.localPosition*=1.2f; d.tip.localPosition*=1.2f;
                PrefabUtility.RecordPrefabInstancePropertyModifications(d.mid); PrefabUtility.RecordPrefabInstancePropertyModifications(d.tip);
                if(d.target)d.target.position+=d.tip.position-hands[arm];
                if(d.hint)d.hint.position+=d.mid.position-elbows[arm];
                if(d.target)PrefabUtility.RecordPrefabInstancePropertyModifications(d.target);
                if(d.hint)PrefabUtility.RecordPrefabInstancePropertyModifications(d.hint);
            }
            float maxHandError=0; int handVertices=0;
            foreach(var r in renderers)
            {
                var original=r.sharedMesh; var mesh=UnityEngine.Object.Instantiate(original); mesh.name=original.name+"_Arms20";
                var bones=r.bones; var bind=original.bindposes; var transforms=new Matrix4x4[bones.Length]; var newBind=new Matrix4x4[bones.Length];
                for(int j=0;j<bones.Length;j++)
                {
                    var stretch=stretches.TryGetValue(bones[j],out var value)?value:Matrix4x4.identity;
                    transforms[j]=r.transform.worldToLocalMatrix*bones[j].localToWorldMatrix*stretch*bind[j];
                    newBind[j]=bones[j].worldToLocalMatrix*r.transform.localToWorldMatrix;
                }
                var vertices=original.vertices; var normals=original.normals; var tangents=original.tangents; var weights=original.boneWeights;
                Check(vertices.Length==weights.Length,"Weight count mismatch.");
                for(int i=0;i<vertices.Length;i++)
                {
                    var w=weights[i]; var ids=new[]{w.boneIndex0,w.boneIndex1,w.boneIndex2,w.boneIndex3};var ws=new[]{w.weight0,w.weight1,w.weight2,w.weight3};
                    var deform=new Matrix4x4();
                    for(int k=0;k<4;k++)if(ws[k]>0)for(int row=0;row<4;row++)for(int col=0;col<4;col++)deform[row,col]+=transforms[ids[k]][row,col]*ws[k];
                    Vector3 old=vertices[i]; vertices[i]=deform.MultiplyPoint3x4(old);
                    if(normals.Length==vertices.Length)normals[i]=deform.inverse.transpose.MultiplyVector(normals[i]).normalized;
                    if(tangents.Length==vertices.Length){var t=tangents[i];Vector3 direction=deform.MultiplyVector(new Vector3(t.x,t.y,t.z)).normalized;tangents[i]=new Vector4(direction.x,direction.y,direction.z,t.w);}
                    foreach(var arm in arms)
                    {
                        var hand=arm.data.tip;
                        bool onlyHand=true;for(int k=0;k<4;k++)if(ws[k]>0&&!bones[ids[k]].IsChildOf(hand))onlyHand=false;
                        if(!onlyHand)continue;
                        Vector3 displacement=hand.position-hands[arm];
                        Vector3 expected=r.transform.TransformPoint(bakedBefore[r][i])+displacement;
                        maxHandError=Mathf.Max(maxHandError,Vector3.Distance(expected,r.transform.TransformPoint(vertices[i])));handVertices++;
                    }
                }
                mesh.vertices=vertices;if(normals.Length==vertices.Length)mesh.normals=normals;if(tangents.Length==vertices.Length)mesh.tangents=tangents;mesh.bindposes=newBind;mesh.RecalculateBounds();
                string path=AssetDatabase.GenerateUniqueAssetPath(Folder+"/"+r.name.Replace('/','_')+".asset");AssetDatabase.CreateAsset(mesh,path);
                r.sharedMesh=mesh;MechaVRArmBoundsSetup.FitRenderer(r,arms);PrefabUtility.RecordPrefabInstancePropertyModifications(r);EditorUtility.SetDirty(r);
            }
            Check(handVertices>0&&maxHandError<.0001f,"Hand shape changed: "+maxHandError);
            foreach(var pair in fingerOffsets)if(!arms.Any(a=>a.data.tip==pair.Key))Check(Vector3.Distance(pair.Key.localPosition,pair.Value)<.000001f,"Finger or grip moved locally.");
            var shield=body.GetComponentInChildren<MechaVRShieldDeploy>(true);
            if(shield)
            {
                var parent=shield.transform.parent;
                if(stretches.TryGetValue(parent,out var stretch))shield.transform.localPosition=stretch.MultiplyPoint3x4(shield.transform.localPosition);
                foreach(var line in shield.GetComponentsInChildren<LineRenderer>())
                {
                    for(int k=0;k<line.positionCount;k++){Vector3 v=line.GetPosition(k);v.x*=1.2f;line.SetPosition(k,v);}
                    PrefabUtility.RecordPrefabInstancePropertyModifications(line);
                }
                PrefabUtility.RecordPrefabInstancePropertyModifications(shield.transform);
                typeof(MechaVRShieldSetup).GetMethod("FitSeamLights",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{shield.gameObject,body.gameObject});
            }
            var contact=body.GetComponent<MechaVRBossContact>();
            Check(contact,"Boss contact solver missing.");
            var builder=arms[0].GetComponentInParent<RigBuilder>();
            var fitted=(Dictionary<Transform,Bounds>)typeof(MechaVRBossSetup).GetMethod("FitVolumes",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{builder.transform,contact.arms});
            foreach(var arm in contact.arms)foreach(var box in arm.volumes)
            {
                if(!box||!fitted.TryGetValue(box.transform.parent,out var bounds))continue;
                box.center=bounds.center;box.size=Vector3.Max(bounds.size,Vector3.one*.015f)+Vector3.one*.004f;
                PrefabUtility.RecordPrefabInstancePropertyModifications(box);EditorUtility.SetDirty(box);
            }
            foreach(var arm in arms)
            {
                var d=arm.data;Vector2 before=lengths[arm];
                float upper=Vector3.Distance(d.root.position,d.mid.position),lower=Vector3.Distance(d.mid.position,d.tip.position);
                Check(Mathf.Abs(upper/before.x-1.2f)<.0001f&&Mathf.Abs(lower/before.y-1.2f)<.0001f,"Bone length mismatch.");
                report.AppendLine(d.tip.name+": upper "+before.x.ToString("F5")+" -> "+upper.ToString("F5")+" m; forearm "+before.y.ToString("F5")+" -> "+lower.ToString("F5")+" m");
            }
            report.AppendLine("Hand shape error="+maxHandError+"m; checked hand vertices="+handVertices);
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);
            Check(EditorSceneManager.SaveScene(scene),"Scene save failed.");Undo.CollapseUndoOperations(group);
            MechaVRSwordSetup.ValidateInstalled();Check(File.ReadAllText("Library/MechaSwordSetup.txt").StartsWith("PASS"),"Sword validation failed.");
            MechaVRShieldPoseValidation.Validate();Check(File.ReadAllText("Library/ShieldForwardValidation.txt").StartsWith("PASS"),"Shield validation failed.");
            Render(body.gameObject,framing,"MechaVR_Validation/Arms20_After.png");
            File.WriteAllText(Report,"PASS\nBackup="+backup+"\n"+report+"Both upper/forearms 1.2x; hand shape preserved; mesh bindposes updated; colliders refit; sword/shield PASS\n");
            Debug.Log("[MechaVR Arms20] PASS\n"+report);
        }
        catch(Exception e){File.WriteAllText(Report,"FAIL\n"+report+e);Debug.LogException(e);}
    }
    static void Render(GameObject body,Bounds framing,string path)
    {
        var scene=EditorSceneManager.NewPreviewScene();var rt=new RenderTexture(1000,800,24);var previous=RenderTexture.active;
        try
        {
            var clone=UnityEngine.Object.Instantiate(body);SceneManager.MoveGameObjectToScene(clone,scene);
            var cameraObject=new GameObject("ArmsPreviewCamera");SceneManager.MoveGameObjectToScene(cameraObject,scene);
            var camera=cameraObject.AddComponent<Camera>();camera.scene=scene;camera.orthographic=true;
            camera.orthographicSize=Mathf.Max(framing.extents.y,framing.extents.x/1.25f)*1.35f;
            camera.transform.position=framing.center+body.transform.forward*3+body.transform.right*.6f;camera.transform.LookAt(framing.center);
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.07f,.09f,.12f);camera.targetTexture=rt;
            var lightObject=new GameObject("ArmsPreviewLight");SceneManager.MoveGameObjectToScene(lightObject,scene);var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=2;lightObject.transform.rotation=Quaternion.Euler(25,-35,0);
            camera.Render();RenderTexture.active=rt;var image=new Texture2D(1000,800,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1000,800),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);
        }
        finally{RenderTexture.active=previous;rt.Release();UnityEngine.Object.DestroyImmediate(rt);EditorSceneManager.ClosePreviewScene(scene);}
    }
}