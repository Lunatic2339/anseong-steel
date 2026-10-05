using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

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
    private Quaternion gripToHandRotation = Quaternion.identity;
    private bool rotationOffsetInitialized;
    private bool calibrated;
    private TrackedPoseDriver controllerPoseDriver;
    private float calibrateAt = -1f;
    private int calibrationCount;

    private bool HasReferences => trackingSpace != null && mechaSpace != null
        && controller != null && target != null;

    public bool IsCalibrated => calibrated;
    public int CalibrationCount => calibrationCount;
    public string Status => !HasReferences ? "Missing a required Transform reference"
        : calibrateAt >= 0f ? "Calibrating in " + Mathf.Max(0f, calibrateAt - Time.unscaledTime).ToString("F1") + " s"
        : calibrated ? "Following controller" : "Waiting for controller tracking (automatic calibration)";

    private void Start()
    {
        controllerPoseDriver = controller != null ? controller.GetComponent<TrackedPoseDriver>() : null;
        if (!HasReferences)
            Debug.LogError("[MechaVR] " + name + ": " + Status, this);
        else
            Debug.Log("[MechaVR] " + name + ": automatic calibration enabled. Tracking starts as soon as the controller pose is ready; F8 recalibrates.", this);
    }

    private void Update()
    {
        if (!HasReferences)
            return;

        if (Keyboard.current != null && Keyboard.current.f8Key.wasPressedThisFrame)
            BeginCalibration();

        if (calibrateAt >= 0f && Time.unscaledTime >= calibrateAt)
            Calibrate();

        // Input System updates the controller pose before Update. Calibrate here
        // after body follow has aligned the tracking frames, without a timed delay.
        if (!calibrated && calibrateAt < 0f && HasTrackedControllerPose())
            Calibrate();

        ApplyTargetPose();
    }

    private bool HasTrackedControllerPose()
    {
        // Also support transform-driven controllers and the existing rig diagnostics.
        if (controllerPoseDriver == null)
            return true;
        if (!controllerPoseDriver.isActiveAndEnabled)
            return false;

        var position = controllerPoseDriver.positionInput.action;
        var rotation = controllerPoseDriver.rotationInput.action;
        if (position == null || !position.enabled || rotation == null || !rotation.enabled)
            return false;

        if (controllerPoseDriver.ignoreTrackingState)
            return position.activeControl != null && rotation.activeControl != null;

        var tracking = controllerPoseDriver.trackingStateInput.action;
        const int positionAndRotation = 3;
        return tracking != null && tracking.enabled
            && (tracking.ReadValue<int>() & positionAndRotation) == positionAndRotation;
    }

    public void ApplyTargetPose()
    {
        if (!HasReferences || !calibrated)
            return;

        Vector3 position = trackingSpace.InverseTransformPoint(controller.position);
        Quaternion rotation = Quaternion.Inverse(trackingSpace.rotation) * GetControllerGripRotation();
        Vector3 localTarget = alignPositionToController
            ? position * movementGain
            : targetReference + (position - controllerReference) * movementGain;
        Quaternion localRotation = rotation * gripToHandRotation;

        target.SetPositionAndRotation(mechaSpace.TransformPoint(localTarget), mechaSpace.rotation * localRotation);
    }

    private Quaternion GetControllerGripRotation()
    {
        // The XRI controller Transform may use pointerRotation (aim pose).
        // deviceRotation is the OpenXR grip pose, expressed in XR-origin space.
        var action = controllerPoseDriver != null ? controllerPoseDriver.rotationInput.action : null;
        var device = action != null && action.activeControl != null
            ? action.activeControl.device as XRController : null;
        // A valid identity pose may not have performed an action yet.
        // Resolve the bound hand device even before activeControl is populated.
        if (device == null && action != null)
        {
            foreach (var control in action.controls)
            {
                device = control.device as XRController;
                if (device != null) break;
            }
        }
        if (device != null)
        {
            Quaternion grip = device.deviceRotation.ReadValue();
            return controller.parent != null ? controller.parent.rotation * grip : grip;
        }
        return controller.rotation;
    }

    private bool InitializeRotationOffset()
    {
        if (rotationOffsetInitialized)
            return true;

        var ik = GetComponent<TwoBoneIKConstraint>();
        // Transform-only diagnostic probes have no skeleton to retarget.
        if (ik == null)
        {
            gripToHandRotation = Quaternion.identity;
            rotationOffsetInitialized = true;
            return true;
        }

        Transform hand = ik.data.tip;
        if (hand == null)
            return false;
        Transform index = null, little = null;
        foreach (Transform child in hand)
        {
            if (child.name.StartsWith("Index.01.")) index = child;
            if (child.name.StartsWith("Little.01.")) little = child;
        }
        if (index == null || little == null)
            return false;

        // Unity/OpenXR grip +Z runs along the held handle, little finger to index.
        // +X is out of the left palm / into the right palm. The mirrored model's
        // knuckle positions determine both axes without sampling the player's pose.
        Vector3 indexLocal = hand.InverseTransformPoint(index.position);
        Vector3 littleLocal = hand.InverseTransformPoint(little.position);
        Vector3 gripForward = indexLocal - littleLocal;
        Vector3 wristToKnuckles = (indexLocal + littleLocal) * 0.5f;
        Vector3 gripRight = Vector3.Cross(gripForward, wristToKnuckles);
        if (gripForward.sqrMagnitude < 0.000001f || gripRight.sqrMagnitude < 0.000000001f)
            return false;

        Vector3 gripUp = Vector3.Cross(gripForward, gripRight);
        gripToHandRotation = Quaternion.Inverse(Quaternion.LookRotation(gripForward, gripUp));
        rotationOffsetInitialized = true;
        return true;
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

        if (!InitializeRotationOffset())
            return;

        controllerReference = trackingSpace.InverseTransformPoint(controller.position);
        targetReference = mechaSpace.InverseTransformPoint(target.position);
        calibrateAt = -1f;
        calibrated = true;
        calibrationCount++;
        Debug.Log("[MechaVR] " + name + ": calibration complete. Move the controller to move the IK target.", this);
    }
}
