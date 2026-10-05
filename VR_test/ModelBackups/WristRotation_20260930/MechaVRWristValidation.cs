using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

[InitializeOnLoad]
public static class MechaVRWristValidation
{
    const string Folder = @"C:\Windows\Temp\palm_repair";
    static MechaVRWristValidation() { EditorApplication.delayCall += Run; }
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    public static void Run()
    {
        if (!File.Exists(Folder + "/wrist_check.request")) return;
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
        { EditorApplication.delayCall += Run; return; }
        File.Delete(Folder + "/wrist_check.request");
        var scene = EditorSceneManager.NewPreviewScene();
        var report = new StringBuilder();
        try
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/mecha_arms_PCVR.fbx");
            Check(source != null, "FBX not loaded");
            var model = UnityEngine.Object.Instantiate(source);
            SceneManager.MoveGameObjectToScene(model, scene);
            model.SetActive(false);
            foreach (string side in new[] { "L", "R" })
            {
                Transform hand = null;
                foreach (var t in model.GetComponentsInChildren<Transform>(true))
                    if (t.name == "Hand." + side) hand = t;
                Check(hand != null, "Hand missing");
                var host = new GameObject("WristTest");
                SceneManager.MoveGameObjectToScene(host, scene);
                host.SetActive(false);
                var ik = host.AddComponent<TwoBoneIKConstraint>();
                var data = ik.data; data.tip = hand; ik.data = data;
                var driver = host.AddComponent<MechaVRHandTarget>();
                Transform NewTransform(string name)
                {
                    var go = new GameObject(name); go.transform.SetParent(host.transform); return go.transform;
                }
                driver.trackingSpace = NewTransform("Tracking");
                driver.mechaSpace = NewTransform("Mecha");
                driver.controller = NewTransform("Controller");
                driver.target = NewTransform("Target");
                driver.alignPositionToController = true;
                var flags = BindingFlags.NonPublic | BindingFlags.Instance;
                var init = typeof(MechaVRHandTarget).GetMethod("InitializeRotationOffset", flags);
                Check((bool)init.Invoke(driver, null), "Axis initialization failed");
                typeof(MechaVRHandTarget).GetField("calibrated", flags).SetValue(driver, true);
                Quaternion offset = (Quaternion)typeof(MechaVRHandTarget).GetField("gripToHandRotation", flags).GetValue(driver);
                Vector3 i = hand.InverseTransformPoint(hand.Find("Index.01." + side).position);
                Vector3 l = hand.InverseTransformPoint(hand.Find("Little.01." + side).position);
                Vector3 handle = (i - l).normalized;
                Vector3 fingers = ((i + l) * 0.5f).normalized;
                Vector3 palm = Vector3.Cross(handle, fingers).normalized;
                Check(Vector3.Dot(offset * handle, Vector3.forward) > .9999f, "Handle axis reversed");
                Check(Vector3.Dot(offset * palm, Vector3.right) > .9999f, "Palm axis reversed");
                // Independently check the model's physical palm normals in bind pose.
                Vector3 palmWorld = hand.TransformDirection(palm);
                report.AppendLine(side + " bind hand=" + hand.position.ToString("F5") + " palm=" + palmWorld.ToString("F5") + " handle=" + hand.TransformDirection(handle).ToString("F5") + " fingers=" + hand.TransformDirection(fingers).ToString("F5"));
                Check(Vector3.Dot(palmWorld, Vector3.right) > .8f, "Physical palm normal reversed");
                var poses = new[] { Quaternion.identity, Quaternion.Euler(90, 0, 0), Quaternion.Euler(-90, 0, 0),
                    Quaternion.Euler(0, 90, 0), Quaternion.Euler(0, 0, 90), Quaternion.Euler(35, -70, 125) };
                float worst = 0f;
                foreach (var heading in new[] { Quaternion.identity, Quaternion.Euler(0, 67, 0) })
                foreach (var pose in poses)
                {
                    driver.trackingSpace.rotation = heading;
                    driver.mechaSpace.rotation = heading;
                    driver.controller.rotation = heading * pose;
                    driver.controller.position = new Vector3(.2f, 1.1f, .4f);
                    foreach (var stale in poses)
                    {
                        driver.target.rotation = stale;
                        driver.ApplyTargetPose();
                        var expected = heading * pose * offset;
                        float err = Quaternion.Angle(driver.target.rotation, expected);
                        worst = Mathf.Max(worst, err);
                        Check(err < .1f, "Pose depends on initial target rotation");
                        Check(Vector3.Distance(driver.target.position, driver.controller.position) < .0001f, "Position regression");
                        Check(Vector3.Dot(driver.target.rotation * handle, heading * pose * Vector3.forward) > .9999f, "Grip alignment failure");
                    }
                }
                var xrDevice = InputSystem.AddDevice<XRController>();
                var action = new InputAction("TestGrip", InputActionType.Value, expectedControlType: "Quaternion");
                try
                {
                    action.AddBinding(xrDevice.deviceRotation);
                    action.Enable();
                    var poseDriver = driver.controller.gameObject.AddComponent<TrackedPoseDriver>();
                    poseDriver.rotationInput = new InputActionProperty(action);
                    typeof(MechaVRHandTarget).GetField("controllerPoseDriver", flags).SetValue(driver, poseDriver);
                    driver.trackingSpace.rotation = Quaternion.identity;
                    driver.mechaSpace.rotation = Quaternion.identity;
                    foreach (var grip in poses)
                    {
                        InputSystem.QueueDeltaStateEvent(xrDevice.deviceRotation, grip);
                        InputSystem.Update();
                        // An unrelated aim Transform must not change the hand's grip rotation.
                        driver.controller.rotation = Quaternion.Euler(25, 130, -40);
                        driver.ApplyTargetPose();
                        Check(Quaternion.Angle(driver.target.rotation, grip * offset) < .1f, "Aim pose used instead of device grip");
                    }
                    report.AppendLine(side + " PASS: 6 simulated XR grip poses with deliberately mismatched aim Transform");
                }
                finally { action.Dispose(); InputSystem.RemoveDevice(xrDevice); }
                report.AppendLine(side + " PASS: 72 pose/frame/initial-target cases; max error=" + worst + " degrees; offset=" + offset.ToString("F7") + "; bind-palm=" + palmWorld.ToString("F5"));
            }
            report.AppendLine("PASS: actual imported FBX skeleton and runtime ApplyTargetPose; no scene changes saved.");
            File.WriteAllText(Folder + "/wrist_validation.txt", report.ToString());
            Debug.Log("[MechaVR] Wrist rotation validation passed. " + report);
        }
        catch (Exception e) { File.WriteAllText(Folder + "/wrist_validation.txt", report + "FAIL: " + e); Debug.LogException(e); }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }
}
