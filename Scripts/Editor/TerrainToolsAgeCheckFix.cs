#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;

// Silences the "Checking lifetime of RenderTextures but m_AgeCheckAdded = false" error spam from the Terrain Tools package.
// It happens because the package keeps a static flag that gets out of sync when Domain Reload is disabled.
// This just keeps that flag set to true (Editor only, does nothing in builds).
[InitializeOnLoad]
static class TerrainToolsAgeCheckFix
{
    static FieldInfo field;
    static bool searched;

    static TerrainToolsAgeCheckFix()
    {
        EditorApplication.update += Fix;
        EditorApplication.playModeStateChanged += _ => Fix();
    }

    static void Fix()
    {
        if (field == null)
        {
            if (searched) return;
            searched = true;
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = asm.GetType("UnityEditor.TerrainTools.RTUtils");
                if (t == null) continue;
                field = t.GetField("m_AgeCheckAdded", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                if (field != null) break;
            }
            if (field == null) return;
        }
        try { if (!(bool)field.GetValue(null)) field.SetValue(null, true); } catch { }
    }
}
#endif
