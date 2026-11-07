using Engine;
using Engine.Graphics;
using Engine.Media;

namespace Game {
    // 富文本部件：多 span（颜色/字号/粗体/链接）单 FontBatch 合批绘制
    // 排版走 RichTextLayout；命中测试按行内 run 的 x 区间反查（单行 LTR 无 bidi）
    public class RichTextWidget : Widget {
        public List<TextSpan> m_spans = [];

        public BitmapFont m_font;

        public VectorFont m_vectorFont;

        public VectorFont m_monoVectorFont;

        public Vector2 m_fontSpacing;

        public float m_fontScale;

        public int m_maxLines = int.MaxValue;

        public bool m_wordWrap;

        public TextWordBreak m_wordBreak = TextWordBreak.BreakWord;

        public bool m_ellipsis;

        float m_lineHeightMultiplier = 1f;

        float m_lineHeightPx;

        public readonly RichTextLayout m_layout = new();

        public Vector2? m_layoutSize;

        public float? m_layoutAvailableWidth;

        public float? m_layoutAvailableHeight;

        public bool m_isDirty = true;

        public Vector2 Size { get; set; }

        public List<TextSpan> Spans {
            get => m_spans;
            set {
                if (value != m_spans) {
                    m_spans = value ?? [];
                    MarkLayoutDirty();
                }
            }
        }

        // 就地修改 Spans 列表内容后调用（列表引用不变时引擎无法感知变化）
        public void MarkLayoutDirty() {
            m_isDirty = true;
        }

        public TextAnchor TextAnchor { get; set; }

        public BitmapFont Font {
            get => m_font ?? FontManager.FallbackBitmapFont;
            set {
                if (value != m_font) {
                    m_font = value;
                    MarkLayoutDirty();
                }
            }
        }

        // 矢量字体链（P4）：null=位图路径（零改动）；挂链时排版 advance 与绘制同源矢量实测
        //（样式降级 Stroked1/切变在 VectorFont.DrawText 内解析），缺字 run 回退位图伪粗/切变路径。
        // 行内代码（span.Monospace）用 MonoVectorFont
        public VectorFont VectorFont {
            get => m_vectorFont;
            set {
                if (value != m_vectorFont) {
                    m_vectorFont = value;
                    MarkLayoutDirty();
                }
            }
        }

        // 等宽矢量链：行内代码 span 的排版与绘制链；null=随 VectorFont（口径同上）
        public VectorFont MonoVectorFont {
            get => m_monoVectorFont;
            set {
                if (value != m_monoVectorFont) {
                    m_monoVectorFont = value;
                    MarkLayoutDirty();
                }
            }
        }

        public float FontScale {
            get => m_fontScale;
            set {
                if (value != m_fontScale) {
                    m_fontScale = value;
                    MarkLayoutDirty();
                }
            }
        }

        public Vector2 FontSpacing {
            get => m_fontSpacing;
            set {
                if (value != m_fontSpacing) {
                    m_fontSpacing = value;
                    MarkLayoutDirty();
                }
            }
        }

        public bool WordWrap {
            get => m_wordWrap;
            set {
                if (value != m_wordWrap) {
                    m_wordWrap = value;
                    MarkLayoutDirty();
                }
            }
        }

        /// <summary>断行模式：默认 BreakWord=词边界回退+无断点强拆（引擎历史行为）；Normal/KeepAll 无断点纯溢出；BreakAll=行尾即拆。</summary>
        public TextWordBreak WordBreak {
            get => m_wordBreak;
            set {
                if (value != m_wordBreak) {
                    m_wordBreak = value;
                    MarkLayoutDirty();
                }
            }
        }

        public bool Ellipsis {
            get => m_ellipsis;
            set {
                if (value != m_ellipsis) {
                    m_ellipsis = value;
                    MarkLayoutDirty();
                }
            }
        }

        // line-height 倍率（CSS number/em/% 同义，1=默认单倍）；HTML builder 侧段落级声明
        // 覆盖容器继承后直设（默认 1，0 会算出零行距——消费侧保证 >0 才写）
        public float LineHeightMultiplier {
            get => m_lineHeightMultiplier;
            set {
                if (value != m_lineHeightMultiplier) {
                    m_lineHeightMultiplier = value;
                    MarkLayoutDirty();
                }
            }
        }

