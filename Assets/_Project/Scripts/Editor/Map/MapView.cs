using UnityEditor;
using UnityEngine;

namespace Cabin.Editor.Map
{
    /// <summary>
    /// Tools/Cabin/Map/View Whole Map: the Scene view looking straight down on the whole province, with the game's
    /// distance fog turned off in the Scene view (it would white the land out from up there) and the far clip reaching
    /// past the map. The game keeps its fog.
    /// </summary>
    public static class MapView
    {
        [MenuItem("Tools/Cabin/Map/View Whole Map", priority = 31)]
        public static void ViewWholeMap()
        {
            var view = SceneView.lastActiveSceneView;
            if (view == null) return;
            ClearView(view);
            var recipe = MapGenerator.Recipe();
            view.orthographic = false;
            view.LookAtDirect(new Vector3(0f, 0f, 0f), Quaternion.Euler(90f, 0f, 0f), Mathf.Max(recipe.WorldWidth, recipe.WorldDepth) * 0.55f);
            view.Repaint();
        }

        [MenuItem("Tools/Cabin/Map/Scene View Without Fog", priority = 32)]
        public static void SceneViewWithoutFog()
        {
            foreach (SceneView view in SceneView.sceneViews) ClearView(view);
        }

        static void ClearView(SceneView view)
        {
            view.sceneViewState.showFog = false;
            // Clipping follows the zoom, so up high it reaches past the whole map and close up it still works.
            var settings = view.cameraSettings;
            settings.dynamicClip = true;
            settings.farClip = Mathf.Max(settings.farClip, 30000f);
            view.cameraSettings = settings;
            view.Repaint();
        }
    }
}
