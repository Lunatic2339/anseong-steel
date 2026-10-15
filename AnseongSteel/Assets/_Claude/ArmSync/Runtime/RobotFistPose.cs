using UnityEngine;

namespace AnseongSteel.ArmSync
{
    // Locks a Mixamo-named robot hand into a closed fist.
    // Computed once from the model's rest pose with geometric tests only (no assumed
    // bone axes or Euler signs), so the mirrored left and right hands both close correctly.
    // The same steps were rendered in Blender against mecha-finger-rig-mixamo-v026.fbx.
    [DefaultExecutionOrder(300)]
    public sealed class RobotFistPose : MonoBehaviour
    {
        [Tooltip("\"Left\" or \"Right\", as in mixamorig:LeftHandIndex1")]
        public string side = "Left";
        [Tooltip("Curl of the first, second and third finger joints, degrees")]
        public Vector3 fingerCurl = new Vector3(80, 95, 65);
        [Tooltip("How far in front of the folded fingers the thumb tip rests, in palm lengths")]
        public float thumbOut = .45f;

        public bool Ready { get; private set; }
        // Hand-local directions taken from the rest pose: wrist to middle knuckle, and palm normal.
        public Vector3 KnuckleLocal { get; private set; }
        public Vector3 PalmLocal { get; private set; }

        static readonly string[] Fingers = { "Index", "Middle", "Ring", "Pinky" };
        Transform[] joints;
        Quaternion[] closed;

        void Awake() => Build();

        // Safe to call more than once; the fist is built from the rest pose only the first time.
        public void Build()
        {
            if (Ready) return;
            var hand = transform;
            joints = new Transform[15];
            for (int f = 0; f < 4; f++) for (int j = 0; j < 3; j++) joints[f * 3 + j] = Bone(Fingers[f] + (j + 1));
            for (int j = 0; j < 3; j++) joints[12 + j] = Bone("Thumb" + (j + 1));
            foreach (var t in joints) if (!t) { Debug.LogWarning($"RobotFistPose: finger bones for {side} hand not found under {name}", this); return; }

            Vector3 H = hand.position;
            Vector3 center = Vector3.zero;
            for (int f = 0; f < 4; f++) center += joints[f * 3].position;
            center /= 4;
            Vector3 forward = (center - H).normalized;
            Vector3 across = (joints[9].position - joints[0].position).normalized; // pinky1 - index1
            Vector3 inward = Vector3.Cross(forward, across).normalized;
            // The rest pose already has a relaxed curl; it tells us which side is the palm.
            Vector3 curvature = Vector3.ProjectOnPlane(joints[5].position - joints[3].position, forward); // middle3 - middle1
            if (Vector3.Dot(curvature, inward) < 0) inward = -inward;
            KnuckleLocal = hand.InverseTransformDirection(forward);
            PalmLocal = hand.InverseTransformDirection(inward);

            Vector3 curl = Vector3.Cross(forward, inward).normalized;
            Vector3 d = (joints[4].position - joints[3].position).normalized; // middle2 - middle1
            if (Vector3.Dot(Quaternion.AngleAxis(10, curl) * d, inward) < Vector3.Dot(d, inward)) curl = -curl;
            for (int f = 0; f < 4; f++)
                for (int j = 0; j < 3; j++)
                {
                    var t = joints[f * 3 + j];
                    t.rotation = Quaternion.AngleAxis(fingerCurl[j], curl) * t.rotation;
                }

            // Thumb: small CCD so the thumb tip rests over the folded index and middle fingers.
            float palm = Vector3.Distance(H, joints[3].position);
            Vector3 target = (joints[1].position + joints[4].position) * .5f + inward * palm * thumbOut;
            Transform t1 = joints[12], t2 = joints[13], t3 = joints[14];
            float l3 = Vector3.Distance(t3.position, t2.position);
            for (int i = 0; i < 12; i++)
            {
                Aim(t2, Tip(t2, t3, l3) - t2.position, target - t2.position);
                Aim(t1, Tip(t2, t3, l3) - t1.position, target - t1.position);
            }
            Aim(t3, Tip(t2, t3, l3) - t3.position, target + across * palm * .6f - t3.position);

            closed = new Quaternion[joints.Length];
            for (int i = 0; i < joints.Length; i++) closed[i] = joints[i].localRotation;
            Ready = true;
        }

        Transform Bone(string n) => Find(transform, "mixamorig:" + side + "Hand" + n);
        static Vector3 Tip(Transform t2, Transform t3, float l3) => t3.position + (t3.position - t2.position).normalized * l3;

        static void Aim(Transform joint, Vector3 from, Vector3 to)
        {
            if (from.sqrMagnitude > 1e-10f && to.sqrMagnitude > 1e-10f)
                joint.rotation = Quaternion.FromToRotation(from, to) * joint.rotation;
        }

        // Re-apply every frame in case an Animator or another script touches the fingers.
        void LateUpdate()
        {
            if (!Ready) return;
            for (int i = 0; i < joints.Length; i++) joints[i].localRotation = closed[i];
        }

        public static Transform Find(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var found = Find(root.GetChild(i), name);
                if (found) return found;
            }
            return null;
        }
    }
}
