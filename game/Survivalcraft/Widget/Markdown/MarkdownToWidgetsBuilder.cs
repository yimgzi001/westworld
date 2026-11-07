using System.Text;
using Engine;
using Markdig;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MdBlock = Markdig.Syntax.Block;

namespace Game {
    // Markdig 块级 AST → widget 树。行内内容一律走 MarkdownSpanParser；
    // HTML 块委托 HtmlToWidgetsBuilder（Widget/Html/），块注册表合并回本 builder。
    // 配置字段/块级常量/widget 原语在基类 RichContentBuilder（与 HTML 前端共用）。
    // 布局事实：CanvasWidget 是唯一会把有限 Size 约束进 child 测量输入的容器（并按 child 位置扣减可用宽）；
    // Rectangle/BevelledRectangle 的 MeasureOverride 直接返回 Size（-1 表示排列时撑满父 cell）；
    // ScrollPanelWidget 不设 DesiredSize，故其外层必须显式定高
    public class MarkdownToWidgetsBuilder : RichContentBuilder {
        // Strikethrough 扩展：默认 pipeline 不解析 ~~x~~（会留在字面量）
        // PipeTables：默认 pipeline 不解析 GFM 管道表（| a | b |）
        // TaskLists：默认 pipeline 不解析 GFM 任务项（- [ ] / - [x]）
        static readonly MarkdownPipeline s_pipeline = new MarkdownPipelineBuilder().UseEmphasisExtras(EmphasisExtraOptions.Strikethrough)
            .UsePipeTables()
            .UseTaskLists()
            .Build();

        public StackPanelWidget Build(string markdown) {
            MarkdownDocument document = Markdown.Parse(markdown ?? string.Empty, s_pipeline);
            StackPanelWidget panel = CreatePanel();
            BuildBlocks(document, panel, 0, null, 0);
            return panel;
        }

        public void BuildBlocks(IEnumerable<MdBlock> blocks, StackPanelWidget panel, int listDepth, Color? colorOverride) {
            BuildBlocks(blocks, panel, listDepth, colorOverride, 0);
        }

        void BuildBlocks(IEnumerable<MdBlock> blocks, StackPanelWidget panel, int listDepth, Color? colorOverride, int depth) {
            if (depth > MaxDepth) {
                return;
            }
            foreach (MdBlock block in blocks) {
                BuildBlock(block, panel, listDepth, colorOverride, depth);
            }
        }

        void BuildBlock(MdBlock block, StackPanelWidget panel, int listDepth, Color? colorOverride, int depth) {
            switch (block) {
                case HeadingBlock heading: {
                    RichTextWidget widget = CreateHeading(heading.Level, colorOverride);
                    if (heading.Inline != null) {
                        MarkdownSpanParser.Parse(heading.Inline, widget.Spans, default, widget.Color);
                    }
                    panel.Children.Add(widget);
                    break;
                }
                case ParagraphBlock paragraph: {
                    // 纯图段落 → 图片块（异步加载管线）；混排段维持占位文本（行内真嵌图范围外）
                    if (ImageLoader != null
                        && TryGetParagraphImages(paragraph.Inline, out List<LinkInline> images)) {
                        foreach (LinkInline image in images) {
                            panel.Children.Add(AddImageBlock(panel, image.Url, MarkdownSpanParser.GetInlineText(image)));
                        }
                        break;
                    }
                    RichTextWidget widget = CreateRichText(colorOverride);
                    if (paragraph.Inline != null) {
                        MarkdownSpanParser.Parse(paragraph.Inline, widget.Spans, default, widget.Color);
                    }
                    panel.Children.Add(widget);
                    break;
                }
                case ListBlock list: BuildList(list, panel, listDepth, colorOverride, depth); break;
                case QuoteBlock quote: panel.Children.Add(CreateQuote(quote, listDepth, colorOverride, depth)); break;
                case FencedCodeBlock fencedCode:
                    panel.Children.Add(CreateCodeBlock(GetBlockLines(fencedCode.Lines.Lines, fencedCode.Lines.Count))); break;
                case CodeBlock indentedCode:
                    panel.Children.Add(CreateCodeBlock(GetBlockLines(indentedCode.Lines.Lines, indentedCode.Lines.Count))); break;
                case ThematicBreakBlock: panel.Children.Add(CreateThematicBreak()); break;
                case Table table: panel.Children.Add(CreateTable(table, colorOverride)); break;
                case HtmlBlock htmlBlock: BuildHtmlBlock(GetBlockText(htmlBlock), panel, listDepth, colorOverride, depth + 1); break;
                default: {
                    // 未知块（自定义扩展等）：取源文本按段落兜底，避免内容静默丢失
                    string text = GetBlockText(block);
                    if (!string.IsNullOrWhiteSpace(text)) {
                        RichTextWidget widget = CreateRichText(colorOverride);
                        widget.Spans.Add(new TextSpan(text.TrimEnd()));
                        panel.Children.Add(widget);
                    }
                    break;
                }
            }
        }

