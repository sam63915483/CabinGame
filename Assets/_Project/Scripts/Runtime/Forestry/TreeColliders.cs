using System.Collections.Generic;
using UnityEngine;

namespace Cabin.Forestry
{
    /// <summary>
    /// Trunks you can walk into. The map has a quarter of a million terrain trees, and giving each a collider through
    /// its prefab made the physics world carry every one of them from the first frame (seconds of loading, and
    /// memory). Instead a small pool of capsules follows the player: every <see cref="Interval"/> seconds the terrain
    /// trees within <see cref="Radius"/> metres are looked up through the forest's index and the capsules are put on
    /// their trunks, sized from the tree's measured trunk. A tree being chopped has its own collider
    /// (<see cref="ChoppingTree"/>) and is no longer a terrain tree. On the World scene's map, beside the forest.
    /// </summary>
    [RequireComponent(typeof(Forest))]
    public sealed class TreeColliders : MonoBehaviour
    {
        const float Interval = 0.2f, Radius = 24f;
        const int PoolSize = 96;

        Forest forest;
        Transform player;
        readonly List<CapsuleCollider> pool = new List<CapsuleCollider>();
        readonly List<TreeTarget> nearby = new List<TreeTarget>();
        float next;

        void Awake()
        {
            forest = GetComponent<Forest>();
            var holder = new GameObject("TreeColliders");
            holder.transform.SetParent(transform, false);
            for (int i = 0; i < PoolSize; i++)
            {
                var go = new GameObject("Trunk");
                go.transform.SetParent(holder.transform, false);
                var capsule = go.AddComponent<CapsuleCollider>();
                go.SetActive(false);
                pool.Add(capsule);
            }
        }

        void Update()
        {
            if (Time.time < next) return;
            next = Time.time + Interval;
            if (player == null)
            {
                var cam = Camera.main;
                if (cam == null) return;
                player = cam.transform;
            }
            nearby.Clear();
            forest.Within(player.position, Radius, nearby);
            int n = Mathf.Min(nearby.Count, pool.Count);
            for (int i = 0; i < n; i++)
            {
                var t = nearby[i];
                var capsule = pool[i];
                float radius = Mathf.Clamp(t.Data.RadiusAt(1f) * t.Scale.x, 0.12f, 1.2f);
                float height = Mathf.Clamp(t.Data.Height * t.Scale.y, radius * 2f, 8f);
                capsule.radius = radius;
                capsule.height = height;
                capsule.center = new Vector3(0f, height * 0.5f, 0f);
                capsule.transform.SetPositionAndRotation(t.Base, t.Rotation);
                capsule.gameObject.SetActive(true);
            }
            for (int i = n; i < pool.Count; i++) pool[i].gameObject.SetActive(false);
        }
    }
}
