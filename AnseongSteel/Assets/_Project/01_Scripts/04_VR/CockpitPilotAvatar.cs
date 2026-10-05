using AnseongSteel.PilotV04;
using UnityEngine;

namespace AnseongSteel.PlayerMotion
{
    // Reuses the authored V04 wrist/finger solver; only the input source changes.
    [DefaultExecutionOrder(250)]
    public sealed class CockpitPilotAvatar : MonoBehaviour
    {
        public PilotReviewRig rig;
        public PilotLocalView localView;
        public Transform headBone, eyeAnchor;
        public Vector3 neutralEye;
        public Vector3 leftReady, rightReady;
        public Quaternion leftReadyRotation, rightReadyRotation;
        public float leftReachScale = 1, rightReachScale = 1;
        Camera localCamera;
        bool guardAligned, reachInitialized;
        Quaternion leftGuardFrame, rightGuardFrame;
        Quaternion leftRotationDelta = Quaternion.identity, rightRotationDelta = Quaternion.identity;
        float leftElbowLift, rightElbowLift;
        float leftTransverseScale = 1, rightTransverseScale = 1;
        readonly RigidWristConstraint leftConstraint = new RigidWristConstraint(), rightConstraint = new RigidWristConstraint();
        public bool IsLocal => localCamera;

        public void Configure(PilotReviewRig source, Transform eye)
        {
            rig = source; rig.enabled = false;
            localView = rig.GetComponent<PilotLocalView>();
            headBone = rig.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Head);
            eyeAnchor = eye;
            neutralEye = transform.InverseTransformPoint(eye.position);
            rig.SetPose(1);
            rig.left.target.position = transform.TransformPoint(new Vector3(-.26f, 1.42f, .32f));
            rig.right.target.position = transform.TransformPoint(new Vector3(.26f, 1.42f, .32f));
            leftReady = transform.InverseTransformPoint(rig.left.target.position);
            rightReady = transform.InverseTransformPoint(rig.right.target.position);
            leftReadyRotation = Quaternion.Inverse(transform.rotation) * rig.left.target.rotation;
            rightReadyRotation = Quaternion.Inverse(transform.rotation) * rig.right.target.rotation;
            SetStartingReach(true); SetStartingReach(false); reachInitialized = true;
            ResetGuard();
        }
        void SetStartingReach(bool left)
        {
            var arm = left ? rig.left : rig.right;
            float length = (arm.upperLength + arm.lowerLength) / transform.lossyScale.x;
            float movementScale = length / .65f;
            if (left) leftTransverseScale = movementScale; else rightTransverseScale = movementScale;
            if (ControllerArmFollower.TryWorkspaceReachScale(transform.InverseTransformPoint(arm.upper.position), left ? leftReady : rightReady,
                length, Vector3.forward * .45f, movementScale, out var scale))
            {
                if (left) leftReachScale = scale; else rightReachScale = scale;
            }
        }
        public Vector3 MappedDelta(bool left, Vector3 delta)
        {
            var arm = left ? rig.left : rig.right; var ready = left ? leftReady : rightReady;
            return ControllerArmFollower.MapWorkspace(transform.InverseTransformPoint(arm.upper.position), ready, delta,
                (arm.upperLength + arm.lowerLength) / transform.lossyScale.x,
                left ? leftReachScale : rightReachScale, left ? leftTransverseScale : rightTransverseScale) - ready;
        }

        public void SetLocalCamera(Camera camera)
        {
            localView.Restore(); localCamera = camera;
            // Layer 30 is reserved for the selected pilot's head in this test scene.
            // The other pilot and spectator cameras keep seeing the complete model.
            if (camera) localView.ConfigureLocalCamera(camera, 30);
        }

        public void ResetGuard()
        {
            leftRotationDelta = rightRotationDelta = Quaternion.identity;
            leftElbowLift = rightElbowLift = 0; leftConstraint.Reset(); rightConstraint.Reset();
            rig.left.target.SetPositionAndRotation(transform.TransformPoint(leftReady), transform.rotation * leftReadyRotation);
            rig.right.target.SetPositionAndRotation(transform.TransformPoint(rightReady), transform.rotation * rightReadyRotation);
            rig.headTarget.rotation = transform.rotation;
            rig.left.rollInitialized = rig.right.rollInitialized = false;
            rig.left.pole.position = transform.TransformPoint(new Vector3(-.6f, 1.18f, -.16f));
            rig.right.pole.position = transform.TransformPoint(new Vector3(.6f, 1.18f, -.16f));
            rig.leftGrip = rig.rightGrip = 1;
            Evaluate();
        }

