using System.Globalization;
using System.Text;
using Engine;

namespace Game {
    // style 属性内联 CSS 子集解析：HtmlAgilityPack 只给原始属性串（无任何 CSS 能力，Fizzler 是
    // 选择器不是声明），这里手写声明切分 + 盒简写 TRBL 展开 + px 长度。定位：只认引擎消费的
    // 属性族（padding/margin、border-*、border-radius 等续用同一入口）；padding/margin 收
    // px/无单位/em/%（em/% 须调用方传上下文基准，TryGetBox emBase/percentBase，缺=维持丢弃）；
    // border-width 仍仅 px/无单位（em/% 不识别）；rem/pt 等其余单位盒属性不支持、含无效值的
    // 声明整条丢弃；缺属性=引擎默认（不造浏览器 UA 默认值表——computed-style 口径留给将来
    // 基类 Padding 版本，本类只报"作者写了什么"）
    static class CssStyleParser {
        // 解析 style 串为声明序列（保序，级联=后声明覆盖前声明）：单遍 O(n) 扫描，';' 切声明、
        // 首个 ':' 切属性值，属性名小写、两侧 trim；扫描时同步剥离 /*…*/ 注释（注释可出现在
        // 任意位置、可含分号，未闭合注释按吃到串尾处理——CSS 规范同款）。空段/无冒号/空名空值
        // 跳过。值内分号（url data-uri 等）不支持——本子集消费的盒属性值不出现分号，留档
        public static List<KeyValuePair<string, string>> ParseDeclarations(string style) {
            List<KeyValuePair<string, string>> declarations = [];
            if (string.IsNullOrEmpty(style)) {
                return declarations;
            }
            StringBuilder declaration = new();
            for (int i = 0; i < style.Length; i++) {
                char c = style[i];
                if (c == '/'
                    && i + 1 < style.Length
                    && style[i + 1] == '*') {
                    int end = style.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    if (end < 0) {
                        break;
                    }
                    i = end + 1;
                    continue;
                }
                if (c == ';') {
                    EmitDeclaration(declaration, declarations);
                    continue;
                }
                declaration.Append(c);
            }
            // 尾段无 ';' 收尾；未闭合注释 break 后同样落到这里（注释前已累积的内容仍生效）
            EmitDeclaration(declaration, declarations);
            return declarations;
        }

        static void EmitDeclaration(StringBuilder text, List<KeyValuePair<string, string>> declarations) {
            string part = text.ToString();
            text.Clear();
            int colon = part.IndexOf(':');
            if (colon <= 0) {
                return;
            }
            string name = part[..colon].Trim().ToLowerInvariant();
            string value = part[(colon + 1)..].Trim();
            if (name.Length == 0
                || value.Length == 0) {
                return;
            }
            declarations.Add(new KeyValuePair<string, string>(name, value));
        }

