using Engine;
using Engine.Media;

namespace Game {
    // 富内容构建器基类：Markdown（MarkdownToWidgetsBuilder）与 HTML（HtmlToWidgetsBuilder）
    // 两个前端共用的配置字段、块级视觉常量与 widget 原语（富文本段/代码块/图片块/面板）。
    // 块注册表 CodeBlocks/ImageBlocks 供宿主 widget（MarkdownWidget 等）在测量期回填宽度——
    // 转发方（Markdown 遇 HtmlBlock）负责把子 builder 的注册表合并回自己
    public abstract class RichContentBuilder {
        public BitmapFont Font;

        public float FontScale = 1f;

        public Color DefaultColor = Color.White;

        // <0 跟随父可用宽（宿主 widget MeasureOverride 解析代码块宽度）
        public float ContentWidth = -1f;

        // 非 event：允许宿主整体赋值/解绑
        public Action<string> LinkClicked;

        // 代码块外层 Canvas，宽度需在宿主 MeasureOverride 按可用宽解析（构建时父宽未知）。
        // 元组带构建时的嵌套内容列缩进（CurrentIndentX），回填时从宿主宽中扣减——
        // 否则列表项/引用块内的代码块按全宽构建，右缘溢出面板
        public readonly List<(CanvasWidget Frame, float IndentX)> CodeBlocks = [];

        // 宽度未定时（ContentWidth<=0）需在测量期回填宽度的图片块 frame（同 CodeBlocks 机制；
        // 已加载完成的 frame 由 loader.IsFrameApplied 排除，宽=图宽不可覆盖）
        public readonly List<(CanvasWidget Frame, float IndentX)> ImageBlocks = [];

        // 图片异步加载管线；null = 图片保持占位文本（宿主不接图片管线的场景）。
        // 类型 internal：由宿主 widget 注入并持有纹理生命周期，直接使用本 builder 的外部代码保持占位行为
        internal RichContentImageLoader ImageLoader;

        // 当前嵌套内容列的横向缩进（列表项文字列、引用块竖线右移等）。
        // 进入嵌套上下文前保存、离开时恢复（栈式纪律归调用方）；代码块/图片块宽度据此扣减。
        // internal：Markdown 前端把 HtmlBlock 委托给 HtmlToWidgetsBuilder 时需要传递
        internal float CurrentIndentX;

        // 当前格内块注册面板（HTML 表格 td 直装的 RichContentPanel）：非 null 时 CreateCodeBlock/
        // AddImageBlock 的 shell 注册进 CurrentPanel.Shells 而非顶层 CodeBlocks/ImageBlocks——
        // 格内块的宽度回传方是所在格 panel（按格净宽回填），顶层注册表不触格内 frame。
        // 栈式 push/pop 纪律同 CurrentIndentX：构建格内容前保存、构建后恢复（归调用方，本类不维护栈）
        internal RichContentPanel CurrentPanel;

        // 定宽 sizer 祖先的子树可用宽约束（HTML 前端 TryBeginStyleBox 播种/EndStyleBox 弹出，
        // 元组=「种子时可用宽, 种子时 % 声明, 种子时缩进」；∞ 宽条目=无 px 声明壳的配对占位，
        // Percent<0=无 % 声明，px 与 % 载荷可在同一条目并存如 px width+ % max-width）。% 声明
        // 两路参与：构建期由 ConstrainedContentWidth 以显式 ContentWidth 为基准现场解析（显式
        // ContentWidth 宿主的 img frame 不进 ImageBlocks 回填注册表——注册仅 ContentWidth<=0
        // ——构建期钳制是这类 frame 的唯一定宽点）；跟随宿主（ContentWidth<=0）% 无基准不解析，
        // 测量期 ResolveAncestorCap 以回填宿主 contentWidth 为基准接住。消费见 AddImageBlock
        internal readonly Stack<(float AvailWidth, float Percent, float IndentX)> WidthConstraints = [];

        // 当前缩进下的约束可用宽：px 条目按「种子可用宽−种子后新增缩进」折算取最小（嵌套内层
        // 胜；折算增量钳非负——表格格等重入点把 CurrentIndentX 归零时外层种子不得反向放宽/过收）；
        // % 条目按「ContentWidth×pct/100−CurrentIndentX」现场解析（负钳 0）后同取 min——%
        // 无构建期基准，以宿主显式 ContentWidth 为近似分母、缩进全量扣减（CSS % 应解自包含块
        // 净宽的近似口径，见上留档）
        internal float ConstrainedContentWidth {
            get {
                float width = float.PositiveInfinity;
                foreach ((float availWidth, float percent, float indentX) in WidthConstraints) {
                    width = MathUtils.Min(width, availWidth - MathUtils.Max(CurrentIndentX - indentX, 0f));
                    if (percent >= 0f
                        && ContentWidth > 0f) {
                        width = MathUtils.Min(width, MathUtils.Max(ContentWidth * percent / 100f - CurrentIndentX, 0f));
                    }
                }
                return width;
            }
        }

