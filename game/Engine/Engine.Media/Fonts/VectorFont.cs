using System;
using System.Collections.Generic;
using FontStashSharp;
using FontStashSharp.Interfaces;

namespace Engine.Media {
    /// <summary>
    /// 链条目：一个字体文件（ttc/可变字体）+ 其各样式面规格（FontManager 构建链用）
    /// </summary>
    public class ChainEntry {
        public byte[] Data;

        public string Origin; // 描述用（"Pericles"/"system:msyh"/"user:xxx.ttf"）

        public IReadOnlyDictionary<VectorFontStyle, FontSourceSpec> StyleFaces;
    }

    /// <summary>
    /// 矢量字体链：每样式一个 FontSystem（内部按条目序 fallback）。
    /// 样式缺失→IsXxxAvailable=false，渲染走降级路径（Task 5）。
    /// </summary>
    public class VectorFont : IDisposable {
        public const float DefaultSizeValue = 21f;   // ≈CJK 视觉 19px（rem 锚）
        public const float LineHeightValue = 26f;    // 现位图行距等效

        // 斜体切变常数：15°（PericlesPro Italic 制作角），tan(15°)≈0.2679
        internal const float ItalicShear = 0.2679f;

        readonly FontSystem[] _systems = new FontSystem[4];
        readonly EngineSharpAstroLoader[] _loaders = new EngineSharpAstroLoader[4];
        readonly List<IFontSource> _regularSources = new List<IFontSource>();
        readonly VectorFontRenderer _renderer = new();
        BitmapFont _fallbackBitmapFont;

        /// <summary>
        /// Stroked 白预乘渲染器：FSS Default 的膨胀区像素写 RGB=0/A=coverage（预乘黑），顶点色乘 RGB
        /// 恒黑——描边色无法染色（StrokeColor 白边仍渲染黑边）、fakeBold 在白字上是黑轮廓非加粗。
        /// 此处膨胀形（字芯∪描边）统一 alpha=max(coverage, dilation)、RGB=alpha 白预乘，
        /// 顶点色即可染任意色；膨胀掩码计算与 Default 逐位同源（over 复合公式）。非 Stroked 效果直通 Default
        /// </summary>
        static readonly GlyphRenderer WhitePremultipliedStrokedRenderer = (byte[] input, byte[] output, GlyphRenderOptions options) => {
            if (options.Effect != FontSystemEffect.Stroked
                || options.EffectAmount <= 0
                || options.RasterizationMode != FontRasterizationMode.Standard) {
                GlyphRenderers.Default(input, output, options);
                return;
            }
            int width = options.Size.X;
            int amount = options.EffectAmount;
            int top = width * amount;
            int size = width * options.Size.Y;
            int bottom = size - top;
            int right = width - amount;
            int left = amount;
            for (int i = 0; i < size; i++) {
                int ci = i * 4;
                int col = input[i];
                if (col == 255) {
                    output[ci] = output[ci + 1] = output[ci + 2] = output[ci + 3] = 255;
                    continue;
                }
                int black = 0;
                if (i >= top) {
                    black = input[i - top];
                }
                if (i < bottom) {
                    int d = input[i + top];
                    black = ((255 - d) * black + 255 * d) / 255;
                }
                if (i % width >= left) {
                    int d = input[i - amount];
                    black = ((255 - d) * black + 255 * d) / 255;
                }
                if (i % width < right) {
                    int d = input[i + amount];
                    black = ((255 - d) * black + 255 * d) / 255;
                }
                int alpha = Math.Max(col, black);
                if (alpha == 0) {
                    output[ci] = output[ci + 1] = output[ci + 2] = output[ci + 3] = 0;
                    continue;
                }
                switch (options.GlyphRenderResult) {
                    case GlyphRenderResult.Premultiplied:
                        output[ci] = output[ci + 1] = output[ci + 2] = (byte)alpha;
                        output[ci + 3] = (byte)alpha;
                        break;
                    case GlyphRenderResult.NonPremultiplied:
                        output[ci] = output[ci + 1] = output[ci + 2] = 255;
                        output[ci + 3] = (byte)alpha;
                        break;
                    case GlyphRenderResult.NoAntialiasing: {
                        int v = alpha >= 128 ? 255 : 0;
                        output[ci] = output[ci + 1] = output[ci + 2] = output[ci + 3] = (byte)v;
                        break;
                    }
                }
            }
        };

