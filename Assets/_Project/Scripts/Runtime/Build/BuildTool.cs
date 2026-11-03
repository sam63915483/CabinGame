using UnityEngine;

namespace Cabin.Build
{
    /// <summary>A hotbar tool: hands, nail gun or saw. Ticked every frame while selected and the mouse is captured.</summary>
    public abstract class BuildTool
    {
        protected BuildToolbelt Belt { get; private set; }

        public abstract string Name { get; }
        /// <summary>Controls, shown under the tool name.</summary>
        public abstract string Hint { get; }
        /// <summary>The hotbar picture (null: none, as for hands).</summary>
        public Texture2D Icon { get; set; }
        /// <summary>
        /// The one-line prompt under the crosshair, refreshed by <see cref="Tick"/>: empty when there's nothing to do;
        /// keys in brackets are drawn as caps ("[E] Pick up"); an optional second line is a dimmer readout.
        /// </summary>
        public string Status { get; protected set; } = "";

        public void Bind(BuildToolbelt belt) => Belt = belt;

        public virtual void OnSelected() => Status = "";
        public virtual void OnDeselected() { }
        /// <summary>Extra IMGUI drawn by the HUD while this tool is selected.</summary>
        public virtual void OnGui(float scale) { }
        public abstract void Tick();
    }
}
