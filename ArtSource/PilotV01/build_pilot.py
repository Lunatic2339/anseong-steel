"""Anseong Steel original pilot v01. Blender 5.2 background generator.
Run: blender --background --factory-startup --python build_pilot.py
No purchased/downloaded meshes or texture assets. Dimensions in metres.
"""
import bpy, math, json, os
from mathutils import Vector, Quaternion
from pathlib import Path
O=Path(__file__).resolve().parent
P=O/'previews'; P.mkdir(exist_ok=True)
bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)
bpy.context.preferences.filepaths.save_version=0
for d in list(bpy.data.materials): bpy.data.materials.remove(d)
def mat(name,c,metal,rough):
    m=bpy.data.materials.new(name); m.diffuse_color=(*c,1); m.use_nodes=True
    s=m.node_tree.nodes.get('Principled BSDF'); s.inputs['Base Color'].default_value=(*c,1)
    s.inputs['Metallic'].default_value=metal; s.inputs['Roughness'].default_value=rough
    return m
M=[mat('AS_CeramicWhite',(0.77,.82,.81),.18,.32),mat('AS_FlexGraphite',(.025,.038,.043),.05,.61),mat('AS_AmberVisor',(.8,.29,.028),.78,.22),mat('AS_TeamAccent',(.025,.30,.33),.35,.32)]
parts=[]; bones={}
def weight(o,w):
    if isinstance(w,str): w={w:1}
    for n,val in w.items(): o.vertex_groups.new(name=n).add(list(range(len(o.data.vertices))),val,'REPLACE')
def finish(o,n,m,w,head=False):
    o.name=n; o.data.materials.clear(); o.data.materials.append(M[m]); weight(o,w)
    for f in o.data.polygons:f.use_smooth=True
    o['head_part']=head; parts.append(o); return o
def ell(n,c,s,m,w,head=False,seg=20,rings=12):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=seg,ring_count=rings,location=c)
    o=bpy.context.object; o.scale=s; bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    return finish(o,n,m,w,head)
def mesh(n,v,f,m,w,head=False):
    d=bpy.data.meshes.new(n);d.from_pydata(v,[],f);d.update();o=bpy.data.objects.new(n,d);bpy.context.collection.objects.link(o)
    return finish(o,n,m,w,head)
def loft(n,rows,m,w,N=20):
    # cross sections: centre xyz, x radius, y radius; bevel-like small end rings
    v=[];f=[]
    for x,y,z,rx,ry in rows:
        for j in range(N):
            a=2*math.pi*j/N;v.append((x+rx*math.cos(a),y+ry*math.sin(a),z))
    for k in range(len(rows)-1):
        for j in range(N):f.append((k*N+j,k*N+(j+1)%N,(k+1)*N+(j+1)%N,(k+1)*N+j))
    f += [tuple(reversed(range(N))),tuple((len(rows)-1)*N+j for j in range(N))]
    return mesh(n,v,f,m,w)
def tube(n,a,b,r1,r2,m,w,N=16):
    a,b=Vector(a),Vector(b);axis=(b-a).normalized();u=axis.cross(Vector((0,1,0))).normalized();vv=axis.cross(u)
    v=[];f=[]
    for t,r in [(0,r1*.88),(.04,r1),(.90,r2),(1,r2*.87)]:
        c=a.lerp(b,t)
        for j in range(N):
            ang=2*math.pi*j/N;p=c+r*(math.cos(ang)*u+math.sin(ang)*vv);v.append(p)
    for k in range(3):
        for j in range(N): f.append((k*N+j,k*N+(j+1)%N,(k+1)*N+(j+1)%N,(k+1)*N+j))
    f.extend([tuple(reversed(range(N))),tuple(3*N+j for j in range(N))]);return mesh(n,v,f,m,w)
