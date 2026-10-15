#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

// Makes sure the shaders that code looks up by name (Shader.Find) are part of every build.
// Without this they are left out and the things that use them turn pink or become squares in the build.
[InitializeOnLoad]
public class EnsureRuntimeShaders : IPreprocessBuildWithReport
{
    static readonly string[] shaderNames =
    {
        "Game/Particle Soft",
        "Sprites/Default",
        "Universal Render Pipeline/Lit",
        "Universal Render Pipeline/Simple Lit",
        "Universal Render Pipeline/Unlit",
        "Universal Render Pipeline/Particles/Unlit",
        "Universal Render Pipeline/Particles/Simple Lit",
        "Universal Render Pipeline/Particles/Lit",
    };

    public int callbackOrder => -100;

    static EnsureRuntimeShaders() => EditorApplication.delayCall += () => Ensure(false);

    public void OnPreprocessBuild(BuildReport report) => Ensure(true);

    [MenuItem("Tools/Include Runtime Shaders In Builds")]
    static void MenuItem() => Ensure(true);

    static void Ensure(bool log)
    {
        var settings = AssetDatabase.LoadAssetAtPath<GraphicsSettings>("ProjectSettings/GraphicsSettings.asset");
        if (settings == null) { if (log) Debug.LogWarning("EnsureRuntimeShaders: GraphicsSettings.asset not found."); return; }

        var so = new SerializedObject(settings);
        SerializedProperty list = so.FindProperty("m_AlwaysIncludedShaders");
        if (list == null) return;

        bool changed = false;
        foreach (string name in shaderNames)
        {
            Shader shader = Shader.Find(name);
            if (shader == null) continue;

            bool found = false;
            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == shader) { found = true; break; }
            if (found) continue;

            list.InsertArrayElementAtIndex(list.arraySize);
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
            changed = true;
            if (log) Debug.Log("EnsureRuntimeShaders: added '" + name + "' to Always Included Shaders.");
        }

        if (changed)
        {
            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
        }
    }
}
#endif
