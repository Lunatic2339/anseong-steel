using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(-100)]
public sealed class MechaVRHandTarget : MonoBehaviour
{
    [Tooltip("Unscaled tracking frame. Body follow uses BodyTrackingSpace at the headset.")]
    public Transform trackingSpace;
    [Tooltip("Mech retargeting frame. Body follow uses MechaHeadSpace under MechaRoot.")]
    public Transform mechaSpace;
    [Tooltip("The tracked controller grip transform, not its ray tip.")]
    public Transform controller;
    [Tooltip("The corresponding Two Bone IK Target, not a driven bone.")]
    public Transform target;
    [Min(0.01f)] public float movementGain = 1f;
    [Tooltip("Map the controller position directly between matching body/head frames. Used by the player-following rig.")]
    public bool alignPositionToController;

    private Vector3 controllerReference;
    private Vector3 targetReference;
    private Quaternion controllerRotationReference;
    private Quaternion targetRotationReference;
    private bool calibrated;
    private float calibrateAt = -1f;
    private int calibrationCount;

    private bool HasReferences => trackingSpace != null && mechaSpace != null
        && controller != null && target != null;

    public bool IsCalibrated => calibrated;
    public int CalibrationCount => calibrationCount;
    public string Status => !HasReferences ? "Missing a required Transform reference"
        : calibrateAt >= 0f ? "Calibrating in " + Mathf.Max(0f, calibrateAt - Time.unscaledTime).ToString("F1") + " s"
        : calibrated ? "Following controller" : "Waiting for F8 / Calibrate button";

    private void Start()
    {
        if (!HasReferences)
            Debug.LogError("[MechaVR] " + name + ": " + Status, this);
        else
            Debug.Log("[MechaVR] " + name + ": ready. Press F8 in the Game view, or use Calibrate in the Inspector.", this);
    }

    private void Update()
    {
        if (!HasReferences)
            return;

        if (Keyboard.current != null && Keyboard.current.f8Key.wasPressedThisFrame)
            BeginCalibration();

        if (calibrateAt >= 0f && Time.unscaledTime >= calibrateAt)
            Calibrate();

        ApplyTargetPose();
    }

    public void ApplyTargetPose()
    {
        if (!HasReferences || !calibrated)
            return;

        Vector3 position = trackingSpace.InverseTransformPoint(controller.position);
        Quaternion rotation = Quaternion.Inverse(trackingSpace.rotation) * controller.rotation;
        Vector3 localTarget = alignPositionToController
            ? position * movementGain
            : targetReference + (position - controllerReference) * movementGain;
        Quaternion localRotation = rotation * Quaternion.Inverse(controllerRotationReference) * targetRotationReference;

        target.SetPositionAndRotation(mechaSpace.TransformPoint(localTarget), mechaSpace.rotation * localRotation);
    }

    public void BeginCalibration()
    {
        if (!Application.isPlaying || !HasReferences)
            return;
        calibrateAt = Time.unscaledTime + 3f;
        Debug.Log("[MechaVR] " + name + ": calibration requested; hold the reference pose for 3 seconds.", this);
    }

    [ContextMenu("Calibrate In Play Mode")]
    public void Calibrate()
    {
        if (!Application.isPlaying || !HasReferences)
            return;

        controllerReference = trackingSpace.InverseTransformPoint(controller.position);
        controllerRotationReference = Quaternion.Inverse(trackingSpace.rotation) * controller.rotation;
        targetReference = mechaSpace.InverseTransformPoint(target.position);
        targetRotationReference = Quaternion.Inverse(mechaSpace.rotation) * target.rotation;
        calibrateAt = -1f;
        calibrated = true;
        calibrationCount++;
        Debug.Log("[MechaVR] " + name + ": calibration complete. Move the controller to move the IK target.", this);
    }
}
