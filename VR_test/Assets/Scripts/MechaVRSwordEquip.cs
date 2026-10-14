using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public sealed class MechaVRSwordEquip : MonoBehaviour
{
    public Transform rightHandGrip;
    public GameObject swordPrefab;
    [Tooltip("Handle center in the sword prefab's local coordinates (meters).")]
    public Vector3 swordGripPoint = new Vector3(0f, 0.875f, 0f);
    [Tooltip("Fine adjustment relative to the right hand grip socket.")]
    public Vector3 localPositionOffset;
    // The imported blade points along -Y; turn it toward the grip socket's +Y.
    public Vector3 localEulerOffset = new Vector3(0f, 0f, 180f);
    [Min(0.01f)] public float swordScale = 1f;

    [Tooltip("Only A presses with the right hand inside this torso-mounted box toggle the sword.")]
    public BoxCollider backDrawZone;
    public bool requireBackDrawZone = true;
    public bool entryHaptics = true;
    private bool wasInDrawZone;

    public bool IsHandInDrawZone
    {
        get
        {
            if (!rightHandGrip || !backDrawZone || !backDrawZone.enabled || !backDrawZone.gameObject.activeInHierarchy) return false;
            Vector3 point = backDrawZone.transform.InverseTransformPoint(rightHandGrip.position) - backDrawZone.center;
            Vector3 half = backDrawZone.size * .5f;
            return Mathf.Abs(point.x) <= half.x && Mathf.Abs(point.y) <= half.y && Mathf.Abs(point.z) <= half.z;
        }
    }

    private void LateUpdate()
    {
        bool inside = IsHandInDrawZone;
        if (inside && !wasInDrawZone && entryHaptics)
        {
            var device = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.RightHand);
            if (device.TryGetHapticCapabilities(out var capabilities) && capabilities.supportsImpulse)
                device.SendHapticImpulse(0, .25f, .06f);
        }
        wasInDrawZone = inside;
    }

    private void OnDrawGizmosSelected()
    {
        if (!backDrawZone) return;
        Gizmos.matrix = backDrawZone.transform.localToWorldMatrix;
        Gizmos.color = IsHandInDrawZone ? Color.green : Color.cyan;
        Gizmos.DrawWireCube(backDrawZone.center, backDrawZone.size);
        Gizmos.matrix = Matrix4x4.identity;
    }

    private InputAction equipAction;
    private GameObject equippedSword;
    public GameObject EquippedSword => equippedSword;

    private void OnEnable()
    {
        // Fast Play Mode may retain actions from the previous Input System state.
        // Recreate the action so device bindings and callbacks belong to this activation.
        OnDisable();
        equipAction = new InputAction("EquipRightSword", InputActionType.Button,
            "<XRController>{RightHand}/primaryButton");
        equipAction.performed += OnEquipPressed;
        equipAction.Enable();
    }

    private void OnEquipPressed(InputAction.CallbackContext context) => ToggleEquip();

    public void ToggleEquip()
    {
        if (requireBackDrawZone && !IsHandInDrawZone) return;
        if (equippedSword) Unequip();
        else Equip();
    }

    public void Unequip()
    {
        if (!equippedSword) return;
        var sword = equippedSword;
        equippedSword = null;
        // Hide immediately, including when destruction is deferred until frame end.
        sword.SetActive(false);
        if (Application.isPlaying) Destroy(sword);
        else DestroyImmediate(sword);
    }

    public void Equip()
    {
        if (equippedSword) return;
        if (!rightHandGrip || !swordPrefab)
        {
            Debug.LogWarning("[MechaVR] Assign the right hand grip and sword prefab before equipping.", this);
            return;
        }
        equippedSword = Instantiate(swordPrefab, rightHandGrip, false);
        equippedSword.name = "EquippedSword_Right";
        Quaternion rotation = Quaternion.Euler(localEulerOffset);
        equippedSword.transform.localScale = Vector3.one * swordScale;
        equippedSword.transform.localRotation = rotation;
        equippedSword.transform.localPosition = localPositionOffset - rotation * (swordGripPoint * swordScale);
    }

    private void OnDisable()
    {
        wasInDrawZone = false;
        if (equipAction == null) return;
        equipAction.performed -= OnEquipPressed;
        equipAction.Disable();
        equipAction.Dispose();
        equipAction = null;
    }

    private void OnDestroy()
    {
        OnDisable();
        Unequip();
    }
}
