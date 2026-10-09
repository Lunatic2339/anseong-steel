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

VARIANT = sys.argv[sys.argv.index('--')+2] if '--' in sys.argv else 'A_CloseArc'
cfg = {
 'A_CloseArc': dict(radius=2.65, wide=1.08, amin=-104, amax=104, emin=-32, emax=43, support='deck'),
 'B_RearDome': dict(radius=2.65, wide=1.0, amin=-90, amax=90, emin=-86, emax=86, support='rear'),
 'C_SideDome': dict(radius=2.25, wide=1.24, amin=-90, amax=90, emin=-74, emax=79, support='side'),
}[VARIANT]
R = cfg['radius']; RX = R*cfg['wide']; ZC = PAD_TOP+EYE_HEIGHT
amin,amax,emin,emax = [math.radians(cfg[k]) for k in ('amin','amax','emin','emax')]
def dome(a,e,r=R):
    return (r*cfg['wide']*cos(e)*sin(a), r*cos(e)*cos(a), ZC+r*sin(e))

# Shared dimensions and eye positions are held constant for honest comparisons.
current=groups['01_Deck']
if cfg['support']=='deck':
    box('Compact_deck',(0,-.02,-.12),(4.35,2.5,.24),'Hull_Graphite',.04)
    for ix in range(6):
        for iy in range(3):
            box('Deck_panel',(-1.8+ix*.72,-.87+iy*.78,.012),(.70,.75,.025),'Panel_Steel',.006)
    for side in [-1,1]:
        box('Deck_edge_light',(side*2.13,-.04,.035),(.022,2.36,.016),'Signal_Cyan',.003)
    text('Deck_ID','A / CLOSE ARC',(0,-1.08,.031),.12,rot=(0,0,0))
    # Close the forward floor and the small gap beneath the curved display.
    rim=[dome(amin+(amax-amin)*i/64,emin) for i in range(65)]
    floorverts=[(0,0,-.015)]+[(x,y,-.015) for x,y,z in rim]
    floorfaces=[(0,i+1,i+2) for i in range(64)]
    fm=bpy.data.meshes.new('Forward_floor'); fm.from_pydata(floorverts,[],floorfaces); fm.update()
    fo=bpy.data.objects.new('Forward_floor',fm); current.objects.link(fo); fm.materials.append(M['Panel_Steel'])
    for i in range(64):
        a,b=Vector(rim[i]),Vector(rim[i+1]); mid=(a+b)/2
        o=box('Lower_screen_housing',(mid.x,mid.y,mid.z/2-.015),((b-a).length+.012,.07,mid.z+.03),'Hull_Graphite',.004)
        o.rotation_euler.z=math.atan2(b.y-a.y,b.x-a.x)
else:
    # Two narrow platforms; the display continues visibly below the feet.
    for side in [-1,1]:
        x=side*SPACING/2
        cyl('Platform_disc',(x,0,-.07),.73,.21,'Hull_Graphite',64)
        ring('Platform_edge',(x,0,.03),.70,.024,'Edge_Alloy')
        if cfg['support']=='rear':
            box('Rear_cantilever_deck',(x,-.89,-.10),(1.14,1.50,.20),'Panel_Steel',.025)
            for dx in [-.43,.43]:
                beam('Load_bearing_rear_strut',(x+dx,-1.60,-.83),(x+dx,.22,-.19),.085,'Edge_Alloy')
            box('Rear_anchor_plate',(x,-1.62,-.12),(1.32,.18,1.72),'Hull_Graphite',.035)
            for dx in [-.43,.43]:
                box('Walkway_edge',(x+dx,-.90,.018),(.025,1.38,.016),'Signal_Amber',.003)
        else:
            # The only load paths run outwards to lateral wall segments behind the display edge.
            box('Lateral_cantilever',(side*1.98,-.58,-.12),(1.7,.64,.24),'Panel_Steel',.03)
            for yy in [-.77,-.39]:
                beam('Side_load_strut',(side*2.79,yy,-.89),(side*1.20,yy,-.19),.085,'Edge_Alloy')
            box('Side_anchor_plate',(side*2.81,-.65,-.10),(.18,1.3,1.90),'Hull_Graphite',.035)
            box('Cantilever_edge_light',(side*2.00,-.89,.02),(1.47,.025,.018),'Signal_Amber',.003)

