# 콕핏 통합 플레이 확인

2026-10-11. 기준 브랜치: feat/player-script. HUD 커밋 66fadf2를 fast-forward로 포함한 뒤 통합 변경을 추가했다. 기존 sunwoo_HUDTest, ArmMotionTest 및 기존 보스 자산은 보존했다.

## 실행

Unity 6000.3.24f1에서 AnseongSteel 프로젝트를 열고 Assets/_Project/04_Scenes/sunwoo_IntegratedPlay.unity를 실행한다. 기본 입력은 오프라인 XRLocalBoth(컨트롤러 두 개로 양팔 검증)다. 온라인 연결은 아래 로컬 프로세스 검증 범위까지 완료했다. 실제 두 PC/HMD 조합은 미검증이다.

양손을 중립 위치로 되돌려 준비한 뒤 보스전이 시작된다. 공격은 그립을 잡은 상태의 실제 궤적과 주먹 접촉을 함께 확인한다. 트리거는 마지막 콤보 훅 적중 후 부스트다. 왼쪽 Y는 재보정, 오른쪽 B 또는 키보드 R은 전체 재시작이다. 양쪽 스틱이 계속 같은 방향을 가리킬 때 로봇 전방 기준으로 이동한다.

키보드 F1로 DesktopBoth와 XRLocalBoth를 전환할 수 있다. F/G를 각각 누른 상태에서 1/2는 좌/우 잽, 3/4는 좌/우 훅, Q/E는 적중 후 좌/우 부스트, Z/C는 좌/우 가드, WASD는 양쪽 이동 입력을 함께 보내는 오프라인 검사다. 키보드는 실제 사람의 궤적 정확도를 검증하지 않는다.

## 연결과 규칙

- 기존 V02 물리 콕핏의 외부 디스플레이로 전투 카메라를 표시하고 기존 HUD에 실제 HP, 콤보, 타격 및 수신 피해를 연결했다. 전투 카메라와 HMD 레이어를 분리해 콕핏이 디스플레이에 반복 촬영되지 않도록 했다.
- 왼잽→오른잽→왼훅 또는 반대 방향의 실제 적중 콤보를 인정한다. 빗나간 공격·잘못된 순서·시간 초과·받은 피해는 콤보를 끝내며 이전 피해량은 유지한다. 단순 이동과 가드는 잘못된 공격으로 계산하지 않는다.
- 마지막 훅 적중 후 추가 트리거 입력만 부스트로 인정하며 실패·지연 시 일반 훅 피해는 유지한다. 한 동작의 중복 접촉과 승패 이후 피해는 막는다.
- Assets/_BossPrototype/Generated/BossMecha.prefab과 기존 Punch 클립의 ImpactChecked 이벤트를 사용한다. 현재 하나의 오른팔 Punch 애니메이션으로 일반·강한·가드 불가의 세 규칙을 순환한다. 새 공격 애니메이션을 제작한 것은 아니다.
- 일반 공격은 해당 팔 가드, 강한 공격은 양팔 가드가 필요하다. 가드 불가 공격은 협동 횡이동으로 피한다. 실제 애니메이션 타격 이벤트와 로봇 콜라이더 후보로 피해를 확인한다.
- 보스전 중 잽, 훅, 방어/회피, 교대 콤보 성공에 따라 짧은 안내를 진행한다. 약 5분은 목표 표시이며 시간만 지나면 승리하지 않는다. HP가 0이 되면 승리/패배하고, 재시작은 HP·콤보·위치·HUD 피해·학습을 초기화한다.

HUD 부위 피해는 현재 공격 유형을 부위에 대응시킨 시험 매핑이다. 일반/가드 불가 오른팔 공격은 오른팔, 강한 공격은 몸통에 표시한다. 실제 맞은 해부학적 부위를 추적하는 구현은 아니다. 로봇 전체 HP와 별도로 누적 표시한다.

