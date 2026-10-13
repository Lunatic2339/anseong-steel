using UnityEngine;
using UnityEngine.XR;
namespace AnseongSteel.RobotPlay
{
 public struct HapticPulse {public float amplitude,duration;public int priority;public FeedbackKind kind;}
 public sealed class PunchHapticQueue
 {
  readonly HapticPulse[] pending=new HapticPulse[2];readonly bool[] has=new bool[2];readonly float[] sent={-100,-100},queued={-100,-100};readonly FeedbackKind[] lastKind=new FeedbackKind[2];
  public float interval=.09f;
  public static HapticPulse Pattern(FeedbackKind k)=>new HapticPulse{kind=k,amplitude=k==FeedbackKind.Damage?.7f:k==FeedbackKind.Boost?.9f:k==FeedbackKind.Combo?.6f:k==FeedbackKind.Guard?.2f:.35f,duration=k==FeedbackKind.Damage?.14f:k==FeedbackKind.Boost?.11f:k==FeedbackKind.Combo?.1f:k==FeedbackKind.Guard?.04f:.06f,priority=k==FeedbackKind.Damage?5:k==FeedbackKind.Boost?4:k==FeedbackKind.Combo?3:k==FeedbackKind.Hit?2:1};
  public void Queue(int hand,FeedbackKind kind,float now){if(hand<0||hand>1||kind==FeedbackKind.Defeated)return;if(kind==lastKind[hand]&&now-queued[hand]<interval)return;var pulse=Pattern(kind);if(!has[hand]||pulse.priority>pending[hand].priority){pending[hand]=pulse;has[hand]=true;}queued[hand]=now;lastKind[hand]=kind;}
  public bool Take(int hand,float now,out HapticPulse pulse){pulse=default;if(!has[hand]||now-sent[hand]<interval)return false;pulse=pending[hand];has[hand]=false;sent[hand]=now;return true;}
  public void Reset(){for(int i=0;i<2;i++){has[i]=false;sent[i]=queued[i]=-100;}}
 }
 public sealed class IntegratedFeedback : MonoBehaviour
 {
  public RobotPlaySession session;public AudioClip hit,guard,damage,boost;
  [Min(.02f)]public float minimumInterval=.09f;public int Delivered {get;private set;}public int Unsupported {get;private set;}
  [Header("Impulse amplitude / duration seconds (provisional)")]
  public Vector2 hitImpulse=new Vector2(.35f,.06f),guardImpulse=new Vector2(.2f,.04f),damageImpulse=new Vector2(.7f,.14f),comboImpulse=new Vector2(.6f,.1f),boostImpulse=new Vector2(.9f,.11f);
  AudioSource audioSource;readonly PunchHapticQueue queue=new PunchHapticQueue();uint epoch;
  void Start(){audioSource=gameObject.AddComponent<AudioSource>();audioSource.spatialBlend=0;audioSource.volume=.4f;session.Feedback+=Receive;}
  void OnDestroy(){if(session)session.Feedback-=Receive;}
  void OnDisable(){ResetFeedback();}
  public void ResetFeedback(){queue.Reset();if(audioSource)audioSource.Stop();for(int hand=0;hand<2;hand++){var device=InputDevices.GetDeviceAtXRNode(hand==0?XRNode.LeftHand:XRNode.RightHand);if(device.isValid)device.StopHaptics();}}
  void Receive(FeedbackKind kind,PilotSide side,float strength)
  {
   if(session.NetworkActive){if(kind==FeedbackKind.Hit&&(int)side!=session.LocalSide)return;queue.Queue(session.LocalSide,kind,Time.unscaledTime);}
   else if(kind==FeedbackKind.Combo||kind==FeedbackKind.Boost||kind==FeedbackKind.Damage){queue.Queue(0,kind,Time.unscaledTime);queue.Queue(1,kind,Time.unscaledTime);}
   else queue.Queue((int)side,kind,Time.unscaledTime);
  }
  void LateUpdate()
  {
   var online=GetComponent<IntegratedOnlineState>();if(session.NetworkActive&&online&&epoch!=online.State.epoch){epoch=online.State.epoch;ResetFeedback();}
   queue.interval=minimumInterval;bool sound=false;
   for(int hand=0;hand<2;hand++)if(queue.Take(hand,Time.unscaledTime,out var pulse))
   {
    var profile=pulse.kind==FeedbackKind.Hit?hitImpulse:pulse.kind==FeedbackKind.Guard?guardImpulse:pulse.kind==FeedbackKind.Damage?damageImpulse:pulse.kind==FeedbackKind.Combo?comboImpulse:boostImpulse;
    pulse.amplitude=Mathf.Clamp01(profile.x);pulse.duration=Mathf.Clamp(profile.y,0,1);
    var device=InputDevices.GetDeviceAtXRNode(hand==0?XRNode.LeftHand:XRNode.RightHand);
    if(device.isValid&&device.TryGetHapticCapabilities(out var caps)&&caps.supportsImpulse&&caps.numChannels>0&&device.SendHapticImpulse(0,pulse.amplitude,pulse.duration))Delivered++;else Unsupported++;
    var clip=pulse.kind==FeedbackKind.Hit?hit:pulse.kind==FeedbackKind.Guard?guard:pulse.kind==FeedbackKind.Damage?damage:pulse.kind==FeedbackKind.Boost?boost:null;
    if(!sound&&clip&&audioSource){audioSource.PlayOneShot(clip);sound=true;}
   }
  }
 }
}
