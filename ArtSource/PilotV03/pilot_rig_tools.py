"""Run once in Blender Text Editor to add the AS Pilot sidebar. No installation required."""
import bpy,math
from mathutils import Vector

def get_rig():return bpy.data.objects.get('AS_PilotRig')
class AS_OT_arm_ik(bpy.types.Operator):
    bl_idname='as_pilot.arm_ik';bl_label='현재 자세를 유지하고 IK 켜기';bl_options={'REGISTER','UNDO'}
    def execute(self,context):
        rig=get_rig()
        if not rig:return {'CANCELLED'}
        # Capture evaluated pose before changing influence so switching does not jump.
        frames={s:{n:rig.pose.bones[s+n].matrix.copy() for n in ['UpperArm','LowerArm','Hand']} for s in ['Left','Right']}
        for s in frames:
            a=frames[s]['UpperArm'].translation;e=frames[s]['LowerArm'].translation;w=frames[s]['Hand'].translation
            d=(w-a).normalized();p=e-a-d*(e-a).dot(d)
            if p.length<.002:p=Vector((0,-1,-.15))
            bpy.data.objects['CTRL_'+s+'Hand'].matrix_world=rig.matrix_world@frames[s]['Hand']
            bpy.data.objects['CTRL_'+s+'Elbow'].location=rig.matrix_world@(e+p.normalized()*.35)
        rig['ArmIK']=1.0;context.view_layer.update()
        for s in frames:
            c=rig.pose.bones[s+'LowerArm'].constraints['Two_bone_arm_IK'];best=0;error=1e9
            for i in range(32):
                a=-math.pi+2*math.pi*i/32;c.pole_angle=a;context.view_layer.update()
                err=(rig.pose.bones[s+'LowerArm'].head-frames[s]['LowerArm'].translation).length
                if err<error:error=err;best=a
            step=math.pi/16
            for _ in range(3):
                center=best
                for i in [-2,-1,0,1,2]:
                    c.pole_angle=center+i*step/4;context.view_layer.update()
                    err=(rig.pose.bones[s+'LowerArm'].head-frames[s]['LowerArm'].translation).length
                    if err<error:error=err;best=c.pole_angle
                step/=4
            c.pole_angle=best
        context.view_layer.update();return {'FINISHED'}

class AS_OT_arm_fk(bpy.types.Operator):
    bl_idname='as_pilot.arm_fk';bl_label='현재 자세를 유지하고 FK 켜기';bl_options={'REGISTER','UNDO'}
    def execute(self,context):
        rig=get_rig()
        if not rig:return {'CANCELLED'}
        matrices={n:rig.pose.bones[n].matrix.copy() for s in ['Left','Right'] for n in [s+'UpperArm',s+'LowerArm',s+'Hand']}
        rig['ArmIK']=0.0;context.view_layer.update()
        for n,mat in matrices.items():rig.pose.bones[n].matrix=mat;context.view_layer.update()
        return {'FINISHED'}

class AS_OT_select_control(bpy.types.Operator):
    bl_idname='as_pilot.select_control';bl_label='조절점 선택'
    name:bpy.props.StringProperty()
    def execute(self,context):
        o=bpy.data.objects.get(self.name)
        if not o:return {'CANCELLED'}
        if context.object and context.object.mode!='OBJECT':bpy.ops.object.mode_set(mode='OBJECT')
        bpy.ops.object.select_all(action='DESELECT');o.hide_set(False);o.select_set(True);context.view_layer.objects.active=o
        return {'FINISHED'}

class AS_PT_pilot_rig(bpy.types.Panel):
    bl_label='Anseong Pilot v03';bl_idname='AS_PT_pilot_rig';bl_space_type='VIEW_3D';bl_region_type='UI';bl_category='AS Pilot'
    def draw(self,context):
        l=self.layout;r=get_rig()
        if not r:l.label(text='AS_PilotRig가 없습니다.');return
        l.label(text='프레임 31: 중립 / 61: 가드 / 181: 비틀림')
        l.prop(r,'["ArmIK"]',text='팔 IK 영향')
        l.operator('as_pilot.arm_ik');l.operator('as_pilot.arm_fk')
        for side,label in [('Left','왼쪽'),('Right','오른쪽')]:
            row=l.row()
            op=row.operator('as_pilot.select_control',text=label+' 손');op.name='CTRL_'+side+'Hand'
            op=row.operator('as_pilot.select_control',text=label+' 팔꿈치');op.name='CTRL_'+side+'Elbow'
        l.label(text='손: G 이동 / R 회전. 구형 조절점: 팔꿈치 방향')
        l.prop(r,'["HeadControl"]',text='머리 조절점 영향')
        l.prop(r,'["PelvisControl"]',text='골반 조절점 영향')
        for name,label in [('CTRL_Head','머리 조절점'),('CTRL_Pelvis','골반 조절점')]:
            op=l.operator('as_pilot.select_control',text=label);op.name=name

classes=[AS_OT_arm_ik,AS_OT_arm_fk,AS_OT_select_control,AS_PT_pilot_rig]
def register():
    for cls in reversed(classes):
        old=getattr(bpy.types,cls.__name__,None)
        if old:
            try:bpy.utils.unregister_class(old)
            except RuntimeError:pass
    for cls in classes:bpy.utils.register_class(cls)
if __name__=='__main__':register()
