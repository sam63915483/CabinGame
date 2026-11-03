using Cabin.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Cabin.Player
{
    /// <summary>
    /// First-person walker on a CharacterController (handoff §9.1, slowed at Sam's word): walk 2.67 m/s, sprint 4.94, jump, crouch, eye
    /// height 1.7 m. Tab toggles a no-clip fly camera in the editor and CABIN_DEV builds (§9.3).
    /// Esc opens the settings menu (which releases the mouse); clicking recaptures it.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class FirstPersonController : MonoBehaviour
    {
        [SerializeField] Transform cameraRoot;
        [SerializeField] float walkSpeed = 4f / 1.5f; // was 4 (handoff §9.1); slowed 1.5× at Sam's word
        [SerializeField] float sprintSpeed = 7f / 1.7f * 1.2f; // was 7; slowed 1.7×, then 1.2× faster, at Sam's word
        [SerializeField] float crouchSpeed = 2f;
        [SerializeField] float jumpHeight = 1.1f;
        [SerializeField] float gravity = -20f;
        [SerializeField] float lookDegreesPerPixel = 0.08f;
        [SerializeField] float standingHeight = 1.8f;
        [SerializeField] float crouchHeight = 1.4f;
        [SerializeField] float eyeBelowTop = 0.1f;
        [SerializeField] float flySpeed = 8f;
        [SerializeField] float flyFastSpeed = 30f;

        CharacterController body;
        InputAction move, look, jump, sprint, crouch, fly, use;
        float yaw, pitch, verticalVelocity;

        public bool Flying { get; private set; }
        public bool Crouching { get; private set; }
        /// <summary>Degrees the camera looks down (negative: up).</summary>
        public float Pitch => pitch;
        public Transform CameraRoot => cameraRoot;
        /// <summary>Mouse sensitivity: degrees of turn per pixel of mouse travel.</summary>
        public float LookDegreesPerPixel { get => lookDegreesPerPixel; set => lookDegreesPerPixel = value; }

        /// <summary>While set, something else (pilot mode) drives the camera and reads WASD; the body stands still.</summary>
        public bool ExternalControl { get; set; }
        /// <summary>The character's body puts the camera at its eyes (PlayerBody); the controller then leaves the camera's position alone.</summary>
        public bool BodyDrivesCamera { get; set; }
#if UNITY_EDITOR || CABIN_DEV
        int slowFrames;
#endif

        void Awake()
        {
            body = GetComponent<CharacterController>();
            move = Controls.Get("Player/Move");
            look = Controls.Get("Player/Look");
            jump = Controls.Get("Player/Jump");
            sprint = Controls.Get("Player/Sprint");
            crouch = Controls.Get("Player/Crouch");
            fly = Controls.Get("Player/Fly");
            use = Controls.Get("Player/Use");
        }

        void Start()
        {
            yaw = transform.eulerAngles.y;
            CursorCapture.Capture();
#if UNITY_EDITOR || CABIN_DEV
            Debug.Log($"[Cabin] Player ready at {Time.realtimeSinceStartup:F1} s since start");
#endif
        }

        void Update()
        {
#if UNITY_EDITOR || CABIN_DEV
            if (slowFrames < 6 && Time.unscaledDeltaTime > 0.25f) { slowFrames++; Debug.Log($"[Cabin] Slow frame {Time.frameCount}: {Time.unscaledDeltaTime * 1000f:F0} ms at {Time.realtimeSinceStartup:F1} s"); }
#endif
            // Esc belongs to the settings menu (SettingsMenu), which releases and recaptures the mouse.
            if (!CursorCapture.Captured && !CursorCapture.UiOpen && use.WasPressedThisFrame()) CursorCapture.Capture();

            if (ExternalControl)
            {
                if (!Flying) Walk(Vector2.zero); // gravity still applies to the body
                return;
            }
            if (CursorCapture.Captured && !LookLock.Held) Look();
#if UNITY_EDITOR || CABIN_DEV
            if (CursorCapture.Captured && fly.WasPressedThisFrame()) SetFlying(!Flying);
#endif
            Vector2 input = CursorCapture.Captured ? move.ReadValue<Vector2>() : Vector2.zero;
            if (Flying) Fly(input);
            else Walk(input);
        }

        /// <summary>Puts the player somewhere else, facing a heading (degrees) and pitch (degrees down): the bench and dev tools.</summary>
        public void Teleport(Vector3 position, float heading, float lookPitch)
        {
            bool was = body.enabled;
            body.enabled = false;
            transform.position = position;
            yaw = heading;
            pitch = Mathf.Clamp(lookPitch, -89f, 89f);
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            cameraRoot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            verticalVelocity = 0f;
            body.enabled = was && !Flying;
        }

        void Look()
        {
            Vector2 delta = look.ReadValue<Vector2>() * lookDegreesPerPixel;
            yaw += delta.x;
            pitch = Mathf.Clamp(pitch - delta.y, -89f, 89f);
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            cameraRoot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        void SetFlying(bool on)
        {
            Flying = on;
            body.enabled = !on;
            verticalVelocity = 0f;
        }

        void Fly(Vector2 input)
        {
            Vector3 dir = cameraRoot.forward * input.y + cameraRoot.right * input.x;
            if (CursorCapture.Captured && jump.IsPressed()) dir += Vector3.up;
            if (CursorCapture.Captured && crouch.IsPressed()) dir += Vector3.down;
            float speed = sprint.IsPressed() ? flyFastSpeed : flySpeed;
            transform.position += Vector3.ClampMagnitude(dir, 1f) * speed * Time.deltaTime;
        }

        void Walk(Vector2 input)
        {
            bool crouching = CursorCapture.Captured && crouch.IsPressed();
            Crouching = crouching;
            float targetHeight = crouching ? crouchHeight : standingHeight;
            if (!Mathf.Approximately(body.height, targetHeight))
            {
                body.height = Mathf.MoveTowards(body.height, targetHeight, 4f * Time.deltaTime);
                body.center = new Vector3(0f, body.height * 0.5f, 0f);
            }
            if (!ExternalControl && !BodyDrivesCamera) cameraRoot.localPosition = new Vector3(0f, body.height - eyeBelowTop, 0f);

            float speed = crouching ? crouchSpeed : sprint.IsPressed() ? sprintSpeed : walkSpeed;
            Vector3 horizontal = (transform.right * input.x + transform.forward * input.y) * speed;

            if (body.isGrounded)
            {
                verticalVelocity = -2f;
                if (CursorCapture.Captured && jump.WasPressedThisFrame() && !crouching)
                    verticalVelocity = Mathf.Sqrt(2f * -gravity * jumpHeight);
            }
            verticalVelocity += gravity * Time.deltaTime;
            body.Move((horizontal + Vector3.up * verticalVelocity) * Time.deltaTime);
        }
    }
}
