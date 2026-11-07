using Engine;

namespace Game {
    public class TextSpan {
        public string Text { get; set; }

        // null 时回退到 RichTextWidget.Color
        public Color? Color { get; set; }

        // 行内背景色，A==0（含未声明 default 与 CSS background:transparent 解析值）时无背景
        public Color BackgroundColor { get; set; }

        // 删除线（markdown ~~x~~ / HTML del|s|strike），绘制于 em 中线
        public bool Strikethrough { get; set; }

        // 下划线（HTML ins），画在字形下方不穿字（layer=-1 背景批，同链接下划线）
        public bool Underline { get; set; }

        // text-decoration:none（CSS 显式撤销）：抑制链接默认下划线（作者样式>UA 样式），
        // 仅对 IsLink run 生效，非链接 run 无下划线可撤
        public bool LinkUnderlineSuppressed { get; set; }

        // 基线偏移（HTML sub/sup）：正=下移、负=上移，单位=父字号 em 高的比例
        //（绘制期换算 GlyphHeight×DefaultFontScale×Font.Scale）。纯绘制偏移，不改行高/推进量
        public float BaselineShift { get; set; }

        public float FontScale { get; set; }

        // 粗体/斜体（正交 bool 组与 Underline/Strikethrough 同模型）：
        // 矢量直出真面或降级（Stroked1/15° 切变，BoldItalic 请求缺面时按 degrade 序回退），位图伪粗双绘+切变
        public bool Bold { get; set; }

        public bool Italic { get; set; }

        // 等宽（markdown `code` / HTML code|samp|kbd）：排版 advance 与绘制都走等宽矢量链
        //（RichTextWidget.MonoVectorFont），链 null 或首字符缺字随普通 run 回退位图路径
        public bool Monospace { get; set; }

        // 非 null 时该段为链接，点击触发 RichTextWidget.LinkClicked
        public string Href { get; set; }

        // 行内边框（HTML border-* style），null=无边框。每行盒片段独立成框（跨行两侧都有）
        public SpanBorder Border { get; set; }

        // 调用方自定义载荷（如 markdown 源节点），排版层不解释
        public object Tag { get; set; }

        // 行内透明度（CSS opacity，行内 replace 语义）：1=不透明。Draw 期淡出系数，
        // 与宿主 ColorTransform（块级 ctx）叠乘；排版/命中不受影响（纯绘制）
        public float Opacity { get; set; }

        public TextSpan() {
            Text = string.Empty;
            // 0 = 继承 widget 的 FontScale（排版层 SpanScale 对 ≤0 回退），显式值优先
            FontScale = 0f;
            Opacity = 1f;
        }

        public TextSpan(string text) : this() => Text = text ?? string.Empty;

        public TextSpan(string text, Color? color) : this(text) => Color = color;

        public TextSpan(string text, Color? color, bool bold = false, bool italic = false) : this(text, color) {
            Bold = bold;
            Italic = italic;
        }

        public bool IsLink => !string.IsNullOrEmpty(Href);
    }
}