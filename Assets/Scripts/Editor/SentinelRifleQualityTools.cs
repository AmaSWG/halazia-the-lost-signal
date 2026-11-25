using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>In-place rifle inspection and polish. Never recreates mesh subassets or edits scenes.</summary>
public static class SentinelRifleQualityTools
{
    const string PrefabPath = SentinelRifleBuilder.PrefabPath;
    const string MeshPath = "Assets/Art/Models/Weapons/Sentinel_Rifle/Sentinel_Rifle_Meshes.asset";
    const string Output = "Artifacts/SentinelRifle";

    [MenuItem("Tools/Halazia/Sentinel Rifle/Audit Existing Prefab")]
    public static void Audit()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (!prefab) throw new Exception("Missing rifle prefab");
        Directory.CreateDirectory(Output);
        var report = new StringBuilder();
        var bounds = new Bounds(); bool first = true;
        int triangles = 0, degenerate = 0;
        var materials = new HashSet<Material>();
        foreach (var f in prefab.GetComponentsInChildren<MeshFilter>(true))
        {
            var m = f.sharedMesh; var r = f.GetComponent<MeshRenderer>();
            if (!m || !r || r.sharedMaterials.Length != m.subMeshCount) throw new Exception("Invalid part: " + f.name);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(m, out string guid, out long id);
            report.Append(f.transform.parent.name + "/" + f.name + " | " + guid + ":" + id);
            foreach (var mat in r.sharedMaterials)
            {
                if (!mat || !mat.shader || !mat.shader.isSupported || ShaderUtil.ShaderHasError(mat.shader)) throw new Exception("Invalid material on " + f.name);
                materials.Add(mat); report.Append(" | " + mat.name);
            }
            report.AppendLine();
            var b = r.bounds; if (first) { bounds = b; first = false; } else bounds.Encapsulate(b);
            var v = m.vertices; var t = m.triangles; triangles += t.Length / 3;
            for (int i = 0; i < t.Length; i += 3)
                if (Vector3.Cross(v[t[i+1]]-v[t[i]], v[t[i+2]]-v[t[i]]).sqrMagnitude < 1e-20f) degenerate++;
        }
        report.AppendLine("\nMATERIALS");
        foreach(var m in materials) report.AppendLine(m.name + " | shader=" + m.shader.name + " | color=" + m.color + " | emission=" + m.GetColor("_EmissionColor") + " | keywords=" + string.Join(",",m.shaderKeywords));
        report.AppendLine("\nSOCKETS (metres, local forward/up)");
        if (degenerate != 0) throw new Exception("Degenerate rifle triangles remain: " + degenerate);
        if (prefab.transform.localPosition != Vector3.zero || prefab.transform.localRotation != Quaternion.identity || prefab.transform.localScale != Vector3.one)
            throw new Exception("Rifle root must have identity transform.");
        var gripBounds = prefab.transform.Find("Grip/GripCore").GetComponent<MeshFilter>().sharedMesh.bounds;
        if (gripBounds.center.sqrMagnitude > 1e-8f) throw new Exception("Pivot is not centered in the grip.");
        if (prefab.GetComponentsInChildren<MonoBehaviour>(true).Length != 0) throw new Exception("Unexpected rifle script.");
        foreach(var name in new[]{"Grip_Socket","Support_Hand_Socket","Muzzle_Point","Sight_Point","Ejection_Point"})
        {
            var s=prefab.transform.Find("Sockets/"+name);if(!s)throw new Exception("Missing "+name);
            var expectedForward = name == "Ejection_Point" ? Vector3.right : Vector3.forward;
            if (Vector3.Dot(s.forward, expectedForward) < .999f || Vector3.Dot(s.up, Vector3.up) < .999f)
                throw new Exception("Incorrect socket orientation: " + name);
            if (name == "Grip_Socket" && s.localPosition.sqrMagnitude > 1e-8f) throw new Exception("Grip socket must be at the pivot.");
            report.AppendLine(name+" | "+s.localPosition.ToString("F4")+" | forward="+s.forward.ToString("F3")+" | up="+s.up.ToString("F3"));
        }
        report.AppendLine($"\nBounds: {bounds.size}; triangles: {triangles}; degenerate triangles: {degenerate}; materials: {materials.Count}");
        report.AppendLine("Root position="+prefab.transform.localPosition+" rotation="+prefab.transform.localEulerAngles+" scale="+prefab.transform.localScale);
        File.WriteAllText(Output+"/quality-audit.txt", report.ToString());
        SentinelRifleBuilder.Render(prefab,"left",Vector3.left,bounds);
        SentinelRifleBuilder.Render(prefab,"right",Vector3.right,bounds);
        SentinelRifleBuilder.Render(prefab,"front",Vector3.forward,bounds);
        SentinelRifleBuilder.Render(prefab,"rear",Vector3.back,bounds);
        SentinelRifleBuilder.Render(prefab,"top",new Vector3(.001f,1,0),bounds);
        SentinelRifleBuilder.Render(prefab,"bottom",new Vector3(.001f,-1,0),bounds);
        SentinelRifleBuilder.Render(prefab,"perspective",new Vector3(-2,.65f,1.05f),bounds);
        Debug.Log("SENTINEL_RIFLE_AUDIT_PASS: " + triangles + " triangles, " + materials.Count + " valid materials; six views rendered.");
    }

    [MenuItem("Tools/Halazia/Sentinel Rifle/Polish Existing Prefab")]
    public static void Polish()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Polish outside Play Mode.");
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            // Infer the hold point from the existing grip, making this safe to run again.
            var grip = root.transform.Find("Grip/GripCore").GetComponent<MeshFilter>().sharedMesh;
            var hold = grip.bounds.center;
            var moved = new HashSet<Mesh>();
            foreach (var f in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = f.sharedMesh;
                if (AssetDatabase.GetAssetPath(mesh) != MeshPath)
                    throw new InvalidOperationException("Unexpected shared mesh: " + f.name);
                if (!moved.Add(mesh)) continue;
                var vertices = mesh.vertices;
                for (int i = 0; i < vertices.Length; i++) vertices[i] -= hold;
                // The insignia was coplanar with the receiver's outer face.
                if (f.name.StartsWith("Insignia", StringComparison.Ordinal))
                {
                    float side = Mathf.Sign(mesh.bounds.center.x);
                    float target = side * .0572f;
                    float offset = target - mesh.bounds.center.x;
                    for (int i = 0; i < vertices.Length; i++) vertices[i].x += offset;
                }
                var old = mesh.triangles;
                var clean = new List<int>(old.Length);
                for (int i = 0; i < old.Length; i += 3)
                    if (Vector3.Cross(vertices[old[i+1]]-vertices[old[i]], vertices[old[i+2]]-vertices[old[i]]).sqrMagnitude >= 1e-20f)
                    { clean.Add(old[i]); clean.Add(old[i+1]); clean.Add(old[i+2]); }
                mesh.vertices = vertices;
                mesh.triangles = clean.ToArray();
                mesh.RecalculateNormals(); mesh.RecalculateBounds();
                EditorUtility.SetDirty(mesh);
                AssetDatabase.SaveAssetIfDirty(mesh);
            }
            foreach (Transform socket in root.transform.Find("Sockets")) socket.localPosition -= hold;
            root.transform.Find("Sockets/Grip_Socket").localPosition = Vector3.zero;
            var ejection = root.transform.Find("Sockets/Ejection_Point");
            var recess = root.transform.Find("Controls/EjectionRecess").GetComponent<MeshFilter>().sharedMesh.bounds;
            ejection.localPosition = new Vector3(.060f, recess.center.y, recess.center.z);
            ejection.localRotation = Quaternion.LookRotation(Vector3.right, Vector3.up);
            var sight = root.transform.Find("Sockets/Sight_Point");
            sight.localPosition = root.transform.Find("Optic/Reticle").GetComponent<MeshFilter>().sharedMesh.bounds.center;
            var cyan = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Weapons/Mat_Rifle_EnergyCyan.mat");
            cyan.EnableKeyword("_EMISSION");
            cyan.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            EditorUtility.SetDirty(cyan); AssetDatabase.SaveAssetIfDirty(cyan);
            var ivory = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Weapons/Mat_Rifle_StencilIvory.mat");
            ivory.SetFloat("_Metallic", 0f);
            ivory.SetFloat("_Glossiness", .22f);
            EditorUtility.SetDirty(ivory); AssetDatabase.SaveAssetIfDirty(ivory);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        SentinelRifleBuilder.RepairMeshPreview();
        Audit();
    }
}
