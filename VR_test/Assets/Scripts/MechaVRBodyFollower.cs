using UnityEngine;

// Update the body before MechaVRHandTarget (-100) and the Animator.
[DefaultExecutionOrder(-200)]
[DisallowMultipleComponent]
public sealed class MechaVRBodyFollower : MonoBehaviour
{
    public Transform head;
    public Transform trackingOrigin;
    public Transform bodyTrackingSpace;
    public Transform mechaHeadSpace;
    [Tooltip("Use the headset's horizontal heading for the estimated torso. Pitch and roll do not tilt the torso.")]
    public bool followHeadYaw = true;

    private void Update() => ApplyPose();

    public void ApplyPose()
    {
        if (!head || !bodyTrackingSpace || !mechaHeadSpace || mechaHeadSpace.parent != transform)
            return;

        Vector3 forward = followHeadYaw || !trackingOrigin ? head.forward : trackingOrigin.forward;
        forward.y = 0f;
        Quaternion heading = forward.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(forward.normalized, Vector3.up)
            : Quaternion.Euler(0f, transform.eulerAngles.y, 0f);

        // Human and mech frames share the same world origin and heading. Their
        // scale difference changes arm reach without adding body movement twice.
        bodyTrackingSpace.SetPositionAndRotation(head.position, heading);
        transform.rotation = heading;
        transform.position = head.position - transform.TransformVector(mechaHeadSpace.localPosition);
    }
}
