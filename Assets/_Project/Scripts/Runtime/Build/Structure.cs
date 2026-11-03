using System.Collections.Generic;
using UnityEngine;

namespace Cabin.Build
{
    /// <summary>
    /// A group of pieces nailed together, moving as one rigid body with a compound collider. Held still while any
    /// member is planted, clamped or on the cutting table; weightless and steered by the player while carried; otherwise it falls.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class Structure : MonoBehaviour
    {
        /// <summary>Mass while carried: light enough not to bulldoze loose boards, heavy enough to feel solid.</summary>
        public const float CarryMass = 3f;

        public readonly List<Piece> Pieces = new List<Piece>();

        public Rigidbody Body { get; private set; }

        public bool Anchored
        {
            get
            {
                foreach (var p in Pieces) if (p.Planted) return true;
                return false;
            }
        }

        public bool Pinned
        {
            get
            {
                foreach (var p in Pieces) if (p.Planted || p.Clamped || p.OnTable) return true;
                return false;
            }
        }

        public bool Carried
        {
            get
            {
                foreach (var p in Pieces) if (p.Held) return true;
                return false;
            }
        }

        void Awake()
        {
            Body = GetComponent<Rigidbody>();
            Body.interpolation = RigidbodyInterpolation.Interpolate;
            Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            Body.maxAngularVelocity = 30f;
            Body.isKinematic = true;
        }

        /// <summary>Re-reads the members: kinematic state, mass, centre of mass.</summary>
        public void Refresh(Vector3? inheritedVelocity = null, Vector3? inheritedAngularVelocity = null)
        {
            Pieces.RemoveAll(p => p == null);
            bool wasKinematic = Body.isKinematic;
            float mass = 0f;
            foreach (var p in Pieces) mass += p.Mass;

            if (Carried)
            {
                Body.isKinematic = false;
                Body.useGravity = false;
                Body.mass = CarryMass;
                Body.linearDamping = 4f;
                Body.angularDamping = 4f;
                Body.solverIterations = 12;
                Body.ResetCenterOfMass();
                Body.ResetInertiaTensor();
                Body.WakeUp();
                return;
            }

            bool still = Pinned;
            Body.mass = Mathf.Max(0.5f, mass);
            Body.useGravity = true;
            Body.linearDamping = 0.02f;
            Body.angularDamping = 0.05f;
            Body.solverIterations = Physics.defaultSolverIterations;
            Body.isKinematic = still;
            if (!still)
            {
                Body.ResetCenterOfMass();
                Body.ResetInertiaTensor();
                if (wasKinematic)
                {
                    Body.linearVelocity = inheritedVelocity ?? Vector3.zero;
                    Body.angularVelocity = inheritedAngularVelocity ?? Vector3.zero;
                }
                Body.WakeUp();
            }
        }

        /// <summary>Carried pieces follow the hands by kinematic pose when snapping (post/table), by velocity otherwise.</summary>
        public void SetCarryKinematic(bool kinematic)
        {
            if (Body.isKinematic == kinematic) return;
            Body.isKinematic = kinematic;
            if (!kinematic) Body.WakeUp();
        }
    }
}