        // line-height 绝对值 px（px/pt/rem 归一结果）；>0 优先于倍率，0=无绝对声明
        public float LineHeightPx {
            get => m_lineHeightPx;
            set {
                if (value != m_lineHeightPx) {
                    m_lineHeightPx = value;
                    MarkLayoutDirty();
                }
            }
        }

        float m_letterSpacing;

        float m_firstLineIndent;

        float m_firstLineIndentPercent;

        // CSS text-indent（块级首行缩进）：px 声明直落；% 声明落 FirstLineIndentPercent
        //（50=50%），UpdateLayout 期相对本件排版可用宽解析为有效 px 缓存 m_resolvedFirstLineIndent。
        // 声明 build 期定死不运行期变，有效值是宽的纯函数，量测缓存键（宽+高）不扩。
        // 折行预算吃法在 RichTextLayout.Layout（只减整个 layout 第一行），行 x 偏移在
        // Draw/FindLinkAtPoint（只移首行，两处必须同源同值）
        public float FirstLineIndent {
            get => m_firstLineIndent;
            set {
                // 规范化：负值（CSS hanging 不支持）与 NaN/∞ 直钳 0——NaN 经 MathF.Max 钳不住
                // 必须 IsFinite 显式判；规范化后 Draw/命中侧首行 += 偏移无条件安全
                value = !float.IsFinite(value) || value < 0f ? 0f : value;
                if (value != m_firstLineIndent) {
                    m_firstLineIndent = value;
                    MarkLayoutDirty();
                }
            }
        }

        public float FirstLineIndentPercent {
            get => m_firstLineIndentPercent;
            set {
                value = !float.IsFinite(value) || value < 0f ? 0f : value;
                if (value != m_firstLineIndentPercent) {
                    m_firstLineIndentPercent = value;
                    MarkLayoutDirty();
                }
            }
        }

        // 上次排版解析出的有效首行缩进 px（px 声明 +% 折算；% 无有限正宽基准时只落 px）
        public float m_resolvedFirstLineIndent;

        // CSS letter-spacing（块级继承属性，builder 递归链归一 px 传入）：进 CharacterAdvance
        // 排版宽度——影响测量与折行判定，必须 MarkLayoutDirty；负值合法（CSS 收紧字距）。
        // 默认 0 时 setter 不触发（首建即 0）且 advance +0 浮点无变化，零回归
        public float LetterSpacing {
            get => m_letterSpacing;
            set {
                if (value != m_letterSpacing) {
                    m_letterSpacing = value;
                    MarkLayoutDirty();
                }
            }
        }

        public int MaxLines {
            get => m_maxLines;
            set {
                if (value != m_maxLines) {
                    m_maxLines = value;
                    MarkLayoutDirty();
                }
            }
        }

        // span 未指定颜色时的默认色
        public Color Color { get; set; }

        public bool DropShadow { get; set; }

        public bool TextureLinearFilter { get; set; }

        // 链接下划线；关闭后链接仅靠 span 颜色区分
        public bool ShowLinkUnderline { get; set; }

        public bool HasContent {
            get {
                foreach (TextSpan span in m_spans) {
                    if (!string.IsNullOrEmpty(span.Text)) {
                        return true;
                    }
                }
                return false;
            }
        }

        public event Action<string> LinkClicked;

        public RichTextWidget() {
            IsHitTestVisible = false;
            FontScale = 1f;
            Color = Color.White;
            TextureLinearFilter = true;
            ShowLinkUnderline = true;
            Size = new Vector2(-1f);
            // P4：默认启用矢量链（回滚点=本行，与 FontTextWidget ctor 同口径）；链未构建/构建失败时为 null → 位图伪粗/切变路径
            VectorFont = FontManager.DefaultChain;
            MonoVectorFont = FontManager.MonoChain;
        }

        public override void Update() {
            if (LinkClicked != null
                && Input.Click.HasValue) {
                TextRun startRun = FindLinkAtPoint(ScreenToWidget(Input.Click.Value.Start));
                if (startRun != null
                    && FindLinkAtPoint(ScreenToWidget(Input.Click.Value.End)) == startRun) {
                    LinkClicked(startRun.Span.Href);
                }
            }
        }