        // 盒属性查询：prefix="padding"/"margin"，按级联序合并 `prefix` 1~4 值简写与
        // `prefix-left/top/right/bottom` 单边声明（后声明覆盖先声明，CSS 级联口径），展开为四边。
        // 无任何有效相关声明返回 false。简写任一值非法 → 整条简写丢弃（CSS 简写原子性）；
        // 单边非法仅丢该条。输出 Vector4 轴序 = Widget.Margin4（X=左 Y=上 Z=右 W=下）
        // 单位上下文（e632b943 波只收 px/无单位，本波收口）：emBase=em 换算基准
        //（px/1em）、percentBase=% 换算基准（px/100%）——由调用方按元素上下文传（builder 侧
        // tier-1 口径见 BoxLengthContext）；缺上下文（<0）时该单位声明维持丢弃现状
        //（em/% 解析失败同型）。负值接受（px 语义现状，em/% 负值同 acceptance）
        public static bool TryGetBox(string style, string prefix, out Vector4 box, float emBase = -1f, float percentBase = -1f) {
            float? left = null;
            float? top = null;
            float? right = null;
            float? bottom = null;
            foreach (KeyValuePair<string, string> declaration in ParseDeclarations(style)) {
                if (declaration.Key == prefix) {
                    string[] values = declaration.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    // TRBL 展开：1=四边；2=上下 左右；3=上 左右 下；4=上右下左。
                    // 简写原子性：先全部解析进临时量，全成功才写入四边（中途失败整条丢弃，
                    // 无部分生效——审查 P0：先写后析会让非法简写的前缀值泄漏）
                    if (values.Length is < 1 or > 4
                        || !ResolveBoxLength(values[0], emBase, percentBase, out float first)) {
                        continue;
                    }
                    float newTop = first;
                    float newRight = first;
                    float newBottom = first;
                    float newLeft = first;
                    if (values.Length >= 2) {
                        if (!ResolveBoxLength(values[1], emBase, percentBase, out float second)) {
                            continue;
                        }
                        // 两值形：上下=v0、左右=v1（bottom 归 v0——此前误写 v1，探针 padLast MB=32 实证）
                        newBottom = first;
                        newRight = second;
                        newLeft = second;
                    }
                    if (values.Length >= 3) {
                        if (!ResolveBoxLength(values[2], emBase, percentBase, out float third)) {
                            continue;
                        }
                        newBottom = third;
                    }
                    if (values.Length >= 4) {
                        if (!ResolveBoxLength(values[3], emBase, percentBase, out float fourth)) {
                            continue;
                        }
                        newLeft = fourth;
                    }
                    top = newTop;
                    right = newRight;
                    bottom = newBottom;
                    left = newLeft;
                    continue;
                }
                // 单边声明：无效值仅丢该条（不影响已展开的简写值）
                if (declaration.Key == prefix + "-left"
                    && ResolveBoxLength(declaration.Value, emBase, percentBase, out float l)) {
                    left = l;
                }
                else if (declaration.Key == prefix + "-top"
                    && ResolveBoxLength(declaration.Value, emBase, percentBase, out float t)) {
                    top = t;
                }
                else if (declaration.Key == prefix + "-right"
                    && ResolveBoxLength(declaration.Value, emBase, percentBase, out float r)) {
                    right = r;
                }
                else if (declaration.Key == prefix + "-bottom"
                    && ResolveBoxLength(declaration.Value, emBase, percentBase, out float b)) {
                    bottom = b;
                }
            }
            box = new Vector4(left ?? 0f, top ?? 0f, right ?? 0f, bottom ?? 0f);
            return left.HasValue || top.HasValue || right.HasValue || bottom.HasValue;
        }

        // 盒边长度解析（TryParseLengthUnit + 单位换算）：px/无单位→原值（不依赖上下文）；
        // em→原值×emBase、%→原值×percentBase/100；对应基准缺上下文（<0）→该值无效
        //（=既有"em/% 解析失败丢弃"口径，单位恒等）；rem/pt 等其他单位 TryParseLengthUnit false。
        // em/% 换算后非有限（1e38em 级溢出→Infinity）按解析失败丢边，Infinity/NaN 不进布局
        static bool ResolveBoxLength(string value, float emBase, float percentBase, out float px) {
            if (!TryParseLengthUnit(value, out float number, out bool isPercent, out bool isEm)) {
                px = 0f;
                return false;
            }
            if (isEm) {
                if (emBase < 0f) {
                    px = 0f;
                    return false;
                }
                px = number * emBase;
            }
            else if (isPercent) {
                if (percentBase < 0f) {
                    px = 0f;
                    return false;
                }
                px = number * percentBase / 100f;
            }
            else {
                px = number;
                return true;
            }
            if (!float.IsFinite(px)) {
                px = 0f;
                return false;
            }
            return true;
        }

        // 单属性声明查询：级联序取最后一条同名声明（后声明覆盖先声明）。无该属性返回 false。
        // 口径留档：取"最后一条声明"而非"最后一条有效声明"（TryGetBox 是后者）——无效值由
        // 消费方丢弃且不回退前条，与 CSS"解析期丢弃无效声明"有差（子集取舍）。
        // 与 TryGetBox/TryGetBorder 同为逐属性重扫口径——style 串短、调用点少，O(n) 每属性可接受
        public static bool TryGetDeclaration(string style, string name, out string value) {
            value = null;
            List<KeyValuePair<string, string>> declarations = ParseDeclarations(style);
            for (int i = declarations.Count - 1; i >= 0; i--) {
                if (declarations[i].Key == name) {
                    value = declarations[i].Value;
                    return true;
                }
            }
            return false;
        }

