#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
namespace AnseongSteel.Bosses
{
    public sealed class BossCombatVerification : MonoBehaviour
    {
        BossCombatController c;
        string failure;
        IEnumerator Start()
        {
            Application.logMessageReceived+=Errors;
            var test=Checks();
            while(true)
            {
                object step=null;bool more=false;
                try{more=test.MoveNext();if(more)step=test.Current;}catch(Exception e){failure=e.ToString();}
                if(failure!=null||!more)break;
                yield return step;
            }
            Time.timeScale=1;
            var report=failure==null?"PASS: all independent patterns, deduplicated damage, guard/parry, grab release, status expiry, cooldown, cancellation, projectile interception, core window and death/reset.":"FAIL: "+failure;
            File.WriteAllText("Library/BossCombatVerification.txt",report);Debug.Log(report);
            Application.logMessageReceived-=Errors;
            UnityEditor.SessionState.SetBool("BossCombatVerification",false);
            if(Application.isBatchMode)UnityEditor.EditorApplication.Exit(failure==null?0:1);
            else UnityEditor.EditorApplication.isPlaying=false;
        }
        void Errors(string message,string stack,LogType type){if((type==LogType.Exception||type==LogType.Error)&&!stack.Contains("UnityEditor.Search"))failure=message+"\n"+stack;}
        void Assert(bool condition,string message){if(!condition)throw new Exception(message);}
        void Reset(float distance=1.05f)
        {
            c.ResetEncounter();c.driver.ResetMotion();
            var target=c.target.transform.position;
            c.transform.SetPositionAndRotation(new Vector3(target.x,0,target.z-distance),Quaternion.identity);
            Physics.SyncTransforms();
        }
        IEnumerator Checks()
        {
            c=UnityEngine.Object.FindFirstObjectByType<BossCombatController>();
            yield return null;Time.timeScale=3;
            var extra=new GameObject("DuplicateColliderTest");extra.transform.SetParent(c.target.transform,false);extra.AddComponent<BoxCollider>();
            foreach(var d in c.attacks)
            {
                Reset(d.IsSword?d.preferredDistance:1.05f);yield return null;
                Assert(c.TryAttack(d.kind),"Rejected "+d.kind+": "+c.LastMessage);
                Assert(!c.TryAttack(d.kind),"Reentrant attack accepted");
                float deadline=Time.time+12;
                bool observedStatus=false,grabTested=false;
                while(c.IsBusy&&Time.time<deadline)
                {
                    if(d.IsSword&&c.sword.IsDrawn)
                        Assert(Vector3.Distance(c.sword.sword.TransformPoint(new Vector3(0,c.sword.rightGripY,0)),c.sword.rightHand.TransformPoint(c.sword.handPosition))<.005f,"Sword separated from palm");
                    if(d.status!=BossStatus.None&&(c.target.Status&d.status)==d.status)observedStatus=true;
                    if(d.kind==BossAttackKind.Grab&&c.target.GrabbedArm!=BossArm.None&&!grabTested)
                    {
                        Assert(!c.ReceivePlayerHit(1,BossArm.Left),"Grabbed arm counter accepted");
                        Assert(c.ReceivePlayerHit(1,BossArm.Right),"Opposite arm release rejected");
                        Assert(c.target.GrabbedArm==BossArm.None,"Grab did not release");grabTested=true;
                    }
                    yield return null;
                }
                Assert(!c.IsBusy,"Timeout "+d.kind);
                int expected=d.kind==BossAttackKind.MissileBarrage?d.projectileCount:d.kind==BossAttackKind.AlternatingCombo?d.strikeTimes.Length:1;
                Assert(c.target.HitCount==expected,$"{d.kind}: hit count {c.target.HitCount}, expected {expected}");
                Assert(d.status==BossStatus.None||observedStatus,"No status applied: "+d.kind);
                Assert(!c.TryAttack(d.kind),"Cooldown not enforced: "+d.kind);
                Assert(c.driver.movement.enabled&&c.driver.rotation.enabled,"Motion remained locked");
                Debug.Log("PATTERN PASS "+d.kind);
            }
            Reset();yield return null;
            Assert(c.TryAttack(BossAttackKind.HeavyPunch),"Transition punch start failed");
            while(c.IsBusy)yield return null;
            UnityEngine.Object.FindFirstObjectByType<BossModelDemo>().StopDemo();
            var hip=c.driver.animator.GetComponentsInChildren<Transform>().Single(t=>t.name=="mixamorig:Hips");
            c.driver.movement.MoveTo(c.target.transform.position-Vector3.forward*2.8f);
            bool sawBackwards=false;float moveDeadline=Time.time+6;
            while(c.driver.movement.IsMoving&&Time.time<moveDeadline)
            {
                var animator=c.driver.animator;
                var hipLocal=c.transform.InverseTransformPoint(hip.position);
                Assert(Mathf.Abs(hipLocal.z)<.35f,"Mesh teleported during punch-to-sword retreat: "+hipLocal);
                sawBackwards|=animator.GetCurrentAnimatorStateInfo(0).IsName("WalkBackward")||(animator.IsInTransition(0)&&animator.GetNextAnimatorStateInfo(0).IsName("WalkBackward"));
                yield return null;
            }
            Assert(sawBackwards&&!c.driver.movement.IsMoving&&Mathf.Abs(Vector3.ProjectOnPlane(c.transform.position-c.target.transform.position,Vector3.up).magnitude-2.8f)<.02f,"Sword-distance retreat failed");
            Reset(5);yield return null;c.showDebugVolumes=false;
            Assert(c.TryAttack(BossAttackKind.LaserSweep),"Laser visual test rejected");
            while(c.State==BossCombatState.Windup)yield return null;
            Assert(c.laser&&c.laser.IsVisible,"Laser missing when debug volumes disabled");
            Assert(c.transform.InverseTransformPoint(c.laser.Origin).y>2,"Laser is below chest");
            c.CancelAttack();Assert(!c.laser.IsVisible,"Cancelled laser remained visible");c.showDebugVolumes=true;
            Reset(2.8f);
            c.target.Receive(new BossHit{damage=299,requiredGuard=BossArm.None});
            Assert(c.TryOverheadSmash(),"Finishing attack rejected");
            float killDeadline=Time.time+4;
            while(c.target.Health>0&&Time.time<killDeadline)yield return null;
            Assert(c.target.Health==0&&c.IsBusy&&c.driver.IsAttacking,"Defeating target interrupted recovery");
            while(c.IsBusy&&Time.time<killDeadline)yield return null;
            Assert(!c.IsBusy,"Finishing attack did not recover");
            Reset();
            var hit=new BossHit{kind=BossAttackKind.SwordSlash,damage=20,requiredGuard=BossArm.Both};
            c.target.BeginGuard(BossArm.Both);Assert(c.target.Receive(hit)==BossHitResult.Parried,"Parry window failed");
            yield return new WaitForSeconds(.3f);
            Assert(c.target.Receive(hit)==BossHitResult.Blocked,"Sustained guard failed");
            c.target.EndGuard(BossArm.Right);Assert(c.target.Receive(hit)==BossHitResult.Hit,"Single guard blocked two-arm attack");
            c.target.ApplyStatus(BossStatus.LeftArmDisabled|BossStatus.HudDisabled,.2f);
            Assert(!c.target.IsArmAvailable(BossArm.Left),"Arm status ignored");yield return new WaitForSeconds(.3f);
            Assert(c.target.Status==BossStatus.None,"Status did not expire");
            Reset();Assert(c.TrySwordSlash(),"Cancel test start failed");c.CancelAttack();yield return new WaitForSeconds(2.5f);
            Assert(c.target.HitCount==0&&!c.driver.IsAttacking,"Cancelled attack still hit or locked movement");
            Reset(8);Assert(c.TryMissileBarrage(),"Barrage start failed");yield return new WaitForSeconds(1.05f);
            c.InterceptAll();Assert(c.Interceptions>0,"Interception not counted");c.CancelAttack();yield return new WaitForSeconds(3);
            Assert(c.target.HitCount==0,"Cancelled/intercepted projectile damaged target");
            Reset();c.OpenCore(.3f);Assert(c.ReceivePlayerHit(10,BossArm.Right),"Core hit failed");
            Assert(Mathf.Abs(c.Health-(c.maximumHealth-30))<.01f,"Exposed damage multiplier failed");yield return new WaitForSeconds(.4f);
            Assert(!c.CoreIsExposed&&!c.IsBusy,"Core window remained open");
            Assert(c.ReceivePlayerHit(9999,BossArm.Right)&&c.State==BossCombatState.Dead,"Death failed");Assert(!c.TrySwordSlash(),"Dead boss attacked");
            Reset();Assert(c.State==BossCombatState.Idle&&c.Health==c.maximumHealth,"Reset failed");
            Destroy(extra);
        }
    }
}
#endif



