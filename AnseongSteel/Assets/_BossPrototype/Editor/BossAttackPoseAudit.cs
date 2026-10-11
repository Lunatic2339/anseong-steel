using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Object=UnityEngine.Object;
namespace AnseongSteel.Bosses.Editor
{
 public static class BossAttackPoseAudit
 {
  public static void Run()
  {
   EditorSceneManager.OpenScene(BossModelTestBuilder.ScenePath);
   var c=Object.FindFirstObjectByType<BossCombatController>();var a=c.driver.animator;
   c.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);c.sword.Initialize();
   var idle=a.runtimeAnimatorController.animationClips.Single(x=>x.name=="Idle");
   var punch=a.runtimeAnimatorController.animationClips.Single(x=>x.name=="Punch");
   var bones=a.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("mixamorig:")).ToDictionary(t=>t.name);
   var feet=new[]{bones["mixamorig:LeftFoot"],bones["mixamorig:RightFoot"]};
   idle.SampleAnimation(a.gameObject,0);var floor=feet.Select(t=>t.position.y).ToArray();
   var report=new StringBuilder();Directory.CreateDirectory("Library/BossAttackPoseAudit");
   var seen=new HashSet<AnimationClip>();
   foreach(var d in c.attacks)
   {
    var clip=d.kind==BossAttackKind.HeavyPunch?punch:d.clip?d.clip:idle;
    report.AppendLine(d.kind+" clip="+clip.name+" length="+clip.length);
    if(!seen.Add(clip))continue;
    var samples=new Vector3[2,121];float maxStep=0;
    for(int i=0;i<=120;i++)
    {
     idle.SampleAnimation(a.gameObject,0);clip.SampleAnimation(a.gameObject,clip.length*i/120f);
     for(int side=0;side<2;side++)samples[side,i]=feet[side].position;
    }
    for(int side=0;side<2;side++)
    {
     float minY=float.MaxValue,maxY=float.MinValue,nearFloorSlide=0;
     for(int i=0;i<=120;i++)
     {
      var p=samples[side,i];minY=Mathf.Min(minY,p.y-floor[side]);maxY=Mathf.Max(maxY,p.y-floor[side]);
      if(i>0&&p.y<floor[side]+.045f&&samples[side,i-1].y<floor[side]+.045f)
       nearFloorSlide+=Vector3.ProjectOnPlane(p-samples[side,i-1],Vector3.up).magnitude;
      if(i>0)maxStep=Mathf.Max(maxStep,Vector3.Distance(p,samples[side,i-1]));
     }
     report.AppendLine("  "+feet[side].name+" height="+minY.ToString("F3")+".."+maxY.ToString("F3")+" groundedTravel="+nearFloorSlide.ToString("F3"));
    }
    report.AppendLine("  maximum sampled foot step="+maxStep.ToString("F3"));
    foreach(float n in new[]{.12f,.45f,.75f,.95f})
    {
     idle.SampleAnimation(a.gameObject,0);clip.SampleAnimation(a.gameObject,clip.length*n);
     if(d.IsSword){c.sword.Draw();c.sword.ApplyGripPose(0,d.bladeRoll.Evaluate(n));}else c.sword.Sheathe();
     var hands=c.GetComponent<BossHandPose>();
     bool fist=d.kind==BossAttackKind.HeavyPunch||d.kind==BossAttackKind.AlternatingCombo;
     bool grab=d.kind==BossAttackKind.Grab||d.kind==BossAttackKind.ArmLock;
     float fw=fist?Mathf.SmoothStep(0,1,Mathf.InverseLerp(0,.08f,n))*(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(d.kind==BossAttackKind.HeavyPunch?.8f:.7f,.98f,n))):0;
     // Sample the same hand correction used during gameplay.
     bool cast=d.kind==BossAttackKind.EMP||d.kind==BossAttackKind.SensorJam||d.kind==BossAttackKind.VisionDisruption;
     hands.Apply(fw,d.IsSword||fist||grab||cast||clip==idle?1:0,d.IsSword?1:0,d.IsSword?c.sword.SupportWeight:0,grab?Mathf.SmoothStep(0,1,Mathf.InverseLerp(.32f,.6f,n)):0,cast?BossHandPose.CastEnvelope(n):0);
     var baked=BossHandRigWorkshop.Bake(a);var cam=Camera.main;
     cam.orthographic=true;cam.orthographicSize=1.9f;cam.nearClipPlane=.01f;
     cam.transform.SetPositionAndRotation(new Vector3(4,2.8f,6),Quaternion.LookRotation(new Vector3(0,1.4f,0)-new Vector3(4,2.8f,6)));
     BossHandRigWorkshop.Render(cam,"Library/BossAttackPoseAudit/"+d.kind+"_"+Mathf.RoundToInt(n*100)+".png");
     if(!d.IsSword&&!fist&&clip!=idle)
     {
      var hand=bones["mixamorig:RightHand"];var along=(bones["mixamorig:RightHandMiddle1"].position-hand.position).normalized;
      var across=(bones["mixamorig:RightHandIndex1"].position-bones["mixamorig:RightHandPinky1"].position).normalized;
      var direction=(-Vector3.Cross(across,along)+across*.35f-along*.15f).normalized;
      cam.orthographicSize=.3f;cam.transform.SetPositionAndRotation(hand.position+along*.16f+direction*.9f,Quaternion.LookRotation(-direction,-along));
      BossHandRigWorkshop.Render(cam,"Library/BossAttackPoseAudit/"+d.kind+"_Hand_"+Mathf.RoundToInt(n*100)+".png");
     }
     foreach(var go in baked){Object.DestroyImmediate(go.GetComponent<MeshFilter>().sharedMesh);Object.DestroyImmediate(go);}
     hands.Restore();
    }
   }
   File.WriteAllText("Library/BossAttackPoseAudit/report.txt",report.ToString());Debug.Log("ATTACK POSE AUDIT COMPLETE\n"+report);
  }
 }
}

