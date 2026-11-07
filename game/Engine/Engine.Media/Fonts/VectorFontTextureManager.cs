using FontStashSharp.Interfaces;

namespace Engine.Media {
    /// <summary>
    /// FSS 动态图集纹理工厂：图集即引擎 Texture2D（RGBA8888），与引擎纹理体系同源
    /// </summary>
    public class VectorFontTextureManager : ITexture2DManager {
        // SetTextureData 走 Texture2D 子矩形上传（TexSubImage2D），无需缓存全尺寸缓冲。
        // FSS FontAtlas.EnsureTexture 创建图集后必调 EraseTexture 全零清底（含 IsEmpty 字形预留格），
        // 未写字形格保持透明，与旧整传路径语义一致。
        public object CreateTexture(int width, int height) => new Graphics.Texture2D(width, height, 1, Graphics.ColorFormat.Rgba8888);

        public System.Drawing.Point GetTextureSize(object texture) {
            var texture2D = (Graphics.Texture2D)texture;
            return new System.Drawing.Point(texture2D.Width, texture2D.Height);
        }

        public void SetTextureData(object texture, System.Drawing.Rectangle bounds, byte[] data) {
            var texture2D = (Graphics.Texture2D)texture;
            texture2D.SetData(0, bounds.X, bounds.Y, bounds.Width, bounds.Height, data);
        }
    }
}
