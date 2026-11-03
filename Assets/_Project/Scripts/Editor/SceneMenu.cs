using UnityEditor;
using UnityEditor.SceneManagement;

namespace Cabin.Editor
{
    /// <summary>Quick switching between the three project scenes.</summary>
    public static class SceneMenu
    {
        const string ScenesDir = "Assets/_Project/Scenes/";

        [MenuItem("Tools/Cabin/Scenes/Open Boot", priority = 1)]
        static void OpenBoot() => Open("Boot");

        [MenuItem("Tools/Cabin/Scenes/Open World", priority = 2)]
        static void OpenWorld() => Open("World");

        [MenuItem("Tools/Cabin/Scenes/Open TestPlatform", priority = 3)]
        static void OpenTestPlatform() => Open("TestPlatform");

        static void Open(string scene)
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(ScenesDir + scene + ".unity");
        }
    }
}
