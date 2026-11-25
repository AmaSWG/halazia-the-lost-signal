using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Reference-image rifle. +Z muzzle, +Y up, origin at the grip hold point.
/// Dimensions are baked into meshes; no scene objects or agent assets are edited.</summary>
[InitializeOnLoad]
public static class SentinelRifleBuilder
{
    public const string PrefabPath = "Assets/Prefabs/Weapons/Sentinel_Rifle.prefab";
    const string MeshPath = "Assets/Art/Models/Weapons/Sentinel_Rifle/Sentinel_Rifle_Meshes.asset";
    const string MatDir = "Assets/Art/Materials/Weapons";
    const string Request = "Library/SentinelRifleBuild.request";
    const string Output = "Artifacts/SentinelRifle";
    const float Scale = 1.3f;
    static GameObject root;
    static readonly List<Mesh> meshes = new List<Mesh>();
    static readonly Dictionary<string, Transform> groups = new Dictionary<string, Transform>();
    static Material black, silver, steel, rubber, cyan, glass, ivory;

    static SentinelRifleBuilder() { EditorApplication.delayCall += RequestedBuild; }
    static void RequestedBuild()
    {
        if (!File.Exists(Request)) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
        { EditorApplication.delayCall += RequestedBuild; return; }
        string request = File.ReadAllText(Request).Trim();
        File.Delete(Request);
        try { if (request == "audit") SentinelRifleQualityTools.Audit(); else if (request == "polish") SentinelRifleQualityTools.Polish(); else if (request == "repair") RepairMeshPreview(); else BuildAndVerify(); }
        catch (Exception e) { Directory.CreateDirectory(Output); File.WriteAllText(Output + "/failure.txt", e.ToString()); Debug.LogException(e); }
    }

