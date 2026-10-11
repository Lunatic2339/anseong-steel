using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
namespace AnseongSteel.Bosses.Editor
{
 public static class BossSwordFitAudit
 {
  public static void Run()
  {
   EditorSceneManager.OpenScene(BossModelTestBuilder.ScenePath);
   var d=Object.FindFirstObjectByType<BossModelDemo>().driver;d.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
   var s=new StringBuilder();var a=d.GetComponent<BossSwordAttachment>();
   foreach(string name in new[]{"SwordIdle","OverheadSmash","SwordSlash"})
   {
    var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/_BossPrototype/Animation/Combat/"+name+".anim");clip.SampleAnimation(d.animator.gameObject,0);
    s.AppendLine(name);
    foreach(var b in AnimationUtility.GetCurveBindings(clip).Select(x=>x.path).Distinct())if(!d.animator.transform.Find(b))s.AppendLine("MISSING "+b);
    foreach(var t in d.animator.GetComponentsInChildren<Transform>().Where(x=>x.name.Contains("Hand")))s.AppendLine(t.name+" pos="+t.position.ToString("F4")+" right="+t.right.ToString("F3")+" up="+t.up.ToString("F3")+" forward="+t.forward.ToString("F3"));
   }
   s.AppendLine("Sword scale "+a.sword.lossyScale+" hand scale "+a.rightHand.lossyScale);
   File.WriteAllText("Library/BossSwordFitAudit.txt",s.ToString());Debug.Log("SWORD FIT AUDIT PASS");
  }
 }
}
