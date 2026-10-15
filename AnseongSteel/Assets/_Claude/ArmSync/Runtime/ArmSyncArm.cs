using System;
using UnityEngine;

namespace AnseongSteel.ArmSync
{
    // Analytic two-bone IK for one robot arm (shoulder, elbow, wrist).
    // Rotations are expressed relative to the captured rest pose, so it works with any
    // bone axis convention. The elbow bends in the plane given by the pole direction.
    [Serializable]
    public sealed class ArmSyncArm
    {
        public Transform upper, lower, hand;
        public RobotFistPose fist;

        public float UpperLength { get; private set; }
        public float LowerLength { get; private set; }
        public float Length => UpperLength + LowerLength;
        public bool Ready { get; private set; }
        public bool Clamped { get; private set; }
        public Vector3 Shoulder => upper.position;

        Quaternion restUpper, restLower, restHand;
        Quaternion restUpperFrameInv, restLowerFrameInv;

        public void Capture()
        {
            Ready = false;
            if (!upper || !lower || !hand || !upper.parent || !lower.parent) return;
            Vector3 s = upper.position, e = lower.position, w = hand.position;
            UpperLength = Vector3.Distance(s, e);
            LowerLength = Vector3.Distance(e, w);
            if (UpperLength < 1e-4f || LowerLength < 1e-4f) return;
            Vector3 normal = Vector3.Cross(e - s, w - e);
            if (normal.sqrMagnitude < 1e-10f) normal = Vector3.Cross(e - s, Vector3.up);
            normal.Normalize();
            restUpper = upper.localRotation; restLower = lower.localRotation; restHand = hand.localRotation;
            restUpperFrameInv = Quaternion.Inverse(Quaternion.LookRotation(
                upper.parent.InverseTransformDirection(e - s), upper.parent.InverseTransformDirection(normal)));
            restLowerFrameInv = Quaternion.Inverse(Quaternion.LookRotation(
                lower.parent.InverseTransformDirection(w - e), lower.parent.InverseTransformDirection(normal)));
            Ready = true;
        }

        // Moves the wrist to target (clamped to reach). handRotation null keeps the rest wrist.
        public void Solve(Vector3 target, Vector3 pole, Quaternion? handRotation)
        {
            if (!Ready) return;
            Vector3 s = upper.position;
            Vector3 toTarget = target - s;
            float distance = toTarget.magnitude;
            Vector3 n = distance > 1e-5f ? toTarget / distance : (hand.position - s).normalized;
            float a = UpperLength, b = LowerLength;
            float min = Mathf.Abs(a - b) + .001f * (a + b), max = .999f * (a + b);
            Clamped = distance > max || distance < min;
            float d = Mathf.Clamp(distance, min, max);

            Vector3 p = Vector3.ProjectOnPlane(pole, n);
            if (p.sqrMagnitude < 1e-8f) p = Vector3.ProjectOnPlane(Vector3.down, n);
            if (p.sqrMagnitude < 1e-8f) p = Vector3.ProjectOnPlane(Vector3.forward, n);
            p.Normalize();
            float along = (a * a - b * b + d * d) / (2 * d);
            float h = Mathf.Sqrt(Mathf.Max(0, a * a - along * along));
            Vector3 elbow = s + n * along + p * h;
            Vector3 goal = s + n * d;
            // Same handedness as Capture: Cross(elbow - s, goal - elbow) points along Cross(p, n).
            Vector3 normal = Vector3.Cross(p, n).normalized;

            var up = upper.parent;
            upper.localRotation = Quaternion.LookRotation(up.InverseTransformDirection(elbow - s), up.InverseTransformDirection(normal))
                * restUpperFrameInv * restUpper;
            var lp = lower.parent;
            lower.localRotation = Quaternion.LookRotation(lp.InverseTransformDirection(goal - lower.position), lp.InverseTransformDirection(normal))
                * restLowerFrameInv * restLower;
            if (handRotation.HasValue) hand.rotation = handRotation.Value;
            else hand.localRotation = restHand;
        }
    }
}
