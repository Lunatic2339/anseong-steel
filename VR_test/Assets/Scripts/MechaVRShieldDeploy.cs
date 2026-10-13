using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(11000)]
[DisallowMultipleComponent]
public sealed class MechaVRShieldDeploy : MonoBehaviour
{
    public Renderer energyRenderer;
    public Transform shieldVisualRoot;
    public Renderer emitterRenderer;
    [Tooltip("Forearm position anchor; auto-resolved from the mounted shield's parent.")]
    public Transform forearmAnchor;
    public Transform handAnchor;
    [Tooltip("Headset heading; pitch and roll do not tilt the shield.")]
    public Transform facingReference;
    [Min(0f)] public float forwardOffset = 0.16f;
    private Vector3 lastForward = Vector3.forward;
    public Renderer[] armorSeamLights = System.Array.Empty<Renderer>();
    [Min(0.05f)] public float deploymentSeconds = 0.45f;
    public bool IsOpen { get; private set; }
    public float Deployment { get; private set; }
    private InputAction toggleAction;
    private MaterialPropertyBlock properties;
    private static readonly int OpenId = Shader.PropertyToID("_Open");

    private void Awake()
    {
        ResolvePoseReferences();
        Deployment = 0f; IsOpen = false; ApplyVisual(); RefreshPose();
    }
    private void ResolvePoseReferences()
    {
        var body = GetComponentInParent<MechaVRBodyFollower>();
        if (!forearmAnchor && transform.parent && transform.parent.name == "LowerArm.L")
            forearmAnchor = transform.parent;
        if (!handAnchor && forearmAnchor)
            foreach (var bone in forearmAnchor.GetComponentsInChildren<Transform>(true))
                if (bone.name == "Hand.L") { handAnchor = bone; break; }
        if (!facingReference)
            facingReference = body && body.head ? body.head : Camera.main ? Camera.main.transform : null;
        Vector3 initial = body ? body.transform.forward : Vector3.forward;
        initial.y = 0f;
        if (initial.sqrMagnitude > 0.0001f) lastForward = initial.normalized;
    }
    private void LateUpdate() => RefreshPose();
    public void RefreshPose()
    {
        if (!energyRenderer || !forearmAnchor || !facingReference) return;
        Vector3 forward = Vector3.ProjectOnPlane(facingReference.forward, Vector3.up);
        if (forward.sqrMagnitude > 0.0001f) lastForward = forward.normalized;
        Vector3 center = handAnchor
            ? Vector3.Lerp(forearmAnchor.position, handAnchor.position, 0.55f)
            : forearmAnchor.position;
        // Preserve the imported mesh axes by moving the common visual pivot.
        Transform visual = shieldVisualRoot ? shieldVisualRoot : energyRenderer.transform;
        visual.SetPositionAndRotation(center + lastForward * forwardOffset,
            Quaternion.LookRotation(lastForward, Vector3.up));
    }
    private void OnEnable()
    {
        // Fast Play Mode may retain actions from the previous Input System state.
        // Recreate the action so device bindings and callbacks belong to this activation.
        OnDisable();
        toggleAction = new InputAction("ToggleLeftShield", InputActionType.Button,
            "<XRController>{LeftHand}/primaryButton");
        toggleAction.performed += OnPressed;
        toggleAction.Enable();
        ApplyVisual();
    }
    private void OnPressed(InputAction.CallbackContext context) => Toggle();
    public void Toggle() => IsOpen = !IsOpen;
    private void Update() => Advance(Time.deltaTime);
    // The same transition is exercised by the editor's deterministic input checks.
    public void Advance(float deltaTime)
    {
        Deployment = Mathf.MoveTowards(Deployment, IsOpen ? 1f : 0f,
            Mathf.Max(0f, deltaTime) / Mathf.Max(0.05f, deploymentSeconds));
        ApplyVisual();
    }
    private void ApplyVisual()
    {
        if (!energyRenderer) return;
        if (properties == null) properties = new MaterialPropertyBlock();
        energyRenderer.GetPropertyBlock(properties);
        properties.SetFloat(OpenId, Mathf.SmoothStep(0f, 1f, Deployment));
        energyRenderer.SetPropertyBlock(properties);
        energyRenderer.enabled = Deployment > 0f;
        if (emitterRenderer) emitterRenderer.enabled = Deployment > 0f;
        foreach (var seam in armorSeamLights)
        {
            if (!seam) continue;
            seam.SetPropertyBlock(properties);
            seam.enabled = false;
        }
    }
    private void OnDisable()
    {
        if (toggleAction == null) return;
        toggleAction.performed -= OnPressed;
        toggleAction.Disable();
        toggleAction.Dispose();
        toggleAction = null;
    }
    private void OnDestroy() => OnDisable();
}