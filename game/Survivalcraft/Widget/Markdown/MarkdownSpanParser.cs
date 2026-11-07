using System.Net;
using System.Text;
using Engine;
using Markdig.Syntax.Inlines;

namespace Game {
    // text-transform 值域（CSS 属性子集）：Capitalize=每词首字母大写、其余字符原样
    //（浏览器同款，不做 ToTitleCase 的"其余转小写"）
    public enum TextTransform {
        None,

        Uppercase,

        Lowercase,

        Capitalize
    }

    // 行内样式上下文：粗斜体/颜色/链接随嵌套深入累积
    public struct MarkdownSpanStyle {
        public bool Bold;

        public bool Italic;

        public Color? Color;

        // 行内底色（标签栈路径由 AddText 直带；DOM 递归路径另有后刷机制，两处同值冗余无害）
        public Color? Background;

        public bool Strikethrough;

        public string Href;

        // 字号倍率（HTML small/sub/sup 嵌套累乘）；0=未设置，span.FontScale 留 0 继承 widget
        public float Scale;

        public bool Underline;

        // text-decoration:none（CSS 显式撤销）：抑制链接默认下划线
        public bool LinkUnderlineSuppressed;

        // 基线偏移比例累积（sub +0.25 / sup −0.33），语义同 TextSpan.BaselineShift
        public float BaselineShift;

        // 行内边框（HTML border-* style），引用类型随结构复制共享，null=无边框
        public SpanBorder Border;

        // 等宽（HTML code/samp/kbd 置位）：随结构复制继承嵌套，AddText 出口透传 span.Monospace
        public bool Monospace;

        // 文本大小写变换（CSS text-transform，随嵌套继承；AddText 出口处施加）
        public TextTransform TextTransform;

        // 行内透明度（CSS opacity）：HasOpacity=false=未声明（struct default 即此态，span.Opacity
        // 留 1f）；opacity:0 也经 HasOpacity=true + Opacity=0 表达。随结构复制天然继承嵌套；
        // 再声明=替换不叠乘（显式 1 撤销继承的 <1），叠乘语义只存在于块级 ctx（builder 侧）
        public bool HasOpacity;

        public float Opacity = 1f;

        // CS8983：带字段初始值设定项的 struct 必须显式声明构造函数（空体即可，初始化由字段
        // 初始值承担）。注意 default(MarkdownSpanStyle) 绕过 ctor：HasOpacity=false、Opacity=0，
        // 消费端（AddText）以 HasOpacity 为准，default 态安全
        public MarkdownSpanStyle() { }
    }

    // Markdig inline 序列 → List<TextSpan>；HtmlInline 片段委托 HtmlSpanParser（Widget/Html/）
    public static class MarkdownSpanParser {
        // 行内嵌套深度上限：防御不可信输入的深嵌套（StackOverflowException 不可捕获，直接崩进程）
        const int MaxDepth = 64;

        public static void Parse(ContainerInline container, List<TextSpan> spans, MarkdownSpanStyle style, Color defaultColor) {
            Parse(container, spans, style, defaultColor, 0);
        }

        static void Parse(ContainerInline container, List<TextSpan> spans, MarkdownSpanStyle style, Color defaultColor, int depth) {
            if (depth > MaxDepth) {
                return;
            }
            // 行内 HTML 标签栈：Markdig 1.4.0 对每个标签各发一个 HtmlInline（"<b>x</b>"＝开/文/闭 3 项），
            // 开标签入栈、闭标签出栈，字面量按当前样式染色
            Stack<MarkdownSpanStyle> htmlStack = [];
            foreach (Inline inline in container) {
                // 行内 HTML 开/闭标签会更新样式上下文，需传回循环继续用
                style = ParseInline(inline, spans, style, defaultColor, htmlStack, depth);
            }
        }

