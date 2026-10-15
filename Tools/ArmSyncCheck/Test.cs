using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using AnseongSteel.ArmSync;

static class Test
{
    static int fails;
    static void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS " : "FAIL ") + what); if (!ok) fails++; }
    static System.Random rng = new System.Random(7);
    static Quaternion RandomRot() => Quaternion.Euler((float)rng.NextDouble() * 360, (float)rng.NextDouble() * 360, (float)rng.NextDouble() * 360);

    static Dictionary<string, Transform> BuildRobot(out Transform root, float scale = 6, bool randomAxes = true)
    {
        root = new GameObject("Robot").transform;
        root.localScale = Vector3.one * scale;
        root.localRotation = Quaternion.identity;
        var map = new Dictionary<string, Transform>();
        foreach (var b in Data.Bones)
        {
            string name = b.Item1, parent = b.Item2; Vector3 pos = b.Item3;
            var t = new GameObject(name).transform;
            t.SetParent(parent == null ? root : map[parent], false);
            t.rotation = randomAxes ? RandomRot() : Quaternion.identity;   // arbitrary bone axes, like an FBX import
            t.position = root.TransformPoint(pos);
            map[name] = t;
        }
        return map;
    }

    static void Invoke(object o, string m) => o.GetType().GetMethod(m, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Invoke(o, null);
    static T Field<T>(object o, string f) => (T)o.GetType().GetField(f, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(o);

    static ArmSyncArm MakeArm(Dictionary<string, Transform> bones, string s)
    {
        var hand = bones["mixamorig:" + s + "Hand"]; var f = hand.gameObject.AddComponent<RobotFistPose>(); f.side = s;
        return new ArmSyncArm { upper = bones["mixamorig:" + s + "Arm"], lower = bones["mixamorig:" + s + "ForeArm"], hand = hand, fist = f };
    }

    static void Main()
    {
        // 1. Fist: Unity-side result must equal the Blender-rendered fist (mirrored into Unity axes).
        foreach (var side in new[] { "Left", "Right" })
        {
            var bones = BuildRobot(out var root);
            var hand = bones["mixamorig:" + side + "Hand"];
            var fist = hand.gameObject.AddComponent<RobotFistPose>(); fist.side = side;
            fist.Build();
            var after = side == "Left" ? Data.AfterLeft : Data.AfterRight;
            float worst = 0;
            foreach (var kv in after)
            {
                var p = root.InverseTransformPoint(bones["mixamorig:" + side + (kv.Key == "Hand" ? "Hand" : "Hand" + kv.Key)].position);
                worst = Math.Max(worst, Vector3.Distance(p, kv.Value));
            }
            Check(fist.Ready && worst < 1e-3f, $"{side} fist matches Blender render (worst joint error {worst * 1000:F3} mm in model units)");
            // Building twice must not curl further.
            var before = bones["mixamorig:" + side + "HandIndex3"].position; fist.Build();
            Check(Vector3.Distance(before, bones["mixamorig:" + side + "HandIndex3"].position) < 1e-6f, $"{side} fist Build is idempotent");
        }

        // 2. IK: wrist reaches every reachable target, elbow on the pole side, any bone axes.
        {
            var bones = BuildRobot(out var root);
            var arm = new ArmSyncArm { upper = bones["mixamorig:LeftArm"], lower = bones["mixamorig:LeftForeArm"], hand = bones["mixamorig:LeftHand"] };
            arm.Capture();
            Check(arm.Ready, $"IK captured (upper {arm.UpperLength:F3} m, lower {arm.LowerLength:F3} m)");
            float worst = 0, worstPole = 1; int clampedOk = 0, n = 0;
            for (int i = 0; i < 2000; i++)
            {
                var dir = new Vector3((float)rng.NextDouble() * 2 - 1, (float)rng.NextDouble() * 2 - 1, (float)rng.NextDouble() * 2 - 1).normalized;
                float r = arm.Length * (.15f + .83f * (float)rng.NextDouble());
                var target = arm.Shoulder + dir * r;
                var pole = new Vector3(-.35f, -1, -.25f);
                arm.Solve(target, pole, null);
                worst = Math.Max(worst, Vector3.Distance(arm.hand.position, target));
                var n2 = (target - arm.Shoulder).normalized;
                var bend = Vector3.ProjectOnPlane(arm.lower.position - arm.Shoulder, n2).normalized;
                var p = Vector3.ProjectOnPlane(pole, n2).normalized;
                worstPole = Math.Min(worstPole, Vector3.Dot(bend, p)); n++;
                // segment lengths stay rigid
                if (Math.Abs(Vector3.Distance(arm.upper.position, arm.lower.position) - arm.UpperLength) > 1e-3f) Check(false, "upper length changed");
            }
            Check(worst < 1e-3f * arm.Length, $"IK reaches {n} random targets (worst {worst * 1000:F2} mm)");
            Check(worstPole > .999f, $"elbow always bends toward the pole (min cos {worstPole:F4})");
            var far = arm.Shoulder + new Vector3(0, 0, 1) * arm.Length * 2;
            arm.Solve(far, Vector3.down, null);
            Check(arm.Clamped && Vector3.Angle(arm.hand.position - arm.Shoulder, far - arm.Shoulder) < .5f, "out-of-reach target: arm straightens toward it and reports LIMIT");
        }

        // 3. End to end: recenter, then move/rotate the (simulated) controller.
        {
            var bones = BuildRobot(out var root);
            var go = new GameObject("Arm Sync");
            var input = go.AddComponent<ArmSyncInput>();
            var rig = go.AddComponent<ArmSyncRig>();
            rig.input = input;
            rig.rig = new GameObject("rig").transform;
            rig.head = new GameObject("head").transform; rig.head.SetParent(rig.rig, false);
            rig.leftController = new GameObject("L").transform; rig.leftController.SetParent(rig.rig, false);
            rig.rightController = new GameObject("R").transform; rig.rightController.SetParent(rig.rig, false);
            rig.robotHeadBone = bones["mixamorig:Head"];
            rig.leftArm = MakeArm(bones, "Left"); rig.rightArm = MakeArm(bones, "Right");
            Invoke(input, "Awake"); Invoke(rig, "Awake");
            Invoke(rig, "Update");
            var eye = (Vector3)typeof(ArmSyncRig).GetProperty("EyeAnchor", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(rig);
            Check(Vector3.Distance(rig.head.position, eye) < 1e-3f, $"recenter puts the eyes at the robot eye point (scale x{rig.Scale:F2})");
            var guard = Field<Vector3[]>(rig, "guard");
            Check(Vector3.Distance(rig.rightArm.hand.position, guard[1]) < 1e-3f && Vector3.Distance(rig.leftArm.hand.position, guard[0]) < 1e-3f, "after recenter both fists sit at the guard pose");
            var fwdKnuckle = rig.rightArm.hand.rotation * rig.rightArm.fist.KnuckleLocal;
            Check(Vector3.Angle(fwdKnuckle, Vector3.forward) < .5f, $"guard fist knuckles point robot-forward ({Vector3.Angle(fwdKnuckle, Vector3.forward):F2} deg)");

            var simHand = Field<Vector3[]>(input, "simHand"); var simEuler = Field<Vector3[]>(input, "simHandEuler");
            var r0 = rig.rightArm.hand.position; var l0 = rig.leftArm.hand.position;
            simHand[1] += new Vector3(0, 0, .10f);      // right hand 10 cm forward
            Invoke(rig, "Update");
            var moved = rig.rightArm.hand.position - r0;
            Check(Vector3.Angle(moved, Vector3.forward) < .5f && Math.Abs(moved.magnitude - .10f * rig.Scale) < 1e-3f,
                $"10 cm forward -> robot fist {moved.magnitude:F3} m forward (expected {.10f * rig.Scale:F3}), angle {Vector3.Angle(moved, Vector3.forward):F2} deg");
            Check(Vector3.Distance(rig.leftArm.hand.position, l0) < 1e-4f, "the other arm does not move");
            simHand[1] += new Vector3(-.08f, .06f, 0);   // up and inward
            Invoke(rig, "Update");
            moved = rig.rightArm.hand.position - r0; var expected = new Vector3(-.08f, .06f, .10f) * rig.Scale;
            Check(Vector3.Distance(moved, expected) < 1e-3f, $"diagonal move maps 1:{rig.Scale:F2} (error {Vector3.Distance(moved, expected) * 1000:F2} mm)");

            var palm0 = rig.rightArm.hand.rotation * rig.rightArm.fist.PalmLocal;
            simEuler[1] += new Vector3(0, 0, 90);         // roll the controller 90 deg about its pointing axis
            Invoke(rig, "Update");
            var knuckle = rig.rightArm.hand.rotation * rig.rightArm.fist.KnuckleLocal;
            var palm = rig.rightArm.hand.rotation * rig.rightArm.fist.PalmLocal;
            Check(Vector3.Angle(knuckle, Vector3.forward) < .5f && Math.Abs(Vector3.Angle(palm, palm0) - 90) < .5f, $"controller roll 90 deg -> fist rolls {Vector3.Angle(palm, palm0):F1} deg, knuckles stay forward");
            simEuler[1] += new Vector3(-40, 0, 0);        // tilt up 40 deg
            Invoke(rig, "Update");
            knuckle = rig.rightArm.hand.rotation * rig.rightArm.fist.KnuckleLocal;
            Check(Math.Abs(Vector3.Angle(knuckle, Vector3.forward) - 40) < .5f && knuckle.y > 0, $"controller pitch up 40 deg -> knuckles pitch up {Vector3.Angle(knuckle, Vector3.forward):F1} deg");
        }
        Console.WriteLine(fails == 0 ? "ALL PASSED" : fails + " FAILED");
    }
}
