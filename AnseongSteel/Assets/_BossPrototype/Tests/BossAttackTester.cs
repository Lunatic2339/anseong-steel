using UnityEngine;

namespace AnseongSteel.Bosses
{
    public class BossAttackTester : MonoBehaviour
    {
        [SerializeField] private BossHeavyPunch attack;
        private BossHeavyPunch subscribedAttack;

        private void OnEnable()
        {
            subscribedAttack = attack;
            if (subscribedAttack != null)
            {
                subscribedAttack.ImpactChecked += OnImpactChecked;
            }
        }

        private void OnDisable()
        {
            if (subscribedAttack != null)
            {
                subscribedAttack.ImpactChecked -= OnImpactChecked;
            }
            subscribedAttack = null;
        }

        [ContextMenu("Test/Heavy Punch")]
        private void TestHeavyPunch()
        {
            if (!CanRun()) return;
            bool accepted = attack.TryHeavyPunch();
            Debug.Log($"공격 요청 수락={accepted}, State={attack.State}", attack);
        }

        [ContextMenu("Test/Cancel Attack")]
        private void TestCancelAttack()
        {
            if (!CanRun()) return;
            attack.CancelAttack();
            PrintState();
        }

        [ContextMenu("Test/Print Attack State")]
        private void PrintState()
        {
            if (!CanRun()) return;
            Debug.Log($"IsAttacking={attack.IsAttacking}, State={attack.State}", attack);
        }

        private void OnImpactChecked(Collider[] candidates)
        {
            Debug.Log($"강펀치 판정 1회: 후보 Collider {candidates.Length}개", this);
            foreach (Collider candidate in candidates)
            {
                Debug.Log($"타격 후보: {candidate.name}", candidate);
            }
        }

        private bool CanRun()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("Play 모드에서 테스트 메뉴를 실행하세요.", this);
                return false;
            }

            if (attack == null)
            {
                Debug.LogWarning("Attack에 BossHeavyPunch 컴포넌트를 연결하세요.", this);
                return false;
            }

            return true;
        }
    }
}
