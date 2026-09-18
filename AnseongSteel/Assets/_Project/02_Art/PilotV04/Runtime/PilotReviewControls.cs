using UnityEngine;
using UnityEngine.InputSystem;

namespace AnseongSteel.PilotV04
{
    [DefaultExecutionOrder(0)]
    public sealed class PilotReviewControls : MonoBehaviour
    {
        public PilotReviewRig rig;
        public Camera reviewCamera;
        public float moveSpeed=.22f,rotateSpeed=65;
        int selected=1,pose;
        bool showControls=true;
        readonly string[] names={"Head rotation","Left hand","Right hand","Left elbow","Right elbow"};
        readonly string[] poses={"Neutral","Guard","Reach / grip","Raised arm","Wrist rotation","Reach limit"};
        Transform Target => selected==0?rig.headTarget:selected==1?rig.left.target:selected==2?rig.right.target:selected==3?rig.left.pole:rig.right.pole;
        void Update()
        {
            if(!rig||!rig.IsReady)return;
            var k=Keyboard.current;if(k==null)return;
            if(k.f1Key.wasPressedThisFrame)showControls=!showControls;
            if(k.digit1Key.wasPressedThisFrame)selected=0;if(k.digit2Key.wasPressedThisFrame)selected=1;
            if(k.digit3Key.wasPressedThisFrame)selected=2;if(k.digit4Key.wasPressedThisFrame)selected=3;if(k.digit5Key.wasPressedThisFrame)selected=4;
            if(k.tabKey.wasPressedThisFrame){pose=(pose+1)%poses.Length;rig.SetPose(pose);}
            if(k.rKey.wasPressedThisFrame){pose=0;rig.SetPose(0);}
            if(k.gKey.wasPressedThisFrame)rig.leftGrip=rig.rightGrip=rig.leftGrip>.5f?0:1;
            Vector3 move=new Vector3((k.dKey.isPressed?1:0)-(k.aKey.isPressed?1:0),(k.eKey.isPressed?1:0)-(k.qKey.isPressed?1:0),(k.wKey.isPressed?1:0)-(k.sKey.isPressed?1:0));
            float dt=Mathf.Min(Time.unscaledDeltaTime,.05f);
            if(selected!=0)Target.position+=rig.transform.TransformDirection(move)*moveSpeed*dt;
            Vector3 turn=new Vector3((k.downArrowKey.isPressed?1:0)-(k.upArrowKey.isPressed?1:0),(k.rightArrowKey.isPressed?1:0)-(k.leftArrowKey.isPressed?1:0),(k.cKey.isPressed?1:0)-(k.zKey.isPressed?1:0));
            if(selected<3)Target.rotation=Target.rotation*Quaternion.Euler(turn*rotateSpeed*dt);
            var mouse=Mouse.current;
            if(mouse!=null&&reviewCamera)
            {
                Vector3 focus=rig.transform.TransformPoint(new Vector3(0,1.0f,0));
                if(mouse.rightButton.isPressed)
                {
                    Vector2 d=mouse.delta.ReadValue();
                    reviewCamera.transform.RotateAround(focus,Vector3.up,d.x*.18f);
                    reviewCamera.transform.RotateAround(focus,reviewCamera.transform.right,-d.y*.18f);
                }
                float wheel=mouse.scroll.ReadValue().y;
                if(Mathf.Abs(wheel)>0)
                {
                    Vector3 offset=reviewCamera.transform.position-focus;
                    reviewCamera.transform.position=focus+offset.normalized*Mathf.Clamp(offset.magnitude-wheel*.002f,1.0f,6);
                }
            }
        }
        void OnGUI()
        {
            if(!rig||!rig.IsReady||!showControls)return;
            GUILayout.BeginArea(new Rect(12,12,315,510),GUI.skin.box);
            GUILayout.Label("PILOT V04 | Model & Rig Review");
            GUILayout.Label("Pose checks only - fixed feet, no animation");
            selected=GUILayout.SelectionGrid(selected,names,1);
            int next=GUILayout.SelectionGrid(pose,poses,2);
            if(next!=pose){pose=next;rig.SetPose(pose);}
            GUILayout.Label("Left grip");rig.leftGrip=GUILayout.HorizontalSlider(rig.leftGrip,0,1);
            GUILayout.Label("Right grip");rig.rightGrip=GUILayout.HorizontalSlider(rig.rightGrip,0,1);
            if(GUILayout.Button("Aligned hands / open"))rig.SetWristReview(0,0);
            if(GUILayout.Button("Aligned hands / close"))rig.SetWristReview(0,.85f);
            GUILayout.BeginHorizontal();
            if(GUILayout.Button("Roll -60"))rig.SetWristReview(-60,rig.leftGrip);
            if(GUILayout.Button("Roll +60"))rig.SetWristReview(60,rig.leftGrip);
            GUILayout.EndHorizontal();
            if(rig.left.orientationError>1||rig.right.orientationError>1)GUILayout.Label("Hand angle limited to the review range.");
            GUILayout.Label("WASD: horizontal | Q/E: down/up\nArrows: rotate | Z/C: roll\n1-5: select | Tab: pose | G: grip | R: reset\nRight mouse: orbit | Wheel: zoom | F1: UI");
            GUILayout.Label($"Wrist target distance: L {rig.left.targetError*1000:F1} / R {rig.right.targetError*1000:F1} mm");
            if(rig.left.clampedDistance>.001f||rig.right.clampedDistance>.001f)GUILayout.Label("Outside arm reach: extension is limited.");
            GUILayout.EndArea();
            if(reviewCamera&&Target)
            {
                Vector3 p=reviewCamera.WorldToScreenPoint(Target.position);
                if(p.z>0){GUI.color=Color.cyan;GUI.Label(new Rect(p.x-8,Screen.height-p.y-12,30,25),"+");GUI.color=Color.white;}
            }
        }
    }
}
