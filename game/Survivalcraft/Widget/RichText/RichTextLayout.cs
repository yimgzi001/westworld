using Engine;
using Engine.Media;

namespace Game {
// 富文本排版：spans 摊平为字符单元 → 贪心断行（拉丁词回扫 + CJK 禁则，复用 FontTextWidget 规则）→ 按 span 组装 run
// 字符推进量唯一出处=CharacterAdvance：挂链字符与矢量绘制同源实测，缺字回退位图预算
//（(glyph.Width - kerning + spacing) * scale，与 FontBatch2D.QueueLine 一致）
    public class RichTextLayout {
        public BitmapFont Font { get; set; }

        public Vector2 FontSpacing { get; set; }

        public List<TextSpan> Spans { get; set; } = [];

        public bool WordWrap { get; set; }

        /// <summary>断行模式：默认 BreakWord=词边界回退+无断点强拆（引擎历史行为）；Normal/KeepAll 无断点纯溢出；BreakAll=行尾即拆。</summary>
        public TextWordBreak WordBreak { get; set; } = TextWordBreak.BreakWord;

        public int MaxLines { get; set; } = int.MaxValue;

        public bool Ellipsis { get; set; }

        // line-height 语义（CSS）：倍率=number/em/%÷100（字号倍率同义）；绝对值=px/pt×4/3/rem×16
        // 归一 px。两者同时给时绝对值优先（px 声明不参与继承倍率）
        public float LineHeightMultiplier { get; set; } = 1f;

        // >0 时优先于乘数；0=无绝对声明（沿用倍率）
        public float LineHeightPx { get; set; }

        // CSS letter-spacing（字符间距，块级继承属性归一 px）：排版宽度唯一出处=CharacterAdvance
        // 单点 hook——AddLine run 宽与 CountFittingCells 折行判定同吃一个 advance，加字距后两处
        // 自动同口径。负值合法（CSS 允许负字距收紧）；默认 0 时 advance 公式 +0 浮点无变化，
        // 既有排版逐位不变
        public float LetterSpacing { get; set; }

        // 矢量链（RichTextWidget.UpdateLayout 接线）：字符 advance 出处（覆盖字符走矢量实测，
        // 缺字回退位图预算）；null=纯位图路径，既有排版逐位不变
        public VectorFont VectorFont { get; set; }

        // 等宽链（span.Monospace 的行内代码用）：null=随 VectorFont
        public VectorFont MonoVectorFont { get; set; }

        public float DefaultFontScale { get; set; } = 1f;

        public List<TextLine> Lines { get; } = [];

        public Vector2 MeasuredSize { get; private set; }

        readonly struct Cell(char c, TextSpan span, int sequence, bool isTabSpace = false) {
            public readonly char C = c;

            public readonly TextSpan Span = span;

            // 同一 span 的连续源字符共享同一 sequence，kerning 只在同 sequence 相邻字符间生效（与 QueueLine 的 run 内 kerning 对齐）
            public readonly int Sequence = sequence;

            // '\t' 展开出的空格：下一行行首去空白时保留，否则折行点上的 tab 缩进丢失
            public readonly bool IsTabSpace = isTabSpace;
        }

        public float CalculateLineHeight(float fontScale) => (Font.GlyphHeight + Font.Spacing.Y + FontSpacing.Y) * fontScale * Font.Scale;

