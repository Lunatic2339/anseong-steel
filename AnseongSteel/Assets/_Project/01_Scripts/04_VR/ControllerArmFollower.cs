using UnityEngine;

namespace AnseongSteel.PlayerMotion
{
    // Pose input only. There is deliberately no reference to the punch recognizer.
    public sealed class ControllerArmFollower : MonoBehaviour
    {
        public Transform upper, forearm, hand, robotFrame, targetMarker, actualMarker;
        public Vector3 bendDirection = new Vector3(-1, -.5f, -.2f);
        public Vector3 neutralTarget = new Vector3(-.65f, 1.35f, .55f);
        public Quaternion neutralWrist = Quaternion.identity;
        public float motionScale = 2.4f;
        [Min(.15f)] public float defaultForwardReach = .45f;
        [Range(0, 1)] public float elbowLift;
        public float Extension => IsReady ? Vector3.Distance(upper.position, hand.position) / (upperLength + forearmLength) : 0;
        public float ReachError { get; private set; }
        public float TargetError { get; private set; }
        public Vector3 RequestedTarget { get; private set; }
        public bool IsReady { get; private set; }
        public Quaternion LockedWristRotation { get; private set; }
        public Quaternion HandFrame => Quaternion.LookRotation(hand.TransformDirection(handForwardLocal), hand.TransformDirection(handUpLocal));
        Vector3 handForwardLocal = Vector3.up, handUpLocal = Vector3.forward;
        Vector3 upperAxis, forearmAxis, previousBend;
        float upperLength, forearmLength, transverseScale;
        bool alignGuard;
        Quaternion guardFrameLocal;
        readonly RigidWristConstraint wristConstraint = new RigidWristConstraint();

        public void Initialize()
        {
            if (!upper || !forearm || !hand || !robotFrame) { IsReady = false; return; }
            upperLength = Vector3.Distance(upper.position, forearm.position);
            forearmLength = Vector3.Distance(forearm.position, hand.position);
            upperAxis = upper.InverseTransformDirection(forearm.position - upper.position);
            forearmAxis = forearm.InverseTransformDirection(hand.position - forearm.position);
            var fist = hand.GetComponent<DefaultFistPose>();
            if (fist) { handForwardLocal = fist.palmForwardLocal; handUpLocal = -fist.palmInwardLocal; }
            // The saved prefab may already have a bent wrist. Align the palm's
            // longitudinal axis with the forearm, then keep that LOCAL rotation.
            Vector3 forearmDirection = hand.parent.InverseTransformDirection(hand.position - forearm.position);
            LockedWristRotation = Quaternion.FromToRotation(hand.localRotation * handForwardLocal, forearmDirection) * hand.localRotation;
            alignGuard = true;
            wristConstraint.Reset();
            IsReady = upperLength > .01f && forearmLength > .01f;
            float length = (upperLength + forearmLength) / robotFrame.lossyScale.x;
            transverseScale = length / .65f; // Approximate human shoulder-to-wrist length.
            // Only forward extension uses the reach calibration; rearward and lateral
            // travel retain the limb-size gain even when the guard is near the limit.
            if (IsReady && TryWorkspaceReachScale(robotFrame.InverseTransformPoint(upper.position), neutralTarget,
                length, Vector3.forward * defaultForwardReach, transverseScale, out var startingScale))
                motionScale = startingScale;
        }

