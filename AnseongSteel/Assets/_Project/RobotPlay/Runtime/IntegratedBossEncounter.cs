using System.Linq;
using AnseongSteel.Bosses;
using UnityEngine;
namespace AnseongSteel.RobotPlay
{
    [DefaultExecutionOrder(400)]
    public sealed class IntegratedBossEncounter : MonoBehaviour
    {
        public enum AttackKind { Normal, Heavy, Unguardable }
        public enum Beat { Idle, Warning, Punch, Recovery }
        public RobotPlaySession session;
        public BossAnimationDriver driver;
        public float telegraphSeconds=1.5f,recoverySeconds=.8f,sidestepDistance=.5f;
        public int normalDamage=12,heavyDamage=20,unguardableDamage=16;
        public Beat Phase { get; private set; }
        public AttackKind Kind { get; private set; }
        public int ImpactCount { get; private set; }
        public int DodgeCount { get; private set; }
        public int GuardCount { get; private set; }
        public int DamageCount { get; private set; }
        public string Cue => Phase==Beat.Warning||Phase==Beat.Punch?Kind==AttackKind.Unguardable?"UNBLOCKABLE / MATCH STICKS L OR R":Kind==AttackKind.Heavy?"HEAVY / BOTH GUARDS":"NORMAL / RIGHT GUARD":Phase==Beat.Recovery?"RECOVERY / COUNTER":"READY";
        public bool Active { get; private set; }
        float deadline;
        int attacks;
        void Start(){driver.attack.ImpactChecked+=Impact;}
        void OnDestroy(){if(driver)driver.attack.ImpactChecked-=Impact;}
        public void Begin(float grace=3)
        {
            Active=true;Phase=Beat.Idle;deadline=Time.time+grace;attacks=ImpactCount=DodgeCount=GuardCount=DamageCount=0;
            session.trainingWindow=false;session.SetBossWindow(true,1,false);
        }
        public void Stop()
        {
            Active=false;Phase=Beat.Idle;if(driver)driver.ResetMotion();
        }
        void Update()
        {
            if(!Active||!session.Authority||session.Combat.BossHealth<=0||session.Combat.RobotHealth<=0)return;
            if(Phase==Beat.Punch){if(!driver.IsAttacking){Phase=Beat.Recovery;deadline=Time.time+recoverySeconds;session.SetBossWindow(true,1,false);}return;}
            if(Phase==Beat.Recovery&&driver.IsAttacking)return;
            if(Time.time<deadline)return;
            if(Phase==Beat.Warning)
            {if(driver.TryPunch())Phase=Beat.Punch;else{Phase=Beat.Idle;deadline=Time.time+.2f;}return;}
            Kind=(AttackKind)(attacks++%3);Phase=Beat.Warning;deadline=Time.time+telegraphSeconds;
            session.SetBossWindow(false,1,Kind==AttackKind.Heavy);
        }
        void Impact(Collider[] candidates)
        {
            if(!Active||!session.Authority||Phase!=Beat.Punch||session.Combat.BossHealth<=0||session.Combat.RobotHealth<=0)return;
            ImpactCount++;
            bool touched=candidates.Any(c=>c&&c.transform.IsChildOf(session.robotRoot));
            bool sidestep=Kind==AttackKind.Unguardable&&Mathf.Abs(session.robotRoot.position.x)>=sidestepDistance;
            if(!touched||sidestep)DodgeCount++;
            else
            {
                int damage=Kind==AttackKind.Normal?normalDamage:Kind==AttackKind.Heavy?heavyDamage:unguardableDamage;
                bool guarded=session.Combat.ReceiveAttack(1,Kind==AttackKind.Heavy,damage,Kind!=AttackKind.Unguardable);
                if(guarded)GuardCount++;else DamageCount++;
            }
            Phase=Beat.Recovery;deadline=Time.time+recoverySeconds;
            // The authored clip's impact event opens a real counterattack window.
            session.SetBossWindow(true,1,false);
        }
    }
}