        // firstLineIndent：CSS text-indent（块级首行缩进，px）。只吃整个 layout 的第一行
        //（段序列第一段的首行）折行预算；后续 '\n' 段与折行续行不缩进（CSS：<br> 不产生新块首行）。
        // 行 x 偏移由绘制/命中侧（RichTextWidget）叠加，本方法只管预算口径
        public void Layout(float availableWidth, float availableHeight, float firstLineIndent = 0f) {
            Lines.Clear();
            MeasuredSize = Vector2.Zero;
            if (Font == null
                || Spans == null
                || Spans.Count == 0) {
                return;
            }
            // 按 '\n' 拆段，段内贪心断行；'\t' 按字符列制展开为空格（每 4 列一个 stop）。
            // 列号按段内累计字符数计，word-wrap 折行后与显示行列号不对应——比例字体下像素对齐需等宽字体另配
            const int tabSize = 4;
            List<List<Cell>> paragraphs = [];
            List<Cell> current = [];
            int sequence = 0;
            foreach (TextSpan span in Spans) {
                string text = span.Text ?? string.Empty;
                foreach (char raw in text) {
                    if (raw == '\r'
                        || raw == '\u200b') {
                        continue;
                    }
                    if (raw == '\n') {
                        paragraphs.Add(current);
                        current = [];
                        continue;
                    }
                    if (raw == '\t') {
                        int spaces = tabSize - current.Count % tabSize;
                        for (int t = 0; t < spaces; t++) {
                            current.Add(new Cell(' ', span, sequence, true));
                        }
                        continue;
                    }
                    current.Add(new Cell(raw == '\u00a0' ? ' ' : raw, span, sequence));
                }
                sequence++;
            }
            paragraphs.Add(current);
            // 首行折行预算=可用宽−缩进（下限 1px 防负/零宽死循环）；可用宽无穷时无缩进口径
            bool hasIndent = firstLineIndent > 0f && float.IsFinite(availableWidth);
            float firstLineWidth = hasIndent ? MathF.Max(availableWidth - firstLineIndent, 1f) : availableWidth;
            bool firstLinePending = true;
            int maxLines = MaxLines;
            if (availableHeight < float.PositiveInfinity) {
                // 约束高→行数换算按行距 pitch（与量测同式）：自然高换算在 line-height>1 时
                // 超预算约一倍（Ellipsis 落错行、内容画出约束区）
                maxLines = MathUtils.Min(
                    (int)MathF.Floor(
                        availableHeight / (LineHeightPx > 0f ? LineHeightPx : CalculateLineHeight(DefaultFontScale) * LineHeightMultiplier)
                    ),
                    maxLines
                );
            }
            foreach (List<Cell> paragraph in paragraphs) {
                if (Lines.Count >= maxLines) {
                    break;
                }
                if (paragraph.Count == 0) {
                    AddLine([], false);
                    // 空段也占行盒：缩进口径按「整个 layout 第一行」计，首空行即耗掉缩进
                    firstLinePending = false;
                    continue;
                }
                int start = 0;
                while (start < paragraph.Count) {
                    int end = paragraph.Count;
                    bool lineEllipsized = false;
                    float lineBudget = firstLinePending ? firstLineWidth : availableWidth;
                    if (WordWrap) {
                        if (Ellipsis && Lines.Count + 1 >= maxLines) {
                            // 末行直接按减省略号宽适配剩余内容，放不下才截断补 "..."（与 FontTextWidget flag 路径一致）：
                            // 若先按全宽断行再收缩，断行点空隙≥省略号宽时会静默丢内容且无省略号
                            int fitCount = CountFittingCells(paragraph, start, end, lineBudget, true);
                            if (fitCount < end - start) {
                                end = start + fitCount;
                                lineEllipsized = true;
                            }
                        }
                        else {
                            int fitCount = CountFittingCells(paragraph, start, end, lineBudget, false);
                            if (fitCount < end - start) {
                                int breakPosition = FontTextWidget.FindLineBreakPosition(
                                    BuildString(paragraph, start, end),
                                    MathUtils.Max(fitCount, 1),
                                    WordBreak
                                );
                                if (breakPosition <= 0) {
                                    breakPosition = 1;
                                }
                                end = start + breakPosition;
                            }
                        }
                    }
                    else if (Ellipsis) {
                        // 非换行：段落超宽即截断补 "..."（与 FontTextWidget 非换行分支一致）
                        int fitCount = CountFittingCells(paragraph, start, end, lineBudget, true);
                        if (fitCount < end - start) {
                            end = start + fitCount;
                            lineEllipsized = true;
                        }
                    }
                    // 行尾去空白、下一行首去空白（与 FontTextWidget.UpdateLines 一致；tab 展开空格行首保留）
                    while (end > start
                        && char.IsWhiteSpace(paragraph[end - 1].C)) {
                        end--;
                    }
                    int nextStart = MathUtils.Min(end == start ? start + 1 : end, paragraph.Count);
                    while (nextStart < paragraph.Count
                        && char.IsWhiteSpace(paragraph[nextStart].C)
                        && !paragraph[nextStart].IsTabSpace) {
                        nextStart++;
                    }
                    AddLine(paragraph.GetRange(start, end - start), lineEllipsized);
                    firstLinePending = false;
                    if (lineEllipsized) {
                        break;
                    }
                    start = nextStart;
                    if (Lines.Count >= maxLines) {
                        break;
                    }
                }
            }
            // 累计行 y 与测量尺寸；测量时末行不含行尾间距（与 FontTextWidget 一致）。
            // line-height 行盒=纯推进距（pitch）：不居中字形（无 half-leading），行高>1 的富余
            // 全落字下方——留档取舍（实现最简、下行不压上行，代价是视觉重心偏上）。
            // pitch=绝对值优先，否则行盒高×倍率；末行尾距修剪同式改按末行 pitch 计
            float y = 0f;
            float maxWidth = 0f;
            float lastPitch = 0f;
            for (int i = 0; i < Lines.Count; i++) {
                TextLine line = Lines[i];
                line.Y = y;
                float pitch = LineHeightPx > 0f ? LineHeightPx : line.Height * LineHeightMultiplier;
                y += pitch;
                lastPitch = pitch;
                maxWidth = MathUtils.Max(maxWidth, line.Width);
            }
            if (Lines.Count > 0) {
                // 修剪目标=末行 pitch−字形项（GlyphHeight×末行字号×Font.Scale 与原式同项），
                // 即剔除纯行间距；pitch<字形项（行高<1）时差值为负=末行向上收（CSS 同义）
                y -= lastPitch - Font.GlyphHeight * Lines[^1].MaxScale * Font.Scale;
            }
            if (Lines.Count > 0 && hasIndent) {
                // 首行缩进计入测量宽（首行实际宽+缩进）：收缩/裁剪宿主下 DesiredSize.X 不再少报，
                // 首行不越出宿主盒；空首段耗缩进情形首行宽 0，计入的即缩进本身（行盒占位同义）
                maxWidth = MathUtils.Max(maxWidth, Lines[0].Width + firstLineIndent);
            }
            MeasuredSize = new Vector2(maxWidth, y);
        }

