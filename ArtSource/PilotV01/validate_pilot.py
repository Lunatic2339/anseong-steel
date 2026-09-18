"""Run with Blender in background; validates exported bind bounds and deformation."""
import bpy, json, math
from pathlib import Path
from mathutils import Quaternion
O=Path(__file__).resolve().parent
bpy.ops.wm.open_mainfile(filepath=str(O/'AS_Pilot_v01.blend'))
scene=bpy.context.scene
def evaluated(o):
    dg=bpy.context.evaluated_depsgraph_get();eo=o.evaluated_get(dg);m=eo.to_mesh()
    v=[tuple(o.matrix_world@p.co) for p in m.vertices];eo.to_mesh_clear();return v
def bounds(v):return [[min(p[i] for p in v) for i in range(3)],[max(p[i] for p in v) for i in range(3)]]
source={};report={'poses':{},'bind_roundtrip_max_error_metres':{},'exported_arm_deformation':{}}
for frame,name in [(1,'TPose'),(31,'Neutral'),(61,'Guard'),(91,'Punch'),(121,'BentArm'),(151,'RaisedArm')]:
    scene.frame_set(frame);bpy.context.view_layer.update()
    entry={}
    for n in ['Pilot_Body','Pilot_Head']:
        v=evaluated(bpy.data.objects[n]);entry[n]={'bounds_metres':bounds(v),'nonfinite_coordinates':sum(not math.isfinite(x) for p in v for x in p)}
        assert entry[n]['nonfinite_coordinates']==0
        if frame==1:source[n]=bounds(v)
    report['poses'][name]=entry
bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.fbx(filepath=str(O/'AS_Pilot_v01.fbx'))
rig=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
for n in ['Pilot_Body','Pilot_Head']:
    bb=bounds(evaluated(bpy.data.objects[n]));err=max(abs(bb[j][i]-source[n][j][i]) for j in range(2) for i in range(3))
    report['bind_roundtrip_max_error_metres'][n]=err;assert err<.0001
body=bpy.data.objects['Pilot_Body'];before=evaluated(body)
bone=rig.pose.bones['LeftLowerArm'];bone.rotation_mode='QUATERNION';bone.rotation_quaternion=Quaternion((0,0,1),.8);bpy.context.view_layer.update();after=evaluated(body)
group=body.vertex_groups['LeftLowerArm'].index
selected=[v.index for v in body.data.vertices if any(g.group==group and g.weight>.5 for g in v.groups)]
distances=[math.dist(before[i],after[i]) for i in selected]
report['exported_arm_deformation']={'tested_vertices':len(selected),'max_displacement_metres':max(distances),'mean_displacement_metres':sum(distances)/len(distances)}
assert max(distances)>.05
report['pass']=True
(O/'pose_validation.json').write_text(json.dumps(report,indent=2))
print('POSE_AND_ROUNDTRIP_PASS',report['bind_roundtrip_max_error_metres'],report['exported_arm_deformation'])
