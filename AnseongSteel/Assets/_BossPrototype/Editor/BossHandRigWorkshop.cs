using System;
using System.Linq;
using System.IO;
using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;
namespace AnseongSteel.Bosses.Editor
{
 public static class BossHandRigWorkshop
 {
  const string Output="Library/BossHandReview";
  static bool closed;
  public static void Candidate(){closed=true;Audit();}
  public static void Review(){closed=false;Audit();closed=true;Audit();}
  public static void Pose(Animator animator,int mode=0)
  {
   var bones=animator.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("mixamorig:")).ToDictionary(t=>t.name);
   var rest=new Dictionary<string,Matrix4x4>();
   foreach(var s in animator.GetComponentsInChildren<SkinnedMeshRenderer>())
    for(int i=0;i<s.bones.Length;i++)if(s.bones[i])rest[s.bones[i].name]=s.transform.localToWorldMatrix*s.sharedMesh.bindposes[i].inverse;
   foreach(var side in new[]{"Right","Left"})
   {
    string prefix="mixamorig:"+side+"Hand";var hand=bones[prefix];var inv=rest[prefix].inverse;
    Vector3 Point(string name)=>inv.MultiplyPoint3x4(rest[prefix+name].GetColumn(3));
    var along=Point("Middle1").normalized;var across=(Point("Index1")-Point("Pinky1")).normalized;
    var inward=Vector3.Cross(across,along).normalized;
    if(Vector3.Dot(Point("Middle3")-Point("Middle1"),inward)<0)inward=-inward;
    along=Vector3.Cross(across,inward).normalized;
    if(Vector3.Dot(along,Point("Middle1"))<0)along=-along;
    var attachment=animator.GetComponentInParent<BossSwordAttachment>();
    var gripCenter=Point("Middle1")+along*.012f+inward*.024f;
    if(mode==2&&attachment)
    {
     if(side=="Right")attachment.handPosition=gripCenter;
     else
     {
      attachment.leftPalmLocal=gripCenter;
      var gripAcross=attachment.leftGripRotation*across;
      var target=Vector3.up*Mathf.Sign(Vector3.Dot(gripAcross,Vector3.up));
      attachment.leftGripRotation=Quaternion.FromToRotation(gripAcross,target)*attachment.leftGripRotation;
     }
    }
    foreach(string finger in new[]{"Index","Middle","Ring","Pinky"})
    {
     var angles=mode==2?FitGrip(new[]{Vector3.Distance(Point(finger+"1"),Point(finger+"2")),Vector3.Distance(Point(finger+"2"),Point(finger+"3")),Vector3.Distance(Point(finger+"3"),Point(finger+"3_end"))},new Vector2(Vector3.Dot(gripCenter-Point(finger+"1"),along),Vector3.Dot(gripCenter-Point(finger+"1"),inward))):null;
     for(int j=1;j<=3;j++)
     {
      var bone=bones[prefix+finger+j];var next=bones[prefix+finger+(j==3?"3_end":(j+1).ToString())];
      var origin=Point(finger+j);var restAim=(Point(finger+(j==3?"3_end":(j+1).ToString()))-origin).normalized;
      float relaxation=finger=="Index"?0:finger=="Middle"?1:finger=="Ring"?2:3;
      float degrees=mode==2?angles[j-1]:mode==1?(j==1?12+relaxation*4:j==2?28+relaxation*5:44+relaxation*6):(j==1?80:j==2?165:220);float angle=degrees*Mathf.Deg2Rad;
      var aim=along*Mathf.Cos(angle)+inward*Mathf.Sin(angle);
      if(mode==1)aim=(aim+across*((1-relaxation)*.025f)).normalized;
      var restRotation=Quaternion.Inverse(rest[prefix].rotation)*rest[bone.name].rotation;
      bone.localPosition=rest[bone.parent.name].inverse.MultiplyPoint3x4(rest[bone.name].GetColumn(3));
      bone.rotation=hand.rotation*Quaternion.FromToRotation(restAim,aim)*restRotation;
     }
    }
    if(mode==1)
    {
     for(int j=1;j<=3;j++)
     {
      var bone=bones[prefix+"Thumb"+j];var aim=(along+across*(j==1?.55f:j==2?.30f:.10f)+inward*(j==1?.08f:j==2?.17f:.3f)).normalized;
      var restAim=(Point("Thumb"+(j==3?"3_end":(j+1).ToString()))-Point("Thumb"+j)).normalized;
      bone.localPosition=rest[bone.parent.name].inverse.MultiplyPoint3x4(rest[bone.name].GetColumn(3));
      bone.rotation=hand.rotation*Quaternion.FromToRotation(restAim,aim)*Quaternion.Inverse(rest[prefix].rotation)*rest[bone.name].rotation;
     }
     continue;
    }
    var root=Point("Thumb1");
    float first=Vector3.Distance(root,Point("Thumb2")),second=Vector3.Distance(Point("Thumb2"),Point("Thumb3")),third=Vector3.Distance(Point("Thumb3"),Point("Thumb3_end"));
    var thumbAim=-across;
    var tip=mode==2?gripCenter+inward*.020f-along*.005f:Point("Middle1")+inward*.052f-along*.017f;
    var distal=tip-thumbAim*third;
    var delta=distal-root;float distance=Mathf.Clamp(delta.magnitude,Mathf.Abs(first-second)+.001f,first+second-.001f);var direction=delta.normalized;
    var bend=Vector3.ProjectOnPlane(across+inward*.7f,direction).normalized;
    float length=(first*first-second*second+distance*distance)/(2*distance);
    var middle=root+direction*length+bend*Mathf.Sqrt(Mathf.Max(0,first*first-length*length));
    var targets=new[]{root,middle,root+direction*distance,tip};
    for(int j=1;j<=3;j++)
    {
     var bone=bones[prefix+"Thumb"+j];var restAim=(Point("Thumb"+(j==3?"3_end":(j+1).ToString()))-Point("Thumb"+j)).normalized;
     var restRotation=Quaternion.Inverse(rest[prefix].rotation)*rest[bone.name].rotation;
     bone.localPosition=rest[bone.parent.name].inverse.MultiplyPoint3x4(rest[bone.name].GetColumn(3));
     bone.rotation=hand.rotation*Quaternion.FromToRotation(restAim,(targets[j]-targets[j-1]).normalized)*restRotation;
    }
   }
  }
  static float[] FitGrip(float[] lengths,Vector2 center)
  {
   // Three rigid phalanges tangent to the handle, with clearance for their pads.
   const float radius=.018f;
   var angles=new[]{20f,115f,220f};
   float Cost(float[] values)
   {
    float cost=0;var p=Vector2.zero;
    for(int i=0;i<3;i++)
    {
     float a=values[i]*Mathf.Deg2Rad;var step=new Vector2(Mathf.Cos(a),Mathf.Sin(a))*lengths[i];var next=p+step;
     var nearest=p+step*Mathf.Clamp01(Vector2.Dot(center-p,step)/step.sqrMagnitude);
     float clearance=(nearest-center).magnitude;
     cost+=Mathf.Pow((clearance-radius)*100,2)*(clearance<radius?12:1);
     float bend=values[i]-(i==0?0:values[i-1]);
     if(bend<0)cost+=bend*bend;if(bend>110)cost+=(bend-110)*(bend-110);
     p=next;
    }
    cost+=Mathf.Pow(((p-center).magnitude-radius)*100,2)*2;
    return cost;
   }
   foreach(float step in new[]{20f,10f,5f,2f,.5f})
    for(int pass=0;pass<20;pass++)
    {
     bool improved=false;
     for(int i=0;i<3;i++)
     {
      float best=Cost(angles),original=angles[i],chosen=original;
      foreach(float delta in new[]{-step,step}){angles[i]=Mathf.Clamp(original+delta,-10,260);float cost=Cost(angles);if(cost<best){best=cost;chosen=angles[i];improved=true;}}
      angles[i]=chosen;
     }
     if(!improved)break;
    }
   return angles;
  }
  public static void Audit()
  {
   Directory.CreateDirectory(Output);EditorSceneManager.OpenScene(BossModelTestBuilder.ScenePath);
   var driver=Object.FindFirstObjectByType<BossModelDemo>().driver;
   driver.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
   var bones=driver.animator.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("mixamorig:")).ToDictionary(t=>t.name);
   var sword=driver.GetComponent<BossSwordAttachment>();sword.Initialize();
   var report=new StringBuilder();
   foreach(var skin in driver.animator.GetComponentsInChildren<SkinnedMeshRenderer>())
    if(skin.sharedMesh.boneWeights.Any(w=>skin.bones[w.boneIndex0].name.Contains("Hand")))
    {
     report.AppendLine("SKIN "+skin.name+" vertices="+skin.sharedMesh.vertexCount+" bones="+string.Join(",",skin.bones.Select(b=>b?b.name:"null")));
    }
   var clips=driver.animator.runtimeAnimatorController.animationClips;
   foreach(var pose in new[]{("Grab",1f),("Idle",0f),("Punch",2.05f),("SwordIdle",.1f),("SwordSlash",.85f),("Combat_AlternatingCombo",.6f)})
   {
    var clip=clips.FirstOrDefault(c=>c.name==pose.Item1||c.name==pose.Item1.Replace("Combat_",""));if(!clip)continue;
    clip.SampleAnimation(driver.animator.gameObject,pose.Item2);
    bool holding=pose.Item1.StartsWith("Sword");
    if(holding)
    {
     sword.Draw();var definition=driver.GetComponent<BossCombatController>().attacks.FirstOrDefault(d=>d.kind.ToString()==pose.Item1);
     sword.ApplyGripPose(pose.Item1=="SwordIdle"?1:0,definition?definition.bladeRoll.Evaluate(pose.Item2/clip.length):0);
    }
    else sword.Sheathe();
    if(closed)
    {
     var correction=driver.GetComponent<BossHandPose>();
     if(correction)correction.Apply(pose.Item1.Contains("Punch")||pose.Item1.Contains("Combo")?1:0,pose.Item1=="Idle"||pose.Item1=="Grab"||holding?1:0,holding?1:0,holding?sword.SupportWeight:0,pose.Item1=="Grab"?1:0);
    }
    report.AppendLine("POSE "+pose.Item1);
    foreach(string side in new[]{"Right","Left"})
    {
     var hand=bones["mixamorig:"+side+"Hand"];
     foreach(var t in bones.Values.Where(t=>t.name.Contains(side+"Hand")))
      report.AppendLine(t.name+" local="+hand.InverseTransformPoint(t.position).ToString("F5")+" rot="+t.localRotation.ToString("F5"));
    }
    var baked=Bake(driver.animator);
    foreach(string side in new[]{"Right","Left"})
    {
     var visible=new HashSet<string>(driver.animator.GetComponentsInChildren<SkinnedMeshRenderer>().Where(s=>s.sharedMesh.boneWeights.Any(w=>s.bones[w.boneIndex0].name.StartsWith("mixamorig:"+side+"Hand")||s.bones[w.boneIndex0].name=="mixamorig:"+side+"ForeArm")).Select(s=>"HandReviewMesh_"+s.name));
     foreach(var go in baked)go.SetActive(visible.Contains(go.name));
     var hand=bones["mixamorig:"+side+"Hand"];var index=bones["mixamorig:"+side+"HandIndex1"];var pinky=bones["mixamorig:"+side+"HandPinky1"];
     var across=(index.position-pinky.position).normalized;
     var along=((index.position+pinky.position)*.5f-hand.position).normalized;
     var normal=Vector3.Cross(across,along).normalized;
     var center=hand.position+along*(pose.Item1=="Idle"?.23f:.12f);
     foreach(int view in new[]{0,1})
     {
      var cam=Camera.main;cam.orthographic=true;cam.orthographicSize=pose.Item1=="Idle"?.30f:.25f;cam.nearClipPlane=.01f;
      var direction=(normal*(view==0?1:-1)+across*.35f-along*.15f).normalized;
      cam.transform.SetPositionAndRotation(center+direction*.9f,Quaternion.LookRotation(-direction,-along));
      var lightGo=new GameObject("HandReviewFill");var fill=lightGo.AddComponent<Light>();fill.type=LightType.Directional;fill.intensity=1.5f;lightGo.transform.rotation=cam.transform.rotation;
      Render(cam,Output+"/"+(closed?"Corrected_":"Source_")+pose.Item1+"_"+side+"_"+view+".png");
      Object.DestroyImmediate(lightGo);
     }
    }
    foreach(var go in baked){Object.DestroyImmediate(go.GetComponent<MeshFilter>().sharedMesh);Object.DestroyImmediate(go);}
    if(closed)driver.GetComponent<BossHandPose>()?.Restore();
   }
   File.WriteAllText(Output+"/audit.txt",report.ToString());Debug.Log("HAND AUDIT PASS");
  }
  public static List<GameObject> Bake(Animator animator)
  {
   var result=new List<GameObject>();
   foreach(var skin in animator.GetComponentsInChildren<SkinnedMeshRenderer>())
   {
    var mesh=Object.Instantiate(skin.sharedMesh);var vertices=mesh.vertices;var normals=mesh.normals;var weights=mesh.boneWeights;var binds=mesh.bindposes;
    var matrices=skin.bones.Select((b,i)=>b.localToWorldMatrix*binds[i]).ToArray();
    for(int v=0;v<vertices.Length;v++)
    {
     var w=weights[v];var p=vertices[v];var n=normals[v];
     vertices[v]=matrices[w.boneIndex0].MultiplyPoint3x4(p)*w.weight0+matrices[w.boneIndex1].MultiplyPoint3x4(p)*w.weight1+matrices[w.boneIndex2].MultiplyPoint3x4(p)*w.weight2+matrices[w.boneIndex3].MultiplyPoint3x4(p)*w.weight3;
     normals[v]=(matrices[w.boneIndex0].MultiplyVector(n)*w.weight0+matrices[w.boneIndex1].MultiplyVector(n)*w.weight1+matrices[w.boneIndex2].MultiplyVector(n)*w.weight2+matrices[w.boneIndex3].MultiplyVector(n)*w.weight3).normalized;
    }
    mesh.vertices=vertices;mesh.normals=normals;mesh.RecalculateBounds();var go=new GameObject("HandReviewMesh_"+skin.name);
    go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials;skin.enabled=false;result.Add(go);
   }
   return result;
  }
  public static void Render(Camera cam,string path)
  {
   var target=new RenderTexture(900,900,24);target.Create();var request=new RenderPipeline.StandardRequest{destination=target};
   RenderPipeline.SubmitRenderRequest(cam,request);RenderPipeline.SubmitRenderRequest(cam,request);
   var previous=RenderTexture.active;RenderTexture.active=target;var image=new Texture2D(900,900,TextureFormat.RGB24,false);
   image.ReadPixels(new Rect(0,0,900,900),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());
   RenderTexture.active=previous;Object.DestroyImmediate(image);target.Release();Object.DestroyImmediate(target);
  }
 }
}