        public override void Draw(DrawContext dc) {
            if (m_layout.Lines.Count == 0
                || Color.A == 0) {
                return;
            }
            // 背景/下划线用 layer=-1 的 FlatBatch、删除线用 layer=2：渲染器按 batch Layer 排序绘制，
            // -1 排在文字批（FontBatch layer=1）之前背景才压在文字下；删除线横穿字形，若也在文字下会被
            // Pericles 烘焙黑描边切成暗缝（实测穿越处亮度 255→128），故排到文字批之后画在字上
            bool needsUnderlayBatch = ShowLinkUnderline || HasBackground || HasUnderline || HasBorder;
            FlatBatch2D flatBatch2D = needsUnderlayBatch ? dc.PrimitivesRenderer2D.FlatBatch(-1, DepthStencilState.None) : null;
            FlatBatch2D overlayBatch2D = HasStrikethrough ? dc.PrimitivesRenderer2D.FlatBatch(2, DepthStencilState.None) : null;
            SamplerState samplerState = TextureLinearFilter ? SamplerState.LinearClamp : SamplerState.PointClamp;
            FontBatch2D fontBatch2D = dc.PrimitivesRenderer2D.FontBatch(Font, 1, DepthStencilState.None, null, null, samplerState);
            // 矢量批接收口（P4，复用 FontTextWidget.VectorGlyphSink）：按图集纹理分批缓存并记录本件
            // 入队前顶点数，Draw 收尾统一 TransformTriangles(GlobalTransform)——与 fontBatch2D 同粒度；
            // 位图路径（VectorFont=null）不建，零开销。
            // GT 全局缩放的栅格补偿与 FontTextWidget 同款：字形按屏上像素栅格化（vectorSize×gtScale），
            // sink.Scale=1/gtScale 把 FSS quad 除回 widget 空间；本件走 pos 直传（非 Pivot），pos 同乘
            // gtScale 进 FSS 空间，除回后落点不变
            float vectorGtScale = VectorFont != null || MonoVectorFont != null
                ? (MathF.Abs(GlobalTransform.M11) is float vs and > 0.0001f ? vs : 1f)
                : 1f;
            FontTextWidget.VectorGlyphSink vectorGlyphSink = VectorFont != null || MonoVectorFont != null ? new FontTextWidget.VectorGlyphSink(dc.PrimitivesRenderer2D, samplerState) {
                Scale = 1f / vectorGtScale
            } : null;
            int batchStart = fontBatch2D.TriangleVertices.Count;
            float blockY = CalculateBlockY();
            TextLine firstLine = m_layout.Lines[0];
            foreach (TextLine line in m_layout.Lines) {
                float lineX = 0f;
                if ((TextAnchor & TextAnchor.HorizontalCenter) != 0) {
                    lineX = (ActualSize.X - line.Width) / 2f;
                }
                else if ((TextAnchor & TextAnchor.Right) != 0) {
                    lineX = ActualSize.X - line.Width;
                }
                // text-indent：首行整体右移有效缩进。锚点先按内容宽定位再叠加——居中/右对齐下
                // 缩进为纯右移近似（严格 CSS 口径缩进占首行盒宽、会再挤压锚点偏移，留档不做）；
                // 背景/边框/下划线/省略号随 run x 走自动带上
                if (line == firstLine) {
                    lineX += m_resolvedFirstLineIndent;
                }
                foreach (TextRun run in line.Runs) {
                    // run 级淡出系数：span 级（行内 opacity 声明）。块级整件淡出走 ColorTransform
                    //（builder 落 White×v，经 GlobalColorTransform 级联）；fade==1 走原路径零改动
                    float fade = run.Span.Opacity;
                    Color color = (run.Span.Color ?? Color) * GlobalColorTransform;
                    if (fade < 1f) {
                        // 全通道（含 A）×fade＝预乘淡出：AlphaBlend(One, InvSrcAlpha) 语义下
                        // 等价真透明度，参照 BevelledRectangleWidget.PremultiplyAlpha 先例
                        color *= fade;
                    }
                    if (color.A == 0
                        && !run.Span.IsLink
                        && run.Span.BackgroundColor.A == 0
                        && run.Span.Border == null) {
                        continue;
                    }
                    Vector2 scale = new(SpanScale(run.Span));
                    // 字距折入绘制侧 spacing（QueueLine 推进=glyph.Width−kerning+fullSpacing，
                    // font 单位经 right 缩放）：除回本 run 的 scale×Font.Scale 还原绝对 px，
                    // 与排版侧 CharacterAdvance+LetterSpacing 同值——否则 run 宽虚胀、字形紧排，
                    // 折行/锚点/命中与实画系统性漂移（排版-绘制不变量，文件头自述）。
                    // 矢量 run 不走此量（QueueText 只服务位图），字距经 DrawText 的 characterSpacing 直绘
                    Vector2 drawSpacing = new(FontSpacing.X + LetterSpacing / (scale.X * Font.Scale), FontSpacing.Y);
                    Vector2 position = new(lineX + run.X, blockY + line.Y);
                    // 基线偏移（sub/sup）：先融进 position，背景/文字/删除线/下划线全体跟随；
                    // em 基准=父字号（FontScale=layout DefaultFontScale），不随 span 自身缩小再缩
                    if (run.Span.BaselineShift != 0f) {
                        position.Y += run.Span.BaselineShift * Font.GlyphHeight * FontScale * Font.Scale;
                    }
                    // 文字像素对齐：FontBatch2D 对入批坐标的内部 Round 发生在局部空间，批末再叠加本树的
                    // GlobalTransform——平移带小数（父链累计边距/行高）时屏幕仍落半像素相位，同一字形随段落
                    // 忽锐忽糊、伪粗体双绘读作两个重叠的字。这里连同 QueueText 自身的半间距偏移一起对齐到
                    // 屏幕整数栅格，并传 DisableSnapToPixels 绕开内部 Round，每处文字的绘制相位恒为 0。
                    // 排版坐标不动（命中/省略号不受影响），quad 用同一 delta 跟随文字
                    Vector2 spacingHalf = new(
                        0.5f * scale.X * Font.Scale * (FontSpacing.X + Font.Spacing.X),
                        0.5f * scale.Y * Font.Scale * (FontSpacing.Y + Font.Spacing.Y)
                    );
                    Vector2 draw = position + spacingHalf;
                    Vector2 drawTranslate = new(GlobalTransform.M41, GlobalTransform.M42);
                    draw += new Vector2(
                        MathF.Round(draw.X + drawTranslate.X) - (draw.X + drawTranslate.X),
                        MathF.Round(draw.Y + drawTranslate.Y) - (draw.Y + drawTranslate.Y)
                    );
                    Vector2 drawDelta = draw - position;
                    TextAnchor anchor = TextAnchor.Default | TextAnchor.DisableSnapToPixels;
                    // 装饰矩形宽＝排版 run.Width：装饰 span 的排版盒宽已是逐字符圆整和（RichTextLayout.
                    // MeasureRunWidth perChar 路径），Draw 侧矢量 run 在 gt≠1 时同样按逐字符前缀 ×gt
                    // 定位（见下方矢量分支）——推进量/盒宽/字形落点三方同源，灰底与后续文字的间距
                    // 不再吃两套圆整差（行内代码两侧 margin 乱染名额实证：框→下一字比反向宽 14px）
                    if (flatBatch2D != null
                        && run.Span.BackgroundColor.A != 0) {
                        // 背景＝run 自身字号的 em 高矩形（同下划线基准）；fade<1 时随 run 淡出
                        Color backgroundColor = run.Span.BackgroundColor * GlobalColorTransform;
                        if (fade < 1f) {
                            backgroundColor *= fade;
                        }
                        if (backgroundColor.A != 0) {
                            QueueTransformedQuad(
                                flatBatch2D,
                                position.X + drawDelta.X,
                                position.Y + drawDelta.Y,
                                run.Width,
                                Font.GlyphHeight * scale.X * Font.Scale,
                                backgroundColor
                            );
                        }
                    }
                    // 行内边框（SpanBorder）：每行盒片段独立成框，横条贴行盒、竖条贴片段盒
                    //（片段盒=文字盒外扩左右边宽，run.X 为文字 x——排版层已按边宽占位推进）。
                    // Y 取行盒不随 BaselineShift（行内 border 不参与行盒高度的浏览器口径）
                    if (run.Span.Border is SpanBorder border
                        && flatBatch2D != null) {
                        float bl = MathF.Max(border.Widths.X, 0f);
                        float bt = MathF.Max(border.Widths.Y, 0f);
                        float br = MathF.Max(border.Widths.Z, 0f);
                        float bb = MathF.Max(border.Widths.W, 0f);
                        if (bl + bt + br + bb > 0f) {
                            float boxLeft = position.X + drawDelta.X - bl;
                            float boxTop = blockY + line.Y + drawDelta.Y;
                            float boxWidth = bl + run.Width + br;
                            float boxHeight = line.Height;
                            if (bt > 0f) {
                                QueueTransformedQuad(flatBatch2D, boxLeft, boxTop, boxWidth, bt, BorderColor(border.Top, run.Span, fade));
                            }
                            if (bb > 0f) {
                                QueueTransformedQuad(
                                    flatBatch2D,
                                    boxLeft,
                                    boxTop + boxHeight - bb,
                                    boxWidth,
                                    bb,
                                    BorderColor(border.Bottom, run.Span, fade)
                                );
                            }
                            if (bl > 0f) {
                                QueueTransformedQuad(flatBatch2D, boxLeft, boxTop, bl, boxHeight, BorderColor(border.Left, run.Span, fade));
                            }
                            if (br > 0f) {
                                QueueTransformedQuad(
                                    flatBatch2D,
                                    boxLeft + boxWidth - br,
                                    boxTop,
                                    br,
                                    boxHeight,
                                    BorderColor(border.Right, run.Span, fade)
                                );
                            }
                        }
                    }
                    // 矢量 run 分流（P4）：挂链且 run 首字符在链覆盖内→真字重/真斜体直出（降级在
                    // VectorFont.DrawText 内），画位与位图路径同用 draw（含屏幕整栅对齐），跳过伪粗双绘
                    // 与 ItalicShearMatrix；位图 run（未挂链或首字符缺字整 run 回退）走原路径不变。
                    // 首字符定态为 v1 简化，run 内混排缺字段由 DrawText 的位图回退段兜底。
                    // 删除线/下划线装饰不分流，两态都照画。
                    // 行内代码（span.Monospace）走等宽链（null 随正文链）
                    VectorFont runChain = run.Span.Monospace ? (MonoVectorFont ?? VectorFont) : VectorFont;
                    if (runChain != null
                        && run.Text.Length > 0
                        && runChain.HasGlyph(run.Text[0])) {
                        float vectorSize = runChain.DefaultSize * SpanScale(run.Span) * vectorGtScale;
                        Vector2 vectorDraw = draw * vectorGtScale;
                        // 矢量侧字距直绘：characterSpacing 在 FSS font 空间（经 sink Scale 除回 gt），
                        // ×vectorGtScale 还原排版侧的 widget px——与位图侧 drawSpacing 同一不变量
                        float vectorSpacing = LetterSpacing * vectorGtScale;
                        // 装饰 run 的逐字符定位（gt≠1）：FSS advance 逐栅格字号整数圆整，整串 DrawText
                        // 按 ×gt 字号的圆整推进，与排版盒宽（逐字符控件字号圆整和，MeasureRunWidth
                        // perChar 路径）差随 run 长度累积——灰底右缘 vs 后续文字间距不稳的根源。改为
                        // 按排版前缀推进量 ×gt 逐字 DrawText，字形落点与盒宽/后续 run 落点逐位同源；
                        // 字距已烘进前缀（characterSpacing=0）。仅全链覆盖 run 走此路（装饰 run=代码/
                        // 链接，混排缺字段罕见），混排/非装饰 run 保持整串路径（kerning 保留）
                        bool perCharDraw = vectorGtScale != 1f
                            && RichTextLayout.HasSpanDecoration(run.Span);
                        if (perCharDraw) {
                            foreach (char c in run.Text) {
                                if (!runChain.HasGlyph(c)) {
                                    perCharDraw = false;
                                    break;
                                }
                            }
                        }
                        if (perCharDraw) {
                            float fontScale = SpanScale(run.Span);
                            float x = 0f;
                            foreach (char c in run.Text) {
                                Vector2 charPos = vectorDraw + new Vector2(x * vectorGtScale, 0f);
                                if (DropShadow) {
                                    runChain.DrawText(
                                        vectorGlyphSink,
                                        c.ToString(),
                                        charPos + 1f * new Vector2(FontScale) * vectorGtScale,
                                        new Color((byte)0, (byte)0, (byte)0, color.A),
                                        vectorSize,
                                        run.Span.Bold,
                                        run.Span.Italic
                                    );
                                }
                                runChain.DrawText(vectorGlyphSink, c.ToString(), charPos, color, vectorSize, run.Span.Bold, run.Span.Italic);
                                x += runChain.MeasureString(c.ToString(), runChain.DefaultSize * fontScale, run.Span.Bold, run.Span.Italic).X
                                    + LetterSpacing;
                            }
                        }
                        else {
                            if (DropShadow) {
                                runChain.DrawText(
                                    vectorGlyphSink,
                                    run.Text,
                                    vectorDraw + 1f * new Vector2(FontScale) * vectorGtScale,
                                    new Color((byte)0, (byte)0, (byte)0, color.A),
                                    vectorSize,
                                    run.Span.Bold,
                                    run.Span.Italic,
                                    vectorSpacing
                                );
                            }
                            runChain.DrawText(vectorGlyphSink, run.Text, vectorDraw, color, vectorSize, run.Span.Bold, run.Span.Italic, vectorSpacing);
                        }
                    }
                    else {
                        // 斜体＝run 顶点本地切变（伪斜体），末尾 GlobalTransform 整批变换后套，测量/命中不受影响
                        bool italic = run.Span.Italic;
                        if (DropShadow) {
                            int shadowStart = fontBatch2D.TriangleVertices.Count;
                            fontBatch2D.QueueText(
                                run.Text,
                                draw + 1f * new Vector2(FontScale),
                                0f,
                                new Color((byte)0, (byte)0, (byte)0, color.A),
                                anchor,
                                scale,
                                drawSpacing
                            );
                            if (italic) {
                                fontBatch2D.TransformTriangles(ItalicShearMatrix(scale.X), shadowStart);
                            }
                        }
                        if (run.Span.Bold) {
                            // 伪粗体：同串横偏 1×FontScale 双绘
                            int boldStart = fontBatch2D.TriangleVertices.Count;
                            fontBatch2D.QueueText(
                                run.Text,
                                draw + new Vector2(FontScale, 0f),
                                0f,
                                color,
                                anchor,
                                scale,
                                drawSpacing
                            );
                            if (italic) {
                                fontBatch2D.TransformTriangles(ItalicShearMatrix(scale.X), boldStart);
                            }
                        }
                        int runStart = fontBatch2D.TriangleVertices.Count;
                        fontBatch2D.QueueText(
                            run.Text,
                            draw,
                            0f,
                            color,
                            anchor,
                            scale,
                            drawSpacing
                        );
                        if (italic) {
                            fontBatch2D.TransformTriangles(ItalicShearMatrix(scale.X), runStart);
                        }
                    }
                    if (run.Span.Strikethrough) {
                        // 删除线＝em 中线（基准同下划线：GlyphHeight×span 字号×Font.Scale），厚度同下划线；
                        // em 高常带半像素（51×0.5=25.5），连同全局平移一起取整，线才压满整行像素呈实色
                        float thickness = MathUtils.Max(FontScale * Font.Scale, 1f);
                        float strikeY = position.Y + Font.GlyphHeight * scale.X * Font.Scale * 0.5f - thickness * 0.5f;
                        strikeY = MathF.Round(strikeY + drawDelta.Y + GlobalTransform.M42) - drawDelta.Y - GlobalTransform.M42;
                        QueueTransformedQuad(overlayBatch2D, position.X + drawDelta.X, strikeY + drawDelta.Y, run.Width, thickness, color);
                    }
                    bool underline = !run.Span.LinkUnderlineSuppressed && (run.Span.Underline || (run.Span.IsLink && ShowLinkUnderline));
                    if (underline && flatBatch2D != null) {
                        // 下划线（HTML ins/链接）：字形下方 1px 处、厚度同删除线；GlyphHeight 须乘
                        // Font.Scale（与 CalculateLineHeight 一致），否则落在下一行；基线偏移已融
                        // position 自动跟随
                        float underlineY = position.Y + Font.GlyphHeight * scale.X * Font.Scale + 1f;
                        float thickness = MathUtils.Max(FontScale * Font.Scale, 1f);
                        QueueTransformedQuad(flatBatch2D, position.X + drawDelta.X, underlineY + drawDelta.Y, run.Width, thickness, color);
                    }
                }
            }
            fontBatch2D.TransformTriangles(GlobalTransform, batchStart);
            vectorGlyphSink?.TransformTriangles(GlobalTransform);
        }