기존 VRMPAssets의 클릭/호버/부정/코인 샘플을 임시 타격·가드·피격·부스트 소리로 연결했다. 컨트롤러 햅틱도 이벤트에 연결했다. 실제 HMD, 컨트롤러 진동과 소리 청취는 아직 검증하지 않았다. HMD 화면 흔들림은 사용하지 않는다.

## 검증과 남은 연결

검사 결과는 AnseongSteel/RobotPlayValidation 아래에 있다. IntegratedRulesChecks는 순수 규칙 검사, IntegratedPlayValidation은 Unity Input System 합성 XR 두 컨트롤러→실제 궤적/접촉→HUD/보스 이벤트→승패/재시작 경로 검사다. HudRegression은 기존 HUD 별도 회귀 검사다. 합성 입력 통과가 실제 VR 하드웨어나 네트워크 검증을 대신하지 않는다.

새 동작 시작에 이전 완료 훅 후보가 남아 이른 접촉을 잘못 종료하던 PunchTrajectoryRecognizer를 수정했다. 새 후보만 초기화하고 LastCompleted 기록은 보존하며 별도 회귀 조건을 추가했다.

온라인 연결(검증 결과는 아래 후속 항목 참조): feat/lan-reliability의 dd81303은 병합 또는 cherry-pick하지 않았다. 현재 feat/player-script에 접속 수명주기의 필요한 로직을 통합 코드에 맞게 선택 적용했다. IntegratedOnlineState가 학습 단계·전투 시간·공격 표시·부위 피해와 실행 epoch를 서버 쓰기 전용 상태로 복제한다. 아직 실제 두 PC/HMD 검증을 수행한 것은 아니다.

기획 출처: 부모 작업이 확인한 Google Docs 기획 규칙(2026-10-02)을 적용했다. 공격 판정 임계값·피해량·보스 텔레그래프·가드 임계값은 조정 가능한 시험값이다. 타격 중 실제 사람의 자세와 네트워크 지연에 대한 튜닝은 장비 검증 단계에서 수행한다.

## 2026-10-11 검증 결과

- combat-checks.txt: 기존 순수 전투 검사 27개 통과.
- integrated-rules-checks.txt: 통합 규칙 검사 25개 통과.
- integration-checks.txt: 전체 합성 두 컨트롤러 Play Mode 검사 32개 통과(integration-full-07.log).
- 전송 주기 사이의 짧은 버튼 눌림도 보관하도록 XRLocalBoth 입력을 수정했다. 접촉 후 부스트가 실제 XR 입력 경로에서 보너스 피해로 이어지는 검사가 통과했다.
- integrated-combat/live-damage/evasion/victory/defeat.png는 이 Play Mode 실행에서 렌더한 화면이다. 피격 화면은 검사를 위해 일반 공격 피해를 35로 바꾼 상태이며 씬의 기본 일반 피해 12와 구별해야 한다. 승리 검사는 마지막 공격 피해를 올려 종료 전환을 확인하고 복구한다.

- HudRegression/hud-checks.txt: 기존 HUD 회귀 검사 49개 통과. HMD 및 실제 부위 피해 추적은 검증 범위 밖이다.


## 온라인 실행과 권한

Unity 메뉴 Anseong Steel → Robot Play → Open sunwoo Integrated Play로 Assets/_Project/04_Scenes/sunwoo_IntegratedPlay.unity를 열고 Play한다. 검사용 Windows 빌드는 같은 메뉴의 Build integrated LAN validation player다. 실행 파일은 AnseongSteel/RobotPlayValidation/IntegratedPlayer/IntegratedPlay.exe에 생성된다.

오프라인은 기존 F1/공격/재시작 동작을 유지한다. 온라인 패널에서 한 프로세스는 HOST / LEFT PILOT, 다른 프로세스는 호스트 IPv4를 입력하고 JOIN / RIGHT PILOT으로 접속한다. 127.0.0.1은 같은 PC 검사에만 사용한다. 두 조종사의 최신 입력과 중립 자세 준비를 확인한 후 호스트만 START RUN 또는 Enter로 시작한다. 온라인 시작/재시작 명령은 호스트 sender ID·실행 epoch·명령 순서·중복 간격을 서버에서 검증한다. 클라이언트의 HUD 재시작 버튼은 비활성화된다.