def plate(n,outline,y,bulge,m,w):
    # crowned polygonal ceramic shell, gently rolled edge, thickness; front faces -Y
    cx=sum(p[0] for p in outline)/len(outline);cz=sum(p[1] for p in outline)/len(outline)
    v=[];f=[];N=len(outline)
    for scale,dy in [(1,0),(.94,-.008),(.67,-bulge),(.15,-bulge*1.13)]:
        for x,z in outline:v.append((cx+(x-cx)*scale,y+dy,cz+(z-cz)*scale))
    for k in range(3):
        for j in range(N):f.append((k*N+j,k*N+(j+1)%N,(k+1)*N+(j+1)%N,(k+1)*N+j))
    f.append(tuple(3*N+j for j in range(N)));f.append(tuple(reversed(range(N))))
    return mesh(n,v,f,m,w)
def disk(n,c,r,depth,m,w,axis='Y',head=False):
    bpy.ops.mesh.primitive_cylinder_add(vertices=16,radius=r,depth=depth,location=c)
    o=bpy.context.object;o.rotation_euler=(math.pi/2,0,0) if axis=='Y' else (0,math.pi/2,0)
    bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
    return finish(o,n,m,w,head)
def addbone(n,a,b,parent=None):bones[n]=(a,b,parent)
addbone('Hips',(0,0,.96),(0,0,1.10));addbone('Spine',(0,0,1.10),(0,0,1.24),'Hips')
addbone('Chest',(0,0,1.24),(0,0,1.39),'Spine');addbone('UpperChest',(0,0,1.39),(0,0,1.48),'Chest')
addbone('Neck',(0,0,1.48),(0,0,1.57),'UpperChest');addbone('Head',(0,0,1.57),(0,0,1.79),'Neck')
for side,s in [('Left',1),('Right',-1)]:
    def p(x,y,z):return(s*x,y,z)
    addbone(side+'Shoulder',p(.025,0,1.445),p(.225,0,1.445),'UpperChest')
    addbone(side+'UpperArm',p(.225,0,1.445),p(.475,0,1.445),side+'Shoulder')
    addbone(side+'LowerArm',p(.475,0,1.445),p(.705,0,1.445),side+'UpperArm')
    addbone(side+'Hand',p(.705,0,1.445),p(.79,0,1.445),side+'LowerArm')
    addbone(side+'UpperLeg',p(.103,0,1.00),p(.105,-.012,.555),'Hips')
    addbone(side+'LowerLeg',p(.105,-.012,.555),p(.105,0,.135),side+'UpperLeg')
    addbone(side+'Foot',p(.105,0,.135),p(.105,-.125,.067),side+'LowerLeg')
    addbone(side+'Toes',p(.105,-.125,.067),p(.105,-.215,.06),side+'Foot')
    for idx,(finger,yy,length) in enumerate([('Index',-.032,.096),('Middle',-.010,.106),('Ring',.013,.097),('Little',.034,.079)]):
        a=Vector(p(.786,yy,1.442));par=side+'Hand'
        for k,segname in enumerate(['Proximal','Intermediate','Distal']):
            b=a+Vector((s*length*[.45,.32,.23][k],0,-.002))
            bn=side+finger+segname;addbone(bn,tuple(a),tuple(b),par)
            tube(bn+'_glove',a,b,.0108-k*.0015,.010-k*.0015,1,bn,N=8)
            ell(bn+'_joint',a,(.009,.010,.010),1,bn,seg=8,rings=6)
            if k==0:ell(bn+'_knuckle',a.lerp(b,.38)+Vector((0,0,.009)),(.013,.009,.0055),0,bn,seg=8,rings=6)
            a=b;par=bn
    a=Vector(p(.743,-.033,1.437));par=side+'Hand'
    for k,segname in enumerate(['Proximal','Intermediate','Distal']):
        b=a+Vector((s*.025,-.022 if k==0 else -.011,-.007));bn=side+'Thumb'+segname
        addbone(bn,tuple(a),tuple(b),par);tube(bn+'_glove',a,b,.014-k*.002,.013-k*.002,1,bn,N=8);a=b;par=bn
    # anatomically tapered suit limbs; elbows and knees kept exposed
    tube(side+'_biceps',p(.222,0,1.445),p(.468,0,1.445),.066,.052,1,side+'UpperArm')
    ell(side+'_deltoid',p(.242,0,1.445),(.078,.076,.082),1,side+'UpperArm')
    ell(side+'_elbow_seal',p(.475,0,1.445),(.053,.052,.053),1,{side+'UpperArm':.45,side+'LowerArm':.55})
    tube(side+'_forearm',p(.485,0,1.445),p(.71,0,1.445),.052,.034,1,side+'LowerArm')
    ell(side+'_palm',p(.754,0,1.442),(.059,.045,.024),1,side+'Hand',seg=16,rings=8)
    ell(side+'_hand_plate',p(.749,0,1.462),(.038,.031,.009),0,side+'Hand',seg=16,rings=8)
    tube(side+'_wrist_cuff',p(.683,0,1.445),p(.715,0,1.445),.04,.039,3,side+'LowerArm')
    # radial upper limb armour shells with rounded sculpted crown
    ell(side+'_shoulder_cap',p(.262,.001,1.482),(.105,.085,.068),0,side+'UpperArm')
    plate(side+'_bicep_front',[(s*x,z) for x,z in [(.305,1.488),(.391,1.483),(.439,1.454),(.418,1.399),(.323,1.397),(.296,1.426)]],-.048,.018,0,side+'UpperArm')
    plate(side+'_gauntlet',[(s*x,z) for x,z in [(.516,1.488),(.564,1.503),(.661,1.479),(.682,1.443),(.660,1.410),(.535,1.393),(.506,1.42)]],-.039,.020,0,side+'LowerArm')
    plate(side+'_gauntlet_marker',[(s*x,z) for x,z in [(.61,1.469),(.65,1.461),(.65,1.45),(.61,1.457)]],-.062,.002,3,side+'LowerArm')
    disk(side+'_elbow_hinge',p(.476,-.053,1.445),.025,.011,0,side+'LowerArm')
    disk(side+'_elbow_pin',p(.476,-.061,1.445),.010,.005,3,side+'LowerArm')
    # legs: separate rigid thigh/shin, continuous flexible inner volume
    loft(side+'_thigh_suit',[(s*.103,0,1.015,.082,.089),(s*.105,0,.94,.089,.093),(s*.107,0,.80,.078,.079),(s*.105,-.010,.64,.057,.059),(s*.105,-.012,.556,.052,.054)],1,side+'UpperLeg')
    ell(side+'_knee_seal',p(.105,-.012,.555),(.055,.057,.058),1,{side+'UpperLeg':.45,side+'LowerLeg':.55})
    loft(side+'_calf_suit',[(s*.105,-.008,.554,.051,.050),(s*.108,.007,.46,.062,.069),(s*.107,.013,.36,.058,.060),(s*.105,0,.18,.036,.039),(s*.105,0,.12,.038,.038)],1,side+'LowerLeg')
    plate(side+'_thigh_plate',[(s*x,z) for x,z in [(.047,.953),(.112,.984),(.174,.94),(.174,.815),(.157,.68),(.113,.632),(.062,.68),(.034,.838)]],-.072,.025,0,side+'UpperLeg')
    plate(side+'_thigh_inset',[(s*x,z) for x,z in [(.067,.898),(.088,.903),(.080,.716),(.065,.733)]],-.102,.002,3,side+'UpperLeg')
    plate(side+'_patella',[(s*x,z) for x,z in [(.067,.603),(.14,.603),(.162,.57),(.142,.51),(.107,.495),(.066,.523),(.049,.571)]],-.059,.025,0,side+'LowerLeg')
    plate(side+'_shin_plate',[(s*x,z) for x,z in [(.062,.479),(.109,.505),(.158,.467),(.158,.394),(.142,.186),(.108,.15),(.069,.183),(.049,.385)]],-.041,.035,0,side+'LowerLeg')
    # rear calf shrouds; positive Y via mirrored front shell
    o=plate(side+'_calf_rear',[(s*x,z) for x,z in [(.065,.46),(.142,.46),(.16,.395),(.136,.226),(.078,.225),(.05,.385)]],-.06,.018,0,side+'LowerLeg');o.scale.y=-1
    loft(side+'_boot',[(s*.105,-.059,.028,.066,.143),(s*.105,-.062,.045,.069,.145),(s*.105,-.060,.081,.066,.14),(s*.105,-.04,.111,.058,.112),(s*.105,0,.179,.040,.046)],1,side+'Foot')
    loft(side+'_boot_shell',[(s*.105,-.061,.058,.072,.150),(s*.105,-.061,.085,.071,.149),(s*.105,-.040,.123,.062,.118),(s*.105,0,.180,.045,.052)],0,side+'Foot')
    o=plate(side+'_thigh_rear',[(s*x,z) for x,z in [(.059,.925),(.143,.93),(.171,.87),(.148,.729),(.113,.69),(.064,.746),(.042,.851)]],-.091,.012,0,side+'UpperLeg');o.scale.y=-1
    disk(side+'_hip_joint',p(.182,0,.96),.059,.022,1,'Hips','X');disk(side+'_hip_cap',p(.196,0,.96),.044,.010,0,'Hips','X')
    disk(side+'_ankle_pin',p(.150,0,.145),.025,.012,3,side+'Foot','X')
    # small actual mounting sockets, adjustable assumptions, not exoskeleton geometry
    disk(side+'_arm_socket',p(.55,.053,1.445),.024,.017,1,side+'LowerArm')
    disk(side+'_arm_socket_core',p(.55,.064,1.445),.014,.008,3,side+'LowerArm')

