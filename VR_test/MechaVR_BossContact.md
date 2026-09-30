# boss 타격과 팔 접촉 정지

씬: `Assets/Scenes/MainVRScene.unity`

## 동작

- 양팔의 상완, 전완, 손이 boss의 충돌체에 닿으면 해당 팔이 표면에서 멈춥니다. 반대쪽 팔은 계속 움직일 수 있습니다.
- 실제 컨트롤러 추적은 계속됩니다. 손을 보스에서 빼면 가상 팔이 다시 컨트롤러를 따라옵니다.
- 몸을 움직여 팔을 보스 안으로 밀어 넣는 경우에는 메카 몸통의 이동도 제한합니다. XR 카메라와 실제 컨트롤러의 위치는 변경하지 않습니다.
- 접촉 중 계속 밀고 있어도 타격을 매 프레임 집계하지 않습니다. 손을 뗐다가 다시 타격하면 새로운 타격으로 기록합니다.
- 이 구현은 보스와 접촉한 가상 팔의 자세를 제한하는 기능입니다. 보스 체력 감소나 밀려나는 효과는 별도로 연결할 수 있습니다.

## 확인 방법

1. Quest Link 연결 후 Unity에서 Play를 시작합니다.
2. Game 뷰를 클릭하고 F8을 누른 뒤, 양팔을 편하게 내린 기준 자세로 3초간 기다립니다.
3. boss 근처에서 한쪽 손을 천천히 표면으로 가져갑니다. 손·전완이 멈추는지, 손을 빼면 다시 따라오는지 확인합니다.
4. `MechaRoot > MechaVRBossContact` Inspector에서 Left/Right의 상태를 확인할 수 있습니다.
5. `boss > MechaVRBossTarget`의 Hit Count와 Console의 `[MechaVR] boss hit ...` 메시지로 새 타격을 확인합니다.

정지 판정은 느린 접촉에도 적용됩니다. 타격 이벤트는 기본적으로 0.2 m/s 이상의 움직임에서 발생하고, 같은 팔의 이벤트 사이에 최소 0.12초 간격을 둡니다. 속도는 충돌 영역 중심의 프레임 간 이동으로 추정합니다.

## 구성

- `MechaRoot`의 **MechaVRBossContact**: IK가 만든 자세를 받은 뒤, 상완·전완·손의 회전을 충돌 직전 자세까지만 허용합니다. 팔 길이와 뼈의 부모 관계를 유지합니다.
- 각 팔 뼈 아래의 **Boss Contact Volume**: 스킨 메시에서 해당 뼈에 연결된 정점으로 맞춘 박스 형태의 검사 영역입니다. 손가락은 손 영역에 포함했습니다. 표면 간 여유는 각 면 0.002 Unity 단위입니다.
- `boss`의 **MechaVRBossTarget**: 기존 Box Collider를 사용하며, 타격 횟수와 On Hit 이벤트를 제공합니다. 보스와 그 자식의 활성화된 일반 Collider가 대상이며 Trigger는 막는 표면으로 사용하지 않습니다.
- 기존 MechaVRHandTarget, MechaVRBodyFollower, RigBuilder 연결은 유지합니다.

검사용 Box Collider는 **활성화 + Is Trigger 활성화** 상태이고 Ignore Raycast 레이어를 사용합니다. 물리 힘으로 팔을 밀어내는 용도가 아니라, `Physics.ComputePenetration`으로 검사할 형상을 제공합니다. 접촉 정지는 **MechaVRBossContact**에서 수행합니다. 기본 `OnCollisionEnter` 대신 **MechaVRBossTarget의 On Hit**를 타격 처리에 연결합니다.

빠른 이동은 작은 구간으로 나누어 검사합니다. 프레임당 검사 한도를 넘는 큰 자세 변화는 확인한 구간까지만 이동하고 다음 프레임에 이어서 처리합니다.

## 조절

- 팔이 눈에 보이는 표면보다 너무 일찍 멈추면 해당 뼈 아래 `Boss Contact Volume`의 Box Collider **Center / Size**를 조절합니다. 메시를 박스로 감싼 근사 영역이므로 모서리나 손가락 사이에서는 실제 메시보다 먼저 닿을 수 있습니다.
- MechaRoot를 선택하고 Scene 뷰에서 Gizmos를 켜면 검사 영역을 볼 수 있습니다. 접촉 중인 팔은 빨간색입니다.
- `Minimum Hit Speed`는 타격 이벤트에 필요한 최소 속도, `Hit Cooldown`은 같은 팔의 이벤트 최소 간격입니다. 둘 다 접촉 정지 자체를 끄지는 않습니다.
- 메카 크기는 기존과 같이 MechaRoot의 X/Y/Z를 같은 배율로 조절합니다. 충돌 영역도 함께 확대됩니다.
- 보스 모델을 교체하면 새 오브젝트에 적절한 Collider와 MechaVRBossTarget을 추가하고, MechaRoot의 Boss 참조를 새 컴포넌트로 연결합니다. 런타임에는 이름 검색 대신 이 참조를 사용합니다.

## 타격 효과 연결

boss의 MechaVRBossTarget에서 **On Hit**의 `+`를 누르고, 체력이나 사운드를 담당하는 컴포넌트의 메서드를 연결합니다. 코드에서는 `HitCount`, `LastArm`, `LastPoint`, `LastSpeed`를 읽을 수 있습니다. On Hit는 충돌 진입 단위의 이벤트이며, 접촉을 유지하는 동안 반복 호출되지 않습니다.

## 검사와 백업

Unity Play 모드에서 자동 검사 11개를 통과했습니다. 표면 정지, 지속 접촉 중 중복 타격 방지, 손 빼기와 재타격, 반대쪽 팔의 독립 동작, 얇은 보스를 향한 빠른 이동, 상완·전완 접촉, 몸 이동, 초기 겹침 해소, 메카 배율 2, Trigger 제외, 검사 한도 처리를 확인했습니다. 실제 메카 RigBuilder와 연결한 검사에서도 충돌 정지와 XR 컨트롤러 Transform 보존을 확인했습니다.

- 자동 검사 메뉴: `Tools > Mecha VR > Test Boss Contact In Play Mode`
- 검사 기록: `MechaVR_Validation/boss-contact-tests.txt`
- 충돌 영역 치수: `MechaVR_Validation/boss-setup.txt`
- 수정 전 백업: `MechaVR_BossContact_Backup_20260929_233528`

형상 판정은 Unity의 [Physics.ComputePenetration](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Physics.ComputePenetration.html)을 사용합니다.
