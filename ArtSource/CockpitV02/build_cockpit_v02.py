"""Blender 5.2: editable cockpit + modular FBX + three rendered views.
Run: blender --background --python build_cockpit.py -- OUTPUT_DIRECTORY
Units metres. Blender +Y is forward. All dimensions below are assumptions.
"""
import bpy, math, random, os, sys, json, struct, zlib
from mathutils import Vector
from math import sin, cos, pi
OUT = os.path.abspath(sys.argv[sys.argv.index('--')+1] if '--' in sys.argv else os.path.dirname(__file__))
os.makedirs(OUT, exist_ok=True)
SPACING=2.10
PAD_RADIUS=.65
PAD_TOP=.04
EYE_HEIGHT=1.65
CEILING=3.50
random.seed(47)
bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)
for c in list(bpy.data.collections):
    if c.name != 'Collection': bpy.data.collections.remove(c)
base=bpy.data.collections.get('Collection'); base.name='00_Cockpit'
groups={}
def group(name):
    c=bpy.data.collections.new(name); bpy.context.scene.collection.children.link(c); groups[name]=c; return c
for n in ['01_Deck','02_Station_L','03_Station_R','04_Console_L','05_Console_R','08_Ceiling_Removable','09_Hull','10_Display','11_PreviewOnly']: group(n)
current=groups['01_Deck']
M={}; meta=[]
def mat(name,color,metal=.0,rough=.5,emission=0):
    m=bpy.data.materials.new(name); m.diffuse_color=(*color,1); m.use_nodes=True
    bs=m.node_tree.nodes.get('Principled BSDF'); bs.inputs['Base Color'].default_value=(*color,1); bs.inputs['Metallic'].default_value=metal; bs.inputs['Roughness'].default_value=rough
    if emission: bs.inputs['Emission Color'].default_value=(*color,1); bs.inputs['Emission Strength'].default_value=emission
    M[name]=m; meta.append(dict(name=name,color=list(color),metallic=metal,roughness=rough,emission=emission))
    return m
mat('Hull_Graphite',(.07,.095,.11),.75,.4)
mat('Panel_Steel',(.14,.18,.20),.7,.47)
mat('Edge_Alloy',(.32,.37,.39),.85,.31)
mat('Rubber',(.018,.024,.028),.05,.84)
mat('Inset_Black',(.012,.019,.023),.35,.5)
mat('Warning_Ochre',(.62,.31,.065),.45,.45)
mat('Signal_Cyan',(.08,.65,.84),.25,.32,2)
mat('Signal_Amber',(.95,.36,.07),.1,.35,2)
mat('Lettering',(.65,.76,.77),.1,.65)
mat('Screen_UI',(.023,.14,.20),.2,.35,.6)
mat('Display_Feed',(.23,.38,.45),0,.95,1)
# Subtle material microstructure stays in the Blender source. Unity uses PBR values.
for name in ['Hull_Graphite','Panel_Steel']:
    m=M[name]; ns=m.node_tree.nodes; links=m.node_tree.links
    noise=ns.new('ShaderNodeTexNoise'); noise.inputs['Scale'].default_value=150; noise.inputs['Detail'].default_value=2
    bump=ns.new('ShaderNodeBump'); bump.inputs['Strength'].default_value=.17; bump.inputs['Distance'].default_value=.012
    links.new(noise.outputs['Fac'],bump.inputs['Height']); links.new(bump.outputs['Normal'],ns.get('Principled BSDF').inputs['Normal'])
def finish(o,name,material,bevel=0):
    o.name=name
    for c in list(o.users_collection): c.objects.unlink(o)
    current.objects.link(o)
    if material: o.data.materials.append(M[material])
    if bevel:
        mod=o.modifiers.new('Machined edges','BEVEL'); mod.width=bevel; mod.segments=2
        mod=o.modifiers.new('Weighted normals','WEIGHTED_NORMAL')
    return o
def box(name,loc,scale,material='Panel_Steel',bevel=.015):
    bpy.ops.mesh.primitive_cube_add(size=1,location=loc); o=bpy.context.object; o.scale=scale
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    return finish(o,name,material,bevel)
def cyl(name,loc,r,depth,material='Edge_Alloy',vertices=48):
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices,radius=r,depth=depth,location=loc)
    o=finish(bpy.context.object,name,material,.008)
    return o
def beam(name,a,b,r,material='Hull_Graphite',vertices=12):
    a,b=Vector(a),Vector(b); o=cyl(name,(a+b)/2,r,(b-a).length,material,vertices); o.rotation_euler=(b-a).to_track_quat('Z','Y').to_euler(); return o
