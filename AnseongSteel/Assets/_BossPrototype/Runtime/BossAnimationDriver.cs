using UnityEngine;

namespace AnseongSteel.Bosses
{
    [DefaultExecutionOrder(100)]
    public sealed class BossAnimationDriver : MonoBehaviour
    {
        public Animator animator;
        public BossMovement movement;
        public BossRotation rotation;
        public BossHeavyPunch attack;
        public Transform hitPoint;
        public float hitRadius = 0.22f;
        public float punchDuration = 5.133333f;
        public AnimationCurve leftTurnProgress = AnimationCurve.Linear(0, 0, 1, 1);
        public AnimationCurve rightTurnProgress = AnimationCurve.Linear(0, 0, 1, 1);

        private bool punching;
        private bool movementWasEnabled;
        private bool rotationWasEnabled;
        private float punchDeadline;
        private string currentState;
        private bool externalAction;
        private BossSwordAttachment swordAttachment;
        private string IdleState => swordAttachment && swordAttachment.IsDrawn && animator.HasState(0, Animator.StringToHash("SwordIdle")) ? "SwordIdle" : "Idle";
        private bool externalMovementEnabled, externalRotationEnabled;
        public event System.Action MotionReset;
        public bool IsAttacking => punching || externalAction;
        public bool IsTurning => turning;
        private bool turning;
        private bool turnMovementWasEnabled;
        private float turnRemaining;
        private float segmentAngle;
        private float segmentApplied;
        private float turnDeadline;
        private float leftTurnDuration;
        private float rightTurnDuration;
        private string turnState;
        private bool settlingTurn;

        private void Awake()
        {
            animator.applyRootMotion = false;
            swordAttachment=GetComponent<BossSwordAttachment>();
            // Use the actual clip duration so reimporting/replacing the clip cannot
            // leave an old serialized timeout that cuts the recovery short.
            foreach (var clip in animator.runtimeAnimatorController.animationClips)
            {
                if (clip.name == "Punch") punchDuration = clip.length;
                if (clip.name == "TurnLeft") leftTurnDuration = clip.length;
                if (clip.name == "TurnRight") rightTurnDuration = clip.length;
            }
            rotation.AnimatedTurnHandler = BeginTurn;
            rotation.RotationStopped += CancelTurn;
            attack.ConfigureAnimationAttack(hitPoint, hitRadius);
        }

        private void Start() => PlayState(IdleState, 0f);

        private void Update()
        {
            if (punching)
            {
                // Recover if an event was lost because the Animator was disabled.
                if (!attack.IsAttacking || Time.time >= punchDeadline)
                    OnPunchFinished();
                return;
            }
            if (turning || externalAction) return;
            if(movement.IsMoving && swordAttachment) swordAttachment.Sheathe();
            bool backward=Vector3.Dot(movement.TravelDirection,transform.forward)<-.1f && animator.HasState(0,Animator.StringToHash("WalkBackward"));
            PlayState(movement.IsMoving && !movement.IsJumping ? (backward?"WalkBackward":"Walk") : IdleState, 0.12f);
        }

        public bool TryPunch()
        {
            if (!isActiveAndEnabled || punching || externalAction || turning || !animator.isActiveAndEnabled ||
                !attack.TryHeavyPunch()) return false;
            movementWasEnabled = movement.enabled;
            rotationWasEnabled = rotation.enabled;
            movement.StopMove();
            rotation.StopRotate();
            movement.enabled = false;
            rotation.enabled = false;
            punching = true;
            punchDeadline = Time.time + punchDuration + 0.5f;
            PlayState("Punch", 0.08f);
            return true;
        }

        public void OnPunchImpact()
        {
            if (punching) attack.AnimationImpact();
        }

        public void OnPunchFinished()
        {
            if (!punching) return;
            attack.CancelAttack();
            punching = false;
            movement.enabled = movementWasEnabled;
            rotation.enabled = rotationWasEnabled;
            PlayState(IdleState, 0.12f);
        }

