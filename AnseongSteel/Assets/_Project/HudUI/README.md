# V02 helmet HUD prototype

Open **Anseong Steel > HUD > Open V02 HUD Test** and Play. This creates/opens
`Assets/_Project/HudUI/Scenes/Hud_V02_Test.unity`, a copy of the authored
`ArmMotionTest` scene. The original scene and punch/contact behavior are preserved.
The C cockpit eye renders informed framing only; the runnable baseline is V02.

## Controls and states

- ESC: open/close the suit settings menu. Resume also closes it.
- Tab: existing pilot switch. C: existing tracked-pose calibration.
- Settings sliders: audio, guide opacity, wireframe size.
- Tutorial guide: on/off. Reset Settings restores this session's defaults.
- Damage Demo / Reset: persistent yellow left arm and red right leg, or clear them.
  **No live incoming-damage source exists in this test scene.** Battle state starts
  healthy/blue. The small TEST label identifies this prototype.

The menu replaces battle status and hides the guide while open. It does not pause
the shared simulation or change incoming controller motion. Menu interaction is
verified with desktop pointer/events; XR pointer/ray navigation and physical HMD
comfort/legibility still require device validation.

## Coordinates and integration

`SuitHudController.Attach(input, feed)` installs one helmet world-space canvas
under `input.cockpitCamera`. Layer 31 is reserved for this HUD and excluded from
all three robot panorama cameras. The HUD has no filled battle panel.

`BattlefieldGuide` uses layer 29 and a transparent world-space LineRenderer in
the robot battlefield. Only feed cameras render it into the existing panorama
textures; the cockpit HMD excludes it directly. A head turn or pilot switch
does not regenerate its points from headset coordinates. Both pilots see the
same robot path through the existing physical screen/UV mapping.

Production attack coaching supplies `hud.SupplyWorldGuide(worldPoints)` from
the robot/attack system, not controller or helmet positions. All coordinates
are Unity robot-world coordinates (the existing test arena is offset in Z).
`UseTutorialDemo(hook)` restores the optional illustrative suggestion to the
left reach-contact target, shared by both pilots. This is a demo guide, not a
combat target resolver or a requirement to trace a path exactly.

`ArmMotionInput.PunchCompleted` is observed once by the HUD to choose the demo
jab/hook shape. No changes are made to punch recognition, contact or poses, and
outgoing punches never damage the friendly wireframe.

Incoming condition service should call `hud.Damage.AddDamage(part, normalizedAmount)`
or `SetDamage(part, normalizedSnapshot)`. Values are normalized accumulated damage
(0 healthy, 1 destroyed). Blue < .30, yellow .30-.69, red >= .70. Repairs/reset
are explicit; time and menu changes never heal damage. Network replication is
the condition service's responsibility, not fabricated by this local prototype.

## Validation

`AnseongSteel.Hud.Editor.HudValidation.Run` compiles in the existing Unity project,
creates only the dedicated HUD test scene, exercises menu pointer/slider events,
open/close/reset, lifecycle subscription, damage accumulation/saturation/invalid
inputs, and unchanged guide feed UV/vertices after head rotation and pilot switch.
It renders actual URP cockpit cameras to `HUD_REVIEW_OUTPUT`, with `hud-checks.txt`.
No AI images are used as evidence of runtime behavior.

The original debug panels default to enabled. Only this attached HUD suppresses
them and the diagnostic text/contact pads/direction arrows during the prototype;
cleanup restores prior settings and camera masks. The module is automatically
attached only in the explicitly named `Hud_V02_Test` scene.
