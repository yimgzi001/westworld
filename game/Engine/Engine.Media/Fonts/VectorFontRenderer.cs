using System;
using FontStashSharp;
using FontStashSharp.Interfaces;

namespace Engine.Media {
    /// <summary>
    /// 字形 quad 接收口：矢量图集与位图纹理统一走此口（widget 层按纹理分批，VectorFontBatch2D 为默认实现）
    /// </summary>
    public interface IFontBatchSink {
        void QueueGlyphQuad(Graphics.Texture2D texture, Vector2 destination, Vector2 size, Vector2 uv1, Vector2 uv2, float depth, Color color);
    }

    /// <summary>
    /// 斜体降级 sink 包装（spec §2：italic 缺真源时以 15° 切变近似）：
    /// 仅平移 destination.X（x' = x + ItalicShear * (baselineY - y)，行底沿为不动轴），
    /// 纹理/uv/尺寸/深度/颜色原样透传
    /// </summary>
    internal sealed class ShearedFontBatchSink : IFontBatchSink {
        readonly IFontBatchSink _sink;
        readonly float _baselineY;

        public ShearedFontBatchSink(IFontBatchSink sink, float baselineY) {
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
            _baselineY = baselineY;
        }

        public void QueueGlyphQuad(Graphics.Texture2D texture, Vector2 destination, Vector2 size, Vector2 uv1, Vector2 uv2, float depth, Color color) {
            destination.X += VectorFont.ItalicShear * (_baselineY - destination.Y);
            _sink.QueueGlyphQuad(texture, destination, size, uv1, uv2, depth, color);
        }
    }

    /// <summary>
    /// FSS 渲染器适配：Draw(quad) → IFontBatchSink。
    /// 每字形独立转发其所在图集纹理（FSS 图集可能跨页），不做纹理缓存（分批由 widget 层按纹理做）
    /// </summary>
    public class VectorFontRenderer : IFontStashRenderer {
        readonly VectorFontTextureManager _textureManager = new();
        IFontBatchSink _sink;
        float _depth;
        Color _color;

        public ITexture2DManager TextureManager => _textureManager;

        public void Begin(IFontBatchSink sink, float depth, Color color) {
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
            _depth = depth;
            _color = color;
        }

        public void Draw(object texture, System.Numerics.Vector2 pos, System.Drawing.Rectangle? src, FSColor color,
            float rotation, System.Numerics.Vector2 scale, float depth) {
            var texture2D = (Graphics.Texture2D)texture;
            var rect = src ?? new System.Drawing.Rectangle(0, 0, texture2D.Width, texture2D.Height);
            _sink.QueueGlyphQuad(texture2D,
                new Vector2(pos.X, pos.Y),
                new Vector2(rect.Width, rect.Height),
                new Vector2(rect.X / (float)texture2D.Width, rect.Y / (float)texture2D.Height),
                new Vector2((rect.X + rect.Width) / (float)texture2D.Width, (rect.Y + rect.Height) / (float)texture2D.Height),
                _depth, _color);
        }
    }
}