for side,label in [(-1,'L'),(1,'R')]:
    current=groups['02_Station_L' if side<0 else '03_Station_R']; x=side*SPACING/2
    cyl('Station_tread_'+label,(x,0,.021),PAD_RADIUS,.038,'Rubber',64)
    ring('Station_ring_'+label,(x,0,.04),PAD_RADIUS-.025,.010,'Signal_Cyan')
    for q in range(-7,8):
        yy=q*.065; half=math.sqrt(max(0,(PAD_RADIUS-.1)**2-yy**2))
        box('Tread',(x,yy,.042),(2*half,.014,.006),'Panel_Steel',.002)
    text('Station_ID_'+label,'0'+('1' if side<0 else '2')+' / '+label,(x,-.37,.05),.105,rot=(0,0,0))
    current=groups['04_Console_L' if side<0 else '05_Console_R']
    # Small passive readouts remain below the forward view, within the platform footprint.
    box('Readout_stalk',(x,.57,.25),(.08,.08,.42),'Hull_Graphite')
    box('Readout_housing',(x,.57,.50),(.40,.10,.18),'Panel_Steel',.015)
    box('Readout_face',(x,.511,.50),(.35,.012,.13),'Screen_UI',.004)
    text('Readout_text','LINK / '+label,(x,.503,.50),.047,'Signal_Cyan')

current=groups['09_Hull']
if cfg['support']=='deck':
    for side in [-1,1]:
        box('Short_side_sill',(side*2.19,-.18,.33),(.15,2.12,.66),'Hull_Graphite',.025)
        beam('Rear_frame',(side*2.16,-1.15,.2),(side*2.16,-1.15,3.22),.085,'Edge_Alloy')
else:
    for side in [-1,1]:
        if cfg['support']=='rear':
            box('Rear_wall_section',(side*1.20,-1.77,.70),(1.85,.20,3.50),'Hull_Graphite',.035)
            box('Rear_wall_inset',(side*1.20,-1.65,1.35),(1.42,.035,1.16),'Panel_Steel',.015)
            box('Rear_wall_light',(side*1.97,-1.64,1.18),(.035,.028,2.2),'Signal_Cyan',.004)
        else:
            box('Side_wall_section',(side*2.96,-.98,.67),(.20,1.9,3.60),'Hull_Graphite',.035)
            box('Side_wall_inset',(side*2.845,-.98,1.22),(.035,1.46,1.26),'Panel_Steel',.015)
            box('Side_wall_light',(side*2.82,-1.78,1.21),(.024,.035,2.32),'Signal_Cyan',.004)

if cfg['support']=='deck':
    current=groups['08_Ceiling_Removable']
    box('Short_roof',(0,-.20,3.31),(4.54,2.2,.12),'Hull_Graphite',.03)
    for y in [-1.15,-.1,.78]: box('Roof_rib',(0,y,3.20),(4.48,.09,.12),'Edge_Alloy')
    for side in [-1,1]: box('Roof_light',(side*1.72,-.18,3.18),(.06,1.6,.03),'Signal_Cyan')
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
NA,NE=144,80
verts=[]; uvs=[]; faces=[]
for j in range(NE+1):
    e=emin+(emax-emin)*j/NE
    for i in range(NA+1):
        a=amin+(amax-amin)*i/NA
        verts.append(dome(a,e)); uvs.append((i/NA,j/NE))
for j in range(NE):
    for i in range(NA):
        n=j*(NA+1)+i; faces.append((n,n+1,n+NA+2,n+NA+1))
mesh=bpy.data.meshes.new('Inward_display_UV'); mesh.from_pydata(verts,[],faces); mesh.update()
uv=mesh.uv_layers.new(name='UVMap')
for poly in mesh.polygons:
    poly.use_smooth=True
    for li in poly.loop_indices: uv.data[li].uv=uvs[mesh.loops[li].vertex_index]
display=bpy.data.objects.new('Continuous_display_'+VARIANT,mesh); current.objects.link(display); mesh.materials.append(M['Display_Feed'])
for e,label in [(emin,'Lower'),(emax,'Upper')]:
    tube(label+'_perimeter',[dome(amin+(amax-amin)*i/64,e,R-.02) for i in range(65)],.038,'Hull_Graphite')
for a in [amin,amax]:
    tube('Side_perimeter',[dome(a,emin+(emax-emin)*i/48,R-.02) for i in range(49)],.04,'Hull_Graphite')
    tube('Perimeter_light',[dome(a,emin+(emax-emin)*i/48,R-.045) for i in range(49)],.007,'Signal_Cyan')

current=groups['11_PreviewOnly']
for side,label in [(-1,'Left'),(1,'Right')]:
    anchor=bpy.data.objects.new(label+'_Eye',None); current.objects.link(anchor); anchor.location=(side*SPACING/2,0,ZC)
    anchor.empty_display_type='ARROWS'; anchor.empty_display_size=.2
def light(name,loc,power,color,size,target):
    d=bpy.data.lights.new(name,'AREA'); d.energy=power; d.color=color; d.shape='DISK'; d.size=size
    o=bpy.data.objects.new(name,d); current.objects.link(o); o.location=loc; o.rotation_euler=(Vector(target)-o.location).to_track_quat('-Z','Y').to_euler()
