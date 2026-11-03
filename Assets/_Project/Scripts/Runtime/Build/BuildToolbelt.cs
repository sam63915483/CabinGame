using Cabin.Core;
using Cabin.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Cabin.Build
{
    /// <summary>
    /// The player's build controls: an 8-slot hotbar (1 Nail gun, 2 Saw, 3 Spray paint, 4 Axe, the rest empty — an empty
    /// slot is your hands), the aim raycast every tool reads, quick save/load (F5/F9) and clear (F8 twice).
    /// </summary>
    public sealed class BuildToolbelt : MonoBehaviour
    {
        [SerializeField] BuildSite site;
        [SerializeField] CarryController carry;
        [SerializeField] GhostPreview ghost;
        [SerializeField] SprayCan can;
        [SerializeField] float reach = 4f;
        [SerializeField] Texture2D nailGunIcon, sawIcon, sprayIcon, axeIcon;

        public const int SlotCount = 8;
        /// <summary>Where you start: the first empty slot, so your hands.</summary>
        const int StartSlot = 4;

        readonly InputAction[] hotbar = new InputAction[SlotCount];
        readonly BuildTool[] slots = new BuildTool[SlotCount];
        BuildTool hands;
        InputAction interact, use, remove, quickSave, quickLoad, clearAll, pilot, align;
        BuildTool[] tools;
        Camera cam;
        AimInfo aim;
        float clearArmedUntil;
        string message;
        float messageUntil;

        public BuildSite Site => site;
        public CarryController Carry => carry;
        public GhostPreview Ghost => ghost;
        public SprayCan Can => can;
        /// <summary>The tool model in the player's hand (null if the player has none).</summary>
        public ToolViewModel ViewModel { get; private set; }
        /// <summary>The player's character (null if there's none), for the swing when a nail goes in.</summary>
        public PlayerBody Body { get; private set; }
        public PlayerSounds Sounds { get; private set; }
        public float Reach => reach;
        /// <summary>Every tool, hands first.</summary>
        public BuildTool[] Tools => tools;
        /// <summary>The selected hotbar slot, 0–7.</summary>
        public int CurrentIndex { get; private set; } = StartSlot;
        /// <summary>What's in slot <paramref name="i"/> (null: empty).</summary>
        public BuildTool SlotTool(int i) => slots[i];
        public BuildTool Current => slots[CurrentIndex] ?? hands;
        public ref AimInfo AimRef => ref aim;
        public Ray AimRay => cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        public string Message => Time.time < messageUntil ? message : null;

        public bool InteractPressed => CursorCapture.Settled && interact.WasPressedThisFrame();
        public bool UsePressed => CursorCapture.Settled && use.WasPressedThisFrame();
        public bool RemovePressed => CursorCapture.Settled && remove.WasPressedThisFrame();
        public bool UseHeld => CursorCapture.Settled && use.IsPressed();
        public bool UseReleased => use.WasReleasedThisFrame();
        public bool PilotPressed => CursorCapture.Settled && pilot.WasPressedThisFrame();
        public bool AlignPressed => CursorCapture.Settled && align.WasPressedThisFrame();

        void Awake()
        {
            for (int i = 0; i < hotbar.Length; i++) hotbar[i] = Controls.Get($"Player/Hotbar{i + 1}");
            interact = Controls.Get("Player/Interact");
            use = Controls.Get("Player/Use");
            remove = Controls.Get("Build/Remove");
            quickSave = Controls.Get("Build/QuickSave");
            quickLoad = Controls.Get("Build/QuickLoad");
            clearAll = Controls.Get("Build/ClearAll");
            pilot = Controls.Get("Build/Pilot");
            align = Controls.Get("Build/Align");
            hands = new HandsTool();
            slots[0] = new NailGunTool { Icon = nailGunIcon };
            slots[1] = new SawTool { Icon = sawIcon };
            slots[2] = new SprayTool { Icon = sprayIcon };
            slots[3] = new AxeTool { Icon = axeIcon };
            tools = new[] { hands, slots[0], slots[1], slots[2], slots[3] };
            foreach (var t in tools) t.Bind(this);
        }

        void Start()
        {
            cam = Camera.main;
            ViewModel = cam != null ? cam.GetComponentInChildren<ToolViewModel>(true) : null;
            Body = cam != null ? cam.transform.root.GetComponentInChildren<PlayerBody>(true) : null;
            Sounds = cam != null ? cam.transform.root.GetComponentInChildren<PlayerSounds>(true) : null;
            Current.OnSelected();
        }

        public void Say(string text, float seconds = 2.5f)
        {
            if (string.IsNullOrEmpty(text)) return;
            message = text;
            messageUntil = Time.time + seconds;
        }

        void Update()
        {
            if (InputSystem.actions == null) return; // the actions asset goes first when play stops; no input to read
            if (cam == null) cam = Camera.main;
            if (cam == null) return;
            UpdateAim();
            if (!CursorCapture.Settled)
            {
                LookLock.Held = false;
                return;
            }

            for (int i = 0; i < hotbar.Length; i++)
                if (hotbar[i].WasPressedThisFrame() && i != CurrentIndex) Select(i);

            if (quickSave.WasPressedThisFrame())
            {
                carry.Drop();
                site.QuickSave();
                Say($"Saved {site.Pieces.Count} pieces, {site.Nails.Count} nails");
            }
            if (quickLoad.WasPressedThisFrame())
            {
                carry.Drop();
                Say(site.QuickLoad() ? $"Loaded {site.Pieces.Count} pieces, {site.Nails.Count} nails" : "No quick save yet (F5)");
                UpdateAim();
            }
            if (clearAll.WasPressedThisFrame())
            {
                if (Time.time < clearArmedUntil)
                {
                    carry.Drop();
                    site.ClearAll();
                    clearArmedUntil = 0f;
                    Say("Cleared everything");
                    UpdateAim();
                }
                else
                {
                    clearArmedUntil = Time.time + 3f;
                    Say("Press F8 again to clear everything");
                }
            }

            Current.Tick();
        }

        void Select(int index)
        {
            if (carry.Holding)
            {
                carry.Drop();
                Say("Dropped what you were holding");
            }
            Current.OnDeselected();
            CurrentIndex = index;
            Current.OnSelected();
        }

        void UpdateAim()
        {
            aim = default;
            bool solid = Physics.Raycast(AimRay, out var hit, reach, BuildLayers.AimMask, QueryTriggerInteraction.Ignore);
            if (Physics.Raycast(AimRay, out var clampHit, reach, BuildLayers.ClampMask, QueryTriggerInteraction.Collide)
                && (!solid || clampHit.distance <= hit.distance + 0.05f))
                aim.Clamp = clampHit.collider.GetComponentInParent<ClampVisual>();
            if (!solid) return;
            aim.Hit = true;
            aim.RaycastHit = hit;
            aim.Piece = hit.collider.GetComponent<Piece>();
            aim.Nail = hit.collider.GetComponentInParent<NailHead>();
            aim.NailDirect = aim.Nail != null;
            // Near a nail counts: the nearest head close to the crosshair, as long as it's not behind what you see.
            if (aim.Nail == null) aim.Nail = site.NearestNail(AimRay, Mathf.Min(reach, hit.distance + 0.06f));
            if (aim.Nail != null)
            {
                var t = aim.Nail.transform;
                ghost.DrawNail(t.position - t.forward * 0.004f, t.forward, true);
            }
            aim.Interactable = hit.collider.GetComponentInParent<IInteractable>();
            aim.Table = aim.Interactable as CuttingTable;
        }
    }
}