        // HtmlBlock：委托 HtmlToWidgetsBuilder 平铺渲染，块注册表合并回本 builder
        //（代码块/图片块宽度未定时仍由宿主 MarkdownWidget 测量期统一回填）
        void BuildHtmlBlock(string html, StackPanelWidget panel, int listDepth, Color? colorOverride, int depth) {
            HtmlToWidgetsBuilder htmlBuilder = new() {
                Font = Font,
                FontScale = FontScale,
                DefaultColor = DefaultColor,
                ContentWidth = ContentWidth,
                LinkClicked = LinkClicked,
                ImageLoader = ImageLoader,
                CurrentIndentX = CurrentIndentX
            };
            htmlBuilder.BuildHtmlInto(html, panel, listDepth, colorOverride, depth);
            CodeBlocks.AddRange(htmlBuilder.CodeBlocks);
            ImageBlocks.AddRange(htmlBuilder.ImageBlocks);
        }

        void BuildList(ListBlock list, StackPanelWidget panel, int listDepth, Color? colorOverride, int depth) {
            StackPanelWidget listPanel = CreatePanel();
            listPanel.Margin = new Vector2(0f, 2f);
            // 有序列表起始编号：Markdig 只给 OrderedStart 原始串（"3." 的 "3"），自己转 int（失败回退 1）
            int number = list.IsOrdered && int.TryParse(list.OrderedStart, out int orderedStart) ? orderedStart : 1;
            // 文字列按全列表最宽 marker 对齐（"10." 比 "1." 宽），保底 marker→文字间隙；
            // 嵌套列表挂在本级文字列内，缩进节奏恒一档 ListIndentPerDepth
            float markerScale = FontScale;
            float maxMarkerWidth = 0f;
            int scanNumber = number;
            foreach (ListItemBlock item in list) {
                string marker = HtmlTextUtils.GetListMarker(list.IsOrdered, scanNumber, listDepth + 1);
                maxMarkerWidth = MathUtils.Max(maxMarkerWidth, Font.MeasureText(marker, new Vector2(markerScale), Vector2.Zero).X);
                if (list.IsOrdered) {
                    scanNumber++;
                }
            }
            float contentX = MathUtils.Max(ListIndentPerDepth, MarkerOffsetX + maxMarkerWidth + MarkerTextGap);
            foreach (ListItemBlock item in list) {
                listPanel.Children.Add(
                    CreateListItem(
                        list.IsOrdered,
                        number,
                        listDepth,
                        item,
                        colorOverride,
                        true,
                        depth,
                        contentX
                    )
                );
                number++;
            }
            panel.Children.Add(listPanel);
        }