# torso soft volume with smooth longitudinal weighting
torso=loft('FlexTorso',[(0,0,.995,.139,.083),(0,0,1.05,.151,.092),(0,0,1.115,.145,.088),(0,0,1.19,.149,.089),(0,0,1.26,.180,.108),(0,0,1.34,.205,.116),(0,0,1.405,.211,.109),(0,0,1.452,.167,.078),(0,0,1.482,.068,.058)],1,'Spine',N=24)
torso.vertex_groups.clear()
for bn in ['Hips','Spine','Chest','UpperChest']:torso.vertex_groups.new(name=bn)
levels=[(.995,'Hips'),(1.16,'Spine'),(1.32,'Chest'),(1.45,'UpperChest')]
for v in torso.data.vertices:
    z=v.co.z
    if z<=levels[0][0]: ww={levels[0][1]:1}
    elif z>=levels[-1][0]:ww={levels[-1][1]:1}
    else:
        for (za,na),(zb,nb) in zip(levels,levels[1:]):
            if za<=z<=zb:t=(z-za)/(zb-za);ww={na:1-t,nb:t};break
    for bn,val in ww.items():torso.vertex_groups[bn].add([v.index],val,'REPLACE')
loft('Pelvis_flex',[(0,0,.87,.083,.065),(0,0,.94,.152,.090),(0,0,1.025,.167,.10),(0,0,1.07,.145,.09)],1,'Hips')
loft('Waist_gasket',[(0,0,1.034,.161,.1),(0,0,1.048,.162,.101),(0,0,1.079,.151,.096)],1,'Hips')
for s in [-1,1]:
    plate('Chest_wing',[(s*x,z) for x,z in [(.016,1.427),(.082,1.459),(.164,1.436),(.215,1.398),(.206,1.321),(.145,1.284),(.068,1.312),(.016,1.35)]],-.103,.028,0,'Chest')
    plate('Rib_guard',[(s*x,z) for x,z in [(.112,1.263),(.178,1.286),(.183,1.228),(.149,1.179),(.135,1.116),(.105,1.13)]],-.071,.024,0,'Spine')
    plate('Pelvis_wing',[(s*x,z) for x,z in [(.032,1.073),(.132,1.099),(.17,1.044),(.153,.969),(.098,.938),(.055,.985)]],-.072,.027,0,'Hips')
    back=plate('Scapula_guard',[(s*x,z) for x,z in [(.028,1.434),(.162,1.429),(.207,1.388),(.195,1.313),(.09,1.285),(.045,1.325)]],-.101,.02,0,'Chest');back.scale.y=-1
    for z in [1.334,1.39]:disk('Chest_fastener',(s*.176,-.135,z),.006,.005,1,'Chest')
