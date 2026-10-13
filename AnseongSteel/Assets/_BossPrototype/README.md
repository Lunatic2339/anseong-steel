# Mecha boss model test

Unity version: **6000.3.24f1**. Scope: `Assets/_BossPrototype`.

After the scripts and the three supplied FBXs import, the editor builds
`Scenes/BossModelTest.unity` once. Open that scene and press Play. Use Run demo for
idle, turning, walking to the dummy, and a right-hand punch. Automatic playback
is disabled in the current attack lab. The top-left
buttons restart/reset the demonstration or exercise each action. A successful
hit flashes the dummy orange and increments `Target hits`.

If automatic creation did not run, use **Tools > Boss Prototype > Create Model
Test Scene**. Existing generated scenes are never overwritten by this command.
The builder creates the controller, boss prefab and URP preview materials in
`Generated`. Original capsule test scenes are preserved.

## Animation choices

- The supplied `Animation/Idle.fbx`, `Walk.fbx`, and `Punch.fbx` all contain the
  same v026 mesh and 45-bone hierarchy. The original scene used the model in Idle.fbx; the current scene uses the updated compatible model described below.
- Generic animation is deliberate: these clips already target the same skeleton.
  This does not assert compatibility with unrelated Humanoid animation packs.
- Hips is used as the root-motion node; horizontal motion is extracted and
  Animator root motion is disabled. BossMovement controls world movement.
- Idle and Walk loop. Punch uses the complete 308-frame take (5.13 seconds at
  60fps), including the return to guard. The original FBX bytes remain unchanged.
- The left hand is the guard, not the attacking hand. The right-hand strike peaks
  at Blender frame 125 / FBX frame 124. The hit event runs at 124/60 seconds;
  recovery ends near the end of the full clip. A 0.22m sphere follows the right
  fist. The dummy is 2.5m tall to match this strike's height.
- TurnLeft and TurnRight use the supplied 2.07-second, approximately 90-degree
  takes. The builder samples Unity's source hips at 60 Hz, saves yaw-neutral
  clips in Generated/TurnLeftInPlace.anim and TurnRightInPlace.anim, and transfers
  the sampled yaw progression to the driver. Normalization happens before
  Animator blending, preserving pitch/roll without correcting blended bones.
  Entry blends for 0.22 seconds; exit blends for 0.28 seconds and keeps movement
  locked until settling completes. Root motion stays disabled.
  Smaller angles scale that progression; larger angles use multiple steps.
  Non-90-degree turns can still show some foot sliding and need dedicated clips
  or foot IK for final animation quality. Capsule tests retain their smooth turn.
- Use the Turn left 90 / Turn right 90 buttons to preview both directions.
  Face target also uses these animations. Movement and punching wait for turns.
- BossAnimationDriver locks movement/rotation during the punch. BossHeavyPunch
  keeps its original timer behavior in the existing capsule scenes; the new
  scene explicitly opts into animation-event timing.
- No jump clip was supplied; jump animation is intentionally outside this demo.

## Validation

**Tools > Boss Prototype > Run Model Smoke Test** opens the generated scene and
enters Play mode. It checks nonempty clips, event times, visible bone movement,
world movement, right-hand binding, one impact, target contact, full post-impact
recovery, final facing direction, and suppression of a duplicate impact event.
It also checks left/right 90-degree, right 180-degree, and left 45-degree turns,
their animation states, final angles, no position drift, and movement restoration.
The evaluated hips direction is checked throughout each turn and the blend to
Idle, so a correct actor transform alone cannot hide a visually doubled turn.
Results are written to `Library/BossModelSmokeTest.txt`.

Run this from a clean/saved editor state because it opens the test scene.
The scene is a visual prototype, not a complete damage/AI/networking system.
The known v026 shoulder-armor intersections can remain visible on large arm
motions. Materials are shared URP preview materials; art-source materials are
not edited.



## Updated boss model from develop (2026-10-11)

