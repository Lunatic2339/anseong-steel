# 안성 스틸 / 2인 콕핏 v01

이 시안은 기존 AnseongSteel 프로젝트의 `feat/cockpit` 브랜치를 대상으로 만든 **공간·시야 검토용 모델**입니다. 전투, 네트워크, XR 입력 시스템은 포함하지 않습니다.

## 파일과 열기

- `Anseong_Cockpit_v01.blend`: 수정 가능한 Blender 5.2 원본. 컬렉션마다 구조 분리, 카메라와 조명 포함. 화면 PNG는 원본 안에도 패킹되어 있습니다.
- `Anseong_Cockpit_v01.fbx`: Unity용 모델. 10개 모듈로 묶어 내보냈습니다.
- `TemporaryCameraFeed.png`: 교체 가능한 외부 카메라 테스트 화면. 도시는 텍스처 속 임시 그림이며 콕핏 메시가 아닙니다.
- `Overview.png`, `LeftEye.png`, `RightEye.png`: 실제 Blender 모델의 렌더.
- `LeftEye_Companion.png`: 왼쪽 조종 위치에서 오른쪽 동료 공간을 바라보는 검수 시점.
- `Unity_*.png`, `Unity_Validation.txt`: Unity 검증이 완료되면 생성되는 실제 Unity 출력.
- `build_cockpit.py`: 모델 재생성 스크립트.
- `CockpitPreviewBuilder.cs`: Unity 에디터 전용 임포트·프리팹·씬 생성 도구.

기존 저장소에는 원본을 `ArtSource/CockpitV01/`, Unity 자산을 `AnseongSteel/Assets/_Project/02_Art/CockpitV01/`에 둡니다. Unity의 `Scenes/Cockpit_Preview.unity`를 열면 별도 검토 씬을 볼 수 있습니다. 실제 씬에 적용할 때는 `Prefabs/Cockpit_V01.prefab`을 사용합니다. 기존 게임 씬과 Build Settings는 변경하지 않습니다.

## 공간 가정

| 항목 | 초기값 |
|---|---|
| 단위 | 1 Blender unit = 1 Unity metre |
| 조종 위치 중심 간격 | 2.10 m |
| 원형 발판 지름 | 1.30 m |
| 발판 상단 | 공용 바닥에서 0.20 m |
| 눈높이 | 발판 위 1.65 m / 공용 바닥 기준 1.85 m |
| 주 천장 구조 | 약 3.50 m |
| 공용 데크 | 약 6.15 × 6.10 m |
| 곡면 디스플레이 | 수평 212°, 반지름 4.05 m, 상하 약 45° |
| 안쪽 보조 콘솔 | 조작면 약 1.09 m |

실제 사용자 체형이나 장치 실측값이 없는 상태의 가정입니다. Blender 원본은 X=좌우, +Y=정면, Z=위이고, Unity는 X=좌우, +Z=정면, Y=위입니다. `Pilot_Anchors`의 좌우 바닥 기준 Transform은 발판 위에 있고, 자식 눈높이 Transform은 참고용입니다. 실제 XR Origin의 floor offset은 이와 중복 가산하지 마세요.

## 분리 구조와 교체

- `01_Deck`: 공용 바닥.
- `02_Station_L`, `03_Station_R`: 원형 발판.
- `04_Console_L`, `05_Console_R`: 안쪽 보조 콘솔.
- `06_Arm_L`, `07_Arm_R`: 각 담당 팔의 바깥쪽 기계 프레임.
- `08_Ceiling_Removable`: 천장 외피·보·연결 장치·케이블. 전체 비활성화 또는 교체 가능.
- `09_Hull`: 하부 구조·서비스 패널.
- `10_Display`: 곡면 화면·베젤. `Display_Feed.mat`의 Base Map을 교체하면 임시 영상이 바뀝니다. 실시간 외부 카메라를 쓰려면 이후 RenderTexture를 연결하세요.
- 원본의 `11_PreviewOnly`는 카메라·조명·눈높이 앵커용이며 FBX에서 제외됩니다.

Unity 프리팹에는 단순 바닥·발판 충돌체만 있습니다. 팔 프레임과 콘솔은 현재 시각적 배치 모델이며, 실제 컨트롤러 움직임을 물리적으로 제한하거나 추적하지 않습니다. 머리 뒤의 천장 연결부도 착용 장치의 최종 설계가 아닙니다.

## 재생성

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' --background -t 6 --python '.\build_cockpit.py' -- '원하는_출력_폴더의_절대경로'
```

스크립트 상단의 `SPACING`, `PAD_RADIUS`, `PAD_TOP`, `EYE_HEIGHT`, `CEILING` 및 각 구조 블록의 위치·치수를 조정할 수 있습니다. 일부 구조 치수는 블록 내부에 명시돼 있어 큰 치수 변경 시 함께 조정해야 합니다. 스크립트 실행은 출력 폴더의 생성물을 다시 씁니다. 원본에서 직접 수작업으로 수정했다면 별도 이름으로 저장하세요.

새 FBX, PNG, manifest를 Unity 전용 폴더의 해당 위치에 넣고 `Anseong Steel > Cockpit > Build or Refresh V01 Preview` 메뉴를 실행하면 재질, 프리팹, 검토 씬을 다시 생성합니다. 이 메뉴는 **전용 폴더의 생성된 프리팹·씬을 다시 씁니다**. 검토 씬을 직접 편집하려면 먼저 복제하세요. .blend를 Assets 밖에 둬 Unity의 자동 Blender 변환에 의존하지 않습니다.

## 검수 범위와 다음 확인

- 실제 Blender 원본 저장·재열기와 FBX 내보내기, 전체·좌·우 눈높이 렌더를 검수합니다.
- Unity 배치 검증 결과는 `Unity_Validation.txt`를 기준으로 확인하세요. Blender 절차적 미세 표면은 Unity에 베이크하지 않았으며, Unity에서는 URP PBR 기본값을 사용하므로 외관이 완전히 같지는 않습니다.
- 조종사 중앙 눈높이를 잇는 영역에 가로보나 높은 중앙 장치를 두지 않았습니다. 실제 동료 아바타는 아직 포함하지 않았습니다.
- 팔 장치가 담당 팔을 모든 방향으로 따라 움직이는지, 손·콘솔 간섭, 전방·상부 시야, 개인 키 차이, 양손의 교차 동작은 헤드셋에서 확인해야 합니다. 고정 기계 프레임은 가동 범위를 보장하지 않습니다.
- 프리뷰 카메라는 일반 카메라입니다. VR 검토에는 기존 프로젝트의 XR 리그와 동료 표현을 별도 씬 복제본에 배치해야 합니다.
- 메시·재질을 모듈별로 묶었으나 LOD, 라이트맵, 최종 텍스처와 헤드셋 GPU 프로파일링은 아직 하지 않았습니다. 최종 VR 성능을 보장하는 모델은 아닙니다.
- 커밋·푸시는 하지 않습니다.