양팔을 한 프로세스에서 조작하는 오프라인 XRLocalBoth 설정도 온라인 접속 중에는 해당 조종사의 팔만 전송한다. 서버는 팔 역할, 입력 순서·유효 숫자·범위·이동 속도를 검사한다. 클라이언트는 피해·HP·학습 단계를 직접 쓰는 RPC를 갖지 않는다. 피드백 RPC는 서버만 호출한다. 단, 클라이언트가 보내는 자기 컨트롤러 입력이 실제 물리 장비에서 발생했는지 인증하는 구현은 아니다.

상대 이탈은 입력과 콤보를 지우고 현재 실행을 초기화한다. 호스트는 대기 상태로 돌아가며, 재입장 후 다시 양손 준비와 명시적 시작이 필요하다. 연결 중·이탈·실패 상태가 자동으로 오프라인 권한으로 바뀌지 않는다. 종료를 확인한 뒤 RECONNECT 또는 OFFLINE TEST를 명시적으로 선택한다. 세 번째 조종사는 거절한다.

오른쪽 조종사 카메라는 현재 선택한 콕핏의 실제 좌우 Pilot_Floor 기준점(기존 자산 간격 2.10m)을 사용한다. 기존 0.96m 임시 오프셋은 최신 펀치 보완에서 교체했다. HMD 상대 위치도 이 기준점에 더한다. 물리 콕핏 및 실제 HMD와 맞는 좌석 간격은 장비에서 확인해야 한다. 클라이언트는 서버의 기존 Punch 클립 공격 시점으로 시각 재생만 수행하며 해당 애니메이션 이벤트로 피해나 학습을 계산하지 않는다.

부위 피해는 기존 시험 매핑을 서버에서 계산한 누적 값으로 동기화한다. 전용 금속 타격·피격·부스트 효과음은 연결할 기존 전용 자산을 확인하지 못했으며 VRMPAssets 임시 샘플을 유지했다. 새 모델·애니메이션·오디오는 생성하지 않았다.


## 온라인 단계 검증 결과 (2026-10-11)

- Windows 개발 빌드 성공: online-build-04.log. 이전 모델·Punch 클립을 그대로 사용했다.
- 로컬 루프백 호스트·오른쪽 클라이언트·거절 대상 세 번째 프로세스로 검증했다. OnlineProcesses-20261011-211455/online-checks.txt가 최종 결과이며 final-states.json, 단계별 checkpoint JSON, 각 프로세스 로그를 함께 보관했다. 모든 조건 통과. 실제 두 PC나 실제 XR 입력을 검증한 결과는 아니다.
- 서버가 클라이언트 시작/재시작, 상대 팔 입력 RPC, 가짜 피해 피드백을 거절하고 HP·학습을 보존하는 것을 확인했다. 정상 잽·훅·가드·교대 콤보·적중 후 부스트·학습 완료와 전투 시간·부위 피해가 양쪽에 복제됐다.
- 중복 접속 시작·중복 실행 시작·중복 재시작, 오른쪽 이탈/재입장, 호스트 종료/재시작과 양쪽 재접속, 새 입력 순서 및 공유 상태 복구를 확인했다.
- 온라인에서 한 조종사의 스틱만으로는 움직이지 않고, 양쪽 유지 방향이 일치하면 공유 이동하며, 한쪽 해제로 멈추고 호스트 재시작으로 위치가 초기화됐다.
- 최종 오프라인 회귀: 기존 전투 27개, 통합 규칙 25개, 두 합성 XR 컨트롤러 통합 Play Mode 32개 모두 통과(online-offline-regression-final.log). 기존 HUD 회귀 49개 통과(HudRegressionOnline/hud-checks.txt).
- 재현: 통합 검증 플레이어를 위 Unity 메뉴로 빌드한 뒤 저장소 루트에서 python Tools/Validation/IntegratedOnlineProcesses.py를 실행한다. 도구는 검사 프로세스를 숨겨 실행하고, 자신이 만든 프로세스만 종료한다. 테스트는 포트 17877을 사용한다. 일반 플레이의 기본 포트는 7777이다.
- 검사 입력은 실제 전송·권한·궤적·접촉 경로를 사용하지만 프로그램이 만들어 보낸 입력이다. 보스 피해 검사는 기존 애니메이션 이벤트를 사용한다. 콤보 검사와 반복 공격이 간섭하지 않도록 검사에서만 보스 회복 시간을 늘리며, 버튼 입력 동안 기존 훅 궤적을 유지한다. 검사 JSON 읽기와 빌드 완료 순서 문제를 수정한 뒤 재실행했다. 초기 실패 로그는 그대로 보관했다.