        // 代码块自然宽的水平边距合计：linesPanel Margin.X 5×2 + 外层 ScrollPanel Margin.X 3×2。
        // CreateCodeBlock 构建期把最长行文本宽累加此项记入 shell 条目 NaturalWidth（格内协商
        // 首遍的自然宽探测输入，Q1-B 档）
        const float CodeBlockHorizontalMargin = 16f;

        // 标题字号/金色阶梯（PRD 6.3，帖子详情 header 视觉同源）
        static readonly float[] s_headingScales = [1.6f, 1.4f, 1.2f, 1.1f, 1f, 1f];

        // 标题阶梯取值（HtmlToWidgetsBuilder 盒属性 em 上下文线程用：CSS hN UA font-size 对
        // 自身盒 padding/margin 与子树同时生效，builder 走链 ctx 进标题 case 即乘）
        protected static float HeadingScale(int level) => s_headingScales[MathUtils.Min(level, 6) - 1];

        static readonly Color HeadingColor = new(255, 230, 140);

        protected static readonly Color QuoteColor = new(175, 175, 175);

        static readonly Color QuoteLineColor = new(120, 120, 120);

        static readonly Color CodeTextColor = new(225, 225, 228);

        static readonly Color CodeCenterColor = new(52, 52, 56);

        static readonly Color CodeBevelColor = new(90, 90, 95);

        protected static readonly Color ThematicBreakColor = new(120, 120, 120);

        protected const int ListIndentPerDepth = 22;

        // 列表行内坐标（相对挂载点）：marker 列偏移、marker→文字保底间隙、任务 checkbox 尺寸。
        // marker 字号=正文（用户裁定勿缩小）；y 偏移不做字形垂直补偿（字体可自定义，补偿会害正常字体偏下）
        protected const float MarkerOffsetX = 4f;

        protected const float MarkerOffsetY = 3f;

        protected const float MarkerTextGap = 8f;

        protected const float TaskCheckboxSize = 16f;

        // 图片块高上限（适配时缩小不放大，与 PDCB 同值）
        protected const float MaxImageHeight = 360f;

        // 块级嵌套深度上限（引用/列表/HTML 互嵌递归）：防御不可信输入的深嵌套
        protected const int MaxDepth = 64;

        protected virtual RichTextWidget CreateRichText(Color? colorOverride) {
            RichTextWidget widget = new() {
                WordWrap = true,
                Font = Font,
                FontScale = FontScale,
                Color = colorOverride ?? DefaultColor,
                Margin = new Vector2(0f, 2f)
            };
            if (LinkClicked != null) {
                widget.LinkClicked += href => LinkClicked(href);
            }
            return widget;
        }

        protected static StackPanelWidget CreatePanel() => new() {
            Direction = LayoutDirection.Vertical
        };

        // 图片块：走共用 RichContentImageLoader；宽度未定时 frame 记入 ImageBlocks 由测量期回填。
        // 占位宽扣减嵌套缩进（列表项/引用块内图片不占全宽）。
        // explicitSize：img width/height 属性（-1=该维未给，走自适应）；margin：hspace/vspace
        // 外边距（未给走默认 0/8）；maxWidth：CSS max-width px（<0=未声明，仅 height 反推与
        // 自适应路径的显示尺寸钳制随它下沉加载/重适配路径，占位宽仍归回填子系统）
        // 返回 frame（挂树由调用方负责——HTML 侧 style 盒属性需把 frame 包进壳后挂）
        protected CanvasWidget AddImageBlock(StackPanelWidget panel, string url, string alt, Vector2? explicitSize = null, Vector2? margin = null, float maxWidth = -1f) {
            float width = NestedContentWidth();
            // 定宽 sizer 祖先钳制占位宽（只钳正值：width=-1 为测量期回填哨兵，保持跟随语义
            // 不被数值比较吞掉）：explicitSize 帧宽=属性值不受占位宽影响（CSS 显式宽胜祖先约束），
            // 自适应帧在加载/重适配路径同源此宽。% 声明两路参与：显式 ContentWidth 宿主在
            // ConstrainedContentWidth 内现场解析（此类 frame 不进回填注册表，构建期是唯一定宽
            // 点）；跟随宿主（ContentWidth<=0）% 无基准不解析，测量期 ResolveAncestorCap 接住
            if (width > 0f && width > ConstrainedContentWidth) {
                width = MathUtils.Max(ConstrainedContentWidth, 50f);
            }
            CanvasWidget frame = ImageLoader.CreateImageBlock(url, alt, width, MaxImageHeight, explicitSize, maxWidth);
            frame.Margin = margin ?? new Vector2(0f, 8f);
            // 注册路由（同 CreateCodeBlock）：格内无条件进所在格 panel——panel 的回填基准是
            // 格净宽而非顶层 ContentWidth，顶层 ContentWidth 正负不影响格内注册
            if (CurrentPanel != null) {
                CurrentPanel.Shells.Add(new ShellEntry {
                    Frame = frame,
                    IndentX = CurrentIndentX,
                    Kind = ShellKind.ImageFrame
                });
            }
            else if (ContentWidth <= 0f) {
                ImageBlocks.Add((frame, CurrentIndentX));
            }
            return frame;
        }

