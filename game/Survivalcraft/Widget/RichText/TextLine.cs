namespace Game {
    // 一行内一个同样式连续片段：span 按行拆分后每段生成一个 TextRun
    public class TextRun {
        public TextSpan Span { get; set; }

        // 本行实际绘制的文本（跨行拆分或首尾空白裁剪后的子串）
        public string Text { get; set; }

        // 行内 x 起点（相对行左缘）
        public float X { get; set; }

        public float Width { get; set; }

        public bool IsLink => Span.IsLink;
    }

    public class TextLine {
        public List<TextRun> Runs { get; } = [];

        // 累计所有 run 后的行宽
        public float Width { get; set; }

        // 行高：行内最大 FontScale 对应的 (GlyphHeight + Font.Spacing.Y + FontSpacing.Y) * scale * Font.Scale
        public float Height { get; set; }

        // 相对排版块顶部的 y 偏移
        public float Y { get; set; }

        // 该行使用的最大 FontScale
        public float MaxScale { get; set; }

        // 截断该行即可完成整段排版（供 Ellipsis 判定）
        public bool IsEllipsized { get; set; }
    }
}