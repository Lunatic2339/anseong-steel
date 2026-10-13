using System;
using UnityEngine;

namespace AnseongSteel.Hud
{
    public enum SuitPart { Head, Torso, LeftArm, RightArm, LeftLeg, RightLeg }

    // Shared robot condition, independent of pilot, camera and transient hit feedback.
    public sealed class SuitDamageState
    {
        readonly float[] damage = new float[6];
        public event Action Changed;
        public float this[SuitPart part] => damage[Index(part)];
        static int Index(SuitPart part)
        {
            int i = (int)part;
            if (i < 0 || i >= 6) throw new ArgumentOutOfRangeException(nameof(part));
            return i;
        }
        public void AddDamage(SuitPart part, float amount)
        {
            if (float.IsNaN(amount) || float.IsInfinity(amount) || amount <= 0) return;
            SetDamage(part, this[part] + amount);
        }
        // Repairs / snapshots are explicit; time never heals the accumulated state.
        public void SetDamage(SuitPart part, float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return;
            int i = Index(part); value = Mathf.Clamp01(value);
            if (damage[i] == value) return;
            damage[i] = value; Changed?.Invoke();
        }
        public void Reset() { Array.Clear(damage, 0, damage.Length); Changed?.Invoke(); }
        public static Color ColorFor(float value) => value >= .7f ? new Color(1f, .25f, .24f) :
            value >= .3f ? new Color(1f, .78f, .2f) : new Color(.28f, .78f, 1f);
    }
}
