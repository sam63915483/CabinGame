using System.Collections.Generic;
using System.IO;
using Cabin.Bench;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Cabin.Editor.Bench
{
    /// <summary>
    /// Tools/Cabin/Bench/Write Plan: reads the World scene and writes Temp/bench/plan.json — the stops (spawn, the
    /// highest ground near it, the thickest woods 500 m out, the nearest lakeshore) and the settings combos the
    /// bench sweeps. Deterministic: the same scene gives the same plan, so runs compare.
    /// </summary>
    public static class BenchPlanWriter
    {
        public const string PlanPath = "Temp/bench/plan.json";

        [MenuItem("Tools/Cabin/Bench/Write Plan", priority = 0)]
        public static void Write()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.name != "World")
            {
                if (scene.isDirty) EditorSceneManager.SaveOpenScenes();
                EditorSceneManager.OpenScene("Assets/_Project/Scenes/World.unity", OpenSceneMode.Single);
            }
            var player = GameObject.Find("Player");
            if (player == null) { Debug.LogError("[Bench] no Player in the World scene"); return; }
            Vector3 spawn = player.transform.position;
            var terrains = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None);

            var plan = new BenchPlan { stops = Stops(spawn, terrains), combos = Combos() };
            Directory.CreateDirectory(Path.GetDirectoryName(PlanPath));
            File.WriteAllText(PlanPath, JsonUtility.ToJson(plan, true));
            Debug.Log($"[Bench] plan: {string.Join(", ", System.Array.ConvertAll(plan.stops, s => $"{s.name} ({s.x:F0}, {s.z:F0})"))} → {PlanPath}");
        }

        static BenchStop[] Stops(Vector3 spawn, Terrain[] terrains)
        {
            float Height(float x, float z)
            {
                foreach (var t in terrains)
                {
                    Vector3 o = t.transform.position, s = t.terrainData.size;
                    if (x >= o.x && x <= o.x + s.x && z >= o.z && z <= o.z + s.z) return t.SampleHeight(new Vector3(x, 0f, z)) + o.y;
                }
                return float.NegativeInfinity;
            }

            // The highest ground within 450 m, looking back over the spawn.
            Vector3 hill = spawn;
            float best = float.NegativeInfinity;
            for (float dz = -450f; dz <= 450f; dz += 10f)
            for (float dx = -450f; dx <= 450f; dx += 10f)
            {
                if (dx * dx + dz * dz > 450f * 450f) continue;
                float h = Height(spawn.x + dx, spawn.z + dz);
                if (h > best) { best = h; hill = new Vector3(spawn.x + dx, h, spawn.z + dz); }
            }

            // The thickest woods on a ring 500 m out.
            var trees = new List<Vector3>();
            foreach (var t in terrains)
            {
                Vector3 o = t.transform.position, s = t.terrainData.size;
                foreach (var i in t.terrainData.treeInstances)
                {
                    var p = o + Vector3.Scale(i.position, s);
                    if ((p - spawn).sqrMagnitude < 650f * 650f) trees.Add(p);
                }
            }
            Vector3 woods = spawn + Vector3.forward * 500f;
            int most = -1;
            for (int deg = 0; deg < 360; deg += 10)
            {
                var c = spawn + Quaternion.Euler(0f, deg, 0f) * Vector3.forward * 500f;
                if (float.IsNegativeInfinity(Height(c.x, c.z))) continue;
                int n = 0;
                foreach (var p in trees) if ((p.x - c.x) * (p.x - c.x) + (p.z - c.z) * (p.z - c.z) < 40f * 40f) n++;
                if (n > most) { most = n; woods = c; }
            }
            // Not inside a trunk: the nearest spot with no tree within 3 m.
            for (float ring = 0f; ring < 20f; ring += 1f)
            for (int deg = 0; deg < 360; deg += 30)
            {
                var c = woods + Quaternion.Euler(0f, deg, 0f) * Vector3.forward * ring;
                bool clear = true;
                foreach (var p in trees) if ((p.x - c.x) * (p.x - c.x) + (p.z - c.z) * (p.z - c.z) < 3f * 3f) { clear = false; break; }
                if (clear) { woods = c; ring = 99f; break; }
            }

            // The nearest water: the closest vertex of any lake or river mesh (not the sea) is on the waterline, give
            // or take the skirt under the shore; stand 8 m back from it, facing it.
            var water = GameObject.Find("Map/Water");
            Vector3 shore = spawn;
            float shoreYaw = 0f;
            if (water != null)
            {
                Vector3 nearest = spawn;
                float nd = float.MaxValue;
                foreach (var f in water.GetComponentsInChildren<MeshFilter>())
                {
                    if (f.name == "Sea" || f.sharedMesh == null) continue;
                    var l2w = f.transform.localToWorldMatrix;
                    foreach (var v in f.sharedMesh.vertices)
                    {
                        var p = l2w.MultiplyPoint3x4(v);
                        float d = (p.x - spawn.x) * (p.x - spawn.x) + (p.z - spawn.z) * (p.z - spawn.z);
                        if (d < nd) { nd = d; nearest = p; }
                    }
                }
                if (nd < float.MaxValue)
                {
                    Vector3 dir = new Vector3(nearest.x - spawn.x, 0f, nearest.z - spawn.z);
                    if (dir.sqrMagnitude < 1f) dir = Vector3.forward;
                    dir.Normalize();
                    shore = nearest - dir * 8f;
                    shoreYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                }
                else Debug.LogWarning("[Bench] no lake or river mesh found; the shore stop stays at the spawn");
            }

            float toSpawn = Mathf.Atan2(spawn.x - hill.x, spawn.z - hill.z) * Mathf.Rad2Deg;
            // The headings the first full run found slowest at each stop; every stop sweeps the combos.
            return new[]
            {
                new BenchStop { name = "spawn", x = spawn.x, z = spawn.z, yaws = new[] { 270f }, sweep = true },
                new BenchStop { name = "hill", x = hill.x, z = hill.z, yaws = new[] { toSpawn }, pitch = 8f, sweep = true },
                new BenchStop { name = "woods", x = woods.x, z = woods.z, yaws = new[] { 90f }, sweep = true },
                new BenchStop { name = "shore", x = shore.x, z = shore.z, yaws = new[] { shoreYaw }, pitch = 5f, sweep = true },
            };
        }

        static BenchCombo[] Combos() => new[]
        {
            new BenchCombo { name = "default" },
            new BenchCombo { name = "depth_tex_off", depthTexture = 0 },
            new BenchCombo { name = "opaque_tex_off", opaqueTexture = 0 },
            new BenchCombo { name = "copies_off", depthTexture = 0, opaqueTexture = 0 },
            new BenchCombo { name = "hdr_off", hdr = 0 },
            new BenchCombo { name = "viewmodel_off", viewModel = 0 },
            new BenchCombo { name = "water_off", water = 0 },
            new BenchCombo { name = "terrain_shadows_off", terrainShadows = 0 },
            new BenchCombo { name = "terrain_trees_off", terrainTrees = 0 },
            new BenchCombo { name = "grass_250m", grassDistance = 250 },
            new BenchCombo { name = "grass_80m", grassDistance = 80 },
            new BenchCombo { name = "basemap_250", basemapDistance = 250f },
            new BenchCombo { name = "pixel_error_8", pixelError = 8f },
            new BenchCombo { name = "lod_bias_0.7", lodBias = 0.7f },
            new BenchCombo { name = "lod_bias_1.5", lodBias = 1.5f },
            new BenchCombo { name = "shadows_100m_2c", shadowDistance = 100f, shadowCascades = 2 },
            new BenchCombo { name = "shadows_120m_2c", shadowDistance = 120f, shadowCascades = 2 },
            new BenchCombo { name = "grass_near_12", grassNearMesh = 12f },
            new BenchCombo { name = "grass_fade_35", grassFadeStart = 0.3f, grassFadeFloor = 0.35f },
            new BenchCombo { name = "grass_light", grassNearMesh = 12f, grassFadeStart = 0.3f, grassFadeFloor = 0.35f },
            new BenchCombo { name = "msaa_off", msaa = 0 },
            new BenchCombo { name = "msaa_2x", msaa = 1 },
            new BenchCombo { name = "shadows_off", shadows = 0 },
            new BenchCombo { name = "shadows_low", shadows = 1 },
            new BenchCombo { name = "shadows_medium", shadows = 2 },
            new BenchCombo { name = "quality_medium", quality = 1 },
            new BenchCombo { name = "quality_low", quality = 0 },
            new BenchCombo { name = "grass_off", grass = 0 },
            new BenchCombo { name = "grass_40m", grassDistance = 40 },
            new BenchCombo { name = "grass_density_50", grassDensity = 0.5f },
            new BenchCombo { name = "far_forest_off", farForest = 0 },
            new BenchCombo { name = "trees_150m", treeDistance = 150 },
            new BenchCombo { name = "post_off", post = 0 },
            new BenchCombo { name = "atmosphere_off", atmosphere = 0 },
            new BenchCombo { name = "shafts_off", sunShafts = 0 },
            new BenchCombo { name = "haze_off", haze = 0 },
            new BenchCombo { name = "clouds_off", clouds = 0 },
            new BenchCombo { name = "cloud_shadows_off", cloudShadows = 0 },
            new BenchCombo { name = "render_scale_50", renderScale = 0.5f },
            new BenchCombo { name = "default_end" }, // the default again: drift (heat) over the sweep shows here
        };
    }
}