미검증: 실제 두 PC의 방화벽/지연/패킷 손실, 실제 HMD 렌더·머리 추적·좌석 정렬·컨트롤러 입력과 햅틱·소리 청취. 실제 장비에서 좌석 간격과 공격 판정 튜닝이 필요하다. 부위 피해는 시험 매핑이며 실제 맞은 해부학적 부위 판정은 없다. 전용 효과음 자산은 여전히 부족하다.

최종 로컬 프로세스 검사는 동작 조건 39개 통과이며, 명령 처리 확인을 포함하면 PASS 기록 72개다. 실패는 0개다.


## 펀치 페이즈 보완 — 2026-10-12

기준은 최신 Google Docs ‘박선우)전투로직설계’ 3절 및 ‘9월 30일 회의록’과 이번 사용자 요청이다. 출처: https://docs.google.com/document/d/1Cwnr89HUdYNK8YOk5dMCuNWvWGiKTVo0rLrlhulRrvI/edit . 문서 읽기 담당이 전체 탭과 댓글을 확인해 전달한 규칙을 반영했다. 동시 펀치 협동 기술이나 별도 4타는 추가하지 않았다.

### 콕핏 선택 및 시점

기존 sunwoo_IntegratedPlay 씬을 위 Open 메뉴로 열고 Play한다. F2 또는 NEXT COCKPIT 버튼으로 V01 → V02 → A_CloseArc → B_RearDome → C_SideDome을 순환한다. 기본은 V02다. 오프라인 F3/시점 버튼은 좌우 비교 시점을 전환한다. 한 사람 두 컨트롤러 모드에서 이 시점 전환은 팔 소유권을 바꾸지 않는다. 온라인에서는 호스트/클라이언트 역할로 좌우 시점을 자동 선택한다. 각 조종사는 서로 다른 콕핏을 비교할 수 있으며, 콕핏 선택은 공유 전투 상태를 바꾸지 않는다.

기존 프리팹·모델·파일럿·보스 Idle/Walk/Punch 자산을 그대로 사용했다. 원본 메쉬/FBX는 생성하거나 수정하지 않았다. 셰이더의 화면 각도만 각 ArtSource 제작 스크립트의 UV 범위에 맞게 설정한다. A는 수평 208°, 수직 -32..43°, B는 180°, -86..86°, C는 180°, -74..79°이며 V01/V02 기본은 212°, -18..27°다. 기존 세 카메라의 광학 범위를 벗어나는 영역은 늘려 채우지 않고 검게 표시한다. 전체 구면의 추가 수직 광학 카메라는 이번 범위에 추가하지 않았다.

기존 파일럿의 neutralEye 보정(기본 약 1.665m 및 전방 0.075m)과 로컬 헬멧 숨김을 유지하고 실제 Pilot_Left_Floor/Pilot_Right_Floor에 배치한다. 모델별 바닥 높이와 머리 시점 때문에 HMD 실기에서 간격·눈 위치·시야 가림을 최종 확인해야 한다. 전투 판정 좌표는 콕핏/시점 교체나 이후 고개 회전으로 재보정하지 않는다. Y/재보정은 전방을 보고 손을 중립에 둔 상태에서 수행한다.