        // 嵌套感知的可用内容宽：显式 ContentWidth 扣减当前缩进（保底防深嵌套负值）；
        // <0 时返回 -1（跟随父格/测量期回填）
        protected float NestedContentWidth() {
            return ContentWidth > 0f ? MathUtils.Max(ContentWidth - CurrentIndentX, 50f) : -1f;
        }

        // 标题行 widget：金色 + 字号阶梯（level 1-6）
        protected RichTextWidget CreateHeading(int level, Color? colorOverride) {
            RichTextWidget widget = CreateRichText(colorOverride);
            widget.FontScale = FontScale * s_headingScales[MathUtils.Min(level, 6) - 1];
            widget.Color = HeadingColor;
            widget.Margin = new Vector2(0f, level <= 2 ? 8f : 4f);
            return widget;
        }

        // 代码块：BevelledRectangle 框 + 横向 ScrollPanel + 逐行 Label（不换行，保留原始空白）。
        // ScrollPanel 不产出 DesiredSize：不定位的子节点由外层 Canvas 排满整个 cell，
        // 外层高度显式定（行高累计 + 滚动条/内边距余量），文字内边距走 linesPanel Margin
        // textColor/fontScaleOverride：HTML 侧 pre style 文字类声明直通（color/font-size，
        // 便宜档——等宽管线不进 span 解析，行内其余属性不吃留档）。font-size 为整体替换
        // UA 基准 0.9（绝对/相对声明统一为 ×全局基准系数，同 <small style="font-size">
        // 整体替换先例）；null=现状（Markdown 路径零改）
        // roundingRadius：HTML 侧 pre style border-radius 直通（仅四角最大 px 值便宜档，%
        // 忽略）——引擎 BevelledShapeRenderer 真圆弧石皮观感 vs CSS 扁平圆角有差（留档）；
        // null=引擎缺省 6（Markdown 路径零改）
        // backgroundColor：HTML 侧 pre/内层 code style background-color 直通 frame.CenterColor
        //（内层 code 声明胜 pre，见 HtmlToWidgetsBuilder.ParseCodeTextStyle）——null=调色板
        // CodeCenterColor 零改（Markdown 路径零改）
        // flexNaturalBasis：flex 容器内代码块主轴 basis=自然宽（max-content 同义）——容器净宽
        // 回填与 flex 排布互斥（回填循环经 FlexPanelWidget.IsInsideFlex 跳过 flex 内 frame），
        // 构建期 Size.X 直接定 naturalWidth（false=现状 NestedContentWidth，Markdown 路径零改）
        protected CanvasWidget CreateCodeBlock(string[] lines, Color? textColor = null, float? fontScaleOverride = null, float? roundingRadius = null, bool flexNaturalBasis = false, Color? backgroundColor = null) {
            if (lines.Length == 0
                || (lines.Length == 1 && lines[0].Length == 0)) {
                lines = [" "];
            }
            float codeScale = fontScaleOverride ?? 0.9f;
            float lineHeight = ((Font?.GlyphHeight ?? 51f) + (Font?.Spacing.Y ?? 0f)) * codeScale * (Font?.Scale ?? 1f) + 1f;
            float contentHeight = lines.Length * lineHeight;
            // 最长行自然宽（不换行逐行 Label 的渲染宽）：格内协商首遍（∞ 测量）以此作 CodeBlock
            // 的 desired，列协商才能分到合理宽（ScrollPanel 零贡献，−1 natural 会退化成 ≈0）
            float naturalWidth = CodeBlockHorizontalMargin;
            if (Font != null) {
                foreach (string line in lines) {
                    naturalWidth = MathUtils.Max(
                        naturalWidth,
                        Font.MeasureText(line.Length == 0 ? " " : line, new Vector2(codeScale), Vector2.Zero).X + CodeBlockHorizontalMargin
                    );
                }
            }
            CanvasWidget outer = new() {
                Size = new Vector2(flexNaturalBasis ? naturalWidth : NestedContentWidth(), contentHeight + 20f),
                Margin = new Vector2(0f, 4f)
            };
            BevelledRectangleWidget frame = new() {
                Size = new Vector2(-1f, -1f),
                HorizontalAlignment = WidgetAlignment.Stretch,
                VerticalAlignment = WidgetAlignment.Stretch,
                RoundingRadius = roundingRadius ?? 6f,
                CenterColor = backgroundColor ?? CodeCenterColor,
                BevelColor = CodeBevelColor,
                DirectionalLight = 0f,
                AmbientLight = 1f
            };
            outer.Children.Add(frame);
            ScrollPanelWidget scrollPanel = new() {
                Direction = LayoutDirection.Horizontal,
                ScrollPosition = 0f,
                Margin = new Vector2(3f, 3f),
            };
            StackPanelWidget linesPanel = new() {
                Direction = LayoutDirection.Vertical,
                Margin = new Vector2(5f, 6f)
            };
            foreach (string line in lines) {
                linesPanel.Children.Add(new FontTextWidget {
                    Text = line.Length == 0 ? " " : line,
                    Font = Font,
                    Color = textColor ?? CodeTextColor,
                    FontScale = codeScale,
                    WordWrap = false,
                    // P4：代码块文字走等宽链（mono 槽位→系统等宽，链尾复用 body 条目）；
                    // 链构建失败时 FontManager.MonoChain 已回落 DefaultChain，全缺则为 null 退位图路径
                    VectorFont = FontManager.MonoChain
                });
            }
            scrollPanel.Children.Add(linesPanel);
            outer.Children.Add(scrollPanel);
            // 注册路由：格内（CurrentPanel 在场）注册进所在格 panel（宽度由格净宽回传）；
            // 顶层照旧进 CodeBlocks 由宿主 widget MeasureOverride 按顶层可用宽回填
            if (CurrentPanel != null) {
                CurrentPanel.Shells.Add(new ShellEntry {
                    Frame = outer,
                    IndentX = CurrentIndentX,
                    Kind = ShellKind.CodeBlock,
                    NaturalWidth = naturalWidth
                });
            }
            else {
                CodeBlocks.Add((outer, CurrentIndentX));
            }
            return outer;
        }