        // 局部坐标 quad（x,y 为左上，w/h 尺寸）四角各自经 GlobalTransform 变换后入 FlatBatch（同 RectangleWidget 做法，
        // 只变换两对角点会在旋转/斜切变换下塌陷）
        void QueueTransformedQuad(FlatBatch2D batch, float x, float y, float width, float height, Color color) {
            Matrix m = GlobalTransform;
            Vector2 p1 = Vector2.Transform(new Vector2(x, y), m);
            Vector2 p2 = Vector2.Transform(new Vector2(x + width, y), m);
            Vector2 p3 = Vector2.Transform(new Vector2(x + width, y + height), m);
            Vector2 p4 = Vector2.Transform(new Vector2(x, y + height), m);
            batch.QueueQuad(p1, p2, p3, p4, 0f, color);
        }

        // 边框色解析：side null=currentColor（span 自身色回退 widget 色），×GlobalColorTransform 同其他
        // quad；fade<1 时叠乘 run 淡出系数（行内边框随 opacity 淡出——fade==1 路径色值逐位不变）
        Color BorderColor(Color? side, TextSpan span, float fade) {
            Color color = (side ?? span.Color ?? Color) * GlobalColorTransform;
            return fade < 1f ? color * fade : color;
        }

        // 斜体切变矩阵：x' = x − k·(y − em 中点)，k = tan(12°) ≈ 0.21，顶部右倾。
        // 绕 em 中点而非基线（Pericles 基线未单独暴露）；emHeight = GlyphHeight×span 字号×Font.Scale
        Matrix ItalicShearMatrix(float emHeight) {
            const float k = 0.2126f;
            Matrix m = Matrix.Identity;
            m.M21 = -k;
            m.M41 = k * emHeight * 0.5f;
            return m;
        }

