using UnityEngine;

namespace Cabin.Map
{
    /// <summary>
    /// On each terrain tile: hands the <c>Cabin/Terrain</c> material the tile's second control map (layers 4-7), which
    /// the engine only binds for its own add pass. Runs in the editor too, so the Scene view shows the same ground.
    /// </summary>
    [ExecuteAlways, RequireComponent(typeof(Terrain))]
    public sealed class TerrainControl : MonoBehaviour
    {
        static readonly int Control1Id = Shader.PropertyToID("_Control1");

        void OnEnable() => Bind();

        public void Bind()
        {
            var terrain = GetComponent<Terrain>();
            var data = terrain.terrainData;
            if (data == null) return;
            var maps = data.alphamapTextures;
            if (maps == null || maps.Length < 2) return;
            var block = new MaterialPropertyBlock();
            terrain.GetSplatMaterialPropertyBlock(block);
            block.SetTexture(Control1Id, maps[1]);
            terrain.SetSplatMaterialPropertyBlock(block);
        }
    }
}
