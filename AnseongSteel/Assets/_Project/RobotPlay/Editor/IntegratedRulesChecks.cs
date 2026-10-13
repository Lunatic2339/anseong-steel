using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
namespace AnseongSteel.RobotPlay.Editor
{
    public static class IntegratedRulesChecks
    {
        static readonly List<string> checks=new List<string>();
        static void Check(bool ok,string text){if(!ok)throw new Exception(text);checks.Add("PASS: "+text);}
        static RobotCombat Fresh()=>new RobotCombat(new CombatTuning{requiresAttackButton=true,strictComboRules=true,postContactBoost=true,standaloneHookDamage=true});
        static void Home(RobotCombat c,int side,ref float t){for(int i=0;i<50;i++){t+=.02f;c.Sample(side,Vector3.zero,false,t,false);}}
        static void Jab(RobotCombat c,int side,ref float t,bool hold=true){Home(c,side,ref t);for(int i=1;i<=10;i++){t+=.02f;c.Sample(side,new Vector3(0,0,.06f*i),false,t,hold);}}
        static void Hook(RobotCombat c,int side,ref float t,bool preboost=false){Home(c,side,ref t);for(int i=1;i<=24;i++){t+=.02f;c.Sample(side,RobotPilotInput.DesktopPose(side,i*.02f,true),preboost&&i==24,t,true);}}
        static RobotCombat Combo(ref float t,int side,bool preboost=false){var c=Fresh();Jab(c,side,ref t);c.Hit(side,t,true);Jab(c,1-side,ref t);c.Hit(1-side,t,true);Hook(c,side,ref t,preboost);c.Hit(side,t,true);return c;}
        [MenuItem("Anseong Steel/Robot Play/Run integrated game rules checks")]
        public static void Run()
        {
            checks.Clear();float t=0;var c=Fresh();Jab(c,0,ref t,false);
            Check(!c.Hit(0,t,true)&&c.BossHealth==160,"movement plus contact without attack hold does no damage");
            Jab(c,0,ref t);Check(c.Hit(0,t,true)&&c.BossHealth==152,"attack hold plus valid trajectory and contact damages");
            Check(!c.Hit(0,t+.01f,true)&&c.HitCount==1,"held attack cannot duplicate contact damage");
            t=0;c=Combo(ref t,0,true);Check(c.BossHealth==124,"L R L combo keeps ordinary hook damage when precontact boost is ignored");
            t+=.02f;c.Sample(0,RobotPilotInput.DesktopPose(0,.48f,true),true,t,true);Check(c.BossHealth==104&&c.HitCount==3,"postcontact boost adds bonus once without a fourth hit");
            t+=.02f;c.Sample(0,Vector3.zero,true,t,true);Check(c.BossHealth==104,"repeat boost edge cannot duplicate bonus");
            t=0;c=Combo(ref t,0);t+=.02f;c.Sample(0,Vector3.zero,true,t,false);Check(c.BossHealth==124,"releasing grip cancels post-hit boost without losing ordinary hook damage");
            t+=.02f;c.Sample(0,Vector3.zero,true,t,true);Check(c.BossHealth==124,"regripping cannot resurrect cancelled boost opportunity");
            t=0;c=Combo(ref t,1);Check(c.BossHealth==124,"R L R combo supports the opposite start side");
            t+=.7f;c.Sample(1,Vector3.zero,true,t,false);Check(c.BossHealth==124,"missed boost opportunity preserves ordinary final hook damage");
            t=0;c=Fresh();Jab(c,0,ref t);c.Hit(0,t,true);Jab(c,0,ref t);c.Hit(0,t,true);
            Check(c.ComboStep==0&&c.BossHealth==144,"wrong-side hit ends combo while keeping earned damage");
            t=0;c=Fresh();Jab(c,0,ref t);c.Hit(0,t,true);Jab(c,1,ref t,false);
            for(int i=0;i<12;i++){t+=.02f;c.Sample(1,new Vector3(0,0,.6f),false,t,false);}
            Check(c.ComboStep==1,"nonattack arm motion does not break the sequence");
            t=0;c=Fresh();Jab(c,0,ref t);c.Hit(0,t,true);Home(c,1,ref t);
            for(int i=0;i<15;i++){t+=.02f;c.Sample(1,new Vector3(0,.65f,0),false,t,true);}
            Check(c.ComboStep==1&&c.arms[1].guarding,"guard pose is not an out-of-order attack even with grip held");
            Check(c.ReceiveAttack(1,false,12)&&c.ComboStep==1&&c.RobotHealth==100,"correct guard deals zero damage and preserves combo");
            c.Tick(t+3);Check(c.ComboStep==0,"guard does not pause the combo deadline");
            t=0;c=Fresh();Jab(c,0,ref t);c.Hit(0,t,true);Jab(c,1,ref t);
            for(int i=0;i<12;i++){t+=.02f;c.Sample(1,new Vector3(0,0,.6f),false,t,true);}
            Check(c.ComboStep==0&&c.BossHealth==152,"a held-button punch miss ends combo without reverting damage");
            t=0;c=Fresh();Jab(c,0,ref t);c.Hit(0,t,true);c.ReceiveAttack(1,false,12);
            Check(c.ComboStep==0&&c.RobotHealth==88&&c.BossHealth==152,"incoming damage ends combo and keeps previous outgoing damage");
            c=Fresh();c.arms[0].guarding=true;Check(!c.ReceiveAttack(1,true,20)&&c.RobotHealth==80,"one-arm heavy guard fails without partial damage reduction");
            c=Fresh();c.arms[0].guarding=c.arms[1].guarding=true;
            Check(c.ReceiveAttack(1,true,20)&&c.RobotHealth==100,"two-arm heavy guard blocks all damage");
            Check(!c.ReceiveAttack(1,false,16,false)&&c.RobotHealth==84,"unguardable attack ignores raised arms");
            Check(RobotPlaySession.CooperativeMove(Vector2.right,Vector2.zero)==Vector2.zero,"one pilot alone cannot move robot");
            Check(RobotPlaySession.CooperativeMove(Vector2.right,Vector2.left)==Vector2.zero,"opposed pilot directions stop movement");
            Check(RobotPlaySession.CooperativeMove(Vector2.right,Vector2.right)==Vector2.right,"matching maintained inputs move even without simultaneous starts");
            c=Fresh();c.ReceiveAttack(1,false,100);t=0;Jab(c,0,ref t);Check(!c.Hit(0,t,true)&&c.BossHealth==160,"defeat stops outgoing damage");
            c.Reset();Check(c.RobotHealth==100&&c.BossHealth==160&&c.ComboStep==0&&c.HitCount==0,"restart resets health combo and hit count");
            c=new RobotCombat(new CombatTuning{bossHealth=8,requiresAttackButton=true});t=0;Jab(c,0,ref t);c.Hit(0,t,true);c.ReceiveAttack(1,false,100);
            Check(c.BossHealth==0&&c.RobotHealth==100,"victory stops incoming damage");
            t=0;c=Fresh();Hook(c,1,ref t);c.Hit(1,t,true);Home(c,1,ref t);t+=.02f;c.Sample(1,new Vector3(0,0,.12f),false,t,true);
            Check(c.arms[1].trajectory.Phase==AnseongSteel.PlayerMotion.MotionPhase.Moving&&c.arms[1].trajectory.Candidate.kind==AnseongSteel.PlayerMotion.ObservedPunch.Unknown
                &&c.arms[1].trajectory.LastCompleted.kind==AnseongSteel.PlayerMotion.ObservedPunch.Hook,"new stroke clears stale candidate while preserving completed hook history");
            Directory.CreateDirectory("RobotPlayValidation");File.WriteAllLines("RobotPlayValidation/integrated-rules-checks.txt",checks);Debug.Log("INTEGRATED_RULES_PASS "+checks.Count);
        }
    }
}