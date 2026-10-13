"""Run with Blender --background --python <this file>; then use Unity's
Tools > Boss Prototype > Apply Updated Boss Model. Does not edit art sources.
"""
from pathlib import Path
import bpy
from mathutils import Matrix

assets = Path(__file__).resolve().parents[2]
old = assets / '_BossPrototype/Animation/Idle.fbx'
source = assets / '_Project/02_Art/01_Models/mecha-finger-rig-mixamo-v026dsadsfadf.fbx'
output = assets / '_BossPrototype/Models/BossUpdatedCompatible.fbx'
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(old))
reference = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
rest = {b.name: b.matrix_local.copy() for b in reference.data.bones}
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(source))
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
missing = set(rest) - set(arm.data.bones.keys())
if missing:
    raise RuntimeError('Missing animation bones: ' + str(missing))
for obj in bpy.context.selected_objects:
    obj.select_set(False)
arm.select_set(True)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode='EDIT')
for name, matrix in rest.items():
    arm.data.edit_bones[name].matrix = matrix
bpy.ops.object.mode_set(mode='OBJECT')
for bone in arm.pose.bones:
    bone.matrix_basis = Matrix.Identity(4)
for obj in bpy.data.objects:
    if obj.animation_data:
        obj.animation_data_clear()
output.parent.mkdir(parents=True, exist_ok=True)
bpy.ops.export_scene.fbx(filepath=str(output), apply_scale_options='FBX_SCALE_ALL',
    use_selection=False, object_types={'ARMATURE', 'MESH'}, add_leaf_bones=False,
    bake_anim=False, axis_forward='-Z', axis_up='Y', use_armature_deform_only=False)
