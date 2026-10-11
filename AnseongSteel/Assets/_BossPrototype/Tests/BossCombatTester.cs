using System.Collections;
using UnityEngine;
namespace AnseongSteel.Bosses
{
    public sealed class BossCombatTester : MonoBehaviour
    {
        public BossModelDemo demo;
        public BossCombatController combat;
        public bool showPanel=true;
        public bool positionForAttack=true;
        public bool reviveTestTarget=true;
        public float swordTestDistance=2.8f;
        Vector2 scroll;
        Coroutine approach;
        void OnGUI()
        {
            float scale=Mathf.Clamp(Screen.height/800f,.75f,1.5f);var previous=GUI.matrix;
            GUI.matrix=Matrix4x4.Scale(Vector3.one*scale);
            float width=Screen.width/scale,height=Screen.height/scale;
            if(GUI.Button(new Rect(width-298,58,280,28),showPanel?"Hide combat tests":"Show combat tests"))showPanel=!showPanel;
            if(!showPanel){GUI.matrix=previous;return;}
            GUILayout.BeginArea(new Rect(width-298,92,280,Mathf.Min(650,height-106)),GUI.skin.box);
            scroll=GUILayout.BeginScrollView(scroll);
            GUILayout.Label("BOSS ATTACK LAB");
            GUILayout.Label($"Boss {combat.Health:0} / Target {combat.target.Health:0}");
            GUILayout.Label($"{combat.State} / {combat.LastMessage}");
            GUILayout.Label($"Target status: {combat.target.Status}");
            GUILayout.Label($"Grabbed: {combat.target.GrabbedArm}");
            if(combat.UsingPlaceholder)GUILayout.Label("CLIP MISSING: timing / hit-volume preview");
            GUILayout.Label($"Distance: {Vector3.Distance(combat.transform.position,combat.target.transform.position):0.00} m");
            positionForAttack=GUILayout.Toggle(positionForAttack,"Move to selected attack distance");
            if(GUILayout.Button("Sword distance / face target")){CancelPreparation();approach=StartCoroutine(Approach(swordTestDistance));}
            if(GUILayout.Button("Punch distance / face target")){CancelPreparation();approach=StartCoroutine(Approach(1.05f));}
            foreach(var definition in combat.attacks)
            {
                if(!definition)continue;
                if(GUILayout.Button(definition.kind.ToString()))
                {
                    if(!combat.IsBusy){CancelPreparation();approach=StartCoroutine(AttackOnce(definition));}
                }
            }
            GUILayout.Space(6);GUILayout.Label("Player integration simulation");
            bool left=(combat.target.Guard&BossArm.Left)!=0,right=(combat.target.Guard&BossArm.Right)!=0;
            SetGuard(BossArm.Left,GUILayout.Toggle(left,"Left guard (toggle near impact = parry)"),left);
            SetGuard(BossArm.Right,GUILayout.Toggle(right,"Right guard"),right);
            if(GUILayout.Button("Left player counterattack"))combat.ReceivePlayerHit(10,BossArm.Left);
            if(GUILayout.Button("Right player counterattack / release grab"))combat.ReceivePlayerHit(10,BossArm.Right);
            if(GUILayout.Button("Intercept visible missiles"))combat.InterceptAll();
            if(GUILayout.Button("Expose core (integration hook)")){CancelPreparation();combat.OpenCore(4);}
            if(GUILayout.Button("Stun / interrupt")){CancelPreparation();combat.Stun(1.5f);}
            if(GUILayout.Button("Cancel attack")){CancelPreparation();combat.CancelAttack();}
            if(GUILayout.Button("Reset encounter")){CancelPreparation();demo.ResetDemo();}
            reviveTestTarget=GUILayout.Toggle(reviveTestTarget,"Revive defeated test target before attack");
            combat.showDebugVolumes=GUILayout.Toggle(combat.showDebugVolumes,"Show attack preview volumes");
            GUILayout.EndScrollView();GUILayout.EndArea();GUI.matrix=previous;
        }
        void SetGuard(BossArm arm,bool requested,bool old){if(requested==old)return;if(requested)combat.target.BeginGuard(arm);else combat.target.EndGuard(arm);}
        void CancelPreparation()
        {
            if(approach!=null)StopCoroutine(approach);approach=null;demo.StopDemo();
        }
        IEnumerator AttackOnce(BossAttackDefinition definition)
        {
            if(reviveTestTarget&&combat.target.Health<=0)combat.target.ResetTarget();
            if(positionForAttack)yield return Approach(definition.preferredDistance);
            combat.TryAttack(definition.kind);approach=null;
        }
        IEnumerator Approach(float distance)
        {
            demo.StopDemo();demo.FaceTarget();yield return new WaitUntil(()=>!demo.driver.IsTurning&&!demo.driver.rotation.IsRotating);
            demo.WalkToTarget(distance);yield return new WaitUntil(()=>!demo.driver.movement.IsMoving);
            yield return new WaitForSeconds(.16f);
        }
    }
}
