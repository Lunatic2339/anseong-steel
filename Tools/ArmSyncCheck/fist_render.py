import bpy,sys,math,json,random
from mathutils import Vector,Matrix,Quaternion
fbx,out=sys.argv[1],sys.argv[2]; P=json.loads(sys.argv[3])
side=P.get('side','Left')
for o in list(bpy.data.objects): bpy.data.objects.remove(o)
bpy.ops.import_scene.fbx(filepath=fbx)
arm=[o for o in bpy.data.objects if o.type=='ARMATURE'][0]
def B(n): return arm.pose.bones['mixamorig:'+side+('Hand'+n if n!='Hand' else n)]
for m in [o for o in bpy.data.objects if o.type=='MESH']:
    names={g.index:g.name for g in m.vertex_groups}
    keep=any(g.weight>.5 and (side+'Hand' in names[g.group] or side+'ForeArm' in names[g.group]) for v in m.data.vertices[:50] for g in v.groups)
    m.hide_render=not keep
    m.color=(random.random()*.5+.35,)*1+(random.random()*.5+.35,random.random()*.5+.35,1)
def pos(n): return arm.matrix_world@B(n).head
def upd(): bpy.context.view_layer.update()
def rotate_about(n,axis,deg):
    pb=B(n);h=pb.head.copy()
    R=Matrix.Rotation(math.radians(deg),4,axis)
    pb.matrix=Matrix.Translation(h)@R@Matrix.Translation(-h)@pb.matrix; upd()
def aim(n,frm,to):
    if frm.length<1e-6 or to.length<1e-6: return
    q=frm.rotation_difference(to); pb=B(n);h=pb.head.copy()
    pb.matrix=Matrix.Translation(h)@q.to_matrix().to_4x4()@Matrix.Translation(-h)@pb.matrix; upd()
names=['Hand']+[f+str(j) for f in ['Index','Middle','Ring','Pinky','Thumb'] for j in (1,2,3)]
rest={n:list(pos(n)) for n in names}
# ---- algorithm (mirrors the planned Unity code; heads only, no tails) ----
hand=pos('Hand')
fingers=['Index','Middle','Ring','Pinky']
center=sum((pos(f+'1') for f in fingers),Vector())/4
forward=(center-hand).normalized()
across=(pos('Pinky1')-pos('Index1')).normalized()
inward=forward.cross(across).normalized()
curv=pos('Middle3')-pos('Middle1'); curv-=curv.dot(forward)*forward
if curv.dot(inward)<0: inward=-inward
palm_len=(pos('Middle1')-hand).length
ang=P.get('fingers',[70,90,60])
curl=forward.cross(inward).normalized()
# sign: a small rotation of the middle finger direction must move it toward the palm
d=(pos('Middle2')-pos('Middle1')).normalized()
if (Quaternion(curl,math.radians(10))@d).dot(inward)<d.dot(inward): curl=-curl
for f in fingers:
    for j in (1,2,3): rotate_about(f+str(j),curl,ang[j-1])
# thumb: CCD so the extrapolated tip lands over the curled index/middle middle phalanges
target=(pos('Index2')+pos('Middle2'))*.5 + inward*palm_len*P.get('thumbOut',.25) - across*palm_len*P.get('thumbAcross',0) + forward*palm_len*P.get('thumbFwd',0)
L3=(pos('Thumb3')-pos('Thumb2')).length
def tip(): return pos('Thumb3')+(pos('Thumb3')-pos('Thumb2')).normalized()*L3
for it in range(12):
    for n in ('Thumb2','Thumb1'):
        aim(n,tip()-pos(n),target-pos(n))
aim('Thumb3',tip()-pos('Thumb3'),target+(across)*palm_len*.6-pos('Thumb3'))
print('thumb err',(tip()-target).length)
if P.get('dump'):
    after={n:list(pos(n)) for n in names}
    import json as J; J.dump({'rest':rest,'after':after,'upper':list(arm.matrix_world@arm.pose.bones['mixamorig:'+side+'Arm'].head),'fore':list(arm.matrix_world@arm.pose.bones['mixamorig:'+side+'ForeArm'].head)},open(P['dump'],'w')); sys.exit(0)
# ---- render ----
axisv=forward.cross(inward).normalized()
ctr=pos('Middle1')
bpy.ops.object.camera_add(); cam=bpy.context.object; bpy.context.scene.camera=cam
sc=bpy.context.scene; sc.render.engine='BLENDER_WORKBENCH'; sc.render.resolution_x=sc.render.resolution_y=400
sc.display.shading.light='STUDIO'; sc.display.shading.color_type='OBJECT'
for name,d in {'side':-axisv,'palm':inward,'back':-inward,'front':forward,'thumbside':axisv}.items():
    cam.location=ctr+d*0.3; cam.rotation_euler=(ctr-cam.location).to_track_quat('-Z','Y').to_euler()
    sc.render.filepath=f'{out}_{name}.png'; bpy.ops.render.render(write_still=True)
