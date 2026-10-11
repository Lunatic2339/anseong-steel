using System.Linq;
using UnityEngine;
namespace AnseongSteel.Bosses
{
    // Generic Mixamo clips omit constant foot rotations. Reset that input before
    // Animator evaluation so a cancelled baked IK pose cannot leak into Idle.
    [DefaultExecutionOrder(-50)]
    public sealed class BossFootPoseInput : MonoBehaviour
    {
        Transform[] feet;
        Quaternion[] rotations;
        void Awake()
        {
            feet=GetComponentsInChildren<Transform>().Where(t=>t.name=="mixamorig:LeftFoot"||t.name=="mixamorig:RightFoot").ToArray();
            rotations=feet.Select(t=>t.localRotation).ToArray();
        }
        void Update()=>Restore();
        void OnDisable()=>Restore();
        void Restore(){if(feet==null)return;for(int i=0;i<feet.Length;i++)if(feet[i])feet[i].localRotation=rotations[i];}
    }
}
