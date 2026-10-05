using System;
using UnityEngine;

namespace AnseongSteel.RobotPlay
{
    public enum PilotSide { Left, Right }
    public enum Strike { None, Jab, Hook }
    public enum WeaponMode { Fist, Sword, Shield, Ranged }
    public enum FeedbackKind { Hit, Guard, Damage, Boost, Combo, Defeated }
    public interface IRobotFeedback { void Receive(FeedbackKind kind, PilotSide side, float strength); }

    [Serializable]
    public sealed class CombatTuning
    {
        public float minimumSpeed = .65f, minimumTravel = .12f, strokeSeconds = .7f;
        public float rearmRadius = .22f, rearmSeconds = .12f, guardHeight = .4f, hookWindup = .45f;
        public float comboSeconds = 2.4f, minimumComboGap = .15f, boostMultiplier = 2f;
        public int jabDamage = 8, hookDamage = 20, bossHealth = 160, robotHealth = 100;
        public bool standaloneHookDamage;
    }

    // Pure state machine. All times and poses are supplied by the authoritative simulation.
    // Returning home (with dwell) is required after every attempt, including a miss.
    public sealed class RobotCombat
    {
        public sealed class Arm
        {
            public Vector3 position, previous;
            public bool initialized, armed, guarding, boosted, finishing, hookPrepared;
            public float homeSince = -1, expires, lastSample = -1;
            public Strike strike;
        }
        public readonly Arm[] arms = { new Arm(), new Arm() };
        public readonly CombatTuning tuning;
        public int BossHealth { get; private set; }
        public int RobotHealth { get; private set; }
        public int ComboStep { get; private set; }
        public int NextSide { get; private set; } = -1;
        public int HitCount { get; private set; }
        public float ComboDeadline { get; private set; }
        int firstSide;
        float lastComboHit;
        public event Action<FeedbackKind, PilotSide, float> Feedback;
        public RobotCombat(CombatTuning config) { tuning = config; Reset(); }
        public void Reset()
        {
            BossHealth = tuning.bossHealth; RobotHealth = tuning.robotHealth; HitCount = 0;
            ClearCombo(); for (int i=0;i<2;i++) arms[i] = new Arm();
        }
        public void Tick(float now)
        {
            if (ComboStep > 0 && now > ComboDeadline) ClearCombo();
            foreach (var arm in arms) if (arm.strike != Strike.None && now > arm.expires) EndStroke(arm);
        }
        public void Invalidate(int side)
        {
            if (side<0 || side>1) return;
            arms[side]=new Arm(); ClearCombo();
        }
        public void Sample(int side, Vector3 offset, bool boostPressed, float now)
        {
            if (side<0 || side>1 || !Finite(offset) || BossHealth<=0 || RobotHealth<=0) return;
            Tick(now); var arm=arms[side];
            if (arm.initialized && now<=arm.lastSample) return;
            float dt=arm.initialized ? now-arm.lastSample : 0;
            arm.lastSample=now; arm.position=offset;
            if (!arm.initialized || dt>.25f)
            {
                arm.initialized=true; arm.previous=offset; arm.armed=false; arm.homeSince=-1;
                arm.guarding=false; EndStroke(arm); return;
            }
            Vector3 velocity=(offset-arm.previous)/dt; arm.previous=offset;
            arm.guarding=offset.y>=tuning.guardHeight && arm.strike==Strike.None;
            if (offset.magnitude<tuning.rearmRadius && arm.strike==Strike.None)
            {
                arm.hookPrepared=false;
                if (arm.homeSince<0) arm.homeSince=now;
                if (now-arm.homeSince>=tuning.rearmSeconds) arm.armed=true;
            }
            else arm.homeSince=-1;
            if(offset.x*(side==0?-1:1)>tuning.hookWindup && offset.z>.25f)arm.hookPrepared=true;
            float inward=velocity.x*(side==0?1:-1);
            if (arm.armed && !arm.guarding && arm.strike==Strike.None && offset.magnitude>=tuning.minimumTravel)
            {
                Strike kind=Strike.None;
                if (inward>=tuning.minimumSpeed && inward>Mathf.Abs(velocity.z)*1.15f && offset.z>.25f) kind=Strike.Hook;
                else if (!arm.hookPrepared && velocity.z>=tuning.minimumSpeed && velocity.z>Mathf.Abs(velocity.x)*1.15f && offset.z>Mathf.Abs(offset.x)*1.25f) kind=Strike.Jab;
                if (kind!=Strike.None)
                {
                    arm.strike=kind; arm.armed=false; arm.expires=now+tuning.strokeSeconds;
                    arm.finishing=kind==Strike.Hook && ComboStep==2 && NextSide==side;
                }
            }
            // A held button before a hook cannot preload a boost. The input adapter sends edges.
            if (boostPressed && arm.strike==Strike.Hook && arm.finishing && ComboStep==2 && NextSide==side && !arm.boosted)
            {
                arm.boosted=true; Feedback?.Invoke(FeedbackKind.Boost,(PilotSide)side,1);
            }
        }
        public bool Hit(int side, float now, bool vulnerable)
        {
            if (side<0 || side>1 || BossHealth<=0 || RobotHealth<=0) return false;
            Tick(now); var arm=arms[side]; if (arm.strike==Strike.None) return false;
            Strike kind=arm.strike; bool finish=arm.finishing && ComboStep==2 && NextSide==side;
            bool boosted=arm.boosted; EndStroke(arm);
            if (!vulnerable) { ClearCombo(); return false; }
            int damage=0;
            if (kind==Strike.Jab)
            {
                damage=tuning.jabDamage;
                if (ComboStep==1 && NextSide==side && now-lastComboHit>=tuning.minimumComboGap) { ComboStep=2; NextSide=firstSide; }
                else { firstSide=side; ComboStep=1; NextSide=1-side; }
                lastComboHit=now;
                ComboDeadline=now+tuning.comboSeconds;
            }
            else if (finish)
            {
                damage=Mathf.RoundToInt(tuning.hookDamage*(boosted?tuning.boostMultiplier:1));
                ClearCombo(); Feedback?.Invoke(FeedbackKind.Combo,(PilotSide)side,damage);
            }
            else { if (tuning.standaloneHookDamage) damage=tuning.hookDamage; ClearCombo(); }
            if (damage<=0) return false;
            BossHealth=Mathf.Max(0,BossHealth-damage); HitCount++;
            Feedback?.Invoke(FeedbackKind.Hit,(PilotSide)side,damage);
            if (BossHealth==0) Feedback?.Invoke(FeedbackKind.Defeated,(PilotSide)side,1);
            return true;
        }
        public bool ReceiveAttack(int side, bool both, int damage)
        {
            if (RobotHealth<=0 || BossHealth<=0) return false;
            bool blocked=both ? arms[0].guarding&&arms[1].guarding : arms[side].guarding;
            if (!blocked) RobotHealth=Mathf.Max(0,RobotHealth-damage);
            Feedback?.Invoke(blocked?FeedbackKind.Guard:FeedbackKind.Damage,(PilotSide)side,damage);
            return blocked;
        }
        public static bool Finite(Vector3 p) => float.IsFinite(p.x)&&float.IsFinite(p.y)&&float.IsFinite(p.z);
        void ClearCombo() { ComboStep=0; NextSide=-1; ComboDeadline=0; }
        static void EndStroke(Arm arm) { arm.strike=Strike.None; arm.boosted=false; arm.finishing=false; arm.hookPrepared=false; }
    }
}
