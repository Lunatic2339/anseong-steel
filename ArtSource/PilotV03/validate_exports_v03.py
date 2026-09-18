import bpy,json,math,runpy
from pathlib import Path
from mathutils import Quaternion
O=Path(__file__).resolve().parent
bpy.ops.wm.open_mainfile(filepath=str(O/'AS_Pilot_v03.blend'))
scene=bpy.context.scene;rig=bpy.data.objects['AS_PilotRig'];scene.frame_set(31);bpy.context.view_layer.update()
runpy.run_path(str(O/'pilot_rig_tools.py'),run_name='__main__')
def hand_matrices():return {s:rig.pose.bones[s+'Hand'].matrix.copy() for s in ['Left','Right']}
before=hand_matrices();bpy.ops.as_pilot.arm_fk();after_fk=hand_matrices();bpy.ops.as_pilot.arm_ik();after_ik=hand_matrices()
switch={s:{'fk_position_error_m':(before[s].translation-after_fk[s].translation).length,'ik_position_error_m':(before[s].translation-after_ik[s].translation).length,'ik_rotation_error_deg':math.degrees(before[s].to_quaternion().rotation_difference(after_ik[s].to_quaternion()).angle)} for s in before}
assert all(x['fk_position_error_m']<.002 and x['ik_position_error_m']<.002 and x['ik_rotation_error_deg']<.2 for x in switch.values()),switch
scene.frame_set(1);bpy.context.view_layer.update()
def points(o):
    eo=o.evaluated_get(bpy.context.evaluated_depsgraph_get());m=eo.to_mesh();vv=[tuple(o.matrix_world@v.co) for v in m.vertices];eo.to_mesh_clear();return vv
def bounds(v):return [[min(p[i] for p in v) for i in range(3)],[max(p[i] for p in v) for i in range(3)]]
source={n:bounds(points(bpy.data.objects[n])) for n in ['Pilot_Body','Pilot_Head']}
report={'ui_ik_fk_switch':switch,'exports':{}}
for filename in ['AS_Pilot_v03.fbx','AS_Pilot_v03_QuestCandidate.fbx']:
    bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.fbx(filepath=str(O/filename))
    arm=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE');meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
    row={'bone_count':len(arm.data.bones),'skinned_meshes':len(meshes),'meshes':{}}
    for o in meshes:
        o.data.calc_loop_triangles();bb=bounds(points(o));base=o.name.replace('_Quest','');err=max(abs(bb[j][i]-source[base][j][i]) for j in range(2) for i in range(3))
        weights=[[g.weight for g in v.groups if g.weight>1e-6] for v in o.data.vertices]
        row['meshes'][o.name]={'vertices':len(o.data.vertices),'triangles':len(o.data.loop_triangles),'unweighted_vertices':sum(not w for w in weights),'bad_weight_sums':sum(abs(sum(w)-1)>.0001 for w in weights),'maximum_weights':max(map(len,weights)),'bind_bounds_difference_m':err,'armature_modifiers':sum(m.type=='ARMATURE' for m in o.modifiers)}
    body=next(o for o in meshes if 'Body' in o.name);before=points(body)
    bone=arm.pose.bones['LeftLowerArm'];bone.rotation_mode='QUATERNION';bone.rotation_quaternion=Quaternion((0,0,1),.8);bpy.context.view_layer.update();after=points(body)
    row['max_arm_deformation_m']=max(math.dist(a,b) for a,b in zip(before,after))
    row['pass']=row['bone_count']==58 and row['skinned_meshes']==2 and row['max_arm_deformation_m']>.05 and all(m['unweighted_vertices']==0 and m['bad_weight_sums']==0 and m['maximum_weights']<=4 and m['armature_modifiers']==1 and m['bind_bounds_difference_m']<(.015 if 'Quest' in filename else .0001) for m in row['meshes'].values())
    report['exports'][filename]=row
report['pass']=all(r['pass'] for r in report['exports'].values())
(O/'export_validation.json').write_text(json.dumps(report,indent=2))
print('EXPORT_AND_UI_TEST',json.dumps(report))
