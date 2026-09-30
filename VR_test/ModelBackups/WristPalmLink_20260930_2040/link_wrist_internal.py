import bpy,bmesh,json,array,copy,os,ast
from mathutils import Vector,geometry
from io_scene_fbx import parse_fbx,encode_bin
OUT=r'C:\Windows\Temp\wrist_palm_link'
SRC=r'C:\Windows\Temp\palm_only\mecha_arms_PCVR_repaired.fbx'
tree=ast.parse(open(r'C:\Windows\System32\palm_repair\repair.py').read());exec(compile(ast.Module(body=[n for n in tree.body if isinstance(n,ast.FunctionDef) and n.name in ['child','encode','equal']],type_ignores=[]),'helpers','exec'))
root,ver=parse_fbx.parse(SRC);original=copy.deepcopy(root)
bpy.ops.wm.open_mainfile(filepath=r'C:\Windows\Temp\palm_only\repaired.blend')
boundary=json.load(open(OUT+'/wrist_boundary.json'));report={}
for side,geo in zip(['L','R'],[g for g in child(root,b'Objects').elems if g.id==b'Geometry']):
 s=1 if side=='L' else -1;ob=bpy.data.objects['MechaArm.'+side];mesh=ob.data
 bm=bmesh.new();bm.from_mesh(mesh);bm.verts.ensure_lookup_table();oldfaces=set(bm.faces);base=len(bm.verts)
 keys={tuple(sorted(e)) for e in boundary[side]['edges']}
 edges=[e for e in bm.edges if tuple(sorted(v.index for v in e.verts)) in keys]
 assert all(e.is_boundary for e in edges)
 # Close the wrist opening directly to an existing inner-palm vertex.
 # The attachment sits inside the palm; no exterior overlay is created.
 original_count=2596 if side=='L' else 2603
 candidates=[v for v in bm.verts if v.index>=original_count and .105<v.co.z<.126]
 target=min(candidates,key=lambda v:(v.co.y-.050)**2+(v.co.z-.116)**2)
 fs=bmesh.ops.holes_fill(bm,edges=edges,sides=0)['faces']
 large=max(fs,key=lambda f:len(f.verts));vs=list(large.verts);bm.faces.remove(large)
 for j in range(len(vs)):bm.faces.new([vs[j],vs[(j+1)%len(vs)],target])
 bmesh.ops.triangulate(bm,faces=[f for f in bm.faces if f not in oldfaces])
 bm.verts.index_update();bm.normal_update()
 assert all(not e.is_boundary for e in edges)
 arc=[];palmedges=[]
 added=[f for f in bm.faces if f not in oldfaces];newverts=[v for v in bm.verts if v.index>=base]
 raw=child(geo,b'Vertices').props[0]
 for v in newverts:raw.extend(v.co)
 cluster=next(o for o in child(root,b'Objects').elems if o.id==b'Deformer' and o.props[-1]==b'Cluster' and o.props[1].split(b'\0')[0].decode()=='Hand.'+side and any(c.id==b'Indexes' for c in o.elems))
 for v in newverts:child(cluster,b'Indexes').props[0].append(v.index);child(cluster,b'Weights').props[0].append(1.)
 pvi=child(geo,b'PolygonVertexIndex').props[0];normals=child(child(geo,b'LayerElementNormal'),b'Normals').props[0];ni=child(child(geo,b'LayerElementNormal'),b'NormalsIndex').props[0]
 uvs=child(child(geo,b'LayerElementUV'),b'UV').props[0];ui=child(child(geo,b'LayerElementUV'),b'UVIndex').props[0];smooth=child(child(geo,b'LayerElementSmoothing'),b'Smoothing').props[0]
 tangents=child(child(geo,b'LayerElementTangent'),b'Tangents').props[0];binormals=child(child(geo,b'LayerElementBinormal'),b'Binormals').props[0]
 source_normals={}
 for p in mesh.polygons:
  for li in p.loop_indices:
   source_normals.setdefault(mesh.loops[li].vertex_index,[]).append(mesh.corner_normals[li].vector.copy())
 # Match shading to the existing wrist and palm without changing their normals.
 for f in added:
  a,b,c=[v.index for v in f.verts];pvi.extend([a,b,-c-1]);smooth.append(1)
  for v in f.verts:
   nn=sum(source_normals[v.index],Vector()).normalized()
   ni.append(len(normals)//3);normals.extend(nn)
   ui.append(len(uvs)//2);uvs.extend((.584+(v.co.y-.055)*.15,.382+(v.co.z-.115)*.15))
   t=nn.cross(Vector((0,0,1)))
   if t.length<1e-5:t=nn.cross(Vector((0,1,0)))
   t.normalize();tangents.extend(t);binormals.extend(nn.cross(t))
 seen=set();outedges=[]
 for start in range(0,len(pvi),3):
  poly=[x if x>=0 else -x-1 for x in pvi[start:start+3]]
  for j,a in enumerate(poly):
   key=tuple(sorted((a,poly[(j+1)%3])))
   if key not in seen:seen.add(key);outedges.append(start+j)
 ea=child(geo,b'Edges').props[0];assert list(ea)==outedges[:len(ea)];ea.extend(outedges[len(ea):])
 report[side]={'sealed_wrist_boundary_edges':len(edges),'shared_palm_attachment_vertex':target.index,'added_vertices':len(newverts),'added_triangles':len(added)}
 bm.free()
DEST=OUT+'/mecha_arms_PCVR.fbx';encode_bin.write(DEST,encode(root),ver)
check,_=parse_fbx.parse(DEST)
def preserved(x,y):
 assert x.id==y.id and x.props_type==y.props_type and len(x.props)==len(y.props) and len(x.elems)==len(y.elems)
 for p,q in zip(x.props,y.props):
  if isinstance(p,array.array):assert list(p)==list(q[:len(p)])
  else:assert p==q
 for p,q in zip(x.elems,y.elems):preserved(p,q)
preserved(original,check)
report['preserved']='Every existing property and array prefix of the palm-only FBX verified unchanged. Only inner wrist closure faces connecting to an existing palm vertex appended. Original geometry, UVs, normals, skin, bones and materials preserved.'
bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.fbx(filepath=DEST)
for side in ['L','R']:
 o=bpy.data.objects['MechaArm.'+side];bm=bmesh.new();bm.from_mesh(o.data);bm.verts.ensure_lookup_table()
 keys={tuple(sorted(e)) for e in boundary[side]['edges']}
 assert all(not e.is_boundary for e in bm.edges if tuple(sorted(v.index for v in e.verts)) in keys)
 assert all(abs(sum(g.weight for g in v.groups)-1)<1e-5 for v in o.data.vertices[-report[side]['added_vertices']:])
 bm.free()
for im in bpy.data.images:
 if 'Mecha_BaseColor_' in im.name:
  s='R' if 'Mecha_BaseColor_R' in im.name else 'L';im.filepath=r'D:\UnityProject\VR_test\Assets\Textures\Mecha_BaseColor_'+s+'.png';im.reload()
bpy.ops.wm.save_as_mainfile(filepath=OUT+'/repaired.blend')
json.dump(report,open(OUT+'/validation.json','w'),indent=2);print(json.dumps(report))

