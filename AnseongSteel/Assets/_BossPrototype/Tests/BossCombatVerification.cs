#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
namespace AnseongSteel.Bosses
{
    [DefaultExecutionOrder(1000)]
    public sealed class BossCombatVerification : MonoBehaviour
    {
        BossCombatController c;
        string failure;
        public bool recordEquipment;
        bool recordEquip;
        int recordedFrames;
        Quaternion lastRecordedRotation;
        float maximumGuardStep;
        void CaptureEquipFrame()
        {
            var equip=c.GetComponent<BossSwordEquip>();
            if(recordedFrames>0&&equip.Drawing&&equip.Progress>.96f)
                maximumGuardStep=Mathf.Max(maximumGuardStep,Quaternion.Angle(lastRecordedRotation,c.sword.sword.rotation));
            lastRecordedRotation=c.sword.sword.rotation;
            var cam=Camera.main;
            cam.orthographic=false;cam.fieldOfView=55;
            cam.transform.position=c.transform.TransformPoint(new Vector3(0,2.5f,5.8f));
            cam.transform.LookAt(c.transform.TransformPoint(new Vector3(0,1.65f,0)));
            var target=new RenderTexture(900,900,24);target.Create();
            RenderPipeline.SubmitRenderRequest(cam,new RenderPipeline.StandardRequest{destination=target});
            var previous=RenderTexture.active;RenderTexture.active=target;
            var image=new Texture2D(900,900,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,900,900),0,0);image.Apply();
            Directory.CreateDirectory("Library/BossSwordEquipVideo");File.WriteAllBytes("Library/BossSwordEquipVideo/"+(recordedFrames++).ToString("D4")+".png",image.EncodeToPNG());
            RenderTexture.active=previous;Destroy(image);target.Release();Destroy(target);
        }
        float previousFist;
        float[] previousRelaxedAngles;
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
            Time.timeScale=1;Time.captureDeltaTime=0;
            var report=failure==null?"PASS: all independent patterns, deduplicated damage, guard/parry, grab release, status expiry, cooldown, cancellation, projectile interception, core window and death/reset.":"FAIL: "+failure;
            File.WriteAllText("Library/BossCombatVerification.txt",report);Debug.Log(report);
            Application.logMessageReceived-=Errors;
            UnityEditor.SessionState.SetBool("BossCombatVerification",false);
            if(Application.isBatchMode)UnityEditor.EditorApplication.Exit(failure==null?0:1);
            else UnityEditor.EditorApplication.isPlaying=false;
        }
        void Errors(string message,string stack,LogType type){if((type==LogType.Exception||type==LogType.Error)&&!stack.Contains("UnityEditor.Search"))failure=message+"\n"+stack;}
        void Assert(bool condition,string message){if(!condition)throw new Exception(message);}
        // Coroutines resume before LateUpdate pose overrides. Inspect the final
        // rendered hand here, after BossHandPose (execution order 250).
        void LateUpdate()
        {
            if(!c)return;var hands=c.GetComponent<BossHandPose>();
            if(!hands)return;
            try
            {
                if(previousRelaxedAngles==null)previousRelaxedAngles=new float[hands.joints.Length];
                bool releasing=hands.FistWeight>0&&hands.FistWeight<previousFist&&previousFist<.99f&&hands.RightGripWeight==0;
                for(int i=0;i<hands.joints.Length;i++)
                {
                    var joint=hands.joints[i];float angle=Quaternion.Angle(joint.bone.localRotation,joint.relaxedRotation);
                    if(releasing)Assert(angle<=previousRelaxedAngles[i]+.15f,"Finger reversed direction during fist release: "+joint.bone.name);
                    previousRelaxedAngles[i]=angle;
                }
                previousFist=hands.FistWeight;
                if(hands.FistWeight>.999f)AssertClosedHands();
                if(hands.RelaxedWeight>.999f&&hands.RightGripWeight==0&&hands.FistWeight==0&&hands.GrabWeight==0&&hands.CastWeight==0)AssertRelaxedHands();
                if(c.sword.IsDrawn&&!c.sword.IsTransferring&&hands.RightGripWeight>.999f)AssertGrip(c.sword.rightHand,c.sword.rightGripY);
                if(c.sword.IsDrawn&&!c.sword.IsTransferring&&hands.LeftGripWeight>.999f)AssertGrip(c.sword.leftHand,c.sword.leftGripY);
            }
            catch(Exception e){failure=e.ToString();}
            if(recordEquip)CaptureEquipFrame();
        }
        void AssertRelaxedHands()
        {
            var bones=c.driver.animator.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("mixamorig:")).ToDictionary(t=>t.name);
            foreach(string side in new[]{"Right","Left"})
            {
                string prefix="mixamorig:"+side+"Hand";var hand=bones[prefix];var along=hand.InverseTransformPoint(bones[prefix+"Middle1"].position).normalized;
                foreach(string finger in new[]{"Index","Middle","Ring","Pinky"})
                {
                    var root=hand.InverseTransformPoint(bones[prefix+finger+"1"].position);var tip=hand.InverseTransformPoint(bones[prefix+finger+"3_end"].position);
                    Assert(Vector3.Dot(tip-root,along)>.035f,"Idle retained a curled or twisted finger: "+side+finger);
                }
            }
        }
        void AssertGrip(Transform hand,float gripY)
        {
            var axis=c.sword.sword.up;var center=c.sword.sword.TransformPoint(new Vector3(0,gripY,0));
            foreach(string finger in new[]{"Index","Middle","Ring","Pinky"})
            {
                var tip=hand.GetComponentsInChildren<Transform>().Single(t=>t.name.EndsWith(finger+"3_end"));
                float radial=Vector3.ProjectOnPlane(tip.position-center,axis).magnitude;
                Assert(radial>.025f&&radial<.075f,"Finger missed handle: "+tip.name+" radial="+radial);
            }
        }
        void AssertClosedHands()
        {
            var bones=c.driver.animator.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("mixamorig:")).ToDictionary(t=>t.name);
            foreach(string side in new[]{"Right","Left"})
            {
                string prefix="mixamorig:"+side+"Hand";var hand=bones[prefix];
                var along=hand.InverseTransformPoint(bones[prefix+"Middle1"].position).normalized;
                foreach(string finger in new[]{"Index","Middle","Ring","Pinky"})
                {
                    var root=hand.InverseTransformPoint(bones[prefix+finger+"1"].position);
                    var tip=hand.InverseTransformPoint(bones[prefix+finger+"3_end"].position);
                    Assert(Vector3.Dot(tip-root,along)<-.008f,side+" "+finger+" did not fold back toward palm");
                }
                var thumb=hand.InverseTransformPoint(bones[prefix+"Thumb3"].position);
                Assert(Mathf.Abs(thumb.x)>.035f,side+" thumb is buried inside folded fingers: "+thumb);
            }
        }
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
            yield return null;Time.captureDeltaTime=recordEquipment?1f/180f:1f/60f;Time.timeScale=3;
            var equip=c.GetComponent<BossSwordEquip>();Assert(equip,"Sword equip component missing");
            Reset();yield return null;
            var feetForEquip=c.driver.animator.GetComponentsInChildren<Transform>().Where(t=>t.name=="mixamorig:LeftFoot"||t.name=="mixamorig:RightFoot").ToArray();
            var footPositions=feetForEquip.Select(t=>t.position).ToArray();
            var previewCamera=Camera.main;var cameraPosition=previewCamera.transform.position;var cameraRotation=previewCamera.transform.rotation;float cameraFov=previewCamera.fieldOfView;bool cameraOrtho=previewCamera.orthographic;
            recordEquip=recordEquipment&&SystemInfo.graphicsDeviceType!=GraphicsDeviceType.Null;
            Assert(equip.TryDraw(),"Draw request rejected");
            float equipDeadline=Time.time+4;
            while(equip.IsBusy&&Time.time<equipDeadline)
            {
                Assert(c.target.HitCount==0,"Drawing sword caused damage");
                Assert(c.sword.LastTransferJump<.0001f,"Sword jumped when attachment changed");
                Assert(!c.TrySwordSlash(),"Attack bypassed equip lock");
                if(equip.Progress<equip.drawContact-.01f)Assert(!c.sword.IsDrawn,"Sword detached before contact");
                if(equip.Progress>equip.drawContact+.02f)Assert(c.sword.IsDrawn,"Sword failed to detach");
                for(int i=0;i<footPositions.Length;i++)Assert(Vector3.Distance(footPositions[i],feetForEquip[i].position)<.65f,"Unexpected displacement during Mixamo equip");
                yield return null;
            }
            Assert(!equip.IsBusy&&c.sword.IsDrawn&&!c.driver.IsAttacking,"Draw did not finish");
            yield return new WaitForSeconds(.4f);
            Assert(equip.TrySheathe(),"Sheathe request rejected");equipDeadline=Time.time+4;
            while(equip.IsBusy&&Time.time<equipDeadline)
            {
                Assert(c.sword.LastTransferJump<.0001f,"Sword jumped while sheathing");
                if(equip.Progress<equip.sheatheContact-.02f)Assert(c.sword.IsDrawn,"Sword returned before reaching back");
                if(equip.Progress>equip.sheatheContact+.02f)Assert(!c.sword.IsDrawn,"Sword remained in hand");
                yield return null;
            }
            Assert(!equip.IsBusy&&!c.sword.IsDrawn&&c.sword.sword.parent==c.sword.backSocket,"Sheathe did not finish");
            yield return new WaitForSeconds(.4f);recordEquip=false;
            if(recordedFrames>0){Assert(maximumGuardStep<12,"Sword rotation popped entering guard: "+maximumGuardStep);Debug.Log("SWORD GUARD maximum frame rotation="+maximumGuardStep+" degrees; attachment jump="+c.sword.LastTransferJump);}
            if(recordedFrames>0)File.WriteAllText("Library/BossSwordEquipVideo/frame-count.txt",recordedFrames.ToString());
            previewCamera.transform.SetPositionAndRotation(cameraPosition,cameraRotation);previewCamera.fieldOfView=cameraFov;previewCamera.orthographic=cameraOrtho;
            foreach(float interruptAt in new[]{.4f,1.2f})
            {
                Reset();yield return null;Assert(equip.TryDraw(),"Interrupt draw rejected");yield return new WaitForSeconds(interruptAt);
                c.CancelAttack();yield return new WaitForSeconds(.3f);
                Assert(!equip.IsBusy&&!c.driver.IsAttacking&&c.driver.movement.enabled,"Equip interruption locked boss");
                Assert(c.sword.IsDrawn==(interruptAt>1),"Interrupted equip lost attachment state");
            }
            Reset();yield return null;Assert(equip.TryDraw(),"Unarmed transition setup failed");
            while(equip.IsBusy)yield return null;
            Assert(c.TryGrab()&&c.State==BossCombatState.Equipping,"Unarmed attack did not request sheathing");
            while(c.State==BossCombatState.Equipping)yield return null;
            Assert(!c.sword.IsDrawn,"Unarmed attack started with sword in hand");c.CancelAttack();
            var extra=new GameObject("DuplicateColliderTest");extra.transform.SetParent(c.target.transform,false);extra.AddComponent<BoxCollider>();
            foreach(var d in c.attacks)
            {
                Reset(d.IsSword?d.preferredDistance:1.05f);yield return null;
                Assert(c.TryAttack(d.kind),"Rejected "+d.kind+": "+c.LastMessage);
                Assert(!c.TryAttack(d.kind),"Reentrant attack accepted");
                float deadline=Time.time+12;
                bool observedStatus=false,grabTested=false,closedHands=false,chargedHands=false;
                while(c.IsBusy&&Time.time<deadline)
                {
                    var handPose=c.GetComponent<BossHandPose>();
                    Assert(handPose,"Mechanical hand pose is missing");
                    if(handPose.CastWeight>.99f)chargedHands=true;
                    if(d.kind!=BossAttackKind.EMP&&d.kind!=BossAttackKind.SensorJam&&d.kind!=BossAttackKind.VisionDisruption)Assert(handPose.CastWeight<.001f,"Casting hand override leaked into "+d.kind);
                    if(d.kind==BossAttackKind.HeavyPunch||d.kind==BossAttackKind.AlternatingCombo)
                    {
                        if(handPose.FistWeight>.999f)closedHands=true;
                    }
                    else Assert(handPose.FistWeight<.001f,"Fist override leaked into "+d.kind);
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
                if(d.kind==BossAttackKind.EMP||d.kind==BossAttackKind.SensorJam||d.kind==BossAttackKind.VisionDisruption)Assert(chargedHands,"Charge hand pose never applied: "+d.kind);
                Assert((d.kind!=BossAttackKind.HeavyPunch&&d.kind!=BossAttackKind.AlternatingCombo)||closedHands,"Fists never closed for "+d.kind);
                int expected=d.kind==BossAttackKind.MissileBarrage?d.projectileCount:d.kind==BossAttackKind.AlternatingCombo?d.strikeTimes.Length:1;
                Assert(c.target.HitCount==expected,$"{d.kind}: hit count {c.target.HitCount}, expected {expected}");
                Assert(d.status==BossStatus.None||observedStatus,"No status applied: "+d.kind);
                Assert(!c.TryAttack(d.kind),"Cooldown not enforced: "+d.kind);
                Assert(c.driver.movement.enabled&&c.driver.rotation.enabled,"Motion remained locked");
                Debug.Log("PATTERN PASS "+d.kind);
            }
            Reset();yield return null;
            Assert(c.TryAttack(BossAttackKind.Grab),"Grab hold test rejected");
            float holdDeadline=Time.time+4;
            while(c.State!=BossCombatState.Holding&&Time.time<holdDeadline)yield return null;
            Assert(c.State==BossCombatState.Holding,"Grab did not enter holding");
            yield return new WaitForSeconds(.3f);
            Assert(c.GetComponent<BossHandPose>().GrabWeight>.99f,"Grasp opened while holding opponent");
            c.CancelAttack();yield return new WaitForSeconds(.3f);
            Assert(c.GetComponent<BossHandPose>().GrabWeight<.001f,"Grasp remained after release");
            Reset();yield return null;yield return null;
            var footBones=c.driver.animator.GetComponentsInChildren<Transform>().Where(t=>t.name=="mixamorig:LeftFoot"||t.name=="mixamorig:RightFoot").ToArray();
            var footInputs=footBones.Select(t=>t.localRotation).ToArray();
            Assert(c.TryAttack(BossAttackKind.AlternatingCombo),"Foot cancellation test rejected");
            yield return new WaitForSeconds(.7f);c.driver.ResetMotion();yield return new WaitForSeconds(.3f);
            for(int i=0;i<footBones.Length;i++)Assert(Quaternion.Angle(footInputs[i],footBones[i].localRotation)<.1f,"Foot pose remained after cancellation: "+footBones[i].name);
            Reset();yield return null;yield return null;
            var fingers=c.GetComponent<BossHandPose>();
            var distal=fingers.joints.Where(j=>j.bone.name.EndsWith("3")).Select(j=>j.bone).ToArray();
            var idleRotations=distal.Select(t=>t.localRotation).ToArray();
            Assert(c.TryAttack(BossAttackKind.HeavyPunch),"Hand cancellation test rejected");
            yield return new WaitForSeconds(.7f);Assert(fingers.FistWeight>.99f,"Fist did not close before cancellation");
            c.driver.ResetMotion();yield return null;yield return null;
            Assert(fingers.FistWeight==0,"Fist weight remained after reset");
            for(int i=0;i<distal.Length;i++)Assert(Quaternion.Angle(idleRotations[i],distal[i].localRotation)<.1f,"Constant distal channel retained fist pose: "+distal[i].name);
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
