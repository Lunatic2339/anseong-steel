import bpy,bmesh,math,json,os,pickle
from mathutils import Vector
OUT=r'C:\Windows\Temp\palm_anatomy'
bpy.ops.wm.open_mainfile(filepath=OUT+'/original.blend')
rig=bpy.data.objects['Mecha_Armature'];rig.data.pose_position='REST'
result={}
def ellipsoid(name,center,scale):
 bpy.ops.mesh.primitive_uv_sphere_add(segments=24,ring_count=12,location=center)
 ob=bpy.context.object;ob.name=name;ob.scale=scale;bpy.ops.object.transform_apply(location=False,rotation=False,scale=True);return ob
def capsule(name,a,b,r):
 a,b=Vector(a),Vector(b);ob=ellipsoid(name,(a+b)*.5,(r,r,(b-a).length*.5+r))
 ob.rotation_euler=(b-a).to_track_quat('Z','Y').to_euler();return ob
def hull_mesh(name,points):
 bm=bmesh.new()
 for p in points:bm.verts.new(p)
 bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.00001)
 ret=bmesh.ops.convex_hull(bm,input=list(bm.verts),use_existing_faces=False)
 unused=set(ret.get('geom_unused',[])+ret.get('geom_interior',[]))
 dead=[g for g in unused if isinstance(g,bmesh.types.BMVert) and g.is_valid and not g.link_faces]
 if dead:bmesh.ops.delete(bm,geom=dead,context='VERTS')
 bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
 me=bpy.data.meshes.new(name);bm.to_mesh(me);bm.free();me.update();return me
