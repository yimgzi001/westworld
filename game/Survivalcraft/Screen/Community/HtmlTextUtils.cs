using HtmlAgilityPack;

namespace Game {
    // 帖子详情 HTML 相关纯函数：可见文本探测、纯文本规整、列表标记计算。
    // 与 widget 构造解耦，便于单测（RichTextTool 探针直调 marker/序号格式化）
    public static class HtmlTextUtils {
        // 翻译前置检查：是否存在可翻译的可见文本（script/style 与纯图片不计）。
        public static bool HasVisibleText(string html) {
            if (string.IsNullOrWhiteSpace(html)) {
                return false;
            }
            HtmlDocument document = new();
            document.LoadHtml(html);
            return HasVisibleText(document.DocumentNode);
        }

        static bool HasVisibleText(HtmlNode node) {
            foreach (HtmlNode child in node.ChildNodes) {
                switch (child.NodeType) {
                    case HtmlNodeType.Text:
                        if (!string.IsNullOrWhiteSpace(HtmlEntity.DeEntitize(((HtmlTextNode)child).Text))) {
                            return true;
                        }
                        break;
                    case HtmlNodeType.Comment: break;
                    case HtmlNodeType.Element:
                        if (child.Name?.ToLowerInvariant() is not ("script" or "style")
                            && HasVisibleText(child)) {
                            return true;
                        }
                        break;
                }
            }
            return false;
        }

        // 纯文本（Git release notes 等）逐行解析："- "/"* " 开头识别为列表项，空行丢弃。
        // 渲染（widget 产出）由 CommunityPostDetailsDialog.AppendPlainText 按此结果执行。
        public static List<(bool IsListItem, string Text)> SplitPlainTextLines(string text) {
            List<(bool, string)> lines = [];
            if (string.IsNullOrWhiteSpace(text)) {
                return lines;
            }
            foreach (string rawLine in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')) {
                string line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line)) {
                    continue;
                }
                if (line.StartsWith("- ", StringComparison.Ordinal)
                    || line.StartsWith("* ", StringComparison.Ordinal)) {
                    lines.Add((true, line[2..].Trim()));
                }
                else {
                    lines.Add((false, line));
                }
            }
            return lines;
        }

        // 无序列表标记按层级：• / ◦ / 第三级及更深 ▪；有序列表 "1." "2." …（number 由调用方按列表维护计数）。
        // Markdown 管线沿用本口径（无 type 属性概念）
        public static string GetListMarker(bool ordered, int number, int depth) {
            if (ordered) {
                return $"{number}.";
            }
            return depth switch {
                <= 1 => "•",
                2 => "◦",
                _ => "▪"
            };
        }

        // HTML type 属性扩展：ordered 认 '1'/'a'/'A'/'i'/'I'（其余回退阿拉伯），unordered 认
        // 字形 '•'/'◦'/'▪'（其余回退 disc）。type='\0' 表示无 type 属性，回退上一重载口径
        public static string GetListMarker(bool ordered, char type, int number, int depth) {
            if (type != '\0') {
                if (ordered) {
                    string body = type switch {
                        'a' => FormatLetterSequence(number, upper: false),
                        'A' => FormatLetterSequence(number, upper: true),
                        'i' => FormatRomanSequence(number, upper: false),
                        'I' => FormatRomanSequence(number, upper: true),
                        _ => null
                    };
                    if (body != null) {
                        return $"{body}.";
                    }
                    return $"{number}.";
                }
                return type is '◦' or '▪' ? type.ToString() : "•";
            }
            return GetListMarker(ordered, number, depth);
        }

        // 字母序号（bijective base-26：1→a…26→z，27→aa…）；≤0 越界回退阿拉伯（浏览器同款）
        public static string FormatLetterSequence(int number, bool upper) {
            if (number < 1) {
                return number.ToString();
            }
            var builder = new System.Text.StringBuilder();
            char low = upper ? 'A' : 'a';
            while (number > 0) {
                number--;
                builder.Insert(0, (char)(low + number % 26));
                number /= 26;
            }
            return builder.ToString();
        }

        // 罗马数字（1→i…；≥4000 按浏览器口径重复 m 前缀，不做过线记法）；≤0 回退阿拉伯
        public static string FormatRomanSequence(int number, bool upper) {
            if (number < 1) {
                return number.ToString();
            }
            (int Value, string Symbol)[] pairs = [
                (1000, "m"), (900, "cm"), (500, "d"), (400, "cd"), (100, "c"), (90, "xc"),
                (50, "l"), (40, "xl"), (10, "x"), (9, "ix"), (5, "v"), (4, "iv"), (1, "i")
            ];
            var builder = new System.Text.StringBuilder();
            foreach ((int value, string symbol) in pairs) {
                while (number >= value) {
                    builder.Append(symbol);
                    number -= value;
                }
            }
            return upper ? builder.ToString().ToUpperInvariant() : builder.ToString();
        }
    }
}