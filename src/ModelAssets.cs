using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace Horse.Floppy;

internal static class ModelAssets
{
    private sealed class Part
    {
        internal string Assembly;
        internal Mesh Mesh;
        internal int Material;
    }
    private sealed class Surface
    {
        internal string Name;
        internal Color Color;
        internal float Emission;
    }
    private static readonly List<Part> parts = new List<Part>();
    private static readonly List<Surface> surfaces = new List<Surface>();
    private static Texture2D plastic;
    private static Texture2D metal;
    private static string loggedShader;
    internal static bool Ready => parts.Count > 0;

    internal static void Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Floppy.Model")
            ?? throw new InvalidDataException("Embedded Floppy model missing.");
        using var reader = new BinaryReader(stream, Encoding.UTF8);
        if (Encoding.ASCII.GetString(reader.ReadBytes(8)) != "FLOPPY01")
            throw new InvalidDataException("Unknown Floppy mesh version.");
        int materialCount = Count(reader, 64);
        for (int i = 0; i < materialCount; i++)
            surfaces.Add(new Surface { Name = ReadString(reader),
                Color = new Color(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                Emission = reader.ReadSingle() });
        int count = Count(reader, 64);
        for (int i = 0; i < count; i++)
        {
            string assembly = ReadString(reader);
            int material = reader.ReadInt32();
            if (material < 0 || material >= surfaces.Count) throw new InvalidDataException("Bad material index.");
            int vertices = Count(reader, 300000);
            if (vertices % 3 != 0) throw new InvalidDataException("Incomplete triangle.");
            var positions = new Vector3[vertices];
            var normals = new Vector3[vertices];
            var uv = new Vector2[vertices];
            var indices = new int[vertices];
            for (int v = 0; v < vertices; v++)
            {
                positions[v] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                normals[v] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                uv[v] = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                indices[v] = v;
            }
            var mesh = new Mesh { name = "Floppy_" + assembly + "_" + i,
                indexFormat = vertices > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.vertices = positions;
            mesh.normals = normals;
            mesh.uv = uv;
            mesh.triangles = indices;
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            parts.Add(new Part { Assembly = assembly, Mesh = mesh, Material = material });
        }
        if (stream.Position != stream.Length) throw new InvalidDataException("Trailing model data.");
        plastic = LoadTexture("Plastic");
        metal = LoadTexture("Metal");
    }

    private static int Count(BinaryReader reader, int maximum)
    {
        int count = reader.ReadInt32();
        if (count < 0 || count > maximum) throw new InvalidDataException("Invalid mesh count.");
        return count;
    }
    private static string ReadString(BinaryReader reader)
        => Encoding.UTF8.GetString(reader.ReadBytes(Count(reader, 1024)));

    internal static GameObject Instantiate(Transform parent)
    {
        var root = new GameObject("Floppy");
        root.SetActive(false);
        root.transform.SetParent(parent, false);
        try
        {
            var tray = new GameObject("Tray").transform;
            tray.SetParent(root.transform, false);
            var carriage = new GameObject("RailCarriage").transform;
            carriage.SetParent(root.transform, false);
            tray.gameObject.layer = 12;
            var trayTarget = tray.gameObject.AddComponent<BoxCollider>();
            trayTarget.isTrigger = true;
            trayTarget.center = ModelMetrics.TrayCenter;
            trayTarget.size = ModelMetrics.TraySize;
            // Mycopunk supplies its own sun/shadow lighting to Pastel. URP Lit
            // does not consume those globals and made the old model very dark.
            var shader = FindShader("Shader Graphs/PastelObjUV") ?? FindShader("Shader Graphs/Pastel")
                ?? FindShader("Universal Render Pipeline/Unlit");
            if (shader == null) throw new InvalidOperationException("No supported loaded shader found.");
            if (loggedShader != shader.name)
            {
                loggedShader = shader.name;
                FloppyPlugin.Log.LogInfo("Floppy surfaces: " + shader.name + "; embedded Plastic/Metal 512x512; shop module scale 0.5.");
            }
            var materials = new Material[surfaces.Count];
            for (int i = 0; i < materials.Length; i++)
            {
                var s = surfaces[i];
                var m = new Material(shader) { name = s.Name };
                SetColor(m, s.Color);
                // Explicit shadow color keeps beige plastic readable with the
                // same stylized lighting as the surrounding native props.
                if (m.HasProperty("_ShadowColor")) m.SetColor("_ShadowColor", new Color(s.Color.r * .72f, s.Color.g * .72f, s.Color.b * .72f, 1));
                bool steel = s.Name.Contains("Steel");
                var texture = steel ? metal : s.Name.Contains("Ivory") || s.Name.Contains("Bezel") ||
                    s.Name.Contains("Rubber") || s.Name.Contains("Badge") ? plastic : Texture2D.whiteTexture;
                if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", texture);
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", texture);
                if (m.HasProperty("_SecondaryTex")) m.SetTexture("_SecondaryTex", texture);
                if (m.HasProperty("_MainTexTile")) m.SetFloat("_MainTexTile", 1f);
                if (m.HasProperty("_SecondaryTexTile")) m.SetFloat("_SecondaryTexTile", 1f);
                if (m.HasProperty("_MainBlend")) m.SetFloat("_MainBlend", 0f);
                if (m.HasProperty("_SunColorIntensity")) m.SetFloat("_SunColorIntensity", .5f);
                if (m.HasProperty("_Infection")) m.SetFloat("_Infection", 0f);
                if (m.HasProperty("_SpecularMap")) m.SetTexture("_SpecularMap", Texture2D.blackTexture);
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", .22f);
                if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", steel ? .65f : 0f);
                if (m.HasProperty("_Emission")) m.SetFloat("_Emission", s.Emission);
                if (m.HasProperty("_UseEmission")) m.SetFloat("_UseEmission", s.Emission > 0 ? 1 : 0);
                if (s.Emission > 0)
                {
                    m.EnableKeyword("_EMISSION");
                    if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", s.Color * s.Emission);
                }
                materials[i] = m;
            }
            var owner = root.AddComponent<ModelMaterialOwner>();
            owner.Materials = materials;
            foreach (var part in parts)
            {
                var go = new GameObject(part.Assembly + "_Mesh");
                go.transform.SetParent(part.Assembly == "Tray" ? tray : part.Assembly == "RailCarriage" ? carriage : root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = part.Mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = materials[part.Material];
            }
            var socket = new GameObject("ModuleSocket").transform;
            socket.SetParent(tray, false);
            socket.localPosition = ModelMetrics.SocketCenter;
            root.layer = 12; // Match BuyableUpgrade's selectable layer.
            var collider = root.AddComponent<BoxCollider>();
            collider.center = ModelMetrics.CaseCenter;
            collider.size = ModelMetrics.CaseSize;
            return root;
        }
        catch
        {
            UnityEngine.Object.Destroy(root);
            throw;
        }
    }

    internal static void SetColor(Material material, Color color)
    {
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
    }

    private static Shader FindShader(string name)
    {
        var shader = Shader.Find(name);
        return shader != null && shader.isSupported ? shader : null;
    }

    private static Texture2D LoadTexture(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Floppy.Texture." + name)
            ?? throw new InvalidDataException("Embedded Floppy surface missing: " + name);
        using var reader = new BinaryReader(stream, Encoding.UTF8);
        if (Encoding.ASCII.GetString(reader.ReadBytes(8)) != "FLPTEX01")
            throw new InvalidDataException("Unknown Floppy surface format: " + name);
        int width = reader.ReadInt32(), height = reader.ReadInt32();
        if (width <= 0 || height <= 0 || width > 1024 || height > 1024 || stream.Length - stream.Position != width * height * 3)
            throw new InvalidDataException("Invalid Floppy surface dimensions: " + name);
        var pixels = reader.ReadBytes(width * height * 3);
        var texture = new Texture2D(width, height, TextureFormat.RGB24, true, false)
            { name = "Floppy_" + name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 2 };
        try
        {
            // Only mip 0 is embedded; Apply builds the remaining mip chain.
            texture.SetPixelData(pixels, 0, 0);
            texture.Apply(true, true);
            return texture;
        }
        catch
        {
            UnityEngine.Object.Destroy(texture);
            throw;
        }
    }

    internal static void Dispose()
    {
        foreach (var part in parts) if (part.Mesh != null) UnityEngine.Object.Destroy(part.Mesh);
        parts.Clear();
        surfaces.Clear();
        if (plastic != null) UnityEngine.Object.Destroy(plastic);
        if (metal != null) UnityEngine.Object.Destroy(metal);
        plastic = metal = null;
        loggedShader = null;
    }
}

internal sealed class ModelMaterialOwner : MonoBehaviour
{
    internal Material[] Materials;
    private void OnDestroy()
    {
        if (Materials != null)
            foreach (var material in Materials) if (material != null) Destroy(material);
    }
}
