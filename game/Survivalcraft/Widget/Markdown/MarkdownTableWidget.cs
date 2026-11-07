using Engine;
using Engine.Graphics;
using Engine.Media;

namespace Game {
    public enum MarkdownTableAlignment {
        Left,
        Center,
        Right
    }

    // Markdown 表格部件（P3，设计＝temp/MarkdownSupportPRD.md §7）：布局委托 TablePanelWidget
    // （Task 7 换底座），本体只保留对外契约与滚动分派。每格一个 RichTextWidget（复用排版/链接
    // 命中/伪粗体/背景/删除线），AddRow 时快照字体属性、塞 TableRowWidget 进内嵌 TablePanelWidget。
    // 列宽语义（旧实现迁移）：列宽=自然内容宽+2×CellPadding（padding 属列框内）——底座协商量测
    // cell DesiredSize.X（不含 CellPadding），故 AddRow 时把 cell Size.X 显式锚定为 自然宽+2×6，
    // 底座 track 即旧列宽；高度轮底座按 track−2×CellPadding 重测拿折行高（Size.Y=-1 自动），
    // 摆位由底座 ArrangeChildWidgetInCell 内缩 CellPadding，等价旧 Arrange((x+6,y+3),(w-12,h-6))。
    // 滚动分派（旧 Σmin 判定迁移）：Measure 先按宿主可用宽测底座——Σ自然宽 ≤ 可用宽自然宽原样，
    // 超宽底座线性比例收缩（旧瀑布削峰的裁定替代，收缩态列宽可能不同），Σmin 仍超宽时底座定格
    // min 交外层 → DesiredSize.X > 可用宽即切横向 ScrollPanel 并按 ∞ 重测拿自然宽兜底。
    // 全边框格线：底座 OuterBorder+InnerBorder，BorderColor 跟随 GridLineColor，layer=-1 quad。
    // 无斑马条纹；表头粗体由 builder 侧染 span Style，跟随内容换行
    public class MarkdownTableWidget : ContainerWidget {
        // 格线色（淡，半透明灰）；case 目验调
        public static Color GridLineColor = new(160, 160, 160, 90);

        const float CellPaddingX = 6f;

        const float CellPaddingY = 3f;

        // 滚动模式下 ScrollPanel 高度 = 表格高 + 此余量：滚动条画在 ActualSize.Y-5..-1，
        // 不留余量会压在表格底框线上
        const float ScrollBarReserve = 8f;

        readonly List<TableRowWidget> m_rows = [];

        readonly List<MarkdownTableAlignment?> m_alignments = [];

        readonly TablePanelWidget m_table;

        ScrollPanelWidget m_scrollPanel;

        // 视口宽（滚动模式下 ScrollPanel 的 ActualSize）
        float m_viewportWidth;

        bool m_scrollMode;

        // cells 在 AddRow 时快照字体属性，故 Font/FontScale/DefaultColor 须在加行前设置
        public BitmapFont Font { get; set; }

        public float FontScale { get; set; } = 1f;

        public Color DefaultColor { get; set; } = Color.White;

        public event Action<string> LinkClicked;

        public MarkdownTableWidget() {
            Margin = new Vector2(0f, 4f);
            m_table = new TablePanelWidget {
                CellPadding = new Vector2(CellPaddingX, CellPaddingY), OuterBorder = true, InnerBorder = true, BorderColor = GridLineColor
            };
        }

        public void AddRow(bool isHeader, List<List<TextSpan>> cells) {
            TableRowWidget row = new() { Header = isHeader };
            foreach (List<TextSpan> spans in cells) {
                RichTextWidget widget = new() {
                    Font = Font,
                    FontScale = FontScale,
                    Color = DefaultColor,
                    WordWrap = true,
                    // 拉满 cell 矩形（track−2×CellPadding），等价旧 Arrange((x+6,y+3),(w-12,h-6))：
                    // TextAnchor 居中/右对齐按 ActualSize 定偏移，Near 会塌缩到内容宽
                    HorizontalAlignment = WidgetAlignment.Stretch,
                    VerticalAlignment = WidgetAlignment.Stretch
                };
                widget.Spans.AddRange(spans);
                if (LinkClicked != null) {
                    widget.LinkClicked += href => LinkClicked(href);
                }
                // 列宽=自然内容宽+2×CellPadding：按 float.MaxValue 自测拿自然宽（与旧 scratch
                // Layout(float.MaxValue,∞) 同参对齐），Size.X 锚定为 自然宽+2×6 让底座 track=旧
                // 列宽；Size.Y=-1 保持折行高自动（底座高度轮按 track−2×CellPadding 重测消费）
                widget.Measure(new Vector2(float.MaxValue, float.PositiveInfinity));
                widget.Size = new Vector2(widget.ParentDesiredSize.X + 2f * CellPaddingX, -1f);
                row.Children.Add(widget);
            }
            m_rows.Add(row);
            m_table.Children.Add(row);
            AssignAnchors();
        }

