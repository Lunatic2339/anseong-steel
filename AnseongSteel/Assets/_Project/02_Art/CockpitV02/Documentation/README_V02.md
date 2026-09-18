# 콕핏 V02 — 두 조종사의 공용 공간

퍼시픽림에서 원하는 분위기를 기계식 구속 장치 대신 묵직한 실내 구조와 두 사람이 공유하는 조종 공간으로 표현한 수정본입니다.

## 변경

- 천장에 매달린 연결 장치, 하네스, 늘어진 케이블 제거.
- 좌우 바깥쪽 기계 팔·지지 프레임 제거.
- 중앙의 독립형 콘솔과 버튼·스틱 제거. 전방 가장자리에 낮은 상태 표시 화면만 배치.
- 두 원형 발판을 높이 약 4cm의 조종 위치 표시로 단순화. 조종 위치 간격 2.10m와 지름 1.30m는 유지.
- 금속 바닥, 교체 가능한 구조 천장, 벽 패널, 조명, 212° 곡면 화면은 유지.
- 화면 아래 바닥 틈을 메우는 하부 판 추가.

상태 화면과 바닥 표시는 현재 시각적 시안입니다. 실제 입력이나 동기화 상태가 연결돼 있지는 않습니다. 조종사 아바타·전투 시스템은 추가하지 않았습니다.

## 파일과 Unity에서 열기

`Anseong_Cockpit_v02.blend`는 편집 가능한 Blender 원본, `.fbx`는 Unity용 모델입니다. `TemporaryCameraFeed.png`는 교체 가능한 임시 영상이며 도시 메시를 만들지 않았습니다.

기존 Unity 프로젝트의 `Assets/_Project/02_Art/CockpitV02/Scenes/Cockpit_Preview_V02.unity`를 열어 검토하세요. `Prefabs/Cockpit_V02.prefab`은 실제 씬에 배치할 수 있는 모델입니다. 기존 V01은 비교용으로 남아 있습니다. 원본은 저장소의 `ArtSource/CockpitV02/`에도 저장합니다.

`Overview.png`, `LeftEye.png`, `RightEye.png`, `LeftEye_Companion.png`는 Blender 렌더입니다. `Unity_*.png`는 실제 Unity 렌더, `Unity_Validation.txt`는 임포트 검증 결과입니다. Unity 패키지는 현재 프로젝트에 이미 설치한 자산의 배포용 사본입니다.

## 치수·재생성

단위는 미터입니다. 눈높이는 바닥 표시 위 1.65m, 공용 바닥 기준 1.69m를 가정했습니다. 기계식 천장 높이는 약 3.5m입니다. 실제 체형과 VR 착용감·팔 동작·성능은 헤드셋 검증이 필요합니다.

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' --background -t 8 --python '.\build_cockpit_v02.py' -- '출력 폴더의 절대 경로'
```

FBX·PNG·manifest를 Unity 전용 폴더에 복사하고 `Anseong Steel > Cockpit > Build or Refresh V02 Preview` 메뉴로 재생성합니다. 메뉴는 V02의 생성 씬·프리팹을 다시 쓰므로 수작업 수정은 복제본에서 하세요. Blender 원본의 `08_Ceiling_Removable`은 천장 교체·숨김용 컬렉션입니다.

Blender의 절차적 표면과 오프라인 조명은 Unity로 베이크하지 않아 외관 차이가 있습니다. 현재 모델에는 단순 바닥·위치 표시 영역 충돌체만 있고, XR 리그·입력·애니메이션은 포함하지 않습니다. 커밋·푸시는 하지 않습니다.