plate('Sternum_key',[(-.031,1.409),(.031,1.409),(.035,1.35),(0,1.322),(-.035,1.35)],-.137,.005,3,'Chest')
plate('Pelvis_keel',[(-.043,1.045),(.043,1.045),(.066,.963),(.027,.904),(-.027,.904),(-.066,.963)],-.094,.014,0,'Hips')
for i in range(4):
    z=1.105+i*.039
    plate('Abdominal_bellows',[(-.082,z+.025),(.082,z+.025),(.083,z+.006),(0,z-.004),(-.083,z+.006)],-.092,.009,1,'Spine')
for z in [1.13,1.20,1.27,1.34]:
    o=plate('Spinal_segment',[(-.031,z+.032),(.031,z+.032),(.038,z),(.024,z-.023),(-.024,z-.023),(-.038,z)],-.096,.017,0,'Spine' if z<1.27 else 'Chest');o.scale.y=-1
disk('Back_mount',(0,.132,1.335),.038,.02,1,'Chest');disk('Back_mount_core',(0,.146,1.335),.023,.009,3,'Chest')
loft('Neck_pressure_seal',[(0,0,1.45,.063,.059),(0,0,1.51,.060,.056),(0,0,1.573,.062,.057)],1,'Neck')
loft('Collar',[(0,0,1.469,.079,.074),(0,0,1.485,.085,.079),(0,0,1.51,.075,.071)],0,'UpperChest')

