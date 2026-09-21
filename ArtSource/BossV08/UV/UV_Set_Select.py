import bpy
SET = '01_TorsoHead'  # 01_TorsoHead, 02_Pelvis, 03_Arm_L, 04_Arm_R, 05_Leg_L, 06_Leg_R, 07_Shield, 08_Weapons
if bpy.context.object and bpy.context.object.mode != 'OBJECT':
    bpy.ops.object.mode_set(mode='OBJECT')
bpy.ops.object.select_all(action='DESELECT')
objects = [o for o in bpy.context.scene.objects if o.type == 'MESH' and o.get('UV_Set') == SET]
if not objects:
    raise ValueError('Unknown UV Set: ' + SET)
for o in objects:
    o.hide_set(False)
    o.select_set(True)
bpy.context.view_layer.objects.active = objects[0]
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='SELECT')