        // 与列一一对应（GFM 冒号对齐；缺省/超出部分左对齐）
        public void SetColumnAlignments(IReadOnlyList<MarkdownTableAlignment?> alignments) {
            m_alignments.Clear();
            m_alignments.AddRange(alignments);
            AssignAnchors();
        }

        public override void MeasureOverride(Vector2 parentAvailableSize) {
            IsDrawRequired = true;
            m_table.BorderColor = GridLineColor; // 静态字段每帧跟随（case 目验调）
            float availX = MathUtils.Max(parentAvailableSize.X, 0f);
            // 先按宿主可用宽测底座：DesiredSize.X 超出可用宽 = 各列压到 min 仍超宽（底座定格 min
            // 交外层），即旧版滚动兜底信号；其余（自然宽/线性收缩）底座已自行协商完成。
            // +0.01f 容差：线性收缩态 DesiredSize.X 数学上恰=availX，浮点求和落 +ε 侧会误切滚动
            // （表格按自然宽渲染+高度多 8px 余量，视觉突变）——对齐旧 CollapseWidths 的 0.01 退出容差
            m_table.Measure(new Vector2(availX, float.PositiveInfinity));
            m_scrollMode = m_table.DesiredSize.X > availX + 0.01f;
            UpdateChildrenStructure();
            if (m_scrollMode && m_scrollPanel != null) {
                // 滚动兜底按 ∞ 重测拿自然宽（底座 shrink-to-fit 退化=max；定格态底座已把列 max
                // 钳到 min，故 track=min=token 扫描宽，略 kerning 比旧带 kerning 自然宽约 +2%）
                m_table.Measure(new Vector2(float.PositiveInfinity, float.PositiveInfinity));
                m_viewportWidth = MathUtils.Min(m_table.DesiredSize.X, availX);
                float height = m_table.DesiredSize.Y + ScrollBarReserve;
                // 子件测量必须由本部件驱动：ScrollPanel 测 child 时 X 方向不约束，m_table 测量返回既定尺寸
                m_scrollPanel.Measure(new Vector2(m_viewportWidth, height));
                DesiredSize = new Vector2(m_viewportWidth, height);
            }
            else {
                DesiredSize = m_table.DesiredSize;
            }
        }

        public override void ArrangeOverride() {
            if (m_scrollMode && m_scrollPanel != null) {
                m_scrollPanel.Arrange(Vector2.Zero, new Vector2(m_viewportWidth, m_table.DesiredSize.Y + ScrollBarReserve));
                return;
            }
            m_table.Arrange(Vector2.Zero, m_table.DesiredSize);
        }

        // 结构切换：滚动模式=Children[ScrollPanel[table]]，非滚动=Children[table]。
        // WidgetsList.Add 对已含子件 Contains 早退不改 ParentWidget，换容器须先显式 Remove 旧宿主
        void UpdateChildrenStructure() {
            if (m_scrollMode) {
                if (m_scrollPanel == null) {
                    m_scrollPanel = new ScrollPanelWidget { Direction = LayoutDirection.Horizontal, ScrollPosition = 0f, ScrollSpeed = 0f };
                }
                if (m_table.ParentWidget != m_scrollPanel) {
                    Children.Remove(m_table);
                    m_scrollPanel.Children.Add(m_table);
                }
                if (m_scrollPanel.ParentWidget != this) {
                    Children.Clear();
                    Children.Add(m_scrollPanel);
                }
            }
            else if (m_table.ParentWidget != this) {
                m_scrollPanel?.Children.Remove(m_table);
                Children.Clear();
                Children.Add(m_table);
            }
        }

        // 列对齐锚定：列数=各行 cell 数最大值（缺省/超出列左对齐），逐列写 cell TextAnchor
        // （Draw 期消费；行组装时/对齐后设一次即可，与旧 Relayout 时机等价）
        void AssignAnchors() {
            int columns = 0;
            foreach (TableRowWidget row in m_rows) {
                columns = MathUtils.Max(columns, row.Children.Count);
            }
            for (int c = 0; c < columns; c++) {
                MarkdownTableAlignment? alignment = c < m_alignments.Count ? m_alignments[c] : null;
                TextAnchor anchor = alignment switch {
                    MarkdownTableAlignment.Center => TextAnchor.HorizontalCenter,
                    MarkdownTableAlignment.Right => TextAnchor.Right,
                    _ => TextAnchor.Default
                };
                foreach (TableRowWidget row in m_rows) {
                    if (c < row.Children.Count) {
                        ((RichTextWidget)row.Children[c]).TextAnchor = anchor;
                    }
                }
            }
        }
    }
}