def tube(name,points,r,material='Rubber'):
    cr=bpy.data.curves.new(name,'CURVE'); cr.dimensions='3D'; cr.resolution_u=12; cr.bevel_depth=r; cr.bevel_resolution=2
    sp=cr.splines.new('BEZIER'); sp.bezier_points.add(len(points)-1)
    for p,co in zip(sp.bezier_points,points): p.co=co; p.handle_left_type='AUTO'; p.handle_right_type='AUTO'
    o=bpy.data.objects.new(name,cr); current.objects.link(o); cr.materials.append(M[material]); return o
def ring(name,loc,r,thickness,material='Edge_Alloy'):
    bpy.ops.mesh.primitive_torus_add(major_radius=r,minor_radius=thickness,major_segments=64,minor_segments=8,location=loc)
    return finish(bpy.context.object,name,material)
def text(name,body,loc,size=.1,material='Lettering',rot=(pi/2,0,0)):
    cr=bpy.data.curves.new(name,'FONT'); cr.body=body; cr.size=size; cr.extrude=.0003; cr.align_x='CENTER'
    o=bpy.data.objects.new(name,cr); current.objects.link(o); o.location=loc; o.rotation_euler=rot; cr.materials.append(M[material]); return o
def bolt(loc): return cyl('Fastener',loc,.021,.011,'Edge_Alloy',6)
# Shared deck: recessed plates, cross seams and access grilles.
box('Deck_foundation',(0,.1,-.16),(6.15,6.1,.27),'Hull_Graphite',.06)
for ix in range(6):
    for iy in range(6):
        x=-2.5+ix; y=-2.4+iy
        box('Deck_plate',(x,y,-.015),(.98,.98,.055),'Panel_Steel',.012)
        for dx,dy in [(-.43,-.43),(.43,.43)]: bolt((x+dx,y+dy,.019))
for x in [-2.5,2.5]:
    for y in [-1.9,-.9,.1,1.1,2.1]:
        box('Service_grille',(x,y,.023),(.58,.7,.02),'Inset_Black')
        for q in range(10): box('Grille_bar',(x,y-.29+q*.064,.043),(.52,.018,.018),'Edge_Alloy',.002)
for x in [-2.84,2.84]:
    for y in [-2.3,-1.5,-.7,.1,.9,1.7,2.5]: box('Deck_marker',(x,y,.032),(.035,.19,.015),'Signal_Amber',.004)
text('Entry_label','ASE / DUAL SYNC',(0,-2.05,.028),.19,rot=(0,0,0))
text('Entry_sub','CHEST CONTROL MODULE   /   01',(0,-2.32,.028),.075,rot=(0,0,0))
for s,label in [(-1,'L'),(1,'R')]:
    x=s*SPACING/2; current=groups['02_Station_L' if s<0 else '03_Station_R']
    # Flush location markers, not a mechanical standing rig.
    cyl('Station_tread_'+label,(x,0,.021),PAD_RADIUS,.038,'Rubber',64)
    ring('Station_ring_'+label,(x,0,.04),PAD_RADIUS-.025,.010,'Signal_Cyan')
    for q in range(-8,9):
        yy=q*.065; half=math.sqrt(max(0,(PAD_RADIUS-.1)**2-yy**2))
        box('Tread',(x,yy,.042),(2*half,.014,.006),'Panel_Steel',.002)
    text('Station_ID_'+label,'0'+('1' if s<0 else '2')+' / '+label,(x,-.37,.05),.105,rot=(0,0,0))
    # Read-only status modules at the front perimeter; controllers remain in free space.
    current=groups['04_Console_L' if s<0 else '05_Console_R']; cx=s*.68
    box('Status_mount',(cx,3.27,.60),(.14,.16,.27),'Hull_Graphite')
    box('Status_housing',(cx,3.20,.83),(.52,.14,.25),'Panel_Steel',.025)
    box('Status_screen',(cx,3.12,.84),(.46,.016,.18),'Screen_UI',.006)
    text('Status_label','SYNC / '+label,(cx,3.108,.85),.06,'Signal_Cyan')
    for j in range(5): box('Status_bar',(cx-.12+j*.06,3.107,.794),(.034,.008,.011),'Signal_Cyan',.002)
