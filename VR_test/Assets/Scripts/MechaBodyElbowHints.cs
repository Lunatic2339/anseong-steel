using UnityEngine;
using UnityEngine.Animations.Rigging;

[DefaultExecutionOrder(100)]
public class MechaBodyElbowHints : MonoBehaviour
{
    public OVRSkeleton skeleton;
    public Transform bodyTrackingSpace;
    public Transform mechaHeadSpace;
    public Transform leftHint;
    public Transform rightHint;
    public float smoothing = 15f;

    Vector3 leftRest, rightRest;
    TwoBoneIKConstraint leftIK, rightIK;
    readonly MechaArmStability.PoleState leftState = new MechaArmStability.PoleState();
    readonly MechaArmStability.PoleState rightState = new MechaArmStability.PoleState();

    void Start()
    {
        if (leftHint) { leftRest = leftHint.localPosition; leftIK = leftHint.GetComponentInParent<TwoBoneIKConstraint>(); }
        if (rightHint) { rightRest = rightHint.localPosition; rightIK = rightHint.GetComponentInParent<TwoBoneIKConstraint>(); }
    }

    void Update()
    {
        if (!bodyTrackingSpace || !mechaHeadSpace) return;
        UpdateHint(leftHint, OVRSkeleton.BoneId.Body_LeftArmLower, leftRest, leftIK, leftState);
        UpdateHint(rightHint, OVRSkeleton.BoneId.Body_RightArmLower, rightRest, rightIK, rightState);
    }

    void UpdateHint(Transform hint, OVRSkeleton.BoneId jointId, Vector3 rest,
        TwoBoneIKConstraint ik, MechaArmStability.PoleState state)
    {
        if (!hint) return;
        Vector3 restWorld = hint.parent ? hint.parent.TransformPoint(rest) : rest;
        Vector3 desired = restWorld; bool tracked = false;
        if (skeleton && skeleton.IsInitialized && skeleton.IsDataValid && skeleton.Bones != null)
            foreach (var bone in skeleton.Bones)
                if (bone.Id == jointId && bone.Transform)
                {
                    desired = mechaHeadSpace.TransformPoint(bodyTrackingSpace.InverseTransformPoint(bone.Transform.position));
                    tracked = true; break;
                }
        float blend = 1f - Mathf.Exp(-Mathf.Max(0f, smoothing) * Time.deltaTime);
        if (ik && ik.data.root && ik.data.mid && ik.data.tip && ik.data.target)
        {
            var d = ik.data;
            float length = Vector3.Distance(d.root.position, d.mid.position) + Vector3.Distance(d.mid.position, d.tip.position);
            var hand = ik.GetComponent<MechaVRHandTarget>();
            float reach = hand && hand.IsCalibrated ? hand.RawReachRatio : Vector3.Distance(d.root.position, d.target.position) / Mathf.Max(length, .00001f);
            hint.position = MechaArmStability.StableHint(d.root.position, d.target.position,
                desired, restWorld, mechaHeadSpace.rotation, length, reach, tracked, blend, state, d.target.rotation);
        }
        else hint.position = Vector3.Lerp(hint.position, desired, blend);
    }
}