        // 文字块顶部相对 widget 的 y 偏移（垂直锚点）；绘制与命中测试必须共用同一计算。
        // 总高按行距 pitch 累加（与 Layout 量测同式），line-height≠1 时垂直居中/沉底与盒一致
        float CalculateBlockY() {
            float totalHeight = 0f;
            foreach (TextLine line in m_layout.Lines) {
                totalHeight += m_layout.LineHeightPx > 0f ? m_layout.LineHeightPx : line.Height * m_layout.LineHeightMultiplier;
            }
            if ((TextAnchor & TextAnchor.VerticalCenter) != 0) {
                return (ActualSize.Y - totalHeight) / 2f;
            }
            if ((TextAnchor & TextAnchor.Bottom) != 0) {
                return ActualSize.Y - totalHeight;
            }
            return 0f;
        }

        bool HasStrikethrough {
            get {
                foreach (TextSpan span in Spans) {
                    if (span.Strikethrough) {
                        return true;
                    }
                }
                return false;
            }
        }

        bool HasUnderline {
            get {
                foreach (TextSpan span in Spans) {
                    if (span.Underline) {
                        return true;
                    }
                }
                return false;
            }
        }

        bool HasBackground {
            get {
                foreach (TextSpan span in Spans) {
                    if (span.BackgroundColor.A != 0) {
                        return true;
                    }
                }
                return false;
            }
        }