# Rounded closed pressure helmet. Visor is a conforming spherical patch, no transparent shader.
ell('Helmet_shell',(0,0,1.681),(.123,.116,.151),0,'Head',True,seg=32,rings=20)
def visor(n,rx,ry,rz,amin,amax,bmin,bmax,m):
    v=[];f=[];nx=24;nz=12
    for j in range(nz+1):
        b=bmin+(bmax-bmin)*j/nz
        for i in range(nx+1):
            a=amin+(amax-amin)*i/nx
            v.append((rx*math.sin(a)*math.cos(b),-ry*math.cos(a)*math.cos(b),1.681+rz*math.sin(b)))
    for j in range(nz):
        for i in range(nx):k=j*(nx+1)+i;f.append((k,k+1,k+nx+2,k+nx+1))
    return mesh(n,v,f,m,'Head',True)
visor('Visor_gasket',.125,.123,.152,-1.18,1.18,-.63,.91,1)
visor('Amber_pressure_visor',.126,.126,.153,-1.10,1.10,-.54,.85,2)
for s in [-1,1]:
    disk('Temple_gasket',(s*.121,0,1.688),.057,.018,1,'Head','X',True)
    disk('Temple_shell',(s*.132,0,1.688),.045,.012,0,'Head','X',True)
    disk('Temple_insert',(s*.14,0,1.688),.027,.008,3,'Head','X',True)
    disk('Temple_lock',(s*.145,0,1.688),.013,.009,2,'Head','X',True)
    ell('Jaw_rail',(s*.071,-.055,1.57),(.046,.061,.025),0,'Head',True,seg=16,rings=8)
ell('Chin_bridge',(0,-.084,1.559),(.064,.03,.025),0,'Head',True,seg=20,rings=8)