### 펀치와 추적

공격 버튼 유지·실제 궤적·실제 주먹 접촉을 함께 확인하고, 같은 동작의 다중 접촉은 한 타격으로 소비한다. 왼잽→오른잽→왼훅/반대 순서의 실제 명중을 인정한다. 최종 훅 명중 후 공격 버튼을 계속 유지하며 새 부스트 버튼 입력을 추가해야 보너스가 붙는다. 그립 해제는 기회를 취소하며 재그립으로 되살릴 수 없다. 실패해도 일반 훅 피해는 유지된다.

적중 후 기존 HUD에 HOLD GRIP + TRIGGER 안내를 표시한다. 짧은 슬로모션은 현재 보스 Animator의 시각 재생만 낮추는 임시 정책이다(기본 0.22초, 속도 0.35). 서버 입력/궤적/콤보/네트워크 시계의 Time.timeScale은 바꾸지 않는다. 전역 전투 슬로모션을 구현한 것으로 해석하지 않는다. boostWindowSeconds 등 타이밍·피해·분류 수치는 조절 가능한 시험값이다.

‘잘못된 파일럿 차례’는 연계 종료 규칙이며, ‘잘못된 공격 종류’는 별도 임시 정책 wrongKindEndsCombo로 분리했다. 기본은 종료이고, 이 값을 끄면 올바른 차례의 다른 공격 종류는 기존 제한시간을 연장하지 않고 보존한다. 잘못된 파일럿의 공격은 설정과 무관하게 연계를 종료한다. standaloneHookDamage도 미확정 단독 훅 정책이며 현재 통합 씬은 일반 훅을 허용하는 시험값이다.

위치 추적 비트가 없거나 isTracked가 거짓이면 로컬 입력을 즉시 무효화하고, 온라인은 해당 역할의 유실 알림을 서버에 보낸다. 상대 팔의 유실 알림은 권한 검사로 거절한다. NaN 위치·0/NaN 회전은 보정에 사용하지 않는다. 회전 추적만 없는 정상 위치 추적은 기준 회전으로 표시한다. 재획득 후 유지 중인 그립은 공격으로 쓰지 않으며 놓고 다시 누른 뒤 정상 공격한다. 재시작/재보정은 이전 버튼·회전·팔꿈치 추정 상태를 초기화한다.

손목은 가상 주먹의 길이 방향이 전완과 일자가 되는 기존 강체 제약을 유지한다. 펀치/복귀 중 원시 회전을 손목에 복사하지 않고, 펀치 중 회전의 팔꿈치 영향은 억제하며 복귀 회전은 감쇠·속도 제한한다. 정상 위치 궤적은 지연시키는 애니메이션으로 대체하지 않는다. 팔꿈치와 전완 방향은 손목 위치·가상 뼈 길이·움직임에서 추정하는 IK이며, 실제 팔꿈치/팔뚝 측정이나 실제 관절 구속이 아니다.

### 컨트롤러 햅틱

명중·가드·피격·교대 연계 성공·부스트를 서로 다른 기본 진폭/시간으로 구분한다. 같은 프레임은 피격 > 부스트 > 연계 성공 > 명중 > 가드 우선순위로 합치고, 같은 이벤트 중복 및 컨트롤러별 최소 간격을 제한한다. 기본 간격 0.09초와 각 프로필은 Inspector에서 조절하는 미확정 값이다. 온라인은 자신의 실제 타격과 공유 성공/피격을 자기 역할의 컨트롤러에 전달한다. 오프라인 두 컨트롤러는 공유 성공/피격을 양쪽에 전달한다. 재시작/비활성화 시 대기 및 진행 중 진동을 종료한다. 지원하는 XR 컨트롤러 impulse만 사용하며, 외골격·PM28·ESP32·실물 모터는 호출하지 않는다.

