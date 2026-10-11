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