        // 项 = CanvasWidget 行：marker 列 + 内容列。CanvasWidget 会按 child 位置自动扣减测量可用宽，
        // 内容列换行宽 = 行宽 - 缩进；行内内容与嵌套块共用一个竖排 StackPanel。
        // 嵌套块/子列表挂在本级文字列内，因此行内坐标全部相对挂载点（marker 4 / 文字列 contentX），
        // 缩进随嵌套层级自然累加一档，不再按 listDepth 绝对档重复累计
        CanvasWidget CreateListItem(bool ordered,
            int number,
            int listDepth,
            IEnumerable<MdBlock> markdigBlocks,
            Color? colorOverride,
            bool markdigItem,
            int depth,
            float contentX) {
            // 任务项（- [ ] / - [x]）：marker 列换成 CheckboxWidget（GFM 任务项无 •/编号）
            bool isTaskItem = false;
            bool taskChecked = false;
            ParagraphBlock taskParagraph = null;
            if (markdigItem) {
                foreach (MdBlock itemBlock in markdigBlocks) {
                    if (itemBlock is ParagraphBlock firstParagraph) {
                        isTaskItem = TryGetTaskList(firstParagraph, out taskChecked);
                        taskParagraph = firstParagraph;
                        break;
                    }
                }
            }
            // 任务项 checkbox（16 宽）占满 marker 列：文字列保底 checkbox 右缘 + 间隙，
            // 否则 checkbox 与文字只剩 ~2px（marker 列宽 22 是按 •/数字定的）
            if (isTaskItem) {
                contentX = MathUtils.Max(contentX, MarkerOffsetX + TaskCheckboxSize + MarkerTextGap);
            }
            // 行框宽随嵌套收窄（构建时 CurrentIndentX=父累计，未含本档 contentX——文字列挂载点
            // 扣减的 contentX 即本档缩进），li 套 li 时行框/文字列不溢出宿主右缘
            CanvasWidget row = new() { Size = new Vector2(NestedContentWidth(), -1f) };
            if (!isTaskItem) {
                FontTextWidget markerLabel = new() {
                    Text = HtmlTextUtils.GetListMarker(ordered, number, listDepth + 1),
                    Font = Font,
                    Color = colorOverride ?? DefaultColor,
                    FontScale = FontScale
                };
                row.Children.Add(markerLabel);
                CanvasWidget.SetPosition(markerLabel, new Vector2(MarkerOffsetX, MarkerOffsetY));
            }
            StackPanelWidget contentPanel = CreatePanel();
            row.Children.Add(contentPanel);
            CanvasWidget.SetPosition(contentPanel, new Vector2(contentX, 0f));
            // 嵌套上下文：项内代码块/图片块宽度按文字列收窄。contentX 是相对本级行挂载点的
            // 局部列偏移，绝对缩进=父累计+本级（引用内列表、li 套 li 时靠累加保持口径）
            float prevIndent = CurrentIndentX;
            CurrentIndentX += contentX;
            bool hasBlocks = false;
            if (markdigItem) {
                foreach (MdBlock itemBlock in markdigBlocks) {
                    // 列表项内的段落与其标记同行
                    if (itemBlock is ParagraphBlock paragraphBlock) {
                        RichTextWidget widget = CreateRichText(colorOverride);
                        if (paragraphBlock.Inline != null) {
                            MarkdownSpanParser.Parse(paragraphBlock.Inline, widget.Spans, default, widget.Color);
                            // 任务项：段落首个 inline 是 TaskList（LeafInline，上面的未知叶 inline 规则丢弃它不渲染，
                            // 余下文本带一个前导空格正好作 checkbox 与文字的间隙）
                            if (isTaskItem && paragraphBlock == taskParagraph) {
                                // 任务标记后的前导空格只该当 checkbox 与首行文字的间隙——留在 run 里会让首行
                                // 字形比内容列缩进一个空格宽，续行块对不齐首行。首行去掉它，间隙由 checkbox
                                // 尺寸（16，内容列前留 2px）与位置保证，续行/嵌套块天然与首行文字对齐
                                TextSpan firstSpan = widget.Spans.FirstOrDefault(s => s.Text.Length > 0);
                                if (firstSpan?.Text[0] == ' ') {
                                    firstSpan.Text = firstSpan.Text[1..];
                                }
                                CheckboxWidget checkbox = CreateCheckbox(taskChecked, colorOverride);
                                row.Children.Add(checkbox);
                                CanvasWidget.SetPosition(checkbox, new Vector2(MarkerOffsetX, 5f));
                            }
                        }
                        contentPanel.Children.Add(widget);
                    }
                    else {
                        BuildBlock(itemBlock, contentPanel, listDepth + 1, colorOverride, depth + 1);
                    }
                    hasBlocks = true;
                }
            }
            if (!hasBlocks) {
                contentPanel.Children.Add(CreateRichText(colorOverride));
            }
            CurrentIndentX = prevIndent;
            return row;
        }

        // 任务项探测：GFM 任务标记是项内首段落的第一个 inline（- [ ] 未完成 / - [x] 已完成）
        static bool TryGetTaskList(ParagraphBlock paragraph, out bool isChecked) {
            if (paragraph.Inline?.FirstChild is TaskList taskList) {
                isChecked = taskList.Checked;
                return true;
            }
            isChecked = false;
            return false;
        }