전용 효과음은 새로 만들지 않았다. 기존 VRMPAssets 임시 샘플을 유지하며 실제 진동·오디오 체감은 미검증이다.

### 검증 증거

RobotPlayValidation/PunchReview에 추적·노이즈·머리 회전·손목 정렬·햅틱 정책·잘못된 공격 종류/파일럿 구분 및 콕핏 비교 검사와 cockpit-0..4-left/right.png 10장이 있다. 번호는 V01, V02, A, B, C 순서다. 실제 Unity 카메라 렌더이며 HMD 캡처나 생성 이미지가 아니다. 초기 비교에서 발견한 재선택 재질 탐색 오류, V03 각도 매핑 왜곡, 로컬 헬멧 가림을 수정하고 다시 촬영했다.

검사 메뉴: Anseong Steel → Robot Play → Validate punch tracking and cockpit comparison. 기존 모션 코어는 PunchPhaseValidation.RunMotionCore로 원본 ArmMotionTest 씬을 다시 생성하지 않고 실행한다. 최종 전투/HUD/온라인 회귀의 세부 통과 수와 로그는 후속 결과 항목에 기록한다.

실기 미검증: 실제 HMD 프레임/좌석/시야·진동 구별 체감·오디오 청취·물리 컨트롤러 노이즈/추적 유실과 네트워크 지연. 특히 팔꿈치 추정, 자연스러운 훅의 체감, 회전 감쇠와 판정 임계값은 실제 VR에서 조정해야 한다.


## 전용 채팅 1차 검증 — 2026-10-12 UTC

이전 담당의 중지 확인 후 feat/player-script / 0f8525d의 미커밋 변경을 보존해 검증했다. Google Docs의 전체 탭을 가져와 박선우)전투로직설계 3절 및 9월 30일 회의록을 직접 재확인했다. 현재 댓글은 없다.

### 추가 보완

RobotPilotInput은 위치와 회전 추적 플래그, 두 입력 채널, 추적 상태 입력을 모두 요구한다. 회전 누락을 identity로 대체하거나 추적 플래그 누락을 정상 상태로 간주하지 않는다. 유실 전 처음 보정되기 전이라도 공격을 차단하며, 복구 뒤 그립을 놓아야 공격과 부스트를 다시 허용한다. 유실 알림은 상태 전환당 한 번 보내 반복 RPC를 억제한다.

PunchPhaseValidation에 회전 플래그 누락, 회전 액션 누락, 추적 상태 액션 누락의 즉시 차단·그립 유지 복구 차단·놓은 뒤 정상 재공격 9조건을 추가했다. IntegratedPlayValidation의 시작 및 유실 복구 준비 단계도 그립을 놓도록 맞췄다. 온라인 검증은 정상 좌우 추적 유실 RPC·재준비·연계 종료와 기존 피해 유지·재시작 후 협동 이동을 추가한다.

### 최종 코드의 증거

| 검사 | 통과 | 증거 (AnseongSteel 기준) |
|---|---:|---|
| 전투 코어 | 27 | RobotPlayValidation/combat-checks.txt |
| 통합 규칙 | 27 | RobotPlayValidation/integrated-rules-checks.txt |
| 오프라인 합성 XR 통합 | 32 | RobotPlayValidation/integration-checks.txt, dedicated-integration-02.log |
| 추적·손목·콕핏 | 44 | RobotPlayValidation/PunchReview/punch-checks.txt, dedicated-punch-review.log |
| 모션 코어 | 62 | RobotPlayValidation/PunchReview/motion-core-checks.txt, dedicated-motion-core.log |
| 기존 HUD | 49 | HudReview/hud-checks.txt, RobotPlayValidation/dedicated-hud.log |
| 온라인 프로세스 | 49조건 | RobotPlayValidation/OnlineProcesses-20261013-012358/online-checks.txt |