# Mechanical ceiling with replaceable structure.
current=groups['08_Ceiling_Removable']
for x in [-2.7,-1.8,0,1.8,2.7]: box('Roof_longeron',(x,-.05,3.48),(.14,5.5,.22),'Hull_Graphite')
for y in [-2.2,-.9,.55,1.9]: box('Roof_crossmember',(0,y,3.47),(5.6,.16,.20),'Edge_Alloy')
for x in [-2.22,-.90,.90,2.22]:
    for y in [-1.55,-.18,1.19]:
        box('Roof_panel',(x,y,3.59),(.96,1.14,.075),'Panel_Steel')
        for j in range(6): box('Roof_cooling_slot',(x-.25+j*.1,y,3.543),(.038,.63,.016),'Inset_Black',.002)
for x in [-2.35,2.35]: box('Ceiling_light',(x,.05,3.32),(.10,2.3,.045),'Signal_Cyan')
box('Roof_pressure_shell',(0,.1,3.73),(8.25,7.9,.14),'Hull_Graphite',.025)
# Rear and lateral service housings; front is unobstructed curved media surface.
current=groups['09_Hull']
for s in [-1,1]:
    for y in [-2,-1,0,1,2]:
        box('Lower_hull',(s*2.96,y,.4),(.20,.96,.77),'Hull_Graphite',.035)
        box('Service_panel',(s*2.835,y,.43),(.045,.78,.50),'Panel_Steel')
    for y in [-2.4,2.7]:
        beam('Corner_stanchion',(s*2.85,y,.4),(s*2.65,y,3.4),.105,'Edge_Alloy')
    box('Rear_equipment',(s*2.7,-2.3,1.73),(.40,.51,1.9),'Hull_Graphite',.04)
    for z in [1.05,1.6,2.15]:
        box('Rear_access',(s*2.46,-2.3,z),(.035,.42,.42),'Panel_Steel')
    tube('Wall_conduit',[(s*2.73,-2.6,.3),(s*2.73,-1.4,.45),(s*2.73,1.7,.45),(s*2.69,2.4,1.05)],.032,'Edge_Alloy')
cyl('Front_floor_apron',(0,.10,-.075),3.88,.10,'Hull_Graphite',96)
# Segmented lower display housing closes the gap between deck and media surface.
for k in range(32):
    a=math.radians(-104+208*(k+.5)/32)
    o=box('Display_service_apron',(3.76*sin(a),3.76*cos(a),.255),(.45,.24,.61),'Hull_Graphite',.015); o.rotation_euler.z=-a
    o=box('Apron_inset',(3.61*sin(a),3.61*cos(a),.30),(.33,.018,.25),'Panel_Steel',.008); o.rotation_euler.z=-a
    if k%4==0:
        o=box('Apron_warning',(3.592*sin(a),3.592*cos(a),.30),(.15,.023,.032),'Warning_Ochre',.002); o.rotation_euler.z=-a
# Temporary camera-feed texture: procedural skyline test image, replaceable PNG.
W,H=1536,768
buf=bytearray(W*H*4)
for yy in range(H):
    t=yy/H
    for xx in range(W):
        cloud=(sin(xx*.013+sin(yy*.02)*2)+sin(xx*.027+yy*.019))*.013
        col=(.14+.11*(1-t)+cloud,.23+.16*(1-t)+cloud,.28+.18*(1-t)+cloud)
        if yy>H*.59: col=(.10,.17,.20)
        if yy%64==0 or xx%96==0: col=tuple(v+.026 for v in col)
        i=(yy*W+xx)*4; buf[i:i+4]=bytes([max(0,min(255,int(v*255))) for v in col]+[255])
def rect(x0,y0,x1,y1,c):
    c=bytes(c)
    for yy in range(max(0,int(y0)),min(H,int(y1))):
        for xx in range(max(0,int(x0)),min(W,int(x1))):
            i=(yy*W+xx)*4; buf[i:i+4]=c
for layer in range(3):
    for k in range(90):
        x=random.randrange(W); w=random.randrange(8,34); bottom=int(H*(.60+layer*.055)); h=random.randrange(10,90+layer*10)
        rect(x,bottom-h,x+w,bottom,(29-layer*5,49-layer*6,59-layer*6,255))
        for yy in range(bottom-h+8,bottom-3,12):
            for xx in range(x+4,x+w-3,8):
                if random.random()>.4: rect(xx,yy,xx+2,yy+2,(73,107,115,255))
