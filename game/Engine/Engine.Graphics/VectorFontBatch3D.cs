namespace Engine.Graphics {
    /// <summary>
    /// 矢量字体批（3D）：与 VectorFontBatch2D 同构，顶点为世界空间位置；
    /// Flush 显式绑 FSS 动态图集纹理（BatchTexture），不经过 BitmapFont。
    /// 直接由 VectorFont.DrawText 驱动请走 VectorFontSink3D（文本空间→世界基向量变换+按图集纹理分批），
    /// 本类为单纹理低层 quad 批
    /// </summary>
    public class VectorFontBatch3D : BaseFontBatch {
        /// <summary>FSS 动态图集（RGBA8888 引擎纹理），由 QueueGlyphQuad 显式指定</summary>
        public Texture2D BatchTexture { get; set; }

        public VectorFontBatch3D() {
            DepthStencilState = DepthStencilState.Default;
            RasterizerState = RasterizerState.CullNoneScissor;
            BlendState = BlendState.AlphaBlend;
            SamplerState = SamplerState.LinearClamp;
        }

        /// <summary>
        /// 排入一个 glyph 四边形：position 为字形左上角锚点，right/down 为宽度/高度方向
        /// 基向量（模长即缩放，与 FontBatch3D.QueueText 的 right/down 同义，可非正交任意朝向），
        /// 四角 = position、position+right*size.X、position+right*size.X+down*size.Y、position+down*size.Y；
        /// uv1 左上、uv2 右下（与图集上传坐标一致），顶点/索引布局与 FontBatch3D 的 glyph 路径同构
        /// </summary>
        public void QueueGlyphQuad(Texture2D texture, Vector3 position, Vector3 right, Vector3 down, Vector2 size, Vector2 uv1, Vector2 uv2, Color color) {
            BatchTexture = texture;
            Vector3 v = position;
            Vector3 v2 = position + right * size.X;
            Vector3 v3 = v2 + down * size.Y;
            Vector3 v4 = position + down * size.Y;
            int count = TriangleVertices.Count;
            TriangleVertices.Count += 4;
            TriangleVertices.Array[count] = new VertexPositionColorTexture(v, color, uv1);
            TriangleVertices.Array[count + 1] = new VertexPositionColorTexture(v2, color, new Vector2(uv2.X, uv1.Y));
            TriangleVertices.Array[count + 2] = new VertexPositionColorTexture(v3, color, uv2);
            TriangleVertices.Array[count + 3] = new VertexPositionColorTexture(v4, color, new Vector2(uv1.X, uv2.Y));
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
