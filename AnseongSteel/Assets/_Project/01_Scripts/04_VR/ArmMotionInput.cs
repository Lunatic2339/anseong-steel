using System;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace AnseongSteel.PlayerMotion
{
    [DefaultExecutionOrder(200)]
    public sealed class ArmMotionInput : MonoBehaviour
    {
        public ControllerArmFollower leftArm, rightArm;
        public Transform pilotSeat, leftSeat, rightSeat;
        public Camera cockpitCamera;
        public CockpitPilotAvatar leftAvatar, rightAvatar;
        public CockpitPilotAvatar Avatar => leftPilot ? leftAvatar : rightAvatar;
        Vector3 EyePosition => Avatar ? Avatar.neutralEye : Vector3.up * 1.65f;
        public TextMesh cockpitStatus, exteriorStatus;
        public LineRenderer trail;
        public TrajectoryTuning recognition = new TrajectoryTuning();
        public bool leftPilot = true;
        public bool compensateHeadTranslation = true;
        public bool recognizePunches = true;
        public bool updateBeforeRender = true;
        public bool showHandDirections = true;
        public float calibrationStillSeconds = .35f;
        public float calibrationStillSpeed = .18f;
        public InputAction headPosition = PoseAction("Head position", "<XRHMD>/centerEyePosition", "Vector3");
        public InputAction headRotation = PoseAction("Head rotation", "<XRHMD>/centerEyeRotation", "Quaternion");
        public InputAction headState = PoseAction("Head tracking state", "<XRHMD>/trackingState", "Integer");
        public InputAction leftPosition = PoseAction("Left position", "<XRController>{LeftHand}/devicePosition", "Vector3");
        public InputAction leftRotation = PoseAction("Left rotation", "<XRController>{LeftHand}/deviceRotation", "Quaternion");
        public InputAction leftState = PoseAction("Left tracking state", "<XRController>{LeftHand}/trackingState", "Integer");
        public InputAction rightPosition = PoseAction("Right position", "<XRController>{RightHand}/devicePosition", "Vector3");
        public InputAction rightRotation = PoseAction("Right rotation", "<XRController>{RightHand}/deviceRotation", "Quaternion");
        public InputAction rightState = PoseAction("Right tracking state", "<XRController>{RightHand}/trackingState", "Integer");
        public InputAction calibrate = new InputAction("Calibrate", InputActionType.Button, "<XRController>{LeftHand}/secondaryButton");
        public InputAction selectPilot = new InputAction("Switch pilot", InputActionType.Button, "<XRController>{LeftHand}/primaryButton");
        public InputAction clearTrail = new InputAction("Clear trail", InputActionType.Button, "<XRController>{RightHand}/secondaryButton");
        public InputAction toggleRecord = new InputAction("Record CSV", InputActionType.Button, "<XRController>{RightHand}/primaryButton");
        public InputAction captureReach = new InputAction("Calibrate full reach", InputActionType.Button, "<XRController>{LeftHand}/thumbstickClicked");
        public InputAction viewModifier = new InputAction("View switch modifier", InputActionType.Button, "<XRController>{RightHand}/gripPressed");
        public RobotViewComparison ViewComparison { get; private set; }
        public RobotPunchContactTest ContactTest { get; private set; }
        public PunchTrajectoryRecognizer Recognizer { get; private set; }
        public bool Calibrated { get; private set; }
        public bool TrackingValid { get; private set; }
        public string Status { get; private set; } = "Y / C: calibrate in a comfortable ready pose";
        public string RecordingPath { get; private set; }
        public Vector3 LastControllerDelta { get; private set; }
        public string InputStatus { get; private set; }
        public HandDirectionDisplay Directions { get; private set; }
        public event Action<PunchObservation> PunchCompleted;
        public ControllerArmFollower Arm => leftPilot ? leftArm : rightArm;
        readonly Vector3[] trailPoints = new Vector3[180];
        readonly ControllerElbowHint elbowHint = new ControllerElbowHint();
        float previousVisualTime = -1;
        public float ElbowLift => elbowHint.Lift;
        int trailCount;
        bool calibrating, hadSample, initialized, hasHeadReference;
        float stableSince = -1, previousTime, nextText;
        Vector3 headOrigin, referenceHand, previousHand, previousHead;
        Quaternion trackingToRobot = Quaternion.identity, referenceRotation;
        StreamWriter recording;
        static InputAction PoseAction(string name, string path, string type) => new InputAction(name, InputActionType.Value, path, expectedControlType: type);
        InputAction[] Actions => new[] { headPosition, headRotation, headState, leftPosition, leftRotation, leftState, rightPosition, rightRotation, rightState, calibrate, selectPilot, clearTrail, toggleRecord, captureReach, viewModifier };

        void OnEnable()
        {
            foreach (var action in Actions) action.Enable();
            Application.onBeforeRender += BeforeRender;
            BuildRecognizer();
            RequestCalibration();
            if (initialized) SetSeat();
        }
        void Start()
        {
            leftArm.Initialize(); rightArm.Initialize();
            leftArm.Follow(Vector3.zero, Quaternion.identity); rightArm.Follow(Vector3.zero, Quaternion.identity);
            initialized = true; SetSeat();
            Directions = gameObject.AddComponent<HandDirectionDisplay>(); Directions.input = this;
            ViewComparison = gameObject.AddComponent<RobotViewComparison>(); ViewComparison.input = this;
            ContactTest = gameObject.AddComponent<RobotPunchContactTest>(); ContactTest.input = this;
        }
        void OnDisable()
        {
            Application.onBeforeRender -= BeforeRender;
            foreach (var action in Actions) action.Disable();
            CloseRecording(); Calibrated = false; hadSample = false;
            if (leftAvatar) leftAvatar.SetLocalCamera(null);
            if (rightAvatar) rightAvatar.SetLocalCamera(null);
            if (ViewComparison) ViewComparison.SetDirect(false);
        }
        void BuildRecognizer()
        {
            Recognizer = new PunchTrajectoryRecognizer(leftPilot, recognition);
            Recognizer.Completed += result => PunchCompleted?.Invoke(result);
        }
        public void RequestCalibration()
        {
            calibrating = true; Calibrated = false; stableSince = -1; hadSample = false;
            Recognizer.LoseTracking(); ClearTrail(); Status = "Hold head and assigned hand still in ready pose...";
            elbowHint.Reset(); previousVisualTime = -1;
        }
        public void SwitchPilot()
        {
            leftPilot = !leftPilot; CloseRecording(); BuildRecognizer(); SetSeat();
            leftArm.elbowLift = rightArm.elbowLift = 0;
            leftArm.Follow(Vector3.zero, Quaternion.identity); rightArm.Follow(Vector3.zero, Quaternion.identity);
            RequestCalibration();
        }
        void SetSeat()
        {
            if (!pilotSeat) return;
            Transform anchor = leftPilot ? leftSeat : rightSeat;
            if (anchor) pilotSeat.SetPositionAndRotation(anchor.position, anchor.rotation);
            if (leftAvatar) { leftAvatar.SetLocalCamera(null); leftAvatar.ResetGuard(); }
            if (rightAvatar) { rightAvatar.SetLocalCamera(null); rightAvatar.ResetGuard(); }
            cockpitCamera.transform.localPosition = EyePosition;
            if (Avatar) { Avatar.SetLocalCamera(cockpitCamera); Avatar.Evaluate(); }
        }
        public void ClearTrail() { trailCount = 0; if (trail) trail.positionCount = 0; }
        void Update()
        {
            var keyboard = Keyboard.current;
            if (selectPilot.WasPressedThisFrame() || (keyboard != null && keyboard.tabKey.wasPressedThisFrame)) SwitchPilot();
            if (calibrate.WasPressedThisFrame() || (keyboard != null && keyboard.cKey.wasPressedThisFrame)) RequestCalibration();
            bool viewChord = clearTrail.WasPressedThisFrame() && viewModifier.IsPressed();
            if (ViewComparison && (viewChord || (keyboard != null && keyboard.gKey.wasPressedThisFrame))) ViewComparison.Toggle();
            if ((clearTrail.WasPressedThisFrame() && !viewChord) || (keyboard != null && keyboard.spaceKey.wasPressedThisFrame)) ClearTrail();
            if (ContactTest && keyboard != null && keyboard.tKey.wasPressedThisFrame) ContactTest.visible = !ContactTest.visible;
            if (toggleRecord.WasPressedThisFrame() || (keyboard != null && keyboard.rKey.wasPressedThisFrame)) ToggleRecording();
            if (keyboard != null && keyboard.fKey.wasPressedThisFrame) showHandDirections = !showHandDirections;
            if (!initialized) return;
            UpdateHead();
            float now = Time.unscaledTime;
            if (!ReadPose(out var hp, out var hr, out var hand, out var rotation))
            { Invalidate("Missing tracked head or assigned hand. Hold controllers in view; Y/C to retry."); RefreshText(); return; }
            TrackingValid = true;
            float dt = hadSample ? now - previousTime : 0;
            bool sampleGap = hadSample && dt > recognition.maximumSampleGap;
            bool discontinuity = hadSample && dt > 0 &&
                ((hand - previousHand).magnitude / dt > recognition.maximumTrackingSpeed || (hp - previousHead).magnitude / dt > recognition.maximumTrackingSpeed);
            if (discontinuity) { Invalidate("Pose discontinuity. Press Y to recalibrate."); RefreshText(); return; }
            if (sampleGap && Calibrated) Recognizer.DiscardInterruptedStroke();
            if (calibrating)
            {
                bool still = hadSample && dt > 0 && (hand - previousHand).magnitude / dt < calibrationStillSpeed && (hp - previousHead).magnitude / dt < calibrationStillSpeed;
                if (!still) stableSince = -1;
                else if (stableSince < 0) stableSince = now;
                Status = $"AUTO CALIBRATION: hold {(leftPilot ? "LEFT" : "RIGHT")} hand and head still ({(stableSince < 0 ? 0 : Mathf.Clamp01((now - stableSince) / calibrationStillSeconds)) * 100:F0}%)";
                if (stableSince >= 0 && now - stableSince >= calibrationStillSeconds)
                {
                    headOrigin = hp;
                    trackingToRobot = Quaternion.Inverse(Quaternion.Euler(0, hr.eulerAngles.y, 0));
                    referenceHand = RelativeHand(hp, hand); referenceRotation = trackingToRobot * rotation;
                    Recognizer.Calibrate(referenceHand); calibrating = false; Calibrated = true;
                    Status = "Live hand following. Punch freely; no animation playback.";
                }
            }
            if (Calibrated)
            {
                if (captureReach.WasPressedThisFrame() || (keyboard != null && keyboard.eKey.wasPressedThisFrame)) CalibrateFullReach();
                ApplyVisual(hp, hr, hand, rotation);
                if (recognizePunches) Recognizer.Sample(RelativeHand(hp, hand), trackingToRobot * rotation, now);
                if (Recognizer.Phase == MotionPhase.TrackingLost && recognizePunches)
                { Recognizer.DiscardInterruptedStroke(); }
                else
                {
                    AppendTrail(Arm.RequestedTarget);
                    WriteSample(now, hp, hand, rotation);
                }
            }

            previousHand = hand; previousHead = hp; previousTime = now; hadSample = true;
            RefreshText();
        }
        void BeforeRender()
        {
            if (initialized) UpdateHead();
            if (!updateBeforeRender || !Calibrated || !initialized) return;
            if (ReadPose(out var hp, out var hr, out var hand, out var rotation) && (hand - previousHand).magnitude < .2f)
                ApplyVisual(hp, hr, hand, rotation); // Visual pose only: no duplicate recognition/events here.
        }
        void UpdateHead()
        {
            if (!ReadTrackedPose(headPosition, headRotation, headState, out var p, out var q)) return;
            if (!hasHeadReference) { headOrigin = p; trackingToRobot = Quaternion.Inverse(Quaternion.Euler(0, q.eulerAngles.y, 0)); hasHeadReference = true; }
            cockpitCamera.transform.localPosition = EyePosition + trackingToRobot * (p - headOrigin);
            cockpitCamera.transform.localRotation = trackingToRobot * q;
            if (Avatar) Avatar.Evaluate();
        }
        bool ReadPose(out Vector3 hp, out Quaternion hr, out Vector3 hand, out Quaternion rotation)
        {
            bool head = ReadTrackedPose(headPosition, headRotation, headState, out hp, out hr);
            bool controller = ReadTrackedPose(leftPilot ? leftPosition : rightPosition, leftPilot ? leftRotation : rightRotation,
                leftPilot ? leftState : rightState, out hand, out rotation);
            return head && controller && Vector3.Distance(hp, hand) < 2.2f;
        }
        // Pose and validity must come from the SAME device. With hand tracking and
        // controllers enabled, independent Value actions can pick different devices.
        public static bool ReadTrackedPose(InputAction position, InputAction rotation, InputAction state, out Vector3 p, out Quaternion q)
        {
            p = default; q = Quaternion.identity;
            foreach (var control in position.controls)
            {
                if (!(control is Vector3Control pc) || !pc.device.added || !pc.device.enabled) continue;
                QuaternionControl rc = null; IntegerControl sc = null;
                foreach (var candidate in rotation.controls) if (candidate.device == pc.device && candidate is QuaternionControl r) { rc = r; break; }
                foreach (var candidate in state.controls) if (candidate.device == pc.device && candidate is IntegerControl s) { sc = s; break; }
                if (rc == null || sc == null || (sc.ReadValue() & 3) != 3) continue;
                var v = pc.ReadValue(); var angle = rc.ReadValue();
                if (!PunchTrajectoryRecognizer.Finite(v) || !PunchTrajectoryRecognizer.Finite(angle)) continue;
                p = v; q = angle; return true;
            }
            return false;
        }
        public bool ControllerInCockpit(bool left, out Vector3 position, out Quaternion rotation)
        {
            position = default; rotation = Quaternion.identity;
            if (!pilotSeat || !ReadTrackedPose(headPosition, headRotation, headState, out var hp, out _) ||
                !ReadTrackedPose(left ? leftPosition : rightPosition, left ? leftRotation : rightRotation, left ? leftState : rightState, out var p, out var q)) return false;
            position = pilotSeat.TransformPoint(EyePosition + trackingToRobot * (p - headOrigin));
            rotation = pilotSeat.rotation * trackingToRobot * q;
            return Vector3.Distance(hp, p) < 2.2f;
        }
        Vector3 RelativeHand(Vector3 hp, Vector3 hand) => trackingToRobot * (hand - (compensateHeadTranslation ? hp : headOrigin));
        void ApplyVisual(Vector3 hp, Quaternion hr, Vector3 hand, Quaternion rotation)
        {
            cockpitCamera.transform.localPosition = EyePosition + trackingToRobot * (hp - headOrigin);
            cockpitCamera.transform.localRotation = trackingToRobot * hr;
            LastControllerDelta = RelativeHand(hp, hand) - referenceHand;
            Quaternion delta = trackingToRobot * rotation * Quaternion.Inverse(referenceRotation);
            float now = Time.unscaledTime;
            float dt = previousVisualTime < 0 ? 1f / 90f : now - previousVisualTime;
            Arm.elbowLift = elbowHint.Update(LastControllerDelta, Mathf.Min(dt, .1f)); previousVisualTime = now;
            Arm.Follow(LastControllerDelta, delta);
            if (Avatar) Avatar.Follow(leftPilot, LastControllerDelta, delta, Arm.elbowLift);
        }
        public bool CalibrateFullReach()
        {
            if (!Calibrated || !ReadPose(out var hp, out _, out var hand, out _))
            { Status = "Calibrate ready pose with Y/C first, then extend hand and press E / LEFT stick."; return false; }
            var delta = RelativeHand(hp, hand) - referenceHand;
            if (!Arm.CalibrateReach(delta))
            { Status = "Reach calibration: extend assigned hand straight forward at least 15 cm from ready pose."; return false; }
            if (Avatar) Avatar.CalibrateReach(leftPilot, delta);
            Recognizer.DiscardInterruptedStroke(); ClearTrail();
            Status = $"Full reach saved (robot scale {Arm.motionScale:F2}). Return to guard, then punch.";
            return true;
        }
        void Invalidate(string reason)
        {
            bool pendingCalibration = calibrating && !Calibrated;
            TrackingValid = false; Calibrated = false; calibrating = pendingCalibration; hadSample = false; stableSince = -1;
            Recognizer.LoseTracking(); Status = reason;
        }
        void AppendTrail(Vector3 p)
        {
            if (!trail || (trailCount > 0 && Vector3.Distance(trailPoints[trailCount - 1], p) < .008f)) return;
            if (trailCount == trailPoints.Length) { Array.Copy(trailPoints, 1, trailPoints, 0, trailPoints.Length - 1); trailCount--; }
            trailPoints[trailCount++] = p; trail.positionCount = trailCount;
            for (int i = 0; i < trailCount; i++) trail.SetPosition(i, trailPoints[i]);
        }
        public void ToggleRecording()
        {
            if (recording != null) { CloseRecording(); return; }
            string folder = Path.Combine(Application.persistentDataPath, "ArmMotionRecordings");
            try
            {
                Directory.CreateDirectory(folder);
                RecordingPath = Path.Combine(folder, DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + ".csv");
                recording = new StreamWriter(RecordingPath, false);
                recording.WriteLine("time,side,head_x,head_y,head_z,hand_x,hand_y,hand_z,rot_x,rot_y,rot_z,rot_w,delta_x,delta_y,delta_z,phase,candidate,quality,completed,last_final,reach_error,ik_error");
                recording.AutoFlush = true; Debug.Log("Arm motion CSV: " + RecordingPath);
            }
            catch (Exception ex) { CloseRecording(); Debug.LogWarning("Cannot start motion recording: " + ex.Message); }
        }
        void WriteSample(float now, Vector3 hp, Vector3 hand, Quaternion rotation)
        {
            if (recording == null) return;
            try
            {
                recording.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "{0:F5},{1},{2:F5},{3:F5},{4:F5},{5:F5},{6:F5},{7:F5},{8:F5},{9:F5},{10:F5},{11:F5},{12:F5},{13:F5},{14:F5},{15},{16},{17:F3},{18},{19},{20:F5},{21:F5}",
                    now, leftPilot ? "L" : "R", hp.x, hp.y, hp.z, hand.x, hand.y, hand.z, rotation.x, rotation.y, rotation.z, rotation.w,
                    LastControllerDelta.x, LastControllerDelta.y, LastControllerDelta.z, Recognizer.Phase, Recognizer.Candidate.kind, Recognizer.Candidate.quality,
                    Recognizer.CompletedCount, Recognizer.LastCompleted.kind, Arm.ReachError, Arm.TargetError));
            }
            catch (Exception ex) { CloseRecording(); Debug.LogWarning("Motion recording stopped: " + ex.Message); }
        }
        void CloseRecording() { recording?.Dispose(); recording = null; }
        void RefreshText()
        {
            if (Time.unscaledTime < nextText) return; nextText = Time.unscaledTime + .1f;
            var c = Recognizer.Candidate; var f = Recognizer.LastCompleted;
            string DeviceStatus(InputAction p, InputAction r, InputAction s) => ReadTrackedPose(p, r, s, out _, out _) ? "TRACKED" : p.controls.Count == 0 ? "NO DEVICE" : "NOT TRACKED";
            InputStatus = $"HEAD: {DeviceStatus(headPosition, headRotation, headState)} | LEFT: {DeviceStatus(leftPosition, leftRotation, leftState)} | RIGHT: {DeviceStatus(rightPosition, rightRotation, rightState)}";
            string text = $"REAL HAND TRACKING | {(leftPilot ? "LEFT PILOT" : "RIGHT PILOT")}\n{Status}\n{Recognizer.Phase} / CANDIDATE: {c.kind} ({c.quality:F2})\nFINAL #{Recognizer.CompletedCount}: {f.kind}\n{c.reason}\nForward {c.forward:F2}m | Inward {c.inward:F2}m | Arc {c.arcRatio:F2}\nReach limit {Arm.ReachError:F3}m | IK error {Arm.TargetError:F4}m\nY: calibrate   X: switch pilot   B: clear trail   A: {(recording == null ? "record CSV" : "STOP RECORDING")}";
            if (cockpitStatus) cockpitStatus.text = InputStatus + "\n" + text + $"\nArm extension {Arm.Extension:P0} | Elbow hint {ElbowLift:P0}\nE / LEFT stick: save full reach\nCYAN: controller +Z | YELLOW: model hand +Z | F: directions";
            if (exteriorStatus) exteriorStatus.text = $"{(leftPilot ? "L" : "R")} | {Recognizer.Phase}\nCandidate: {c.kind} / Final: {f.kind}\n{c.reason}";
        }
        void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 560, 335), GUI.skin.box);
            GUILayout.Label($"ASSIGNED HAND: {(leftPilot ? "LEFT" : "RIGHT")} | {InputStatus}");
            GUILayout.Label(Status);
            GUILayout.Label("C: calibrate | Tab: pilot | Space: clear | R: CSV");
            if (GUILayout.Button("Calibrate (requires tracked headset + hand)")) RequestCalibration();
            if (GUILayout.Button("Switch assigned hand")) SwitchPilot();
            if (GUILayout.Button("Save full reach (extend assigned hand, E / LEFT stick)")) CalibrateFullReach();
            GUILayout.Label($"Arm extension: {Arm.Extension:P0} | Estimated elbow lift: {ElbowLift:P0}");
            showHandDirections = GUILayout.Toggle(showHandDirections, "Hand directions (F): CYAN controller +Z / YELLOW model hand +Z");
            if (ViewComparison && GUILayout.Button($"View: {(ViewComparison.Direct ? "DIRECT ROBOT" : "COCKPIT")} (G / RIGHT grip + B)")) ViewComparison.Toggle();
            if (ContactTest) ContactTest.visible = GUILayout.Toggle(ContactTest.visible, "Reach contact pads (T) - " + ContactTest.HitCount + " hits");
            GUILayout.EndArea();
        }
    }
}
