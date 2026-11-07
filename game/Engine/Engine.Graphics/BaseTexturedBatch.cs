namespace Engine.Graphics {
    public abstract class BaseTexturedBatch : BaseBatch {
        public static UnlitShader m_shader = new(true, true, false, false);

        public static UnlitShader m_shaderAlphaTest = new(true, true, false, true);

        public readonly DynamicArray<VertexPositionColorTexture> TriangleVertices = [];

        public readonly DynamicArray<int> TriangleIndices = [];

        public Texture2D Texture { get; set; }

        public bool UseAlphaTest { get; set; }

        public SamplerState SamplerState { get; set; }

        public override bool IsEmpty() => TriangleIndices.Count == 0;

        public override void Clear() {
            TriangleVertices.Clear();
            TriangleIndices.Clear();
        }

        /// <summary>
        /// 按四边形数一次性把顶点/索引容量顶到位（仍走 DynamicArray 的精确扩容，不超额分配）。
        /// 大批量 QueueQuad 前调用可避免逐 quad 超容量触发 O(N²) 整段拷贝。
        /// </summary>
        public void EnsureQuadCapacity(int quadCount) {
            EnsureCapacity(quadCount * 4, quadCount * 6);
        }

        /// <summary>
        /// 按顶点/索引数一次性把容量顶到位（不限定四边形拓扑，供 DrawMeshBlock 等直接整块
        /// 拷贝顶点的路径使用；与 DynamicArray 的精确扩容配合，不超额分配）。
        /// </summary>
        public void EnsureCapacity(int vertexCount, int indexCount) {
            if (vertexCount > TriangleVertices.Capacity) {
                TriangleVertices.Capacity = vertexCount;
            }
            if (indexCount > TriangleIndices.Capacity) {
                TriangleIndices.Capacity = indexCount;
            }
        }

        public virtual void Flush(Matrix matrix, bool clearAfterFlush = true) {
            Flush(matrix, Vector4.One, clearAfterFlush);
        }

        public override void Flush(Matrix matrix, Vector4 color, bool clearAfterFlush = true) {
            Display.DepthStencilState = DepthStencilState;
            Display.RasterizerState = RasterizerState;
            Display.BlendState = BlendState;
            FlushWithDeviceState(UseAlphaTest, Texture, SamplerState, matrix, color, clearAfterFlush);
        }

        public void FlushWithDeviceState(bool useAlphaTest,
            Texture2D texture,
            SamplerState samplerState,
            Matrix matrix,
            Vector4 color,
            bool clearAfterFlush = true) {
            if (useAlphaTest) {
                m_shaderAlphaTest.Texture = texture;
                m_shaderAlphaTest.SamplerState = samplerState;
                m_shaderAlphaTest.Transforms.World[0] = matrix;
                m_shaderAlphaTest.Color = color;
                m_shaderAlphaTest.AlphaThreshold = 0f;
                FlushWithDeviceState(m_shaderAlphaTest, clearAfterFlush);
            }
            else {
                m_shader.Texture = texture;
                m_shader.SamplerState = samplerState;
                m_shader.Transforms.World[0] = matrix;
                m_shader.Color = color;
                FlushWithDeviceState(m_shader, clearAfterFlush);
            }
        }

        public void FlushWithDeviceState(Shader shader, bool clearAfterFlush = true) {
            int num = 0;
            int num2 = TriangleIndices.Count;
            while (num2 > 0) {
                int num3 = Math.Min(num2, 196605);
                Display.DrawUserIndexed(
                    PrimitiveType.TriangleList,
                    shader,
                    VertexPositionColorTexture.VertexDeclaration,
                    TriangleVertices.Array,
                    0,
                    TriangleVertices.Count,
                    TriangleIndices.Array,
                    num,
                    num3
                );
                num += num3;
                num2 -= num3;
            }
            if (clearAfterFlush) {
                Clear();
            }
        }

        public void TransformTriangles(Matrix matrix, int start = 0, int end = -1) {
            VertexPositionColorTexture[] array = TriangleVertices.Array;
            if (end < 0) {
                end = TriangleVertices.Count;
            }
            for (int i = start; i < end; i++) {
                Vector3.Transform(ref array[i].Position, ref matrix, out array[i].Position);
            }
        }

        public void TransformTrianglesColors(Color color, int start = 0, int end = -1) {
            VertexPositionColorTexture[] array = TriangleVertices.Array;
            if (end < 0) {
                end = TriangleVertices.Count;
            }
            for (int i = start; i < end; i++) {
                array[i].Color *= color;
            }
        }
    }
}