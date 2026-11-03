using UnityEngine;

namespace Cabin.Core
{
    /// <summary>
    /// Single owner of the mouse cursor. <see cref="Settled"/> is false on the frame the cursor was captured, so
    /// the click that captured it doesn't also act in the world.
    /// </summary>
    public static class CursorCapture
    {
        static int capturedFrame = -1;

        public static bool Captured => Cursor.lockState == CursorLockMode.Locked;
        /// <summary>A UI panel (colour picker) has the mouse: clicking it must not recapture the cursor.</summary>
        public static bool UiOpen;
        public static bool Settled => Captured && Time.frameCount > capturedFrame;

        public static void Capture()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            capturedFrame = Time.frameCount;
        }

        public static void Release()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
