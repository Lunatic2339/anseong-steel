"""Anseong Steel original pilot v03. Blender 5.2 background generator.
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
def smooth_rows(rows,steps=3):
    result=[]
    for i in range(len(rows)-1):
        p0=rows[max(0,i-1)];p1=rows[i];p2=rows[i+1];p3=rows[min(len(rows)-1,i+2)]
        for j in range(steps):
            t=j/steps
            result.append(tuple(.5*((2*b)+(-a+c)*t+(2*a-5*b+4*c-d)*t*t+(-a+3*b-3*c+d)*t*t*t) for a,b,c,d in zip(p0,p1,p2,p3)))
    result.append(rows[-1]);return result


parts=[]; bones={}
def weight(o,w):
    if isinstance(w,str): w={w:1}
    for n,val in w.items(): o.vertex_groups.new(name=n).add(list(range(len(o.data.vertices))),val,'REPLACE')
def finish(o,n,m,w,head=False):
    o.name=n; o.data.materials.clear(); o.data.materials.append(M[m]); weight(o,w)
    for f in o.data.polygons:f.use_smooth=True
    o['head_part']=head; parts.append(o); return o
def ell(n,c,s,m,w,head=False,seg=32,rings=16):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=seg,ring_count=rings,location=c)
    o=bpy.context.object; o.scale=s; bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    return finish(o,n,m,w,head)
def mesh(n,v,f,m,w,head=False):
    d=bpy.data.meshes.new(n);d.from_pydata(v,[],f);d.update();o=bpy.data.objects.new(n,d);bpy.context.collection.objects.link(o)
    return finish(o,n,m,w,head)
def loft(n,rows,m,w,N=32):
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
    bpy.ops.mesh.primitive_cylinder_add(vertices=48,radius=r,depth=depth,location=c)
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
            ell(bn+'_joint',a,(.009,.010,.010),1,bn,seg=12,rings=8)
            if k==0:ell(bn+'_knuckle',a.lerp(b,.38)+Vector((0,0,.009)),(.013,.009,.0055),0,bn,seg=12,rings=8)
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

# V02: replace applied flat plaques with tailored compound-curved shells.
remove_keys=['shoulder_cap','bicep_front','gauntlet','thigh_plate','thigh_inset','patella','shin_plate','calf_rear','thigh_rear','Chest_wing','Rib_guard','Pelvis_wing','Scapula_guard','Chest_fastener','Sternum_key','Pelvis_keel','Abdominal_bellows','Spinal_segment','Waist_gasket','Jaw_rail','Chin_bridge']
for o in list(parts):
    if any(k in o.name for k in remove_keys):
        parts.remove(o);bpy.data.objects.remove(o,do_unlink=True)
M[1].diffuse_color=(.008,.012,.016,1)
M[1].node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=(.008,.012,.016,1)
M[1].node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value=.44
M[0].node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value=.27
bpy.context.view_layer.update()
for o in parts:
    n=o.name;inv=o.matrix_world.inverted()
    for v in o.data.vertices:
        p=o.matrix_world@v.co
        if n=='FlexTorso':
            z=p.z
            taper=.84 if z<1.23 else (.84+min(1,(z-1.23)/.14)*.13)
            p.x*=taper
            p.y*=.94
        if n=='Pelvis_flex':p.x*=.94;p.y*=.94
        if any(k in n for k in ['thigh_suit','calf_suit','knee_seal']):
            center=.105 if n.startswith('Left') else -.105
            p.x=center+(p.x-center)*.88;p.y*=.90
        if 'boot' in n:
            center=.105 if n.startswith('Left') else -.105
            p.x=center+(p.x-center)*.85
            p.y=.0+(p.y)*.93
        if 'Temple_' in n:
            p.y*=.81;p.z=1.688+(p.z-1.688)*.81
            p.x*=.96
        if 'hip_joint' in n or 'hip_cap' in n:
            p.x*=.94;p.y*=.84;p.z=.96+(p.z-.96)*.84
        if o['head_part']:
            p.x*=.96;p.y*=1.00;p.z-=.016
        v.co=inv@p
    if 'boot_shell' in n:
        o.data.materials.append(M[1])
        for f in o.data.polygons:
            if sum(o.data.vertices[i].co.y for i in f.vertices)/len(f.vertices)<-.146:f.material_index=1

def shell(n,rows,amin,amax,m,bone,side=1,cx=0,back=False,N=32,hem=None):
    # z/rx/ry stations; sin/cos cross sections wrap continuously around the body.
    v=[];f=[]
    raw_rows=rows;rows=smooth_rows(rows,3)
    for j,(z,rx,ry) in enumerate(rows):
        for i in range(N+1):
            jt=j/3;k=min(len(raw_rows)-1,int(jt));q=jt-k
            lo=(amin(k)*(1-q)+amin(min(k+1,len(raw_rows)-1))*q) if callable(amin) else amin
            a=lo+(amax-lo)*i/N
            zz=z+(hem(a,j/3) if hem else 0)
            v.append((side*(cx+rx*math.sin(a)),(-1 if not back else 1)*ry*math.cos(a),zz))
    for j in range(len(rows)-1):
        for i in range(N):
            k=j*(N+1)+i;f.append((k,k+1,k+N+2,k+N+1))
    o=mesh(n,v,f,m,bone)
    bpy.context.view_layer.objects.active=o
    solid=o.modifiers.new('Ceramic_thickness','SOLIDIFY');solid.thickness=.003;solid.offset=-.3
    bpy.ops.object.modifier_apply(modifier=solid.name)
    return o

# Sculpted chest yoke: broad upper chest, raised central abdominal arch, no floating plates.
chestrows=[(1.285,.174,.113),(1.296,.180,.12),(1.335,.198,.136),(1.38,.207,.141),(1.413,.199,.129),(1.444,.167,.102),(1.465,.115,.074),(1.478,.075,.062)]
def chesthem(a,j):return .072*max(0,math.cos(a))**5*max(0,1-j/3)
shell('Sculpted_pectoral_yoke',chestrows,-1.50,1.50,0,'Chest',N=48,hem=chesthem)
shell('Chest_lower_gasket',[(1.28,.176,.115),(1.289,.179,.12)],-1.5,1.5,1,'Chest',N=48,hem=lambda a,j:.072*max(0,math.cos(a))**5)
shell('Sculpted_back_carapace',[(1.295,.159,.105),(1.316,.184,.116),(1.371,.202,.119),(1.414,.193,.108),(1.445,.157,.081),(1.469,.073,.06)],-1.47,1.47,0,'Chest',back=True,N=40)
for s in [-1,1]:
    shell('Swept_flank_shell',[(1.016,.151,.098),(1.043,.155,.102),(1.10,.135,.10),(1.165,.128,.095),(1.223,.146,.101),(1.288,.177,.113),(1.314,.184,.12)],lambda j:[.15,.25,.44,.58,.63,.56,.52][j],2.03,0,'Spine',side=s,N=22)
    # restrained chest marking and recessed seam hardware
    disk('Chest_flush_lock',(s*.149,-.094,1.305),.008,.006,1,'Chest')
    disk('Chest_flush_core',(s*.149,-.098,1.305),.004,.003,3,'Chest')
    shell('Flank_engraved_seam',[(1.104,.137,.102),(1.109,.137,.103)],.63,1.72,1,'Spine',side=s,N=16)
    shell('Hip_contour',[(.911,.041,.069),(.936,.068,.088),(.977,.116,.104),(1.014,.145,.108),(1.051,.155,.107)],.02,1.64,0,'Hips',side=s,N=24)
    shell('Back_hip_sweep',[(.958,.116,.088),(1.001,.149,.10),(1.04,.154,.099)],.15,1.6,0,'Hips',side=s,back=True,N=18)
for z in [1.113,1.181,1.243]:
    shell('Flex_abdominal_seam',[(z,.107,.087),(z+.008,.108,.088)],-.8,.8,1,'Spine',N=24,hem=lambda a,j:-.012*math.cos(a))
for z in [1.11,1.175,1.24]:
    shell('Dorsal_vertebra',[(z,.034,.095),(z+.038,.03,.099)],-.75,.75,0,'Spine',back=True,N=12)

def arm_shell(n,rows,bn,s,amin=-.6,amax=3.72,N=24):
    rows=smooth_rows(rows,3)
    v=[];f=[]
    for j,(x,ry,rz) in enumerate(rows):
        for i in range(N+1):
            a=amin+(amax-amin)*i/N
            v.append((s*x,-ry*math.cos(a),1.445+rz*math.sin(a)))
    for j in range(len(rows)-1):
        for i in range(N):
            k=j*(N+1)+i;f.append((k,k+1,k+N+2,k+N+1))
    o=mesh(n,v,f,0,bn);bpy.context.view_layer.objects.active=o
    mod=o.modifiers.new('Rolled_shell_thickness','SOLIDIFY');mod.thickness=.003;mod.offset=0;bpy.ops.object.modifier_apply(modifier=mod.name)
    return o
for side,s in [('Left',1),('Right',-1)]:
    arm_shell(side+'_fitted_pauldron',[(.162,.019,.02),(.18,.052,.055),(.202,.071,.077),(.238,.086,.090),(.28,.084,.088),(.315,.07,.067),(.33,.068,.062)],side+'UpperArm',s,amin=-.42,amax=3.56,N=28)
    arm_shell(side+'_upper_arm_shell',[(.335,.068,.068),(.351,.068,.069),(.390,.061,.06),(.428,.057,.052),(.449,.05,.045)],side+'UpperArm',s,amin=-.47,amax=2.70,N=24)
    arm_shell(side+'_flowing_bracer',[(.509,.05,.055),(.528,.057,.060),(.563,.057,.06),(.607,.05,.052),(.66,.041,.042),(.687,.037,.037)],side+'LowerArm',s,N=28)
    shell(side+'_thigh_carapace',[(.646,.060,.067),(.696,.065,.071),(.765,.073,.081),(.825,.081,.088),(.907,.086,.091),(.963,.082,.089),(.991,.078,.088)],-.86,1.95,0,side+'UpperLeg',side=s,cx=.105,N=28,hem=lambda a,j: .052*(1-math.cos(a))*max(0,1-j/2))
    shell(side+'_rear_thigh_sweep',[(.693,.059,.068),(.763,.067,.079),(.867,.081,.089),(.933,.080,.089)],-.18,1.15,0,side+'UpperLeg',side=s,cx=.105,back=True,N=18)
    shell(side+'_anatomic_greave',[(.162,.037,.04),(.194,.040,.045),(.285,.05,.058),(.374,.061,.069),(.448,.064,.072),(.477,.056,.061),(.493,.052,.059)],-1.03,2.25,0,side+'LowerLeg',side=s,cx=.105,N=28,hem=lambda a,j: .019*math.cos(a) if j==6 else 0)
    shell(side+'_calf_return',[(.183,.036,.041),(.292,.049,.069),(.39,.056,.083),(.455,.052,.077)],-.90,1.5,0,side+'LowerLeg',side=s,cx=.105,back=True,N=20)
    # slim seam line along outer thigh rather than a raised coloured plaque
    shell(side+'_thigh_identity_line',[(.734,.065,.072),(.88,.086,.091)],1.03,1.09,3,side+'UpperLeg',side=s,cx=.105,N=3)

# Small integrated chest insignia and helmet centre seam; no oversized chin pads.
disk('Sternal_ID',(0,-.143,1.405),.013,.004,3,'Chest')
# End v02 redesign.

# V03: a manufactured pressure helmet, panel hierarchy, smooth flex sleeves.
for o in list(parts):
    if o['head_part'] or any(k in o.name for k in ['_biceps','_forearm','_elbow_seal','_deltoid','Sternal_ID','Chest_flush','Flex_abdominal_seam']):
        parts.remove(o);bpy.data.objects.remove(o,do_unlink=True)

def smooth_rows(rows,steps=3):
    result=[]
    for i in range(len(rows)-1):
        p0=rows[max(0,i-1)];p1=rows[i];p2=rows[i+1];p3=rows[min(len(rows)-1,i+2)]
        for j in range(steps):
            t=j/steps
            result.append(tuple(.5*((2*b)+(-a+c)*t+(2*a-5*b+4*c-d)*t*t+(-a+3*b-3*c+d)*t*t*t) for a,b,c,d in zip(p0,p1,p2,p3)))
    result.append(rows[-1]);return result

def wire(n,points,r,m,bn,head_part=False,sides=6):
    cu=bpy.data.curves.new(n,'CURVE');cu.dimensions='3D';cu.resolution_u=5;cu.bevel_depth=r;cu.bevel_resolution=3 if r>.005 else 1;cu.resolution_u=4;cu.use_fill_caps=True
    sp=cu.splines.new('BEZIER');sp.bezier_points.add(len(points)-1)
    for b,p in zip(sp.bezier_points,points):b.co=p;b.handle_left_type='AUTO';b.handle_right_type='AUTO'
    ob=bpy.data.objects.new(n,cu);bpy.context.collection.objects.link(ob)
    bpy.ops.object.select_all(action='DESELECT');ob.select_set(True);bpy.context.view_layer.objects.active=ob;bpy.ops.object.convert(target='MESH')
    return finish(ob,n,m,bn,head_part)

# Reduced crown height, wider temples and a defined jaw instead of an ellipsoid.
helmetrows=smooth_rows([(1.535,.056,.068,.071),(1.549,.076,.086,.084),(1.579,.101,.107,.105),(1.626,.117,.121,.116),(1.686,.122,.125,.123),(1.736,.118,.115,.122),(1.771,.103,.090,.105),(1.793,.070,.054,.071),(1.799,.027,.021,.028),(1.7995,.004,.004,.004)],3)
v=[];f=[];N=64
for z,rx,front,back in helmetrows:
    for j in range(N):
        a=2*math.pi*j/N;c=math.cos(a)
        # The lower-front mask has a straighter jaw contour; upper rings remain curved.
        xx=rx*math.sin(a);yy=-(front if c>=0 else back)*c
        v.append((xx,yy,z))
for j in range(len(helmetrows)-1):
    for k in range(N):f.append((j*N+k,j*N+(k+1)%N,(j+1)*N+(k+1)%N,(j+1)*N+k))
f+=[tuple(reversed(range(N))),tuple((len(helmetrows)-1)*N+k for k in range(N))]
mesh('Helmet_pressure_hull',v,f,0,'Head',True)

def visorpos(u,t,offset=0):
    top=1.766-.028*abs(u)**3;bottom=1.572+.027*abs(u)**2
    return (.112*u*(1-.045*(2*t-1)**2),-.145+.067*u*u+.021*(2*t-1)**2-offset,bottom+(top-bottom)*t)
def visorgrid(name,scale,m):
    vs=[];fs=[];nx=40;ny=24
    for j in range(ny+1):
        for i in range(nx+1):
            u=-1+2*i/nx;t=j/ny;x,y,z=visorpos(u,t,.0007 if m==2 else 0)
            vs.append((x*scale,y+(0 if m==2 else .012),1.665+(z-1.665)*scale))
    for j in range(ny):
        for i in range(nx):k=j*(nx+1)+i;fs.append((k,k+1,k+nx+2,k+nx+1))
    return mesh(name,vs,fs,m,'Head',True)
visorgrid('Visor_black_compression_gasket',1.052,1)
visorgrid('Visor_gold_contoured_mask',1,2)
for t in [0,1]:
    wire('Visor_machined_retainer',[visorpos(u,t,.002) for u in [-1,-.8,-.5,0,.5,.8,1]],.0032,0,'Head',True)

# Layered jaw spars, separate temple modules, crown and rear service panels.
for s in [-1,1]:
    jaw=[(s*.109,-.014,1.644),(s*.104,-.047,1.597),(s*.079,-.088,1.553),(s*.035,-.11,1.543)]
    wire('Jaw_carbon_gasket',jaw,.012,1,'Head',True)
    wire('Jaw_structural_spar',[(x,y-.005,z+.003) for x,y,z in jaw],.008,0,'Head',True)
    disk('Temple_isolator',(s*.121,.005,1.674),.046,.012,1,'Head','X',True)
    disk('Temple_annular_housing',(s*.129,.005,1.674),.038,.012,0,'Head','X',True)
    disk('Temple_bronze_lock',(s*.137,.005,1.674),.025,.004,2,'Head','X',True)
    disk('Temple_carbon_center',(s*.140,.005,1.674),.019,.006,1,'Head','X',True)
    for a in [0,math.pi/2,math.pi,3*math.pi/2]:
        disk('Temple_captive_screw',(s*.140,.005+.032*math.cos(a),1.674+.032*math.sin(a)),.0035,.004,1,'Head','X',True)
    wire('Crown_split_seam',[(s*.036,-.047,1.787),(s*.047,.002,1.794),(s*.054,.052,1.786),(s*.049,.098,1.758),(s*.042,.12,1.712)],.0015,1,'Head',True)
    wire('Occipital_panel_seam',[(s*.043,.122,1.714),(s*.091,.088,1.715),(s*.109,.043,1.694)],.0013,1,'Head',True)
    for i in range(4):
        z=1.6+i*.010
        wire('Helmet_rear_heat_slot',[(s*.032,.109,z),(s*.062,.100,z),(s*.079,.085,z+.003)],.0021,1,'Head',True)
    for z in [1.564,1.582]:disk('Jaw_lock',(s*.083,-.08,z),.005,.004,1,'Head','Y',True)
# Low-profile chin cassette has a chamfered polygon silhouette, not round beads.
chin=plate('Chin_filter_frame',[(-.05,1.566),(.05,1.566),(.049,1.545),(.028,1.533),(-.028,1.533),(-.049,1.545)],-.095,.012,0,'Head');chin['head_part']=True
for x in [-.026,-.013,0,.013,.026]:
    wire('Chin_filter_slot',[(x,-.11,1.54),(x,-.114,1.55)],.0018,1,'Head',True)

# Add six deformation helpers. Twist helpers spread axial rotation; pauldrons lift partially.
for side,s in [('Left',1),('Right',-1)]:
    addbone(side+'UpperArmTwist',(s*.225,0,1.445),(s*.475,0,1.445),side+'UpperArm')
    addbone(side+'ForearmTwist',(s*.475,0,1.445),(s*.705,0,1.445),side+'LowerArm')
    addbone(side+'ShoulderArmor',(s*.225,0,1.445),(s*.335,0,1.445),side+'Shoulder')
    # Closed continuous flex sleeve with many joint rings instead of intersecting capped cylinders.
    rows=smooth_rows([(.16,.056,.06),(.20,.071,.075),(.245,.073,.073),(.30,.069,.066),(.38,.061,.059),(.435,.052,.05),(.455,.049,.048),(.475,.049,.048),(.495,.050,.049),(.53,.051,.050),(.59,.044,.043),(.66,.035,.034),(.709,.031,.03)],2)
    vv=[];ff=[];ns=28
    for x,ry,rz in rows:
        for j in range(ns):
            a=2*math.pi*j/ns;vv.append((s*x,-ry*math.cos(a),1.445+rz*math.sin(a)))
    for j in range(len(rows)-1):
        for i in range(ns):ff.append((j*ns+i,j*ns+(i+1)%ns,(j+1)*ns+(i+1)%ns,(j+1)*ns+i))
    ff.extend([tuple(reversed(range(ns))),tuple((len(rows)-1)*ns+i for i in range(ns))])
    sleeve=mesh(side+'_continuous_flex_sleeve',vv,ff,1,side+'UpperArm')
    sleeve.vertex_groups.clear()
    names=[side+n for n in ['Shoulder','UpperArm','UpperArmTwist','LowerArm','ForearmTwist','Hand']]
    for name in names:sleeve.vertex_groups.new(name=name)
    def smooth01(t):t=max(0,min(1,t));return t*t*(3-2*t)
    for vertex in sleeve.data.vertices:
        x=abs(vertex.co.x)
        if x<.26:
            t=smooth01((x-.18)/.08);weights={side+'Shoulder':1-t,side+'UpperArm':t}
        elif x<.43:
            t=smooth01((x-.27)/.16)*.65;weights={side+'UpperArm':1-t,side+'UpperArmTwist':t}
        elif x<.525:
            t=smooth01((x-.43)/.095);weights={side+'UpperArm':1-t,side+'LowerArm':t}
        elif x<.67:
            t=smooth01((x-.525)/.145);weights={side+'LowerArm':1-t,side+'ForearmTwist':t}
        else:
            t=smooth01((x-.67)/.04);weights={side+'ForearmTwist':1-t,side+'Hand':t}
        for name,w in weights.items():
            if w>0:sleeve.vertex_groups[name].add([vertex.index],w,'REPLACE')
    for o in parts:
        if o.name==side+'_fitted_pauldron':
            o.vertex_groups.clear();weight(o,side+'ShoulderArmor')
        if o.name==side+'_flowing_bracer':
            o.vertex_groups.clear();weight(o,side+'ForearmTwist')
    # Fabric accordion rings on the outer elbow, thin raised stitching.
    for x in [.454,.466,.478,.49]:
        pts=[(s*x,-.0495*math.cos(a),1.445+.05*math.sin(a)) for a in [.4,.8,1.2,1.6,2,2.4,2.8]]
        ob=wire(side+'_elbow_seam',pts,.0012,1,side+'LowerArm')
        ob.vertex_groups.clear();t=max(0,min(1,(x-.43)/.095));weight(ob,{side+'UpperArm':1-t,side+'LowerArm':t})

# Panel seam sampling exactly follows the existing compound chest surface.
def chestpoint(a,t,offset=.003):
    dense=smooth_rows(chestrows,3);at=t*3;k=min(len(dense)-2,int(at));q=at-k
    z,rx,ry=[dense[k][i]*(1-q)+dense[k+1][i]*q for i in range(3)]
    return ((rx+offset)*math.sin(a),-(ry+offset)*math.cos(a),z+.072*max(0,math.cos(a))**5*max(0,1-t/3))
for s in [-1,1]:
    for uv in [[(.13,6.5),(.18,5.1),(.37,4.7),(.70,4.8),(1.12,4.7),(1.32,3.4)],[(.10,3.9),(.30,2.8),(.65,1.7),(1.06,1.3),(1.38,1.5)]]:
        wire('Chest_precision_panel_joint',[chestpoint(s*a,t) for a,t in uv],.00125,1,'Chest')
    for a,t in [(.20,5.25),(.70,4.82),(1.25,2),(.55,1.0)]:
        p=chestpoint(s*a,t,.003);disk('Chest_recessed_fastener',p,.0032,.003,1,'Chest')
    # Angled chest service port and concentric seat, visibly asymmetric utility module.
    p=chestpoint(s*.87,1.25,.004)
    disk('Pectoral_service_seat',p,.017,.008,1,'Chest')
    disk('Pectoral_service_ring',(p[0],p[1]-.004,p[2]),.0125,.007,0,'Chest')
    disk('Pectoral_service_core',(p[0],p[1]-.009,p[2]),.0085,.005,2 if s==1 else 3,'Chest')
    # Parallel contour seams on side armour.
    for z,rx,ry in [(1.15,.13,.096),(1.218,.147,.103),(1.266,.167,.111)]:
        wire('Flank_laminated_seam',[(s*rx*math.sin(a),-ry*math.cos(a),z+.013*math.sin(a)) for a in [.70,.93,1.2,1.5,1.8]],.00125,1,'Spine')
    for side,bn in [('Left' if s==1 else 'Right','UpperArm')]:
        # Shoulder shaped seam loops, screws and a narrow identifier.
        for x,ry,rz in [(.215,.080,.084),(.296,.083,.077)]:
            wire(side+'_pauldron_seam',[(s*x,-ry*math.cos(a),1.445+rz*math.sin(a)) for a in [0,.4,.8,1.2,1.6,2,2.4,2.8,3.1]],.0013,1,side+'ShoulderArmor')
        wire(side+'_pauldron_rolled_edge',[(s*.33,-.068*math.cos(a),1.445+.062*math.sin(a)) for a in [-.42,0,.5,1,1.5,2,2.5,3,3.56]],.0024,0,side+'ShoulderArmor')
        for x in [.245,.286]:disk(side+'_shoulder_rivet',(s*x,-.085,1.463),.0032,.004,1,side+'ShoulderArmor')
        # Inlaid wrist instrument; compact machined frame replaces a large plaque.
        o=plate(side+'_wrist_instrument',[(s*.556,1.469),(s*.611,1.465),(s*.627,1.44),(s*.613,1.425),(s*.556,1.433)],-.055,.005,1,side+'ForearmTwist')
        for k in range(3):
            wire(side+'_instrument_indicator',[(s*(.566+k*.013),-.063,1.438),(s*(.569+k*.013),-.063,1.453)],.0015,3,side+'ForearmTwist')
        for a in [.16,2.92]:
            wire(side+'_bracer_edge_groove',[(s*x,-(ry+.001)*math.cos(a),1.445+(rz+.001)*math.sin(a)) for x,ry,rz in [(.533,.057,.06),(.563,.057,.06),(.607,.05,.052),(.66,.041,.042)]],.00125,1,side+'ForearmTwist')
        # Tapered thigh / greave seams and small inset fasteners.
        for a in [.15,1.1]:
            pts=[(s*(.105+rx*math.sin(a)),-(ry+.001)*math.cos(a),z) for z,rx,ry in [(.72,.068,.077),(.825,.081,.088),(.907,.086,.091),(.957,.082,.089)]]
            wire(side+'_thigh_panel_channel',pts,.0011,1,side+'UpperLeg')
        for a in [.10,1.2]:
            pts=[(s*(.105+rx*math.sin(a)),-(ry+.001)*math.cos(a),z) for z,rx,ry in [(.19,.04,.045),(.285,.05,.058),(.374,.061,.069),(.448,.064,.072)]]
            wire(side+'_greave_panel_channel',pts,.0011,1,side+'LowerLeg')
        for z,y in [(.83,-.092),(.435,-.076)]:
            disk(side+'_leg_captive_screw',(s*.123,y,z),.0035,.003,1,side+('UpperLeg' if z>.6 else 'LowerLeg'))

# Fine paint roughness and elastomer grain in the editable source, not a fake photo overlay.
for m,scale,strength in [(M[0],360,.045),(M[1],240,.10)]:
    nodes=m.node_tree.nodes;links=m.node_tree.links;noise=nodes.new('ShaderNodeTexNoise');noise.inputs['Scale'].default_value=scale;noise.inputs['Detail'].default_value=2
    bump=nodes.new('ShaderNodeBump');bump.inputs['Strength'].default_value=strength;bump.inputs['Distance'].default_value=.001
    links.new(noise.outputs['Fac'],bump.inputs['Height']);links.new(bump.outputs['Normal'],nodes['Principled BSDF'].inputs['Normal'])
M[0].node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value=.31
M[2].node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value=.19

# Angle-aware smoothing preserves manufactured edges without faceting the main surfaces.
for o in parts:
    if '_upper_arm_shell' in o.name:
        for vertex in o.data.vertices:
            vertex.co.y*=1.10;vertex.co.z=1.445+(vertex.co.z-1.445)*1.16
    bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(55),keep_sharp_edges=True)
# End v03 geometry.

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
    if p:
        eb.parent=armdata.edit_bones[p]
        eb.use_connect=(eb.head-eb.parent.tail).length<1e-6
bpy.ops.object.mode_set(mode='OBJECT');rig.show_in_front=True;armdata.display_type='STICK'
# Functional Blender arm controls; helper bones also remain in exported skeleton.
controls={};ik_constraints={}
rig['ArmIK']=0.0;rig['HeadControl']=0.0;rig['PelvisControl']=0.0
for prop,desc in [('ArmIK','0: FK / 1: hand target + elbow pole IK'),('HeadControl','Enable CTRL_Head orientation'),('PelvisControl','Enable CTRL_Pelvis transform')]:
    rig.id_properties_ui(prop).update(min=0,max=1,description=desc)
ctrl_collection=bpy.data.collections.new('Pilot_Rig_Controls');bpy.context.scene.collection.children.link(ctrl_collection)
def control(n,bn,shape='CUBE',size=.06):
    o=bpy.data.objects.new(n,None);ctrl_collection.objects.link(o);o.empty_display_type=shape;o.empty_display_size=size;o.show_in_front=True
    o.rotation_mode='QUATERNION';o.matrix_world=rig.matrix_world@rig.pose.bones[bn].matrix;o.hide_render=True;controls[n]=o;return o
def influence_driver(c,prop):
    d=c.driver_add('influence').driver;d.type='SCRIPTED';d.expression='v'
    v=d.variables.new();v.name='v';v.type='SINGLE_PROP';v.targets[0].id=rig;v.targets[0].data_path='["'+prop+'"]'
for side in ['Left','Right']:
    for helper,target,influence in [('UpperArmTwist','LowerArm',.35),('ForearmTwist','Hand',.5),('ShoulderArmor','UpperArm',1.0)]:
        b=rig.pose.bones[side+helper];c=b.constraints.new('COPY_ROTATION');c.name='Follow_'+target;c.target=rig;c.subtarget=side+target
        c.target_space='LOCAL';c.owner_space='LOCAL';c.influence=influence
        if helper!='ShoulderArmor':c.use_x=False;c.use_z=False;c.use_y=True
        c.mix_mode='REPLACE'
    target=control('CTRL_'+side+'Hand',side+'Hand')
    pole=control('CTRL_'+side+'Elbow',side+'LowerArm','SPHERE',.045)
    pole.location.y-=.35
    c=rig.pose.bones[side+'LowerArm'].constraints.new('IK');c.name='Two_bone_arm_IK';c.target=target;c.pole_target=pole;c.chain_count=2;c.use_stretch=False;c.iterations=100
    influence_driver(c,'ArmIK');ik_constraints[side]=c
    cr=rig.pose.bones[side+'Hand'].constraints.new('COPY_ROTATION');cr.target=target;cr.target_space='WORLD';cr.owner_space='WORLD';influence_driver(cr,'ArmIK')
target=control('CTRL_Head','Head','CIRCLE',.16)
c=rig.pose.bones['Head'].constraints.new('COPY_ROTATION');c.target=target;c.target_space='WORLD';c.owner_space='WORLD';influence_driver(c,'HeadControl')
target=control('CTRL_Pelvis','Hips','CIRCLE',.22)
c=rig.pose.bones['Hips'].constraints.new('COPY_TRANSFORMS');c.target=target;c.target_space='WORLD';c.owner_space='WORLD';influence_driver(c,'PelvisControl')

def snap_ik():
    # FK pose is the desired reference. Locate the pole geometrically and calibrate bone roll.
    rig['ArmIK']=0.0;bpy.context.view_layer.update();goals={}
    for side in ['Left','Right']:
        upper=rig.pose.bones[side+'UpperArm'];lower=rig.pose.bones[side+'LowerArm'];hand=rig.pose.bones[side+'Hand']
        a=upper.head.copy();e=lower.head.copy();w=hand.head.copy();direction=(w-a).normalized()
        perpendicular=e-a-direction*(e-a).dot(direction)
        if perpendicular.length<.002:perpendicular=Vector((0,-1,-.15))
        polepos=e+perpendicular.normalized()*.35
        controls['CTRL_'+side+'Hand'].matrix_world=rig.matrix_world@hand.matrix
        controls['CTRL_'+side+'Elbow'].location=rig.matrix_world@polepos
        goals[side]=e
    rig['ArmIK']=1.0;bpy.context.view_layer.update()
    for side in ['Left','Right']:
        c=ik_constraints[side];best=0;error=1e9
        for i in range(16):
            angle=-math.pi+2*math.pi*i/16;c.pole_angle=angle;bpy.context.view_layer.update()
            dist=(rig.pose.bones[side+'LowerArm'].head-goals[side]).length
            if dist<error:error=dist;best=angle
        step=math.pi/8
        for _ in range(3):
            center=best
            for i in [-2,-1,0,1,2]:
                a=center+i*step/4;c.pole_angle=a;bpy.context.view_layer.update()
                dist=(rig.pose.bones[side+'LowerArm'].head-goals[side]).length
                if dist<error:error=dist;best=a
            step/=4
        c.pole_angle=best
    bpy.context.view_layer.update()
rig['RigHelp']='ArmIK=1: move CTRL_LeftHand/RightHand; elbow spheres set bend plane. FK switch requires snap_ik in generator. HeadControl/PelvisControl opt-in.'

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
rig['DefaultHeightMetres']=1.7995;rig['LocalView']='Hide Pilot_Head for local camera only; keep full rig for remote users'

def reset():
    rig['ArmIK']=0.0;rig['HeadControl']=0.0;rig['PelvisControl']=0.0
    for b in rig.pose.bones:b.rotation_mode='QUATERNION';b.rotation_quaternion=Quaternion();b.location=(0,0,0)
    bpy.context.view_layer.update()
def rotate_world(n,axis,deg):
    b=rig.pose.bones[n];local=b.bone.matrix_local.to_3x3().inverted()@Vector(axis)
    b.rotation_quaternion=Quaternion(local,math.radians(deg))
def rotations(n,values):
    b=rig.pose.bones[n];q=Quaternion()
    for axis,deg in values:
        local=b.bone.matrix_local.to_3x3().inverted()@Vector(axis);q=Quaternion(local,math.radians(deg))@q
    b.rotation_quaternion=q
def fingers(curl):
    for side,s in [('Left',1),('Right',-1)]:
        for k,fn in enumerate(['Index','Middle','Ring','Little']):
            for seg,mult in [('Proximal',.78),('Intermediate',1),('Distal',.72)]:
                rotate_world(side+fn+seg,(0,s,0),curl*mult*(.91+k*.035))
        for seg,mult in [('Proximal',.27),('Intermediate',.52),('Distal',.72)]:rotate_world(side+'Thumb'+seg,(0,s,0),curl*mult)
def pose(name):
    reset()
    if name=='Neutral' or name=='WristTwist':
        for side,s in [('Left',1),('Right',-1)]:
            rotate_world(side+'UpperArm',(0,1,0),s*63)
            rotate_world(side+'LowerArm',(0,0,1),-s*20)
        fingers(18)
    if name=='Guard':
        rotate_world('Chest',(0,0,1),-3)
        for side,s in [('Left',1),('Right',-1)]:
            rotate_world(side+'Shoulder',(0,0,1),-s*7)
            rotations(side+'UpperArm',[((0,1,0),s*14),((0,0,1),-s*67)])
            rotate_world(side+'LowerArm',(0,1,0),-s*102)
        fingers(76)
    if name=='Punch':
        rotate_world('Spine',(0,0,1),-8);rotate_world('Head',(0,0,1),8)
        rotate_world('LeftShoulder',(0,0,1),-7);rotate_world('LeftUpperArm',(0,0,1),-82);rotate_world('LeftLowerArm',(0,0,1),-9)
        rotate_world('RightUpperArm',(0,1,0),-54);rotate_world('RightLowerArm',(0,0,1),82);fingers(76)
    if name=='BentArm':
        rotate_world('LeftUpperArm',(0,1,0),50);rotate_world('LeftLowerArm',(0,0,1),-99)
        rotate_world('RightUpperArm',(0,1,0),-45);rotate_world('RightLowerArm',(0,0,1),94);fingers(48)
    if name=='RaisedArm':
        rotate_world('LeftShoulder',(0,1,0),-12);rotate_world('LeftUpperArm',(0,1,0),-58);rotate_world('LeftLowerArm',(0,0,1),-28)
        rotate_world('RightUpperArm',(0,1,0),-64);fingers(40)
    if name=='WristTwist':
        rotate_world('LeftLowerArm',(0,0,1),-77)
        rig.pose.bones['LeftHand'].rotation_quaternion=Quaternion((0,1,0),math.radians(72));fingers(53)
    bpy.context.view_layer.update()
    if name!='TPose':snap_ik()
for i,name in enumerate(['TPose','Neutral','Guard','Punch','BentArm','RaisedArm','WristTwist']):
    pose(name);frame=1+i*30
    for b in rig.pose.bones:b.keyframe_insert('rotation_quaternion',frame=frame,group=b.name)
    rig.keyframe_insert(data_path='["ArmIK"]',frame=frame)
    for ob in controls.values():
        ob.rotation_mode='QUATERNION';ob.keyframe_insert('location',frame=frame);ob.keyframe_insert('rotation_quaternion',frame=frame)
    for c in ik_constraints.values():c.keyframe_insert('pole_angle',frame=frame)
if rig.animation_data and rig.animation_data.action:rig.animation_data.action.name='AS_NaturalArmReview_7_Positions'
for i,n in enumerate(['TPose','Neutral','Guard','Punch','BentArm','RaisedArm','WristTwist']):bpy.context.scene.timeline_markers.new(n,frame=1+i*30)
scene=bpy.context.scene;scene.frame_end=181;scene.frame_set(1);reset()

# Skin/geometry budget measured from actual final meshes.
stats={'height_metres':round(max(v.co.z for v in head.data.vertices),4),'bones':len(bones),'skinned_renderers':2,'materials':len(M),'meshes':{},'pose_frames':dict(zip(['TPose','Neutral','Guard','Punch','BentArm','RaisedArm','WristTwist'],[1,31,61,91,121,151,181]))}
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
    bpy.ops.export_scene.fbx(filepath=str(O/'AS_Pilot_v03.fbx'),use_selection=True,object_types={'ARMATURE','MESH'},add_leaf_bones=False,bake_anim=False,axis_forward='-Z',axis_up='Y',apply_unit_scale=True,use_mesh_modifiers=True,mesh_smooth_type='FACE',path_mode='AUTO')
export()
# Separate geometry-reduced candidate. This is not a Quest performance certification.
lod_collection=bpy.data.collections.new('Quest_LOD1_Candidate');bpy.context.scene.collection.children.link(lod_collection)
lod_objects=[];lodstats={'label':'Quest candidate; device performance untested','bones':len(bones),'meshes':{}}
for src in [body,head]:
    lo=src.copy();lo.data=src.data.copy();lo.name=src.name+'_Quest';lod_collection.objects.link(lo)
    for mod in list(lo.modifiers):lo.modifiers.remove(mod)
    bpy.ops.object.select_all(action='DESELECT');lo.select_set(True);bpy.context.view_layer.objects.active=lo
    dec=lo.modifiers.new('Geometry_budget','DECIMATE');dec.ratio=.46;dec.use_collapse_triangulate=True
    bpy.ops.object.modifier_apply(modifier=dec.name)
    bpy.ops.object.shade_smooth_by_angle(angle=.95993,keep_sharp_edges=False)
    bpy.ops.object.vertex_group_limit_total(group_select_mode='ALL',limit=4)
    bpy.ops.object.vertex_group_normalize_all(group_select_mode='ALL',lock_active=False)
    mod=lo.modifiers.new('Humanoid_skin','ARMATURE');mod.object=rig
    lo.data.calc_loop_triangles();mw=max(sum(g.weight>1e-6 for g in v.groups) for v in lo.data.vertices)
    lodstats['meshes'][lo.name]={'vertices':len(lo.data.vertices),'triangles':len(lo.data.loop_triangles),'max_weights':mw,'unweighted':sum(not any(g.weight>1e-6 for g in v.groups) for v in lo.data.vertices)}
    lod_objects.append(lo)
lodstats['total_triangles']=sum(x['triangles'] for x in lodstats['meshes'].values())
(O/'quest_candidate_stats.json').write_text(json.dumps(lodstats,indent=2))
bpy.ops.object.select_all(action='DESELECT')
for lo in lod_objects+[rig]:lo.select_set(True)
bpy.context.view_layer.objects.active=rig
bpy.ops.export_scene.fbx(filepath=str(O/'AS_Pilot_v03_QuestCandidate.fbx'),use_selection=True,object_types={'ARMATURE','MESH'},add_leaf_bones=False,bake_anim=False,axis_forward='-Z',axis_up='Y',apply_unit_scale=True,use_mesh_modifiers=True,mesh_smooth_type='FACE')
for lo in lod_objects:lo.hide_render=True;lo.hide_set(True)


# Studio is kept in the editable source, never exported with the avatar.
studio=bpy.data.collections.new('Preview_Studio');scene.collection.children.link(studio)
def stage(o):
    for c in list(o.users_collection):c.objects.unlink(o)
    studio.objects.link(o)
floor=mat('StudioGround',(.022,.037,.048),.1,.58)
bpy.ops.mesh.primitive_plane_add(size=2000,location=(0,0,.019));o=bpy.context.object;o.name='Studio_ground';o.data.materials.append(floor);stage(o)
for loc,energy,size,col in [((2,-3,4),500,3,(.81,.91,1)),((-2,-1,2.7),350,2,(1,.77,.5)),((0,3,3),700,2,(.48,.8,1))]:
    bpy.ops.object.light_add(type='AREA',location=loc);o=bpy.context.object;o.data.energy=energy;o.data.shape='DISK';o.data.size=size;o.data.color=col;o.rotation_euler=(Vector((0,0,1))-o.location).to_track_quat('-Z','Y').to_euler();stage(o)
bpy.ops.object.camera_add(location=(2,-4,2));cam=bpy.context.object;stage(cam);scene.camera=cam;cam.data.type='ORTHO';cam.data.ortho_scale=2.17
scene.render.engine='CYCLES';scene.cycles.samples=32;scene.cycles.use_denoising=True
scene.render.resolution_x=1020;scene.render.resolution_y=1200;scene.render.resolution_percentage=100
scene.world.color=(.18,.18,.18);scene.view_settings.view_transform='AgX'
scene.render.image_settings.file_format='PNG';scene.render.film_transparent=False
def render(name,pos,target=(0,0,.96),scale=2.08):
    if os.environ.get('PILOT_FAST_REVIEW')=='1' and name not in ['01_hero','05_Guard','05_BentArm','05_RaisedArm']:return
    cam.location=pos;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=scale
    scene.render.filepath=str(P/(name+'.png'));bpy.ops.render.render(write_still=True)
scene.frame_set(31);pose('Neutral')
cam.location=(2.6,-4,2.15);cam.rotation_euler=(Vector((0,0,.95))-cam.location).to_track_quat('-Z','Y').to_euler()
if (O/'pilot_rig_tools.py').exists():
    txt=bpy.data.texts.get('pilot_rig_tools.py') or bpy.data.texts.new('pilot_rig_tools.py');txt.clear();txt.write((O/'pilot_rig_tools.py').read_text(encoding='utf-8'))
bpy.ops.wm.save_as_mainfile(filepath=str(O/'AS_Pilot_v03.blend'))
render('01_hero',(2.6,-4,2.05))
render('02_front',(0,-5,1.04));render('03_back',(0,5,1.04));render('04_side',(5,0,1.04))
for name,frame in [('Guard',61),('Punch',91),('BentArm',121),('RaisedArm',151),('WristTwist',181)]:
    scene.frame_set(frame);pose(name);render('05_'+name,(2.5,-4,2.25),scale=2.22)
scene.frame_set(31);pose('Neutral');render('06_helmet',(1.7,-3,1.9),target=(0,0,1.60),scale=.68)
scene.frame_set(31);pose('Neutral')
for ob in [body,head]:ob.hide_render=True
for ob in lod_objects:ob.hide_render=False;ob.hide_set(False)
render('07_quest_candidate',(2.6,-4,2.05))
for ob in [body,head]:ob.hide_render=False
for ob in lod_objects:ob.hide_render=True;ob.hide_set(True)
scene.frame_set(1);reset()
# Round-trip in a clean file proves exported FBX contains skinned meshes and hierarchy.
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(O/'AS_Pilot_v03.fbx'))
rr={'armatures':[],'meshes':[]}
for o in bpy.context.scene.objects:
    if o.type=='ARMATURE':rr['armatures'].append({'name':o.name,'bones':len(o.data.bones),'bone_names':list(o.data.bones.keys())})
    if o.type=='MESH':rr['meshes'].append({'name':o.name,'vertices':len(o.data.vertices),'vertex_groups':len(o.vertex_groups),'armature_modifiers':sum(m.type=='ARMATURE' for m in o.modifiers)})
rr['pass']=len(rr['armatures'])==1 and rr['armatures'][0]['bones']==len(bones) and len(rr['meshes'])==2 and all(m['armature_modifiers']==1 and m['vertex_groups']>0 for m in rr['meshes'])
(O/'fbx_roundtrip.json').write_text(json.dumps(rr,indent=2))
print('PILOT_BUILD_COMPLETE',json.dumps(stats),json.dumps(rr))