# Merge into exactly two skinned renderers while retaining vertex-group rigidity.
def join(obs,name):
    bpy.ops.object.select_all(action='DESELECT')
    for o in obs:o.select_set(True)
    bpy.context.view_layer.objects.active=obs[0];bpy.ops.object.join();o=bpy.context.object;o.name=name
    bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
    # Join duplicates material slots; remove duplicate indices explicitly.
    old=list(o.data.materials);o.data.materials.clear()
    for m in M:o.data.materials.append(m)
    # polygon indices were reset by clear: keep snapshot beforehand in caller fix below
    return o,old
def merged(obs,name):
    bpy.ops.object.select_all(action='DESELECT')
    for o in obs:o.select_set(True)
    bpy.context.view_layer.objects.active=obs[0];bpy.ops.object.join();o=bpy.context.object;o.name=name
    bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
    poly_mats=[o.data.materials[f.material_index] for f in o.data.polygons]
    used=[m for m in M if m in poly_mats];o.data.materials.clear()
    for m in used:o.data.materials.append(m)
    for f,m in zip(o.data.polygons,poly_mats):f.material_index=used.index(m)
    # Recalculate orientation incl mirrored shells, then unify normals.
    bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.mesh.normals_make_consistent(inside=False);bpy.ops.object.mode_set(mode='OBJECT')
    return o
head_parts=[o for o in parts if o['head_part']]
body_parts=[o for o in parts if not o['head_part']]
head=merged(head_parts,'Pilot_Head')
body=merged(body_parts,'Pilot_Body')
bpy.ops.object.select_all(action='DESELECT')
armdata=bpy.data.armatures.new('AS_Humanoid');rig=bpy.data.objects.new('AS_PilotRig',armdata);bpy.context.collection.objects.link(rig)
bpy.context.view_layer.objects.active=rig;rig.select_set(True);bpy.ops.object.mode_set(mode='EDIT')
for n,(a,b,p) in bones.items():
    eb=armdata.edit_bones.new(n);eb.head=a;eb.tail=b
    if p:eb.parent=armdata.edit_bones[p]
bpy.ops.object.mode_set(mode='OBJECT');rig.show_in_front=True
for o in [body,head]:
    o.parent=rig;mod=o.modifiers.new('Humanoid_skin','ARMATURE');mod.object=rig
    # Standard flat-color atlas: one texel cluster per material with independent slots.
    uv=o.data.uv_layers.new(name='UVMap')
    for poly in o.data.polygons:
        index=M.index(o.data.materials[poly.material_index]);u=(index+.5)/4
        for li in poly.loop_indices:uv.data[li].uv=(u,.5)