        public void ResetMotion(float fade=0f)
        {
            MotionReset?.Invoke();
            EndCombatAction();
            OnPunchFinished();
            attack.CancelAttack();
            movement.StopMove();
            rotation.StopRotate();
            currentState = null;
            PlayState(IdleState, fade);
        }

        public bool TryBeginCombatAction(string state)
        {
            if(!isActiveAndEnabled||punching||turning||externalAction||!animator.isActiveAndEnabled)return false;
            externalMovementEnabled=movement.enabled;externalRotationEnabled=rotation.enabled;
            movement.StopMove();rotation.StopRotate();movement.enabled=false;rotation.enabled=false;externalAction=true;
            currentState=null;PlayState(string.IsNullOrEmpty(state)?"Idle":state,.08f);return true;
        }
        public void EndCombatAction()
        {
            if(!externalAction)return;
            externalAction=false;movement.enabled=externalMovementEnabled;rotation.enabled=externalRotationEnabled;
            currentState=null;PlayState(IdleState,.12f);
        }
        private void PlayState(string state, float fade)
        {
            if (currentState == state) return;
            currentState = state;
            animator.CrossFadeInFixedTime(state, fade, 0, 0f);
        }

        private void OnDisable()
        {
            MotionReset?.Invoke();
            EndCombatAction();
            if (punching) OnPunchFinished();
            if (turning) rotation.StopRotate();
        }

        private bool BeginTurn(float signedAngle)
        {
            if (!isActiveAndEnabled || punching || externalAction || leftTurnDuration <= 0f || rightTurnDuration <= 0f)
                return false;
            turning = true;
            settlingTurn = false;
            turnMovementWasEnabled = movement.enabled;
            movement.StopMove();
            movement.enabled = false;
            turnRemaining = signedAngle;
            StartTurnSegment();
            return true;
        }

        private void StartTurnSegment()
        {
            segmentAngle = Mathf.Clamp(turnRemaining, -90f, 90f);
            segmentApplied = 0f;
            turnState = segmentAngle < 0f ? "TurnLeft" : "TurnRight";
            turnDeadline = Time.time + (segmentAngle < 0f ? leftTurnDuration : rightTurnDuration) + 1f;
            currentState = null;
            PlayState(turnState, 0.22f);
        }

        private void LateUpdate()
        {
            if (!turning) return;
            if (settlingTurn)
            {
                if (Time.time >= turnDeadline) rotation.StopRotate();
                return;
            }
            if (!animator.isActiveAndEnabled || Time.time > turnDeadline)
            {
                rotation.StopRotate();
                return;
            }
            var state = animator.GetCurrentAnimatorStateInfo(0);
            if (animator.IsInTransition(0))
            {
                var next = animator.GetNextAnimatorStateInfo(0);
                if (next.IsName(turnState)) state = next;
            }
            if (!state.IsName(turnState)) return;
            float t = Mathf.Clamp01(state.normalizedTime);
            var curve = segmentAngle < 0f ? leftTurnProgress : rightTurnProgress;
            float applied = segmentAngle * (t >= 1f ? 1f : Mathf.Clamp01(curve.Evaluate(t)));
            // Only this code rotates the actor, following the source yaw curve.
            rotation.ApplyAnimatedDelta(applied - segmentApplied);
            segmentApplied = applied;
            if (t < 1f) return;
            turnRemaining -= segmentAngle;
            if (Mathf.Abs(turnRemaining) > 0.001f) StartTurnSegment();
            else
            {
                // Keep movement locked until the feet have blended back to Idle.
                settlingTurn = true;
                turnDeadline = Time.time + .28f;
                PlayState(IdleState, .28f);
            }
        }

        private void CancelTurn()
        {
            if (!turning) return;
            turning = false;
            settlingTurn = false;
            movement.enabled = turnMovementWasEnabled;
            turnRemaining = 0f;
            PlayState(IdleState, 0.28f);
        }

        private void OnDestroy()
        {
            if (rotation == null) return;
            rotation.RotationStopped -= CancelTurn;
            if (rotation.AnimatedTurnHandler == BeginTurn) rotation.AnimatedTurnHandler = null;
        }
    }
}




