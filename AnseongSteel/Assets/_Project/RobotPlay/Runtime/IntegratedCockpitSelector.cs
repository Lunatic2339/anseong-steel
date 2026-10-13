using UnityEngine;
using UnityEngine.InputSystem;
using AnseongSteel.PlayerMotion;
namespace AnseongSteel.RobotPlay
{
 public sealed class IntegratedCockpitSelector:MonoBehaviour
 {
  public GameObject[] variants;public GameObject originalShell;public CockpitLiveView feed;public RobotPilotInput pilot;
  public float eyeHeight=1.665f;public ArmMotionInput context;public int Current {get;private set;}=1;public bool RightView {get;private set;}
  GameObject[] instances;Material[] displays;Vector3 eyeOffset;bool localRight;Vector3 origin;Quaternion orientation;Vector3 scale;Material displayMaterial;
  public string CurrentName=>variants!=null&&Current<variants.Length?variants[Current].name:"Cockpit";
  void Start(){origin=originalShell.transform.position;orientation=originalShell.transform.rotation;scale=originalShell.transform.lossyScale;displayMaterial=feed.screen.sharedMaterials[feed.materialSlot];instances=new GameObject[variants.Length];displays=new Material[variants.Length];eyeOffset=context&&context.leftAvatar?context.leftAvatar.neutralEye:new Vector3(0,eyeHeight,.075f);Select(1);}
  public bool Select(int index)
  {
   if(instances==null||index<0||index>=variants.Length||!variants[index])return false;
   if(!instances[index]){instances[index]=Instantiate(variants[index],origin,orientation);instances[index].transform.localScale=scale;foreach(var camera in instances[index].GetComponentsInChildren<Camera>(true))camera.enabled=false;}
   foreach(var instance in instances)if(instance)instance.SetActive(false);foreach(var oldRenderer in originalShell.GetComponentsInChildren<Renderer>(true))oldRenderer.enabled=false;instances[index].SetActive(true);Current=index;
   if(!displays[index]){displays[index]=new Material(displayMaterial);float span=index<2?212:index==2?208:180;float low=index<2?-18:index==2?-32:index==3?-86:-74;float high=index<2?27:index==2?43:index==3?86:79;displays[index].SetFloat("_AzimuthSpan",span);displays[index].SetFloat("_ElevationMin",low);displays[index].SetFloat("_ElevationMax",high);}
   Renderer screen=null;int slot=-1;Transform left=null,right=null;
   foreach(var t in instances[index].GetComponentsInChildren<Transform>(true)){if(t.name=="Pilot_Left_Floor")left=t;if(t.name=="Pilot_Right_Floor")right=t;}
   foreach(var renderer in instances[index].GetComponentsInChildren<Renderer>(true)){var materials=renderer.sharedMaterials;for(int i=0;i<materials.Length;i++)if(materials[i]&&(materials[i].name.Contains("Display_Feed")||materials[i]==displays[index])){screen=renderer;slot=i;materials[i]=displays[index];renderer.sharedMaterials=materials;}}
   if(!screen||!left||!right){Debug.LogError("Existing cockpit lacks display/pilot anchors: "+CurrentName);return false;}
   feed.enabled=false;feed.screen=screen;feed.materialSlot=slot;feed.enabled=true;
   var parent=pilot.cockpitCamera.transform.parent;var lp=left.TransformPoint(eyeOffset);var rp=right.TransformPoint(eyeOffset);
   pilot.SetCockpitOrigin(parent?parent.InverseTransformPoint(lp):lp,parent?parent.InverseTransformVector(rp-lp):rp-lp,RightView);
   if(context){context.leftAvatar.transform.SetPositionAndRotation(left.position,left.rotation);context.rightAvatar.transform.SetPositionAndRotation(right.position,right.rotation);ApplyLocalView(pilot.session.NetworkActive?pilot.session.LocalSide==1:RightView);}
   return true;
  }
  public void View(bool right){RightView=right;Select(Current);}
  void ApplyLocalView(bool right){localRight=right;int mask=pilot.cockpitCamera.cullingMask;context.leftAvatar.SetLocalCamera(right?null:pilot.cockpitCamera);context.rightAvatar.SetLocalCamera(right?pilot.cockpitCamera:null);pilot.cockpitCamera.cullingMask=(mask|(1<<31))&~((1<<27)|(1<<29)|(1<<30));}
  void LateUpdate(){bool right=pilot.session.NetworkActive?pilot.session.LocalSide==1:RightView;if(context&&right!=localRight)ApplyLocalView(right);}
  void Update(){var k=Keyboard.current;if(k==null)return;if(k.f2Key.wasPressedThisFrame)Select((Current+1)%variants.Length);if(k.f3Key.wasPressedThisFrame&&!pilot.session.NetworkActive)View(!RightView);}
  void OnGUI(){GUILayout.BeginArea(new Rect(Screen.width-300,12,285,130),GUI.skin.box);GUILayout.Label(CurrentName+" / F2: COCKPIT");if(GUILayout.Button("NEXT COCKPIT"))Select((Current+1)%variants.Length);if(!pilot.session.NetworkActive&&GUILayout.Button(RightView?"LEFT VIEW / F3":"RIGHT VIEW / F3"))View(!RightView);GUILayout.EndArea();}
  void OnDestroy(){if(displays!=null)foreach(var material in displays)if(material)Destroy(material);if(instances!=null)foreach(var instance in instances)if(instance)Destroy(instance);}
 }
}
