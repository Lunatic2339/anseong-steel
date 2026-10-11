using UnityEngine;

namespace AnseongSteel.Bosses
{
    public class BossMovementTester : MonoBehaviour
    {
        [SerializeField] private BossMovement movement;
        [SerializeField] private float distance = 4f;
        [SerializeField] private Vector3 targetPosition = new Vector3(3f, 100f, 4f);
        [SerializeField] private float jumpHeight = 2f;
        [SerializeField] private float jumpDuration = 2f;

        [ContextMenu("Test/Move Left")]
        private void TestMoveLeft()
        {
            if (!CanRun()) return;
            movement.MoveLeft(distance);
            PrintState();
        }

        [ContextMenu("Test/Move Right")]
        private void TestMoveRight()
        {
            if (!CanRun()) return;
            movement.MoveRight(distance);
            PrintState();
        }

        [ContextMenu("Test/Move Up")]
        private void TestMoveUp()
        {
            if (!CanRun()) return;
            movement.MoveUp(distance);
            PrintState();
        }

        [ContextMenu("Test/Move Down")]
        private void TestMoveDown()
        {
            if (!CanRun()) return;
            movement.MoveDown(distance);
            PrintState();
        }

        [ContextMenu("Test/Move To")]
        private void TestMoveTo()
        {
            if (!CanRun()) return;
            movement.MoveTo(targetPosition);
            PrintState();
        }

        [ContextMenu("Test/Jump")]
        private void TestJump()
        {
            if (!CanRun()) return;
            movement.Jump(jumpHeight, jumpDuration);
            PrintState();
        }

        [ContextMenu("Test/Stop Move")]
        private void TestStopMove()
        {
            if (!CanRun()) return;
            movement.StopMove();
            PrintState();
        }

        [ContextMenu("Test/Print State")]
        private void PrintState()
        {
            if (!CanRun()) return;

            Debug.Log(
                $"IsMoving={movement.IsMoving}, IsJumping={movement.IsJumping}, " +
                $"Position={movement.transform.position.ToString("F3")}",
                movement);
        }

        private bool CanRun()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("Play 모드에서 테스트 메뉴를 실행하세요.", this);
                return false;
            }

            if (movement == null)
            {
                Debug.LogWarning("Movement에 보스의 이동 컴포넌트를 연결하세요.", this);
                return false;
            }

            // 비활성화된 보스의 상태 확인도 허용합니다.
            return true;
        }
    }
}