        // 宽度类值解析（width/max-width 共用）：px（大小写不敏感）/无单位→px；%→isPercent=true
        // number=百分数（50% → 50）；em/rem→×16px（引擎无字号上下文，按浏览器根字号定口径）、
        // pt→×4/3 换 px；auto/none/负值/未知→false
        public static bool TryParseWidthValue(string value, out bool isPercent, out float number) {
            isPercent = false;
            number = 0f;
            string v = value.Trim();
            if (v.EndsWith("%", StringComparison.Ordinal)) {
                if (!float.TryParse(v[..^1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float percent)
                    || percent < 0f) {
                    return false;
                }
                isPercent = true;
                number = percent;
                return true;
            }
            float scale = 1f;
            if (v.EndsWith("px", StringComparison.OrdinalIgnoreCase)) {
                v = v[..^2].Trim();
            }
            else if (v.EndsWith("rem", StringComparison.OrdinalIgnoreCase)) {
                v = v[..^3].Trim();
                scale = 16f;
            }
            else if (v.EndsWith("em", StringComparison.OrdinalIgnoreCase)) {
                v = v[..^2].Trim();
                scale = 16f;
            }
            else if (v.EndsWith("pt", StringComparison.OrdinalIgnoreCase)) {
                v = v[..^2].Trim();
                scale = 4f / 3f;
            }
            if (!float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out number)
                || number < 0f) {
                number = 0f;
                return false;
            }
            number *= scale;
            return true;
        }

        // 长度：px（大小写不敏感）/无单位（=px）；em/%/rem/pt 等不支持返回 false。负值接受——
        // 引擎布局是浮点加减，负 margin=间距缩小（CSS 同义）。单位感知版见 TryParseLengthUnit
        //（TryGetBox 盒属性 em/% 收口用）；border-width 族仍走本入口（px-only 口径不变）
        public static bool TryParseLength(string value, out float px) {
            if (TryParseLengthUnit(value, out float number, out bool isPercent, out bool isEm)
                && !isPercent
                && !isEm) {
                px = number;
                return true;
            }
            px = 0f;
            return false;
        }

