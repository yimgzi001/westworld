using System.Runtime.CompilerServices;
using Engine.Graphics;

namespace Game {
    /// <summary>
    /// 即时批次键：TexturedBatch 全部可变参数（纹理+alphaTest+layer+深度/光栅/混合/采样状态），
    /// 完整对应物理批次身份，方块与粒子两套容量账本共用。
    /// 纹理为 null 表示「账本域内纹理恒定」的批次（如方块三变体恒贴 AnimatedBlocksTexture，
    /// 纹理由 <see cref="BlocksManager.GetTexturedBatch"/> 调用点提供，不进键）。
    /// 状态字段为 null 时构造器按 PrimitivesRenderer3D.TexturedBatch 进 FindTexturedBatch
    /// 前的补缺规则归一化（深度=Default、混合=AlphaBlend、光栅=CullNoneScissor、采样=LinearClamp）——
    /// 键空间与物理批次空间 1:1，解析后相同的键在账本中合并为同一键累加；
    /// state 对象是引擎单例，按引用判等。
    /// 专用 class：键实例通常来自静态字段（进程内单例）或记账点现构造，判等/哈希一律按字段值语义
    /// （!= 运算符按值），防引用身份混入账本键。
    /// </summary>
    public sealed class TexturedBatchKey : IEquatable<TexturedBatchKey> {
        /// <summary>六面体：alphaTest，默认深度+默认混合（与 MeshAlphaTest、Flat 解析为同一物理批次）。</summary>
        public static readonly TexturedBatchKey Cube = new(null, true, 0, null, RasterizerState.CullCounterClockwiseScissor, null, SamplerState.PointClamp);

        /// <summary>网格不透明/镂空子集：alphaTest，默认深度+默认混合（与 Cube 同一物理批次）。</summary>
        public static readonly TexturedBatchKey MeshAlphaTest = new(null, true, 0, DepthStencilState.Default, RasterizerState.CullCounterClockwiseScissor, null, SamplerState.PointClamp);

        /// <summary>网格透明子集：无 alphaTest，DepthRead+默认混合。</summary>
        public static readonly TexturedBatchKey MeshTransparent = new(null, false, 0, DepthStencilState.DepthRead, RasterizerState.CullCounterClockwiseScissor, null, SamplerState.PointClamp);

        /// <summary>交叉面片：alphaTest，默认深度+默认混合（与 Cube 同一物理批次）。</summary>
        public static readonly TexturedBatchKey Flat = new(null, true, 0, null, RasterizerState.CullCounterClockwiseScissor, BlendState.AlphaBlend, SamplerState.PointClamp);

        public readonly Texture2D Texture;
        public readonly bool UseAlphaTest;
        public readonly int Layer;
        public readonly DepthStencilState DepthStencilState;
        public readonly RasterizerState RasterizerState;
        public readonly BlendState BlendState;
        public readonly SamplerState SamplerState;

        public TexturedBatchKey(Texture2D texture,
            bool useAlphaTest,
            int layer,
            DepthStencilState depthStencilState,
            RasterizerState rasterizerState,
            BlendState blendState,
            SamplerState samplerState) {
            Texture = texture;
            UseAlphaTest = useAlphaTest;
            Layer = layer;
            // 归一化到物理批次的实际解析参数：null → PrimitivesRenderer3D.TexturedBatch 的补缺值。
            // 键不相归一会把同批容量按逻辑键拆开，预扩容取 max 漏掉 sum 的部分。
            DepthStencilState = depthStencilState ?? DepthStencilState.Default;
            RasterizerState = rasterizerState ?? RasterizerState.CullNoneScissor;
            BlendState = blendState ?? BlendState.AlphaBlend;
            SamplerState = samplerState ?? SamplerState.LinearClamp;
        }

        public bool Equals(TexturedBatchKey other) =>
            !ReferenceEquals(other, null)
            && UseAlphaTest == other.UseAlphaTest
            && Layer == other.Layer
            && ReferenceEquals(Texture, other.Texture)
            && ReferenceEquals(DepthStencilState, other.DepthStencilState)
            && ReferenceEquals(RasterizerState, other.RasterizerState)
            && ReferenceEquals(BlendState, other.BlendState)
            && ReferenceEquals(SamplerState, other.SamplerState);

        public override bool Equals(object obj) => obj is TexturedBatchKey other && Equals(other);

        public static bool operator ==(TexturedBatchKey left, TexturedBatchKey right) =>
            ReferenceEquals(left, right) || (!ReferenceEquals(left, null) && left.Equals(right));

        public static bool operator !=(TexturedBatchKey left, TexturedBatchKey right) => !(left == right);

        /// <summary>
        /// 31 进制逐字段累加；state 对象按引用身份取哈希（引擎单例，进程内稳定）。
        /// 归一化后 state 字段恒非 null（Texture 允许 null，null→0），
        /// 与 Equals 的 ReferenceEquals 判等口径一致：相等键必然持有相同 state 引用，哈希必相等。
        /// </summary>
        public override int GetHashCode() {
            unchecked {
                int hash = UseAlphaTest ? unchecked((int)0x9E3779B9) : unchecked((int)0x7F4A7C15);
                hash = hash * 31 + Layer;
                hash = hash * 31 + (Texture != null ? RuntimeHelpers.GetHashCode(Texture) : 0);
                hash = hash * 31 + (DepthStencilState != null ? RuntimeHelpers.GetHashCode(DepthStencilState) : 0);
                hash = hash * 31 + (RasterizerState != null ? RuntimeHelpers.GetHashCode(RasterizerState) : 0);
                hash = hash * 31 + (BlendState != null ? RuntimeHelpers.GetHashCode(BlendState) : 0);
                hash = hash * 31 + (SamplerState != null ? RuntimeHelpers.GetHashCode(SamplerState) : 0);
                return hash;
            }
        }
    }
}