BossModelTest and BossMecha now use the revised mesh from commit fc66e14,
`_Project/02_Art/01_Models/mecha-finger-rig-mixamo-v026dsadsfadf.fbx`.
The art-source FBX and blend are unchanged. Existing five animation clips and
preview materials are reused. The right-hand hit point is rebound to the model.

The new export has different finger defaults/bind axes and unit conventions.
`Models/BossUpdatedCompatible.fbx` normalizes the export. `Models/CompatibleSkins`
contains the revised mesh geometry/weights with canonical animation bind matrices.
Keep these generated assets and their .meta files together with the scene/prefab.

To regenerate after another art update: close Unity, run Blender in background
with `Editor/make_boss_compatible.py`, then run Unity's
Tools > Boss Prototype > Apply Updated Boss Model. It updates the prefab and
scene and starts the existing play-mode smoke test. Save other scenes first.
A different source filename must be updated in the conversion script.
The installer validates all original bone positions before replacing the model.

### Blender textures (2026-10-11)
- Restored 122 packed base-color images and 124 Blender materials under `Models/Surface`, with the original UVs and per-part material slots.
- URP Lit materials use Blender metallic/roughness values. The source and compatible FBX importers remap to these materials; the boss prefab and test scene also use them.
- Regenerate images/manifest with Blender: `blender -b --python Assets/_BossPrototype/Editor/extract_boss_surface.py`, then run **Tools > Boss Prototype > Apply Blender Textures** in Unity.
- **Apply Updated Boss Model** preserves these textured materials. The earlier three-color preview materials are no longer used by the updated boss.


## 독립 공격 테스트 (2026-10-11)

`Scenes/BossModelTest.unity`를 열고 Play를 누릅니다. 첫 화면은 상대 메카 눈높이의 1인칭 카메라입니다. 콕핏 오브젝트는 생성하지 않습니다. 오른쪽 **BOSS ATTACK LAB**에서 **Sword distance / face target**으로 검 거리를 맞추거나 원하는 공격 버튼을 누르세요. 기본으로 선택한 공격의 적정 거리로 이동한 뒤 해당 공격 한 번만 실행합니다. 시점 전환으로 전체 동작도 볼 수 있습니다.

자동 데모는 꺼져 있습니다. 각 버튼은 해당 공격 한 번만 실행합니다. 공격 종료 후 다른 공격을 선택하거나 약점을 자동으로 노출하지 않습니다. 기존 **Run demo**는 별도의 이동·헤비펀치 시연으로 남아 있습니다.

각 공격은 `Runtime/Combat/Patterns`의 개별 컴포넌트와 `Combat`의 개별 `BossAttackDefinition` 에셋으로 분리했습니다. 사거리·대미지·쿨다운·판정 반경·모션의 유효 타격 구간을 각각 수정할 수 있습니다. 사용하지 않을 공격 컴포넌트를 비활성화하면 해당 공격은 실행되지 않습니다.

| 버튼 / 공격 | 개별 스크립트 | 현재 동작 |
|---|---|---|
| HeavyPunch | BossHeavyPunchPattern | 기존 오른손 펀치와 Animation Event 유지 |
| SwordSlash | BossSwordSlash | 대검 베기, 실제 검날 궤적 판정 |
| OverheadSmash | BossOverheadSmash | 대검 내려찍기, 실제 검날 궤적 판정 |
| AlternatingCombo | BossAlternatingCombo | 왼손 잽·오른손 크로스, 각 타격 별 판정 |
| MissileBarrage | BossMissileBarrage | 어깨에서 미사일 연사, 충돌·요격·취소 |
| Grab | BossGrab | 오른손으로 잡기, 반대 팔 반격으로 해제 |
| Feint | BossFeint | 내려찍기 모션의 예비 동작을 되돌린 뒤 타격 |
| Shockwave | BossShockwave | 지면 시전 모션과 확장되는 원형 판정 |
| LaserSweep | BossLaserSweep | 좌우로 훑는 레이저 판정 |
| EMP | BossEMP | 범위 내 HUD 비활성 상태 이벤트 |
| SensorJam | BossSensorJam | 왼쪽 플레이어 센서 차단 상태 이벤트 |
| VisionDisruption | BossVisionDisruption | 오른쪽 플레이어 시야 방해 상태 이벤트 |
| ArmLock | BossArmLock | 잡기 모션을 재사용한 일시적인 왼팔 사용 제한 |
| WeaponBreak | BossWeaponBreak | 베기 모션을 재사용한 일시적인 무기 사용 제한 |

