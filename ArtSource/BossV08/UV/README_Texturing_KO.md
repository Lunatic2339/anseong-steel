# Boss v015 — UV / 텍스처링 인수인계

최종 Blender 파일: E:\CAPSTONE1\blander\Boss_v015_UVReady.blend
원본 모델링 파일: E:\CAPSTONE1\blander\Boss_v014_ChestSymmetryFix.blend
UV 자료 폴더: E:\CAPSTONE1\blander\UV_v015

## 전달할 것
위 UVReady.blend와 이 UV_v015 폴더를 함께 전달하면 됩니다.
Blender 파일 자체에 UV, 8개 세트 재질, 재질 ID, 기존 리그가 포함되어 있습니다.
외부 텍스처 이미지에 의존하지 않습니다. 실제 PBR 텍스처 / 베이크 / FBX는 아직 만들지 않았습니다.

## UV 구성
| Set | 내용 | Object 수 | UV Island 수 |
|---|---|---:|---:|
| 01_TorsoHead | 몸통 / 머리 | 25 | 379 |
| 02_Pelvis | 골반 / 허리 하부 | 12 | 209 |
| 03_Arm_L | 왼팔 / 손 | 35 | 865 |
| 04_Arm_R | 오른팔 / 손 | 35 | 906 |
| 05_Leg_L | 왼다리 / 발 | 23 | 334 |
| 06_Leg_R | 오른다리 / 발 | 23 | 332 |
| 07_Shield | 왼팔 방패 | 18 | 901 |
| 08_Weapons | 검 / 총 / 미사일 / 레이저 | 39 | 1120 |

- 모든 210개 메시의 UV 채널 이름은 UVMap이며, 각 텍스처 세트 안에서 0–1 범위에 배치했습니다.
- 각 세트는 MAT_Boss_<Set> 재질 하나를 공유합니다. 서로 다른 세트끼리는 같은 0–1 좌표를 사용하므로, 전체 8개 세트를 하나의 텍스처로 합쳐서 칠하면 안 됩니다.
- 좌우 UV는 별도이며 의도적인 UV 겹침/미러 공유는 없습니다. 좌우 다른 데칼/손상을 그릴 수 있습니다.
- UV 아일랜드 간 여백은 4096px 작업 기준 16px를 기준으로 패킹했습니다. 해상도를 바꾸면 픽셀 여백도 비례합니다.
- 세트 안에서는 표면적에 맞춰 크기를 정리했습니다. 세트 사이의 픽셀 밀도는 완전히 동일하지 않습니다.
- 각 세트의 4096px PNG와 확장 가능한 SVG 배치도는 Layouts/에 있습니다.
- 모서리 기반 자동 투영 후 겹치는 코너와 미세 베벨 UV를 개별 수정했습니다. 라벨이나 특정 방향의 금속 결이 필요한 주요 장갑은 텍스처 담당자가 해당 아일랜드의 방향을 추가 정리할 수 있습니다.
- 수치적으로 극소인 기존 베벨 면도 삭제하지 않고 독립 UV를 배정했습니다.

## 원래 재질 구분
새 재질은 텍스처 세트 분리용입니다. 기존 색/금속성/거칠기/발광 값은 메시 속성을 통해 Blender에서 유지됩니다.
이 임시 속성 셰이더는 최종 Unity 셰이더가 아닙니다. 텍스처링 완료 후 각 세트의 이미지 맵으로 교체하세요.

ID_Material: CORNER/FLOAT_COLOR 색상 ID.
Material_ID: FACE/INT 원본 재질 번호.

| 번호 | 원래 재질 | ID 색 |
|---:|---|---|
| 0 | MAT_ArmorDark | 빨강 |
| 1 | MAT_ArmorMid | 초록 |
| 2 | MAT_InnerFrame | 파랑 |
| 3 | MAT_Weapon | 노랑 |
| 4 | MAT_EnergyRed | 자홍 |

Preview_Color / Preview_Emission은 기존 외형 표시용 속성입니다.
UV_Metallic / UV_Roughness / UV_EmissionStrength는 원래 셰이더 값입니다.
원래 5개 재질 데이터블록도 파일에 보존되어 있습니다.
다른 프로그램에서 vertex color를 ID 소스로 사용할 때 ID_Material을 선택/내보내세요. 이름 기반 슬롯 구분만으로는 원래 다섯 재질을 식별하지 못합니다.

## 보존 / 변경
- 모델링용 베벨·미러·용접·마스크·노멀·면 분할 Modifier는 이 UV 작업본에서만 적용했습니다. 따라서 실제 최종 표면과 좌우 모두 독립적으로 UV를 가집니다.
- 210개 메시의 평가된 버텍스 위치 및 폴리곤 연결을 원본과 비교: 전부 동일.
- 282,712 triangles. UV 작업으로 형상이나 삼각형 수를 늘리지 않았습니다.
- 67개 본, 15개 SOCK_ 소켓, Root, Object 이름, Parent, Transform, 기본 Pose 유지.
- Armature Modifier 210개 유지. 모든 버텍스의 유효 본 가중치 합 1 확인.
- 가동 부품 병합 없음. Animation Action 추가 없음.
- 원본 v014 덮어쓰기 없음. 이후 모델링 수정이 필요하면 원본의 비파괴 Modifier를 이용할 수 있습니다.

## QA
- UV 없는 메시: 0
- 0–1 범위를 벗어난 UV: 0
- 면적 0인 UV triangle (1e-13 이하): 0
- 세트 내부 UV triangle 겹침 (면적 1e-10 초과): 0
- 더 작은 부동소수점 경계 접촉은 검출 허용오차로 취급합니다.
- 정면 / 사선 / 후면 / 가슴 확대 체크무늬 검수 완료. Previews/의 이미지는 검수용이며 실제 디자인 텍스처가 아닙니다.
- UV_Manifest.json에 부위별 Object 목록과 검증값이 있습니다.

## Blender에서 확인
UV_Set_Select.py의 SET 값을 바꿔 실행하면 해당 세트의 메시만 선택하고 Edit Mode로 들어갑니다.
UV Editing 작업공간에서 UV를 볼 수 있습니다.
재질 색상 미리보기는 Material Preview 또는 Solid > Color > Attribute(Preview_Color)를 사용하세요.
ID를 확인할 때는 ID_Material 색상 속성을 활성화하거나 제공된 MaterialID_Front.png를 참고하세요.

## 다음 작업
텍스처 담당자는 세트별 Base Color / Roughness / Metallic / Normal / Emission 등을 제작하고,
원래 Material_ID 또는 ID_Material로 재질 마스크를 구분하면 됩니다.
노멀 베이크와 최종 Export 사이에는 같은 메시 삼각 분할을 유지하세요.
Unity용 해상도, 맵 패킹, 압축 및 머티리얼 설정은 실제 텍스처 제작 단계에서 결정하면 됩니다.
