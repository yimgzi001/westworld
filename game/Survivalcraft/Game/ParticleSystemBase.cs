using System.Collections.Generic;
using Engine.Graphics;

namespace Game {
    /// <summary>按混合模式拆分的 quad 容量需求（供共享批次预扩容）。</summary>
    public struct QuadDemandByBlend {
        public int Additive;
        public int Alpha;

        public static QuadDemandByBlend operator +(QuadDemandByBlend left, QuadDemandByBlend right) =>
            new QuadDemandByBlend { Additive = left.Additive + right.Additive, Alpha = left.Alpha + right.Alpha };

        public static QuadDemandByBlend operator -(QuadDemandByBlend left, QuadDemandByBlend right) =>
            new QuadDemandByBlend { Additive = left.Additive - right.Additive, Alpha = left.Alpha - right.Alpha };
    }

    /// <summary>
    /// 单个共享批次的容量需求条目：批次键（完整物理批次身份，须与入队时 TexturedBatch 的实参一致，
    /// null 状态字段由 <see cref="TexturedBatchKey"/> 构造器按 TexturedBatch 工厂同款规则归一）
    /// + 顶点/索引数上界。
    /// </summary>
    public struct BatchDemandEntry {
        public TexturedBatchKey Key;
        public int VertexCount;
        public int IndexCount;
    }

    public abstract class ParticleSystemBase {
        public SubsystemParticles SubsystemParticles;

        public abstract void Draw(Camera camera);

        public abstract bool Simulate(float dt);

        public virtual void OnAdded() { }

        public virtual void OnRemoved() { }

        /// <summary>
        /// 粒子系统自报容量需求：本系统在场上时最多会向哪个纹理批次入队多少 quad。
        /// SubsystemParticles 在系统增删时据此记账，绘制前对共享批次一次性精确预扩容
        /// （避免大批量入队在 DynamicArray 精确扩容策略下触发逐 quad O(N²) 整段拷贝）。
        /// 返回 null 纹理表示不参与预扩容（走原生逐 quad 扩容行为）。
        /// 契约：纹理与容量需求在系统生命周期内须恒定（注册后运行中改动会导致账本增删不对称）。
        /// 便捷面：仅覆盖"单纹理、Additive/Alpha 两标准批次"的粒子系统（如 ParticleSystem&lt;T&gt;）。
        /// </summary>
        public virtual Texture2D QuadDemandTexture => null;

        /// <summary>与 <see cref="QuadDemandTexture"/> 同生命周期恒定契约；Additive/Alpha 两侧按各自上界独立报告。</summary>
        public virtual QuadDemandByBlend MaxQuadDemand => default;

        /// <summary>
        /// 批次容量需求主自报口：把本系统对全部共享批次的容量上界追加进 <paramref name="demands"/>。
        /// SubsystemParticles 在系统增删时调用本方法记账，绘制前按条目批次身份一次性精确预扩容。
        /// 默认实现把 <see cref="QuadDemandTexture"/>/<see cref="MaxQuadDemand"/> 翻译成两标准批次条目；
        /// 多纹理、非 quad 拓扑或非标准批次状态的系统直接 override 本方法
        /// （契约同上：条目集合在系统生命周期内须恒定，纹理会运行中切换的系统应对全部可能纹理恒报上界）。
        /// </summary>
        public virtual void GetBatchDemands(List<BatchDemandEntry> demands) {
            Texture2D texture = QuadDemandTexture;
            if (texture == null) {
                return;
            }
            QuadDemandByBlend maxQuadDemand = MaxQuadDemand;
            // 两标准批次键实参须与 ParticleSystem<T>.Draw 懒建批次一致（含 PointClamp，键归一才命中同一物理批次）
            if (maxQuadDemand.Additive > 0) {
                demands.Add(new BatchDemandEntry {
                    Key = new TexturedBatchKey(texture, true, 0, DepthStencilState.DepthRead, null, BlendState.Additive, SamplerState.PointClamp),
                    VertexCount = maxQuadDemand.Additive * 4,
                    IndexCount = maxQuadDemand.Additive * 6
                });
            }
            if (maxQuadDemand.Alpha > 0) {
                demands.Add(new BatchDemandEntry {
                    Key = new TexturedBatchKey(texture, true, 0, DepthStencilState.Default, null, BlendState.AlphaBlend, SamplerState.PointClamp),
                    VertexCount = maxQuadDemand.Alpha * 4,
                    IndexCount = maxQuadDemand.Alpha * 6
                });
            }
        }
    }
}