`BossCombatController.TryAttack(BossAttackKind.SwordSlash)` 또는 각 컴포넌트의 `TryExecute()`로 하나씩 호출합니다. 다른 공격 중이거나 사거리·방향·쿨다운 조건을 만족하지 않으면 false를 반환합니다. `LastMessage`에서 이유를 확인합니다. 연속 펀치만 하나의 공격 내부에 두 타격을 포함하며, 여러 공격을 묶는 AI/페이즈/랜덤 선택기는 없습니다.

### 플레이어 팀과 연결할 부분

- `BossCombatTarget`은 테스트용 수신기입니다. `HitReceived`, `StatusChanged`, `GrabChanged` 이벤트를 실제 체력·팔·HUD 시스템에 연결하세요.
- 가드: `BeginGuard(BossArm.Left/Right/Both)`, `EndGuard(...)`. 충돌 직전 가드는 패링으로 처리합니다.
- 잡힘: 잡힌 팔은 가드·반격에 사용할 수 없습니다. 반대 팔의 `ReceivePlayerHit(damage, arm)`으로 해제하며 제한 시간 후에도 풀립니다.
- `BossCombatController.OpenCore(seconds)`, `Stun(seconds)`, `CancelAttack()`, `ResetEncounter()`는 독립 함수입니다. 약점 노출은 테스트 버튼으로 직접 실행하며 대미지 배율을 확인할 수 있습니다.
- `BossMissile.Intercept()`가 개별 요격 진입점입니다. 패널의 일괄 요격은 테스트 보조 기능입니다.
- HUD·센서·시야 방해와 무기 제한은 현재 상태 값과 이벤트를 제공합니다. 실제 플레이어 화면/무기를 변경하는 연출은 플레이어 구현에 맞춰 연결해야 합니다.
- 미사일은 임시 구체, 충격파는 디버그 링입니다. 레이저에는 가슴 충전·빔 연출을 적용했습니다. 사운드·네트워크 동기화·밸런스는 아직 포함하지 않았습니다.

### Mixamo 모션

로그인된 Mixamo의 v026 캐릭터로 **Without Skin / FBX Binary / 60 FPS / no keyframe reduction**을 다운로드했습니다. 원본은 `Animation/CombatSource`, 모델 경로를 맞추고 수평 이동을 제거한 클립은 `Animation/Combat`에 있습니다. 같은 v026 Generic 리그를 사용합니다.

대검 팩의 베기·내려찍기·대기·시전·기 모으기와 **Grab And Slam**, **Boxing (Jab Cross Combo)**를 연결했습니다. 잡기는 던지는 후반부를 잘라 손을 뻗은 자세에서 유지합니다. 페인트는 내려찍기 클립의 시간 흐름을 편집한 임시 모션입니다. EMP·센서·시야 방해는 같은 PowerUp 모션을, 팔 봉쇄는 Grab을, 무기 제한은 SwordSlash를 재사용합니다. 미사일과 레이저에는 별도 신체 모션 없이 발사/판정 기능을 제공합니다.