    [MenuItem("Tools/Halazia/Sentinel Rifle/Build and Verify Reference Rifle")]
    public static void BuildAndVerify()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Build outside Play Mode.");
        Folder(MatDir); Folder(Path.GetDirectoryName(MeshPath).Replace('\\', '/')); Folder("Assets/Prefabs/Weapons");
        black = Mat("CeramicBlack", new Color(.065f,.075f,.09f), .45f, .36f);
        silver = Mat("SilverArmor", new Color(.59f,.64f,.75f), .65f, .48f);
        steel = Mat("Gunmetal", new Color(.22f,.24f,.29f), .72f, .46f);
        rubber = Mat("GripRubber", new Color(.025f,.028f,.034f), .05f, .22f);
        cyan = Mat("EnergyCyan", new Color(.01f,.58f,.9f), .15f, .6f, true);
        glass = Mat("OpticGlass", new Color(.012f,.10f,.16f), .5f, .9f);
        ivory = Mat("StencilIvory", new Color(.8f,.85f,.94f), .1f, .35f);
        meshes.Clear(); groups.Clear(); root = new GameObject("Sentinel_Rifle");
        try
        {
            Model();
            var container = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            if (container == null) { container = new Mesh { name = "Sentinel_Rifle_Meshes" }; AssetDatabase.CreateAsset(container, MeshPath); }
            var old = AssetDatabase.LoadAllAssetsAtPath(MeshPath);
            foreach (var m in old) if (m != container) Object.DestroyImmediate(m, true);
            foreach (var mesh in meshes) AssetDatabase.AddObjectToAsset(mesh, container);
            UpdateCombinedMesh(container, meshes);
            var sockets = new GameObject("Sockets").transform; sockets.SetParent(root.transform, false);
            Socket(sockets, "Grip_Socket", 0,0,0);
            Socket(sockets, "Support_Hand_Socket", 0,.06f,.34f);
            Socket(sockets, "Muzzle_Point", 0,.135f,.66f);
            Socket(sockets, "Sight_Point", 0,.255f,-.025f);
            Socket(sockets, "Ejection_Point", .043f,.137f,.065f);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool success);
            if (!success) throw new IOException("Prefab save failed");
            AssetDatabase.SaveAssets();
        }
        finally { Object.DestroyImmediate(root); }
        Verify();
    }

    static void Model()
    {
        // Side profiles are (forward Z, up Y), reconstructed from the supplied side view.
        Plate("Receiver", "Core", black, -.035f,.035f, .008f, -.14f,.075f, -.14f,.17f, -.065f,.20f, .13f,.18f, .24f,.13f, .18f,.065f, .025f,.035f);
        Plate("Foreend", "HandguardCore", black,-.031f,.031f,.005f, .16f,.065f,.18f,.177f,.49f,.177f,.51f,.153f,.49f,.067f);
        Plate("Stock", "BufferSpine", black,-.028f,.028f,.005f, -.32f,.115f,-.32f,.18f,-.12f,.18f,-.13f,.11f);
        // Open triangular stock: separate beams keep the large negative space real.
        Plate("Stock", "UpperBeam", black,-.033f,.033f,.007f, -.48f,.181f,-.47f,.215f,-.32f,.207f,-.25f,.17f,-.30f,.139f,-.45f,.139f);
        Plate("Stock", "DiagonalBrace", black,-.026f,.026f,.004f, -.45f,.036f,-.43f,.021f,-.28f,.155f,-.32f,.175f);
        Plate("Stock", "ButtFrame", silver,-.035f,.035f,.006f, -.49f,.21f,-.47f,.22f,-.45f,.203f,-.45f,.027f,-.43f,.009f,-.46f,.002f,-.49f,.03f);
        Plate("Stock", "ButtPad", rubber,-.032f,.032f,.004f,-.498f,.204f,-.49f,.211f,-.49f,.027f,-.47f,.007f,-.486f,.008f,-.503f,.033f);
        Plate("Stock", "CheekArmor", silver,-.034f,.034f,.004f,-.463f,.215f,-.33f,.208f,-.31f,.19f,-.457f,.188f);
        Plate("Grip", "GripCore", black,-.026f,.026f,.005f,-.115f,.064f,-.065f,.046f,-.11f,-.093f,-.151f,-.12f,-.17f,-.103f);
        Plate("Grip", "GripHeel", silver,-.029f,.029f,.004f,-.171f,-.101f,-.15f,-.12f,-.107f,-.092f,-.102f,-.108f,-.148f,-.141f,-.177f,-.118f);
        // Trigger guard is an open loop; the curved trigger is separate inside it.
        Bar("Controls","GuardFront",black,0,.055f,.052f,0,.004f,.049f,.010f,.047f);
        Bar("Controls","GuardBottom",black,0,.004f,.049f,0,-.002f,-.058f,.011f,.047f);
        Bar("Controls","GuardRear",black,0,-.002f,-.058f,0,.039f,-.077f,.010f,.047f);
        Bar("Controls","TriggerUpper",steel,0,.051f,-.004f,0,.022f,-.012f,.006f,.012f);
        Bar("Controls","TriggerTip",steel,0,.022f,-.012f,0,.014f,.001f,.006f,.012f);
        Plate("Magazine","MagazineBody",black,-.029f,.029f,.004f,.04f,.046f,.133f,.032f,.174f,-.141f,.073f,-.16f);
        Plate("Magazine","MagazineWell",steel,-.035f,.035f,.003f,.025f,.06f,.145f,.042f,.15f,.019f,.03f,.035f);
        Plate("Magazine","FloorPlate",steel,-.034f,.034f,.004f,.07f,-.146f,.178f,-.128f,.188f,-.155f,.078f,-.178f);
        foreach (float side in new[]{-1f,1f})
        {
            float x = side*.037f;
            Panel("Receiver","Shield",silver,x,.008f,.005f, -.077f,.185f,.086f,.178f,.117f,.149f,.080f,.112f,.027f,.041f,-.047f,.057f,-.091f,.135f);
            Panel("Foreend","UpperArmor",silver,side*.035f,.007f,.004f,.10f,.18f,.19f,.184f,.22f,.163f,.46f,.169f,.49f,.146f,.47f,.125f,.23f,.136f,.18f,.124f);
            Panel("Foreend","LowerArmor",silver,side*.035f,.007f,.004f,.17f,.115f,.22f,.105f,.47f,.10f,.49f,.074f,.46f,.058f,.20f,.061f);
            Panel("Foreend","VentRecess",black,side*.040f,.004f,.001f,.235f,.108f,.466f,.104f,.45f,.14f,.22f,.145f);
            for(int i=0;i<4;i++)
            {
                float z=.255f+i*.045f;
                Panel("Foreend","CyanVent"+i,cyan,side*.043f,.002f,.001f,z,.114f,z+.023f,.113f,z+.009f,.135f,z-.014f,.136f);
            }
            Panel("Stock","StockInset",rubber,side*.034f,.003f,.001f,-.448f,.15f,-.337f,.155f,-.35f,.179f,-.453f,.174f);
            Panel("Stock","StockLight",cyan,side*.036f,.002f,.001f,-.438f,.156f,-.374f,.159f,-.365f,.17f,-.442f,.168f);
            Panel("Stock","ConnectorArmor",silver,side*.03f,.006f,.003f,-.25f,.166f,-.13f,.174f,-.12f,.142f,-.21f,.132f);
            Panel("Grip","GripInset",rubber,side*.027f,.003f,.002f,-.119f,.035f,-.084f,.018f,-.12f,-.087f,-.151f,-.105f,-.153f,-.084f);
            for(int i=0;i<16;i++) Bar("Grip","GripTread"+i,steel,side*.029f,.016f-i*.006f,-.112f-i*.002f,side*.029f,.005f-i*.006f,-.09f-i*.002f,.0013f,.0013f);
            Panel("Magazine","MagazineInset",rubber,side*.030f,.002f,.002f,.055f,.018f,.116f,.008f,.149f,-.126f,.086f,-.139f);
            Bar("Magazine","SideSpine",silver,side*.032f,.019f,.131f,side*.032f,-.133f,.167f,.009f,.004f);
            Bar("Magazine","ChargeTrack",steel,side*.033f,-.043f,.095f,side*.033f,-.135f,.116f,.005f,.002f);
            Bar("Magazine","ChargeLight",cyan,side*.035f,-.109f,.110f,side*.035f,-.136f,.116f,.004f,.002f);
            // Three disconnected stencil triangles reproduce the receiver emblem.
            Panel("Details","InsigniaA",ivory,side*.043f,.0007f,0,-.028f,.092f,-.014f,.092f,.003f,.132f);
            Panel("Details","InsigniaB",ivory,side*.043f,.0007f,0,.008f,.135f,.021f,.112f,.032f,.089f,.018f,.095f);
            Panel("Details","InsigniaC",ivory,side*.043f,.0007f,0,-.026f,.085f,.032f,.084f,.016f,.092f,-.015f,.093f);
            Panel("Controls","EjectionRecess",rubber,side*.037f,.004f,.002f,.095f,.12f,.185f,.125f,.19f,.146f,.115f,.149f);
            Bar("Controls","BoltCover",steel,side*.040f,.136f,.12f,side*.040f,.14f,.18f,.012f,.003f);
            foreach(var p in new[]{new Vector2(-.052f,.074f),new Vector2(-.066f,.16f),new Vector2(.088f,.159f),new Vector2(.201f,.073f),new Vector2(.467f,.155f),new Vector2(-.23f,.151f)})
            {
                Cylinder("Details","Fastener",steel,new Vector3(side*.044f,p.y,p.x),Vector3.right,.0055f,.003f,8);
                Bar("Details","ScrewSlot",black,side*.046f,p.y-.002f,p.x,side*.046f,p.y+.002f,p.x,.001f,.001f);
            }
            for(int i=0;i<3;i++) Panel("Foreend","RearCoolingSlot"+i,black,side*.039f,.002f,.001f,.148f+i*.022f,.076f,.158f+i*.022f,.077f,.143f+i*.022f,.099f,.133f+i*.022f,.098f);
        }
        Box("Rail","RailBase",steel,0,.191f,.215f,.032f,.014f,.52f);
        for(int i=0;i<29;i++) Box("Rail","PicatinnyTooth"+i,black,0,.202f,-.035f+i*.018f,.045f,.009f,.009f);
        Cylinder("Barrel","Barrel",steel,new Vector3(0,.135f,.536f),Vector3.forward,.017f,.09f,20);
        Cylinder("Barrel","Collar",black,new Vector3(0,.135f,.563f),Vector3.forward,.022f,.017f,20);
        Tube("MuzzleBrake",new Vector3(0,.135f,.613f),.027f,.015f,.085f);
        Cylinder("Barrel","MuzzleRearBand",steel,new Vector3(0,.135f,.578f),Vector3.forward,.028f,.008f,20);
        Tube("MuzzleLip",new Vector3(0,.135f,.654f),.029f,.016f,.009f);
        for(int i=0;i<6;i++)
        {
            float a=i*Mathf.PI/3; var pos=new Vector3(Mathf.Cos(a)*.0265f,.135f+Mathf.Sin(a)*.0265f,.616f);
            var go=Box("Barrel","BrakePort"+i,black,pos.x,pos.y,pos.z,.012f,.002f,.037f);
            var mesh=go.GetComponent<MeshFilter>().sharedMesh;var vertices=mesh.vertices;var rotation=Quaternion.Euler(0,0,a*Mathf.Rad2Deg-90);
            for(int j=0;j<vertices.Length;j++) vertices[j]=pos*Scale+rotation*(vertices[j]-pos*Scale);
            mesh.vertices=vertices;mesh.RecalculateNormals();mesh.RecalculateBounds();
        }
        // Hooded reflex sight, with a real opening and inset blue optical pane.
        Box("Optic","Mount",steel,0,.214f,-.035f,.064f,.018f,.17f);
        foreach(float side in new[]{-1f,1f})
        {
            Panel("Optic","HoodSide",black,side*.026f,.011f,.002f,-.058f,.218f,.004f,.218f,-.006f,.28f,-.039f,.284f);
            Panel("Optic","HoodArmor",steel,side*.032f,.003f,.001f,-.056f,.222f,-.037f,.276f,-.009f,.274f,-.002f,.226f);
            Box("Optic","StatusStrip",cyan,side*.034f,.255f,-.014f,.0015f,.018f,.004f);
        }
        Box("Optic","HoodTop",steel,0,.284f,-.027f,.057f,.010f,.047f);
        Box("Optic","Lens",glass,0,.254f,-.035f,.040f,.045f,.002f);
        Box("Optic","Reticle",cyan,0,.254f,-.037f,.002f,.002f,.001f);
        Cylinder("Optic","WindageDial",steel,new Vector3(.035f,.237f,-.038f),Vector3.right,.012f,.012f,16);
        Box("Optic","StatusLight",cyan,.032f,.262f,-.02f,.001f,.017f,.004f);
        Box("FrontSight","Base",steel,0,.19f,.50f,.047f,.012f,.035f);
        Box("FrontSight","LeftEar",black,-.02f,.214f,.50f,.009f,.043f,.023f);
        Box("FrontSight","RightEar",black,.02f,.214f,.50f,.009f,.043f,.023f);
        Box("FrontSight","Bridge",steel,0,.237f,.50f,.042f,.008f,.023f);
        Box("FrontSight","Post",steel,0,.211f,.50f,.005f,.026f,.008f);
    }

    static void Folder(string path) { if(AssetDatabase.IsValidFolder(path)) return; string parent=Path.GetDirectoryName(path).Replace('\\','/'); Folder(parent); AssetDatabase.CreateFolder(parent,Path.GetFileName(path)); }
    static Material Mat(string name,Color color,float metallic,float smooth,bool emission=false)
    {
        string path=MatDir+"/Mat_Rifle_"+name+".mat";
        var m=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(m==null) { m=new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(m,path); }
        m.color=color; m.SetFloat("_Metallic",metallic); m.SetFloat("_Glossiness",smooth);
        if(emission) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor",new Color(0,.65f,1.25f)); }
        EditorUtility.SetDirty(m); return m;
    }
    static GameObject Part(string group,string name,Material mat,List<Vector3> v,List<int> t)
    {
        if(!groups.TryGetValue(group,out var parent)) { parent=new GameObject(group).transform; parent.SetParent(root.transform,false); groups.Add(group,parent); }
        var mesh=new Mesh { name=group+"_"+name+"_"+meshes.Count };
        // Split corners for crisp machined surfaces and predictable flat shading.
        var verts=new Vector3[t.Count]; var ids=new int[t.Count]; var uv=new Vector2[t.Count];
        for(int i=0;i<t.Count;i++) { verts[i]=v[t[i]]*Scale; ids[i]=i; uv[i]=new Vector2(verts[i].z,verts[i].y); }
        mesh.vertices=verts; mesh.triangles=ids; mesh.uv=uv; mesh.RecalculateNormals(); mesh.RecalculateBounds(); meshes.Add(mesh);
        var go=new GameObject(name); go.transform.SetParent(parent,false); go.AddComponent<MeshFilter>().sharedMesh=mesh; go.AddComponent<MeshRenderer>().sharedMaterial=mat; return go;
    }
    static void Panel(string g,string n,Material m,float x,float depth,float bevel,params float[] p) { Plate(g,n,m,x-depth/2,x+depth/2,bevel,p); }
    static void Plate(string g,string n,Material m,float lo,float hi,float bevel,params float[] p)
    {
        int count=p.Length/2; var poly=new List<Vector2>(); for(int i=0;i<count;i++) poly.Add(new Vector2(p[i*2],p[i*2+1]));
        float area=0; for(int i=0;i<count;i++) { var a=poly[i];var b=poly[(i+1)%count];area+=a.x*b.y-b.x*a.y; } if(area<0) poly.Reverse();
        var center=Vector2.zero; foreach(var a in poly) center+=a; center/=count;
        var v=new List<Vector3>(); var t=new List<int>(); float bvl=Mathf.Min(bevel,(hi-lo)*.4f);
        for(int ring=0;ring<4;ring++) foreach(var a in poly) { var q=(ring==0||ring==3)?Vector2.MoveTowards(a,center,bevel):a; float x=ring==0?lo:ring==1?lo+bvl:ring==2?hi-bvl:hi; v.Add(new Vector3(x,q.y,q.x)); }
        for(int r=0;r<3;r++) for(int i=0;i<count;i++) {int j=(i+1)%count; Quad(t,r*count+i,(r+1)*count+i,(r+1)*count+j,r*count+j);}
        // Ear clipping handles the concave armor profiles without triangle fans crossing edges.
        var ix=new List<int>(); for(int i=0;i<count;i++) ix.Add(i);
        int guard=0; while(ix.Count>2 && guard++<count*count)
        {
            bool found=false; for(int k=0;k<ix.Count;k++) {
                int a=ix[(k+ix.Count-1)%ix.Count], b=ix[k], c=ix[(k+1)%ix.Count];
                if(Cross(poly[b]-poly[a],poly[c]-poly[b])<=0) continue;
                bool inside=false; foreach(int j in ix) if(j!=a&&j!=b&&j!=c && Cross(poly[b]-poly[a],poly[j]-poly[a])>=0 && Cross(poly[c]-poly[b],poly[j]-poly[b])>=0 && Cross(poly[a]-poly[c],poly[j]-poly[c])>=0) {inside=true;break;}
                if(inside) continue; t.Add(a);t.Add(b);t.Add(c);t.Add(3*count+c);t.Add(3*count+b);t.Add(3*count+a);ix.RemoveAt(k);found=true;break;
            } if(!found) throw new InvalidOperationException("Cannot triangulate "+n);
        }
        Part(g,n,m,v,t);
    }
    static float Cross(Vector2 a,Vector2 b)=>a.x*b.y-a.y*b.x;
    static void Quad(List<int> t,int a,int b,int c,int d) {t.Add(a);t.Add(b);t.Add(c);t.Add(a);t.Add(c);t.Add(d);}
    static GameObject Box(string g,string n,Material m,float x,float y,float z,float w,float h,float l)
    {
        Plate(g,n,m,x-w/2,x+w/2,Mathf.Min(.002f,Mathf.Min(w,Mathf.Min(h,l))*.15f),z-l/2,y-h/2,z+l/2,y-h/2,z+l/2,y+h/2,z-l/2,y+h/2);
        return groups[g].GetChild(groups[g].childCount-1).gameObject;
    }
    static void Bar(string g,string n,Material m,float x,float y,float z,float xx,float yy,float zz,float thick,float width)
    {
        var a=new Vector2(z,y);var b=new Vector2(zz,yy);var d=(b-a).normalized;var q=new Vector2(-d.y,d.x)*thick/2;
        Panel(g,n,m,(x+xx)/2,width,Mathf.Min(.001f,thick*.15f),a.x+q.x,a.y+q.y,a.x-q.x,a.y-q.y,b.x-q.x,b.y-q.y,b.x+q.x,b.y+q.y);
    }
    static void Cylinder(string g,string n,Material m,Vector3 pos,Vector3 axis,float rad,float length,int sides)
    {
        var v=new List<Vector3>();var t=new List<int>();var rot=Quaternion.FromToRotation(Vector3.forward,axis);
        for(int r=0;r<2;r++) for(int i=0;i<sides;i++) {float a=i*2*Mathf.PI/sides;v.Add(pos+rot*new Vector3(Mathf.Cos(a)*rad,Mathf.Sin(a)*rad,(r-.5f)*length));}
        for(int i=0;i<sides;i++) {int j=(i+1)%sides;Quad(t,i,j,j+sides,i+sides);if(i>0&&i<sides-1){t.Add(0);t.Add(i+1);t.Add(i);t.Add(sides);t.Add(sides+i);t.Add(sides+i+1);}}
        Orient(v,t,pos); Part(g,n,m,v,t);
    }
    static void Orient(List<Vector3> v,List<int> t,Vector3 center) {for(int i=0;i<t.Count;i+=3) {var a=v[t[i]];var b=v[t[i+1]];var c=v[t[i+2]];if(Vector3.Dot(Vector3.Cross(b-a,c-a),(a+b+c)/3-center)<0){int q=t[i];t[i]=t[i+2];t[i+2]=q;}}}
    static void Tube(string n,Vector3 pos,float outer,float inner,float length)
    {
        var v=new List<Vector3>();var t=new List<int>();const int N=24;
        for(int r=0;r<4;r++) for(int i=0;i<N;i++){float a=i*2*Mathf.PI/N;float rad=r<2?outer:inner;v.Add(pos+new Vector3(Mathf.Cos(a)*rad,Mathf.Sin(a)*rad,(r%2-.5f)*length));}
        for(int i=0;i<N;i++){int j=(i+1)%N;Quad(t,i,i+N,j+N,j);Quad(t,i+2*N,j+2*N,j+3*N,i+3*N);Quad(t,i+N,i+3*N,j+3*N,j+N);Quad(t,i,j,j+2*N,i+2*N);}
        for(int i=0;i<t.Count;i+=3){int q=t[i];t[i]=t[i+2];t[i+2]=q;}
        Part("Barrel",n,silver,v,t);
    }
    static void Socket(Transform parent,string name,float x,float y,float z) {var t=new GameObject(name).transform;t.SetParent(parent,false);t.localPosition=new Vector3(x,y,z)*Scale;}

    // The main asset is a complete geometry preview, not an empty Mesh placeholder.
    // The prefab continues using its material-assigned individual mesh subassets.
    static void UpdateCombinedMesh(Mesh combined, IList<Mesh> parts)
    {
        var instances = new CombineInstance[parts.Count];
        for (int i = 0; i < parts.Count; i++)
            instances[i] = new CombineInstance { mesh = parts[i], transform = Matrix4x4.identity };
        combined.Clear();
        combined.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        combined.CombineMeshes(instances, true, false);
        combined.name = "Sentinel_Rifle_Meshes";
        combined.RecalculateBounds();
        EditorUtility.SetDirty(combined);
    }

    [MenuItem("Tools/Halazia/Sentinel Rifle/Repair Main Mesh Preview")]
    public static void RepairMeshPreview()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var combined = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        if (prefab == null || combined == null) throw new InvalidOperationException("Build the rifle first.");
        var parts = new List<Mesh>();
        foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>())
        {
            if (filter.sharedMesh == null || filter.sharedMesh == combined)
                throw new InvalidOperationException("Invalid rifle mesh reference.");
            parts.Add(filter.sharedMesh);
        }
        UpdateCombinedMesh(combined, parts);
        AssetDatabase.SaveAssetIfDirty(combined);
        Verify();
    }

    [MenuItem("Tools/Halazia/Sentinel Rifle/Verify and Render")]
    public static void Verify()
    {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath); if(prefab==null) throw new Exception("Missing prefab");
        int triangles=0;Bounds bounds=default;bool first=true;
        foreach(var f in prefab.GetComponentsInChildren<MeshFilter>()) {
            if(f.sharedMesh==null||f.sharedMesh.vertexCount==0||!AssetDatabase.Contains(f.sharedMesh))throw new Exception("Invalid mesh: "+f.name);
            var r=f.GetComponent<MeshRenderer>();if(r==null||r.sharedMaterial==null||r.sharedMaterial.shader==null||!r.sharedMaterial.shader.isSupported)throw new Exception("Invalid material: "+f.name);
            triangles+=f.sharedMesh.triangles.Length/3;if(first){bounds=f.sharedMesh.bounds;first=false;}else bounds.Encapsulate(f.sharedMesh.bounds);
        }
        foreach(string n in new[]{"Grip_Socket","Support_Hand_Socket","Muzzle_Point","Sight_Point","Ejection_Point"})if(prefab.transform.Find("Sockets/"+n)==null)throw new Exception("Missing socket "+n);
        var combined = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        if (combined == null || combined.vertexCount == 0 || combined.triangles.Length / 3 != triangles ||
            Vector3.Distance(combined.bounds.size, bounds.size) > .001f)
            throw new Exception("Main rifle mesh preview is empty or does not match the prefab.");
        if(prefab.transform.localScale!=Vector3.one || bounds.size.z<1.45f || bounds.size.z>1.6f) throw new Exception("Incorrect scale or length");
        if(prefab.GetComponentsInChildren<MonoBehaviour>(true).Length!=0)throw new Exception("Unexpected gameplay component");
        Directory.CreateDirectory(Output);
        Render(prefab,"side",new Vector3(-2,0,0),bounds);
        Render(prefab,"perspective",new Vector3(-2,.65f,1.05f),bounds);
        Render(prefab,"top",new Vector3(.001f,2,0),bounds);
        string report=$"PASS: Unity compiled and built prefab.\nMesh renderers: {prefab.GetComponentsInChildren<MeshRenderer>().Length}\nTriangles: {triangles}\nBounds metres: {bounds.size}\nPivot: grip hold point, +Z forward, +Y up, unit root scale.\nFive sockets verified; meshes and materials persisted; no gameplay scripts.\n";
        report += $"Main mesh preview: {combined.vertexCount} vertices, {combined.triangles.Length / 3} triangles; matches prefab bounds.\n";
        File.WriteAllText(Output+"/verification.txt",report);Debug.Log("SENTINEL_RIFLE_VERIFY_PASS: "+report);
    }
    public static void Render(GameObject prefab,string name,Vector3 direction,Bounds bounds)
    {
        var preview=new PreviewRenderUtility();Texture2D tex=null;bool asyncCompilation=ShaderUtil.allowAsyncCompilation;
        try {
            ShaderUtil.allowAsyncCompilation=false;
            var instance=Object.Instantiate(prefab);preview.AddSingleGO(instance);
            preview.camera.orthographic=true;preview.camera.orthographicSize=bounds.size.z*.34f;preview.camera.nearClipPlane=.01f;preview.camera.farClipPlane=20;
            preview.camera.transform.position=bounds.center+direction.normalized*4;preview.camera.transform.LookAt(bounds.center);
            preview.camera.clearFlags=CameraClearFlags.SolidColor;preview.camera.backgroundColor=new Color(.055f,.065f,.085f);
            preview.lights[0].intensity=1.5f;preview.lights[0].transform.rotation=preview.camera.transform.rotation*Quaternion.Euler(25,-30,0);
            preview.lights[1].intensity=1.2f;preview.lights[1].transform.rotation=preview.camera.transform.rotation*Quaternion.Euler(-15,40,0);
            preview.ambientColor=new Color(.27f,.29f,.33f);
            preview.BeginStaticPreview(new Rect(0,0,1600,900));preview.Render();tex=preview.EndStaticPreview();File.WriteAllBytes(Output+"/"+name+".png",tex.EncodeToPNG());
        } finally {ShaderUtil.allowAsyncCompilation=asyncCompilation;if(tex!=null)Object.DestroyImmediate(tex);preview.Cleanup();}
    }
}
