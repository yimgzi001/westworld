using System;
using System.Collections.Generic;
using FontStashSharp.Interfaces;
using SharpAstro.Fonts;
using SharpAstro.Fonts.Rasterizer;

namespace Engine.Media {
    /// <summary>
    /// 字体源规格：一次 AddFont 对应一条 spec（ttc face index 与变量轴实例）
    /// </summary>
    public struct FontSourceSpec {
        public int FaceIndex;
        public IReadOnlyDictionary<string, float> Variation;
        public FontSourceSpec(int faceIndex, IReadOnlyDictionary<string, float> variation = null) {
            FaceIndex = faceIndex;
            Variation = variation;
        }
    }

    /// <summary>
    /// SharpAstro 光栅器的 FSS loader：官方适配器不支持 ttc face index 与变量轴，
    /// 这里按 FontSystem.AddFont 调用顺序从 spec 队列弹出参数（VectorFont 控制顺序，确定性成立）。
    /// 经 FontSystemSettings.FontLoader 实例注入，零 fork。
    /// </summary>
    public class EngineSharpAstroLoader : IFontLoader {
        readonly Queue<FontSourceSpec> _specs;
        readonly List<IFontSource> _createdSources = new List<IFontSource>();

        /// <summary>已创建的字体源（与 AddFont 顺序一致），供 glyph 覆盖查询使用</summary>
        public IReadOnlyList<IFontSource> CreatedSources => _createdSources;

        public EngineSharpAstroLoader(IEnumerable<FontSourceSpec> specs) => _specs = new Queue<FontSourceSpec>(specs);

        public IFontSource Load(byte[] data) {
            var source = new EngineSharpAstroSource(data, _specs.Dequeue());
            _createdSources.Add(source);
            return source;
        }
    }

    /// <summary>
    /// IFontSource 实现：包 SharpAstro.OpenTypeFont（单面，带 hinting 光栅）
    /// </summary>
    internal class EngineSharpAstroSource : IFontSource {
        readonly OpenTypeFont _font;

        public EngineSharpAstroSource(byte[] data, FontSourceSpec spec) {
            ArgumentNullException.ThrowIfNull(data);
            _font = OpenTypeFont.Load(data, spec.FaceIndex);
            // CFF2 轮廓（CFF 变量字体，Android 16 起 NotoSansCJK-Regular.ttc 即此格式）
            // SharpAstro 不实现：Cff/Glyf 双 null，构造成功但首个字形渲染即 NotSupportedException
            // 直穿 widget 量测炸掉整屏。此为末道防线：坏面到达建链时整链降级（BuildChain
            // catch→位图 / CreateFont→null），各发现路线应在匹配期拒收（FontLocator.ProbeFaceOutlines /
            // ProbeFontFile），不让坏面走到这里
            if (_font.Glyf == null
                && !_font.HasCffOutlines) {
                throw new NotSupportedException("font face has no renderable outlines (CFF2-only variable fonts not supported).");
            }
            if (spec.Variation != null) {
                _font = _font.WithVariation(spec.Variation);
            }
        }

        public void GetMetricsForSize(float fontSize, out int ascent, out int descent, out int lineHeight) {
            // 统一纵向框架：链内所有源必须上报同一线性度量。FSS DynamicSpriteFont.PreDraw 按
            // 串首可解析字形"所属源"取整行 ascent（GetMetrics(FontSourceIndex)），多源回退链里
            // Latin 面（Pericles asc≈0.6em）与 CJK 面（msyh asc≈1.06em）混排时，谁打头整行基线
            // 就差 ~0.46em——表现为同排 Label 与 Link 垂直错位。此处全部源恒报 1em/-0.25em/1.25em
            // （与字形自身 bitmap.Top 的"相对基线"语义配合：基线恒=行 ascent，墨顶=ascent−Top），
            // 基线与首字符的供字源解耦。行高消费方（widget 行距/切变轴）用自有常数，不受此项影响
            ascent = (int)(fontSize + 0.5f);
            descent = -(int)(0.25f * fontSize + 0.5f);
            lineHeight = (int)(1.25f * fontSize + 0.5f);
        }

        public int? GetGlyphId(int codepoint) {
            uint id = _font.GetGlyphId((uint)codepoint);
            return id == 0 ? null : (int)id;
        }

        public void GetGlyphMetrics(int glyphId, float fontSize, out int advance, out int x0, out int y0, out int x1, out int y1) {
            advance = (int)(_font.Hmtx.GetAdvanceWidth((uint)glyphId) * CalculateScale(fontSize) + 0.5f);
            GlyphBitmap bitmap = RenderGlyph(glyphId, fontSize);
            x0 = bitmap.Left;
            y0 = -bitmap.Top;
            x1 = x0 + bitmap.Width;
            y1 = y0 + bitmap.Height;
        }

        public void RasterizeGlyphBitmap(int glyphId, float fontSize, byte[] buffer, int startIndex, int outWidth, int outHeight, int outStride) {
            GlyphBitmap bitmap = RenderGlyph(glyphId, fontSize);
            if (bitmap.IsEmpty) {
                return;
            }
            int width = Math.Min(outWidth, bitmap.Width);
            int height = Math.Min(outHeight, bitmap.Height);
            for (int y = 0; y < height; y++) {
                int srcIndex = y * bitmap.Width;
                int dstIndex = startIndex + y * outStride;
                for (int x = 0; x < width; x++) {
                    buffer[dstIndex + x] = bitmap.Alpha[srcIndex + x];
                }
            }
        }

        public void RasterizeGlyphSDF(int glyphId, float fontSize, byte[] buffer, int startIndex, int padding, byte onedge_value, float pixel_dist_scale)
            => throw new NotImplementedException("EngineSharpAstroSource doesn't support SDF.");

        public int GetGlyphKernAdvance(int previousGlyphId, int glyphId, float fontSize)
            => (int)(_font.GetKerning((uint)previousGlyphId, (uint)glyphId) * CalculateScale(fontSize));

        public float CalculateScaleForTextShaper(float fontSize) => CalculateScale(fontSize);

        float CalculateScale(float fontSize) => fontSize / _font.UnitsPerEm;

        GlyphBitmap RenderGlyph(int glyphId, float fontSize) => _font.RenderGlyphHinted((uint)glyphId, fontSize);

        public void Dispose() { }
    }
}
