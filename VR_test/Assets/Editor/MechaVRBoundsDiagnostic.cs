using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Animations.Rigging;
[InitializeOnLoad]
public static class MechaVRBoundsDiagnostic
{
 static MechaVRBoundsDiagnostic(){EditorApplication.update+=Tick;}
 static void Tick()
 {
  if(!File.Exists("Library/ArmBoundsDiagnostic.request")||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
  File.Delete("Library/ArmBoundsDiagnostic.request");Run();
 }
 public static void Run()
 {
  var report=new StringBuilder();var preview=EditorSceneManager.NewPreviewScene();
  try
  {
   var scene=SceneManager.GetSceneByPath("Assets/Scenes/MainVRScene_BodyTracking.unity");
   var body=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MechaVRBodyFollower>(true)).Single();
   var clone=UnityEngine.Object.Instantiate(body.gameObject);SceneManager.MoveGameObjectToScene(clone,preview);
   foreach(var b in clone.GetComponentsInChildren<Behaviour>())b.enabled=false;
   var cameraObject=new GameObject("BoundsDiagnosticCamera");SceneManager.MoveGameObjectToScene(cameraObject,preview);var camera=cameraObject.AddComponent<Camera>();
   var actual=body.head?body.head.GetComponent<Camera>():null;
   if(actual)camera.CopyFrom(actual);else{camera.fieldOfView=90;camera.nearClipPlane=.01f;}
   camera.enabled=false;camera.transform.position=body.head?body.head.position:body.transform.position+Vector3.up*.83f;
   float yaw=body.transform.eulerAngles.y;
   report.AppendLine("PlayMode="+EditorApplication.isPlaying+" nearClip="+camera.nearClipPlane+" fov="+camera.fieldOfView);
   var renderers=clone.GetComponentsInChildren<SkinnedMeshRenderer>(true);
   for(int pose=0;pose<2;pose++)
   {
    if(pose==1)foreach(var ik in clone.GetComponentsInChildren<TwoBoneIKConstraint>(true)) {ik.data.root.localRotation*=Quaternion.Euler(0,0,60);ik.data.mid.localRotation*=Quaternion.Euler(0,0,45);}
    foreach(var r in renderers)
    {
     var mesh=new Mesh();r.BakeMesh(mesh);var world=mesh.vertices.Select(v=>r.transform.TransformPoint(v)).ToArray();
     var bounds=new Bounds(world[0],Vector3.zero);foreach(var v in world)bounds.Encapsulate(v);
     Bounds declared=r.bounds;int outside=world.Count(v=>!declared.Contains(v));
     report.AppendLine("pose="+pose+" renderer="+r.name+" rootBone="+(r.rootBone?r.rootBone.name:"none")+" updateWhenOffscreen="+r.updateWhenOffscreen);
     report.AppendLine("Declared="+declared.ToString("F4")+" Actual="+bounds.ToString("F4")+" OutsideVertices="+outside+"/"+world.Length);
     for(int pitch=0;pitch<=80;pitch+=10)
     {
      camera.transform.rotation=Quaternion.Euler(-pitch,yaw,0);var planes=GeometryUtility.CalculateFrustumPlanes(camera);
      bool declaredVisible=GeometryUtility.TestPlanesAABB(planes,declared),actualVisible=GeometryUtility.TestPlanesAABB(planes,bounds);
      report.AppendLine("pitchUp="+pitch+" declaredVisible="+declaredVisible+" actualVisible="+actualVisible);
     }
     UnityEngine.Object.DestroyImmediate(mesh);
    }
   }
   File.WriteAllText("Library/ArmBoundsDiagnostic.txt",report.ToString());
  }
  catch(Exception e){File.WriteAllText("Library/ArmBoundsDiagnostic.txt",report+"\nFAIL\n"+e);}
  finally{EditorSceneManager.ClosePreviewScene(preview);}
 }
}