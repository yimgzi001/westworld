using Engine;

namespace Game {
    // 富文本行内配色：Markdown（MarkdownSpanParser）与 HTML（HtmlSpanParser）双解析器共用。
    // 行内代码橙黄字 + 深底（PRD 6.4）、超链接蓝
    public static class RichContentPalette {
        public static readonly Color CodeColor = new(255, 180, 96);

        public static readonly Color CodeBackgroundColor = new(60, 60, 70);

        public static readonly Color LinkColor = new(96, 168, 255);

        // <kbd> 键帽灰底深字（浅灰底是网页惯例，深色主题上按用户指定值保留亮块语义）
        public static readonly Color KbdColor = new(80, 80, 80);

        public static readonly Color KbdBackgroundColor = new(240, 240, 240);

        // <mark> 高亮：Chrome 默认黄底黑字
        public static readonly Color MarkColor = new(0, 0, 0);

        public static readonly Color MarkBackgroundColor = new(255, 255, 0);
    }
}