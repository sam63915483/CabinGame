namespace Cabin.Build
{
    /// <summary>Something the player can press E on: racks, the cutting table.</summary>
    public interface IInteractable
    {
        /// <summary>One line for the HUD, or null when there's nothing to do right now.</summary>
        string Prompt(BuildToolbelt belt);
        void Interact(BuildToolbelt belt);
    }
}
