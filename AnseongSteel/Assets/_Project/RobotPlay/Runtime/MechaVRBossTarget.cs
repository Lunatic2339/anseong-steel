using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
public sealed class MechaVRBossTarget : MonoBehaviour
{
    [Tooltip("Called once when an arm makes a new impact. Connect damage, sound or effects here.")]
    public UnityEvent onHit = new UnityEvent();
    public bool logHits = true;
    public int HitCount { get; private set; }
    public string LastArm { get; private set; }
    public Vector3 LastPoint { get; private set; }
    public float LastSpeed { get; private set; }
    private Collider[] colliders;

    public Collider[] ContactColliders
    {
        get { if (colliders == null) RefreshColliders(); return colliders; }
    }

    private void OnEnable() => RefreshColliders();
    private void OnTransformChildrenChanged() => RefreshColliders();
    public void RefreshColliders() => colliders = GetComponentsInChildren<Collider>(true);

    public void RegisterHit(string arm, Vector3 point, float speed)
    {
        HitCount++;
        LastArm = arm;
        LastPoint = point;
        LastSpeed = speed;
        if (logHits) Debug.Log("[MechaVR] boss hit by " + arm + "; hits=" + HitCount + "; speed=" + speed.ToString("F2") + " m/s", this);
        onHit.Invoke();
    }
}
