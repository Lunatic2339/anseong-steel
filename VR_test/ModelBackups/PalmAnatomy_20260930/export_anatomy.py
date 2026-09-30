import bpy,bmesh,json,math,array,copy,ast,os
from mathutils import Vector,geometry
from mathutils.bvhtree import BVHTree
from io_scene_fbx import parse_fbx,encode_bin
OUT=r'C:\Windows\Temp\palm_anatomy'
SRC=r'D:\UnityProject\VR_test\ModelBackups\PalmRepair_20260930_1929\mecha_arms_PCVR.original.fbx'
tree=ast.parse(open(r'C:\Windows\System32\palm_repair\repair.py').read());exec(compile(ast.Module(body=[n for n in tree.body if isinstance(n,ast.FunctionDef) and n.name in ['child','encode','equal']],type_ignores=[]),'helpers','exec'))
root,version=parse_fbx.parse(SRC);before=copy.deepcopy(root);objects=child(root,b'Objects').elems
data=json.load(open(OUT+'/anatomy_geometry.json'));geos=[o for o in objects if o.id==b'Geometry'];changed=set();report={}
bpy.ops.wm.open_mainfile(filepath=OUT+'/original.blend')
for side,geo in zip(['L','R'],geos):
 d=data[side];mesh=bpy.data.objects['MechaArm.'+side].data;remove=set(d['remove_polygons']);positions=[v.co[:] for v in mesh.vertices];polys=[list(p.vertices) for p in mesh.polygons];sel=[i for i in range(len(polys)) if i in remove]
 bvh=BVHTree.FromPolygons(positions,[polys[i] for i in sel],all_triangles=True)
 base=len(positions);orig_uv=mesh.uv_layers.active.data
 pvi=child(geo,b'PolygonVertexIndex').props[0];assert len(pvi)==len(polys)*3
 keep=[i for i in range(len(polys)) if i not in remove]
 loopkeep=[i*3+j for i in keep for j in range(3)]
 def filter_arr(elem,indices,chunk=1):
  old=elem.props[0];elem.props[0]=array.array(old.typecode,(old[i*chunk+j] for i in indices for j in range(chunk)));return elem.props[0]
 pvi=filter_arr(child(geo,b'PolygonVertexIndex'),loopkeep)
 ni=filter_arr(child(child(geo,b'LayerElementNormal'),b'NormalsIndex'),loopkeep)
 ui=filter_arr(child(child(geo,b'LayerElementUV'),b'UVIndex'),loopkeep)
 smooth=filter_arr(child(child(geo,b'LayerElementSmoothing'),b'Smoothing'),keep)
 tangent=filter_arr(child(child(geo,b'LayerElementTangent'),b'Tangents'),loopkeep,3)
 binormal=filter_arr(child(child(geo,b'LayerElementBinormal'),b'Binormals'),loopkeep,3)
 normals=child(child(geo,b'LayerElementNormal'),b'Normals').props[0];uvs=child(child(geo,b'LayerElementUV'),b'UV').props[0];verts=child(geo,b'Vertices').props[0]
 for p in d['vertices']:verts.extend(p)
 transferred=0
 for f in d['faces']:
  a,b,c=[base+i for i in f];pvi.extend([a,b,-c-1]);smooth.append(1)
  center=sum((Vector(d['vertices'][i]) for i in f),Vector())/3
  loc,no,index,dist=bvh.find_nearest(center)
  poly=mesh.polygons[sel[index]];pn=(Vector(d['vertices'][f[1]])-Vector(d['vertices'][f[0]])).cross(Vector(d['vertices'][f[2]])-Vector(d['vertices'][f[0]])).normalized()
  # A continuous graphite surface avoids projecting disconnected armor atlas
  # islands onto the reconstructed anatomical palm and finger roots.
  transfer=False
  if transfer:transferred+=1
  for i in f:
   p=Vector(d['vertices'][i]);n=Vector(d['normals'][i]);ni.append(len(normals)//3);normals.extend(n)
   if transfer:
    abc=[Vector(positions[v]) for v in poly.vertices];uvabc=[Vector((*orig_uv[l].uv,0)) for l in poly.loop_indices]
    uv=geometry.barycentric_transform(p,*abc,*uvabc)
    # Keep transfer near the source triangle to avoid unrelated texture islands.
    uv.x=max(min(x.x for x in uvabc),min(max(x.x for x in uvabc),uv.x));uv.y=max(min(x.y for x in uvabc),min(max(x.y for x in uvabc),uv.y))
   else:uv=Vector((.584+(p.y-.05)*.11,.382+(p.z-.13)*.11,0))
   ui.append(len(uvs)//2);uvs.extend(uv[:2]);t=n.cross(Vector((0,0,1)))
   if t.length<.001:t=n.cross(Vector((0,1,0)))
   t.normalize();tangent.extend(t);binormal.extend(n.cross(t))
 edges=child(geo,b'Edges');seen=set();outedges=[]
 for start in range(0,len(pvi),3):
  poly=[x if x>=0 else -x-1 for x in pvi[start:start+3]]
  for j,a in enumerate(poly):
   key=tuple(sorted((a,poly[(j+1)%3])))
   if key not in seen:seen.add(key);outedges.append(start+j)
 edges.props[0]=array.array(edges.props[0].typecode,outedges)
 names=set(n for w in d['weights'] for n in w)
 for name in names:
  cluster=next(o for o in objects if o.id==b'Deformer' and o.props[-1]==b'Cluster' and o.props[1].split(b'\0')[0].decode()==name and any(c.id==b'Indexes' for c in o.elems))
  changed.add(cluster.props[0]);idx=child(cluster,b'Indexes').props[0];ws=child(cluster,b'Weights').props[0]
  for i,w in enumerate(d['weights']):
   if w.get(name,0)>0:idx.append(base+i);ws.append(w[name])
 report[side]={'removed_hand_triangles':len(remove),'new_hand_triangles':len(d['faces']),'new_vertices':len(d['vertices']),'original_vertex_count':base,'single_component':len(d['component_sizes'])==1,'boundary_edges':d['boundary_edges'],'nonmanifold_edges':d['nonmanifold_edges'],'euler':d['euler'],'texture_transferred_triangles':transferred}
DEST=OUT+'/mecha_arms_PCVR.fbx';encode_bin.write(DEST,encode(root),version)
check,_=parse_fbx.parse(DEST)
for a,b in zip(child(before,b'Objects').elems,child(check,b'Objects').elems):
 if a.id!=b'Geometry' and a.props[0] not in changed:equal(a,b)
 if a.props[0] in changed:
  for x,y in zip(a.elems,b.elems):
   if x.id in [b'Indexes',b'Weights']:assert list(x.props[0])==list(y.props[0][:len(x.props[0])])
   else:equal(x,y)
equal(child(before,b'Connections'),child(check,b'Connections'));equal(child(before,b'GlobalSettings'),child(check,b'GlobalSettings'))
bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.fbx(filepath=DEST)
rig=bpy.data.objects['Mecha_Armature'];rig.data.pose_position='REST'
for side in ['L','R']:
 o=bpy.data.objects['MechaArm.'+side];base=report[side]['original_vertex_count']
 # FBX keeps unused source vertices, so retained old vertex and skin IDs stay stable.
 assert len(o.data.vertices)==base+report[side]['new_vertices']
 assert all(abs(sum(g.weight for g in v.groups)-1)<1e-5 for v in o.data.vertices[base:])
 b=bmesh.new();b.from_mesh(o.data);b.verts.ensure_lookup_table();newverts=[v for v in b.verts if v.index>=base];newedges=set(e for v in newverts for e in v.link_edges);newfaces=set(f for v in newverts for f in v.link_faces)
 assert all(e.is_manifold for e in newedges)
 assert len(newverts)-len(newedges)+len(newfaces)==2
 b.free()
for im in bpy.data.images:
 if 'Mecha_BaseColor_' in im.name:
  s='R' if 'Mecha_BaseColor_R' in im.name else 'L';im.filepath=r'D:\UnityProject\VR_test\Assets\Textures\Mecha_BaseColor_'+s+'.png';im.reload()
bpy.ops.wm.save_as_mainfile(filepath=OUT+'/final.blend')
report['preserved']='Skeleton, bind transforms, animations, object/material IDs, original weights and non-hand geometry preserved. Replaced only hand/wrist/proximal-finger surfaces; new vertices have blended weights.'
json.dump(report,open(OUT+'/validation.json','w'),indent=2);print(json.dumps(report))
