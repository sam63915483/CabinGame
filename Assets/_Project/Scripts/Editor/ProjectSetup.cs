using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace Cabin.Editor
{
    /// <summary>Re-applies project settings the GPU Resident Drawer depends on (handoff §6.2).</summary>
    public static class ProjectSetup
    {
        [MenuItem("Tools/Cabin/Setup/Apply Graphics Settings")]
        public static void ApplyGraphicsSettings()
        {
            // EditorGraphicsSettings.batchRendererGroupShaderStrippingMode is read-only, so write the serialized field.
            var settings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
            settings.FindProperty("m_BrgStripping").intValue = (int)BatchRendererGroupStrippingMode.KeepAll;
            settings.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            Debug.Log($"[Cabin] BatchRendererGroup Variants = {EditorGraphicsSettings.batchRendererGroupShaderStrippingMode}");
        }
    }
}
