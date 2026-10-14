using UnityEngine;

// Visualize anatomical arm chains rather than the flat Transform hierarchy.
[DefaultExecutionOrder(10000)]
public sealed class BodyArmTrackingView : MonoBehaviour
{
 public OVRSkeleton skeleton;
 GameObject visuals;
 LineRenderer[] lines;
 Transform[,] dots;
 Material leftMaterial,rightMaterial,elbowMaterial;
 readonly Vector3[][] positions={new Vector3[3],new Vector3[3]};
 readonly bool[] valid=new bool[2];
 readonly OVRSkeleton.BoneId[][] joints={new[]{OVRSkeleton.BoneId.Body_LeftArmUpper,OVRSkeleton.BoneId.Body_LeftArmLower,OVRSkeleton.BoneId.Body_LeftHandWrist},new[]{OVRSkeleton.BoneId.Body_RightArmUpper,OVRSkeleton.BoneId.Body_RightArmLower,OVRSkeleton.BoneId.Body_RightHandWrist}};
 Material MakeMaterial(Color c){var m=new Material(Shader.Find("Universal Render Pipeline/Unlit"));m.SetColor("_BaseColor",c);return m;}
 void Start(){
  if(!skeleton)skeleton=GetComponent<OVRSkeleton>();
  leftMaterial=MakeMaterial(Color.cyan);rightMaterial=MakeMaterial(Color.magenta);elbowMaterial=MakeMaterial(Color.yellow);
  visuals=new GameObject("ArmTrackingVisualization");visuals.transform.SetParent(transform,false);
  lines=new LineRenderer[2];dots=new Transform[2,3];
  for(int side=0;side<2;side++){
   var root=new GameObject(side==0?"LeftArm":"RightArm");root.transform.SetParent(visuals.transform,false);
   var l=root.AddComponent<LineRenderer>();l.sharedMaterial=side==0?leftMaterial:rightMaterial;l.useWorldSpace=true;l.positionCount=3;l.startWidth=l.endWidth=.008f;lines[side]=l;
   for(int j=0;j<3;j++){
    var o=GameObject.CreatePrimitive(PrimitiveType.Sphere);o.name=new[]{"Shoulder","Elbow","Wrist"}[j];o.transform.SetParent(root.transform,false);o.transform.localScale=Vector3.one*(j==1?.035f:.023f);
    var col=o.GetComponent<Collider>();col.enabled=false;Destroy(col);o.GetComponent<Renderer>().sharedMaterial=j==1?elbowMaterial:l.sharedMaterial;dots[side,j]=o.transform;
   }
  }
 }
 bool FindJoint(OVRSkeleton.BoneId id,out Vector3 p){
  foreach(var b in skeleton.Bones)if(b.Transform && b.Id==id){p=b.Transform.position;return true;}
  p=default;return false;
 }
 void LateUpdate(){
  var old=transform.Find("SkeletonRenderer");if(old)old.gameObject.SetActive(false);
  if(!visuals)return;
  for(int side=0;side<2;side++){
   valid[side]=skeleton && skeleton.IsInitialized && skeleton.IsDataValid;
   if(valid[side])for(int j=0;j<3;j++)valid[side]&=FindJoint(joints[side][j],out positions[side][j]);
   lines[side].gameObject.SetActive(valid[side]);
   if(!valid[side])continue;
   lines[side].SetPositions(positions[side]);for(int j=0;j<3;j++)dots[side,j].position=positions[side][j];
  }
 }
 void OnGUI(){
  GUI.Box(new Rect(12,12,410,88),"");
  GUI.Label(new Rect(22,18,390,25),"Body tracking: "+(valid[0]&&valid[1]?"valid joint data":"waiting for valid joint data"));
  GUI.Label(new Rect(22,44,390,25),"LEFT = cyan | RIGHT = magenta | ELBOW = yellow");
  GUI.Label(new Rect(22,68,390,25),"Keep wrist still; move elbow sideways to check tracking.");
 }
 void OnDisable(){if(visuals)visuals.SetActive(false);}
 void OnEnable(){if(visuals)visuals.SetActive(true);}
 void OnDestroy(){if(visuals)Destroy(visuals);if(leftMaterial)Destroy(leftMaterial);if(rightMaterial)Destroy(rightMaterial);if(elbowMaterial)Destroy(elbowMaterial);}
}

