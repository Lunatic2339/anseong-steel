using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
namespace AnseongSteel.Bosses.Editor
{
 public static class BossCombatPoseReview
 {
  public static void PolishAndCapture(){BossSwordPolishInstaller.Apply();Capture();}
  public static void Capture()
  {
   EditorSceneManager.OpenScene(BossModelTestBuilder.ScenePath);
   var demo=Object.FindFirstObjectByType<BossModelDemo>();
   var actor=demo.driver.transform;
   actor.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
   var sword=actor.GetComponent<BossSwordAttachment>();sword.Initialize();sword.Draw();
   var cam=Camera.main;cam.transform.position=new Vector3(5,3.7f,6);cam.transform.LookAt(new Vector3(0,1.4f,0));cam.fieldOfView=43;
   foreach(var item in new[]{("SwordIdle",.1f),("OverheadSmash",.45f),("OverheadSmash",.7f),("SwordSlash",.85f),("SwordSlash",1.1f),("AlternatingCombo",.4f),("Grab",1f),("LaserCharge",.1f),("LaserFire",.1f)})
   {
    if(item.Item1=="AlternatingCombo")sword.Sheathe();
    bool laserPose=item.Item1.StartsWith("Laser");
    if(laserPose)sword.Sheathe();
    var clip=laserPose?demo.driver.animator.runtimeAnimatorController.animationClips.Single(c=>c.name=="Idle"):AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/_BossPrototype/Animation/Combat/"+item.Item1+".anim");
    clip.SampleAnimation(demo.driver.animator.gameObject,item.Item2);var combat=actor.GetComponent<BossCombatController>();
    var definition=combat.attacks.FirstOrDefault(d=>d.kind.ToString()==item.Item1);
    sword.ApplyGripPose(item.Item1=="SwordIdle"?1:0,definition?definition.bladeRoll.Evaluate(item.Item2/clip.length):0);
    if(laserPose){combat.laser.Initialize();combat.laser.Charge(.75f);if(item.Item1=="LaserFire")combat.laser.Fire(combat.laser.Origin+Quaternion.AngleAxis(-16,Vector3.up)*Vector3.forward*8); }
    // SampleAnimation changes bones immediately; bake skinning explicitly because
    // several poses are rendered in a single editor frame.
    var bakedObjects=new System.Collections.Generic.List<GameObject>();
    foreach(var skin in demo.driver.animator.GetComponentsInChildren<SkinnedMeshRenderer>())
    {
     var mesh=Object.Instantiate(skin.sharedMesh);
     var vertices=mesh.vertices;var normals=mesh.normals;var weights=mesh.boneWeights;var binds=mesh.bindposes;
     var matrices=new Matrix4x4[skin.bones.Length];
     for(int b=0;b<matrices.Length;b++)matrices[b]=skin.bones[b].localToWorldMatrix*binds[b];
     for(int v=0;v<vertices.Length;v++)
     {
      var w=weights[v];var p=vertices[v];var n=normals[v];
      vertices[v]=matrices[w.boneIndex0].MultiplyPoint3x4(p)*w.weight0+matrices[w.boneIndex1].MultiplyPoint3x4(p)*w.weight1+matrices[w.boneIndex2].MultiplyPoint3x4(p)*w.weight2+matrices[w.boneIndex3].MultiplyPoint3x4(p)*w.weight3;
      normals[v]=(matrices[w.boneIndex0].MultiplyVector(n)*w.weight0+matrices[w.boneIndex1].MultiplyVector(n)*w.weight1+matrices[w.boneIndex2].MultiplyVector(n)*w.weight2+matrices[w.boneIndex3].MultiplyVector(n)*w.weight3).normalized;
     }
     mesh.vertices=vertices;mesh.normals=normals;mesh.RecalculateBounds();
     var go=new GameObject("PoseReviewMesh");
     go.AddComponent<MeshFilter>().sharedMesh=mesh;
     go.AddComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials;
     skin.enabled=false;bakedObjects.Add(go);
    }
    foreach(bool firstPerson in new[]{false,true})
    {
    cam.transform.position=firstPerson?new Vector3(0,2.25f,3.35f):new Vector3(5,3.7f,6);
    cam.transform.rotation=firstPerson?Quaternion.LookRotation(Vector3.back+Vector3.down*.20f):Quaternion.LookRotation(new Vector3(0,1.4f,0)-cam.transform.position);
    cam.fieldOfView=firstPerson?65:43;
    var target=new RenderTexture(1280,800,24);target.Create();
    var request=new RenderPipeline.StandardRequest{destination=target};
    RenderPipeline.SubmitRenderRequest(cam,request);RenderPipeline.SubmitRenderRequest(cam,request);
    var previous=RenderTexture.active;RenderTexture.active=target;
    var image=new Texture2D(1280,800,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1280,800),0,0);image.Apply();
    File.WriteAllBytes("Library/BossSwordPolish_"+item.Item1+"_"+item.Item2.ToString("F2",System.Globalization.CultureInfo.InvariantCulture)+(firstPerson?"_FirstPerson":"_Overview")+".png",image.EncodeToPNG());
    RenderTexture.active=previous;Object.DestroyImmediate(image);target.Release();Object.DestroyImmediate(target);
    }
    foreach(var go in bakedObjects){Object.DestroyImmediate(go.GetComponent<MeshFilter>().sharedMesh);Object.DestroyImmediate(go);}
   }
   Debug.Log("COMBAT POSE CAPTURE PASS");
  }
 }
}


