# Mecha boss model test

Unity version: **6000.3.24f1**. Scope: `Assets/_BossPrototype`.

After the scripts and the three supplied FBXs import, the editor builds
`Scenes/BossModelTest.unity` once. Open that scene and press Play. It automatically
shows idle, turning, walking to the dummy, and a right-hand punch. The top-left
buttons restart/reset the demonstration or exercise each action. A successful
hit flashes the dummy orange and increments `Target hits`.

If automatic creation did not run, use **Tools > Boss Prototype > Create Model
Test Scene**. Existing generated scenes are never overwritten by this command.
The builder creates the controller, boss prefab and URP preview materials in
`Generated`. Original capsule test scenes are preserved.

## Animation choices

- The supplied `Animation/Idle.fbx`, `Walk.fbx`, and `Punch.fbx` all contain the
  same v026 mesh and 45-bone hierarchy. The scene uses the model in Idle.fbx.
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


