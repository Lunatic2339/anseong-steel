using UnityEngine;

namespace AnseongSteel.Bosses
{
    // Animation Events are received on the same GameObject as the Animator.
    public sealed class BossAnimationEvents : MonoBehaviour
    {
        public BossAnimationDriver driver;
        public void PunchImpact() => driver?.OnPunchImpact();
        public void PunchFinished() => driver?.OnPunchFinished();
    }
}