        public void Follow(bool left, Vector3 controllerDelta, Quaternion rotationDelta, float elbowLift = 0)
        {
            if (left) { leftRotationDelta = rotationDelta; leftElbowLift = elbowLift; }
            else { rightRotationDelta = rotationDelta; rightElbowLift = elbowLift; }
            var arm = left ? rig.left : rig.right;
            // Metres at human scale; the robot applies its own motionScale downstream.
            arm.target.SetPositionAndRotation(transform.TransformPoint((left ? leftReady : rightReady) + MappedDelta(left, controllerDelta)),
                transform.rotation * rotationDelta * (left ? leftReadyRotation : rightReadyRotation));
            arm.pole.position = transform.TransformPoint(Vector3.Lerp(new Vector3(left ? -.6f : .6f, 1.18f, -.16f),
                new Vector3(left ? -.8f : .8f, 1.55f, -.2f), elbowLift));
            Evaluate();
        }

        public bool CalibrateReach(bool left, Vector3 delta)
        {
            var arm = left ? rig.left : rig.right;
            if (!ControllerArmFollower.ValidReachDelta(delta)) return false;
            if (!ControllerArmFollower.TryWorkspaceReachScale(transform.InverseTransformPoint(arm.upper.position), left ? leftReady : rightReady,
                (arm.upperLength + arm.lowerLength) / transform.lossyScale.x, delta,
                left ? leftTransverseScale : rightTransverseScale, out var scale)) return false;
            if (left) leftReachScale = scale; else rightReachScale = scale;
            return true;
        }

        public void Evaluate()
        {
            if (!rig || !rig.IsReady) return;
            // Existing saved scenes also need the new defaults; Configure runs only
            // when the editor builds a scene, not when that scene enters Play mode.
            if (!reachInitialized) { SetStartingReach(true); SetStartingReach(false); reachInitialized = true; }
            // Keep the existing review solver's WRIST rotations disabled. Apply
            // controller orientation through elbow position and the whole forearm below.
            rig.maxWristFlex = rig.maxWristDeviation = rig.maxForearmRoll = 0;
            rig.leftGrip = rig.rightGrip = 1;
            SetBasePole(rig.left, true, leftElbowLift); SetBasePole(rig.right, false, rightElbowLift);
            if (localCamera) rig.headTarget.rotation = localCamera.transform.rotation;
            rig.Evaluate();
            if (!guardAligned)
            {
                AlignGuard(rig.left); AlignGuard(rig.right);
                guardAligned = true; rig.Evaluate();
                leftGuardFrame = Quaternion.Inverse(transform.rotation) * rig.HandFrame(rig.left);
                rightGuardFrame = Quaternion.Inverse(transform.rotation) * rig.HandFrame(rig.right);
            }
            var leftDesired = transform.rotation * leftRotationDelta * leftGuardFrame;
            var rightDesired = transform.rotation * rightRotationDelta * rightGuardFrame;
            SetRotationPole(rig.left, leftConstraint, leftDesired, leftRotationDelta);
            SetRotationPole(rig.right, rightConstraint, rightDesired, rightRotationDelta);
            rig.Evaluate();
            RigidWristConstraint.AlignForearm(rig.left.lower, rig.left.hand, rig.left.handRest, rig.HandFrame(rig.left) * Vector3.up, leftDesired * Vector3.up);
            RigidWristConstraint.AlignForearm(rig.right.lower, rig.right.hand, rig.right.handRest, rig.HandFrame(rig.right) * Vector3.up, rightDesired * Vector3.up);
            // Eye position is authoritative. Never parent the camera to a driven bone:
            // that would apply HMD movement twice and feed IK back into tracking.
            if (localCamera && eyeAnchor && headBone)
                headBone.position += localCamera.transform.position - eyeAnchor.position;
        }

        void SetBasePole(PilotReviewRig.Arm arm, bool left, float lift)
        {
            arm.pole.position = transform.TransformPoint(Vector3.Lerp(new Vector3(left ? -.6f : .6f, 1.18f, -.16f),
                new Vector3(left ? -.8f : .8f, 1.55f, -.2f), lift));
        }
        void SetRotationPole(PilotReviewRig.Arm arm, RigidWristConstraint constraint, Quaternion desired, Quaternion rotationDelta)
        {
            var axis = (arm.hand.position - arm.upper.position).normalized;
            var bend = Vector3.ProjectOnPlane(arm.lower.position - arm.upper.position, axis);
            if (bend.sqrMagnitude < .000001f) return;
            bend = constraint.Bend(axis, bend.normalized, desired * Vector3.forward, rotationDelta);
            arm.pole.position = arm.upper.position + bend;
        }

        void AlignGuard(PilotReviewRig.Arm arm)
        {
            var axis = (arm.hand.position - arm.lower.position).normalized;
            var back = Vector3.ProjectOnPlane(transform.forward, axis);
            if (back.sqrMagnitude < .001f) return;
            float roll = Vector3.SignedAngle(rig.HandFrame(arm) * Vector3.up, back, axis);
            arm.hand.rotation = Quaternion.AngleAxis(roll, axis) * arm.hand.rotation;
            // A constant authored reference offset, never a live wrist rotation.
            arm.handRest = arm.hand.localRotation;
        }

        void LateUpdate() => Evaluate();
        void OnDisable() { if (localView) localView.Restore(); localCamera = null; }
    }
}
