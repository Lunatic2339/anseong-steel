using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AnseongSteel.RobotPlay.Editor
{
    public static class RobotCombatChecks
    {
        static readonly List<string> results=new List<string>();
        static void Check(bool ok,string name){if(!ok)throw new Exception("FAIL: "+name);results.Add("PASS: "+name);}
        static RobotCombat Fresh()=>new RobotCombat(new CombatTuning());
        static void Home(RobotCombat c,int side,ref float time)
        {for(int n=0;n<50;n++){time+=.02f;c.Sample(side,Vector3.zero,false,time);}}
        static void Jab(RobotCombat c,int side,ref float time)
        {Home(c,side,ref time);time+=.02f;c.Sample(side,new Vector3(0,0,.2f),false,time);}
        static void Hook(RobotCombat c,int side,ref float time,bool boost)
        {
            Home(c,side,ref time);float sign=side==0?-1:1;
            for(int n=1;n<=14;n++){time+=.02f;c.Sample(side,new Vector3(sign*.85f,0,.8f)*(n/14f),false,time);}
            time+=.02f;c.Sample(side,new Vector3(sign*.65f,0,.82f),boost,time);
        }
        [MenuItem("Anseong Steel/Robot Play/Run combat checks")]
        public static void Run()
        {
            results.Clear();
            var c=Fresh();float t=0;Jab(c,0,ref t);
            Check(c.Hit(0,t,true)&&c.BossHealth==152,"individual jab damages");
            Check(!c.Hit(0,t+.01f,true)&&c.HitCount==1,"one stroke cannot damage twice");
            t+=.02f;c.Sample(0,new Vector3(0,0,.1f),false,t);Check(!c.Hit(0,t,true),"return motion cannot attack");
            c=Fresh();t=0;Jab(c,0,ref t);c.Hit(0,t,true);Jab(c,1,ref t);c.Hit(1,t,true);Hook(c,0,ref t,true);
            Check(c.arms[0].boosted,"final hook permits fresh boost press");
            c.Hit(0,t,true);Check(c.BossHealth==104&&c.ComboStep==0,"L R L boosted combo damage and reset");
            c=Fresh();t=0;Jab(c,1,ref t);c.Hit(1,t,true);Jab(c,0,ref t);c.Hit(0,t,true);Hook(c,1,ref t,false);c.Hit(1,t,true);
            Check(c.BossHealth==124,"R L R regular combo");
            c=Fresh();t=0;Hook(c,0,ref t,true);Check(!c.arms[0].boosted&&!c.Hit(0,t,true),"standalone hook cannot boost or damage by default");
            c=Fresh();t=0;Jab(c,0,ref t);c.Hit(0,t,true);Jab(c,0,ref t);c.Hit(0,t,true);Check(c.ComboStep==1&&c.NextSide==1,"same side cannot supply second cooperative jab");
            c.Tick(t+3);Check(c.ComboStep==0,"combo expires");
            c=Fresh();t=0;Jab(c,0,ref t);Check(!c.Hit(0,t,false)&&c.HitCount==0,"closed attack window consumes attempt without damage");
            c=Fresh();t=0;Home(c,0,ref t);t+=.02f;c.Sample(0,new Vector3(0,.65f,0),false,t);
            Check(c.ReceiveAttack(0,false,10)&&c.RobotHealth==100,"raised left arm blocks left attack");
            Check(!c.ReceiveAttack(1,false,10)&&c.RobotHealth==90,"wrong arm does not guard opposite attack");
            Check(!c.ReceiveAttack(0,true,10)&&c.RobotHealth==80,"single guard does not block dual attack");
            Home(c,1,ref t);t+=.02f;c.Sample(1,new Vector3(0,.65f,0),false,t);Check(c.ReceiveAttack(0,true,10),"two raised arms block dual attack");
            c.Invalidate(0);Check(!c.arms[0].guarding&&!c.arms[0].armed,"tracking loss invalidates guard and rearm");
            c=Fresh();t=0;Jab(c,0,ref t);c.Tick(t+1);Check(!c.Hit(0,t+1,true),"expired miss cannot hit on recovery");
            c=Fresh();t=0;Home(c,0,ref t);c.Sample(0,new Vector3(float.NaN,0,0),true,t+.02f);Check(RobotCombat.Finite(c.arms[0].position),"nonfinite input rejected");
            Check(RobotPlaySession.Authorized(0,0,0,42)&&RobotPlaySession.Authorized(1,42,0,42)&&!RobotPlaySession.Authorized(0,42,0,42)&&!RobotPlaySession.Authorized(1,99,0,42)&&!RobotPlaySession.Authorized(2,42,0,42),"network sender must own requested pilot side");
            foreach(float interval in new[]{.016f,.022f,.033f,.05f,.08f})
            {
                c=Fresh();t=0;Home(c,0,ref t);float begin=t;
                for(float age=interval;age<.43f;age+=interval){t=begin+age;c.Sample(0,RobotPilotInput.DesktopPose(0,age,true),false,t);}
                Check(c.arms[0].strike==Strike.Hook,"hook windup stays a hook at sample interval "+interval);
            }
            Directory.CreateDirectory("RobotPlayValidation");File.WriteAllLines("RobotPlayValidation/combat-checks.txt",results);
            Debug.Log("ROBOT_COMBAT_CHECKS_PASS "+results.Count);
        }
    }
}