light('Screen_fill',(0,1.55,2.35),180,(.5,.76,1),3,(0,0,.2))
light('Rear_fill',(0,-1.1,3),170,(1,.72,.45),3,(0,0,0))
light('Platform_fill',(0,-.9,.9),70,(.45,.72,1),2,(0,0,-.5))
def camera(name,loc,target,lens):
    d=bpy.data.cameras.new(name); o=bpy.data.objects.new(name,d); current.objects.link(o)
    o.location=loc; o.rotation_euler=(Vector(target)-o.location).to_track_quat('-Z','Y').to_euler(); d.lens=lens; d.clip_start=.02; return o
cams=[camera('Overview',(0,-5.7,2.64),(0,.8,1.64),24),
      camera('LeftEye',(-SPACING/2,0,ZC),(-.46,3,1.73),18),
      camera('RightEye',(SPACING/2,0,ZC),(.46,3,1.73),18),
      camera('LookDown',(-SPACING/2,0,ZC),(-.6,1,-.2),18)]
scene=bpy.context.scene; scene.unit_settings.system='METRIC'; scene.unit_settings.scale_length=1
scene.render.engine='CYCLES'; scene.cycles.samples=24; scene.cycles.use_denoising=True
scene.render.resolution_x=1200; scene.render.resolution_y=834; scene.render.resolution_percentage=100
scene.world.use_nodes=True; scene.world.node_tree.nodes.get('Background').inputs['Color'].default_value=(.025,.04,.055,1)
scene.world.node_tree.nodes.get('Background').inputs['Strength'].default_value=.12
scene.view_settings.view_transform='AgX'; scene.camera=cams[1]
scene['Design_notes']='Two standing pilots. No suspended harness or articulated arms. Preview feed only; XR integration pending.'
scene['Variant']=VARIANT
try:
    cp=bpy.context.preferences.addons['cycles'].preferences; cp.compute_device_type='OPTIX'; cp.get_devices()
    gpu=[d for d in cp.devices if d.type=='OPTIX']
    if gpu:
        for d in cp.devices: d.use=(d.type=='OPTIX')
        scene.cycles.device='GPU'
except Exception as err: print('GPU_FALLBACK',err,flush=True)
bpy.context.preferences.filepaths.save_version=0
for area in bpy.context.screen.areas if bpy.context.screen else []:
    if area.type=='VIEW_3D':
        area.spaces.active.region_3d.view_perspective='CAMERA'
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT,'Anseong_Cockpit_'+VARIANT+'.blend'))
# Export a copy, joined per module/material for predictable Unity draw calls.
bpy.ops.object.select_all(action='DESELECT'); export_objects=[]
for name,col in groups.items():
    if name=='11_PreviewOnly': continue
    originals=[o for o in col.objects if o.type in {'MESH','CURVE','FONT'}]
    if not originals: continue
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
bpy.ops.export_scene.fbx(filepath=os.path.join(OUT,'Anseong_Cockpit_'+VARIANT+'.fbx'),use_selection=True,object_types={'MESH'},apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',axis_forward='-Z',axis_up='Y',bake_space_transform=True,use_mesh_modifiers=True,mesh_smooth_type='FACE',add_leaf_bones=False,path_mode='STRIP',bake_anim=False)
triangles=sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in export_objects)

eye_forward=R*math.sqrt(1-(SPACING/2/RX)**2)
report=dict(variant=VARIANT,materials=meta,modules=len(export_objects),triangles=triangles,
 stationSpacing=SPACING,padRadius=PAD_RADIUS,padTop=PAD_TOP,eyeHeight=EYE_HEIGHT,
 displayRadius=R,displayWidthRadius=RX,displayDegrees=cfg['amax']-cfg['amin'],
 displayVerticalDegrees=cfg['emax']-cfg['emin'],forwardDistanceFromEachEye=eye_forward,support=cfg['support'],
 previewOnly=True,gameplayOrXRIncluded=False)
assert abs((Vector((SPACING/2,0,ZC))-Vector((-SPACING/2,0,ZC))).length-SPACING)<1e-6
assert all(p.normal.dot(p.center-Vector((0,0,ZC)))<0 for p in mesh.polygons), 'Display must face inward'
assert eye_forward<2.5
assert triangles<150000
open(os.path.join(OUT,'cockpit_manifest.json'),'w',encoding='utf-8').write(json.dumps(report,indent=2))
print('VARIANT_EXPORT_OK',json.dumps(report),flush=True)
for ob in export_objects: bpy.data.objects.remove(ob,do_unlink=True)
for cam in cams:
    # Rear structural wall is cut away only for the overview inspection image.
    cut=[o for o in groups['09_Hull'].objects if o.name.startswith('Rear_wall')]
    if cam.name=='Overview':
        for o in cut: o.hide_render=True
    scene.camera=cam; scene.render.filepath=os.path.join(OUT,cam.name+'.png'); bpy.ops.render.render(write_still=True)
    for o in cut: o.hide_render=False
print('VARIANT_RENDER_OK',VARIANT,flush=True)
