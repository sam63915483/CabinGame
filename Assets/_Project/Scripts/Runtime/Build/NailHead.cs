using UnityEngine;

namespace Cabin.Build
{
    /// <summary>The visible head of a driven nail. Sits on the piece it was driven through so it moves with it.</summary>
    public sealed class NailHead : MonoBehaviour
    {
        public NailRecord Nail { get; private set; }

        public void Init(NailRecord nail)
        {
            Nail = nail;
            name = $"Nail_{nail.id}";
            gameObject.layer = BuildLayers.Nail;
            foreach (var t in GetComponentsInChildren<Transform>()) t.gameObject.layer = BuildLayers.Nail;
        }
    }
}
