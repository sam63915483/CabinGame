using System.Collections.Generic;
using UnityEngine;

namespace Cabin.Build
{
    /// <summary>
    /// A nail in flight from the gun's nose, then — if it didn't join two pieces — stuck where it hit: shank buried,
    /// head proud, riding along with whatever it's in. Purely visual; it holds nothing together and isn't saved.
    /// When it carries a real driven nail, the nail's head appears as the shot arrives and the shot goes away.
    /// </summary>
    public sealed class NailShot : MonoBehaviour
    {
        /// <summary>Metres per second: quick, but you can still see it go.</summary>
        public const float Speed = 30f;
        /// <summary>The most stuck nails kept about; the oldest go first.</summary>
        const int MaxStuck = 300;
        static readonly Queue<NailShot> Stuck = new Queue<NailShot>();

        Vector3 target;
        Transform stickTo;
        GameObject reveal;
        TrailRenderer trail;
        bool flying;

        /// <summary>Starts the shot from <paramref name="from"/> to <paramref name="to"/>, pointing along <paramref name="direction"/>.</summary>
        public void Fire(Vector3 from, Vector3 to, Vector3 direction, Transform stickInto, GameObject revealOnArrival)
        {
            transform.SetPositionAndRotation(from, Quaternion.LookRotation(direction));
            target = to;
            stickTo = stickInto;
            reveal = revealOnArrival;
            if (reveal != null) reveal.SetActive(false);
            trail = GetComponentInChildren<TrailRenderer>();
            if (trail != null) trail.Clear();
            flying = true;
        }

        void Update()
        {
            if (!flying) return;
            transform.position = Vector3.MoveTowards(transform.position, target, Speed * Time.deltaTime);
            if ((transform.position - target).sqrMagnitude > 1e-8f) return;
            flying = false;
            if (reveal != null)
            {
                reveal.SetActive(true);
                Destroy(gameObject, trail != null ? trail.time : 0f);
                enabled = false;
                return;
            }
            // Stuck: the origin is under the head, so the shank is in the surface.
            if (stickTo != null) transform.SetParent(stickTo, true);
            if (trail != null) trail.emitting = false;
            Stuck.Enqueue(this);
            while (Stuck.Count > MaxStuck)
            {
                var old = Stuck.Dequeue();
                if (old != null) Destroy(old.gameObject);
            }
            enabled = false;
        }

        // Enter Play Mode doesn't reload the domain: forget last session's nails.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Stuck.Clear();

        /// <summary>Clears every stuck nail (the site was cleared).</summary>
        public static void ClearAll()
        {
            while (Stuck.Count > 0)
            {
                var n = Stuck.Dequeue();
                if (n != null) Destroy(n.gameObject);
            }
        }
    }
}