        public bool IsBoldAvailable => _systems[(int)VectorFontStyle.Bold] != null;
        public bool IsItalicAvailable => _systems[(int)VectorFontStyle.Italic] != null;
        public bool IsBoldItalicAvailable => _systems[(int)VectorFontStyle.BoldItalic] != null;
        public float DefaultSize => DefaultSizeValue;
        public float LineHeight => LineHeightValue;
        public BitmapFont FallbackBitmapFont { get => _fallbackBitmapFont; set => _fallbackBitmapFont = value; }
        internal IReadOnlyList<IFontSource> RegularSources => _regularSources;

        public VectorFont(IReadOnlyList<ChainEntry> entries, BitmapFont fallbackBitmapFont) {
            ArgumentNullException.ThrowIfNull(entries);
            _fallbackBitmapFont = fallbackBitmapFont;
            // 每样式：收集有该样式面的条目 → 一个 FontSystem，AddFont 顺序=链序
            for (int i = 0; i < 4; i++) {
                var style = (VectorFontStyle)i;
                var specs = new List<FontSourceSpec>();
                var datas = new List<byte[]>();
                foreach (var entry in entries) {
                    if (entry.StyleFaces.TryGetValue(style, out var spec)) {
                        specs.Add(spec);
                        datas.Add(entry.Data);
                    }
                }
                if (specs.Count == 0) {
                    continue; // 该样式整链缺失
                }
                // 非 Regular 样式系统尾部按 degrade 序并挂低级样式源：样式面常只覆盖部分文字
                // （链首合成斜体/粗斜面仅有拉丁字形、Windows CJK 全家族无斜体文件），样式请求落到
                // 缺字面时 FSS 逐码点回退，字形不丢（修复前 CJK 斜体/粗斜请求整字空白）。
                // BoldItalic 中间并挂 Bold 面：CJK 缺合成粗斜面时落真 Bold 面（有 CJK），字重不降级。
                // 斜体切变由 ShearedFontBatchSink 统一施加（含回退段）→CJK 斜体=正排字形切变，
                // 与位图伪斜体同观感；度量（MeasureString/GetBoldAdvanceScale）同享回退，不按样式缺字失真
                if (style != VectorFontStyle.Regular) {
                    VectorFontStyle[] fallbackStyles = style == VectorFontStyle.BoldItalic
                        ? [VectorFontStyle.Bold, VectorFontStyle.Regular]
                        : [VectorFontStyle.Regular];
                    foreach (var fbStyle in fallbackStyles) {
                        foreach (var entry in entries) {
                            if (entry.StyleFaces.TryGetValue(fbStyle, out var fbSpec)) {
                                specs.Add(fbSpec);
                                datas.Add(entry.Data);
                            }
                        }
                    }
                }
                // 每样式一个 loader 实例（spec 队列=链序，AddFont→Load 串行一一对应），
                // Regular 的 CreatedSources 供 HasGlyph 查 glyph 覆盖
                var loader = new EngineSharpAstroLoader(specs);
                var settings = new FontSystemSettings {
                    TextureWidth = 1024,
                    TextureHeight = 1024,
                    // 引擎 BlendState.AlphaBlend = (One, InverseSourceAlpha) 预乘混合，
                    // 图集必须预乘（背景 RGB=0 才不会整块叠加顶点色）；NonPremultiplied 是 straight 语义
                    GlyphRenderResult = GlyphRenderResult.Premultiplied,
                    FontLoader = loader,
                    GlyphRenderer = WhitePremultipliedStrokedRenderer
                };
                var system = new FontSystem(settings);
                foreach (var data in datas) {
                    system.AddFont(data);
                }
                _systems[i] = system;
                _loaders[i] = loader;
                if (style == VectorFontStyle.Regular) {
                    // 复制 source 引用供 HasGlyph（loader.Load 顺序=AddFont 顺序=链序）
                    foreach (var source in loader.CreatedSources) {
                        _regularSources.Add(source);
                    }
                }
            }
        }