        bool HasBorder {
            get {
                foreach (TextSpan span in Spans) {
                    if (span.Border != null) {
                        return true;
                    }
                }
                return false;
            }
        }

        public override void MeasureOverride(Vector2 parentAvailableSize) {
            IsDrawRequired = HasContent && Color.A != 0;
            UpdateLayout(parentAvailableSize.X, parentAvailableSize.Y);
            if (!m_layoutSize.HasValue) {
                return;
            }
            DesiredSize = new Vector2(Size.X < 0f ? m_layoutSize.Value.X : Size.X, Size.Y < 0f ? m_layoutSize.Value.Y : Size.Y);
        }

        public float CalculateLineHeight() => (Font.GlyphHeight + Font.Spacing.Y + FontSpacing.Y) * FontScale * Font.Scale;

        void UpdateLayout(float availableWidth, float availableHeight) {
            availableWidth += 0.1f;
            if (!m_isDirty
                && m_layoutSize.HasValue
                && m_layoutAvailableWidth.HasValue
                && m_layoutAvailableWidth.Value == availableWidth
                && m_layoutAvailableHeight.HasValue
                && m_layoutAvailableHeight.Value == availableHeight) {
                return;
            }
            m_layout.Font = Font;
            m_layout.FontSpacing = FontSpacing;
            m_layout.VectorFont = VectorFont;
            m_layout.MonoVectorFont = MonoVectorFont;
            m_layout.DefaultFontScale = FontScale;
            m_layout.WordWrap = WordWrap;
            m_layout.WordBreak = WordBreak;
            m_layout.LineHeightMultiplier = LineHeightMultiplier;
            m_layout.LineHeightPx = LineHeightPx;
            m_layout.LetterSpacing = LetterSpacing;
            m_layout.MaxLines = MaxLines;
            m_layout.Ellipsis = Ellipsis;
            m_layout.Spans = m_spans;
            // 有效缩进解析（% 分母=本件排版可用宽）：超界宽（MaxValue 自测口径/∞）无基准只落 px，
            // 防 %×巨宽溢出成 ∞ 把首行预算钳到 1px
            float percentBasis = availableWidth > 0f && availableWidth < 1e7f ? availableWidth : 0f;
            m_resolvedFirstLineIndent = m_firstLineIndent + m_firstLineIndentPercent / 100f * percentBasis;
            m_layout.Layout(availableWidth, availableHeight, m_resolvedFirstLineIndent);
            m_layoutSize = m_layout.MeasuredSize;
            m_layoutAvailableWidth = availableWidth;
            m_layoutAvailableHeight = availableHeight;
            m_isDirty = false;
        }

