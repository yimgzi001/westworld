namespace Engine.Graphics {
    /// <summary>
    /// 矢量字体批：BaseFontBatch 的 Flush 硬绑 Font.Texture（BitmapFont 位图路径），
    /// 这里显式携带 FSS 动态图集纹理（BatchTexture），不经过 BitmapFont
    /// </summary>
    public class VectorFontBatch2D : BaseFontBatch, Engine.Media.IFontBatchSink {
        /// <summary>FSS 动态图集（RGBA8888 引擎纹理），由工厂或 QueueGlyphQuad 显式指定</summary>
        public Texture2D BatchTexture { get; set; }

        public VectorFontBatch2D() {
            DepthStencilState = DepthStencilState.None;
            RasterizerState = RasterizerState.CullNoneScissor;
            BlendState = BlendState.AlphaBlend;
            SamplerState = SamplerState.LinearClamp;
        }

        /// <summary>
        /// 排入一个 glyph 四边形：uv1 左上、uv2 右下（与图集上传坐标一致），
        /// 顶点/索引布局与 FontBatch2D 的 glyph 路径同构
        /// </summary>
        public void QueueGlyphQuad(Texture2D texture, Vector2 destination, Vector2 size, Vector2 uv1, Vector2 uv2, float depth, Color color) {
            BatchTexture = texture;
            int count = TriangleVertices.Count;
            TriangleVertices.Count += 4;
            TriangleVertices.Array[count] = new VertexPositionColorTexture(
                new Vector3(destination.X, destination.Y, depth), color, uv1);
            TriangleVertices.Array[count + 1] = new VertexPositionColorTexture(
                new Vector3(destination.X + size.X, destination.Y, depth), color, new Vector2(uv2.X, uv1.Y));
            TriangleVertices.Array[count + 2] = new VertexPositionColorTexture(
                new Vector3(destination.X + size.X, destination.Y + size.Y, depth), color, uv2);
            TriangleVertices.Array[count + 3] = new VertexPositionColorTexture(
                new Vector3(destination.X, destination.Y + size.Y, depth), color, new Vector2(uv1.X, uv2.Y));
            int count2 = TriangleIndices.Count;
            TriangleIndices.Count += 6;
            TriangleIndices.Array[count2] = count;
            TriangleIndices.Array[count2 + 1] = count + 1;
            TriangleIndices.Array[count2 + 2] = count + 2;
            TriangleIndices.Array[count2 + 3] = count + 2;
            TriangleIndices.Array[count2 + 4] = count + 3;
            TriangleIndices.Array[count2 + 5] = count;
        }

        /// <summary>
        /// 排入一个任意四角 glyph 四边形：p0-p3 = 字形左上/右上/右下/左下角位的屏上落点（已含旋转），
        /// uv 角随位形对应（p0↔uv1、p1↔右上、p2↔uv2、p3↔左下）。
        /// QueueGlyphQuad(dest,size,uv1,uv2) 的 uv 角可分离（u 只随屏 X、v 只随屏 Y），
        /// 表达不了 90° 旋转（竖排 Rotated）——旋转须由调用方转好角位经此口排入
        /// </summary>
        public void QueueGlyphQuadCorners(Texture2D texture, Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, Vector2 uv1, Vector2 uv2, float depth, Color color) {
            BatchTexture = texture;
            int count = TriangleVertices.Count;
            TriangleVertices.Count += 4;
            TriangleVertices.Array[count] = new VertexPositionColorTexture(
                new Vector3(p0.X, p0.Y, depth), color, uv1);
            TriangleVertices.Array[count + 1] = new VertexPositionColorTexture(
                new Vector3(p1.X, p1.Y, depth), color, new Vector2(uv2.X, uv1.Y));
            TriangleVertices.Array[count + 2] = new VertexPositionColorTexture(
                new Vector3(p2.X, p2.Y, depth), color, uv2);
            TriangleVertices.Array[count + 3] = new VertexPositionColorTexture(
                new Vector3(p3.X, p3.Y, depth), color, new Vector2(uv1.X, uv2.Y));
            int count2 = TriangleIndices.Count;
            TriangleIndices.Count += 6;
            TriangleIndices.Array[count2] = count;
            TriangleIndices.Array[count2 + 1] = count + 1;
            TriangleIndices.Array[count2 + 2] = count + 2;
            TriangleIndices.Array[count2 + 3] = count + 2;
            TriangleIndices.Array[count2 + 4] = count + 3;
            TriangleIndices.Array[count2 + 5] = count;
        }

        public override void Flush(Matrix matrix, Vector4 color, bool clearAfterFlush = true) {
            Display.DepthStencilState = DepthStencilState;
            Display.RasterizerState = RasterizerState;
            Display.BlendState = BlendState;
            // 与 BaseFontBatch.FlushWithDeviceState(BitmapFont, ...) 相同的 shader 绑定路径，
            // 但纹理直接取 BatchTexture，不再为传纹理构造临时 BitmapFont
            m_shader.Texture = BatchTexture;
            m_shader.SamplerState = SamplerState;
            m_shader.Transforms.World[0] = matrix;
            m_shader.Color = color;
            FlushWithDeviceState(m_shader, clearAfterFlush);
        }
    }
}
