using UnityEngine;
using UnityEngine.Rendering;
namespace AnseongSteel.RobotPlay
{
 [DefaultExecutionOrder(900)]
 public sealed class IntegratedPunchCue:MonoBehaviour
 {
  public float visualSlowSeconds=.22f,visualSpeed=.35f;
  [Header("Optional render-only boost reaction / provisional values")]
  public bool showBoostReaction=true;
  [Min(.05f)]public float reactionSeconds=.9f;
  [Range(0,2)]public float bossRetreat=.65f;
  [Range(0,.5f)]public float bossLift=.12f;
  [Range(0,30)]public float bossTilt=12;
  [Range(0,.15f)]public float armPush=.07f;
  public int Side {get;private set;}=-1;
  public int AcceptedReactions {get;private set;}
  public int RenderApplications {get;private set;}
  public float MaxBossOffset {get;private set;}
  public float MaxArmPush {get;private set;}
  public float MaxWristDeviation {get;private set;}
  public bool ReactionActive=>boostSide>=0&&Time.unscaledTime-boostBegan<reactionSeconds;
  RobotPlaySession session;IntegratedBossEncounter boss;
  float began=-100,boostBegan=-100;int previous=-1,boostSide=-1;uint epoch;
  bool drawing,wasNetwork;Vector3 bossPosition;Quaternion bossRotation;
  Transform visualBoss;MechaVRBossContact.Arm drawnArm;
  Quaternion upperRotation,forearmRotation,handRotation;
  void Awake(){session=GetComponent<RobotPlaySession>();boss=GetComponent<IntegratedBossEncounter>();}
  void OnEnable(){session.Feedback+=Receive;RenderPipelineManager.beginCameraRendering+=BeginCamera;RenderPipelineManager.endCameraRendering+=EndCamera;}
  void OnDisable(){session.Feedback-=Receive;RenderPipelineManager.beginCameraRendering-=BeginCamera;RenderPipelineManager.endCameraRendering-=EndCamera;ResetPresentation();}
  void Receive(FeedbackKind kind,PilotSide side,float strength)
  {
   // This event arrives through the existing server-only FeedbackRpc online.
   // A final hook alone, failed trigger or legacy precontact boost does not react.
   if(kind!=FeedbackKind.Boost||!session.tuning.postContactBoost||strength<=0||ReactionActive)return;
   boostSide=(int)side;boostBegan=Time.unscaledTime;AcceptedReactions++;
  }
  public void ResetPresentation()
  {
   Restore();boostSide=-1;boostBegan=began=-100;previous=Side=-1;
   AcceptedReactions=RenderApplications=0;MaxBossOffset=MaxArmPush=MaxWristDeviation=0;
   if(boss&&boss.driver)boss.driver.animator.speed=1;
  }
  void LateUpdate()
  {
   var online=GetComponent<IntegratedOnlineState>();
   if(session.NetworkActive!=wasNetwork||(session.NetworkActive&&online&&epoch!=online.State.epoch))
   {ResetPresentation();wasNetwork=session.NetworkActive;if(online)epoch=online.State.epoch;}
   Side=session.NetworkActive&&!session.Authority?(online.State.boostRemaining>0?online.State.boostSide:-1):session.Combat.BoostOpportunity(Time.time);
   if(Side>=0&&previous<0)began=Time.unscaledTime;previous=Side;
   // Preserve server trajectory, input and combo clocks; only the Animator slows.
   boss.driver.animator.speed=Side>=0&&Time.unscaledTime-began<visualSlowSeconds?visualSpeed:1;
  }
  public static float ReactionEnvelope(float age,float duration)
  {
   if(age<0||duration<=0||age>=duration)return 0;
   float t=age/duration;
   return t<.2f?Mathf.SmoothStep(0,1,t/.2f):1-Mathf.SmoothStep(0,1,(t-.2f)/.8f);
  }
  void BeginCamera(ScriptableRenderContext context,Camera camera)
  {
   if(drawing||!showBoostReaction||!ReactionActive||!session.target||!session.contact||!boss||!boss.driver)return;
   float weight=ReactionEnvelope(Time.unscaledTime-boostBegan,reactionSeconds);if(weight<=0)return;
   visualBoss=boss.driver.animator.transform;drawnArm=session.contact.arms[boostSide];
   if(!visualBoss||!drawnArm.upperArm||!drawnArm.forearm||!drawnArm.hand)return;
   // Never SyncTransforms here. These saved visual transforms exist only while
   // a camera renders; animation, physics, input and network poses stay untouched.
   drawing=true;bossPosition=visualBoss.localPosition;bossRotation=visualBoss.localRotation;
   upperRotation=drawnArm.upperArm.localRotation;forearmRotation=drawnArm.forearm.localRotation;handRotation=drawnArm.hand.localRotation;
   Vector3 away=session.robotRoot.forward;
   visualBoss.position+=away*(bossRetreat*weight)+Vector3.up*(bossLift*weight);
   visualBoss.rotation=Quaternion.AngleAxis(bossTilt*weight,session.robotRoot.right)*visualBoss.rotation;
   float push=PushArm(drawnArm,away*armPush*weight);
   MaxArmPush=Mathf.Max(MaxArmPush,push);MaxBossOffset=Mathf.Max(MaxBossOffset,new Vector2(bossRetreat,bossLift).magnitude*weight);
   var follower=drawnArm.upperArm.GetComponent<AnseongSteel.PlayerMotion.ControllerArmFollower>();
   if(follower&&follower.IsReady)MaxWristDeviation=Mathf.Max(MaxWristDeviation,Vector3.Angle(drawnArm.hand.position-drawnArm.forearm.position,follower.HandFrame*Vector3.forward));
   RenderApplications++;
  }
  static float PushArm(MechaVRBossContact.Arm arm,Vector3 push)
  {
   Vector3 shoulder=arm.upperArm.position,oldHand=arm.hand.position,oldElbow=arm.forearm.position;
   float upperLength=Vector3.Distance(shoulder,oldElbow),lowerLength=Vector3.Distance(oldElbow,oldHand);
   Vector3 requested=oldHand+push,axis=(requested-shoulder).normalized;
   float distance=Mathf.Clamp(Vector3.Distance(shoulder,requested),Mathf.Abs(upperLength-lowerLength)+.005f,upperLength+lowerLength-.005f);
   if(upperLength<.01f||lowerLength<.01f||axis.sqrMagnitude<.5f)return 0;
   Vector3 bend=Vector3.ProjectOnPlane(oldElbow-shoulder,axis).normalized;
   if(bend.sqrMagnitude<.5f)return 0;
   float along=(upperLength*upperLength-lowerLength*lowerLength+distance*distance)/(2*distance);
   Vector3 elbow=shoulder+axis*along+bend*Mathf.Sqrt(Mathf.Max(0,upperLength*upperLength-along*along));
   Quaternion locked=arm.hand.localRotation;
   arm.upperArm.rotation=Quaternion.FromToRotation(arm.forearm.position-shoulder,elbow-shoulder)*arm.upperArm.rotation;
   arm.forearm.rotation=Quaternion.FromToRotation(arm.hand.position-arm.forearm.position,shoulder+axis*distance-arm.forearm.position)*arm.forearm.rotation;
   arm.hand.localRotation=locked;
   return Vector3.Distance(oldHand,arm.hand.position);
  }
  void EndCamera(ScriptableRenderContext context,Camera camera)=>Restore();
  void Restore()
  {
   if(!drawing)return;drawing=false;
   if(visualBoss){visualBoss.localPosition=bossPosition;visualBoss.localRotation=bossRotation;}
   if(drawnArm!=null){drawnArm.upperArm.localRotation=upperRotation;drawnArm.forearm.localRotation=forearmRotation;drawnArm.hand.localRotation=handRotation;}
  }
 }
}