rect(0,455,W,457,(67,113,124,255))
for x in range(0,W,96): rect(x,451,x+1,463,(101,155,161,255))
rect(W//2-22,H//2,W//2+22,H//2+1,(106,177,185,255)); rect(W//2,H//2-22,W//2+1,H//2+22,(106,177,185,255))
def chunk(tag,data): return struct.pack('!I',len(data))+tag+data+struct.pack('!I',zlib.crc32(tag+data)&0xffffffff)
png=b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('!2I5B',W,H,8,6,0,0,0))+chunk(b'IDAT',zlib.compress(b''.join(b'\0'+buf[y*W*4:(y+1)*W*4] for y in range(H)),7))+chunk(b'IEND',b'')
texture=os.path.join(OUT,'TemporaryCameraFeed.png'); open(texture,'wb').write(png)
img=bpy.data.images.load(texture); nodes=M['Display_Feed'].node_tree.nodes; links=M['Display_Feed'].node_tree.links
tex=nodes.new('ShaderNodeTexImage'); tex.image=img; bs=nodes.get('Principled BSDF'); links.new(tex.outputs['Color'],bs.inputs['Base Color']); links.new(tex.outputs['Color'],bs.inputs['Emission Color']); img.pack()
current=groups['10_Display']
R=4.05; CENTER=(0,0,1.8); amin,amax=math.radians(-106),math.radians(106); emin,emax=math.radians(-18),math.radians(27)
def dome(a,e,r=R): return (r*cos(e)*sin(a),r*cos(e)*cos(a),1.8+r*sin(e))
verts=[]; uvs=[]; faces=[]; NA,NE=128,28
for j in range(NE+1):
    e=emin+(emax-emin)*j/NE
    for i in range(NA+1):
        a=amin+(amax-amin)*i/NA; verts.append(dome(a,e)); uvs.append((i/NA,j/NE))
for j in range(NE):
    for i in range(NA):
        n=j*(NA+1)+i; faces.append((n,n+1,n+NA+2,n+NA+1))
mesh=bpy.data.meshes.new('DomeUV'); mesh.from_pydata(verts,[],faces); mesh.update(); uv=mesh.uv_layers.new(name='UVMap')
for poly in mesh.polygons:
    poly.use_smooth=True
    for li in poly.loop_indices: uv.data[li].uv=uvs[mesh.loops[li].vertex_index]
o=bpy.data.objects.new('Display_212deg_ReplaceableFeed',mesh); current.objects.link(o); mesh.materials.append(M['Display_Feed'])
# normals of parametric patch are inward (verified below).
for e,name in [(emin,'Display_lower_bezel'),(emax,'Display_upper_bezel')]:
    tube(name,[dome(amin+(amax-amin)*i/32,e,R-.02) for i in range(33)],.055,'Hull_Graphite')
for a in [amin,amax]: tube('Display_end_bezel',[dome(a,emin+(emax-emin)*i/8,R-.02) for i in range(9)],.06,'Hull_Graphite')
text('Display_label','CAMERA ARRAY / TEST FEED',(0,3.58,.48),.058,'Signal_Cyan')
# Semantic anchors remain visible as empties in source, added as transforms in Unity.
for station_group in ['02_Station_L','03_Station_R']:
    for ob in groups[station_group].objects: ob.location.z += PAD_TOP-.04
for ob in groups['08_Ceiling_Removable'].objects: ob.location.z += CEILING-3.50
current=groups['11_PreviewOnly']
for s,label in [(-1,'Left'),(1,'Right')]:
    e=bpy.data.objects.new(label+'_Eye',None); current.objects.link(e); e.location=(s*SPACING/2,0,PAD_TOP+EYE_HEIGHT); e.empty_display_type='ARROWS'; e.empty_display_size=.2
def light(name,loc,color,power,size,target):
    ld=bpy.data.lights.new(name,'AREA'); ld.energy=power*.15; ld.color=color; ld.shape='DISK'; ld.size=size
    o=bpy.data.objects.new(name,ld); current.objects.link(o); o.location=loc; o.rotation_euler=(Vector(target)-o.location).to_track_quat('-Z','Y').to_euler()
light('Screen bounce',(0,2.7,2.5),(.49,.72,.90),1100,4,(0,0,1))
light('Overhead practical',(0,-.4,3.27),(.65,.82,.88),650,3,(0,0,0))
light('Rear inspection',(0,-3.3,2.7),(1,.73,.48),850,3,(0,.5,1))
light('Port fill',(-2.5,.8,2.6),(.25,.62,.8),350,2,(0,0,1))
light('Starboard fill',(2.5,.8,2.6),(.25,.62,.8),350,2,(0,0,1))
def camera(name,loc,target,lens):
    d=bpy.data.cameras.new(name); o=bpy.data.objects.new(name,d); current.objects.link(o); o.location=loc; o.rotation_euler=(Vector(target)-o.location).to_track_quat('-Z','Y').to_euler(); d.lens=lens; d.clip_start=.03; return o
