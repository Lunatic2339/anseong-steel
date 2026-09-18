using UnityEngine;
using UnityEditor;
using AnseongSteel.PilotV03;

[CustomEditor(typeof(PilotReviewRig))]
public sealed class PilotReviewRigEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var rig=(PilotReviewRig)target;
        EditorGUILayout.HelpBox("Play: PC controls. Edit: select target transforms and use Move/Rotate. Feet stay fixed; head target controls orientation only.",MessageType.Info);
        string[] poses={"Neutral","Guard","Reach / Grip","Raised arm","Wrist rotation","Reach limit"};
        for(int i=0;i<poses.Length;i++)if(GUILayout.Button(poses[i]))
        {
            Undo.RecordObjects(new Object[]{rig,rig.left.target,rig.right.target,rig.left.pole,rig.right.pole,rig.headTarget},"Pilot review pose");
            rig.SetPose(i);EditorUtility.SetDirty(rig);SceneView.RepaintAll();
        }
        if(GUILayout.Button("Select left hand target"))Selection.activeTransform=rig.left.target;
        if(GUILayout.Button("Select right hand target"))Selection.activeTransform=rig.right.target;
        DrawDefaultInspector();
    }
}