검은 공격 시작 시 등에서 손으로 즉시 장착되며, 다음 비검 공격이나 이동·리셋 때 돌아갑니다. 발도·납도·피격·사망 클립도 준비해 두었지만 별도 동작으로 아직 실행 연결하지 않았습니다. 손바닥 안에 오른손 그립을 맞추고 왼손은 닿는 구간에서만 손잡이를 보조하도록 보정합니다. 갑옷 간 간섭은 최종 모델에 맞춘 전용 모션으로 더 다듬을 수 있습니다.

**Tools > Boss Prototype > Build Downloaded Combat Animations**는 원본에서 클립을 다시 만들고 바인딩합니다. 수동으로 수정한 공격 클립/타격 구간은 다시 덮어쓸 수 있으므로 필요할 때만 사용하세요. **Bind Combat Animation Clips**는 정의 에셋에 직접 지정한 클립을 Animator에 연결합니다.

### 자동 검증

**Tools > Boss Prototype > Verify Independent Combat Patterns**에서 개별 공격의 명중·중복 콜라이더·쿨다운·가드/패링·잡기 해제·상태 만료·취소·미사일 요격·약점 배율·사망/리셋을 검사합니다. 결과: `Library/BossCombatVerification.txt`. 테스트 씬을 열고 Play 모드로 전환하므로 편집 중인 씬을 먼저 저장하세요.



### 검 그립·거리·1인칭 개선

- 검 방향은 손목 사이의 선 대신 오른손 검지/새끼손가락 관절 축을 기준으로 정합니다. 손바닥 안의 손잡이, 닫힌 손가락, 왼손 보조 IK를 사용합니다. 대기 자세는 바깥쪽 35도 대각 가드로 얼굴을 가리지 않게 합니다.
- 검 공격의 `preferredDistance`는 2.8m, 주먹·잡기는 1.05m입니다. 최대 공격 허용 거리(`range`)와 별도입니다. 거리만 늘리는 대신 실제 검날 판정을 유지합니다.
- 테스트 패널의 `Move to selected attack distance`가 켜져 있으면 선택한 공격 거리로 전진/후퇴하고, 요청한 공격 한 번만 실행합니다. 끄면 현재 자리에서 즉시 실행합니다. 후퇴는 별도 역재생 걷기 클립을 사용합니다. 기존 Run demo는 여전히 1.05m의 헤비펀치 시연입니다.
- 1인칭 눈높이는 2.25m를 유지하고 전면 피격체 중심에서 0.55m 뒤에 카메라를 둡니다. 검 거리에서 몸통·다리와 검 동작이 보이도록 약간 아래를 봅니다. 콕핏 오브젝트는 없습니다.
- `Revive defeated test target before attack`는 체력이 0인 테스트 타깃을 다음 버튼 실행 전에 복구합니다. 실제 전투 수신기는 자동 부활하지 않습니다. 처치한 공격도 복귀 모션을 끝까지 재생합니다.
- 자동 검증은 검 거리에서의 명중, 손잡이 이탈, 후퇴 모션/거리, 처치 후 복귀를 추가로 확인합니다.
- 재적용 메뉴: **Tools > Boss Prototype > Polish Sword Grip and Test Distances**. 수동으로 조정한 거리와 카메라를 기본값으로 맞추므로 필요할 때만 실행하세요.

### 검날 정렬·후퇴 순간이동·가슴 레이저 보정