        public void Follow(Vector3 controllerDelta, Quaternion controllerRotationDelta)
        {
            if (!IsReady) Initialize();
            if (!IsReady || !PunchTrajectoryRecognizer.Finite(controllerDelta) || !PunchTrajectoryRecognizer.Finite(controllerRotationDelta)) return;
            RequestedTarget = robotFrame.TransformPoint(MapWorkspace(robotFrame.InverseTransformPoint(upper.position), neutralTarget, controllerDelta, (upperLength + forearmLength) / robotFrame.lossyScale.x, motionScale, transverseScale));
            Vector3 fromShoulder = RequestedTarget - upper.position;
            Vector3 axis = fromShoulder.sqrMagnitude > .00001f ? fromShoulder.normalized : robotFrame.forward;
            float distance = Mathf.Clamp(fromShoulder.magnitude, Mathf.Abs(upperLength - forearmLength) + .005f, upperLength + forearmLength - .005f);
            Vector3 goal = upper.position + axis * distance;
            var raisedBend = new Vector3(Mathf.Sign(bendDirection.x), .2f, -.3f);
            Vector3 bend = Vector3.ProjectOnPlane(robotFrame.TransformDirection(Vector3.Lerp(bendDirection, raisedBend, elbowLift)), axis);
            if (bend.sqrMagnitude < .01f) bend = Vector3.ProjectOnPlane(previousBend, axis);
            if (bend.sqrMagnitude < .01f) bend = Vector3.ProjectOnPlane(robotFrame.up, axis);
            if (bend.sqrMagnitude < .01f) bend = Vector3.ProjectOnPlane(robotFrame.right, axis);
            bend.Normalize(); previousBend = bend;
            var desiredFrame = robotFrame.rotation * controllerRotationDelta * guardFrameLocal;
            if (!alignGuard) bend = wristConstraint.Bend(axis, bend, desiredFrame * Vector3.forward, controllerRotationDelta);
            float along = (upperLength * upperLength - forearmLength * forearmLength + distance * distance) / (2 * distance);
            float height = Mathf.Sqrt(Mathf.Max(0, upperLength * upperLength - along * along));
            Vector3 elbow = upper.position + axis * along + bend * height;
            upper.rotation = Quaternion.FromToRotation(upper.TransformDirection(upperAxis), elbow - upper.position) * upper.rotation;
            forearm.rotation = Quaternion.FromToRotation(forearm.TransformDirection(forearmAxis), goal - forearm.position) * forearm.rotation;
            hand.localRotation = LockedWristRotation;
            if (alignGuard && controllerDelta.sqrMagnitude < .000001f)
            {
                // Straightness alone leaves the palm's roll undefined. Set the
                // reference in the SOLVED guard pose, not the imported T pose.
                var axisAlongArm = (hand.position - forearm.position).normalized;
                var backOfHand = Vector3.ProjectOnPlane(robotFrame.forward, axisAlongArm);
                if (backOfHand.sqrMagnitude > .001f)
                {
                    float roll = Vector3.SignedAngle(HandFrame * Vector3.up, backOfHand, axisAlongArm);
                    hand.rotation = Quaternion.AngleAxis(roll, axisAlongArm) * hand.rotation;
                    LockedWristRotation = hand.localRotation; alignGuard = false;
                    guardFrameLocal = Quaternion.Inverse(robotFrame.rotation) * HandFrame;
                }
            }
            if (!alignGuard)
            {
                desiredFrame = robotFrame.rotation * controllerRotationDelta * guardFrameLocal;
                RigidWristConstraint.AlignForearm(forearm, hand, LockedWristRotation, HandFrame * Vector3.up, desiredFrame * Vector3.up);
            }
            ReachError = Vector3.Distance(RequestedTarget, goal);
            TargetError = Vector3.Distance(goal, hand.position);
            if (targetMarker) targetMarker.position = RequestedTarget;
            if (actualMarker) actualMarker.position = hand.position;
        }

