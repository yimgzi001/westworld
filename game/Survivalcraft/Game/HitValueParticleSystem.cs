using Engine;
using Engine.Graphics;
using Engine.Media;

namespace Game {
    public class HitValueParticleSystem : ParticleSystem<HitValueParticleSystem.Particle> {
        public class Particle : Game.Particle {
            public float TimeToLive;

            public Vector3 Velocity;

            public Color BaseColor;

            public string Text;
        }

        public FontBatch3D m_batch;

        public VectorFontSink3D m_sink;

        public HitValueParticleSystem(Vector3 position, Vector3 velocity, Color color, string text) : base(1) {
            Random random = new();
            Particle obj = Particles[0];
            obj.IsActive = true;
            obj.Position = position;
            obj.TimeToLive = 0.9f;
            obj.Velocity = velocity + random.Vector3(0.75f) * new Vector3(1f, 0f, 1f) + 0.5f * Vector3.UnitY;
            obj.BaseColor = color;
            obj.Text = text;
        }

        public override bool Simulate(float dt) {
            dt = Math.Clamp(dt, 0f, 0.1f);
            float num = MathF.Pow(0.1f, dt);
            bool flag = false;
            for (int i = 0; i < Particles.Length; i++) {
                Particle particle = Particles[i];
                if (particle.IsActive) {
                    flag = true;
                    particle.TimeToLive -= dt;
                    if (particle.TimeToLive > 0f) {
                        particle.Velocity += new Vector3(0f, 0.5f, 0f) * dt;
                        particle.Velocity *= num;
                        particle.Position += particle.Velocity * dt;
                        particle.Color = particle.BaseColor * MathUtils.Saturate(2f * particle.TimeToLive);
                    }
                    else {
                        particle.IsActive = false;
                    }
                }
            }
            return !flag;
        }

        public override void Draw(Camera camera) {
            VectorFont chain = FontManager.DefaultChain;
            if (chain == null) {
                if (m_batch == null) {
                    m_batch = SubsystemParticles.PrimitivesRenderer.FontBatch(FontManager.FallbackBitmapFont, 0, DepthStencilState.None);
                }
            }
            else if (m_sink == null) {
                // 矢量分流：伤害数字走 3D 矢量链（billboard 基向量与位图路径同口径；层 0/无深度
                // 同位图批）。字号按位图行盒折算（牌子先例公式）后，矢量 px 与位图 px 在同一基向量
                // 下世界尺寸等效——基向量 s 每位图字形 px，折算使 1 矢量 px ≡ 1 位图 px
                m_sink = new VectorFontSink3D(SubsystemParticles.PrimitivesRenderer, SamplerState.LinearClamp, 0, DepthStencilState.None);
            }
            Vector3 viewDirection = camera.ViewDirection;
            Vector3 vector = Vector3.Normalize(Vector3.Cross(viewDirection, Vector3.UnitY));
            Vector3 v = -Vector3.Normalize(Vector3.Cross(vector, viewDirection));
            for (int i = 0; i < Particles.Length; i++) {
                Particle particle = Particles[i];
                if (particle.IsActive) {
                    float num = Vector3.Distance(camera.ViewPosition, particle.Position);
                    float num2 = MathUtils.Saturate(3f * (num - 0.2f));
                    float num3 = MathUtils.Saturate(0.2f * (20f - num));
                    float num4 = num2 * num3;
                    if (num4 > 0f) {
                        float s = 0.006f * MathF.Sqrt(num);
                        Color color = particle.Color * num4;
                        if (chain != null) {
                            BitmapFont bm = FontManager.FallbackBitmapFont;
                            m_sink.QueueText(
                                chain,
                                particle.Text,
                                particle.Position,
                                vector * s,
                                v * s,
                                color,
                                chain.DefaultSize * (bm.GlyphHeight + bm.Spacing.Y) * bm.Scale / chain.LineHeight,
                                TextAnchor.HorizontalCenter | TextAnchor.VerticalCenter,
                                strokeColor: Color.Black,
                                strokeAmount: 1,
                                resolutionScale: 3f
                            );
                        }
                        else {
                            m_batch.QueueText(
                                particle.Text,
                                particle.Position,
                                vector * s,
                                v * s,
                                color,
                                TextAnchor.HorizontalCenter | TextAnchor.VerticalCenter,
                                Vector2.Zero
                            );
                        }
                    }
                }
            }
        }
    }
}