atlas=bpy.data.images.new('AS_Pilot_Palette',width=16,height=4,alpha=False)
pix=[]
for yy in range(4):
    for xx in range(16):pix.extend((*M[xx//4].diffuse_color[:3],1))
atlas.pixels=pix;atlas.filepath_raw=str(O/'AS_Pilot_Palette.png');atlas.file_format='PNG';atlas.save();atlas.pack()
# Metadata sockets do not add deform bones.
for name,bn,pos in [('Mount_Back','Chest',(0,.155,1.335)),('Mount_LeftArm','LeftLowerArm',(.55,.075,1.445)),('Mount_RightArm','RightLowerArm',(-.55,.075,1.445))]:
    e=bpy.data.objects.new(name,None);bpy.context.collection.objects.link(e);e.parent=rig;e.parent_type='BONE';e.parent_bone=bn
    e.matrix_world.translation=pos;e.empty_display_size=.04
rig['TrackingAssumption']='HMD + left/right controllers; legs inferred/planted, no measured full-body tracking'
rig['DefaultHeightMetres']=1.832;rig['LocalView']='Hide Pilot_Head for local camera only; keep full rig for remote users'

def reset():
    for b in rig.pose.bones:b.rotation_mode='QUATERNION';b.rotation_quaternion=Quaternion();b.location=(0,0,0)
    bpy.context.view_layer.update()
def rotate_world(n,axis,deg):
    b=rig.pose.bones[n];local=b.bone.matrix_local.to_3x3().inverted()@Vector(axis)
    b.rotation_quaternion=Quaternion(local,math.radians(deg))
def fingers(curl):
    for side in ['Left','Right']:
        for fn in ['Index','Middle','Ring','Little']:
            for seg in ['Proximal','Intermediate','Distal']:rotate_world(side+fn+seg,(0,1 if side=='Left' else -1,0),curl)
        for seg in ['Proximal','Intermediate','Distal']:rotate_world(side+'Thumb'+seg,(0,1 if side=='Left' else -1,0),curl*.5)
def pose(name):
    reset()
    if name=='Neutral':
        for side,s in [('Left',1),('Right',-1)]:rotate_world(side+'UpperArm',(0,1,0),s*68);rotate_world(side+'LowerArm',(0,0,1),-s*8)
        fingers(12)
    if name=='Guard':
        for side,s in [('Left',1),('Right',-1)]:
            rotate_world(side+'UpperArm',(0,0,1),-s*66);rotate_world(side+'LowerArm',(0,1,0),-s*105)
        fingers(63)
    if name=='Punch':
        rotate_world('LeftUpperArm',(0,0,1),-88);rotate_world('LeftLowerArm',(0,0,1),-5)
        rotate_world('RightUpperArm',(0,1,0),-50);rotate_world('RightLowerArm',(0,0,1),80);fingers(62)
    if name=='BentArm':
        rotate_world('LeftUpperArm',(0,1,0),52);rotate_world('LeftLowerArm',(0,0,1),-115)
        rotate_world('RightUpperArm',(0,1,0),-40);rotate_world('RightLowerArm',(0,0,1),105);fingers(40)
    if name=='RaisedArm':
        rotate_world('LeftUpperArm',(0,1,0),-70);rotate_world('LeftLowerArm',(0,0,1),-30)
        rotate_world('RightUpperArm',(0,1,0),-64);fingers(35)
    bpy.context.view_layer.update()
for i,name in enumerate(['TPose','Neutral','Guard','Punch','BentArm','RaisedArm']):
    pose(name)
    for b in rig.pose.bones:b.keyframe_insert('rotation_quaternion',frame=1+i*30,group=b.name)
if rig.animation_data and rig.animation_data.action:rig.animation_data.action.name='AS_PoseReview_6_Positions'
for i,n in enumerate(['TPose','Neutral','Guard','Punch','BentArm','RaisedArm']):bpy.context.scene.timeline_markers.new(n,frame=1+i*30)
scene=bpy.context.scene;scene.frame_end=151;scene.frame_set(1);reset()
# Skin/geometry budget measured from actual final meshes.
stats={'height_metres':round(max(v.co.z for v in head.data.vertices),4),'bones':len(bones),'skinned_renderers':2,'materials':len(M),'meshes':{},'pose_frames':dict(zip(['TPose','Neutral','Guard','Punch','BentArm','RaisedArm'],[1,31,61,91,121,151]))}
for o in [body,head]:
    o.data.calc_loop_triangles();s={'vertices':len(o.data.vertices),'triangles':len(o.data.loop_triangles),'material_slots':len(o.data.materials),'unweighted_vertices':0,'max_weights':0,'non_unit_weight_vertices':0}
    for v in o.data.vertices:
        ws=[g.weight for g in v.groups if g.weight>1e-6];s['unweighted_vertices']+=not bool(ws);s['max_weights']=max(s['max_weights'],len(ws));s['non_unit_weight_vertices']+=abs(sum(ws)-1)>1e-4
    stats['meshes'][o.name]=s
stats['total_triangles']=sum(x['triangles'] for x in stats['meshes'].values())
(O/'model_stats.json').write_text(json.dumps(stats,indent=2))
def export():
    bpy.ops.object.select_all(action='DESELECT')
    for o in [body,head,rig]:o.select_set(True)
    bpy.context.view_layer.objects.active=rig
    bpy.ops.export_scene.fbx(filepath=str(O/'AS_Pilot_v01.fbx'),use_selection=True,object_types={'ARMATURE','MESH'},add_leaf_bones=False,bake_anim=False,axis_forward='-Z',axis_up='Y',apply_unit_scale=True,use_mesh_modifiers=True,mesh_smooth_type='FACE',path_mode='AUTO')
export()
# Studio is kept in the editable source, never exported with the avatar.
studio=bpy.data.collections.new('Preview_Studio');scene.collection.children.link(studio)
def stage(o):
    for c in list(o.users_collection):c.objects.unlink(o)
    studio.objects.link(o)
floor=mat('StudioGround',(.022,.037,.048),.1,.58)
bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,.019));o=bpy.context.object;o.name='Studio_ground';o.data.materials.append(floor);stage(o)
for loc,energy,size,col in [((2,-3,4),500,3,(.81,.91,1)),((-2,-1,2.7),350,2,(1,.77,.5)),((0,3,3),700,2,(.48,.8,1))]:
    bpy.ops.object.light_add(type='AREA',location=loc);o=bpy.context.object;o.data.energy=energy;o.data.shape='DISK';o.data.size=size;o.data.color=col;o.rotation_euler=(Vector((0,0,1))-o.location).to_track_quat('-Z','Y').to_euler();stage(o)
bpy.ops.object.camera_add(location=(2,-4,2));cam=bpy.context.object;stage(cam);scene.camera=cam;cam.data.type='ORTHO';cam.data.ortho_scale=2.17
scene.render.engine='CYCLES';scene.cycles.samples=24;scene.cycles.use_denoising=True
scene.render.resolution_x=850;scene.render.resolution_y=1000;scene.render.resolution_percentage=100
scene.world.color=(.18,.18,.18);scene.view_settings.view_transform='AgX'
scene.render.image_settings.file_format='PNG';scene.render.film_transparent=False
def render(name,pos,target=(0,0,.96),scale=2.08):
    cam.location=pos;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=scale
    scene.render.filepath=str(P/(name+'.png'));bpy.ops.render.render(write_still=True)
scene.frame_set(31);pose('Neutral')
cam.location=(2.6,-4,2.15);cam.rotation_euler=(Vector((0,0,.95))-cam.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.wm.save_as_mainfile(filepath=str(O/'AS_Pilot_v01.blend'))
render('01_hero',(2.6,-4,2.05))
render('02_front',(0,-5,1.04));render('03_back',(0,5,1.04));render('04_side',(5,0,1.04))
for name,frame in [('Guard',61),('Punch',91),('BentArm',121),('RaisedArm',151)]:
    scene.frame_set(frame);pose(name);render('05_'+name,(2.5,-4,2.25),scale=2.22)
scene.frame_set(31);pose('Neutral');render('06_helmet',(1.7,-3,1.9),target=(0,0,1.60),scale=.68)
scene.frame_set(1);reset()
# Round-trip in a clean file proves exported FBX contains skinned meshes and hierarchy.
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(O/'AS_Pilot_v01.fbx'))
rr={'armatures':[],'meshes':[]}
for o in bpy.context.scene.objects:
    if o.type=='ARMATURE':rr['armatures'].append({'name':o.name,'bones':len(o.data.bones),'bone_names':list(o.data.bones.keys())})
    if o.type=='MESH':rr['meshes'].append({'name':o.name,'vertices':len(o.data.vertices),'vertex_groups':len(o.vertex_groups),'armature_modifiers':sum(m.type=='ARMATURE' for m in o.modifiers)})
rr['pass']=len(rr['armatures'])==1 and rr['armatures'][0]['bones']==len(bones) and len(rr['meshes'])==2 and all(m['armature_modifiers']==1 and m['vertex_groups']>0 for m in rr['meshes'])
(O/'fbx_roundtrip.json').write_text(json.dumps(rr,indent=2))
print('PILOT_BUILD_COMPLETE',json.dumps(stats),json.dumps(rr))
