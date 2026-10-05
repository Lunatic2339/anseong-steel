using System;
using System.Linq;
using UnityEngine;

namespace AnseongSteel.PlayerMotion
{
    // Serialized default pose, independent of tracking, calibration and grip buttons.
    [DefaultExecutionOrder(250)]
    public sealed class DefaultFistPose : MonoBehaviour
    {
        public Transform[] joints = Array.Empty<Transform>();
        public Quaternion[] closedRotations = Array.Empty<Quaternion>();
        public Quaternion[] restRotations = Array.Empty<Quaternion>();
        public Vector3 palmForwardLocal, palmInwardLocal;

        public void Configure(Transform hand, string side)
        {
            var children = hand.GetComponentsInChildren<Transform>();
            Transform Bone(string finger, int joint) => children.First(t => t.name == "mixamorig:" + side + "Hand" + finger + joint);
            var ordered = new System.Collections.Generic.List<Transform>();
            foreach (var finger in new[] { "Index", "Middle", "Ring", "Pinky", "Thumb" })
                for (int joint = 1; joint <= 3; joint++) ordered.Add(Bone(finger, joint));
            joints = ordered.ToArray(); restRotations = joints.Select(j => j.localRotation).ToArray();

            var index = Bone("Index", 1); var middle = Bone("Middle", 1); var pinky = Bone("Pinky", 1);
            Vector3 forward = (((index.position + middle.position + pinky.position) / 3) - hand.position).normalized;
            Vector3 across = (pinky.position - index.position).normalized;
            Vector3 inward = Vector3.Cross(forward, across).normalized;
            // v26 already has a relaxed bend. Its curvature identifies the palm side
            // without assuming opposite Euler signs on mirrored FBX bones.
            var curvature = Vector3.ProjectOnPlane(Bone("Middle", 3).position - middle.position, forward);
            if (Vector3.Dot(curvature, inward) < 0) inward = -inward;
            palmForwardLocal = hand.InverseTransformDirection(forward); palmInwardLocal = hand.InverseTransformDirection(inward);
            Vector3 axis = Vector3.Cross(forward, inward).normalized;
            var localAxes = joints.Select(j => j.InverseTransformDirection(axis)).ToArray();
            var angles = new[] { 55f, 45f, 25f };
            for (int i = 0; i < 12; i++) joints[i].localRotation = restRotations[i] * Quaternion.AngleAxis(angles[i % 3], localAxes[i]);

            // Place the thumb over the folded fingers on the palm-facing side.
            // Positions and lengths remain untouched; only joint rotations change.
            float palmLength = Vector3.Distance(hand.position, middle.position);
            Vector3 thumbTarget = (index.position + middle.position) * .5f + inward * palmLength * .55f;
            var thumb1 = Bone("Thumb", 1); var thumb2 = Bone("Thumb", 2); var thumb3 = Bone("Thumb", 3);
            Aim(thumb1, thumb2.position - thumb1.position, thumbTarget - thumb1.position);
            Aim(thumb2, thumb3.position - thumb2.position, thumbTarget - thumb2.position);
            Aim(thumb3, thumb3.position - thumb2.position, across + inward * .15f);
            closedRotations = joints.Select(j => j.localRotation).ToArray();
        }
        static void Aim(Transform joint, Vector3 from, Vector3 to)
        { if (from.sqrMagnitude > .000001f && to.sqrMagnitude > .000001f) joint.rotation = Quaternion.FromToRotation(from, to) * joint.rotation; }
        public void Apply()
        {
            if (joints.Length != closedRotations.Length) return;
            for (int i = 0; i < joints.Length; i++) if (joints[i]) joints[i].localRotation = closedRotations[i];
        }
        void OnEnable() { Apply(); }
        void LateUpdate() { Apply(); }
    }
}
