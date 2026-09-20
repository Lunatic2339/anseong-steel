using UnityEngine;

namespace AnseongSteel.Bosses
{
    public class BossRotationTester : MonoBehaviour
    {
        [SerializeField] private BossRotation rotation;
        [SerializeField] private float angle = 90f;
        [SerializeField] private float targetYRotation = 180f;
        [SerializeField] private Transform lookTarget;

        [ContextMenu("Test/Turn Left")]
        private void TestTurnLeft()
        {
            if (!CanRun()) return;
            rotation.TurnLeft(angle);
            PrintState();
        }

        [ContextMenu("Test/Turn Right")]
        private void TestTurnRight()
        {
            if (!CanRun()) return;
            rotation.TurnRight(angle);
            PrintState();
        }

        [ContextMenu("Test/Rotate To")]
        private void TestRotateTo()
        {
            if (!CanRun()) return;
            rotation.RotateTo(targetYRotation);
            PrintState();
        }

        [ContextMenu("Test/Look At")]
        private void TestLookAt()
        {
            if (!CanRun()) return;

            if (lookTarget == null)
            {
                Debug.LogWarning("Look Target을 연결하세요.", this);
                return;
            }

            rotation.LookAt(lookTarget.position);
            PrintState();
        }

        [ContextMenu("Test/Stop Rotate")]
        private void TestStopRotate()
        {
            if (!CanRun()) return;
            rotation.StopRotate();
            PrintState();
        }

        [ContextMenu("Test/Print Rotation State")]
        private void PrintState()
        {
            if (!CanRun()) return;

            Debug.Log(
                $"IsRotating={rotation.IsRotating}, " +
                $"World Y Rotation={rotation.transform.eulerAngles.y:F3}",
                rotation);
        }

        private bool CanRun()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("Play 모드에서 테스트 메뉴를 실행하세요.", this);
                return false;
            }

            if (rotation == null)
            {
                Debug.LogWarning("Rotation에 BossRotation 컴포넌트를 연결하세요.", this);
                return false;
            }

            return true;
        }
    }
}