온라인은 명령 수신 확인까지 합치면 PASS 93줄이다. 같은 PC의 별도 호스트·클라이언트·추가 접속 프로세스로 검사했다. dedicated-online-build.log의 Build Finished, Result: Success와 BUILD_EXIT=0을 확인한 새 IntegratedPlayer/IntegratedPlay.exe를 사용했다. 온라인 자동 입력은 RPC/서버 판정 검증이며 실제 XR 센서 입력 검증은 아니다.

첫 오프라인 실행은 새 복구 규칙에 맞지 않는 합성 입력 준비 자세 때문에 실패했고, 첫 온라인 실행은 추가한 재시작 뒤 명시적 전투 시작이 빠져 이동 검사에서 실패했다. 런타임 안전 규칙을 완화하지 않고 검사 준비 순서를 수정했다. dedicated-integration.log와 OnlineProcesses-20261013-012211의 실패 증거도 보존했다.

현재 비교 렌더는 PunchReview/cockpit-0..4-left/right.png 10장이다. V01, V02, A, B, C 순서이며 실제 Unity 렌더다. sunwoo_IntegratedPlay 씬에서 F2/NEXT COCKPIT으로 선택하고 오프라인 F3로 좌우 시점을 비교한다. XRLocalBoth는 오프라인에서 한 사람의 두 컨트롤러를 이용한다. 온라인은 호스트 왼팔·클라이언트 오른팔로 제한된다. 추적 복구 후에는 그립을 한 번 놓고 다시 누른다. 부스트는 마지막 훅 명중 후 그립 유지 상태에서 해당 트리거를 추가로 누른다.

실제 HMD 시야·좌석·좌우 훅 체감·센서 노이즈·진동 및 음향 체감, 실제 두 PC의 지연·패킷 손실은 미검증이다. 팔꿈치와 전완은 IK 추정이며 센서 측정값이 아니다. 부스트 추가 연출은 아래 최종 보완에서 구현했다. 수치와 단독 훅·잘못된 종류 정책은 계속 임시 조절값이다.

검증 전에 설정 파일의 작업 사본을 저장했다. 이번 실행이 비운 preloadedAssets는 인계 당시 작업 사본으로 복구하고, 이번 실행이 재생성한 XR Simulation 4파일은 인계 당시 삭제 상태로 유지한다. 이전부터 있던 자동설정 변경·XR 삭제·임포트 메타는 사용자 변경과 섞여 있어 임의로 HEAD로 되돌리지 않는다. Unity는 이전 테스트가 남긴 PerformanceTestRunInfo/Settings JSON 및 메타를 실행 종료 시 자동 제거했다. HudReview는 이번 HUD 검사 증거로 남긴다. 커밋·스테이징·푸시는 하지 않았다.


## 부스트 성공 반응 최종 보완 — 2026-10-12 UTC

IntegratedPunchCue가 서버 승인 Boost 이벤트에만 기존 보스와 해당 팔의 시각 반응을 적용한다. 성공 시 기존 보스 모델이 잠깐 뒤로 밀리고 들리며 기울어진 뒤 원래 자세로 돌아온다. 팔은 기존 두 관절 구조를 사용해 조금 더 밀어 넣으며 손목의 로컬 고정 회전을 유지한다. 새 모델·메시·애니메이션은 만들지 않았다.

이 변형은 카메라 렌더 시작부터 종료까지만 적용한 뒤 원상 복구한다. 물리 콜라이더 동기화, 보스의 실제 전투 위치, 컨트롤러 입력, 타격 분류, 피해 판정, HMD 카메라를 변경하지 않는다. 연출 때문에 공격을 추가로 명중시키거나 원시 추적을 보정하지 않는다. 로봇 팔꿈치·전완은 계속 IK 추정이다.

