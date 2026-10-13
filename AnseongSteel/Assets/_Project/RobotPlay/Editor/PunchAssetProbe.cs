using UnityEditor;
using UnityEngine;
namespace AnseongSteel.RobotPlay.Editor
{
 public static class PunchAssetProbe
 {
  public static readonly string[] Paths={"Assets/_Project/02_Art/CockpitV01/Prefabs/Cockpit_V01.prefab","Assets/_Project/02_Art/CockpitV02/Prefabs/Cockpit_V02.prefab","Assets/CockpitVariants/A_CloseArc/Prefabs/Cockpit_A_CloseArc.prefab","Assets/CockpitVariants/B_RearDome/Prefabs/Cockpit_B_RearDome.prefab","Assets/CockpitVariants/C_SideDome/Prefabs/Cockpit_C_SideDome.prefab"};
  public static void Run()
  {
   foreach(var path in Paths){var asset=AssetDatabase.LoadAssetAtPath<GameObject>(path);Debug.Log("COCKPIT_ASSET "+path);
    foreach(var renderer in asset.GetComponentsInChildren<Renderer>(true))Debug.Log("COCKPIT_RENDERER "+renderer.name+" materials="+string.Join(",",System.Array.ConvertAll(renderer.sharedMaterials,m=>m?m.name:"null")));
    foreach(var t in asset.GetComponentsInChildren<Transform>(true))if(t.name.ToLower().Contains("view")||t.name.ToLower().Contains("camera")||t.name.ToLower().Contains("pilot"))Debug.Log("COCKPIT_ANCHOR "+t.name+" "+t.position);
   }
   IntegratedPlayBuilder.Open();var feed=Object.FindFirstObjectByType<AnseongSteel.PlayerMotion.CockpitLiveView>();Debug.Log("SOURCE_SCREEN "+feed.screen.name+" root="+PrefabUtility.GetOutermostPrefabInstanceRoot(feed.screen.gameObject)?.name+" slot="+feed.materialSlot);Debug.Log("SOURCE_CAMERA "+feed.cockpitCamera.transform.position);
  }
 }
}
