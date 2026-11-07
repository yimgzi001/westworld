using Engine;
using Engine.Graphics;

namespace Game {
    public abstract class FlatBlock : Block {
        public override void GenerateTerrainVertices(BlockGeometryGenerator generator, TerrainGeometry geometry, int value, int x, int y, int z) { }

        /// <summary>交叉面片家族：上界 2 quad（billboard 单 quad，无指向时两面）。</summary>
        public override BlockDrawDemand GetDrawDemand(int value, SubsystemTerrain subsystemTerrain) => BlockDrawDemand.Flat;

        public override void DrawBlock(PrimitivesRenderer3D primitivesRenderer,
            int value,
            Color color,
            float size,
            ref Matrix matrix,
            DrawBlockEnvironmentData environmentData) {
            BlocksManager.DrawFlatOrImageExtrusionBlock(
                primitivesRenderer,
                value,
                size,
                ref matrix,
                GetDefaultTexture(value),
                color,
                false,
                environmentData
            );
        }
    }
}