Inspector의 IntegratedPunchCue에서 showBoostReaction을 끌 수 있다. 기본 임시값은 reactionSeconds=0.9초, bossRetreat=0.65m, bossLift=0.12m, bossTilt=12도, armPush=0.07m이다. 반응은 짧게 상승한 뒤 부드럽게 복귀한다. 같은 반응 중 중복 이벤트는 무시하며, 실패 입력·단독 훅·일반 훅만으로는 발생하지 않는다. 재시작과 네트워크 epoch 변경은 반응과 카운터를 지운다. 서버 전용 FeedbackRpc를 통해 양쪽 파일럿이 승인된 반응을 받는다.

### 마지막 코드 검증

| 검사 | 통과 | 증거 (AnseongSteel 기준) |
|---|---:|---|
| 전투 / 규칙 | 27 / 27 | RobotPlayValidation/combat-checks.txt, integrated-rules-checks.txt |
| 실제 충돌 + 합성 XR 통합 | 41 | RobotPlayValidation/integration-checks.txt, boost-integration.log |
| 추적·손목·콕핏·반응 곡선 | 47 | RobotPlayValidation/PunchReview/punch-checks.txt, boost-punch-review.log |
| 모션 코어 / 기존 HUD | 62 / 49 | PunchReview/motion-core-checks.txt, HudReview/hud-checks.txt |
| 온라인 | 53조건 | RobotPlayValidation/OnlineProcesses-20261013-014054/online-checks.txt |

온라인 PASS 줄은 수신 확인을 합쳐 99개다. boost-online-build.log의 성공한 최신 빌드로 실행했다. 위조 부스트의 피해·연출 차단, 양쪽 승인 반응 1회, 중복 부스트의 무추가 피해·무추가 연출, 호스트 재시작 후 양쪽 반응 초기화를 확인했다. 전체 좌석·권한·추적·재접속·협동 이동 검사도 통과했다.

오프라인은 추가 밀어넣기 도중 손목 정렬, 렌더 종료 시 보스 및 팔 관절의 정확한 복원, 전투 스냅샷·HMD 자세 불변, 연출 비활성화, 단독 훅 실패 및 재시작을 확인했다. integrated-boost.png와 integrated-boost-disabled.png는 실제 렌더다. 새 온라인 시도의 명령 파일 공유 잠금 실패(OnlineProcesses-20261013-013738)와 재시작 직후 0.35초 제어 제한 실패(013843)는 보존했다. 자동 검사에서 같은 명령 ID의 제한된 파일 재시도와 호스트 명령 간격을 보정했다. 서버의 보안·중복·제어 제한은 완화하지 않았다.

필수 코드 보완은 이번 범위에서 완료했다. 실제 HMD의 훅 자연스러움·센서 노이즈·좌석과 시야·진동 및 음향 체감, 두 실제 PC의 지연·패킷 손실·시각 반응 타이밍은 여전히 미검증이다. 연출 수치와 게임 판정 수치는 실제 VR에서 조정해야 한다.

V02 대표 좌우 비교 렌더를 직접 확인했다. 지원 Library 업로드 경로는 현재 호스트에서 prepare_uploads를 제공하지 않아 저장하지 못했다. library_file_id는 없다. 원본 경로는 RobotPlayValidation/PunchReview/cockpit-1-left.png 및 cockpit-1-right.png이며, 업로드 준비용 동일 복사본은 C:/Users/anthony/Documents/Codex/2026-10-13/task/library-boost-review/cockpit-v02-left.png 및 cockpit-v02-right.png에 있다.

XR Simulation 삭제 4파일은 인계 당시 삭제 표시를 보존했다. 작업 시작 스냅샷 before.diff에 삭제가 기록되어 있고 스냅샷 작업 사본에도 이 파일들은 없었다. Unity가 이번 검사에서 재생성한 사본만 보관 후 제거했다. ProjectSettings.asset의 이번 preloadedAssets 자동 변경은 인계 당시 바이트로 복구했다. 이전부터 있던 설정 수정·XR 삭제·미확인 임포트 메타는 유지하며 HEAD 복원·reset·stash·커밋·스테이징·push는 하지 않았다.