        float SpanScale(TextSpan span) => span.FontScale > 0f ? span.FontScale : FontScale;

        // 命中测试：坐标为 widget 本地坐标；先按行 y 区间定位行（含垂直锚点偏移，与 Draw 一致），再按行内 run x 区间定位链接 run。
        // 行 y 区间按行内 sub/sup 的基线偏移外扩（Draw 侧字形整体平移出 line box，命中框不外扩则
        // sup 内链接上缘、sub 内链接下缘点不中）
        public TextRun FindLinkAtPoint(Vector2 point) {
            float blockY = CalculateBlockY();
            // 与 Draw 同源：首行 x 叠加有效缩进（命中区间跟随绘制位置，两处口径必须一致）
            TextLine firstLine = m_layout.Lines.Count > 0 ? m_layout.Lines[0] : null;
            foreach (TextLine line in m_layout.Lines) {
                float shiftUp = 0f;
                float shiftDown = 0f;
                foreach (TextRun run in line.Runs) {
                    float shift = run.Span.BaselineShift * Font.GlyphHeight * FontScale * Font.Scale;
                    if (shift > 0f) {
                        shiftDown = MathUtils.Max(shiftDown, shift);
                    }
                    else {
                        shiftUp = MathUtils.Max(shiftUp, -shift);
                    }
                }
                if (point.Y < blockY + line.Y - shiftUp
                    || point.Y >= blockY + line.Y + line.Height + shiftDown) {
                    continue;
                }
                float lineX = 0f;
                if ((TextAnchor & TextAnchor.HorizontalCenter) != 0) {
                    lineX = (ActualSize.X - line.Width) / 2f;
                }
                else if ((TextAnchor & TextAnchor.Right) != 0) {
                    lineX = ActualSize.X - line.Width;
                }
                if (line == firstLine) {
                    lineX += m_resolvedFirstLineIndent;
                }
                foreach (TextRun run in line.Runs) {
                    if (run.IsLink
                        && point.X >= lineX + run.X
                        && point.X < lineX + run.X + run.Width) {
                        return run;
                    }
                }
            }
            return null;
        }
    }
}