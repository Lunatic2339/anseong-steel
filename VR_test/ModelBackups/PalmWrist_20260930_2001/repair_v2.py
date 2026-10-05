import bpy,bmesh,json,math,array,copy,ast,os
from mathutils import Vector
from io_scene_fbx import parse_fbx,encode_bin
OUT=r'C:\Windows\Temp\palm_repair_v2';os.makedirs(OUT,exist_ok=True)
SRC=r'D:\UnityProject\VR_test\Assets\mecha_arms_PCVR.fbx'
tree=ast.parse(open(r'C:\Windows\System32\palm_repair\repair.py').read())
exec(compile(ast.Module(body=[n for n in tree.body if isinstance(n,ast.FunctionDef) and n.name in ['child','encode','equal']],type_ignores=[]),'helpers','exec'))
root,version=parse_fbx.parse(SRC);before=copy.deepcopy(root)
objs=child(root,b'Objects').elems;geos=[o for o in objs if o.id==b'Geometry']
report={};changed_clusters=set()
for side,geo in zip(['L','R'],geos):
 sign=1 if side=='L' else -1
 # Rounded palm shell blends into a complete wrist sleeve. Ends tuck inside
 # existing hand/forearm armor; the front surface stays above knuckle geometry.
 sections=[(.074,.458,.044,.053,.057),(.082,.452,.044,.053,.058),(.105,.443,.046,.053,.060),(.130,.435,.047,.052,.060),(.154,.430,.049,.050,.057),(.174,.428,.048,.047,.055),(.190,.423,.048,.047,.053),(.210,.415,.047,.048,.051),(.232,.407,.044,.049,.047)]
 n=32;verts=[];faces=[];lookup={};weights=[]
 for layer in range(2):
  for row,(z,xc,rx,yc,ry) in enumerate(sections):
   # Front hemisphere on lower palm; full circumference around wrist.
   indices=range(n) if row>=4 else range(n//4,3*n//4+1)
   for j in indices:
    a=2*math.pi*j/n;ca=math.cos(a);sa=math.sin(a)
    flat=max(0,1-(z-.154)/.04) if z>.154 else 1
    power=.4*flat+1*(1-flat)
    cx=math.copysign(abs(ca)**power,ca)
    thick=.0045*layer
    p=(sign*(xc+(rx-thick)*cx),yc+(ry-thick)*sa,z)
    lookup[layer,row,j]=len(verts);verts.append(p)
    t=max(0,min(1,(z-.154)/(.218-.154)));t=t*t*(3-2*t)
    weights.append(1-t)
  for row in range(len(sections)-1):
   for j in range(n):
    keys=[(layer,row,j),(layer,row,(j+1)%n),(layer,row+1,(j+1)%n),(layer,row+1,j)]
    if all(k in lookup for k in keys):
     face=[lookup[k] for k in keys]
     if layer:face.reverse()
     faces.append(face)
 # Close every outer-sheet boundary to its matching inner surface.
 counts={}
 for f in faces:
  if all(v<len(verts)//2 for v in f):
   for a,b in zip(f,f[1:]+f[:1]):
    key=tuple(sorted((a,b)));counts.setdefault(key,[]).append((a,b))
 half=len(verts)//2
 for occurrences in counts.values():
  if len(occurrences)==1:
   a,b=occurrences[0];faces.append([b,a,a+half,b+half])
 me=bpy.data.meshes.new('PalmWristPatch');me.from_pydata(verts,[],faces);me.update()
 bm=bmesh.new();bm.from_mesh(me);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bmesh.ops.triangulate(bm,faces=list(bm.faces));bm.normal_update();bm.verts.ensure_lookup_table()
 assert all(e.is_manifold for e in bm.edges),'Patch must be closed'
 patch=[{'ids':[v.index for v in f.verts],'normals':[list(v.normal) for v in f.verts],'normal':list(f.normal)} for f in bm.faces]
 raw=child(geo,b'Vertices').props[0];base=len(raw)//3;oldp=len(child(geo,b'PolygonVertexIndex').props[0])//3
 for p in verts:raw.extend(p)
 pvi=child(geo,b'PolygonVertexIndex').props[0];normals=child(child(geo,b'LayerElementNormal'),b'Normals').props[0];ni=child(child(geo,b'LayerElementNormal'),b'NormalsIndex').props[0]
 uv=child(child(geo,b'LayerElementUV'),b'UV').props[0];ui=child(child(geo,b'LayerElementUV'),b'UVIndex').props[0]
 sm=child(child(geo,b'LayerElementSmoothing'),b'Smoothing').props[0];ta=child(child(geo,b'LayerElementTangent'),b'Tangents').props[0];bi=child(child(geo,b'LayerElementBinormal'),b'Binormals').props[0]
 for f in patch:
  a,b,c=[base+i for i in f['ids']];pvi.extend([a,b,-c-1]);sm.append(1)
  for vid,vn in zip(f['ids'],f['normals']):
   ni.append(len(normals)//3);normals.extend(vn);p=verts[vid]
   ui.append(len(uv)//2);uv.extend([.584+(p[1]-.05)*.11,.382+(p[2]-.14)*.11])
   normal=Vector(vn);t=normal.cross(Vector((0,0,1)))
   if t.length<.001:t=normal.cross(Vector((0,1,0)))
   t.normalize();ta.extend(t);bi.extend(normal.cross(t))
 edges=child(geo,b'Edges').props[0];seen=set();newedges=[];start=0
 for i,v in enumerate(pvi):
  if v<0:
   poly=[x if x>=0 else -x-1 for x in pvi[start:i+1]]
   for j,a in enumerate(poly):
    key=tuple(sorted((a,poly[(j+1)%len(poly)])))
    if key not in seen:seen.add(key);newedges.append(start+j)
   start=i+1
 assert newedges[:len(edges)]==list(edges);edges.extend(newedges[len(edges):])
 for bn,hand in [('Hand.'+side,True),('LowerArm.'+side,False)]:
  cluster=next(o for o in objs if o.id==b'Deformer' and o.props[-1]==b'Cluster' and o.props[1].split(b'\0')[0].decode()==bn and any(c.id==b'Indexes' for c in o.elems))
  changed_clusters.add(cluster.props[0]);idx=child(cluster,b'Indexes').props[0];ws=child(cluster,b'Weights').props[0]
  for i,w in enumerate(weights):
   w=w if hand else 1-w
   if w>0:idx.append(base+i);ws.append(w)
 report[side]={'original_vertices':base,'added_vertices':len(verts),'original_triangles':oldp,'added_triangles':len(patch),'patch_closed':True,'palm_front_x':sign*(.443-.046),'wrist_range_z':[.154,.232]}
 bm.free();bpy.data.meshes.remove(me)
DEST=OUT+'/mecha_arms_PCVR.fbx';encode_bin.write(DEST,encode(root),version)
after,_=parse_fbx.parse(DEST)
for a,b in zip(child(before,b'Objects').elems,child(after,b'Objects').elems):
 if a.id!=b'Geometry' and a.props[0] not in changed_clusters:equal(a,b)
 if a.props[0] in changed_clusters:
  for x,y in zip(a.elems,b.elems):
   if x.id in [b'Indexes',b'Weights']:assert list(y.props[0][:len(x.props[0])])==list(x.props[0])
   else:equal(x,y)
equal(child(before,b'Connections'),child(after,b'Connections'));equal(child(before,b'GlobalSettings'),child(after,b'GlobalSettings'))
bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.fbx(filepath=DEST)
for o in bpy.data.objects:
 if o.type=='ARMATURE':o.data.pose_position='REST'
 if o.type=='MESH':
  side=o.name[-1];base=report[side]['original_vertices']
  assert len(o.data.vertices)==base+report[side]['added_vertices']
  assert all(abs(sum(g.weight for g in v.groups)-1)<1e-5 for v in o.data.vertices[base:])
for im in bpy.data.images:
 if 'Mecha_BaseColor_' in im.name:
  s='R' if 'Mecha_BaseColor_R' in im.name else 'L';im.filepath=r'D:\UnityProject\VR_test\Assets\Textures\Mecha_BaseColor_'+s+'.png';im.reload()
bpy.ops.wm.save_as_mainfile(filepath=OUT+'/palm_wrist_fixed.blend')
report['preserved']='Original geometry, original skin weights, all bones/bind matrices, object/material IDs, connections, animation and global settings preserved; only patch geometry and its skin weights appended.'
json.dump(report,open(OUT+'/validation.json','w'),indent=2);print(json.dumps(report))
