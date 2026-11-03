using UnityEngine;

namespace Cabin.Player
{
    /// <summary>Whatever the player has in their right hand (and left, for a two-handed tool), for the arms to reach for.</summary>
    public interface IHeldItem
    {
        /// <summary>Where the right wrist goes and how the hand is turned (world space); false with nothing in hand.</summary>
        bool TryGetGrip(out Vector3 wrist, out Quaternion hand);

        /// <summary>Where the left wrist goes and how that hand is turned, for a tool held in both hands; false otherwise.</summary>
        bool TryGetSecondGrip(out Vector3 wrist, out Quaternion hand);

        /// <summary>How the right hand closes on it: round a handle, on a pistol grip with a finger on the trigger, round a can.</summary>
        HandShape Shape { get; }

        /// <summary>
        /// For a tool with a handle: where on it this hand's grip is (world) and which way the handle runs there (up the
        /// handle, the way the thumbs point). The arm then puts the hollow of the curled fingers round it.
        /// </summary>
        bool TryGetHandle(bool left, out Vector3 point, out Vector3 along);

        /// <summary>The arm couldn't reach the grip: move the item so the grip is at <paramref name="wrist"/>.</summary>
        void MoveGripTo(Vector3 wrist);
    }
}
