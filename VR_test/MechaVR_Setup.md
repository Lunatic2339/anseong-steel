# Mecha VR 연결 및 위치 추적

프로젝트: `D:\UnityProject\VR test`  
씬: `Assets/Scenes/MainVRScene.unity`  
환경: Unity 6000.6.0f1, Quest 3 / Quest Link, PC OpenXR

## 실행

1. Quest Link를 연결하고 헤드셋과 양쪽 컨트롤러 추적을 활성화합니다.
2. Unity에서 MainVRScene을 열고 Play를 누릅니다.
3. Game 뷰를 클릭한 뒤 F8을 누릅니다. 정면을 보면서 양팔을 편하게 아래로 내리고 3초간 기준 자세를 유지합니다.
4. Console의 양쪽 `calibration complete` 메시지를 확인합니다. 손을 움직이거나 실제로 이동하면 팔과 메카 몸통 기준점이 따라옵니다.

각 Play 실행 때 다시 보정합니다. F8 대신 각 IK 오브젝트의 MechaVRHandTarget Inspector에서 `Calibrate after 3 seconds`를 눌러도 됩니다. Play 중 Inspector의 `Following controller`가 보정 완료 상태입니다.

## 이번에 수정한 원인

- Rig Builder의 비어 있던 Rig 참조를 ArmRig에 연결하고 저장했습니다. 양쪽 Two Bone IK Constraint는 ArmRig 하위에 있습니다.
- Animator의 Culling Mode를 Always Animate로 저장했습니다. 이전 Cull Update Transforms 상태에서는 검사 당시 목표만 움직이고 손목 뼈가 움직이지 않았습니다.
- 기존 손목 스크립트는 컨트롤러 이동량만 적용했고 MechaRoot는 씬에 고정돼 있었습니다. MechaVRBodyFollower가 헤드셋의 위치와 수평 방향을 따라가도록 연결했습니다.
- 손목은 동일한 머리 기준을 가진 BodyTrackingSpace와 MechaHeadSpace 사이에서 계산합니다. 몸을 움직일 때 이동량이 두 번 더해지지 않으며, 기본 배율 1에서는 손목 목표 위치가 컨트롤러 위치와 일치합니다. 실제 뼈는 팔 길이에 따른 IK 도달 범위의 제한을 받습니다.

## 현재 연결

```text
XR Origin (XR Rig)
  Camera Offset
    Main Camera
    Left Controller
    Right Controller
  BodyTrackingSpace

MechaRoot [MechaVRBodyFollower]
  MechaHeadSpace
  mecha_arms_PCVR [Animator + RigBuilder]
    Mecha_Armature / ... / UpperArm / LowerArm / Hand
    ArmRig [Rig]
      Two Bone IK Constraint.L [MechaVRHandTarget]
      Two Bone IK Constraint.R [MechaVRHandTarget]
```

각 MechaVRHandTarget의 Tracking Space는 BodyTrackingSpace, Mecha Space는 MechaHeadSpace입니다. Align Position To Controller는 활성화, Movement Gain은 1입니다. Target은 IK용 목표 Transform이며 Hand 뼈를 직접 지정하지 않습니다.

## 위치와 크기 조정

- 원본 모델 크기와 뼈 비율은 유지했습니다. MechaRoot의 X/Y/Z Scale을 같은 값으로 변경하면 전체 크기와 손 이동 범위가 함께 바뀝니다.
- XR Origin과 BodyTrackingSpace의 배율은 현재 1입니다. 메카만 확대할 때 이 둘까지 함께 확대하지 않습니다.
- 눈과 어깨 사이의 배치는 MechaHeadSpace의 Local Position으로 조정합니다. 현재 기준은 양쪽 어깨 뼈 중간점에서 위 0.18, 앞 0.06 Unity 단위입니다. Local Y를 높이면 눈을 기준으로 팔 전체가 아래로 내려갑니다.
- MechaRoot의 Position과 Rotation은 실행 중 추적 코드가 매 프레임 갱신합니다. 착용 위치 보정은 MechaHeadSpace에서 조정합니다.
- Follow Head Yaw는 머리의 수평 방향으로 몸통 방향을 추정합니다. 고개를 위아래로 기울여도 몸통은 기울지 않습니다. 실제 허리 추적을 사용한 것은 아닙니다.
- 손 회전이 어색하면 정면을 보고 기준 자세에서 F8로 다시 보정합니다.

## 검사 기록

2026-09-29 Unity Play 모드에서 다음 검사를 통과했습니다.

- 가상 머리와 컨트롤러를 함께 X +1m, Z +0.5m 이동: MechaRoot와 손목 목표도 같은 거리 이동.
- 손만 Y +0.15m, Z +0.1m 이동: 손목 목표만 해당 거리 이동.
- 가상 머리 45도 회전, 컨트롤러 위치 고정: 손목 목표 위치 유지.
- 머리와 메카 머리 기준점 위치 일치.
- 좌우 IK 목표를 각각 위로 0.06m 이동: 각 손목 뼈가 0.06m 이동.

수치 기록은 `MechaVR_Validation` 폴더에 있습니다. 위 수치 검사는 자동 검사입니다. 이후 사용자가 Quest 3 실기기에서 팔과 몸통이 내 위치를 따라온다고 확인했습니다. 확인 기록은 quest-confirmation.txt에 있습니다. 자동 진단 세션은 종료했습니다.

## 백업

몸통 추적 추가 직전 상태: `MechaVR_BodyFollow_Backup_20260929_230310`  
초기 Rig 수정 전 상태: `MechaVR_Backup_20260929_223043`

백업에는 MainVRScene.unity와 당시 스크립트가 있습니다. 원본 Blender 파일은 이번 Unity 수정에서 변경하지 않았습니다.
