using Engine.Media;

namespace Engine.Graphics {
    public class PrimitivesRenderer3D : BasePrimitivesRenderer<FlatBatch3D, TexturedBatch3D, FontBatch3D> {
        // 矢量字体批的平行缓存容器（泛型基类的 m_fontBatches 固定为 FontBatch3D，装不下 VectorFontBatch3D）；
        // 批同时注册进 m_allBatches，现有 Flush/Clear 遍历与 Layer 排序自然覆盖
        readonly LinkedList<VectorFontBatch3D> m_vectorFontBatches = new();

        public FlatBatch3D FlatBatch(int layer = 0,
            DepthStencilState depthStencilState = null,
            RasterizerState rasterizerState = null,
            BlendState blendState = null) {
            depthStencilState = depthStencilState ?? DepthStencilState.Default;
            rasterizerState = rasterizerState ?? RasterizerState.CullNoneScissor;
            blendState = blendState ?? BlendState.AlphaBlend;
            return FindFlatBatch(layer, depthStencilState, rasterizerState, blendState);
        }

        public TexturedBatch3D TexturedBatch(Texture2D texture,
            bool useAlphaTest = false,
            int layer = 0,
            DepthStencilState depthStencilState = null,
            RasterizerState rasterizerState = null,
            BlendState blendState = null,
            SamplerState samplerState = null) {
            depthStencilState = depthStencilState ?? DepthStencilState.Default;
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

        public FontBatch3D FontBatch(BitmapFont font,
            int layer = 0,
            DepthStencilState depthStencilState = null,
            RasterizerState rasterizerState = null,
            BlendState blendState = null,
            SamplerState samplerState = null) {
            depthStencilState = depthStencilState ?? DepthStencilState.Default;
            rasterizerState = rasterizerState ?? RasterizerState.CullNoneScissor;
            blendState = blendState ?? BlendState.AlphaBlend;
            samplerState = samplerState ?? SamplerState.LinearClamp;
            return FindFontBatch(font, layer, depthStencilState, rasterizerState, blendState, samplerState);
        }

        /// <summary>
        /// 矢量字体（FSS 动态图集）批工厂：与 PrimitivesRenderer2D.VectorFontBatch 同构，
        /// 匹配键为 BatchTexture + layer + 各渲染状态；3D 默认深度状态为 Default
        /// </summary>
        public VectorFontBatch3D VectorFontBatch(Texture2D atlasTexture,
            int layer = 0,
            DepthStencilState depthStencilState = null,
            RasterizerState rasterizerState = null,
            BlendState blendState = null,
            SamplerState samplerState = null) {
            depthStencilState = depthStencilState ?? DepthStencilState.Default;
            rasterizerState = rasterizerState ?? RasterizerState.CullNoneScissor;
            blendState = blendState ?? BlendState.AlphaBlend;
            samplerState = samplerState ?? SamplerState.LinearClamp;
            return FindVectorFontBatch(atlasTexture, layer, depthStencilState, rasterizerState, blendState, samplerState);
        }

        public VectorFontBatch3D FindVectorFontBatch(Texture2D atlasTexture,
            int layer,
            DepthStencilState depthStencilState,
            RasterizerState rasterizerState,
            BlendState blendState,
            SamplerState samplerState) {
            ArgumentNullException.ThrowIfNull(atlasTexture);
            for (LinkedListNode<VectorFontBatch3D> linkedListNode = m_vectorFontBatches.First; linkedListNode != null; linkedListNode = linkedListNode.Next) {
                VectorFontBatch3D value = linkedListNode.Value;
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
            VectorFontBatch3D val = new() {
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
    }
}