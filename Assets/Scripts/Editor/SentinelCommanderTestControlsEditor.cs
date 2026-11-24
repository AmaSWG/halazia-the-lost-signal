using Halazia.AI.Sentinel;
using UnityEditor;
using UnityEngine;

/// <summary>Inspector buttons for the Commander ↔ Sentinel test controls.</summary>
[CustomEditor(typeof(SentinelCommanderTestControls))]
public sealed class SentinelCommanderTestControlsEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var controls = (SentinelCommanderTestControls)target;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Commander → Sentinel (Play Mode)", EditorStyles.boldLabel);
        if (!Application.isPlaying)
            EditorGUILayout.HelpBox("Enter Play Mode. Orders go through CommanderAgent.IssueDirectOrder.", MessageType.Info);
        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("CoverRoute")) controls.CommanderCoverRoute();
                if (GUILayout.Button("HoldPosition")) controls.CommanderHoldPosition();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Blocked Path Test")) controls.CommanderBlockedPath();
                if (GUILayout.Button("Cancel Order")) controls.CancelSentinelOrder();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Release To Commander")) controls.ReleaseToCommander();
                if (GUILayout.Button("Toggle Target (Loss Test)")) controls.ToggleTarget();
            }
        }
        if (Application.isPlaying) Repaint();
    }
}
