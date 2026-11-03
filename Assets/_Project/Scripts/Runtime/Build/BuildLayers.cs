using UnityEngine;

namespace Cabin.Build
{
    /// <summary>Physics layers. Named in Project Settings → Tags and Layers by the asset generator.</summary>
    public static class BuildLayers
    {
        public const int Piece = 8;
        public const string PieceName = "BuildPiece";
        public const int Nail = 9;
        public const string NailName = "Nail";
        public const int Player = 10;
        public const string PlayerName = "Player";
        /// <summary>The piece in the player's hands: collides with the world, never with the player or the aim ray.</summary>
        public const int HeldPiece = 11;
        public const string HeldPieceName = "HeldPiece";
        /// <summary>Clamp visuals: trigger colliders the aim ray can find, never touching anything physically.</summary>
        public const int Clamp = 12;
        public const string ClampName = "Clamp";

        public static int PieceMask => 1 << Piece;
        public static int NailMask => 1 << Nail;
        public static int PlayerMask => 1 << Player;
        public static int HeldMask => 1 << HeldPiece;
        public static int ClampMask => 1 << Clamp;
        /// <summary>Terrain, platforms, workstations: anything that isn't lumber or the player.</summary>
        public static int GroundMask => Physics.DefaultRaycastLayers & ~PieceMask & ~NailMask & ~PlayerMask & ~HeldMask & ~ClampMask;
        public static int AimMask => Physics.DefaultRaycastLayers & ~PlayerMask & ~HeldMask & ~ClampMask;

        /// <summary>Collision matrix entries that must hold for carrying to work. Idempotent.</summary>
        public static void ApplyCollisionMatrix()
        {
            Physics.IgnoreLayerCollision(Player, HeldPiece, true);
            Physics.IgnoreLayerCollision(Player, Nail, true);
            Physics.IgnoreLayerCollision(HeldPiece, Nail, true);
            Physics.IgnoreLayerCollision(Piece, Nail, true);
            Physics.IgnoreLayerCollision(Nail, Nail, true);
            for (int i = 0; i < 32; i++) Physics.IgnoreLayerCollision(Clamp, i, true);
        }
    }
}
