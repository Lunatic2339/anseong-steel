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
        public float ReachError { get; private set; }
        public float TargetError { get; private set; }
        public Vector3 RequestedTarget { get; private set; }
        public bool IsReady { get; private set; }
        Vector3 upperAxis, forearmAxis, previousBend;
        float upperLength, forearmLength;

        public void Initialize()
        {
            if (!upper || !forearm || !hand || !robotFrame) { IsReady = false; return; }
            upperLength = Vector3.Distance(upper.position, forearm.position);
            forearmLength = Vector3.Distance(forearm.position, hand.position);
            upperAxis = upper.InverseTransformDirection(forearm.position - upper.position);
            forearmAxis = forearm.InverseTransformDirection(hand.position - forearm.position);
            IsReady = upperLength > .01f && forearmLength > .01f;
        }

        public void Follow(Vector3 controllerDelta, Quaternion controllerRotationDelta)
        {
            if (!IsReady) Initialize();
            if (!IsReady || !PunchTrajectoryRecognizer.Finite(controllerDelta) || !PunchTrajectoryRecognizer.Finite(controllerRotationDelta)) return;
            RequestedTarget = robotFrame.TransformPoint(neutralTarget + controllerDelta * motionScale);
            Vector3 fromShoulder = RequestedTarget - upper.position;
            Vector3 axis = fromShoulder.sqrMagnitude > .00001f ? fromShoulder.normalized : robotFrame.forward;
            float distance = Mathf.Clamp(fromShoulder.magnitude, Mathf.Abs(upperLength - forearmLength) + .005f, upperLength + forearmLength - .005f);
            Vector3 goal = upper.position + axis * distance;
            Vector3 bend = Vector3.ProjectOnPlane(robotFrame.TransformDirection(bendDirection), axis);
            if (bend.sqrMagnitude < .01f) bend = Vector3.ProjectOnPlane(previousBend, axis);
            if (bend.sqrMagnitude < .01f) bend = Vector3.ProjectOnPlane(robotFrame.up, axis);
            if (bend.sqrMagnitude < .01f) bend = Vector3.ProjectOnPlane(robotFrame.right, axis);
            bend.Normalize(); previousBend = bend;
            float along = (upperLength * upperLength - forearmLength * forearmLength + distance * distance) / (2 * distance);
            float height = Mathf.Sqrt(Mathf.Max(0, upperLength * upperLength - along * along));
            Vector3 elbow = upper.position + axis * along + bend * height;
            upper.rotation = Quaternion.FromToRotation(upper.TransformDirection(upperAxis), elbow - upper.position) * upper.rotation;
            forearm.rotation = Quaternion.FromToRotation(forearm.TransformDirection(forearmAxis), goal - forearm.position) * forearm.rotation;
            hand.rotation = robotFrame.rotation * controllerRotationDelta * neutralWrist;
            ReachError = Vector3.Distance(RequestedTarget, goal);
            TargetError = Vector3.Distance(goal, hand.position);
            if (targetMarker) targetMarker.position = RequestedTarget;
            if (actualMarker) actualMarker.position = hand.position;
        }
    }
}
