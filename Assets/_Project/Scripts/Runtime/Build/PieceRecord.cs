using System;
using System.Collections.Generic;
using UnityEngine;

namespace Cabin.Build
{
    /// <summary>One piece in a save: its shape (cut so far) and where it is.</summary>
    [Serializable]
    public sealed class PieceRecord
    {
        public int id;
        public StockKind kind;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3[] vertices = new Vector3[0];
        /// <summary>Face loops flattened; <see cref="faceSizes"/> says how many indices each face takes.</summary>
        public int[] faceIndices = new int[0];
        public int[] faceSizes = new int[0];
        public bool planted;
        public bool onTable;
        public int seatedIn;
        /// <summary>Held by the clamps when saved; re-clamped to whatever it touches on load.</summary>
        public bool clamped;
        /// <summary>Spray paint as a base64 PNG in the piece's paint atlas layout; empty if never painted.</summary>
        public string paint;

        public static PieceRecord From(int id, StockKind kind, Vector3 position, Quaternion rotation, ConvexSolid solid, bool planted, bool onTable, int seatedIn = 0)
        {
            var r = new PieceRecord { id = id, kind = kind, position = position, rotation = rotation, planted = planted, onTable = onTable, seatedIn = seatedIn };
            r.vertices = solid.Vertices.ToArray();
            var idx = new List<int>();
            var sizes = new List<int>();
            foreach (var f in solid.Faces)
            {
                idx.AddRange(f);
                sizes.Add(f.Length);
            }
            r.faceIndices = idx.ToArray();
            r.faceSizes = sizes.ToArray();
            return r;
        }

        public ConvexSolid ToSolid()
        {
            var s = new ConvexSolid();
            s.Vertices.AddRange(vertices);
            int at = 0;
            foreach (int n in faceSizes)
            {
                var f = new int[n];
                Array.Copy(faceIndices, at, f, 0, n);
                s.Faces.Add(f);
                at += n;
            }
            return s;
        }
    }
}
