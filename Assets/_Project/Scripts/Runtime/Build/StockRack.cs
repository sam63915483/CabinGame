using UnityEngine;

namespace Cabin.Build
{
    /// <summary>A rack holding one kind and length of stock. Unlimited supply in creative mode.</summary>
    public sealed class StockRack : MonoBehaviour, IInteractable
    {
        [SerializeField] StockKind kind;
        [SerializeField] float length = 8f * Units.Foot;

        public StockKind Kind => kind;
        public float Length => length;

        public string Label => kind switch
        {
            StockKind.DeckBlock => Stock.Name(kind),
            StockKind.Plywood => $"Plywood 4×{Mathf.RoundToInt((length > 0f ? length : 8f * Units.Foot) / Units.Foot)}",
            _ => $"{Stock.Name(kind)} × {Units.FeetInches(length)}",
        };

        public void Set(StockKind k, float len)
        {
            kind = k;
            length = len;
        }

        public string Prompt(BuildToolbelt belt) =>
            belt.Carry.Holding ? $"[E] Drop what you're holding first\n{Label}" : $"[E] Take one\n{Label}";

        public void Interact(BuildToolbelt belt)
        {
            if (belt.Carry.Holding)
            {
                belt.Say("Your hands are full");
                return;
            }
            belt.Carry.TakeNew(kind, length);
            belt.Say($"Took a {Label}");
        }
    }
}