        public bool CalibrateReach(Vector3 fullyExtendedDelta)
        {
            if (!IsReady) Initialize();
            if (!IsReady || !ValidReachDelta(fullyExtendedDelta)) return false;
            if (!TryWorkspaceReachScale(robotFrame.InverseTransformPoint(upper.position), neutralTarget,
                (upperLength + forearmLength) / robotFrame.lossyScale.x, fullyExtendedDelta, transverseScale, out var scale)) return false;
            motionScale = scale; return true;
        }
        // Keep the interior workspace responsive. Compress only radial movement
        // near full extension; tangential sweep around the shoulder is retained.
        static float SoftStart(Vector3 shoulder, Vector3 ready, float length)
            => Mathf.Clamp(Mathf.Max(.78f, Vector3.Distance(shoulder, ready) / length + .015f), .78f, .95f) * length;
        public static Vector3 MapWorkspace(Vector3 shoulder, Vector3 ready, Vector3 delta, float length, float forwardScale, float movementScale)
        {
            float z = delta.z;
            // C1 transition at guard: small movements in either direction use the
            // same gain. Reach calibration gradually takes over in front of guard.
            var raw = ready + delta * movementScale;
            if (z > 0)
                raw.z = ready.z + forwardScale * z + (movementScale - forwardScale) * ForwardTransition(z);
            var offset = raw - shoulder; float distance = offset.magnitude;
            float start = SoftStart(shoulder, ready, length), span = .995f * length - start;
            if (distance > start)
                offset *= (start + span * (1 - Mathf.Exp(-(distance - start) / span))) / distance;
            return shoulder + offset;
        }
        static float ForwardTransition(float z) => .05f * (1 - Mathf.Exp(-z / .05f));
        public static bool TryWorkspaceReachScale(Vector3 shoulder, Vector3 ready, float length, Vector3 delta, float movementScale, out float scale)
        {
            scale = 1;
            if (!ValidReachDelta(delta)) return false;
            float start = SoftStart(shoulder, ready, length), span = .995f * length - start;
            // Invert the radial compression so E still means 96% actual extension.
            float rawDistance = start - span * Mathf.Log(1 - (.96f * length - start) / span);
            var sideReady = ready + new Vector3(delta.x, delta.y, 0) * movementScale;
            if (!TryReachScale(shoulder, sideReady, rawDistance / .96f, Vector3.forward * delta.z, out var linearScale)) return false;
            float transition = ForwardTransition(delta.z);
            scale = (linearScale * delta.z - movementScale * transition) / (delta.z - transition);
            return float.IsFinite(scale) && scale > 0 && scale <= 12;
        }
        public static bool ValidReachDelta(Vector3 delta)
            => PunchTrajectoryRecognizer.Finite(delta) && delta.z >= .15f && Mathf.Abs(delta.x) <= delta.z * .65f;

        public static bool TryReachScale(Vector3 shoulder, Vector3 ready, float armLength, Vector3 delta, out float scale)
        {
            scale = 1;
            if (!PunchTrajectoryRecognizer.Finite(delta) || delta.z < .15f || Mathf.Abs(delta.x) > delta.z * .65f) return false;
            // Intersect the ray from the ready wrist with a sphere at 96% arm reach.
            var n = ready - shoulder; float a = delta.sqrMagnitude, b = Vector3.Dot(n, delta);
            float c = n.sqrMagnitude - armLength * armLength * .96f * .96f;
            float discriminant = b * b - a * c;
            if (discriminant <= 0) return false;
            scale = (-b + Mathf.Sqrt(discriminant)) / a;
            return float.IsFinite(scale) && scale >= .15f && scale <= 12;
        }
    }

    // A continuous IK hint, never a punch classification or animation selection.
    public sealed class ControllerElbowHint
    {
        Vector3 previous; bool hasPrevious;
        public float Lift { get; private set; }
        public void Reset() { previous = Vector3.zero; hasPrevious = false; Lift = 0; }
        public float Update(Vector3 delta, float dt)
        {
            if (dt <= 0) return Lift;
            float lateralSpeed = hasPrevious ? Mathf.Abs(delta.x - previous.x) / dt : 0;
            previous = delta; hasPrevious = true;
            float lateral = Mathf.InverseLerp(.05f, .22f, Mathf.Abs(delta.x));
            float sweeping = Mathf.InverseLerp(.25f, 1.1f, lateralSpeed);
            float raised = Mathf.InverseLerp(.06f, .23f, delta.y);
            float cue = Mathf.Max(lateral, sweeping * .9f, raised * .8f);
            // Relax the hint as a straight punch approaches full extension.
            cue *= 1 - .5f * Mathf.InverseLerp(.3f, .6f, delta.z);
            Lift = Mathf.Lerp(Lift, cue, 1 - Mathf.Exp(-dt / (cue > Lift ? .055f : .18f)));
            return Lift;
        }
    }
}
