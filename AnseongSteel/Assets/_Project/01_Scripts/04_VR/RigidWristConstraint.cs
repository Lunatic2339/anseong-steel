using UnityEngine;

namespace AnseongSteel.PlayerMotion
{
    // With wrist position and bone lengths fixed, the elbow lies on a circle.
    // Choose a point on that circle using the controller's desired forearm direction.
    public sealed class RigidWristConstraint
    {
        float previousAngle, continuousAngle;
        bool hasAngle;
        public void Reset() { hasAngle = false; previousAngle = continuousAngle = 0; }

        public Vector3 Bend(Vector3 shoulderToWrist, Vector3 fallback, Vector3 desiredForearm, Quaternion rotationDelta)
        {
            var axis = shoulderToWrist.normalized;
            var normal = Vector3.ProjectOnPlane(fallback, axis).normalized;
            // Preserve the position-based hook hint at the calibrated guard orientation.
            float weight = Mathf.SmoothStep(0, 1, Quaternion.Angle(Quaternion.identity, rotationDelta) / 35f);
            if (weight < .0001f) { Reset(); return normal; }
            var desired = -Vector3.ProjectOnPlane(desiredForearm, axis);
            if (desired.sqrMagnitude < .0025f || normal.sqrMagnitude < .001f) return normal;
            float angle = Vector3.SignedAngle(normal, desired.normalized, axis);
            if (!hasAngle) { continuousAngle = angle; hasAngle = true; }
            else continuousAngle += Mathf.DeltaAngle(previousAngle, angle);
            previousAngle = angle;
            // Avoid choosing the opposite, anatomically implausible elbow branch.
            return Quaternion.AngleAxis(Mathf.Clamp(continuousAngle, -110, 110) * weight, axis) * normal;
        }

        public static void AlignForearm(Transform forearm, Transform hand, Quaternion lockedWrist, Vector3 currentBack, Vector3 desiredBack)
        {
            var axis = (hand.position - forearm.position).normalized;
            var target = Vector3.ProjectOnPlane(desiredBack, axis);
            var from = Vector3.ProjectOnPlane(currentBack, axis);
            if (target.sqrMagnitude > .001f && from.sqrMagnitude > .001f)
                forearm.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(from, target, axis), axis) * forearm.rotation;
            // The hand travels with the forearm. No flex/deviation/twist at the wrist joint.
            hand.localRotation = lockedWrist;
        }
    }
}