        // 引用块外壳：宽随嵌套收窄（构建时 CurrentIndentX=父累计，未含本块 +10——inner 挂载点
        // 扣减的 10 即本块缩进），左竖线（-1 高度随内容拉伸）+ 灰字缩进容器，inner 面板交调用方填内容
        protected CanvasWidget CreateQuoteFrame(out StackPanelWidget inner) {
            CanvasWidget outer = new() {
                Size = new Vector2(NestedContentWidth(), -1f),
                Margin = new Vector2(0f, 4f)
            };
            RectangleWidget line = new() {
                Size = new Vector2(2f, 1f),
                VerticalAlignment = WidgetAlignment.Stretch,
                FillColor = QuoteLineColor,
                OutlineColor = Color.Transparent,
                OutlineThickness = 0f
            };
            outer.Children.Add(line);
            inner = CreatePanel();
            // 内容缩进走 MarginLeft 不走 SetPosition：Canvas 对 SetPosition 子件按 desired 尺寸排布
            //（alignment 被忽略），收缩宽下段级 HorizontalCenter 锚点无居中空间；MarginLeft 走通用
            // 排布路径，Near 视觉不变（x=MarginLeft=10），居中容器内引用由 HTML 侧置 Stretch 撑满
            inner.MarginLeft = 10f;
            inner.MarginTop = 2f;
            inner.MarginBottom = 2f;
            outer.Children.Add(inner);
            return outer;
        }

        // 表头单元格 span 染粗（Markdown 管道表与 HTML 表共用口径）：正排/斜体 span 一律加粗
        //（斜体保留=粗斜体），其余属性不动
        protected static void BoldCellSpans(List<TextSpan> spans) {
            foreach (TextSpan span in spans) {
                span.Bold = true;
            }
        }

        // 水平分隔线（hr / Markdown ---）
        protected static RectangleWidget CreateThematicBreak() {
            return new RectangleWidget {
                Size = new Vector2(-1f, -1f),
                HorizontalAlignment = WidgetAlignment.Stretch,
                FillColor = ThematicBreakColor,
                OutlineColor = Color.Transparent,
                OutlineThickness = 0f,
                Margin = new Vector2(0f, 6f)
            };
        }
    }
}
