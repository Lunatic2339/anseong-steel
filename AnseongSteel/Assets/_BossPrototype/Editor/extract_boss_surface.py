import bpy,json,pathlib
assets=pathlib.Path(__file__).resolve().parents[2]
out=assets/'_BossPrototype/Models/Surface'
out.mkdir(exist_ok=True)
(out/'Textures').mkdir(exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(assets/'_Project/02_Art/01_Models/mecha-finger-rig-mixamo-v026.blend'))
materials=[]
for m in bpy.data.materials:
 if not m.use_nodes:raise RuntimeError(m.name)
 p=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
 base=p.inputs['Base Color'];tex=''
 if base.is_linked:
  n=base.links[0].from_node
  assert n.type=='TEX_IMAGE' and n.image.packed_file,m.name
  tex=n.image.filepath.replace('\\','/').split('/')[-1]
  (out/'Textures'/tex).write_bytes(n.image.packed_file.data)
 materials.append(dict(name=m.name,texture=tex,color=list(base.default_value),metallic=p.inputs['Metallic'].default_value,roughness=p.inputs['Roughness'].default_value,emission=list(p.inputs['Emission Color'].default_value),strength=p.inputs['Emission Strength'].default_value))
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(assets/'_BossPrototype/Models/BossUpdatedCompatible.fbx'))
objects=[dict(name=o.name,materials=[m.name for m in o.data.materials]) for o in bpy.data.objects if o.type=='MESH']
names={m['name'] for m in materials}
assert all(n in names for o in objects for n in o['materials'])
(out/'BossSurface.json').write_text(json.dumps(dict(materials=materials,objects=objects),indent=2),encoding='utf-8')
print('EXPORTED',len(materials),'materials',len(list((out/'Textures').glob('*.png'))),'textures',len(objects),'meshes')