        static MarkdownSpanStyle ParseInline(Inline inline,
            List<TextSpan> spans,
            MarkdownSpanStyle style,
            Color defaultColor,
            Stack<MarkdownSpanStyle> htmlStack,
            int depth) {
            switch (inline) {
                case LiteralInline literal: HtmlSpanParser.AddText(literal.Content.ToString(), spans, style, defaultColor); break;
                case EmphasisInline emphasis: {
                    // DelimiterCount：1=斜体、2=粗体、3=粗斜体；'~' 为删除线（pipeline 开 Strikethrough 扩展才会产出）
                    MarkdownSpanStyle inner = style;
                    if (emphasis.DelimiterChar == '~') {
                        inner.Strikethrough = true;
                    }
                    else {
                        if (emphasis.DelimiterCount >= 2) {
                            inner.Bold = true;
                        }
                        if (emphasis.DelimiterCount == 1
                            || emphasis.DelimiterCount >= 3) {
                            inner.Italic = true;
                        }
                    }
                    if (inline is ContainerInline emphasisContainer) {
                        Parse(emphasisContainer, spans, inner, defaultColor, depth + 1);
                    }
                    break;
                }
                case CodeInline code:
                    // 行内代码只接 Bold（斜体/删除线不接为既有取舍），装饰/缩放三字段随嵌套上下文透传
                    //（<u><code>、<small><code>、<sub><code> 行内路径不再丢样式）。
                    // TextTransform 同不接（code 字面语义，变换即失真——与斜体同口径留档）
                    spans.Add(
                        new TextSpan(code.Content, RichContentPalette.CodeColor) {
                            BackgroundColor = RichContentPalette.CodeBackgroundColor,
                            Monospace = true,
                            Bold = style.Bold,
                            FontScale = style.Scale,
                            Underline = style.Underline,
                            BaselineShift = style.BaselineShift
                        }
                    ); break;
                case LinkInline link: {
                    if (link.IsImage) {
                        // 图片占位：v1 不真渲染（P3）
                        spans.Add(new TextSpan($"[图片:{GetInlineText(link)}]", new Color(160, 160, 168)));
                        break;
                    }
                    MarkdownSpanStyle inner = style;
                    inner.Href = link.Url;
                    inner.Color = RichContentPalette.LinkColor;
                    if (inline is ContainerInline linkContainer) {
                        Parse(linkContainer, spans, inner, defaultColor, depth + 1);
                    }
                    break;
                }
                case AutolinkInline autolink: {
                    // Markdig 邮箱 autolink 的 Url 不带 scheme，补 mailto: 才能被系统识别打开
                    string href = autolink.IsEmail ? "mailto:" + autolink.Url : autolink.Url;
                    spans.Add(new TextSpan(autolink.Url, RichContentPalette.LinkColor) { Href = href });
                    break;
                }
                case LineBreakInline:
                    // 软换行 v1 一律当硬换行
                    HtmlSpanParser.AddText("\n", spans, style, defaultColor); break;
                case HtmlEntityInline entity:
                    HtmlSpanParser.AddText(WebUtility.HtmlDecode(entity.Original.ToString()), spans, style, defaultColor); break;
                case HtmlInline html: style = HtmlSpanParser.ApplyInlineHtmlTag(html.Tag, spans, style, defaultColor, htmlStack, depth); break;
                default:
                    if (inline is ContainerInline fallbackContainer) {
                        Parse(fallbackContainer, spans, style, defaultColor, depth + 1);
                    }
                    // 未知叶子 inline 不兜底转文本：LeafInline.ToString() 返回 CLR 类型名
                    // （仅 LiteralInline/AutolinkInline 重写过，均已在前接住），渲染出类名是垃圾
                    break;
            }
            return style;
        }

        // 拼接 inline 子树的纯文本（图片 alt 等场景）
        public static string GetInlineText(ContainerInline container) => GetInlineText(container, 0);

        static string GetInlineText(ContainerInline container, int depth) {
            if (depth > MaxDepth) {
                return string.Empty;
            }
            StringBuilder builder = new();
            foreach (Inline inline in container) {
                switch (inline) {
                    case LiteralInline literal: builder.Append(literal.Content.ToString()); break;
                    case CodeInline code: builder.Append(code.Content); break;
                    case HtmlEntityInline entity: builder.Append(WebUtility.HtmlDecode(entity.Original.ToString())); break;
                    default:
                        if (inline is ContainerInline inner) {
                            builder.Append(GetInlineText(inner, depth + 1));
                        }
                        break;
                }
            }
            return builder.ToString();
        }
    }
}