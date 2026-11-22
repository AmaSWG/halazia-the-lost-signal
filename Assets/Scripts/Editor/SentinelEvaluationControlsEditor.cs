using Halazia.AI.Sentinel;
using UnityEditor;
using UnityEngine;

/// <summary>Inspector buttons for the existing SentinelEvaluationControls context-menu actions.</summary>
[CustomEditor(typeof(SentinelEvaluationControls))]
public sealed class SentinelEvaluationControlsEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var controls = (SentinelEvaluationControls)target;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Play Mode Actions", EditorStyles.boldLabel);
        if (!Application.isPlaying)
            EditorGUILayout.HelpBox("Enter Play Mode to issue orders. Selecting a Task alone does not issue it.", MessageType.Info);
        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            if (GUILayout.Button("Start Patrol Route")) controls.StartPatrolRoute();
            if (GUILayout.Button("Issue Selected Order")) controls.IssueOrder();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Cancel Order")) controls.Cancel();
                if (GUILayout.Button("Damage Sentinel 25")) controls.Damage();
            }
        }
        // Keep live diagnostics fields refreshing while playing.
        if (Application.isPlaying) Repaint();
    }
}
