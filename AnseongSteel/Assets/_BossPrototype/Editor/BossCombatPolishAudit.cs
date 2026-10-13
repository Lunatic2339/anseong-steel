using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
namespace AnseongSteel.Bosses.Editor
{
 public static class BossCombatPolishAudit
 {
  public static void Run()
  {
   EditorSceneManager.OpenScene(BossModelTestBuilder.ScenePath);
   var d=Object.FindFirstObjectByType<BossModelDemo>().driver;d.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
   var hip=d.animator.GetComponentsInChildren<Transform>().Single(t=>t.name=="mixamorig:Hips");
   var s=new StringBuilder();
   foreach(string name in new[]{"Idle","Walk","WalkBackward","Punch","SwordIdle"})
   {
    var clip=d.animator.runtimeAnimatorController.animationClips.Single(c=>c.name==name);
    foreach(float n in new[]{0f,.25f,.5f,.75f,1f}){clip.SampleAnimation(d.animator.gameObject,n*clip.length);s.AppendLine(name+" "+n+" hip="+d.transform.InverseTransformPoint(hip.position).ToString("F4")+" model="+d.animator.transform.localPosition.ToString("F4"));}
    foreach(var b in AnimationUtility.GetCurveBindings(clip).Where(b=>b.propertyName.Contains("Position")&&(b.path.EndsWith("Hips")||b.path==""||b.path=="Armature")))
    {var c=AnimationUtility.GetEditorCurve(clip,b);s.AppendLine(b.path+" "+b.propertyName+" "+c.Evaluate(0)+" -> "+c.Evaluate(clip.length));}
   }
   File.WriteAllText("Library/BossCombatPolishAudit.txt",s.ToString());Debug.Log(s.ToString());
  }
 }
}
