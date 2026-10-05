import bpy,bmesh,json,array,copy,os
from mathutils import Vector
from io_scene_fbx import parse_fbx,encode_bin
OUT=r'C:\Windows\Temp\palm_repair'
SRC=r'D:\UnityProject\VR_test\Assets\mecha_arms_PCVR.fbx'
bpy.ops.wm.open_mainfile(filepath=OUT+'/imported.blend')
root,ver=parse_fbx.parse(SRC);original=copy.deepcopy(root)
def child(e,k):return next(c for c in e.elems if c.id==k)
geos=[e for e in child(root,b'Objects').elems if e.id==b'Geometry']
boundaries=json.load(open(OUT+'/boundaries.json'));report={}
for side,geo in zip(['L','R'],geos):
 ob=bpy.data.objects['MechaArm.'+side];mesh=ob.data
 raw=child(geo,b'Vertices').props[0]
 assert len(raw)==len(mesh.vertices)*3
 assert max((v.co-Vector(raw[v.index*3:v.index*3+3])).length for v in mesh.vertices)<1e-5
 loop=max([x for x in boundaries[ob.name] if x['groups']==['Hand.'+side]],key=lambda x:x['edges'])
 assert loop['edges']==len(loop['ids']) and loop['edges'] in (36,37)
 bm=bmesh.new();bm.from_mesh(mesh);bm.verts.ensure_lookup_table()
 ids=set(loop['ids']);edges=[e for e in bm.edges if e.is_boundary and all(v.index in ids for v in e.verts)]
 assert len(edges)==loop['edges']
 fs=bmesh.ops.holes_fill(bm,edges=edges,sides=0)['faces'];assert len(fs)==1
 tris=bmesh.ops.triangulate(bm,faces=fs)['faces'];bm.normal_update()
 # Preserve boundary winding; the cap faces point toward the palm interior side.
 direction=-1 if side=='L' else 1
 assert sum(f.normal.x*f.calc_area() for f in tris)*direction>0
 uv=bm.loops.layers.uv.active
 for f in tris:
  f.smooth=True
  for l in f.loops:
   # A small dark graphite region in the existing atlas, no new material or texture.
   l[uv].uv=(.584+(l.vert.co.y-.055)*.15,.382+(l.vert.co.z-.115)*.15)
 bm.normal_update()
 added=[{'ids':[v.index for v in f.verts],'normal':list(f.normal),'normals':[list(v.normal) for v in f.verts],'uv':[list(l[uv].uv) for l in f.loops]} for f in tris]
 assert all(not e.is_boundary for e in edges)
 oldfaces=len(mesh.polygons)
 bm.to_mesh(mesh);bm.free();mesh.update()
 pvi=child(geo,b'PolygonVertexIndex').props[0]
 normals=child(child(geo,b'LayerElementNormal'),b'Normals').props[0]
 ni=child(child(geo,b'LayerElementNormal'),b'NormalsIndex').props[0]
 uvs=child(child(geo,b'LayerElementUV'),b'UV').props[0]
 ui=child(child(geo,b'LayerElementUV'),b'UVIndex').props[0]
 smoothing=child(child(geo,b'LayerElementSmoothing'),b'Smoothing').props[0]
 tangents=child(child(geo,b'LayerElementTangent'),b'Tangents').props[0]
 binormals=child(child(geo,b'LayerElementBinormal'),b'Binormals').props[0]
 for f in added:
  a,b,c=f['ids'];pvi.extend([a,b,-c-1]);n=Vector(f['normal'])
  for vn in f['normals']:ni.append(len(normals)//3);normals.extend(vn)
  t=(Vector((0,1,0))-n*n.y).normalized();bi=n.cross(t)
  for pair in f['uv']:ui.append(len(uvs)//2);uvs.extend(pair);tangents.extend(t);binormals.extend(bi)
  smoothing.append(1)
 # Rebuild edge references in polygon order, keeping original entries intact.
 seen=set();all_edges=[];start=0
 for i,v in enumerate(pvi):
  if v<0:
   poly=[x if x>=0 else -x-1 for x in pvi[start:i+1]]
   for j,a in enumerate(poly):
    key=tuple(sorted((a,poly[(j+1)%len(poly)])))
    if key not in seen:seen.add(key);all_edges.append(start+j)
   start=i+1
 edgearr=child(geo,b'Edges').props[0];assert all_edges[:len(edgearr)]==list(edgearr)
 edgearr.extend(all_edges[len(edgearr):])
 report[ob.name]={'added_triangles':len(added),'closed_boundary_edges':len(edges),'original_vertices':len(mesh.vertices),'old_triangles':oldfaces}
def encode(e):
 out=encode_bin.FBXElem(e.id)
 methods={'Y':'add_int16','C':'add_bool','I':'add_int32','L':'add_int64','F':'add_float32','D':'add_float64','S':'add_string','R':'add_bytes','f':'add_float32_array','d':'add_float64_array','l':'add_int64_array','i':'add_int32_array','b':'add_bool_array','c':'add_byte_array','B':'add_int8','Z':'add_char'}
 methods.update(C='add_char',B='add_bool',Z='add_int8')
 for t,p in zip(e.props_type,e.props):
  getattr(out,methods[chr(t)])(p)
 out.elems=[encode(c) for c in e.elems];return out
DEST=OUT+'/mecha_arms_PCVR_repaired.fbx'
encode_bin.write(DEST,encode(root),ver)
new,_=parse_fbx.parse(DEST)
def equal(a,b):
 assert a.id==b.id and a.props_type==b.props_type and len(a.props)==len(b.props) and len(a.elems)==len(b.elems)
 for x,y in zip(a.props,b.props):assert x==y
 for x,y in zip(a.elems,b.elems):equal(x,y)
# All model, skeleton, skin, material, animation and connection records remain identical.
for a,b in zip(child(original,b'Objects').elems,child(new,b'Objects').elems):
 if a.id!=b'Geometry':equal(a,b)
equal(child(original,b'Connections'),child(new,b'Connections'))
equal(child(original,b'GlobalSettings'),child(new,b'GlobalSettings'))
report['preserved']='All non-geometry Objects, Connections and GlobalSettings identical; original vertices and all existing face/UV/normal data retained.'
bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.fbx(filepath=DEST)
for side in ['L','R']:
 ob=bpy.data.objects['MechaArm.'+side];assert len(ob.data.polygons)==report[ob.name]['old_triangles']+report[ob.name]['added_triangles']
 bm=bmesh.new();bm.from_mesh(ob.data);bm.verts.ensure_lookup_table()
 ids=set(max([x for x in boundaries[ob.name] if x['groups']==['Hand.'+side]],key=lambda x:x['edges'])['ids'])
 assert not any(e.is_boundary and all(v.index in ids for v in e.verts) for e in bm.edges)
 bm.free()
for im in bpy.data.images:
 if 'Mecha_BaseColor_' in im.name:
  suffix='R' if 'Mecha_BaseColor_R' in im.name else 'L'
  im.filepath=r'D:\UnityProject\VR_test\Assets\Textures\Mecha_BaseColor_'+suffix+'.png';im.reload()
bpy.ops.wm.save_as_mainfile(filepath=OUT+'/repaired.blend')
open(OUT+'/validation.json','w').write(json.dumps(report,indent=2));print(json.dumps(report))