        // 长度+单位解析（TryParseLength 的单位感知版，TryGetBox 盒边换算前置）：px/无单位→
        // number 原值；em→isEm=true、%→isPercent=true，number=原始数值（换算由调用方按上下文
        // 基准做——em 基准随元素 font-size tier-1 修正，% 基准=包含块近似，见 builder
        // BoxLengthContext）；rem/pt 等其他单位→false。负值接受（px 语义现状 e632b943 裁定，
        // em/% 负值同 acceptance）
        public static bool TryParseLengthUnit(string value, out float number, out bool isPercent, out bool isEm) {
            number = 0f;
            isPercent = false;
            isEm = false;
            string v = value.Trim();
            if (v.EndsWith("%", StringComparison.Ordinal)) {
                if (!float.TryParse(v[..^1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out number)) {
                    number = 0f;
                    return false;
                }
                isPercent = true;
                return true;
            }
            if (v.EndsWith("px", StringComparison.OrdinalIgnoreCase)) {
                v = v[..^2].Trim();
            }
            else if (v.EndsWith("rem", StringComparison.OrdinalIgnoreCase)) {
                // rem/pt 等盒属性不支持（本波只收 em/%，留档）
                return false;
            }
            else if (v.EndsWith("em", StringComparison.OrdinalIgnoreCase)) {
                v = v[..^2].Trim();
                isEm = true;
            }
            if (!float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) {
                number = 0f;
                return false;
            }
            return true;
        }

        // border 解析结果：Widths 轴序=X左 Y上 Z右 W下（同 Margin4），0=该边未启用；
        // 四色 null=currentColor（调用方按继承文字色解析）。子集启用口径（用户裁定 A，从宽）：
        // 该边出现任一有效组件（width/color/非 none 的 style 关键字）即启用，缺 width 取
        // medium=3px；仅 none/hidden（简写或 border-style）禁用该边
        public class CssBorderSpec {
            public Vector4 Widths;

            public Color? Left, Top, Right, Bottom;

            public bool HasAny => Widths.X > 0f || Widths.Y > 0f || Widths.Z > 0f || Widths.W > 0f;
        }

        // CSS medium 线宽（浏览器缺省 border-width）
        const float BorderMediumWidth = 3f;

        // style 关键字：本波搁置 -style 不识别线型，只吞掉并按"启用"计；none/hidden=禁用
        static readonly string[] BorderStyleKeywords = [
            "solid",
            "dotted",
            "dashed",
            "double",
            "groove",
            "ridge",
            "inset",
            "outset"
        ];

        // border 组解析：border / border-{side} 简写（width/color/style 任意序混排）+
        // border-width / border-color（TRBL）+ border-{side}-width / -color 单边单分量 +
        // border-style / border-{side}-style。返回=是否出现任何有效 border 声明（含全禁：
        // 内层 border:none 借此替换掉继承边框）；是否画查 spec.HasAny。启用口径（用户裁定 A，
        // 从宽）：该边出现任一有效组件（width/color/非 none 的 style 关键字）即启用，缺 width
        // 取 medium=3px；仅 none/hidden（简写或 border-style）禁用该边。非法 width（负值/em/%）
        // 或未知 token 丢弃所在声明（不污染其他边）
        public static bool TryGetBorder(string style, out CssBorderSpec spec) {
            spec = new CssBorderSpec();
            bool hasDeclarations = false;
            if (string.IsNullOrEmpty(style)) {
                return false;
            }
            // 侧序沿用 CSS TRBL：0=上 1=右 2=下 3=左（展开/输出时再映射 Margin4 轴序）
            float?[] widths = new float?[4];
            Color?[] colors = new Color?[4];
            bool[] enabled = new bool[4];
            foreach (KeyValuePair<string, string> declaration in ParseDeclarations(style)) {
                string key = declaration.Key;
                string value = declaration.Value;
                if (key == "border") {
                    hasDeclarations |= ApplyBorderShorthand(value, widths, colors, enabled, true, -1);
                }
                else if (key is "border-top" or "border-right" or "border-bottom" or "border-left") {
                    hasDeclarations |= ApplyBorderShorthand(value, widths, colors, enabled, false, SideIndex(key["border-".Length..]));
                }
                else if (key == "border-width") {
                    hasDeclarations |= ApplyBorderWidths(value, widths, enabled);
                }
                else if (key == "border-color") {
                    hasDeclarations |= ApplyBorderColors(value, colors, enabled);
                }
                else if (key is "border-top-width" or "border-right-width" or "border-bottom-width" or "border-left-width") {
                    if (TryParseLength(value, out float w)
                        && w >= 0f) {
                        int side = SideIndex(key["border-".Length..^"-width".Length]);
                        widths[side] = w;
                        enabled[side] = true;
                        hasDeclarations = true;
                    }
                }
                else if (key is "border-top-color" or "border-right-color" or "border-bottom-color" or "border-left-color") {
                    if (CssColorParser.TryParse(value, out Color c)) {
                        int side = SideIndex(key["border-".Length..^"-color".Length]);
                        colors[side] = c;
                        enabled[side] = true;
                        hasDeclarations = true;
                    }
                }
                else if (key == "border-style"
                    || key is "border-top-style" or "border-right-style" or "border-bottom-style" or "border-left-style") {
                    // border-style 支持 1~4 值 TRBL；单边形式值域退化为单值。none/hidden 禁用，
                    // 其余合法线型关键字启用该边（线型本身搁置不识别，裁定 A）
                    string[] tokens = SplitTopLevelTokens(value);
                    if (tokens.Length is < 1 or > 4) {
                        continue;
                    }
                    string[] expanded = ExpandTrbl(tokens);
                    int side = key == "border-style" ? -1 : SideIndex(key["border-".Length..^"-style".Length]);
                    bool anyEdge = false;
                    for (int i = 0; i < 4; i++) {
                        if (side >= 0
                            && side != i) {
                            continue;
                        }
                        string token = expanded[i].ToLowerInvariant();
                        if (token is "none" or "hidden") {
                            widths[i] = null;
                            colors[i] = null;
                            enabled[i] = false;
                            anyEdge = true;
                        }
                        else if (BorderStyleKeywords.Contains(token)) {
                            enabled[i] = true;
                            anyEdge = true;
                        }
                    }
                    hasDeclarations |= anyEdge;
                }
            }
            for (int i = 0; i < 4; i++) {
                float w = widths[i] ?? (enabled[i] ? BorderMediumWidth : 0f);
                // Margin4 轴序输出：X=左(3) Y=上(0) Z=右(1) W=下(2)
                switch (i) {
                    case 0:
                        spec.Widths.Y = w;
                        spec.Top = colors[i];
                        break;
                    case 1:
                        spec.Widths.Z = w;
                        spec.Right = colors[i];
                        break;
                    case 2:
                        spec.Widths.W = w;
                        spec.Bottom = colors[i];
                        break;
                    case 3:
                        spec.Widths.X = w;
                        spec.Left = colors[i];
                        break;
                }
            }
            return hasDeclarations;
        }

        static int SideIndex(string side) => side switch {
            "top" => 0,
            "right" => 1,
            "bottom" => 2,
            _ => 3
        };

        // TRBL 1~4 值展开：1=四边；2=上下 左右；3=上 左右 下；4=上右下左
        static string[] ExpandTrbl(string[] tokens) => tokens.Length switch {
            1 => [tokens[0], tokens[0], tokens[0], tokens[0]],
            2 => [tokens[0], tokens[1], tokens[0], tokens[1]],
            3 => [tokens[0], tokens[1], tokens[2], tokens[1]],
            _ => tokens
        };

        // border-radius 解析（角展开序与 ExpandTrbl 的边展开同构：2 值=TL/BR + TR/BL、
        // 3 值=TL + TR/BL + BR，正合 CSS 角口径，复用）。出四角 px 愿望值 + 四角百分数
        //（50=50%），轴序=CSS 角序 (左上,右上,右下,左下)，非 Margin4 边序。单值 %→percents
        // 对应位（px 位 0）、px/em/rem/pt→px（% 位 0），两 Vector4 各存各的（支持部分角 % 部分
        // 角 px 混合；% 的 px 值 Draw 期按 min(w,h) 解析）。斜杠椭圆语法（h / v）只取 '/' 前半
        //（水平半径），垂直半径搁置留档。任一值负/非法 → 整条 false（CSS 简写原子性）；无声明
        // false；全 0 合法（=直角，语义恒等无半径）。取最后一条声明（TryGetDeclaration 级联口径）
        public static bool TryParseBorderRadius(string style, out Vector4 corners, out Vector4 percents) {
            corners = default;
            percents = default;
            if (!TryGetDeclaration(style, "border-radius", out string value)) {
                return false;
            }
            // 斜杠椭圆语法：'/' 连同后半整体截去（'/ 4px' 与 '/4px' 同型），前半 trim 后照常切分
            int slash = value.IndexOf('/');
            if (slash >= 0) {
                value = value[..slash].Trim();
            }
            string[] tokens = SplitTopLevelTokens(value);
            if (tokens.Length is < 1 or > 4) {
                return false;
            }
            string[] expanded = ExpandTrbl(tokens);
            float[] px = new float[4];
            float[] pct = new float[4];
            for (int i = 0; i < 4; i++) {
                // TryParseWidthValue：%→isPercent+百分数、px/em/rem/pt→px、负/未知→false
                if (!TryParseWidthValue(expanded[i], out bool isPercent, out float number)) {
                    return false;
                }
                if (isPercent) {
                    pct[i] = number;
                }
                else {
                    px[i] = number;
                }
            }
            corners = new Vector4(px[0], px[1], px[2], px[3]);
            percents = new Vector4(pct[0], pct[1], pct[2], pct[3]);
            return true;
        }

        // 简写 token 分类：长度（≥0）→width、颜色→color、线型关键字→启用、none/hidden→禁用、
        // 未知→整条声明丢弃。none/hidden 胜出其余组件（CSS `border: none red` 无边框口径）。
        // 返回=是否有任一边有效写入
        static bool ApplyBorderShorthand(string value, float?[] widths, Color?[] colors, bool[] enabled, bool allSides, int side) {
            float? width = null;
            Color? color = null;
            bool styleOn = false;
            bool styleOff = false;
            foreach (string token in SplitTopLevelTokens(value)) {
                if (TryParseLength(token, out float w)
                    && w >= 0f) {
                    width = w;
                }
                else if (CssColorParser.TryParse(token, out Color c)) {
                    color = c;
                }
                else if (BorderStyleKeywords.Contains(token.ToLowerInvariant())) {
                    styleOn = true;
                }
                else if (token.Equals("none", StringComparison.OrdinalIgnoreCase)
                    || token.Equals("hidden", StringComparison.OrdinalIgnoreCase)) {
                    styleOff = true;
                }
                else {
                    return false;
                }
            }
            bool any = false;
            for (int i = 0; i < 4; i++) {
                if (!allSides
                    && side != i) {
                    continue;
                }
                if (styleOff) {
                    widths[i] = null;
                    colors[i] = null;
                    enabled[i] = false;
                    any = true;
                }
                else {
                    // 任一有效组件即启用（裁定 A）：仅 color 也启用（缺 width 取 medium）
                    if (width.HasValue) {
                        widths[i] = width.Value;
                    }
                    if (color.HasValue) {
                        colors[i] = color.Value;
                    }
                    if (width.HasValue
                        || color.HasValue
                        || styleOn) {
                        enabled[i] = true;
                    }
                    any |= width.HasValue || color.HasValue || styleOn;
                }
            }
            return any;
        }

        static bool ApplyBorderWidths(string value, float?[] widths, bool[] enabled) {
            string[] tokens = SplitTopLevelTokens(value);
            // 任一 token 非法整条丢弃（CSS 口径，避免 TRBL 错位吃值）
            if (tokens.Length is < 1 or > 4
                || !tokens.All(t => TryParseLength(t, out float w) && w >= 0f)) {
                return false;
            }
            string[] expanded = ExpandTrbl(tokens);
            for (int i = 0; i < 4; i++) {
                if (TryParseLength(expanded[i], out float w)
                    && w >= 0f) {
                    widths[i] = w;
                    enabled[i] = true;
                }
            }
            return true;
        }

        static bool ApplyBorderColors(string value, Color?[] colors, bool[] enabled) {
            string[] tokens = SplitTopLevelTokens(value);
            // 任一 token 非法整条丢弃（CSS 口径，避免 TRBL 错位吃值）
            if (tokens.Length is < 1 or > 4
                || !tokens.All(t => CssColorParser.TryParse(t, out _))) {
                return false;
            }
            string[] expanded = ExpandTrbl(tokens);
            for (int i = 0; i < 4; i++) {
                if (CssColorParser.TryParse(expanded[i], out Color c)) {
                    colors[i] = c;
                    // 仅 color 声明也启用该边（裁定 A，缺 width 取 medium）
                    enabled[i] = true;
                }
            }
            return true;
        }

        // 括号感知 token 切分：rgb(1, 2, 3) 等函数色内含空格，按深度跳过括号内空白
        static string[] SplitTopLevelTokens(string value) {
            List<string> tokens = [];
            int depth = 0;
            int start = -1;
            for (int i = 0; i < value.Length; i++) {
                char c = value[i];
                if (c == '(') {
                    depth++;
                }
                else if (c == ')') {
                    depth = MathUtils.Max(depth - 1, 0);
                }
                if (depth == 0
                    && char.IsWhiteSpace(c)) {
                    if (start >= 0) {
                        tokens.Add(value[start..i]);
                        start = -1;
                    }
                }
                else if (start < 0) {
                    start = i;
                }
            }
            if (start >= 0) {
                tokens.Add(value[start..]);
            }
            return [.. tokens];
        }
    }
}