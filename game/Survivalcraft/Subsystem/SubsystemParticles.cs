using Engine;
using Engine.Graphics;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game {
    public class SubsystemParticles : Subsystem, IDrawable, IUpdateable {
        public SubsystemTime m_subsystemTime;

        public Dictionary<ParticleSystemBase, bool> m_particleSystems = [];

        /// <summary>各批次身份（纹理+状态组合）的容量需求账本（粒子系统增删时自报累计），绘制前据此预扩容。</summary>
        public readonly Dictionary<TexturedBatchKey, (int VertexCount, int IndexCount)> m_batchDemand = [];

        /// <summary>记账暂存列表（增删时逐系统求值自报条目，避免每次分配）。
        /// 契约：仅在主线程增删路径使用；GetBatchDemands 实现不得重入 Add/RemoveParticleSystem。</summary>
        private static readonly List<BatchDemandEntry> s_demandEntries = [];

        public PrimitivesRenderer3D PrimitivesRenderer = new();

        public bool ParticleSystemsDraw = true;

        public bool ParticleSystemsSimulate = true;

        public int[] m_drawOrders = [300];

        public List<ParticleSystemBase> m_endedParticleSystems = [];

        public UpdateOrder UpdateOrder => UpdateOrder.Default;

        public SubsystemSky SubsystemSky { get; private set; }

        public int[] DrawOrders => m_drawOrders;

        public void AddParticleSystem(ParticleSystemBase particleSystem, bool throwOnAlreadyAdded = false) {
            if (particleSystem.SubsystemParticles == null) {
                m_particleSystems.Add(particleSystem, true);
                particleSystem.SubsystemParticles = this;
                particleSystem.OnAdded();
                BookBatchDemand(particleSystem, 1);
                return;
            }
            if (throwOnAlreadyAdded) {
                throw new InvalidOperationException("Particle system is already added.");
            }
        }

        public void RemoveParticleSystem(ParticleSystemBase particleSystem, bool throwOnNotFound = false) {
            if (particleSystem.SubsystemParticles == this) {
                particleSystem.OnRemoved();
                m_particleSystems.Remove(particleSystem);
                particleSystem.SubsystemParticles = null;
                BookBatchDemand(particleSystem, -1);
                return;
            }
            if (throwOnNotFound) {
                throw new InvalidOperationException("Particle system is not added.");
            }
        }

        /// <summary>粒子系统增删时按其自报的批次条目维护容量需求账本，绘制前据此一次性精确预扩容。</summary>
        private void BookBatchDemand(ParticleSystemBase particleSystem, int sign) {
            s_demandEntries.Clear();
            particleSystem.GetBatchDemands(s_demandEntries);
            foreach (BatchDemandEntry entry in s_demandEntries) {
                if (entry.Key == null || entry.VertexCount <= 0 || entry.IndexCount <= 0) {
                    continue;
                }
                TexturedBatchKey key = entry.Key;
                int vertexCount = m_batchDemand.TryGetValue(key, out (int VertexCount, int IndexCount) value) ? value.VertexCount : 0;
                int indexCount = value.IndexCount;
                vertexCount += sign * entry.VertexCount;
                indexCount += sign * entry.IndexCount;
                if (vertexCount <= 0 && indexCount <= 0) {
                    m_batchDemand.Remove(key);
                }
                else {
                    m_batchDemand[key] = (vertexCount, indexCount);
                }
            }
        }

        public bool ContainsParticleSystem(ParticleSystemBase particleSystem) => particleSystem.SubsystemParticles == this;

        public override void Load(ValuesDictionary valuesDictionary) {
            m_subsystemTime = Project.FindSubsystem<SubsystemTime>(true);
            SubsystemSky = Project.FindSubsystem<SubsystemSky>(true);
        }

        public virtual void Update(float dt) {
            if (ParticleSystemsSimulate) {
                m_endedParticleSystems.Clear();
                foreach (ParticleSystemBase key in m_particleSystems.Keys) {
                    try {
                        if (key.Simulate(m_subsystemTime.GameTimeDelta)) {
                            m_endedParticleSystems.Add(key);
                        }
                    }
                    catch (Exception e) {
                        Log.Error(e);
                        m_endedParticleSystems.Add(key);
                    }
                }
                foreach (ParticleSystemBase endedParticleSystem in m_endedParticleSystems) {
                    RemoveParticleSystem(endedParticleSystem);
                }
            }
        }

        public virtual void Draw(Camera camera, int drawOrder) {
            if (ParticleSystemsDraw) {
                // 按账本（粒子系统增删时自报容量的累计）一次性精确预扩容共享批次：
                // 消除逐 quad/逐系统扩容爬坡（DynamicArray 精确扩容下大批量入队是 O(N²) 拷贝）
                foreach (KeyValuePair<TexturedBatchKey, (int VertexCount, int IndexCount)> entry in m_batchDemand) {
                    PrimitivesRenderer.TexturedBatch(
                        entry.Key.Texture,
                        entry.Key.UseAlphaTest,
                        entry.Key.Layer,
                        entry.Key.DepthStencilState,
                        entry.Key.RasterizerState,
                        entry.Key.BlendState,
                        entry.Key.SamplerState
                    ).EnsureCapacity(entry.Value.VertexCount, entry.Value.IndexCount);
                }
                foreach (ParticleSystemBase key in m_particleSystems.Keys) {
                    try {
                        key.Draw(camera);
                    }
                    catch (Exception e) {
                        Log.Error(e);
                    }
                }
                Shader shader = ContentManager.Get<Shader>("Shaders/AlphaTested");
                shader.GetParameter("u_origin").SetValue(Vector2.Zero);
                shader.GetParameter("u_viewProjectionMatrix").SetValue(camera.ViewProjectionMatrix);
                shader.GetParameter("u_viewPosition").SetValue(camera.ViewPosition);
                shader.GetParameter("u_fogYMultiplier").SetValue(SubsystemSky.VisibilityRangeYMultiplier);
                shader.GetParameter("u_fogColor").SetValue(new Vector3(SubsystemSky.ViewFogColor));
                shader.GetParameter("u_hazeStartDensity").SetValue(new Vector2(SubsystemSky.ViewHazeStart, SubsystemSky.ViewHazeDensity));
                shader.GetParameter("u_fogBottomTopDensity")
                    .SetValue(new Vector3(SubsystemSky.ViewFogBottom, SubsystemSky.ViewFogTop, SubsystemSky.ViewFogDensity));
                shader.GetParameter("u_alphaThreshold").SetValue(0f);
                ShaderParameter parameter = shader.GetParameter("u_texture");
                ShaderParameter parameter2 = shader.GetParameter("u_samplerState");
                foreach (TexturedBatch3D texturedBatch in PrimitivesRenderer.TexturedBatches) {
                    Display.DepthStencilState = texturedBatch.DepthStencilState;
                    Display.RasterizerState = texturedBatch.RasterizerState;
                    Display.BlendState = texturedBatch.BlendState;
                    parameter.SetValue(texturedBatch.Texture);
                    parameter2.SetValue(texturedBatch.SamplerState);
                    texturedBatch.FlushWithDeviceState(shader);
                }
                PrimitivesRenderer.Flush(camera.ViewProjectionMatrix);
            }
        }
    }
}