for side in ['L','R']:
 s=1 if side=='L' else -1;original=bpy.data.objects['MechaArm.'+side]
 groups={'Hand.'+side,'Thumb.01.'+side,'Index.01.'+side,'Middle.01.'+side,'Ring.01.'+side,'Little.01.'+side}
 selected=[]
 for p in original.data.polygons:
  if all(any(original.vertex_groups[g.group].name in groups and g.weight>.5 for g in original.data.vertices[i].groups) for i in p.vertices):selected.append(p.index)
 selectedset=set(selected)
 components=json.load(open(OUT+'/components.json'))[original.name]
 shell=next(c for c in components if c['groups']==['Hand.'+side] and c['faces'] in (126,127))
 shellids=set(shell['ids'])
 def keep_for_union(p):
  if p.index not in selectedset:return False
  ishand=all(any(original.vertex_groups[g.group].name=='Hand.'+side and g.weight>.5 for g in original.data.vertices[i].groups) for i in p.vertices)
  return not ishand or all(i in shellids for i in p.vertices)
 me=bpy.data.meshes.new('OriginalHandPieces');me.from_pydata([v.co[:] for v in original.data.vertices],[],[list(p.vertices) for p in original.data.polygons if keep_for_union(p)]);me.update()
 ob=bpy.data.objects.new('HandCore.'+side,me);bpy.context.collection.objects.link(ob)
 bpy.context.view_layer.objects.active=ob;ob.select_set(True)
 parts=[ob]
 # Anatomical sections: broad metacarpals, narrower wrist, diagonal knuckle line.
 sections=[(.070,.470,.017,.053,.054),(.078,.464,.024,.053,.059),(.092,.454,.026,.052,.060),(.111,.444,.029,.050,.058),(.133,.436,.028,.048,.052),(.152,.430,.028,.046,.043),(.172,.428,.026,.046,.038),(.191,.423,.025,.047,.036),(.212,.415,.021,.048,.032)]
 verts=[];faces=[];n=40
 for z,xc,rx,yc,ry in sections:
  for j in range(n):
   a=2*math.pi*j/n;y=yc+ry*math.sin(a)
   # Gradually taper the metacarpal slant to the wrist.
   slant=.36*max(0,min(1,(.165-z)/.08))
   x=xc-slant*(y-yc)+rx*math.cos(a)
   # Soft thenar/hypothenar pads with a shallow hollow through the palm centre.
   if math.cos(a)<0:
    pad=.0035*math.exp(-((z-.119)/.031)**2)*(math.exp(-((y-.018)/.022)**2)+.65*math.exp(-((y-.090)/.025)**2))
    x-=pad*(-math.cos(a))
   verts.append((s*x,y,z))
 for r in range(len(sections)-1):
  for j in range(n):faces.append((r*n+j,r*n+(j+1)%n,(r+1)*n+(j+1)%n,(r+1)*n+j))
 faces.append(tuple(reversed(range(n))));faces.append(tuple((len(sections)-1)*n+j for j in range(n)))
 # Enclose the actual side-rail vertices in the palm volume, so no raised rail
 # or disconnected lip can leave an opening along either side of the hand.
 core=hull_mesh('PalmVolume',verts+[original.data.vertices[i].co[:] for i in shellids])
 ob.data=core
 for digit in ['Thumb','Index','Middle','Ring','Little']:
  points=[v.co[:] for v in original.data.vertices if any(original.vertex_groups[g.group].name==digit+'.01.'+side and g.weight>.5 for g in v.groups)]
  if digit!='Thumb' and side=='L':
   bone=rig.data.bones[digit+'.01.'+side];a=bone.head_local;axis=bone.tail_local-a;length=axis.length;axis.normalize();adjusted=[]
   for point in points:
    p=Vector(point);t=max(0,min(1,(p-a).dot(axis)/length));center=a+axis*((p-a).dot(axis))
    p=center+(p-center)*(1-.20*math.sin(math.pi*t));adjusted.append(p)
   points=adjusted
  dmesh=hull_mesh(digit+'Base',points);dob=bpy.data.objects.new(digit+'Base',dmesh);bpy.context.collection.objects.link(dob);parts.append(dob)
 # Connect each finger individually instead of hiding them behind a rectangular flap.
 for digit in ['Index','Middle','Ring','Little']:
  bone=rig.data.bones[digit+'.01.'+side];a=bone.head_local;b=bone.tail_local
  root=a+Vector((-s*.009,0,.014));end=a.lerp(b,.70)
  parts.append(capsule(digit+'Root',root,end,.013 if digit!='Little' else .012))
 for d1,d2 in [('Index','Middle'),('Middle','Ring'),('Ring','Little')]:
  a=rig.data.bones[d1+'.01.'+side].head_local+Vector((-s*.004,0,.005));b=rig.data.bones[d2+'.01.'+side].head_local+Vector((-s*.004,0,.005))
  parts.append(capsule('FingerWeb',a,b,.008))
 thumb=rig.data.bones['Thumb.01.'+side]
 parts.append(capsule('ThenarBridge',Vector((s*.427,.012,.129)),thumb.head_local.lerp(thumb.tail_local,.6),.021))
 bpy.ops.object.select_all(action='DESELECT')
 for p in parts:p.select_set(True)
 bpy.context.view_layer.objects.active=ob;bpy.ops.object.join()
 close=ob.modifiers.new('Close narrow internal seams','DISPLACE');close.direction='NORMAL';close.strength=.002;close.mid_level=0;bpy.ops.object.modifier_apply(modifier=close.name)
 # Voxel union makes one welded skin through original armor and the new palm.
 ob.data.remesh_voxel_size=.00115;ob.data.remesh_voxel_adaptivity=0
 bpy.ops.object.voxel_remesh()
 shrink=ob.modifiers.new('Restore surface after seam closure','DISPLACE');shrink.direction='NORMAL';shrink.strength=-.0015;shrink.mid_level=0;bpy.ops.object.modifier_apply(modifier=shrink.name)
 smooth=ob.modifiers.new('Palm transition smoothing','SMOOTH');smooth.factor=.65;smooth.iterations=5;bpy.ops.object.modifier_apply(modifier=smooth.name)
 dec=ob.modifiers.new('Retain hand silhouette','DECIMATE');dec.ratio=.026;bpy.ops.object.modifier_apply(modifier=dec.name)
 smooth=ob.modifiers.new('Soften palm facets','SMOOTH');smooth.factor=.35;smooth.iterations=3;bpy.ops.object.modifier_apply(modifier=smooth.name)
 bm=bmesh.new();bm.from_mesh(ob.data);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bmesh.ops.triangulate(bm,faces=list(bm.faces));bm.to_mesh(ob.data);bm.free();ob.data.update()
 for p in ob.data.polygons:p.use_smooth=True
 # Inspect connectivity: the reconstructed palm and all five proximal digits must be one piece.
 bm=bmesh.new();bm.from_mesh(ob.data);remaining=set(bm.verts);sizes=[]
 while remaining:
  todo=[remaining.pop()];c=set(todo)
  while todo:
   for e in todo.pop().link_edges:
    for v in e.verts:
     if v in remaining:remaining.remove(v);todo.append(v);c.add(v)
  sizes.append(len(c))
  if len(c)<100:bmesh.ops.delete(bm,geom=list(c),context='VERTS')
 bm.to_mesh(ob.data);ob.data.update()
 boundary=sum(e.is_boundary for e in bm.edges);nonmanifold=sum(not e.is_manifold for e in bm.edges);euler=len(bm.verts)-len(bm.edges)+len(bm.faces);bm.free()
 assert boundary==0 and nonmanifold==0 and euler==2 and len([n for n in sizes if n>=100])==1,(side,boundary,nonmanifold,euler)
 weights=[]
 for v in ob.data.vertices:
  p=v.co;w={};z=p.z
  fore=max(0,min(1,(z-.164)/.045));fore=fore*fore*(3-2*fore)
  if fore>0:w['LowerArm.'+side]=fore
  handweight=1-fore
  # Root blending is limited to the actual digit base, not the entire palm.
  candidates=[]
  for digit in ['Index','Middle','Ring','Little']:
   b=rig.data.bones[digit+'.01.'+side];a=b.head_local;axis=(b.tail_local-a).normalized();t=(p-a).dot(axis)
   radial=((p-a)-axis*t).length
   if radial<.024:
    f=max(0,min(1,(t+.010)/.024));candidates.append((f,digit+'.01.'+side,radial))
  if candidates and z<.090:
   f,name,rad=min(candidates,key=lambda x:x[2]);f*=handweight
   if f>0:w[name]=f;handweight-=f
  thumbweight=max(0,min(1,(.006-p.y)/.025)) if z>.09 and p.y<.006 else 0
  if thumbweight>0:
   f=thumbweight*handweight;w['Thumb.01.'+side]=f;handweight-=f
  if handweight>0:w['Hand.'+side]=handweight
  assert abs(sum(w.values())-1)<1e-5;weights.append(w)
 for name in groups|{'LowerArm.'+side}:ob.vertex_groups.new(name=name)
 for i,w in enumerate(weights):
  for name,value in w.items():ob.vertex_groups[name].add([i],value,'REPLACE')
 ob.data.materials.append(original.data.materials[0]);arm=ob.modifiers.new('Skin','ARMATURE');arm.object=rig
 # A companion file will transfer old UVs where the surface was retained.
 result[side]={'remove_polygons':selected,'vertices':[list(v.co) for v in ob.data.vertices],'faces':[list(p.vertices) for p in ob.data.polygons],'normals':[list(v.normal) for v in ob.data.vertices],'weights':weights,'component_sizes':[n for n in sizes if n>=100],'boundary_edges':boundary,'nonmanifold_edges':nonmanifold,'euler':euler}
 print(side,'new verts',len(ob.data.vertices),'faces',len(ob.data.polygons),'components',sizes,'boundary',boundary,'nonmanifold',nonmanifold,'euler',len(ob.data.vertices)-len(ob.data.edges)+len(ob.data.polygons))
 original.hide_render=True
 # Remove selected originals for preview, retaining every other original arm/finger polygon.
 kept=bpy.data.meshes.new('RetainedMesh');kept.from_pydata([v.co[:] for v in original.data.vertices],[],[list(p.vertices) for p in original.data.polygons if p.index not in selectedset]);kept.materials.append(original.data.materials[0])
 preview=bpy.data.objects.new('Retained.'+side,kept);bpy.context.collection.objects.link(preview)
 for group in original.vertex_groups:preview.vertex_groups.new(name=group.name)
 for v in original.data.vertices:
  for g in v.groups:preview.vertex_groups[g.group].add([v.index],g.weight,'REPLACE')
 arm=preview.modifiers.new('Skin','ARMATURE');arm.object=rig
json.dump(result,open(OUT+'/anatomy_geometry.json','w'))
bpy.ops.wm.save_as_mainfile(filepath=OUT+'/anatomy_work.blend')