- 검 공격은 `BossAttackDefinition.bladeRoll` 곡선으로 손잡이 축을 회전시킵니다. 공격 클립의 검 끝 이동 궤적을 샘플링해 검의 면 법선과 궤적이 수직이 되도록 계산하며, 오른손 손바닥의 고정점은 유지합니다. 준비/복귀 구간은 부드럽게 연결하고 복귀 시 불필요한 한 바퀴 회전을 피합니다.
- `WalkBackward`의 원본 골반 이동 데이터를 제거했습니다. 이 리그는 Armature가 회전되어 있어 골반 로컬 X/Y가 수평축입니다. 높이 변화와 다리 동작은 유지하고 실제 위치는 `BossMovement`가 이동시킵니다. 일반 공격 준비의 Idle 전환은 0.18초 블렌드를 사용합니다.
- `BossChestLaser`는 Spine 아래 `ChestLaserMuzzle`에서 충전 링, 중심 빔, 붉은 광선과 끝점 발광을 생성합니다. 가슴 움직임을 따라가고 디버그 판정을 꺼도 표시됩니다. 장애물 앞에서 광선이 끝나며 취소/리셋 시 즉시 꺼집니다. 별도 신체 발사 애니메이션이나 사운드는 아직 없습니다.
- **Tools > Boss Prototype > Polish Blade Edge and Chest Laser**로 재적용합니다. 기존 검 그립·거리 재설정을 포함하므로 사용자 조정값은 먼저 확인하세요. 공격 클립을 교체하면 이 메뉴로 검날 곡선을 다시 계산합니다.
- Play 검증은 펀치 후 검 거리로 후퇴할 때 골반이 보스 원점에서 튀지 않는지, 레이저의 가슴 높이·디버그 독립 표시·취소도 확인합니다. `BossCombatFinishInstaller.Review`는 검날 정렬 수치와 실제 포즈 이미지를 Library에 출력합니다.

### 메카 주먹 포즈 보정

- `BossHandPose`가 헤비펀치와 연속 펀치에서 양손 30개 손가락 관절을 보정합니다. 메카 관절의 기준 위치/길이를 유지하면서 네 손가락의 끝마디까지 접고 엄지는 바깥으로 감쌉니다. 준비 구간에서 서서히 쥐고 복귀 구간에서 보정된 편안한 손으로 직접 이어집니다. 펀치 상태에도 대기 손을 기본 포즈로 적용해 중간에 원본 손 포즈가 섞이지 않도록 합니다.
- 원본 Blend/FBX 및 스킨 가중치는 변경하지 않았습니다. 기존 Mixamo 모션의 손 포즈를 메카에 맞게 보완하는 Unity 컴포넌트입니다. 대기/이동에는 편안하게 편 손, 펀치에는 주먹, 검에는 손잡이를 감싸는 별도 포즈를 적용합니다. 잡기/팔 구속은 오른손이 상대 팔을 감싸는 별도 보정을 적용하고, 시전 등 나머지 동작의 손 모션은 유지합니다.
- Mixamo가 생략한 고정 끝마디 채널에 주먹 회전이 남지 않도록, 다음 Animator 평가 전에 원래 포즈를 복원합니다. 공격 취소와 컴포넌트 비활성화에도 동일하게 복원합니다.
- `BossMecha.prefab`와 `BossModelTest.unity`에 적용했습니다. **Tools > Boss Prototype > Calibrate Mechanical Hand Poses**로 현재 리그에 재계산할 수 있습니다. **Apply Updated Boss Model**로 모델을 교체하면 새 손가락 본에 다시 연결합니다.
- `strength`로 보정 강도를 조절할 수 있습니다. 현재 연결 상태 이름은 `Punch`, `Combat_AlternatingCombo`입니다. 새로운 펀치 상태를 만들면 `BossHandPose.StateWeight`에 연결하세요.
- **Verify Independent Combat Patterns**는 최종 LateUpdate 포즈에서 양손 손끝의 접힘·엄지 위치, 취소 후 끝마디 복원, 다른 공격으로 보정이 남지 않는지도 검사합니다.

### 대기 손·양손 검 그립