        /// <summary>
        /// Regular 链是否覆盖该码点（run 切分用，按链序任一源命中即可）
        /// </summary>
        public bool HasGlyph(int codepoint) {
            foreach (var source in _regularSources) {
                if (source.GetGlyphId(codepoint) != null) {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 度量：与 DrawText 同一降级解析（缺失样式→Regular），保证度量的就是将绘制的矢量链；
        /// 仅 Regular 链整链缺失时返回 Zero，调用方退位图度量。<br/>
        /// 与 DrawText 同构分段量测（HasGlyph 切分）：矢量段走 FSS、链外段走位图回退度量——
        /// FSS 对链外字符只记 .notdef advance（Pericles ≈0.33em），整串直量会把 CJK 等回退段
        /// 宽度记小 ~3 倍，与 DrawText 的位图回退推进错位（FontTextWidget 整串量推行宽/锚点，
        /// 内置字体等缺字链下整行错位即此；宽随字长线性放大）。<br/>
        /// Y=各段行高最大值（矢量段 FSS ink bounds、位图段 LineHeight×scale），并非 FSS 全串
        /// bounds 口径，消费方勿与 DrawText 返回的行高语义混用（现网消费方均只取 X）。
        /// </summary>
        public Vector2 MeasureString(string text, float size, bool bold = false, bool italic = false) {
            ArgumentNullException.ThrowIfNull(text);
            var font = GetStyleFont(ResolveStyle(bold, italic, degrade: true), size);
            if (font == null) {
                return Vector2.Zero; // 整链缺失，调用方退位图度量
            }
            float width = 0f;
            float height = 0f;
            int start = 0;
            bool segVector = text.Length > 0 && HasGlyph(text[0]);
            for (int i = 1; i <= text.Length; i++) {
                bool segIsVector = i < text.Length && HasGlyph(text[i]);
                if (i < text.Length
                    && segIsVector == segVector) {
                    continue; // 同态延续，段继续累积
                }
                if (segVector) {
                    var size2 = font.MeasureString(text[start..i]);
                    width += size2.X;
                    if (size2.Y > height) {
                        height = size2.Y;
                    }
                }
                else if (_fallbackBitmapFont != null) {
                    // 与 DrawBitmapFallback 同几何（kerning/跳字符同口径）；回退字体缺失时段不画
                    // 不推进（DrawText 同行为），宽度记 0
                    width += MeasureBitmapRun(text[start..i], size);
                    float bitmapHeight = _fallbackBitmapFont.LineHeight * (size / DefaultSizeValue * _fallbackBitmapFont.Scale);
                    if (bitmapHeight > height) {
                        height = bitmapHeight;
                    }
                }
                start = i;
                segVector = segIsVector;
            }
            return new Vector2(width, height);
        }

        // 粗体排版宽度系数探针串：Latin 比例字真 Bold 面 advance>Regular，CJK 等宽 em 粗体 advance 恒同，
        // 分档实测；wide 分类阈 CJK 部首扩展起（汉字/假名/谚文全落等宽档）
        const string BoldScaleLatinProbe = "Survivalcraft abcdefg 0123";
        const string BoldScaleWideProbe = "生存战争测试字体";
        const int BoldScaleWideCharThreshold = 0x2E80;

        float? _boldScaleLatin;
        float? _boldScaleWide;

        /// <summary>
        /// 粗体 span 的排版宽度系数（RichTextLayout 布局预算用）：按 ResolveStyle(bold, degrade:true)
        /// 实际解析结果量探针串 Bold/Regular advance 比。真 Bold 面→实测比值；降级 Stroked1
        /// （FSS Stroked 只扩字形纹理边，XAdvance 取 Regular advance 不变）与整链缺失→1。
        /// 惰性缓存：链构建后度量不变；Dispose 后度量守卫返 Zero→兜 1
        /// </summary>
        public float GetBoldAdvanceScale(char c) {
            ref float? cache = ref c >= BoldScaleWideCharThreshold ? ref _boldScaleWide : ref _boldScaleLatin;
            if (cache.HasValue) {
                return cache.Value;
            }
            var regular = GetStyleFont(ResolveStyle(false, false, degrade: true), DefaultSize);
            var bold = GetStyleFont(ResolveStyle(true, false, degrade: true), DefaultSize);
            float scale = 1f;
            if (regular != null && bold != null) {
                string probe = c >= BoldScaleWideCharThreshold ? BoldScaleWideProbe : BoldScaleLatinProbe;
                float regularWidth = regular.MeasureString(probe).X;
                if (regularWidth > 0f) {
                    float ratio = bold.MeasureString(probe).X / regularWidth;
                    if (float.IsFinite(ratio) && ratio > 0f) {
                        scale = ratio;
                    }
                }
            }
            cache = scale;
            return scale;
        }

        internal SpriteFontBase GetStyleFont(VectorFontStyle style, float size)
            => _systems[(int)style]?.GetFont(size);

        /// <summary>
        /// run 切分绘制：矢量有字段走 FSS，缺字段走 FallbackBitmapFont 纹理。
        /// 行为锚：text 单行（换行由调用方处理），pos=行左上，字形按 baseline 对齐。
        /// 样式降级（spec §2）：bold 缺真 Bold 面→Stroked1 描边消歧；italic 缺真 Italic/BoldItalic 面→15° 切变；
        /// 位图回退段恒正排 Regular（无描边/切变）。返回绘制高度（各段行高最大值）。
        /// characterSpacing：pos 空间的字符间距 px（FSS 段按其原值并入字符间推进、位图段每字符后直加），
        /// 与同空间位移同标度；尾字符后补齐一个间距（FSS 计字符间 n−1，补 1 使段宽=布局逐字符 n 的口径）。
        /// strokeAmount&gt;0：矢量段以 Stroked(strokeAmount) 绘制（alpha 形态膨胀，advance/度量不变；
        /// 膨胀量钳制 0..8 防图集溢出），并补偿 FSS 上游 Stroked 字形 (N,N) 笔位偏移使环均匀外露，
        /// 优先级高于 fakeBold 的 Stroked1——独立描边色双遍绘制方案的描边遍用此参，字芯遍再调本方法恢复字重；
        /// 位图回退段无描边能力，该段忽略此参
        /// </summary>
        public float DrawText(IFontBatchSink sink, string text, Vector2 pos, Color color, float size, bool bold = false, bool italic = false, float characterSpacing = 0f, int strokeAmount = 0) {
            ArgumentNullException.ThrowIfNull(sink);
            ArgumentNullException.ThrowIfNull(text);
            if (text.Length == 0) {
                return 0f;
            }
            // 样式降级解析：ResolveStyle 只返回实际存在的系统（Regular 链缺失时解析出的 Regular 为 null）
            var style = ResolveStyle(bold, italic, degrade: true);
            var system = _systems[(int)style] ?? _systems[(int)VectorFontStyle.Regular];
            if (system == null) {
                // 矢量链整链缺失：整行走位图回退
                return DrawBitmapFallback(sink, text, pos, color, size, 0f, characterSpacing);
            }
            // bold 请求落到 Regular 源（链内无真 Bold 面）→Stroked1；italic 请求落到非 Italic/BoldItalic 源→切变。
            // bold+italic 仅 Regular 可用时两者叠加（spec 裁定 combined）
            bool fakeBold = bold && ReferenceEquals(system, _systems[(int)VectorFontStyle.Regular]);
            bool fakeItalic = italic && style != VectorFontStyle.Italic && style != VectorFontStyle.BoldItalic;
            // 描边覆盖（strokeAmount>0）优先于 fakeBold 的 Stroked1：双遍绘制的描边遍整链（含真 Bold 面）
            // 按膨胀量描边，字芯遍以 strokeAmount=0 再调恢复字重消歧
            bool strokeOverride = strokeAmount > 0;
            var effect = strokeOverride || fakeBold ? FontSystemEffect.Stroked : FontSystemEffect.None;
            // 膨胀上限钳制：FSS 按 (size+2N)² 尺寸往 1024² 图集塞字形，N 过大直接 throw 炸 UI；
            // 缓存键 (int)effect<<16|amount 在 N≥65536 时串位。UI 描边 8px 远超语义所需
            int effectAmount = Math.Clamp(strokeOverride ? strokeAmount : fakeBold ? 1 : 0, 0, 8);
            var spriteFont = system.GetFont(size);
            // FSS 上游怪癖补偿：Stroked 字形的光栅芯被画进膨胀缓冲 (N,N) 偏移处
            //（FontAtlas.RasterizeGlyphBitmap startIndex=N+N*width），quad 又按芯度量 RenderOffset 摆位
            // ——芯整体落在笔位 +(N,N)，可见环只剩右/下两条带（硬阴影观感）。笔位回移 (-N,-N)
            // 使环均匀外露、芯回到真实笔位（advance 记账与 effect 无关，不动）
            float strokeShift = effect == FontSystemEffect.Stroked ? effectAmount : 0f;
            // 切变只作用于矢量段（经 _renderer 的 quad），位图段直连原始 sink 正排
            IFontBatchSink vectorSink = fakeItalic
                ? new ShearedFontBatchSink(sink, pos.Y - strokeShift + spriteFont.LineHeight) // baselineY≈行底沿，切变不动轴（随位移同步）
                : sink;
            var fsColor = new FSColor(color.R, color.G, color.B, color.A);
            _renderer.Begin(vectorSink, 0f, color);
            // 简单状态机：连续同态（矢量/位图）字符合成一段，遇态变或串尾 flush
            float drawHeight = 0f;
            float xAdvance = 0f; // 已渲段累计占宽，后段左移进
            int runStart = 0;
            bool runIsBitmap = !HasGlyph(text[0]);
            for (int i = 1; i <= text.Length; i++) {
                bool isBitmap = i < text.Length && !HasGlyph(text[i]);
                if (i < text.Length && isBitmap == runIsBitmap) {
                    continue; // 同态延续，段继续累积
                }
                // flush [runStart, i)
                string run = text[runStart..i];
                float runHeight;
                if (runIsBitmap) {
                    runHeight = DrawBitmapFallback(sink, run, new Vector2(pos.X + xAdvance, pos.Y), color, size, 0f, characterSpacing);
                    if (_fallbackBitmapFont != null) {
                        // spacing 只计实际推进的字符（\r/\u200b 被推进循环跳过不加间距）；
                        // 回退字体缺失时段整体不画不推进，累计量与 spacing 一并不动
                        xAdvance += MeasureBitmapRun(run, size) + characterSpacing * CountBitmapAdvanced(run);
                    }
                }
                else {
                    spriteFont.DrawText(_renderer, run,
                        new System.Numerics.Vector2(pos.X + xAdvance - strokeShift, pos.Y - strokeShift), fsColor,
                        characterSpacing: characterSpacing,
                        effect: effect, effectAmount: effectAmount);
                    runHeight = spriteFont.LineHeight;
                    // MeasureString 带 spacing 只计字符间（n−1），补 1 个凑每字符后一个的布局口径，
                    // 段间起点与逐字符排版步进逐位同源
                    xAdvance += spriteFont.MeasureString(run,
                        characterSpacing: characterSpacing,
                        effect: effect, effectAmount: effectAmount).X + characterSpacing;
                }
                if (runHeight > drawHeight) {
                    drawHeight = runHeight;
                }
                runStart = i;
                runIsBitmap = isBitmap;
            }
            return drawHeight;
        }

        // 位图回退段绘制：FallbackBitmapFont 字形 quad 经 sink（几何镜像 FontBatch2D.QueueLine：
        // 跳过 \r、\u200b，\u00a0→空格，推进含 kerning，字形尺寸/偏移按 size 比例缩放）。
        // 恒正排 Regular：无 Stroked 消歧、无斜体切变（spec 裁定 (a)；视觉底沿对齐锚
        // BitmapBaselineAnchor 由 Task 10 探针断言校准，初值 0 即此实现）
        float DrawBitmapFallback(IFontBatchSink sink, string text, Vector2 pos, Color color, float size, float depth, float characterSpacing = 0f) {
            var font = _fallbackBitmapFont;
            if (font == null || font.Texture == null) {
                return 0f;
            }
            float scale = size / DefaultSizeValue * font.Scale; // 位图按矢量 size 比例缩放（19px 视觉锚在 size=21 对齐）
            Vector2 drawPos = pos;
            for (int i = 0; i < text.Length; i++) {
                char c = text[i];
                if (c == '\r'
                    || c == '\u200b') {
                    continue;
                }
                if (c == '\u00a0') {
                    c = ' ';
                }
                var glyph = font.GetGlyph(c);
                if (!glyph.IsBlank) {
                    Vector2 glyphSize = new(
                        (glyph.TexCoord2.X - glyph.TexCoord1.X) * font.Texture.Width * scale,
                        (glyph.TexCoord2.Y - glyph.TexCoord1.Y) * font.Texture.Height * scale);
                    sink.QueueGlyphQuad(font.Texture,
                        drawPos + glyph.Offset * scale,
                        glyphSize,
                        glyph.TexCoord1, glyph.TexCoord2,
                        depth, color);
                }
                float kerning = i < text.Length - 1 ? font.GetKerning(c, text[i + 1]) : 0f;
                drawPos.X += (glyph.Width - kerning + font.Spacing.X) * scale + characterSpacing;
            }
            return font.LineHeight * scale;
        }

        // 位图回退段度量：与 DrawBitmapFallback 同一几何（BitmapFont.MeasureText 内部乘 font.Scale，
        // 且同样跳过 \r/\u200b、\u00a0→空格、按 kerning 扣减，推进公式一致）
        float MeasureBitmapRun(string text, float size) {
            var font = _fallbackBitmapFont;
            if (font == null) {
                return 0f;
            }
            return font.MeasureText(text, new Vector2(size / DefaultSizeValue), Vector2.Zero).X;
        }

        // DrawBitmapFallback 实际推进的字符数：跳过 \r/\u200b（\u00a0 转空格照常推进）——
        // 段宽的 spacing 记账与推进循环同口径
        static int CountBitmapAdvanced(string text) {
            int count = 0;
            for (int i = 0; i < text.Length; i++) {
                char c = text[i];
                if (c != '\r'
                    && c != '\u200b') {
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// 样式解析：degrade=false 取精确样式；degrade=true 按可用性沿链回退
        /// （boldItalic→BoldItalic→Bold→Regular；bold→Bold→Regular；italic→Italic→Regular），
        /// 返回值对应实际存在的 FontSystem（Regular 链缺失时返回的 Regular 解析为 null，由调用方退位图）
        /// </summary>
        internal VectorFontStyle ResolveStyle(bool bold, bool italic, bool degrade) {
            if (bold && italic) {
                if (!degrade || IsBoldItalicAvailable) {
                    return VectorFontStyle.BoldItalic;
                }
                if (IsBoldAvailable) {
                    return VectorFontStyle.Bold;
                }
                return VectorFontStyle.Regular;
            }
            if (bold) {
                if (!degrade || IsBoldAvailable) {
                    return VectorFontStyle.Bold;
                }
                return VectorFontStyle.Regular;
            }
            if (italic) {
                if (!degrade || IsItalicAvailable) {
                    return VectorFontStyle.Italic;
                }
            }
            return VectorFontStyle.Regular;
        }

        public void Dispose() {
            // 先断引用再释放：热切换 dispose 旧链时，切换动画未过半的旧屏/未关对话框仍持本链
            // 在层次中绘制（约数帧），FontSystem.Dispose 清空 _fontSources 后任何迟到的 GetFont
            // 都抛 "Could not create a font without a single font source"。置空走既有的缺链守卫
            // （度量=Zero、DrawText/HasGlyph=位图回退、IsXxxAvailable=false），陈旧持有者静默
            // 退位图，与链缺失降级同型
            for (int i = 0; i < _systems.Length; i++) {
                var system = _systems[i];
                _systems[i] = null;
                system?.Dispose();
            }
            _regularSources.Clear();
        }
    }
}