        // 纯图段落判定：inline 序列全部是图片（允许图片间空白字面量，`![](a) ![](b)` 也算）。
        // 链接包图 [![img](i)](href) 外层非 image link，不算
        static bool TryGetParagraphImages(ContainerInline inline, out List<LinkInline> images) {
            images = null;
            bool any = false;
            if (inline == null) {
                return false;
            }
            foreach (Inline child in inline) {
                if (child is LinkInline link
                    && link.IsImage) {
                    (images ??= []).Add(link);
                    any = true;
                }
                else if (!(child is LiteralInline literal && string.IsNullOrWhiteSpace(literal.Content.ToString()))) {
                    return false;
                }
            }
            return any;
        }

        // CheckboxWidget 纯展示（v1 定稿裁定）：不自动勾选、关命中测试防挡滚动拖动；
        // 尺寸 16 对齐正文行高（XML 默认 32 偏大，18 会顶到内容列），tick 贴图来自全局图集
        CheckboxWidget CreateCheckbox(bool isChecked, Color? colorOverride) => new() {
            IsChecked = isChecked,
            IsAutoCheckingEnabled = false,
            IsHitTestVisible = false,
            CheckboxSize = new Vector2(TaskCheckboxSize, TaskCheckboxSize),
            Color = colorOverride ?? DefaultColor
        };

        // 引用块：左竖线（-1 高度随内容拉伸）+ 灰字 + 左缩进
        CanvasWidget CreateQuote(ContainerBlock quote, int listDepth, Color? colorOverride, int depth) {
            CanvasWidget outer = CreateQuoteFrame(out StackPanelWidget inner);
            float prevIndent = CurrentIndentX;
            CurrentIndentX += 10f;
            BuildBlocks(quote, inner, listDepth, QuoteColor, depth + 1);
            CurrentIndentX = prevIndent;
            return outer;
        }

        // 表格：Table→TableRow(IsHeader)→TableCell（1.4.0 无 Inline 属性，行内挂在 cell 内 ParagraphBlock.Inline 上）；
        // 列对齐在 Table.ColumnDefinitions[i].Alignment；表头行染粗体 span（跟随换行）
        MarkdownTableWidget CreateTable(Table table, Color? colorOverride) {
            MarkdownTableWidget widget = new() { Font = Font, FontScale = FontScale, DefaultColor = colorOverride ?? DefaultColor };
            if (LinkClicked != null) {
                widget.LinkClicked += href => LinkClicked(href);
            }
            List<MarkdownTableAlignment?> alignments = new();
            foreach (TableColumnDefinition definition in table.ColumnDefinitions) {
                alignments.Add(
                    definition.Alignment switch {
                        TableColumnAlign.Center => MarkdownTableAlignment.Center,
                        TableColumnAlign.Right => MarkdownTableAlignment.Right,
                        TableColumnAlign.Left => MarkdownTableAlignment.Left,
                        _ => null
                    }
                );
            }
            widget.SetColumnAlignments(alignments);
            foreach (MdBlock child in table) {
                if (child is not TableRow row) {
                    continue;
                }
                bool isHeader = row.IsHeader;
                List<List<TextSpan>> cells = [];
                foreach (MdBlock rowChild in row) {
                    if (rowChild is not TableCell cell) {
                        continue;
                    }
                    List<TextSpan> spans = [];
                    foreach (MdBlock cellChild in cell) {
                        if (cellChild is ParagraphBlock paragraph
                            && paragraph.Inline != null) {
                            MarkdownSpanParser.Parse(paragraph.Inline, spans, default, widget.DefaultColor);
                        }
                    }
                    if (isHeader) {
                        BoldCellSpans(spans);
                    }
                    cells.Add(spans);
                }
                widget.AddRow(isHeader, cells);
            }
            return widget;
        }

        static string[] GetBlockLines(Markdig.Helpers.StringLine[] lines, int count) {
            string[] result = new string[count];
            for (int i = 0; i < count; i++) {
                result[i] = lines[i].ToString();
            }
            return result;
        }

        static string GetBlockText(MdBlock block) => GetBlockText(block, 0);

        static string GetBlockText(MdBlock block, int depth) {
            if (depth > MaxDepth) {
                return string.Empty;
            }
            if (block is LeafBlock leafBlock) {
                return string.Join("\n", GetBlockLines(leafBlock.Lines.Lines, leafBlock.Lines.Count));
            }
            StringBuilder builder = new();
            if (block is ContainerBlock container) {
                foreach (MdBlock child in container) {
                    builder.AppendLine(GetBlockText(child, depth + 1));
                }
            }
            return builder.ToString();
        }
    }
}