        // 组装一行：同 sequence 连续 cell 归并为一个 run，kerning 记在相邻对的前一个字符上（与 QueueLine 相同）。
        // 行内边框（SpanBorder）按片段占位：每个 run 左右各加边宽（run.X=文字 x，推进含右宽），行盒高度不撑
        //（浏览器行内 border 口径：水平进排版、垂直可与相邻行重叠）
        void AddLine(List<Cell> cells, bool lineEllipsized) {
            TextLine line = new();
            float maxScale = DefaultFontScale;
            float x = 0f;
            int i = 0;
            while (i < cells.Count) {
                TextSpan span = cells[i].Span;
                float scale = SpanScale(span);
                maxScale = MathUtils.Max(maxScale, scale);
                int j = i;
                while (j + 1 < cells.Count
                    && cells[j + 1].Sequence == cells[i].Sequence) {
                    j++;
                }
                char[] text = new char[j - i + 1];
                for (int k = i; k <= j; k++) {
                    text[k - i] = cells[k].C;
                }
                float width = MeasureRunWidth(new string(text), span, scale);
                SpanBorder border = span.Border;
                if (border != null) {
                    x += MathF.Max(border.Widths.X, 0f);
                }
                TextRun run = new() { Span = span, Text = new string(text), X = x, Width = width };
                x += width;
                if (border != null) {
                    x += MathF.Max(border.Widths.Z, 0f);
                }
                line.Runs.Add(run);
                i = j + 1;
            }
            line.Width = x;
            line.MaxScale = maxScale;
            line.Height = CalculateLineHeight(maxScale);
            line.IsEllipsized = lineEllipsized;
            if (lineEllipsized) {
                // 省略号继承截断点处 span 的视觉样式，但不是链接
                TextSpan source = cells.Count > 0 ? cells[^1].Span : null;
                float sourceScale = source != null ? SpanScale(source) : DefaultFontScale;
                TextSpan ellipsis = new("...") {
                    Color = source?.Color,
                    BackgroundColor = source != null ? source.BackgroundColor : Color.Transparent,
                    FontScale = sourceScale,
                    Bold = source?.Bold ?? false,
                    Italic = source?.Italic ?? false,
                    Underline = source?.Underline ?? false,
                    BaselineShift = source?.BaselineShift ?? 0f,
                    Opacity = source?.Opacity ?? 1f
                };
                TextRun run = new() { Span = ellipsis, Text = "...", X = x, Width = MeasureEllipsisWidth(sourceScale) };
                line.Runs.Add(run);
                line.Width += run.Width;
            }
            Lines.Add(line);
        }

