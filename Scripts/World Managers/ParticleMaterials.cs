using UnityEngine;
using UnityEngine.Rendering;

// Materials for particles and simple objects made by code.
// The URP particle shaders lose their "transparent" and "alpha clip" versions in a build (nothing in the project uses them,
// so Unity leaves them out) and particles turn into squares, leaves get a white background and things go pink.
// "Game/Particle Soft" (Assets/Resources/GameParticleSoft.shader) has no such versions, so it looks the same in the build.
public static class ParticleMaterials
{
    private static Shader shader;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => shader = null;

    static Shader GetShader()
    {
        if (shader == null) shader = Shader.Find("Game/Particle Soft");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        return shader;
    }

    // See-through (or additive) material. cutoff > 0 = cut-out leaves/dots instead (no blending). lit = darkens with the sun
    public static Material Create(string name, Texture texture, bool additive = false, float cutoff = 0f, bool lit = false, bool doubleSided = true, bool vertexColors = true)
    {
        Material m = new Material(GetShader()) { name = name };
        if (m.HasProperty("_VertexColor")) m.SetFloat("_VertexColor", vertexColors ? 1f : 0f);
        if (texture != null)
        {
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", texture);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", texture);
        }
        bool cut = cutoff > 0f;
        if (m.HasProperty("_SrcBlend"))
        {
            m.SetFloat("_SrcBlend", (float)(cut ? BlendMode.One : BlendMode.SrcAlpha));
            m.SetFloat("_DstBlend", (float)(cut ? BlendMode.Zero : additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            m.SetFloat("_ZWrite", cut ? 1f : 0f);
        }
        if (m.HasProperty("_Cutoff")) m.SetFloat("_Cutoff", cutoff);
        if (m.HasProperty("_Lit")) m.SetFloat("_Lit", lit ? 1f : 0f);
        if (m.HasProperty("_Cull")) m.SetFloat("_Cull", doubleSided ? 0f : 2f);
        m.renderQueue = cut ? (int)RenderQueue.AlphaTest : (int)RenderQueue.Transparent;
        return m;
    }

    // Solid colored material (arrow shaft and the like)
    public static Material CreateSolid(string name, Color color)
    {
        Material m = new Material(GetShader()) { name = name };
        if (m.HasProperty("_VertexColor")) m.SetFloat("_VertexColor", 0f); // meshes made by code have no vertex colors
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        if (m.HasProperty("_Color")) m.SetColor("_Color", color);
        if (m.HasProperty("_SrcBlend"))
        {
            m.SetFloat("_SrcBlend", (float)BlendMode.One);
            m.SetFloat("_DstBlend", (float)BlendMode.Zero);
            m.SetFloat("_ZWrite", 1f);
            m.SetFloat("_Cull", 2f);
            m.SetFloat("_Lit", 1f);
        }
        m.renderQueue = (int)RenderQueue.Geometry;
        return m;
    }

    private static Texture2D softDot;

    // A soft round dot: white in the middle, transparent at the edges
    public static Texture2D SoftDot()
    {
        if (softDot != null) return softDot;
        const int size = 32;
        softDot = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Soft Dot", wrapMode = TextureWrapMode.Clamp };
        float r = size * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r)) / r;
                softDot.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01((1f - d) * 2.5f)));
            }
        softDot.Apply();
        softDot.hideFlags = HideFlags.HideAndDontSave;
        return softDot;
    }

    // Shared 1x1 quad facing -Z (leaf particles and leaf piles)
    private static Mesh quad;
    public static Mesh Quad()
    {
        if (quad != null) return quad;
        quad = new Mesh { name = "Leaf Quad" };
        quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
        quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
        quad.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        quad.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
        quad.RecalculateBounds();
        return quad;
    }

    // Copies the picture and makes its background (the corner colour) transparent. Done on the GPU, so no "Read/Write" needed.
    // tolerance = how different from the background a pixel must be to stay
    public static Texture2D RemoveBackground(Texture2D source, float tolerance)
    {
        int w = source.width, h = source.height;
        float scale = Mathf.Min(1f, 256f / Mathf.Max(w, h)); // small copy is enough for a leaf
        w = Mathf.Max(4, Mathf.RoundToInt(w * scale));
        h = Mathf.Max(4, Mathf.RoundToInt(h * scale));

        RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        RenderTexture previous = RenderTexture.active;
        Graphics.Blit(source, rt);
        RenderTexture.active = rt;
        var copy = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = source.name + " (no background)", wrapMode = TextureWrapMode.Clamp };
        copy.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);

        Color32[] px = copy.GetPixels32();
        Color32[] corners = { px[0], px[w - 1], px[(h - 1) * w], px[h * w - 1] };
        int transparentCorners = 0;
        float r = 0f, g = 0f, b = 0f;
        foreach (Color32 c in corners)
        {
            if (c.a < 128) transparentCorners++;
            r += c.r; g += c.g; b += c.b;
        }
        if (transparentCorners >= 2)
        {
            Object.Destroy(copy);
            return source; // the picture already has a transparent background
        }
        Color bg = new Color(r / 1020f, g / 1020f, b / 1020f);

        for (int i = 0; i < px.Length; i++)
        {
            Color c = px[i];
            float diff = Mathf.Max(Mathf.Abs(c.r - bg.r), Mathf.Abs(c.g - bg.g), Mathf.Abs(c.b - bg.b));
            float alpha = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(tolerance * 0.5f, tolerance, diff));
            px[i].a = (byte)(alpha * px[i].a);
        }
        copy.SetPixels32(px);
        copy.Apply(true);
        return copy;
    }
}
