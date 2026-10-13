# 콕핏 V03 — 가까운 화면과 반구형 공간, 세 가지 비교안

화면 앞의 빈 공간을 줄이고, 두 사람이 같은 화면을 바라보는 구성을 유지한 실제 3D 모델입니다. 기존 V01/V02와 별도의 자산이며 현재 Unity 프로젝트의 Assets/CockpitVariants에 설치했습니다. 커밋·푸시는 하지 않았습니다.

| 버전 | 공간 구성 | 각 조종사 정면 화면 거리 | 화면 범위(중앙 기준) |
|---|---|---:|---|
| A_CloseArc | 짧아진 공용 바닥과 천장, 가까운 곡면 화면 | 약 2.47m | 수평 208°, 수직 75° |
| B_RearDome | 뒤쪽 벽에 연결된 두 발판, 전면 반구형 화면 | 약 2.43m | 수평 180°, 수직 172° |
| C_SideDome | 좌우 벽에서 나온 독립 발판, 더 가까운 넓은 반구형 화면 | 약 2.08m | 수평 180°, 수직 153° |

기존 V02의 같은 위치에서 정면 화면 거리는 약 3.91m입니다. 표의 거리는 눈 위치에서 정면 방향으로 쏜 선과 화면의 교점까지 계산한 값이며, 가장 가까운 화면 지점까지의 거리는 아닙니다. 반구형의 상하 끝에는 작은 개구부가 있습니다. C는 좌우로 넓힌 타원형 단면입니다.

두 조종사의 간격 2.10m, 발판 표시 지름 1.30m, 발판 상단 0.04m, 눈높이 1.69m는 고정했습니다. 천장에 매달린 기계나 조종사를 묶는 장치는 없습니다. 화면은 별도 메시와 UV, 교체 가능한 Display_Feed 재질로 분리했습니다.

## 확인하는 방법

각 버전 폴더의 `Overview.png`, `LeftEye.png`, `RightEye.png`, `LookDown.png`는 실제 Blender 모델 렌더입니다. 세 버전은 같은 카메라 위치와 렌즈로 렌더했습니다. B의 Overview만 내부를 보여주기 위해 뒤쪽 벽 패널을 임시로 숨긴 절개도입니다. Blender 원본과 FBX에는 벽이 있습니다.

각 폴더의 `.blend`는 편집 가능한 원본, `.fbx`는 Unity용 모델입니다. `.blend`에는 텍스처가 포함돼 있습니다. `TemporaryCameraFeed.png`는 공간 비교를 위한 도시 테스트 이미지이며 실제 외부 카메라 영상은 연결하지 않았습니다.

## Unity에서 열기

1. `CockpitVariants.unitypackage`를 기존 Unity 프로젝트에 임포트합니다. 자산은 `Assets/CockpitVariants`에 추가됩니다.
2. URP 프로젝트에서는 **Anseong Steel > Cockpit > Variants > Build All Three** 메뉴를 한 번 실행해 프로젝트에 맞는 재질을 다시 생성합니다. 배포 패키지는 Built-in 렌더 파이프라인 검증용 재질을 포함합니다.
3. 각 버전의 `Scenes` 폴더에서 `Cockpit_Preview_버전명.unity`를 엽니다. Play 시 왼쪽 조종사의 눈높이 카메라로 시작합니다. 오른쪽 시점은 `Preview_Cameras_Not_XR` 아래 `RightEye` 카메라로 확인할 수 있습니다.
4. 게임에 배치할 때는 해당 버전 `Prefabs` 폴더의 프리팹을 사용합니다. `Pilot_Anchors`에 두 조종사 위치가 있습니다.

메뉴는 이 패키지의 생성용 씬과 프리팹을 다시 쓰므로 수동으로 편집할 때는 복제본을 사용하세요. B/C 충돌체는 발판과 지지 구조의 실제 메시를 따릅니다. 발판 사이 허공을 가리는 공용 바닥 충돌체는 없습니다.

## 검증 범위

Blender에서 화면 법선이 안쪽을 향하는지, 치수, 눈 위치와 화면 거리, 내보낸 폴리곤 수를 확인했습니다. Unity 6000.3.24f1의 별도 프로젝트에서 스크립트 컴파일, FBX 임포트, 크기와 좌우/전후 방향, 재질 매핑, 프리팹·씬 생성, 실제 카메라 렌더, 패키지 내보내기를 확인했습니다. 버전별 `Unity_Validation.txt`와 `Unity_*.png`를 참고하세요.

추가로 실제 AnseongSteel 프로젝트의 URP에서 세 버전의 컴파일, 재질 재생성, 씬·프리팹 생성 및 조종사 시점 렌더까지 검증했습니다. 현재 프로젝트에서는 재생성 메뉴 없이 각 버전의 Scenes 폴더에서 바로 열 수 있습니다. 실제 XR 리그·조종사 모델·입력·실시간 화면은 연결하지 않았으며 헤드셋의 시야, 착용감과 성능은 아직 확인하지 않았습니다. 특히 반구형 화면의 실시간 영상은 투사 방식에 맞춰 연결해야 합니다. 단일 평면 카메라 영상을 그대로 늘리면 왜곡됩니다.

## 다시 생성하기

Blender 5.2에서 `build_variants.py`를 실행합니다. 출력 경로 다음에 `A_CloseArc`, `B_RearDome`, `C_SideDome` 중 하나를 넘깁니다.

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe' --background -t 8 --python './build_variants.py' -- './A_CloseArc' A_CloseArc
```

Unity 메뉴 스크립트는 unitypackage 안의 Editor 폴더에 포함됩니다.
