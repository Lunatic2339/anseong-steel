using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class MechaVRThinSwordValidation
{
    const string Request="Library/ThinSwordValidation.request";
    static MechaVRThinSwordValidation(){EditorApplication.update+=Tick;}
    static void Tick()
    {
        if(!File.Exists(Request)||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)return;
        File.Delete(Request); Validate();
    }
    static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    [MenuItem("Tools/Mecha VR/Validate Thin Sword")]
    public static void Validate()
    {
        var scene=EditorSceneManager.NewPreviewScene();
        try
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Weapons/CapstoneSword/CapstoneSword.prefab");
            var instance=(GameObject)PrefabUtility.InstantiatePrefab(prefab,scene);
            var filters=instance.GetComponentsInChildren<MeshFilter>();
            Check(filters.Length>0 && filters.All(f=>f.sharedMesh),"Sword prefab mesh reference broken.");
            var vertices=filters.SelectMany(f=>f.sharedMesh.vertices.Select(v=>instance.transform.InverseTransformPoint(f.transform.TransformPoint(v)))).ToArray();
            var blade=vertices.Where(v=>v.y<.649f).ToArray();
            float thickness=blade.Max(v=>v.z)-blade.Min(v=>v.z);
            Check(Mathf.Abs(thickness-.015039062f)<.0001f,"Imported blade thickness incorrect: "+thickness);
            Check(vertices.Max(v=>v.y)>.999f && vertices.Max(v=>v.y)<1.001f,"Sword length changed.");
            Check(filters.All(f=>f.sharedMesh.uv.Length==f.sharedMesh.vertexCount),"Sword UVs missing.");
            Check(instance.GetComponentsInChildren<Renderer>().All(r=>r.sharedMaterials.All(m=>m&&m.GetTexture("_BaseMap"))),"Sword material/texture missing.");
            MechaVRSwordSetup.ValidateInstalled();
            Check(File.ReadAllText("Library/MechaSwordSetup.txt").StartsWith("PASS"),"Sword equipment validation failed.");
            var cameraObject=new GameObject("ThinSwordPreviewCamera"); SceneManager.MoveGameObjectToScene(cameraObject,scene);
            var camera=cameraObject.AddComponent<Camera>(); camera.scene=scene; camera.orthographic=true; camera.orthographicSize=.57f;
            camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.08f,.1f,.13f);
            var lightObject=new GameObject("ThinSwordPreviewLight"); SceneManager.MoveGameObjectToScene(lightObject,scene);
            var light=lightObject.AddComponent<Light>(); light.type=LightType.Directional; light.intensity=2; lightObject.transform.rotation=Quaternion.Euler(35,-30,0);
            var rt=new RenderTexture(640,1000,24); camera.targetTexture=rt;
            var previous=RenderTexture.active;
            try
            {
                Directory.CreateDirectory("MechaVR_Validation");
                foreach(string view in new[]{"Front","Side","Angled"})
                {
                    Vector3 offset=view=="Front"?new Vector3(0,0,2):view=="Side"?new Vector3(2,0,0):new Vector3(1.5f,0,2);
                    camera.transform.position=new Vector3(0,.5f,0)+offset; camera.transform.LookAt(new Vector3(0,.5f,0)); camera.Render();
                    RenderTexture.active=rt; var image=new Texture2D(640,1000,TextureFormat.RGB24,false);
                    image.ReadPixels(new Rect(0,0,640,1000),0,0); image.Apply();
                    File.WriteAllBytes("MechaVR_Validation/ThinSword_"+view+".png",image.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(image);
                }
            }
            finally{RenderTexture.active=previous; camera.targetTexture=null; rt.Release(); UnityEngine.Object.DestroyImmediate(rt);}
            File.WriteAllText("Library/ThinSwordValidation.txt","PASS\nUnity blade thickness="+thickness+"m\nPrefab mesh, texture, UVs, length, grip alignment, A toggle PASS\n");
            Debug.Log("[Thin Sword] PASS: "+thickness+"m");
        }
        catch(Exception e){File.WriteAllText("Library/ThinSwordValidation.txt","FAIL\n"+e);Debug.LogException(e);}
        finally{EditorSceneManager.ClosePreviewScene(scene);}
    }
}