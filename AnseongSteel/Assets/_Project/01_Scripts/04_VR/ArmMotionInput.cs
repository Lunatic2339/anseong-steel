using System;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AnseongSteel.PlayerMotion
{
    [DefaultExecutionOrder(200)]
    public sealed class ArmMotionInput : MonoBehaviour
    {
        public ControllerArmFollower leftArm, rightArm;
        public Transform pilotSeat, leftSeat, rightSeat;
        public Camera cockpitCamera;
        public TextMesh cockpitStatus, exteriorStatus;
        public LineRenderer trail;
        public TrajectoryTuning recognition = new TrajectoryTuning();
        public bool leftPilot = true;
        public bool compensateHeadTranslation = true;
        public bool recognizePunches = true;
        public bool updateBeforeRender = true;
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
        public PunchTrajectoryRecognizer Recognizer { get; private set; }
        public bool Calibrated { get; private set; }
        public bool TrackingValid { get; private set; }
        public string Status { get; private set; } = "Y / C: calibrate in a comfortable ready pose";
        public string RecordingPath { get; private set; }
        public Vector3 LastControllerDelta { get; private set; }
        public event Action<PunchObservation> PunchCompleted;
        public ControllerArmFollower Arm => leftPilot ? leftArm : rightArm;
        readonly Vector3[] trailPoints = new Vector3[180];
        int trailCount;
        bool calibrating, hadSample, initialized, hasHeadReference;
        float stableSince = -1, previousTime, nextText;
        Vector3 headOrigin, referenceHand, previousHand, previousHead;
        Quaternion trackingToRobot = Quaternion.identity, referenceRotation;
        StreamWriter recording;
        static InputAction PoseAction(string name, string path, string type) => new InputAction(name, InputActionType.Value, path, expectedControlType: type);
        InputAction[] Actions => new[] { headPosition, headRotation, headState, leftPosition, leftRotation, leftState, rightPosition, rightRotation, rightState, calibrate, selectPilot, clearTrail, toggleRecord };

        void OnEnable()
        {
            foreach (var action in Actions) action.Enable();
            Application.onBeforeRender += BeforeRender;
            BuildRecognizer();
        }
        void Start()
        {
            leftArm.Initialize(); rightArm.Initialize();
            leftArm.Follow(Vector3.zero, Quaternion.identity); rightArm.Follow(Vector3.zero, Quaternion.identity);
            initialized = true; SetSeat();
        }
        void OnDisable()
        {
            Application.onBeforeRender -= BeforeRender;
            foreach (var action in Actions) action.Disable();
            CloseRecording(); Calibrated = false; hadSample = false;
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
        }
        public void SwitchPilot()
        {
            leftPilot = !leftPilot; CloseRecording(); BuildRecognizer(); SetSeat();
            leftArm.Follow(Vector3.zero, Quaternion.identity); rightArm.Follow(Vector3.zero, Quaternion.identity);
            RequestCalibration();
        }
        void SetSeat()
        {
            if (!pilotSeat) return;
            Transform anchor = leftPilot ? leftSeat : rightSeat;
            if (anchor) pilotSeat.SetPositionAndRotation(anchor.position, anchor.rotation);
        }
        public void ClearTrail() { trailCount = 0; if (trail) trail.positionCount = 0; }
        void Update()
        {
            var keyboard = Keyboard.current;
            if (selectPilot.WasPressedThisFrame() || (keyboard != null && keyboard.tabKey.wasPressedThisFrame)) SwitchPilot();
            if (calibrate.WasPressedThisFrame() || (keyboard != null && keyboard.cKey.wasPressedThisFrame)) RequestCalibration();
            if (clearTrail.WasPressedThisFrame() || (keyboard != null && keyboard.spaceKey.wasPressedThisFrame)) ClearTrail();
            if (toggleRecord.WasPressedThisFrame() || (keyboard != null && keyboard.rKey.wasPressedThisFrame)) ToggleRecording();
            if (!initialized) return;
            UpdateHead();
            float now = Time.unscaledTime;
            if (!ReadPose(out var hp, out var hr, out var hand, out var rotation))
            { Invalidate("Tracking lost / no XR device. Pose frozen; press Y after recovery."); RefreshText(); return; }
            TrackingValid = true;
            float dt = hadSample ? now - previousTime : 0;
            bool discontinuity = hadSample && (dt > recognition.maximumSampleGap ||
                (dt > 0 && ((hand - previousHand).magnitude / dt > recognition.maximumTrackingSpeed || (hp - previousHead).magnitude / dt > recognition.maximumTrackingSpeed)));
            if (discontinuity) { Invalidate("Pose discontinuity. Press Y to recalibrate."); RefreshText(); return; }
            if (calibrating)
            {
                bool still = hadSample && dt > 0 && (hand - previousHand).magnitude / dt < calibrationStillSpeed && (hp - previousHead).magnitude / dt < calibrationStillSpeed;
                if (!still) stableSince = -1;
                else if (stableSince < 0) stableSince = now;
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
                ApplyVisual(hp, hr, hand, rotation);
                if (recognizePunches) Recognizer.Sample(RelativeHand(hp, hand), trackingToRobot * rotation, now);
                if (Recognizer.Phase == MotionPhase.TrackingLost && recognizePunches)
                { Invalidate("Recognition sample gap: press Y to recalibrate."); }
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
            var p = headPosition.ReadValue<Vector3>(); var q = headRotation.ReadValue<Quaternion>();
            if ((headState.ReadValue<int>() & 3) != 3 || !PunchTrajectoryRecognizer.Finite(p) || !PunchTrajectoryRecognizer.Finite(q)) return;
            if (!hasHeadReference) { headOrigin = p; trackingToRobot = Quaternion.Inverse(Quaternion.Euler(0, q.eulerAngles.y, 0)); hasHeadReference = true; }
            cockpitCamera.transform.localPosition = Vector3.up * 1.65f + trackingToRobot * (p - headOrigin);
            cockpitCamera.transform.localRotation = trackingToRobot * q;
        }
        bool ReadPose(out Vector3 hp, out Quaternion hr, out Vector3 hand, out Quaternion rotation)
        {
            hp = headPosition.ReadValue<Vector3>(); hr = headRotation.ReadValue<Quaternion>();
            hand = (leftPilot ? leftPosition : rightPosition).ReadValue<Vector3>();
            rotation = (leftPilot ? leftRotation : rightRotation).ReadValue<Quaternion>();
            return (headState.ReadValue<int>() & 3) == 3 && ((leftPilot ? leftState : rightState).ReadValue<int>() & 3) == 3
                && PunchTrajectoryRecognizer.Finite(hp) && PunchTrajectoryRecognizer.Finite(hand)
                && PunchTrajectoryRecognizer.Finite(hr) && PunchTrajectoryRecognizer.Finite(rotation) && Vector3.Distance(hp, hand) < 2.2f;
        }
        Vector3 RelativeHand(Vector3 hp, Vector3 hand) => trackingToRobot * (hand - (compensateHeadTranslation ? hp : headOrigin));
        void ApplyVisual(Vector3 hp, Quaternion hr, Vector3 hand, Quaternion rotation)
        {
            cockpitCamera.transform.localPosition = Vector3.up * 1.65f + trackingToRobot * (hp - headOrigin);
            cockpitCamera.transform.localRotation = trackingToRobot * hr;
            LastControllerDelta = RelativeHand(hp, hand) - referenceHand;
            Quaternion delta = trackingToRobot * rotation * Quaternion.Inverse(referenceRotation);
            Arm.Follow(LastControllerDelta, delta);
        }
        void Invalidate(string reason)
        {
            TrackingValid = false; Calibrated = false; calibrating = false; hadSample = false;
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
            string text = $"REAL HAND TRACKING | {(leftPilot ? "LEFT PILOT" : "RIGHT PILOT")}\n{Status}\n{Recognizer.Phase} / CANDIDATE: {c.kind} ({c.quality:F2})\nFINAL #{Recognizer.CompletedCount}: {f.kind}\n{c.reason}\nForward {c.forward:F2}m | Inward {c.inward:F2}m | Arc {c.arcRatio:F2}\nReach limit {Arm.ReachError:F3}m | IK error {Arm.TargetError:F4}m\nY: calibrate   X: switch pilot   B: clear trail   A: {(recording == null ? "record CSV" : "STOP RECORDING")}";
            if (cockpitStatus) cockpitStatus.text = text;
            if (exteriorStatus) exteriorStatus.text = $"{(leftPilot ? "L" : "R")} | {Recognizer.Phase}\nCandidate: {c.kind} / Final: {f.kind}\n{c.reason}";
        }
        void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 420, 170), GUI.skin.box);
            GUILayout.Label("REAL XR INPUT ONLY - no keyboard punch animation");
            GUILayout.Label(Status);
            GUILayout.Label("C: calibrate | Tab: pilot | Space: clear | R: CSV");
            if (GUILayout.Button("Calibrate (requires tracked headset + hand)")) RequestCalibration();
            if (GUILayout.Button("Switch assigned hand")) SwitchPilot();
            GUILayout.EndArea();
        }
    }
}
