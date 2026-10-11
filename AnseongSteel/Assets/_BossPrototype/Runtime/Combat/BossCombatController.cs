using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace AnseongSteel.Bosses
{
    [DefaultExecutionOrder(300)]
    public sealed class BossCombatController : MonoBehaviour
    {
        public BossAnimationDriver driver;
        public BossSwordAttachment sword;
        public BossChestLaser laser;
        public BossCombatTarget target;
        public BossAttackDefinition[] attacks;
        public Material telegraphMaterial,missileMaterial;
        public LayerMask hitLayers=~0;
        public bool showDebugVolumes=true;
        public float maximumHealth=500f;
        public float exposedDamageMultiplier=3f;
        public BossCombatState State {get;private set;}
        public BossAttackKind CurrentAttack {get;private set;}
        public float Health {get;private set;}
        public string LastMessage {get;private set;}="Ready";
        public bool IsBusy=>State!=BossCombatState.Idle;
        public bool CoreIsExposed=>State==BossCombatState.CoreExposed;
        public bool UsingPlaceholder=>active!=null&&!clipPlaying&&!legacy&&CurrentAttack!=BossAttackKind.LaserSweep&&CurrentAttack!=BossAttackKind.MissileBarrage;
        public int Generation {get;private set;}
        public int Interceptions {get;private set;}
        public event Action<BossCombatState> StateChanged;
        public event Action<BossHit,BossHitResult> HitResolved;
        public event Action<bool> CoreExposureChanged;
        public event Action<float> HealthChanged;
        public event Action Died;
        readonly Dictionary<BossAttackKind,float> readyAt=new Dictionary<BossAttackKind,float>();
        readonly HashSet<BossCombatTarget> struck=new HashSet<BossCombatTarget>();
        readonly List<BossMissile> missiles=new List<BossMissile>();
        BossAttackDefinition active;
        BossAttackPattern activePattern;
        public BossAttackDefinition Definition=>active;
        public bool HasAttackClip=>clipPlaying;
        LineRenderer telegraph;
        float started,windup,activeTime,recovery,deadline;
        int sequence=-1;
        bool clipPlaying,legacy,previousValid;
        Vector3 previousA,previousB;
        public IReadOnlyList<BossMissile> Missiles=>missiles;
        void Awake()
        {
            Health=maximumHealth;
            driver.MotionReset+=CancelAttack;driver.attack.ImpactChecked+=OnLegacyPunch;
            var go=new GameObject("Combat_Debug_Volume");go.transform.SetParent(transform,false);
            telegraph=go.AddComponent<LineRenderer>();telegraph.useWorldSpace=true;telegraph.sharedMaterial=telegraphMaterial;
            telegraph.startWidth=telegraph.endWidth=.025f;telegraph.enabled=false;
        }
        public bool TryAttack(BossAttackKind kind)
        {
            if(!isActiveAndEnabled||IsBusy||driver.IsAttacking||driver.IsTurning)return Reject("Busy");
            if(!target||target.Health<=0)return Reject("No living target");
            var definition=attacks.FirstOrDefault(a=>a&&a.kind==kind);
            if(!definition||!definition.IsValid)return Reject("Invalid attack definition");
            if(readyAt.TryGetValue(kind,out var time)&&Time.time<time)return Reject("Cooling down");
            var delta=target.transform.position-transform.position;delta.y=0;
            if(delta.magnitude>definition.range)return Reject("Out of range: approach target first");
            bool radial=kind==BossAttackKind.EMP||kind==BossAttackKind.SensorJam||kind==BossAttackKind.VisionDisruption||kind==BossAttackKind.Shockwave;
            if(!radial&&Vector3.Angle(transform.forward,delta)>definition.facingAngle)return Reject("Face target first");
            var pattern=GetComponents<BossAttackPattern>().FirstOrDefault(p=>p.Kind==kind&&p.enabled);
            if(!pattern)return Reject("Attack module missing or disabled");
            clipPlaying=definition.clip&&driver.animator.HasState(0,Animator.StringToHash(definition.StateName));
            legacy=kind==BossAttackKind.HeavyPunch;
            if(legacy)
            {
                sword?.Sheathe();
                if(!driver.TryPunch())return Reject("Punch unavailable");
            }
            else if(!driver.TryBeginCombatAction(clipPlaying?definition.StateName:null))return Reject("Motion is busy");
            activePattern=pattern;activePattern.Begin();
            active=definition;CurrentAttack=kind;started=Time.time;struck.Clear();sequence=-1;previousValid=false;
            windup=clipPlaying?definition.clip.length*definition.windupEnd:definition.windup;
            activeTime=clipPlaying?definition.clip.length*(definition.activeEnd-definition.windupEnd):definition.active;
            recovery=clipPlaying?definition.clip.length*(1-definition.activeEnd):definition.recovery;
            if(definition.IsSword)sword?.Draw();else sword?.Sheathe();
            SetState(BossCombatState.Windup);
            LastMessage=kind+(UsingPlaceholder?" [timed preview; clip missing]":"");
            return true;
        }
        public bool TrySwordSlash()=>TryAttack(BossAttackKind.SwordSlash);
        public bool TryOverheadSmash()=>TryAttack(BossAttackKind.OverheadSmash);
        public bool TryMissileBarrage()=>TryAttack(BossAttackKind.MissileBarrage);
        public bool TryGrab()=>TryAttack(BossAttackKind.Grab);
        bool Reject(string message){LastMessage=message;return false;}
        void LateUpdate()
        {
            missiles.RemoveAll(m=>!m||m.Resolved);
            if(State==BossCombatState.Dead)return;
            if(State==BossCombatState.Holding)
            {if(!target||target.GrabbedArm==BossArm.None||Time.time>=deadline){target?.ReleaseGrab();Complete();}return;}
            if(State==BossCombatState.CoreExposed||State==BossCombatState.Stunned)
            {
                if(Time.time>=deadline){if(CoreIsExposed)CoreExposureChanged?.Invoke(false);driver.EndCombatAction();SetState(BossCombatState.Idle);}return;
            }
            if(active==null)return;
            if(!target||!driver.isActiveAndEnabled||!driver.animator.isActiveAndEnabled){CancelAttack();return;}
            if(legacy)
            {if(!driver.IsAttacking)Complete();return;}
            float elapsed=Time.time-started;
            if(elapsed<windup){if(CurrentAttack==BossAttackKind.LaserSweep)laser?.Charge(Mathf.Clamp01(elapsed/windup));DrawVolume(0,false);return;}
            if(elapsed<windup+activeTime)
            {
                SetState(BossCombatState.Active);
                float t=Mathf.Clamp01((elapsed-windup)/activeTime);
                TickActive(t);if(active!=null)DrawVolume(t,true);return;
            }
            // Preserve an impact even when a slow frame crosses the entire active interval.
            if(State==BossCombatState.Windup||State==BossCombatState.Active) {TickActive(1);if(active==null)return;}
            SetState(BossCombatState.Recovery);if(telegraph)telegraph.enabled=false;
            if(elapsed>=windup+activeTime+recovery)
            {
                if(CurrentAttack==BossAttackKind.Grab&&target.GrabbedArm!=BossArm.None){SetState(BossCombatState.Holding);deadline=Time.time+active.holdSeconds;}
                else Complete();
            }
        }
        void TickActive(float t) {if(active!=null&&activePattern)activePattern.Tick(this,t);}
        public Vector3 HandPoint(BossArm arm) { var hand=driver.animator.GetComponentsInChildren<Transform>().First(t=>t.name==(arm==BossArm.Left?"mixamorig:LeftHandMiddle1":"mixamorig:RightHandMiddle1")); return hand.position; }
        public void BeginStrike(int index){sequence=index;struck.Clear();previousValid=false;}
        public void Sweep(Vector3 a,Vector3 b)
        {
            if(!active)return;
            float radius=clipPlaying?active.hitRadius:Mathf.Max(active.hitRadius,.28f);
            Query(a,b,radius);if(!active)return;
            if(previousValid){Query(previousA,a,radius);if(!active)return;Query(previousB,b,radius);if(!active)return;}
            previousA=a;previousB=b;previousValid=true;
        }
        public void HitExpandingRing(float radius)
        {
            Physics.SyncTransforms();
            foreach(var c in Physics.OverlapSphere(transform.position,active.range+1,hitLayers,QueryTriggerInteraction.Ignore))
            {
                var receiver=c.GetComponentInParent<BossCombatTarget>();if(!receiver)continue;
                var delta=receiver.transform.position-transform.position;delta.y=0;
                if(delta.magnitude<=radius+.25f)Resolve(receiver,MakeHit(c.ClosestPoint(transform.position)));
                if(!active)return;
            }
        }        public void Query(Vector3 a,Vector3 b,float radius)
        {
            Physics.SyncTransforms();
            foreach(var c in Physics.OverlapCapsule(a,b,radius,hitLayers,QueryTriggerInteraction.Ignore))
            {
                if(c.transform.IsChildOf(transform))continue;
                var receiver=c.GetComponentInParent<BossCombatTarget>();
                if(receiver)Resolve(receiver,MakeHit(c.ClosestPoint(b)));
                if(active==null)return;
            }
        }
        BossHit MakeHit(Vector3 point)=>new BossHit {kind=CurrentAttack,damage=active.damage,requiredGuard=CurrentAttack==BossAttackKind.AlternatingCombo?(sequence%2==0?BossArm.Right:BossArm.Left):active.requiredGuard,grabbedArm=BossArm.Left,point=point,sequence=Mathf.Max(0,sequence),status=active.status,statusSeconds=active.statusSeconds};
        void Resolve(BossCombatTarget receiver,BossHit hit)
        {
            if(!struck.Add(receiver))return;
            var result=receiver.Receive(hit);HitResolved?.Invoke(hit,result);LastMessage=hit.kind+": "+result;
            if(result==BossHitResult.Parried) {Stun(1.5f);}
        }
        void OnLegacyPunch(Collider[] candidates)
        {
            var definition=attacks.FirstOrDefault(a=>a&&a.kind==BossAttackKind.HeavyPunch);if(!definition)return;
            var receivers=new HashSet<BossCombatTarget>();
            foreach(var c in candidates)
            {
                var receiver=c.GetComponentInParent<BossCombatTarget>();if(!receiver||!receivers.Add(receiver))continue;
                var hit=new BossHit {kind=BossAttackKind.HeavyPunch,damage=definition.damage,requiredGuard=definition.requiredGuard,point=driver.hitPoint.position};
                var result=receiver.Receive(hit);HitResolved?.Invoke(hit,result);LastMessage="HeavyPunch: "+result;
                if(result==BossHitResult.Parried){Stun(1.5f);return;}
            }
            if(legacy&&active!=null)SetState(BossCombatState.Recovery);
        }
        public void SpawnMissile(int index)
        {
            if(missiles.Count(m=>m&&!m.Resolved)>=24)return;
            var go=GameObject.CreatePrimitive(PrimitiveType.Sphere);go.name="BossMissile_"+index;
            var collider=go.GetComponent<Collider>();collider.enabled=false;Destroy(collider);
            go.transform.position=transform.TransformPoint(new Vector3(index%2==0?-.65f:.65f,2.25f+.08f*(index/2),-.1f));go.transform.localScale=Vector3.one*.15f;
            go.GetComponent<Renderer>().sharedMaterial=missileMaterial;
            var missile=go.AddComponent<BossMissile>();missiles.Add(missile);
            var aim=target.GetComponent<Collider>().bounds.center+Vector3.up*.5f;
            missile.Initialize(this,aim,active.projectileSpeed,MakeHit(aim),Generation,hitLayers);
        }
        public void ResolveProjectile(BossCombatTarget receiver,BossHit hit)
        {var result=receiver.Receive(hit);HitResolved?.Invoke(hit,result);LastMessage="Missile: "+result;}
        public void ReportInterception(){Interceptions++;LastMessage="Missile intercepted";}
        public void InterceptAll(){foreach(var missile in missiles.ToArray())if(missile)missile.Intercept();}
        void Complete()
        {
            float exposure=active?active.coreExposureSeconds:0;
            if(active)readyAt[active.kind]=Time.time+active.cooldown;
            active=null;legacy=false;previousValid=false;
            driver.EndCombatAction();if(telegraph)telegraph.enabled=false;SetState(BossCombatState.Idle);
            if(exposure>0)OpenCore(exposure);
        }
        public void CancelAttack()
        {
            laser?.Stop();
            Generation++;foreach(var m in missiles.ToArray())if(m)m.Cancel();missiles.Clear();
            if(CoreIsExposed)CoreExposureChanged?.Invoke(false);
            active=null;legacy=false;previousValid=false;struck.Clear();target?.ReleaseGrab();
            if(driver){driver.OnPunchFinished();driver.EndCombatAction();}
            if(telegraph)telegraph.enabled=false;
            if(State!=BossCombatState.Dead)SetState(BossCombatState.Idle);
        }
        public void Stun(float seconds)
        {if(Health<=0)return;CancelAttack();driver.TryBeginCombatAction(null);SetState(BossCombatState.Stunned);deadline=Time.time+Mathf.Max(.1f,seconds);}
        public void OpenCore(float seconds)
        {if(Health<=0)return;CancelAttack();driver.TryBeginCombatAction(null);SetState(BossCombatState.CoreExposed);deadline=Time.time+Mathf.Max(.1f,seconds);CoreExposureChanged?.Invoke(true);LastMessage="Core exposed: counterattack window";}
        public bool ReceivePlayerHit(float damage,BossArm arm)
        {
            if(Health<=0||damage<=0||float.IsNaN(damage)||float.IsInfinity(damage)||arm==BossArm.None||!target||!target.IsArmAvailable(arm))return false;
            bool release=target.GrabbedArm!=BossArm.None&&(arm&target.GrabbedArm)==0;
            Health=Mathf.Max(0,Health-damage*(CoreIsExposed?exposedDamageMultiplier:1));HealthChanged?.Invoke(Health);
            if(release){target.ReleaseGrab();if(State==BossCombatState.Holding)Complete();}
            if(Health<=0){CancelAttack();driver.TryBeginCombatAction(null);sword?.Sheathe();SetState(BossCombatState.Dead);Died?.Invoke();}
            return true;
        }
        public void ResetEncounter()
        {CancelAttack();driver.EndCombatAction();Health=maximumHealth;readyAt.Clear();Interceptions=0;sword?.Sheathe();target?.ResetTarget();SetState(BossCombatState.Idle);HealthChanged?.Invoke(Health);LastMessage="Ready";}
        void SetState(BossCombatState value){if(State==value)return;State=value;StateChanged?.Invoke(value);}
        void DrawVolume(float t,bool live)
        {
            if(!telegraph)return;
            if(CurrentAttack==BossAttackKind.LaserSweep&&laser){telegraph.enabled=false;return;}
            telegraph.enabled=showDebugVolumes;if(!showDebugVolumes)return;
            telegraph.startColor=telegraph.endColor=live?Color.red:Color.yellow;
            if(CurrentAttack==BossAttackKind.LaserSweep&&live&&previousValid){telegraph.positionCount=2;telegraph.SetPosition(0,previousA);telegraph.SetPosition(1,previousB);return;}
            telegraph.positionCount=33;
            float radius=CurrentAttack==BossAttackKind.Shockwave?Mathf.Lerp(.1f,active.range,t):active.range;
            for(int i=0;i<33;i++){float angle=i/32f*Mathf.PI*2;telegraph.SetPosition(i,transform.position+new Vector3(Mathf.Cos(angle)*radius,.04f,Mathf.Sin(angle)*radius));}
        }
        void OnDisable()=>CancelAttack();
        void OnDestroy(){if(driver){driver.MotionReset-=CancelAttack;if(driver.attack)driver.attack.ImpactChecked-=OnLegacyPunch;}}
    }
}