- `BossHandPose`가 Animator 평가 뒤 손가락만 보정합니다. 본의 기준 위치와 길이는 유지하며, 다음 Animator 평가 전에 원래 입력 포즈를 복원합니다. `relaxedRotation`, `fistRotation`, `gripRotation`은 서로 독립적인 포즈입니다.
- 손잡이 중심을 손바닥에 맞추고, 손가락 세 마디가 손잡이 둘레를 감싸도록 계산합니다. 왼손은 `BossSwordAttachment.SupportWeight`에 맞춰 그립을 풀고 쥡니다. `BossSwordAttachment`의 과거 고정 손가락 덮어쓰기는 새 손 컴포넌트가 있을 때 사용하지 않습니다.
- 원래 손잡이가 두 손보다 짧아 가드와 폼멜에 걸쳤습니다. `Generated/GreatswordLongGrip.asset`은 검 원본 메시의 손잡이 구간만 2.5배 늘린 테스트용 사본입니다. 칼날/가드 형상과 원본 FBX는 유지하고, 폼멜 위치 및 양손 장착 위치를 조정했습니다. `originalSwordMesh`에 원본 참조를 보관해 재적용 시 길이가 누적되지 않습니다.
- 검 그립 변경 후 검날 궤적 보정도 다시 계산했습니다. 대기 손 펼침과 그립 상태의 손끝-손잡이 거리도 재생 검증에 포함합니다.

- 대기 손은 검지부터 새끼손가락까지 굽힘과 벌어짐을 다르게 적용합니다. 펀치 복귀에서 손을 푸는 구간을 늘리고 엄지가 조금 먼저 풀리도록 했습니다. 재생 검증은 손을 푸는 동안 관절이 다시 접히는 역방향 변화도 검사합니다.


### 잡기 손과 검 대기 발 고정

- `Combat_Grab`, `Combat_ArmLock`은 준비 후반부터 오른손을 감싸는 형태로 오므립니다. 손바닥에 상대 팔이 들어갈 공간을 남기며 왼손은 편안한 상태를 유지합니다. 잡기 유지 중에는 손을 풀지 않고, 상태를 벗어나면 전환 비율에 맞춰 풀립니다.
- `SwordIdle.anim`은 검을 드는 상체 동작을 유지하고 골반과 양다리/발에는 기본 Idle 시작 자세를 적용했습니다. 루프 중 발이 들썩이거나 미끄러지지 않게 하체 곡선을 고정합니다. **Tools > Boss Prototype > Stabilize Sword Idle Feet**로 재적용할 수 있고, 전투 애니메이션 재생성에도 포함됩니다.
- `BossCombatAnimationBuilder.StabilizeAndReview`는 검 대기의 121개 시점에서 발/발끝 위치와 회전을 검사하고 자세 이미지를 출력합니다. 전투 재생 검증에는 잡기 유지 및 해제 시 손 보정 해제를 포함합니다.

### 전체 공격 손·발 점검

- EMP / SensorJam / VisionDisruption은 충전 구간에서 양손을 쥐고 회복 시 편안하게 풀도록 `BossHandPose.CastWeight`로 보정합니다. 원본에 누락된 손끝 채널이 이전 공격의 포즈를 유지하지 않도록 기존 복원 경로를 사용합니다.
- `BossAttackFootPolish`는 SwordSlash / OverheadSmash / AlternatingCombo / Grab / Feint / GroundCast / PowerUp의 다리 회전을 굽습니다. 발이 낮은 접지 구간의 수평 이동을 줄이고, 발 들기 높이·발 방향·상체 동작은 유지합니다. 빠른 발 들기에서 튀지 않도록 시간에 따라 보정량을 완화하고 다리가 닿는 거리 안에서만 보정합니다. 회전축 이동과 긴 스텝은 일부 남겨 동작을 유지합니다.
- **Tools > Boss Prototype > Polish Attack Foot Contacts**로 적용합니다. 애니메이션 재생성에도 포함되며, 클립 파일 해시를 importer userData에 기록하여 동일 클립에 보정이 누적되지 않게 합니다. 모델이나 원본 동작을 바꾸면 원본에서 전투 클립을 재생성하세요.
- `BossAttackPoseAudit.Run`은 14개 공격의 연결과 공유 클립, 121개 시점의 발 높이/접지 이동, 준비·타격·복귀 이미지를 `Library/BossAttackPoseAudit`에 출력합니다. 접지 이동 수치는 의도된 회전·디딤도 포함하며 0이 목표는 아닙니다. 보정 시 원본 발 회전과 결과를 비교해 방향 변경을 검사합니다.
- 미사일/레이저는 기존 Idle 기반 자세이며, 별도 전신 공격 클립이 추가된 것은 아닙니다. 헤비펀치와 기본 Idle의 안정된 발 동작은 그대로 사용합니다.
- `BossFootPoseInput`은 Animator 평가 전에 누락된 발 회전 입력을 복원합니다. 공격 중간에 취소해도 보정된 발 회전이 Idle/이동에 남지 않으며, 재생 검증에 중도 취소 후 발 회전 복원 검사를 포함합니다.