cams=[camera('Overview',(0,-5.7,2.64),(0,.8,1.64),24),camera('LeftEye',(-SPACING/2,0,PAD_TOP+EYE_HEIGHT),(-.46,3,1.73),18),camera('RightEye',(SPACING/2,0,PAD_TOP+EYE_HEIGHT),(.46,3,1.73),18),camera('LeftEye_Companion',(-SPACING/2,0,PAD_TOP+EYE_HEIGHT),(1.05,.16,1.50),20)]
scene=bpy.context.scene; scene.unit_settings.system='METRIC'; scene.unit_settings.scale_length=1
scene.render.engine='CYCLES'; scene.cycles.samples=16; scene.cycles.use_denoising=True
scene.render.resolution_x=1200; scene.render.resolution_y=834; scene.render.resolution_percentage=100
scene.world.color=(.03,.03,.03); scene.world.node_tree.nodes.get('Background').inputs['Strength'].default_value=.04; scene.view_settings.view_transform='AgX'; scene.camera=cams[0]
scene['Design_notes']='v02 shared pilot space. No suspended rig, harness or side arm. 2.10m station spacing; 1.30m diameter floor markers, 0.04m high; eye 1.65m above markers. Low forward status displays only. Ceiling removable. Media separate.'
for a in bpy.context.screen.areas if bpy.context.screen else []:
    if a.type=='VIEW_3D': a.spaces.active.region_3d.view_distance=8
bpy.context.preferences.filepaths.save_version=0
# Prefer GPU rendering where available, otherwise use CPU.
try:
    cp=bpy.context.preferences.addons['cycles'].preferences; cp.compute_device_type='OPTIX'; cp.get_devices()
    gpu=[d for d in cp.devices if d.type=='OPTIX']
    if gpu:
        for d in cp.devices: d.use=(d.type=='OPTIX')
        scene.cycles.device='GPU'; print('RENDER_DEVICE_OPTIX', [d.name for d in gpu], flush=True)
except Exception as err: print('CPU_RENDER_FALLBACK',str(err),flush=True)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT,'Anseong_Cockpit_v02.blend'))
# Export a copy, joined per module/material for predictable Unity draw calls.
bpy.ops.object.select_all(action='DESELECT'); export_objects=[]
for name,col in groups.items():
    if name=='11_PreviewOnly': continue
    originals=[o for o in col.objects if o.type in {'MESH','CURVE','FONT'}]
    copies=[]
    for src in originals:
        o=src.copy(); o.data=src.data.copy(); base.objects.link(o); copies.append(o)
    for o in copies:
        bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active=o
        bpy.ops.object.convert(target='MESH')
    bpy.ops.object.select_all(action='DESELECT')
    for o in copies: o.select_set(True)
    bpy.context.view_layer.objects.active=copies[0]; bpy.ops.object.join(); merged=bpy.context.object; merged.name=name
    bpy.context.scene.cursor.location=(0,0,0); bpy.ops.object.origin_set(type='ORIGIN_CURSOR'); export_objects.append(merged)
bpy.ops.object.select_all(action='DESELECT')
for o in export_objects: o.select_set(True)
bpy.context.view_layer.objects.active=export_objects[0]
bpy.ops.export_scene.fbx(filepath=os.path.join(OUT,'Anseong_Cockpit_v02.fbx'),use_selection=True,object_types={'MESH'},apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',axis_forward='-Z',axis_up='Y',bake_space_transform=True,use_mesh_modifiers=True,mesh_smooth_type='FACE',add_leaf_bones=False,path_mode='STRIP',bake_anim=False)
triangles=sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in export_objects)
report=dict(materials=meta,modules=len(export_objects),triangles=triangles,stationSpacing=SPACING,padRadius=PAD_RADIUS,padTop=PAD_TOP,eyeHeight=EYE_HEIGHT,ceilingHeight=CEILING,displayDegrees=212)
open(os.path.join(OUT,'cockpit_manifest.json'),'w').write(json.dumps(report,indent=2))
print('COCKPIT_EXPORT_OK',json.dumps(report))

for ob in export_objects: bpy.data.objects.remove(ob,do_unlink=True)
for cam in cams:
    scene.camera=cam; scene.render.filepath=os.path.join(OUT,cam.name+'.png'); bpy.ops.render.render(write_still=True)
