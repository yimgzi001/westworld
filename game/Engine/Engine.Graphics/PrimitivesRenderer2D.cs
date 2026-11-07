using Engine.Media;

namespace Engine.Graphics {
    public class PrimitivesRenderer2D : BasePrimitivesRenderer<FlatBatch2D, TexturedBatch2D, FontBatch2D> {
        // 矢量字体批的平行缓存容器（泛型基类的 m_fontBatches 固定为 FontBatch2D，装不下 VectorFontBatch2D）；
        // 批同时注册进 m_allBatches，现有 Flush/Clear 遍历与 Layer 排序自然覆盖
        readonly LinkedList<VectorFontBatch2D> m_vectorFontBatches = new();

        public static Matrix ViewportMatrix() {
            Viewport viewport = Display.Viewport;
            float num = 1f / viewport.Width;
            float num2 = 1f / viewport.Height;
            return new Matrix(
                2f * num,
                0f,
                0f,
                0f,
                0f,
                -2f * num2,
                0f,
                0f,
                0f,
                0f,
                1f,
                0f,
                -1f,
                1f,
                0f,
                1f
            );
        }

        public FlatBatch2D FlatBatch(int layer = 0,
            DepthStencilState depthStencilState = null,
            RasterizerState rasterizerState = null,
            BlendState blendState = null) {
            depthStencilState = depthStencilState ?? DepthStencilState.None;
            rasterizerState = rasterizerState ?? RasterizerState.CullNoneScissor;
            blendState = blendState ?? BlendState.AlphaBlend;
            return FindFlatBatch(layer, depthStencilState, rasterizerState, blendState);
        }

        public TexturedBatch2D TexturedBatch(Texture2D texture,
            bool useAlphaTest = false,
            int layer = 0,
            DepthStencilState depthStencilState = null,
            RasterizerState rasterizerState = null,
            BlendState blendState = null,
            SamplerState samplerState = null) {
            depthStencilState = depthStencilState ?? DepthStencilState.None;
            rasterizerState = rasterizerState ?? RasterizerState.CullNoneScissor;
            blendState = blendState ?? BlendState.AlphaBlend;
            samplerState = samplerState ?? SamplerState.LinearClamp;
            return FindTexturedBatch(
                texture,
                useAlphaTest,
                layer,
                depthStencilState,
                rasterizerState,
                blendState,
                samplerState
            );
        }

        public FontBatch2D FontBatch(BitmapFont font = null,
            int layer = 0,
            DepthStencilState depthStencilState = null,
            RasterizerState rasterizerState = null,
            BlendState blendState = null,
            SamplerState samplerState = null) {
            font = font ?? BitmapFont.DebugFont;
            depthStencilState = depthStencilState ?? DepthStencilState.None;
            rasterizerState = rasterizerState ?? RasterizerState.CullNoneScissor;
            blendState = blendState ?? BlendState.AlphaBlend;
            samplerState = samplerState ?? SamplerState.LinearClamp;
            return FindFontBatch(font, layer, depthStencilState, rasterizerState, blendState, samplerState);
        }

        /// <summary>
        /// 矢量字体（FSS 动态图集）批工厂：缓存/复用结构与 FontBatch/FindFontBatch 同构，
        /// 匹配键为 BatchTexture + layer + 各渲染状态（与 FindFontBatch 同等粒度）
        /// </summary>
        public VectorFontBatch2D VectorFontBatch(Texture2D atlasTexture,
            int layer = 0,
            DepthStencilState depthStencilState = null,
            RasterizerState rasterizerState = null,
            BlendState blendState = null,
            SamplerState samplerState = null) {
            depthStencilState = depthStencilState ?? DepthStencilState.None;
            rasterizerState = rasterizerState ?? RasterizerState.CullNoneScissor;
            blendState = blendState ?? BlendState.AlphaBlend;
            samplerState = samplerState ?? SamplerState.LinearClamp;
            return FindVectorFontBatch(atlasTexture, layer, depthStencilState, rasterizerState, blendState, samplerState);
        }

        public VectorFontBatch2D FindVectorFontBatch(Texture2D atlasTexture,
            int layer,
            DepthStencilState depthStencilState,
            RasterizerState rasterizerState,
            BlendState blendState,
            SamplerState samplerState) {
            ArgumentNullException.ThrowIfNull(atlasTexture);
            for (LinkedListNode<VectorFontBatch2D> linkedListNode = m_vectorFontBatches.First; linkedListNode != null; linkedListNode = linkedListNode.Next) {
                VectorFontBatch2D value = linkedListNode.Value;
                if (atlasTexture == value.BatchTexture
                    && layer == value.Layer
                    && depthStencilState == value.DepthStencilState
                    && rasterizerState == value.RasterizerState
                    && blendState == value.BlendState
                    && samplerState == value.SamplerState) {
                    if (linkedListNode.Previous != null) {
                        m_vectorFontBatches.Remove(linkedListNode);
                        m_vectorFontBatches.AddFirst(linkedListNode);
                    }
                    return value;
                }
            }
            m_sortNeeded |= m_allBatches.Count > 0 && m_allBatches[^1].Layer > layer;
            VectorFontBatch2D val = new() {
                Layer = layer,
                BatchTexture = atlasTexture,
                SamplerState = samplerState,
                DepthStencilState = depthStencilState,
                RasterizerState = rasterizerState,
                BlendState = blendState
            };
            m_vectorFontBatches.AddFirst(val);
            m_allBatches.Add(val);
            return val;
        }

        public void Flush(bool clearAfterFlush = true, int maxLayer = int.MaxValue) {
            Flush(ViewportMatrix(), clearAfterFlush, maxLayer);
        }
    }
}