using System;
using UnityEngine;
namespace AnseongSteel.Bosses
{
    public enum BossAttackKind { HeavyPunch, SwordSlash, OverheadSmash, AlternatingCombo, MissileBarrage, Grab, Feint, Shockwave, LaserSweep, EMP, SensorJam, VisionDisruption, ArmLock, WeaponBreak }
    public enum BossCombatState { Idle, Windup, Active, Recovery, Holding, CoreExposed, Stunned, Dead }
    [Flags] public enum BossArm { None=0, Left=1, Right=2, Both=3 }
    [Flags] public enum BossStatus { None=0, HudDisabled=1, LeftSensorHidden=2, RightVisionDistorted=4, LeftArmDisabled=8, RightArmDisabled=16, WeaponsDisabled=32 }
    public enum BossHitResult { Miss, Hit, Blocked, Parried, Grabbed }
    public struct BossHit
    {
        public BossAttackKind kind;
        public float damage;
        public BossArm requiredGuard;
        public BossArm grabbedArm;
        public BossStatus status;
        public float statusSeconds;
        public Vector3 point;
        public int sequence;
    }
    [CreateAssetMenu(menuName="Boss Prototype/Attack Definition")]
    public sealed class BossAttackDefinition : ScriptableObject
    {
        public BossAttackKind kind;
        [Min(.05f)] public float windup=.8f, active=.5f, recovery=.7f;
        [Min(0)] public float cooldown=1f;
        [Min(.1f)] public float range=2.3f;
        [Min(.1f)] public float preferredDistance=1.05f;
        [Range(1,180)] public float facingAngle=75f;
        [Min(0)] public float damage=20f;
        public BossArm requiredGuard=BossArm.Both;
        public BossStatus status;
        public float statusSeconds=4f;
        [Min(.02f)] public float hitRadius=.18f;
        [Min(0)] public float coreExposureSeconds;
        [Min(.1f)] public float holdSeconds=4f;
        [Range(1,12)] public int projectileCount=6;
        [Min(.1f)] public float projectileSpeed=3f;
        [Tooltip("Optional. Without a clip, timing and debug hit volumes run without a fabricated body animation.")]
        public AnimationClip clip;
        [Tooltip("Baked handle roll in degrees, keyed by normalized clip time to keep the edge along the swing.")]
        public AnimationCurve bladeRoll = new AnimationCurve();
        [Tooltip("Normalized times within the active interval, one per combo strike.")]
        public float[] strikeTimes = { .2f, .63f };
        [Range(0,1)] public float windupEnd=.35f, activeEnd=.65f;
        public string StateName => "Combat_"+kind;
        public bool IsSword => kind==BossAttackKind.SwordSlash||kind==BossAttackKind.OverheadSmash||kind==BossAttackKind.Feint||kind==BossAttackKind.WeaponBreak||kind==BossAttackKind.Shockwave;
        public bool IsValid => windup>0&&active>0&&recovery>0&&range>0&&hitRadius>0&&
            !float.IsNaN(windup+active+recovery+range+damage+cooldown)&&!float.IsInfinity(windup+active+recovery+range+damage+cooldown)&&
            damage>=0&&cooldown>=0&&windupEnd>0&&activeEnd>windupEnd&&activeEnd<1;
    }
}

