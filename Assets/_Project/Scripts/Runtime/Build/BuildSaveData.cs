using System;
using System.Collections.Generic;

namespace Cabin.Build
{
    [Serializable]
    public sealed class BuildSaveData
    {
        public const int CurrentVersion = 2;

        public int version = CurrentVersion;
        public int nextPieceId = 1;
        public int nextNailId = 1;
        public List<PieceRecord> pieces = new List<PieceRecord>();
        public List<NailRecord> nails = new List<NailRecord>();
    }
}
