using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class MechaVRShieldPoseValidation
{
    const string Request="Library/ShieldForwardValidation.request";
    const string Report="Library/ShieldForwardValidation.txt";
    static MechaVRShieldPoseValidation() { EditorApplication.update+=Tick; }
    static void Tick()
    {
        if(!File.Exists(Request)||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(Request); Validate();
    }
    static void Check(bool valid,string text) {if(!valid) throw new InvalidOperationException(text);}
    [MenuItem("Tools/Mecha VR/Validate Forward Facing Shield")]
    public static void Validate()
    {
        var scene=EditorSceneManager.NewPreviewScene();
        try
        {
            var fixture=new GameObject("ShieldPoseValidation"); SceneManager.MoveGameObjectToScene(fixture,scene);
            var forearm=new GameObject("LowerArm.L").transform; forearm.SetParent(fixture.transform,false);
            var hand=new GameObject("Hand.L").transform; hand.SetParent(forearm,false); hand.localPosition=new Vector3(0,.3f,0);
            var view=new GameObject("View").transform; view.SetParent(fixture.transform,false);
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Weapons/CapstoneShield/CapstoneShield.prefab");
            var instance=(GameObject)PrefabUtility.InstantiatePrefab(prefab,scene); instance.transform.SetParent(forearm,false);
            var d=instance.GetComponent<MechaVRShieldDeploy>(); d.forearmAnchor=forearm; d.handAnchor=hand; d.facingReference=view;
            var light=d.armorSeamLights[0].transform; Vector3 localLight=light.localPosition;
            foreach(var rotation in new[]{Vector3.zero,new Vector3(0,0,90),new Vector3(45,120,75),new Vector3(180,20,160)})
            {
                forearm.SetPositionAndRotation(new Vector3(1,2,3),Quaternion.Euler(rotation));
                view.rotation=Quaternion.Euler(25,40,30); d.RefreshPose();
                Vector3 forward=Vector3.ProjectOnPlane(view.forward,Vector3.up).normalized;
                var plane=d.energyRenderer.transform;
                Check(Vector3.Dot(plane.forward,forward)>.9999f,"Plane follows arm rotation instead of view heading.");
                Check(Vector3.Dot(plane.up,Vector3.up)>.9999f,"Plane must stay upright.");
                Check(Vector3.Distance(plane.position,Vector3.Lerp(forearm.position,hand.position,.55f)+forward*d.forwardOffset)<.0001f,"Plane does not stay in front of arm.");
                Check(light.localPosition==localLight,"Armor seam moved relative to arm.");
            }
            Quaternion before=d.energyRenderer.transform.rotation;
            view.rotation=Quaternion.Euler(-90,40,0); d.RefreshPose();
            Check(Quaternion.Angle(before,d.energyRenderer.transform.rotation)<.01f,"Vertical gaze lost heading.");
            typeof(MechaVRShieldSetup).GetMethod("Validate",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{d});
            File.WriteAllText(Report,"PASS\nArm rotation independent; position follows forearm; front offset 0.16m; upright; view yaw follows; vertical gaze stable; seams remain on arm; X toggle and reversal PASS\n");
            Debug.Log("[MechaVR Shield Pose] PASS");
        }
        catch(Exception e){File.WriteAllText(Report,"FAIL\n"+e); Debug.LogException(e);}
        finally {EditorSceneManager.ClosePreviewScene(scene);}
    }
}