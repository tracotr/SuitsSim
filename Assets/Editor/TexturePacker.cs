using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

// Put this file in any folder named "Editor" inside Assets.
// Select your metallic / roughness / (AO) textures, right click > Pack Textures.
public static class TexturePacker
{
    [MenuItem("Assets/Pack Textures/Metal + Smoothness (Built-in, URP)")]
    static void PackUrp() => Pack(false);

    [MenuItem("Assets/Pack Textures/Mask Map (HDRP)")]
    static void PackHdrp() => Pack(true);

    [MenuItem("Assets/Pack Textures/Metal + Smoothness (Built-in, URP)", true)]
    [MenuItem("Assets/Pack Textures/Mask Map (HDRP)", true)]
    static bool Validate() => Selection.GetFiltered<Texture2D>(SelectionMode.Assets).Length > 0;

    static void Pack(bool hdrp)
    {
        var sel = Selection.GetFiltered<Texture2D>(SelectionMode.Assets);

        var metal = Find(sel, "metal");
        var rough = Find(sel, "rough");
        var ao = Find(sel, "occlusion", "ambient", "_ao");

        if (rough == null)
        {
            Debug.LogError("TexturePacker: no roughness texture in selection (filename must contain 'rough').");
            return;
        }

        string namePath = AssetDatabase.GetAssetPath(metal != null ? metal : rough);

        metal = Prep(metal);
        rough = Prep(rough);
        ao = Prep(ao);

        int w = rough.width, h = rough.height;
        if ((metal != null && (metal.width != w || metal.height != h)) ||
            (ao != null && (ao.width != w || ao.height != h)))
        {
            Debug.LogError("TexturePacker: textures are different sizes. Resize them to match first.");
            return;
        }

        var m = metal != null ? metal.GetPixels32() : null;
        var r = rough.GetPixels32();
        var a = ao != null ? ao.GetPixels32() : null;

        var result = new Color32[w * h];
        for (int i = 0; i < result.Length; i++)
        {
            byte metallic = m != null ? m[i].r : (byte)0;
            byte smooth = (byte)(255 - r[i].r);
            byte occ = a != null ? a[i].r : (byte)255;

            result[i] = hdrp
                ? new Color32(metallic, occ, 0, smooth)   // HDRP mask map: R metal, G AO, B detail, A smooth
                : new Color32(metallic, 0, 0, smooth);    // URP/Built-in: R metal, A smooth
        }

        // linear = true so no sRGB conversion gets baked in
        var packed = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
        packed.SetPixels32(result);
        packed.Apply();

        string baseName = Regex.Replace(Path.GetFileNameWithoutExtension(namePath),
            @"(?i)[_\-\. ]?(metallic|metalness|metal|roughness|rough)$", "");
        string suffix = hdrp ? "_MaskMap" : "_MetalSmooth";
        string outPath = Path.Combine(Path.GetDirectoryName(namePath), baseName + suffix + ".png")
                             .Replace('\\', '/');

        File.WriteAllBytes(outPath, packed.EncodeToPNG());
        Object.DestroyImmediate(packed);
        AssetDatabase.ImportAsset(outPath);

        var imp = (TextureImporter)AssetImporter.GetAtPath(outPath);
        imp.sRGBTexture = false;
        imp.alphaSource = TextureImporterAlphaSource.FromInput;
        imp.alphaIsTransparency = false;
        imp.SaveAndReimport();

        Debug.Log("TexturePacker: wrote " + outPath);
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<Texture2D>(outPath);
    }

    static Texture2D Find(Texture2D[] tex, params string[] keys)
    {
        return tex.FirstOrDefault(t => keys.Any(k => t.name.ToLower().Contains(k)));
    }

    // Makes the texture CPU-readable, linear and uncompressed so GetPixels32 works and values are raw.
    static Texture2D Prep(Texture2D t)
    {
        if (t == null) return null;
        string path = AssetDatabase.GetAssetPath(t);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.isReadable = true;
        imp.sRGBTexture = false;
        imp.textureCompression = TextureImporterCompression.Uncompressed;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
}
