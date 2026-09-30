using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations.Rigging;

public static class MechaVRBossContactTests
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Fixture : IDisposable
    {
        public readonly GameObject root, bossObject;
        public readonly MechaVRBossContact solver;
        public readonly MechaVRBossTarget boss;
        public readonly Vector3 origin = new Vector3(100f, 100f, 100f);
        public Fixture(Vector3 center, Vector3 size, float scale = 1f)
        {
            root = new GameObject("__MechaContactTestRig");
            root.transform.position = origin;
            root.transform.localScale = Vector3.one * scale;
            bossObject = new GameObject("__MechaContactTestBoss");
            bossObject.transform.position = origin + center * scale;
            var collider = bossObject.AddComponent<BoxCollider>();
            collider.size = size * scale;
            boss = bossObject.AddComponent<MechaVRBossTarget>();
            boss.logHits = false;
            solver = root.AddComponent<MechaVRBossContact>();
            solver.enabled = false;
            solver.boss = boss;
            solver.minimumHitSpeed = 0f;
            solver.hitCooldown = 0f;
            solver.arms = new[] { MakeArm("Left", 0f), MakeArm("Right", 0.9f) };
            solver.ConstrainPose();
        }

        private Transform Child(Transform parent, string name, Vector3 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            return go.transform;
        }

        private BoxCollider Volume(Transform bone, Vector3 center, Vector3 size)
        {
            var t = Child(bone, "Volume", Vector3.zero);
            var c = t.gameObject.AddComponent<BoxCollider>();
            c.center = center;
            c.size = size;
            c.enabled = true;
            c.isTrigger = true;
            t.gameObject.layer = 2;
            return c;
        }

        private MechaVRBossContact.Arm MakeArm(string label, float x)
        {
            var upper = Child(root.transform, label + " Upper", new Vector3(x, 0f, 0f));
            var fore = Child(upper, label + " Forearm", new Vector3(0f, -0.3f, 0f));
            var hand = Child(fore, label + " Hand", new Vector3(0f, -0.3f, 0f));
            return new MechaVRBossContact.Arm { label = label, upperArm = upper, forearm = fore, hand = hand,
                volumes = new[] { Volume(upper, new Vector3(0f, -0.15f, 0f), new Vector3(0.06f, 0.3f, 0.06f)),
                    Volume(fore, new Vector3(0f, -0.15f, 0f), new Vector3(0.06f, 0.3f, 0.06f)),
                    Volume(hand, Vector3.zero, Vector3.one * 0.12f) } };
        }

        public void Request(float left, float right = 0f)
        {
            solver.arms[0].upperArm.localRotation = Quaternion.Euler(-left, 0f, 0f);
            solver.arms[1].upperArm.localRotation = Quaternion.Euler(-right, 0f, 0f);
            solver.ConstrainPose();
        }

        public void Clear() => Require(!solver.HasPenetration(out float depth) || depth < 0.00005f, "Visible contact volume penetrates boss: " + depth);
        public void Dispose()
        {
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(bossObject);
        }
    }

    [MenuItem("Tools/Mecha VR/Test Boss Contact In Play Mode")]
    public static void RunMenu() => Debug.Log("[MechaVR] " + Run());

    public static string Run()
    {
        if (!EditorApplication.isPlaying) return "Boss contact tests require Play mode";
        var report = new StringBuilder(DateTime.UtcNow.ToString("O") + "\n");
        int passed = 0, failed = 0;
        Action<string, Action> test = (name, action) =>
        {
            try { action(); report.AppendLine("PASS " + name); passed++; }
            catch (Exception e) { report.AppendLine("FAIL " + name + ": " + e); failed++; }
        };
        test("Native query with enabled query-only trigger volumes", () =>
        {
            var a = new GameObject("__MechaQueryA");
            var b = new GameObject("__MechaQueryB");
            try
            {
                a.transform.position = b.transform.position = Vector3.one * 300f;
                var ca = a.AddComponent<BoxCollider>();
                var cb = b.AddComponent<BoxCollider>();
                Physics.SyncTransforms();
                bool enabledHit = Physics.ComputePenetration(ca, a.transform.position, a.transform.rotation, cb, b.transform.position, b.transform.rotation, out _, out _);
                ca.isTrigger = true;
                bool triggerHit = Physics.ComputePenetration(ca, a.transform.position, a.transform.rotation, cb, b.transform.position, b.transform.rotation, out _, out _);
                ca.enabled = false;
                bool disabledHit = Physics.ComputePenetration(ca, a.transform.position, a.transform.rotation, cb, b.transform.position, b.transform.rotation, out _, out _);
                report.AppendLine("  native enabled=" + enabledHit + " trigger=" + triggerHit + " disabled=" + disabledHit);
                Require(enabledHit && triggerHit, "Native collider query is unavailable for an enabled trigger volume");
            }
            finally { UnityEngine.Object.DestroyImmediate(a); UnityEngine.Object.DestroyImmediate(b); }
        });
        test("Contact stop, hold without repeated hits, withdraw, strike again, other arm independent", () =>
        {
            using (var f = new Fixture(new Vector3(0f, -0.3f, 0.35f), new Vector3(0.5f, 1f, 0.02f)))
            {
                f.Request(90f, 90f);
                Require(f.solver.arms[0].IsBlocked, "Left did not stop");
                Require(!f.solver.arms[1].IsBlocked, "Unobstructed right arm stopped");
                Require(Quaternion.Angle(f.solver.arms[1].upperArm.localRotation, Quaternion.Euler(-90f, 0f, 0f)) < 0.1f, "Right arm did not follow");
                f.Clear();
                Require(f.boss.HitCount == 1, "First strike count was " + f.boss.HitCount);
                for (int i = 0; i < 20; i++) f.Request(90f, 90f);
                Require(f.boss.HitCount == 1, "Holding contact repeated hits");
                f.Request(0f, 90f);
                Require(!f.solver.arms[0].IsBlocked, "Withdrawal stayed blocked");
                f.Clear();
                f.Request(90f, 90f);
                Require(f.boss.HitCount == 2, "Second strike was not registered");
                f.Clear();
            }
        });
        test("Fast hand sweep across thin boss with a clear end pose", () =>
        {
            using (var f = new Fixture(new Vector3(0f, -0.424264f, 0.424264f), new Vector3(0.15f, 0.06f, 0.006f)))
            { f.Request(90f); Require(f.solver.arms[0].IsBlocked, "Fast hand tunnelled through thin boss"); f.Clear(); }
        });
        test("Forearm-only contact", () =>
        {
            using (var f = new Fixture(new Vector3(0f, -0.3182f, 0.3182f), Vector3.one * 0.035f))
            { f.Request(90f); Require(f.solver.arms[0].IsBlocked && f.solver.arms[0].ContactPart.Contains("Forearm"), "Forearm contact not detected: " + f.solver.arms[0].ContactPart); f.Clear(); }
        });
        test("Upper-arm-only contact", () =>
        {
            using (var f = new Fixture(new Vector3(0f, -0.1061f, 0.1061f), Vector3.one * 0.025f))
            { f.Request(90f); Require(f.solver.arms[0].IsBlocked && f.solver.arms[0].ContactPart.Contains("Upper"), "Upper-arm contact not detected: " + f.solver.arms[0].ContactPart); f.Clear(); }
        });
        test("Head/body translation cannot push arms through boss", () =>
        {
            using (var f = new Fixture(new Vector3(0.4f, -0.3f, 0.35f), new Vector3(2f, 1f, 0.01f)))
            {
                f.root.transform.position = f.origin + Vector3.forward;
                f.solver.ConstrainPose();
                Require(f.solver.BodyBlocked && f.root.transform.position.z < f.origin.z + 0.35f, "Body tunnelled through boss");
                f.Clear();
                f.root.transform.position = f.origin;
                f.solver.ConstrainPose();
                Require(!f.solver.BodyBlocked, "Body cannot withdraw");
                f.Clear();
            }
        });
        test("Moving boss / initial overlap recovers without penetration", () =>
        {
            using (var f = new Fixture(new Vector3(0f, -0.3f, 0.5f), Vector3.one * 0.12f))
            {
                f.bossObject.transform.position = f.origin + new Vector3(0f, -0.45f, 0f);
                f.solver.ConstrainPose();
                f.Clear();
                Require(!f.solver.RecoveryFailed, "Overlap recovery failed");
            }
        });
        test("Uniform mech scale 2", () =>
        {
            using (var f = new Fixture(new Vector3(0f, -0.3f, 0.35f), new Vector3(0.5f, 1f, 0.02f), 2f))
            { f.Request(90f); Require(f.solver.arms[0].IsBlocked, "Scaled arm did not stop"); f.Clear(); }
        });
        test("Trigger-only boss does not block", () =>
        {
            using (var f = new Fixture(new Vector3(0f, -0.3f, 0.35f), new Vector3(0.5f, 1f, 0.02f)))
            { f.bossObject.GetComponent<BoxCollider>().isTrigger = true; f.Request(90f); Require(!f.solver.arms[0].IsBlocked && f.boss.HitCount == 0, "Trigger was treated as a solid surface"); }
        });
        test("Substep budget never skips unchecked motion", () =>
        {
            using (var f = new Fixture(new Vector3(0f, -0.424264f, 0.424264f), new Vector3(0.15f, 0.06f, 0.006f)))
            {
                f.solver.maxSubsteps = 16;
                for (int i = 0; i < 25; i++) { f.Request(90f); f.Clear(); }
                Require(f.solver.arms[0].IsBlocked, "Budget exhaustion skipped the obstacle");
            }
        });
        test("Actual mecha RigBuilder integration and unchanged XR controller transforms", RealRigTest);
        report.AppendLine("RESULT " + passed + " passed, " + failed + " failed");
        string folder = Path.Combine(Path.GetTempPath(), "MechaVR_Diagnostics_20260929");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "boss-contact-tests.txt"), report.ToString());
        return "Boss contact tests: " + passed + " passed, " + failed + " failed";
    }

    private static void RealRigTest()
    {
        var solver = UnityEngine.Object.FindObjectsByType<MechaVRBossContact>(FindObjectsInactive.Exclude)
            .FirstOrDefault(s => s.name == "MechaRoot");
        Require(solver && solver.arms.Length == 2, "Real scene contact solver is not configured");
        var builder = solver.GetComponentInChildren<RigBuilder>();
        var drivers = solver.GetComponentsInChildren<MechaVRHandTarget>();
        Require(builder && builder.graph.IsValid() && drivers.Length == 2, "Actual RigBuilder is not running");
        Transform[] bones = solver.arms.SelectMany(a => new[] { a.upperArm, a.forearm, a.hand }).ToArray();
        Quaternion[] rotations = bones.Select(t => t.localRotation).ToArray();
        Vector3[] targetPositions = drivers.Select(d => d.target.position).ToArray();
        Quaternion[] targetRotations = drivers.Select(d => d.target.rotation).ToArray();
        Vector3[] controllerPositions = drivers.Select(d => d.controller.position).ToArray();
        Quaternion[] controllerRotations = drivers.Select(d => d.controller.rotation).ToArray();
        bool[] enabled = drivers.Select(d => d.enabled).ToArray();
        Vector3 rootPosition = solver.transform.position;
        Quaternion rootRotation = solver.transform.rotation;
        MechaVRBossTarget originalBoss = solver.boss;
        bool solverEnabled = solver.enabled;
        var obstacle = new GameObject("__MechaActualRigContactTest");
        try
        {
            foreach (var d in drivers) d.enabled = false;
            solver.enabled = false;
            builder.Evaluate(0f);
            Vector3 forward = solver.transform.forward;
            float furthest = float.NegativeInfinity;
            foreach (var volume in solver.arms.SelectMany(a => a.volumes))
            {
                for (int mask = 0; mask < 8; mask++)
                {
                    Vector3 offset = Vector3.Scale(volume.size * 0.5f, new Vector3((mask & 1) == 0 ? -1f : 1f, (mask & 2) == 0 ? -1f : 1f, (mask & 4) == 0 ? -1f : 1f));
                    furthest = Mathf.Max(furthest, Vector3.Dot(volume.transform.TransformPoint(volume.center + offset), forward));
                }
            }
            Vector3 center = solver.arms[0].hand.position;
            center += forward * (furthest + 0.035f - Vector3.Dot(center, forward));
            obstacle.transform.SetPositionAndRotation(center, solver.transform.rotation);
            var box = obstacle.AddComponent<BoxCollider>();
            box.size = new Vector3(3f, 3f, 0.01f);
            var targetBoss = obstacle.AddComponent<MechaVRBossTarget>();
            targetBoss.logHits = false;
            solver.boss = targetBoss;
            solver.ResetContactState();
            solver.ConstrainPose();
            for (int i = 0; i < drivers.Length; i++) drivers[i].target.position = targetPositions[i] + forward * 0.4f;
            builder.Evaluate(1f / 90f);
            solver.ConstrainPose();
            Require(solver.arms.Any(a => a.IsBlocked), "Actual animated arms did not stop at the test surface");
            Require(!solver.HasPenetration(out float depth) || depth < 0.00005f, "Actual arm volume penetrated by " + depth);
            for (int i = 0; i < drivers.Length; i++)
            {
                Require(Vector3.Distance(drivers[i].controller.position, controllerPositions[i]) < 0.00001f, "XR controller position changed");
                Require(Quaternion.Angle(drivers[i].controller.rotation, controllerRotations[i]) < 0.001f, "XR controller rotation changed");
            }
        }
        finally
        {
            solver.boss = originalBoss;
            solver.transform.SetPositionAndRotation(rootPosition, rootRotation);
            for (int i = 0; i < drivers.Length; i++)
            {
                drivers[i].target.SetPositionAndRotation(targetPositions[i], targetRotations[i]);
                drivers[i].enabled = enabled[i];
            }
            builder.Evaluate(0f);
            for (int i = 0; i < bones.Length; i++) bones[i].localRotation = rotations[i];
            solver.enabled = solverEnabled;
            solver.ResetContactState();
            UnityEngine.Object.DestroyImmediate(obstacle);
        }
    }
}