        // 返回 [start, end) 内总宽不超过 availableWidth 的最大字符数；超宽单字符由调用方强制推进。
        // reserveEllipsis：预留省略号宽度（按当前 cell 的 span 字号度量，与截断后实际追加的省略号字号一致）。
        // 行内边框占位与 AddLine 同模型：每个 sequence 片段的左右边宽计入（累加于片段首 cell）
        int CountFittingCells(List<Cell> cells, int start, int end, float availableWidth, bool reserveEllipsis) {
            float width = 0f;
            float borderWidths = 0f;
            for (int i = start; i < end; i++) {
                if (i == start
                    || cells[i].Sequence != cells[i - 1].Sequence) {
                    SpanBorder border = cells[i].Span.Border;
                    if (border != null) {
                        borderWidths += MathF.Max(border.Widths.X, 0f) + MathF.Max(border.Widths.Z, 0f);
                    }
                }
                float scale = SpanScale(cells[i].Span);
                width += CharacterAdvance(cells[i].C, scale, cells[i].Span);
                if (i + 1 < end
                    && cells[i + 1].Sequence == cells[i].Sequence) {
                    // 位图 kerning 减项只服务双位图字符对：挂链字符 advance 已走矢量实测（FSS 无
                    // 逐对公开 kerning API），减位图 kerning 是两个独立来源混算——矢量对不扣
                    // （kerning 常见为负，预算略松=折行保守），与 MeasureRunWidth 分段盒同口径
                    TextSpan spanI = cells[i].Span;
                    TextSpan spanJ = cells[i + 1].Span;
                    VectorFont chainI = spanI.Monospace ? (MonoVectorFont ?? VectorFont) : VectorFont;
                    VectorFont chainJ = spanJ.Monospace ? (MonoVectorFont ?? VectorFont) : VectorFont;
                    if ((chainI == null || !chainI.HasGlyph(cells[i].C))
                        && (chainJ == null || !chainJ.HasGlyph(cells[i + 1].C))) {
                        width -= Font.GetKerning(cells[i].C, cells[i + 1].C) * scale * Font.Scale;
                    }
                }
                float limit = reserveEllipsis ? availableWidth - MeasureEllipsisWidth(scale) : availableWidth;
                if (width + borderWidths > limit) {
                    return i - start;
                }
            }
            return end - start;
        }

        float SpanScale(TextSpan span) => span.FontScale > 0f ? span.FontScale : DefaultFontScale;

