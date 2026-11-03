using Cabin.Map;
using UnityEditor;
using UnityEngine;

namespace Cabin.Editor.Map
{
    /// <summary>
    /// Place names on the map in the Scene view (Tools/Cabin/Map/Show Place Names), so you can find Grand Lake or the
    /// Shubenacadie on a province shrunk 68 times, and Tools/Cabin/Map/Go To/… to fly the Scene view to one. Editor
    /// only; the game shows nothing.
    /// </summary>
    [InitializeOnLoad]
    public static class MapLabels
    {
        const string PrefKey = "Cabin.MapLabels";

        /// <summary>Name, longitude, latitude.</summary>
        static readonly (string name, double lon, double lat)[] Places =
        {
            ("Halifax", -63.575, 44.648),
            ("Dartmouth", -63.57, 44.67),
            ("Grand Lake", -63.60, 44.91),
            ("Shubenacadie River", -63.48, 45.08),
            ("Truro", -63.28, 45.365),
            ("Minas Basin", -64.0, 45.25),
            ("Lake Rossignol", -65.13, 44.21),
            ("Kejimkujik (spawn)", -65.22, 44.42),
            ("Yarmouth", -66.12, 43.84),
            ("Annapolis Valley", -64.8, 45.0),
            ("LaHave River", -64.5, 44.3),
            ("Lunenburg", -64.31, 44.377),
            ("St. Margaret's Bay", -63.95, 44.58),
            ("Antigonish", -61.99, 45.62),
            ("Canso Causeway", -61.41, 45.646),
            ("Bras d'Or Lake", -60.85, 45.9),
            ("Lake Ainslie", -61.19, 46.13),
            ("Cape Breton Highlands", -60.75, 46.75),
            ("Margaree River", -61.1, 46.37),
            ("Sydney", -60.19, 46.14),
            ("Cobequid Mountains", -63.7, 45.5),
        };

        static GUIStyle style;

        static MapLabels() => SceneView.duringSceneGui += Draw;

        static bool Shown
        {
            get => EditorPrefs.GetBool(PrefKey, true);
            set => EditorPrefs.SetBool(PrefKey, value);
        }

        [MenuItem("Tools/Cabin/Map/Show Place Names", priority = 33)]
        static void Toggle()
        {
            Shown = !Shown;
            SceneView.RepaintAll();
        }

        [MenuItem("Tools/Cabin/Map/Show Place Names", true)]
        static bool ToggleCheck()
        {
            Menu.SetChecked("Tools/Cabin/Map/Show Place Names", Shown);
            return true;
        }

        [MenuItem("Tools/Cabin/Map/Go To/Grand Lake and the Shubenacadie", priority = 40)]
        static void GoGrandLake() => GoTo(-63.55, 44.98, 900f);
        [MenuItem("Tools/Cabin/Map/Go To/Halifax", priority = 41)]
        static void GoHalifax() => GoTo(-63.6, 44.66, 700f);
        [MenuItem("Tools/Cabin/Map/Go To/Lake Rossignol and Kejimkujik", priority = 42)]
        static void GoRossignol() => GoTo(-65.15, 44.3, 1100f);
        [MenuItem("Tools/Cabin/Map/Go To/Bras d'Or Lake", priority = 43)]
        static void GoBrasDor() => GoTo(-60.8, 45.95, 1500f);
        [MenuItem("Tools/Cabin/Map/Go To/Cape Breton Highlands", priority = 44)]
        static void GoHighlands() => GoTo(-60.75, 46.65, 1400f);

        /// <summary>The Scene view straight down over a place, <paramref name="span"/> metres across, labels on.</summary>
        static void GoTo(double lon, double lat, float span)
        {
            var view = SceneView.lastActiveSceneView;
            if (view == null) return;
            MapView.SceneViewWithoutFog();
            Shown = true;
            var p = new MapProjection(MapGenerator.Recipe()).FromGeo(lon, lat);
            view.orthographic = false;
            view.LookAtDirect(new Vector3(p.x, 0f, p.y), Quaternion.Euler(90f, 0f, 0f), span * 0.5f);
            view.Repaint();
        }

        static void Draw(SceneView view)
        {
            if (!Shown || Terrain.activeTerrains.Length == 0) return;
            var recipe = AssetDatabase.LoadAssetAtPath<MapRecipe>(MapGenerator.RecipePath);
            if (recipe == null) return;
            style ??= new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white },
            };
            var proj = new MapProjection(recipe);
            foreach (var (name, lon, lat) in Places)
            {
                var p = proj.FromGeo(lon, lat);
                var world = new Vector3(p.x, Height(p.x, p.y) + 5f, p.y);
                Handles.color = Color.white;
                Handles.DrawSolidDisc(world, Vector3.up, HandleUtility.GetHandleSize(world) * 0.04f);
                Handles.Label(world, name, style);
            }
        }

        static float Height(float x, float z)
        {
            foreach (var t in Terrain.activeTerrains)
            {
                var o = t.transform.position;
                var s = t.terrainData.size;
                if (x >= o.x && z >= o.z && x <= o.x + s.x && z <= o.z + s.z) return t.SampleHeight(new Vector3(x, 0f, z)) + o.y;
            }
            return 0f;
        }
    }
}