### 검 꺼내기·넣기 — Mixamo 원본

- 기존 수제 팔 궤적·몸통 회전 생성 및 발도 역재생 납도는 제거했습니다. Mixamo의 `Draw A Great Sword 1/2`, `Sheath A Great Sword 1/2`를 v026 캐릭터로 직접 다운로드했습니다 (Without Skin, 60fps, Keyframe Reduction: none). 원본은 `Animation/CombatSource/DrawGreatSword1/2.fbx`, `SheathGreatSword1/2.fbx`입니다.
- `BossSwordEquipBuilder`는 원본 전신 움직임을 Generic 리그에 매핑합니다. 기본 속도 85%, 두 원본 사이 0.12초 연결 구간, 대기 자세 전후 블렌드를 사용합니다. 등 장착 위치를 원본 발도 접촉 포즈에 맞추고, 납도 접촉 부근만 짧은 팔 IK로 두 촬영 동작의 접촉 위치 차이를 보정합니다. 손가락 그립 보정은 기존 `BossHandPose`를 사용합니다.
- `BossSwordEquip.TryDraw()` / `TrySheathe()`는 독립 실행 함수입니다. `IsBusy` 동안 이동·회전·다른 공격 시작을 잠급니다. 접촉 시점 `drawContact` / `sheatheContact`는 소스 길이로 계산하며 검의 부모만 그 순간 전환합니다. 원본 모션의 체중 이동과 발 움직임은 유지합니다.
- 테스트 패널 **Draw sword / Sheathe sword**로 따로 재생합니다. 검 공격 전 자동으로 꺼내고 맨손 공격 전 자동으로 넣습니다. `TryAttack()`의 true는 준비까지 포함한 요청 접수이며, 준비가 끝나면 거리·방향·대상을 다시 검사합니다. 같은 자리에서 검 공격을 반복하면 꺼낸 상태를 유지합니다.
- 중간 취소는 현재 장착 상태를 유지하고 해당 Idle로 복귀합니다. Reset/사망은 즉시 복구 경로를 사용합니다. 일반 호출에는 낮은 수준의 장착 함수 대신 `BossSwordEquip` API를 사용하세요.
- **Tools > Boss Prototype > Build Mixamo Sword Draw and Sheathe**로 재생성합니다. 원본 소스, 등 장착 위치, 두 클립 이음새를 함께 재계산합니다. 전투 애니메이션 전체 재생성에도 포함됩니다.
- **Record Sword Equip Verification**는 실제 Play 모드를 `Library/BossSwordEquipVideo`에 60fps PNG로 기록합니다. `frame-count.txt`가 이번 녹화의 유효 프레임 수입니다. 검증은 접촉 전후 장착, 그립 해제, 중도 취소, 자동 무기 전환 및 기존 14개 공격을 검사합니다.

- 검 소유권 전환은 현재 월드 위치·회전을 보존하고 새 소켓에서 접촉 오차만 0.14초 동안 완화합니다. DrawSword→SwordIdle 전환에서도 손목 가드 각도를 유지해 순간적으로 검이 되돌아가는 현상을 방지합니다. 장착 진행률은 그립 보정 전에 갱신합니다.