        // run 盒宽：与 Draw 的 run 级分流同口径——按字符 HasGlyph 切段，镜像 VectorFont.DrawText
        // 内部的矢量/位图分段状态机。矢量段默认整串量测：FSS 推进含字符对 kerning（跨链源恒 0、
        // 无逐对公开 API），逐字符和会漏 kerning 使 run 间累积漂移（实测 +5~6px/行）；整串量测
        // 与 DrawText 同函数同值。位图段逐字符位图预算：FSS 度量对链内缺字码点直接跳过 X 贡献
        // （prevGlyph 不复位），整串量测会静默丢缺字字符宽——run 首字挂链+后续含链外字符（emoji/
        // PUA/生僻符号）时盒窄于实画、后续 run 压上来。跨段 kerning 两端都不计（DrawText 各段
        // 独立排布）。字距按段字符数线性并入（每字符后一个，与 CountFittingCells 逐字符口径同 n）；
        // Draw 侧字距经 characterSpacing 直绘同一间距（FSS 段字符间 n−1+段尾补 1）
        // 带装饰 span（背景/边框/删除线/下划线/链接）矢量段改逐字符量测：FSS advance 逐栅格字号
        // 整数圆整，整串按控件字号量测 vs 绘制侧 ×gt 栅格 ÷gt 两套圆整，差随 run 长度线性累积
        // （行内代码灰底"尾 padding"实证）——装饰 run 的 Draw 侧改按逐字符前缀 ×gt 定位（见
        // RichTextWidget.Draw），推进量必须与绘制定位同源：逐字符圆整和。代价=装饰 run 丢段内
        // kerning（行内代码/链接 URL 无 kerning 对，视觉无差）；非装饰 run 保持整串+kerning 不变
        public float MeasureRunWidth(string text, TextSpan span, float fontScale) {
            VectorFont chain = span.Monospace ? (MonoVectorFont ?? VectorFont) : VectorFont;
            bool perChar = chain != null && HasSpanDecoration(span);
            float width = 0f;
            int start = 0;
            bool segVector = chain != null
                && text.Length > 0
                && chain.HasGlyph(text[0]);
            for (int i = 1; i <= text.Length; i++) {
                bool segIsVector = i < text.Length
                    && chain != null
                    && chain.HasGlyph(text[i]);
                if (i < text.Length
                    && segIsVector == segVector) {
                    continue;
                }
                if (segVector) {
                    if (perChar) {
                        for (int k = start; k < i; k++) {
                            width += chain.MeasureString(text[k].ToString(), chain.DefaultSize * fontScale, span.Bold, span.Italic).X
                                + LetterSpacing;
                        }
                    }
                    else {
                        width += chain.MeasureString(text[start..i], chain.DefaultSize * fontScale, span.Bold, span.Italic).X
                            + LetterSpacing * (i - start);
                    }
                }
                else {
                    for (int k = start; k < i; k++) {
                        width += CharacterAdvance(text[k], fontScale, span);
                        if (k + 1 < i) {
                            width -= Font.GetKerning(text[k], text[k + 1]) * fontScale * Font.Scale;
                        }
                    }
                }
                start = i;
                segVector = segIsVector;
            }
            return width;
        }

        // 装饰判定（排版/绘制两端同谓词）：命中即装饰 run——矢量段逐字符量测（排版）、
        // gt≠1 时逐字符前缀 ×gt 定位（绘制），盒宽与字形落点逐位同源
        public static bool HasSpanDecoration(TextSpan span) {
            return span.BackgroundColor.A != 0
                || span.Border != null
                || span.Strikethrough
                || span.Underline
                || span.IsLink;
        }

        // 字符推进量唯一出处（折行判定 CountFittingCells 用）：字距是绝对 px，
        // 不随 span 字号/Font.Scale 缩放（CSS px 口径），kerning 减项保持原样（字距与 kerning 并存）。
        // 挂链时覆盖字符走矢量实测 advance，与 Draw 矢量直出同源——位图预算 vs 矢量字形有 ~5-7%
        // 系统性差（实测 CJK 20→21、拉丁 15→16），逐 run 累积即行尾溢出、run 间重叠；缺字字符
        // 随 Draw 的位图回退段走位图预算（粗体×GetBoldAdvanceScale 预算仅此路径需要——矢量实测
        // advance 已含真面加宽，不再乘）
        float CharacterAdvance(char c, float fontScale, TextSpan span) {
            VectorFont chain = span.Monospace ? (MonoVectorFont ?? VectorFont) : VectorFont;
            if (chain != null
                && chain.HasGlyph(c)) {
                return chain.MeasureString(c.ToString(), chain.DefaultSize * fontScale, span.Bold, span.Italic).X + LetterSpacing;
            }
            float advance = (Font.GetGlyph(c).Width + Font.Spacing.X + FontSpacing.X) * fontScale * Font.Scale + LetterSpacing;
            return span.Bold && chain != null ? advance * chain.GetBoldAdvanceScale(c) : advance;
        }

        float MeasureEllipsisWidth(float fontScale) {
            const string text = "...";
            if (VectorFont != null) {
                return VectorFont.MeasureString(text, VectorFont.DefaultSize * fontScale, false, false).X + text.Length * LetterSpacing;
            }
            return Font.MeasureText(text, 0, text.Length, new Vector2(fontScale), FontSpacing).X;
        }

        static string BuildString(List<Cell> cells, int start, int end) {
            char[] chars = new char[end - start];
            for (int i = start; i < end; i++) {
                chars[i - start] = cells[i].C;
            }
            return new string(chars);
        }
    }
}