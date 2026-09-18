# Pilot model and rig review

Current version: **PilotV04** (wrist alignment and forearm twist distribution).

Open the repository's `AnseongSteel` project in Unity 6000.3.24f1, then open:
`Assets/_Project/02_Art/PilotV04/Scenes/PilotWristReview_v04.unity`.

Press Play and focus the Game view. The review controls support hand targets,
elbow hints, finger curl and discrete inspection poses. This is a fixed-stance
model/rig review, without networking or new animation clips.

- `PilotV04`: current Blender source, generator, Korean instructions and export checks.
- `PilotV03`: previous rig review source, retained for comparison.
- `PilotV01`: initial source, retained for history.
- Unity materials, prefabs, scripts, scenes and validation reports are stored in
  the matching `Assets/_Project/02_Art/PilotVxx` directories.

V04 passed Humanoid/weight checks, left/right wrist rotation checks, angular-limit
checks and Play Mode keyboard movement/grip/pose checks. Actual Quest controller
calibration, interaction with cockpit controls and device performance remain untested.

The generated `.unitypackage`, ZIP deliveries and Unity Library are intentionally
excluded from Git. The editable assets and source needed to reproduce them are included.
