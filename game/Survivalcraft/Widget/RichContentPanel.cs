using Engine;

namespace Game {
    // shell 类别：panel 测量期按类别决定宽度回填方式（有限宽回填 / 无界时自然宽探测）
    public enum ShellKind {
        CodeBlock,

        ImageFrame,

        // 引用块外壳 / 列表 li 行框（构建期 Size=(-1,-1) natural 宽的块级外壳）
        FlowShell
    }

    // 统一 shell 注册表条目：Frame=宽度需按所在容器净宽回填的块级外壳，IndentX=构建时的
    // 嵌套内容列缩进（回填时从容器净宽中扣减，同 RichContentBuilder.CodeBlocks 元组口径），
    // NaturalWidth=构建期记录的内容自然宽（仅 CodeBlock 消费，见 MeasureOverride 无界分支）
    public struct ShellEntry {
        public CanvasWidget Frame;

        public float IndentX;

        public ShellKind Kind;

        public float NaturalWidth;
    }

    // 格内块级内容容器（HTML 表格 td 直装产物，RichContentPanel 命名照 RichContentBuilder/
    // RichContentImageLoader 同名族）：把 MarkdownWidget/HtmlWidget.MeasureOverride 的"块 shell
    // 宽度测量期回填"段提取成可挂格内的独立部件。回传通道=引擎 Measure 链——TablePanelWidget
    // 高度轮 MeasureRowHeights 对每格 Measure(innerW,∞)，格净宽沿 parentAvailableSize.X 到达
    // 本部件（有限=高度轮回传点；∞=协商首遍列宽探测，spec
    // docs/superpowers/specs/2026-10-04-cell-content-width-feedback-design.md §3）
    public class RichContentPanel : StackPanelWidget {
        // <0 跟随 parentAvailableSize.X（同 MarkdownWidget/HtmlWidget 宿主壳口径）
        public float ContentWidth { get; set; } = -1f;

        // 引用顶层 host 的图片管线（panel 不持有纹理生命周期，仅调用回填/重适配；
        // null = 无图片管线，ImageFrame 一律走占位回填分支）。internal 照
        // RichContentBuilder.ImageLoader 口径：loader 类型不对外暴露
        internal RichContentImageLoader ImageLoader;

        // 统一 shell 注册表（builder 构建期写入，单列表+Kind；测量期逐帧遍历回填）
        public readonly List<ShellEntry> Shells = [];

        public RichContentPanel() {
            Direction = LayoutDirection.Vertical;
            // 块容器横向撑满宿主格（照 HtmlWidget ctor 口径）：shell 宽回填的参考系恒为格净宽，
            // 不随内容最宽子件塌缩
            HorizontalAlignment = WidgetAlignment.Stretch;
        }

        public override void MeasureOverride(Vector2 parentAvailableSize) {
            float contentWidth = ContentWidth > 0f ? ContentWidth : parentAvailableSize.X;
            // 有限判定照宿主壳口径（>0 才视作有效回填基准）：TablePanelWidget 协商首遍以 (∞,∞)
            // 测格——此时基准不存在，只做自然宽探测供列协商消费；高度轮以净宽重测才回填
            bool bounded = float.IsFinite(contentWidth) && contentWidth > 0f;
            foreach (ShellEntry shell in Shells) {
                if (bounded) {
                    // 格净宽回传：shell 宽=格净宽−嵌套缩进（50 保底照 MarkdownWidget 回填口径）；
                    // 祖先定宽 sizer 钳制（HtmlBlockSizerWidget.ResolveAncestorCap，px+ % 声明
                    // 自顶向下解析、基准=本格 contentWidth；与顶层 HtmlWidget 完全同序：先 min
                    // cap 后 50 保底）——`<td><div style="width:200px">` 的 sizer 不再被格净宽
                    // 回填击穿，`<td><div style="width:50%">` 同口径钳半宽
                    // flex 内口径（FlexPanelWidget.IsInsideFlex）：代码块构建期 Size.X=自然宽
                    //（CreateCodeBlock flexNaturalBasis）已对，跳过覆写；已应用图跳过 RefitFrame
                    //（自然显示宽胜，超 item 宽由 arrange 钳制/溢出）；pending 占位帧覆写保留
                    //（瞬时通胀=格净宽，应用后归位显示宽——frame.Size.X>0 才能被
                    // ResolvePendingWidths 收口）；FlowShell（引用外壳/li 行框）维持现状回填
                    bool inFlex = FlexPanelWidget.IsInsideFlex(shell.Frame, this);
                    float cap = HtmlBlockSizerWidget.ResolveAncestorCap(shell.Frame, this, contentWidth);
                    float width = MathUtils.Max(MathUtils.Min(contentWidth - shell.IndentX, cap), 50f);
                    if (shell.Kind == ShellKind.ImageFrame
                        && ImageLoader?.IsFrameApplied(shell.Frame) == true) {
                        if (!inFlex) {
                            // 已换纹理的 frame 宽=图宽不可直改：随格宽等比重适配（只缩不放，网页
                            // max-width:100% 同义）；Size.Y 等比联动 → 行高随本帧测量自动更新。
                            // width/height 属性帧走 RefitFrame 内的显式口径（属性优先+防溢出钳制）
                            ImageLoader.RefitFrame(shell.Frame, width);
                        }
                    }
                    else if ((shell.Kind != ShellKind.CodeBlock || !inFlex)
                        && (shell.Kind != ShellKind.ImageFrame
                            || ImageLoader?.IsFrameExplicit(shell.Frame) != true)) {
                        shell.Frame.Size = new Vector2(width, shell.Frame.Size.Y);
                    }
                }
                else {
                    // 无界分支（协商首遍，spec §3 无界语义=内容自然宽探测，Q1-B 档）。有限分支
                    // 上帧覆写过显式宽后 −1 natural 不可自愈，必须逐帧还原协商输入（否则 ∞ 探测
                    // 读到上帧回填值，列宽逐帧缩 2×CellPadding 直至 50 保底的棘轮）：
                    // CodeBlock=构建期最长行宽；已加载图=纹理固有宽（高上限内，RefitFrame(∞)
                    // 即按上限内全宽适配）；占位 frame/quote 外壳/li 行框=−1 natural（内容自然宽，
                    // CSS max-content 同义）
                    switch (shell.Kind) {
                        case ShellKind.CodeBlock:
                            shell.Frame.Size = new Vector2(MathUtils.Max(shell.NaturalWidth, 50f), shell.Frame.Size.Y);
                            break;
                        case ShellKind.ImageFrame:
                            if (ImageLoader?.IsFrameApplied(shell.Frame) == true) {
                                ImageLoader.RefitFrame(shell.Frame, float.PositiveInfinity);
                            }
                            else if (ImageLoader?.IsFrameExplicit(shell.Frame) != true) {
                                // 属性显式帧保持构建期属性值（协商自然宽=属性宽），不做 -1 还原
                                shell.Frame.Size = new Vector2(-1f, shell.Frame.Size.Y);
                            }
                            break;
                        default:
                            shell.Frame.Size = new Vector2(-1f, shell.Frame.Size.Y);
                            break;
                    }
                }
            }
            // 宽已解析的挂起 frame（纹理先于测量就绪：缓存命中/秒图）此刻补适配上屏
            // （照 MarkdownWidget/HtmlWidget 惯例；∞ 遍 frame 宽已还原 ≤0，挂起帧天然跳过）
            ImageLoader?.ResolvePendingWidths();
            base.MeasureOverride(parentAvailableSize);
        }
    }
}
