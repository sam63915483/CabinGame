using UnityEngine.InputSystem;

namespace Cabin.Core
{
    /// <summary>Access to the project-wide input actions (Assets/_Project/Settings/CabinInput.inputactions).</summary>
    public static class Controls
    {
        /// <summary>Finds an action by "Map/Action" and makes sure its map is enabled.</summary>
        public static InputAction Get(string path)
        {
            var action = InputSystem.actions.FindAction(path, throwIfNotFound: true);
            if (!action.actionMap.enabled) action.actionMap.Enable();
            return action;
        }
    }
}
