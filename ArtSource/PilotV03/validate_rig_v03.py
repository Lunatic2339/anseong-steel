import bpy,json,math
from pathlib import Path
from mathutils import Vector,Quaternion
O=Path(__file__).resolve().parent
bpy.ops.wm.open_mainfile(filepath=str(O/'AS_Pilot_v03.blend'))
scene=bpy.context.scene;rig=bpy.data.objects['AS_PilotRig'];report={'pose_checks':{},'ik_motion_tests':[]}
for name,frame in [('TPose',1),('Neutral',31),('Guard',61),('Punch',91),('BentArm',121),('RaisedArm',151),('WristTwist',181)]:
    scene.frame_set(frame);bpy.context.view_layer.update();row={}
    for side in ['Left','Right']:
        target=bpy.data.objects['CTRL_'+side+'Hand'];hand=rig.matrix_world@rig.pose.bones[side+'Hand'].head
        row[side+'_target_error_m']=(target.matrix_world.translation-hand).length
        row[side+'_ik_influence']=rig.pose.bones[side+'LowerArm'].constraints['Two_bone_arm_IK'].influence
    report['pose_checks'][name]=row
scene.frame_set(31);bpy.context.view_layer.update()
# Remove only animation evaluation in this test process to permit target perturbations.
rig.animation_data_clear()
for o in bpy.data.objects:
    if o.name.startswith('CTRL_'):o.animation_data_clear()
# Clearing armature animation also removes influence drivers; set IK constraints explicitly.
for side in ['Left','Right']:
    rig.pose.bones[side+'LowerArm'].constraints['Two_bone_arm_IK'].influence=1
    for c in rig.pose.bones[side+'Hand'].constraints:c.influence=1
    target=bpy.data.objects['CTRL_'+side+'Hand'];base=target.location.copy()
    for delta in [(-.02,-.025,.035),(.015,-.04,.06),(0,.015,.025)]:
        target.location=base+Vector(delta);bpy.context.view_layer.update()
        hand=rig.matrix_world@rig.pose.bones[side+'Hand'].head
        err=(target.matrix_world.translation-hand).length
        report['ik_motion_tests'].append({'side':side,'delta':delta,'error_m':err})
    target.location=base
bpy.context.view_layer.update()
head=rig.pose.bones['Head'];ctrl=bpy.data.objects['CTRL_Head'];head.constraints[0].influence=1
ctrl.rotation_quaternion=Quaternion((0,0,1),math.radians(18))@ctrl.rotation_quaternion
bpy.context.view_layer.update()
report['head_control_error_deg']=math.degrees((rig.matrix_world@head.matrix).to_quaternion().rotation_difference(ctrl.matrix_world.to_quaternion()).angle)
for side in ['Left','Right']:
    for c in rig.pose.bones[side+'LowerArm'].constraints:c.influence=0
    for c in rig.pose.bones[side+'Hand'].constraints:c.influence=0
    rig.pose.bones[side+'Hand'].rotation_quaternion=Quaternion((0,1,0),math.radians(72))
bpy.context.view_layer.update()
report['twist_distribution_degrees']={}
for side in ['Left','Right']:
    q=(rig.pose.bones[side+'LowerArm'].matrix.inverted()@rig.pose.bones[side+'ForearmTwist'].matrix).to_quaternion()
    report['twist_distribution_degrees'][side]=math.degrees(q.angle)
report['pass']=all(x['error_m']<.002 for x in report['ik_motion_tests']) and all(v[s+'_target_error_m']<.002 for n,v in report['pose_checks'].items() if n!='TPose' for s in ['Left','Right']) and report['head_control_error_deg']<.05 and all(abs(v-36)<.1 for v in report['twist_distribution_degrees'].values())
(O/'rig_validation.json').write_text(json.dumps(report,indent=2))
print('RIG_TEST',json.dumps(report))
