// Minimal stand-ins for the Unity API used by ArmSync, with Unity's math formulas,
// so the scripts can be compiled and the IK/fist math exercised without Unity.
using System;
using System.Collections.Generic;
namespace UnityEngine
{
    public struct Vector2 { public float x, y; public Vector2(float x, float y) { this.x = x; this.y = y; }
        public float sqrMagnitude => x * x + y * y; public float magnitude => (float)Math.Sqrt(sqrMagnitude);
        public static Vector2 operator *(Vector2 a, float d) => new Vector2(a.x * d, a.y * d); }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public float this[int i] => i == 0 ? x : i == 1 ? y : z;
        public static Vector3 zero => new Vector3(0, 0, 0); public static Vector3 one => new Vector3(1, 1, 1);
        public static Vector3 up => new Vector3(0, 1, 0); public static Vector3 down => new Vector3(0, -1, 0);
        public static Vector3 forward => new Vector3(0, 0, 1); public static Vector3 right => new Vector3(1, 0, 0);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator -(Vector3 a) => new Vector3(-a.x, -a.y, -a.z);
        public static Vector3 operator *(Vector3 a, float d) => new Vector3(a.x * d, a.y * d, a.z * d);
        public static Vector3 operator *(float d, Vector3 a) => a * d;
        public static Vector3 operator /(Vector3 a, float d) => new Vector3(a.x / d, a.y / d, a.z / d);
        public float sqrMagnitude => x * x + y * y + z * z; public float magnitude => (float)Math.Sqrt(sqrMagnitude);
        public Vector3 normalized { get { float m = magnitude; return m > 1e-5f ? this / m : zero; } }
        public void Normalize() { this = normalized; }
        public static Vector3 Cross(Vector3 a, Vector3 b) => new Vector3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        public static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public static float Distance(Vector3 a, Vector3 b) => (a - b).magnitude;
        public static Vector3 Scale(Vector3 a, Vector3 b) => new Vector3(a.x * b.x, a.y * b.y, a.z * b.z);
        public static Vector3 ProjectOnPlane(Vector3 v, Vector3 n) { float s = Dot(n, n); return s < 1e-12f ? v : v - n * (Dot(v, n) / s); }
        public static float Angle(Vector3 a, Vector3 b) { float d = (float)Math.Sqrt(a.sqrMagnitude * b.sqrMagnitude); if (d < 1e-15f) return 0; return (float)(Math.Acos(Math.Max(-1, Math.Min(1, Dot(a, b) / d))) * 180 / Math.PI); }
        public override string ToString() => $"({x:F4}, {y:F4}, {z:F4})";
    }
    public struct Quaternion
    {
        public float x, y, z, w;
        public Quaternion(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static Quaternion identity => new Quaternion(0, 0, 0, 1);
        public static Quaternion operator *(Quaternion a, Quaternion b) => new Quaternion(
            a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y, a.w * b.y + a.y * b.w + a.z * b.x - a.x * b.z,
            a.w * b.z + a.z * b.w + a.x * b.y - a.y * b.x, a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z);
        public static Vector3 operator *(Quaternion q, Vector3 v)
        {
            var u = new Vector3(q.x, q.y, q.z); var t = Vector3.Cross(u, v) * 2;
            return v + t * q.w + Vector3.Cross(u, t);
        }
        public static float Dot(Quaternion a, Quaternion b) => a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w;
        public Quaternion normalized { get { float m = (float)Math.Sqrt(Dot(this, this)); return new Quaternion(x / m, y / m, z / m, w / m); } }
        public static Quaternion Inverse(Quaternion q) { float n = Dot(q, q); return new Quaternion(-q.x / n, -q.y / n, -q.z / n, q.w / n); }
        public static Quaternion AngleAxis(float deg, Vector3 axis)
        { axis = axis.normalized; double h = deg * Math.PI / 360; float s = (float)Math.Sin(h); return new Quaternion(axis.x * s, axis.y * s, axis.z * s, (float)Math.Cos(h)); }
        public static Quaternion Euler(float x, float y, float z) => AngleAxis(y, Vector3.up) * AngleAxis(x, Vector3.right) * AngleAxis(z, Vector3.forward);
        public static Quaternion Euler(Vector3 e) => Euler(e.x, e.y, e.z);
        public static float Angle(Quaternion a, Quaternion b) { float d = Math.Min(Math.Abs(Dot(a.normalized, b.normalized)), 1f); return d > 0.999999f ? 0 : (float)(2 * Math.Acos(d) * 180 / Math.PI); }
        public static Quaternion FromToRotation(Vector3 a, Vector3 b)
        {
            a = a.normalized; b = b.normalized; float d = Vector3.Dot(a, b);
            if (d < -0.999999f) { var ax = Vector3.Cross(Vector3.right, a); if (ax.sqrMagnitude < 1e-6f) ax = Vector3.Cross(Vector3.up, a); return AngleAxis(180, ax); }
            var c = Vector3.Cross(a, b); return new Quaternion(c.x, c.y, c.z, 1 + d).normalized;
        }
        public static Quaternion LookRotation(Vector3 forward, Vector3 up)
        {
            var f = forward.normalized; var r = Vector3.Cross(up, f).normalized; var u = Vector3.Cross(f, r);
            float m00 = r.x, m01 = u.x, m02 = f.x, m10 = r.y, m11 = u.y, m12 = f.y, m20 = r.z, m21 = u.z, m22 = f.z;
            float tr = m00 + m11 + m22; Quaternion q;
            if (tr > 0) { float s = (float)Math.Sqrt(tr + 1) * 2; q = new Quaternion((m21 - m12) / s, (m02 - m20) / s, (m10 - m01) / s, .25f * s); }
            else if (m00 > m11 && m00 > m22) { float s = (float)Math.Sqrt(1 + m00 - m11 - m22) * 2; q = new Quaternion(.25f * s, (m01 + m10) / s, (m02 + m20) / s, (m21 - m12) / s); }
            else if (m11 > m22) { float s = (float)Math.Sqrt(1 + m11 - m00 - m22) * 2; q = new Quaternion((m01 + m10) / s, .25f * s, (m12 + m21) / s, (m02 - m20) / s); }
            else { float s = (float)Math.Sqrt(1 + m22 - m00 - m11) * 2; q = new Quaternion((m02 + m20) / s, (m12 + m21) / s, .25f * s, (m10 - m01) / s); }
            return q.normalized;
        }
    }
    public struct Color { public float r, g, b, a; public Color(float r, float g, float b, float a = 1) { this.r = r; this.g = g; this.b = b; this.a = a; } public static Color white => new Color(1, 1, 1); }
    public struct Rect { }
    public static class Mathf
    {
        public const float Rad2Deg = 57.29578f;
        public static float Clamp(float v, float a, float b) => v < a ? a : v > b ? b : v;
        public static float Clamp01(float v) => Clamp(v, 0, 1);
        public static float Max(float a, float b) => Math.Max(a, b); public static float Min(float a, float b) => Math.Min(a, b);
        public static float Abs(float a) => Math.Abs(a); public static float Sqrt(float a) => (float)Math.Sqrt(a);
        public static float Atan2(float y, float x) => (float)Math.Atan2(y, x);
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
    }
    public static class Time { public static float unscaledTime, unscaledDeltaTime = 1 / 72f; }
    public static class Application { public static event Action onBeforeRender; public static string persistentDataPath = "/tmp"; internal static void Touch() => onBeforeRender?.Invoke(); }
    public static class Debug { public static void Log(object o) => Console.WriteLine(o); public static void LogWarning(object o, Object c = null) => Console.WriteLine("WARN " + o); public static void LogError(object o, Object c = null) => Console.WriteLine("ERR " + o); }
    public class Object { public string name; public static void DestroyImmediate(Object o) { } public static implicit operator bool(Object o) => !ReferenceEquals(o, null); }
    public class Component : Object
    {
        public GameObject gameObject; public Transform transform => gameObject.transform;
        public T GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
        public T[] GetComponentsInChildren<T>() where T : Component => new T[0];
    }
    public class Behaviour : Component { public bool enabled = true; public bool isActiveAndEnabled => enabled; }
    public class MonoBehaviour : Behaviour { }
    public class GameObject : Object
    {
        readonly List<Component> comps = new List<Component>(); public Transform transform; public string tag; bool active = true;
        public GameObject(string name = "GameObject") { this.name = name; transform = new Transform(); transform.gameObject = this; transform.name = name; }
        public T AddComponent<T>() where T : Component, new() { var c = new T { gameObject = this, name = name }; comps.Add(c); return c; }
        public T GetComponent<T>() where T : Component { foreach (var c in comps) if (c is T t) return t; return null; }
        public T[] GetComponentsInChildren<T>() where T : Component => new T[0];
        public void SetActive(bool v) => active = v; public bool activeSelf => active; public bool activeInHierarchy => active;
        public static GameObject CreatePrimitive(PrimitiveType t) => new GameObject();
    }
    public enum PrimitiveType { Sphere, Capsule, Cylinder, Cube, Plane, Quad }
    public class Transform : Component
    {
        public Vector3 localPosition; public Quaternion localRotation = Quaternion.identity; public Vector3 localScale = Vector3.one;
        Transform parentT; readonly List<Transform> children = new List<Transform>();
        public Transform parent { get => parentT; set => SetParent(value, true); }
        public void SetParent(Transform p, bool worldStays)
        {
            var pos = position; var rot = rotation;
            parentT?.children.Remove(this); parentT = p; p?.children.Add(this);
            if (worldStays) { position = pos; rotation = rot; }
        }
        public int childCount => children.Count; public Transform GetChild(int i) => children[i];
        public Vector3 TransformPoint(Vector3 p) { var q = localPosition + localRotation * Vector3.Scale(localScale, p); return parentT != null ? parentT.TransformPoint(q) : q; }
        public Vector3 InverseTransformPoint(Vector3 p)
        { var q = parentT != null ? parentT.InverseTransformPoint(p) : p; var l = Quaternion.Inverse(localRotation) * (q - localPosition); return new Vector3(l.x / localScale.x, l.y / localScale.y, l.z / localScale.z); }
        public Vector3 position { get => parentT != null ? parentT.TransformPoint(localPosition) : localPosition; set => localPosition = parentT != null ? parentT.InverseTransformPoint(value) : value; }
        public Quaternion rotation { get => parentT != null ? parentT.rotation * localRotation : localRotation; set => localRotation = parentT != null ? Quaternion.Inverse(parentT.rotation) * value : value; }
        public Vector3 TransformDirection(Vector3 d) => rotation * d;
        public Vector3 InverseTransformDirection(Vector3 d) => Quaternion.Inverse(rotation) * d;
        public void SetPositionAndRotation(Vector3 p, Quaternion q) { position = p; rotation = q; }
        public void SetLocalPositionAndRotation(Vector3 p, Quaternion q) { localPosition = p; localRotation = q; }
    }
    public class Renderer : Component { public Material sharedMaterial; public Rendering.ShadowCastingMode shadowCastingMode; }
    public class MeshRenderer : Renderer { }
    public class LineRenderer : Renderer { public int positionCount; public float widthMultiplier; public void SetPosition(int i, Vector3 p) { } }
    public class Collider : Component { }
    public class Animator : Behaviour { }
    public class AudioListener : Behaviour { }
    public class Light : Behaviour { public LightType type; public float intensity; }
    public enum LightType { Directional }
    public class Camera : Behaviour { public float nearClipPlane, farClipPlane, fieldOfView; public CameraClearFlags clearFlags; public Color backgroundColor; public RenderTexture targetTexture; public StereoTargetEyeMask stereoTargetEye; }
    public enum CameraClearFlags { SolidColor } public enum StereoTargetEyeMask { None, Both }
    public class TextMesh : Component { public string text; public Font font; public int fontSize; public float characterSize; public TextAnchor anchor; public Color color; }
    public enum TextAnchor { UpperLeft }
    public class Font : Object { public Material material; }
    public class Texture : Object { }
    public class RenderTexture : Texture { public RenderTexture(int w, int h, int d) { } }
    public class Shader : Object { public static Shader Find(string n) => new Shader(); }
    public class Material : Object { public Shader shader; public Material(Shader s) { shader = s; } public bool HasProperty(string n) => true; public void SetColor(string n, Color c) { } public void SetTexture(string n, Texture t) { } }
    public static class Resources { public static T GetBuiltinResource<T>(string p) where T : Object, new() => new T(); }
    public static class RenderSettings { public static Rendering.AmbientMode ambientMode; }
    public class HeaderAttribute : Attribute { public HeaderAttribute(string s) { } }
    public class TooltipAttribute : Attribute { public TooltipAttribute(string s) { } }
    public class MinAttribute : Attribute { public MinAttribute(float f) { } }
    public class DefaultExecutionOrder : Attribute { public DefaultExecutionOrder(int o) { } }
}
namespace UnityEngine.Rendering { public enum ShadowCastingMode { Off, On } public enum AmbientMode { Trilight } }
namespace UnityEngine.SceneManagement { public struct Scene { } }
namespace UnityEngine.InputSystem
{
    using UnityEngine.InputSystem.Controls;
    public enum InputActionType { Value, Button }
    public class InputDevice { public bool added, enabled; }
    public class InputControl { public InputDevice device; }
    public class InputAction
    {
        public InputAction(string name = null, InputActionType type = default, string binding = null, string interactions = null, string processors = null, string expectedControlType = null) { }
        public object AddBinding(string path) => null;
        public void Enable() { } public void Disable() { }
        public bool WasPressedThisFrame() => false;
        public T ReadValue<T>() where T : struct => default;
        public List<InputControl> controls = new List<InputControl>();
    }
    public static class InputSystem { public static T GetDevice<T>() where T : InputDevice => null; }
    public class Keyboard : InputDevice { public static Keyboard current; public ButtonControl leftShiftKey, aKey, dKey, eKey, qKey, wKey, sKey, iKey, jKey, kKey, lKey, oKey, uKey; }
    public class Mouse : InputDevice { public static Mouse current; public ButtonControl rightButton; public Vector2Control delta; }
}
namespace UnityEngine.InputSystem.Controls
{
    public class ButtonControl : InputControl { public bool isPressed; }
    public class Vector2Control : InputControl { public Vector2 ReadValue() => default; }
    public class Vector3Control : InputControl { public Vector3 ReadValue() => default; }
    public class QuaternionControl : InputControl { public Quaternion ReadValue() => default; }
    public class IntegerControl : InputControl { public int ReadValue() => 0; }
}
namespace UnityEngine.InputSystem.XR { public class XRHMD : InputDevice { } }
namespace UnityEditor
{
    using UnityEngine;
    public class MenuItem : System.Attribute { public MenuItem(string s) { } }
    public static class AssetDatabase
    {
        public static T LoadAssetAtPath<T>(string p) where T : Object => null; public static bool DeleteAsset(string p) => true;
        public static void CreateAsset(Object o, string p) { } public static void SaveAssets() { }
        public static bool IsValidFolder(string p) => true; public static string CreateFolder(string a, string b) => "";
    }
    public static class PrefabUtility { public static Object InstantiatePrefab(Object o, UnityEngine.SceneManagement.Scene s) => null; }
    public static class EditorUtility { public static bool DisplayDialog(string a, string b, string c) => true; public static void SetDirty(Object o) { } }
    public static class Selection { public static GameObject activeGameObject; }
    public class EditorBuildSettingsScene { public string path; public EditorBuildSettingsScene(string p, bool e) { path = p; } }
    public static class EditorBuildSettings { public static EditorBuildSettingsScene[] scenes = new EditorBuildSettingsScene[0]; }
}
namespace UnityEditor.SceneManagement
{
    public enum NewSceneSetup { EmptyScene } public enum NewSceneMode { Single }
    public static class EditorSceneManager
    {
        public static bool SaveCurrentModifiedScenesIfUserWantsTo() => true;
        public static UnityEngine.SceneManagement.Scene NewScene(NewSceneSetup s, NewSceneMode m) => default;
        public static bool SaveScene(UnityEngine.SceneManagement.Scene s, string p) => true;
        public static UnityEngine.SceneManagement.Scene OpenScene(string p) => default;
    }
}
