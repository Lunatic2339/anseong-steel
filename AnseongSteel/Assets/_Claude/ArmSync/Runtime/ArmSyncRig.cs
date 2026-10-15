using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace AnseongSteel.ArmSync
{
    // Controller -> robot arm verification rig.
    //
    // The whole tracking space (camera + controller ghosts) is scaled up by
    // robotArmLength / userArmLength and placed so the pilot's eyes sit at the robot's eyes.
    // In that giant space your real hand motion and the robot's hand motion are directly
    // comparable: moving your hand 10 cm forward should move the robot fist exactly
    // scale x 10 cm forward, in the same direction, with the same wrist rotation.
    //
    // GuardRelative: robot fist = robot guard + (controller - controller at recenter).
    // Absolute:      robot fist = where your giant hand actually is.
    [DefaultExecutionOrder(100)]
    public sealed class ArmSyncRig : MonoBehaviour
    {
        public enum MappingMode { GuardRelative, Absolute }

        [Header("Scene")]
        public ArmSyncInput input;
        public Transform rig;
        public Transform head;
        public Transform leftController, rightController;
        public Transform robotHeadBone;
        public ArmSyncArm leftArm = new ArmSyncArm(), rightArm = new ArmSyncArm();

        [Header("Mapping")]
        public MappingMode mode = MappingMode.GuardRelative;
        [Tooltip("Pilot shoulder-to-controller distance with the arm straight, metres")]
        public float userArmLength = .62f;
        [Tooltip("Eye position from the robot head bone, robot frame (right, up, forward), in robot arm lengths")]
        public Vector3 eyeOffset = new Vector3(0, .22f, .12f);
        [Tooltip("Robot guard wrist from the shoulder, robot frame (inward, up, forward), in robot arm lengths")]
        public Vector3 guardOffset = new Vector3(.15f, 0f, .5f);
        [Tooltip("Elbow direction, robot frame (outward, up, forward)")]
        public Vector3 elbowHint = new Vector3(.35f, -1f, -.25f);
        public bool driveWrist = true;
        public bool solveBeforeRender = true;

        [Header("Markers (optional)")]
        public Transform leftExpected, rightExpected;
        public LineRenderer leftErrorLine, rightErrorLine;
        public GameObject markersRoot;
        public TextMesh hud;
        public GameObject sideMonitor;
        public Camera sideCamera;

        public float Scale => robotArmLength / Mathf.Max(.2f, userArmLength);
        public ArmSyncArm Arm(int side) => side == 0 ? leftArm : rightArm;
        public bool Recording => csv != null;
        public string RecordingPath { get; private set; }

        float robotArmLength = 1;
        Quaternion robotFrame = Quaternion.identity;
        readonly bool[] calibrated = new bool[2];
        readonly Vector3[] controller0 = new Vector3[2], robotHand0 = new Vector3[2], guard = new Vector3[2], expected = new Vector3[2];
        readonly Quaternion[] controllerRot0 = { Quaternion.identity, Quaternion.identity }, guardRot = { Quaternion.identity, Quaternion.identity }, expectedRot = { Quaternion.identity, Quaternion.identity };
        readonly Stats[] stats = { new Stats(), new Stats() };
        bool needsRecenter = true;
        StreamWriter csv;
        float fpsSmoothed = 72, nextHud;
        string message = "Hold a boxing guard, look forward, press Y (or C)";

        sealed class Stats
        {
            public Vector3 realDelta, robotDelta;
            public float directionError = float.NaN, positionError, rotationError, reach, gain = float.NaN;
            public float peakDirection, peakPosition;
            public bool clamped;
            public void ResetPeaks() { peakDirection = peakPosition = 0; }
        }

        void Awake()
        {
            leftArm.Capture(); rightArm.Capture();
            if (!leftArm.Ready || !rightArm.Ready) { Debug.LogError("ArmSyncRig: arm bones are not assigned", this); enabled = false; return; }
            robotArmLength = (leftArm.Length + rightArm.Length) * .5f;
            Vector3 right = Vector3.ProjectOnPlane(rightArm.Shoulder - leftArm.Shoulder, Vector3.up);
            robotFrame = Quaternion.LookRotation(Vector3.Cross(right, Vector3.up), Vector3.up);
            for (int side = 0; side < 2; side++)
            {
                var arm = Arm(side);
                float outward = side == 0 ? -1 : 1;
                guard[side] = arm.Shoulder + robotFrame * Vector3.Scale(guardOffset, new Vector3(-outward, 1, 1)) * robotArmLength;
                guardRot[side] = GuardHandRotation(arm, outward);
            }
            PlaceSideCamera();
        }

        void OnEnable() => Application.onBeforeRender += BeforeRender;
        void OnDisable() { Application.onBeforeRender -= BeforeRender; CloseCsv(); }

        Quaternion GuardHandRotation(ArmSyncArm arm, float outward)
        {
            if (arm.fist) arm.fist.Build();
            if (!arm.fist || !arm.fist.Ready) return arm.hand.rotation;
            Vector3 k = arm.fist.KnuckleLocal, p = Vector3.ProjectOnPlane(arm.fist.PalmLocal, k);
            Vector3 forward = robotFrame * Vector3.forward, inward = robotFrame * Vector3.right * -outward;
            return Quaternion.LookRotation(forward, inward) * Quaternion.Inverse(Quaternion.LookRotation(k, p));
        }

        void PlaceSideCamera()
        {
            if (!sideCamera) return;
            Vector3 chest = (leftArm.Shoulder + rightArm.Shoulder) * .5f;
            Vector3 look = chest + robotFrame * Vector3.forward * robotArmLength * .5f;
            sideCamera.transform.position = chest + robotFrame * new Vector3(2.6f, .3f, .7f) * robotArmLength;
            sideCamera.transform.rotation = Quaternion.LookRotation(look - sideCamera.transform.position, Vector3.up);
        }

        void Update()
        {
            input.Poll(true);
            if (input.Recenter.WasPressedThisFrame()) needsRecenter = true;
            if (input.ToggleHud.WasPressedThisFrame() && hud) hud.gameObject.SetActive(!hud.gameObject.activeSelf);
            if (input.ToggleMarkers.WasPressedThisFrame() && markersRoot) markersRoot.SetActive(!markersRoot.activeSelf);
            if (input.ToggleMonitor.WasPressedThisFrame() && sideMonitor)
            {
                bool on = !sideMonitor.activeSelf; sideMonitor.SetActive(on);
                if (sideCamera) sideCamera.enabled = on;
            }
            if (input.ToggleMode.WasPressedThisFrame())
            {
                mode = mode == MappingMode.GuardRelative ? MappingMode.Absolute : MappingMode.GuardRelative;
                needsRecenter = true;
            }
            if (input.ArmLengthUpPressed) { userArmLength = Mathf.Min(1f, userArmLength + .02f); needsRecenter = true; }
            if (input.ArmLengthDownPressed) { userArmLength = Mathf.Max(.4f, userArmLength - .02f); needsRecenter = true; }
            if (input.ToggleRecording.WasPressedThisFrame()) { if (csv != null) CloseCsv(); else OpenCsv(); }

            ApplyTracking();
            if (needsRecenter && input.Head.tracked) RecenterNow();
            SolveArms();
            Measure();
            WriteCsv();
            fpsSmoothed = Mathf.Lerp(fpsSmoothed, 1f / Mathf.Max(1e-4f, Time.unscaledDeltaTime), .05f);
            if (hud && hud.gameObject.activeInHierarchy && Time.unscaledTime >= nextHud) { nextHud = Time.unscaledTime + .1f; hud.text = HudText(); }
        }

        // Late pose update so the robot and ghosts match the controllers on the rendered frame.
        void BeforeRender()
        {
            if (!solveBeforeRender || !isActiveAndEnabled) return;
            input.Poll(false);
            ApplyTracking();
            SolveArms();
        }

        void ApplyTracking()
        {
            var h = input.Head;
            if (h.tracked) head.SetLocalPositionAndRotation(h.position, h.rotation);
            SetGhost(leftController, input.Left);
            SetGhost(rightController, input.Right);
        }

        static void SetGhost(Transform ghost, TrackedPose pose)
        {
            if (!ghost) return;
            if (ghost.gameObject.activeSelf != pose.tracked) ghost.gameObject.SetActive(pose.tracked);
            if (pose.tracked) ghost.SetLocalPositionAndRotation(pose.position, pose.rotation);
        }

        public void RecenterNow()
        {
            needsRecenter = false;
            float s = Scale;
            rig.localScale = Vector3.one * s;
            var headLocal = input.Head;
            head.SetLocalPositionAndRotation(headLocal.position, headLocal.rotation);
            Vector3 f = headLocal.rotation * Vector3.forward;
            float headYaw = f.x * f.x + f.z * f.z > 1e-6f ? Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg : 0;
            Vector3 robotForward = robotFrame * Vector3.forward;
            float robotYaw = Mathf.Atan2(robotForward.x, robotForward.z) * Mathf.Rad2Deg;
            rig.rotation = Quaternion.Euler(0, robotYaw - headYaw, 0);
            rig.position = EyeAnchor - rig.rotation * (headLocal.position * s);
            SetGhost(leftController, input.Left);
            SetGhost(rightController, input.Right);
            for (int side = 0; side < 2; side++)
            {
                var c = Ghost(side); var pose = input.Hand(side);
                calibrated[side] = c && pose.tracked;
                if (!calibrated[side]) continue;
                controller0[side] = c.position; controllerRot0[side] = c.rotation;
            }
            SolveArms();
            for (int side = 0; side < 2; side++) { robotHand0[side] = Arm(side).hand.position; stats[side].ResetPeaks(); }
            message = calibrated[0] && calibrated[1] ? "Recentered. Move one hand at a time and compare."
                : "Recentered, but a controller was not tracked. Press Y again with both in view.";
        }

        Vector3 EyeAnchor => (robotHeadBone ? robotHeadBone.position : (leftArm.Shoulder + rightArm.Shoulder) * .5f)
            + robotFrame * eyeOffset * robotArmLength;

        Transform Ghost(int side) => side == 0 ? leftController : rightController;

        void SolveArms()
        {
            for (int side = 0; side < 2; side++)
            {
                var arm = Arm(side); var c = Ghost(side);
                Vector3 pole = robotFrame * Vector3.Scale(elbowHint, new Vector3(side == 0 ? -1 : 1, 1, 1));
                if (!calibrated[side])
                {
                    expected[side] = guard[side]; expectedRot[side] = guardRot[side];
                }
                else if (input.Hand(side).tracked && c)
                {
                    Quaternion delta = c.rotation * Quaternion.Inverse(controllerRot0[side]);
                    expected[side] = mode == MappingMode.GuardRelative ? guard[side] + (c.position - controller0[side]) : c.position;
                    expectedRot[side] = delta * guardRot[side];
                }
                // While a controller is lost the arm holds its last pose.
                arm.Solve(expected[side], pole, driveWrist ? expectedRot[side] : (Quaternion?)null);
                UpdateMarker(side == 0 ? leftExpected : rightExpected, side == 0 ? leftErrorLine : rightErrorLine, side, c);
            }
        }

        void UpdateMarker(Transform marker, LineRenderer line, int side, Transform ghost)
        {
            if (marker)
            {
                // A replica of the controller, placed where the robot fist should be.
                marker.position = expected[side];
                marker.rotation = ghost && calibrated[side] ? ghost.rotation : expectedRot[side];
                marker.localScale = Vector3.one * Scale;
            }
            if (line)
            {
                line.positionCount = 2;
                line.SetPosition(0, expected[side]);
                line.SetPosition(1, Arm(side).hand.position);
            }
        }

        void Measure()
        {
            float s = Scale;
            for (int side = 0; side < 2; side++)
            {
                var st = stats[side]; var arm = Arm(side); var c = Ghost(side);
                st.positionError = Vector3.Distance(expected[side], arm.hand.position);
                st.rotationError = driveWrist ? Quaternion.Angle(expectedRot[side], arm.hand.rotation) : float.NaN;
                st.reach = Vector3.Distance(expected[side], arm.Shoulder) / arm.Length;
                st.clamped = arm.Clamped;
                if (!calibrated[side] || !c || !input.Hand(side).tracked) { st.directionError = st.gain = float.NaN; continue; }
                st.realDelta = (c.position - controller0[side]) / s;
                st.robotDelta = arm.hand.position - robotHand0[side];
                if (st.realDelta.magnitude > .03f)
                {
                    st.directionError = Vector3.Angle(st.realDelta, st.robotDelta);
                    st.gain = st.robotDelta.magnitude / st.realDelta.magnitude;
                    if (!st.clamped) st.peakDirection = Mathf.Max(st.peakDirection, st.directionError);
                }
                else st.directionError = st.gain = float.NaN;
                if (!st.clamped) st.peakPosition = Mathf.Max(st.peakPosition, st.positionError);
            }
        }

        static string Track(TrackedPose p) => p.tracked ? "OK" : "--";
        static string F(float v, string fmt) => float.IsNaN(v) ? "  -  " : v.ToString(fmt, CultureInfo.InvariantCulture);

        string HudText()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"ARM SYNC  {(mode == MappingMode.GuardRelative ? "GUARD-RELATIVE" : "ABSOLUTE")}  x{Scale:F2} (arm {userArmLength * 100:F0}cm)  {fpsSmoothed:F0}fps{(input.Simulating ? "  KEYBOARD SIM" : "")}\n");
            sb.Append($"TRACK head {Track(input.Head)}  L {Track(input.Left)}  R {Track(input.Right)}{(Recording ? "  REC" : "")}\n");
            sb.Append(message).Append('\n');
            sb.Append("    real    robot   gain  dir   pos    rot   reach\n");
            for (int side = 0; side < 2; side++)
            {
                var st = stats[side];
                sb.Append(side == 0 ? "L " : "R ");
                sb.Append($"{F(st.realDelta.magnitude * 100, "F1"),5}cm {F(st.robotDelta.magnitude, "F2"),5}m {F(st.gain, "F1"),4} {F(st.directionError, "F0"),3}deg {F(st.positionError * 100, "F1"),5}cm {F(st.rotationError, "F0"),3}deg {st.reach * 100,3:F0}%{(st.clamped ? " LIMIT" : "")}\n");
            }
            sb.Append($"peak dir L {stats[0].peakDirection:F0} R {stats[1].peakDirection:F0} deg | peak pos L {stats[0].peakPosition * 100:F1} R {stats[1].peakPosition * 100:F1} cm\n");
            sb.Append("Y recenter  X HUD  A CSV  B markers  R-stick click mode  R-stick up/down arm length  L-stick click side view");
            return sb.ToString();
        }

        void OpenCsv()
        {
            try
            {
                string folder = Path.Combine(Application.persistentDataPath, "ArmSync");
                Directory.CreateDirectory(folder);
                RecordingPath = Path.Combine(folder, DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".csv");
                csv = new StreamWriter(RecordingPath, false) { AutoFlush = false };
                csv.WriteLine("time,side,tracked,ctrl_x,ctrl_y,ctrl_z,ctrl_qx,ctrl_qy,ctrl_qz,ctrl_qw,real_dx,real_dy,real_dz,expected_x,expected_y,expected_z,robot_x,robot_y,robot_z,robot_dx,robot_dy,robot_dz,pos_err,dir_err,rot_err,reach,clamped,scale");
                message = "Recording " + RecordingPath;
                Debug.Log("ArmSync CSV: " + RecordingPath);
            }
            catch (Exception e) { csv = null; message = "CSV failed: " + e.Message; }
        }

        void WriteCsv()
        {
            if (csv == null) return;
            var inv = CultureInfo.InvariantCulture;
            for (int side = 0; side < 2; side++)
            {
                var st = stats[side]; var pose = input.Hand(side); var hand = Arm(side).hand.position;
                csv.WriteLine(string.Join(",", new[] {
                    Time.unscaledTime.ToString("F4", inv), side == 0 ? "L" : "R", pose.tracked ? "1" : "0",
                    pose.position.x.ToString("F4", inv), pose.position.y.ToString("F4", inv), pose.position.z.ToString("F4", inv),
                    pose.rotation.x.ToString("F4", inv), pose.rotation.y.ToString("F4", inv), pose.rotation.z.ToString("F4", inv), pose.rotation.w.ToString("F4", inv),
                    st.realDelta.x.ToString("F4", inv), st.realDelta.y.ToString("F4", inv), st.realDelta.z.ToString("F4", inv),
                    expected[side].x.ToString("F4", inv), expected[side].y.ToString("F4", inv), expected[side].z.ToString("F4", inv),
                    hand.x.ToString("F4", inv), hand.y.ToString("F4", inv), hand.z.ToString("F4", inv),
                    st.robotDelta.x.ToString("F4", inv), st.robotDelta.y.ToString("F4", inv), st.robotDelta.z.ToString("F4", inv),
                    st.positionError.ToString("F4", inv), st.directionError.ToString("F2", inv), st.rotationError.ToString("F2", inv),
                    st.reach.ToString("F3", inv), st.clamped ? "1" : "0", Scale.ToString("F3", inv) }));
            }
        }

        void CloseCsv()
        {
            if (csv == null) return;
            csv.Flush(); csv.Dispose(); csv = null;
            message = "Saved " + RecordingPath;
        }
    }
}
