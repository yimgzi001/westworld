using System.Globalization;
using Engine;

namespace Game {
    // transform 函数种类（v1 子集：平移/缩放/旋转三族）
    public enum CssTransformFuncKind {
        Translate,
        Scale,
        Rotate
    }

    // 单条 transform 函数（CSS 声明序保序，复合列表元素）。每参 (值, 是否百分数)：
    // translate 的 percent 相对盒尺寸（Arrange 期 ActualSize 解析）；Rotate 恒 X=角度（度，
    // deg/rad 归一）、Y 未用；scale 恒无单位数。public 同 HtmlStyleBoxWidget 壳属性口径：
    // 探针/宿主读壳断言用（解析器本体 internal）
    public class CssTransformFunc {
        public CssTransformFuncKind Kind;

        public (float Value, bool IsPercent) X;

        public (float Value, bool IsPercent) Y;
    }

    // CSS transform / transform-origin 解析（style 内联子集，同 CssStyleParser 定位：只认引擎
    // 消费的函数族）。复合列表按序解析保序（CSS transform 复合语义）；不识别函数族
    //（matrix/skew/3d 系列/透视图）解析到即整条 false——不支持整体拒、防半解析错观感；
    // 空/none 也 false。长度单位口径同 TryParseWidthValue：px 直读、em/rem×16（引擎无字号
    // 上下文，按浏览器根字号定口径）、% 存比例愿望 Arrange 期解析、无单位数从宽当 px
    static class CssTransformParser {
        // transform 复合列表解析：逐个 name(args) 函数（大小写不敏感，函数间空白分隔，
        // 参数逗号或空白分隔均可）。任一函数不识别/参数非法 → 整条 false（无部分生效）
        public static bool TryParseTransform(string value, out List<CssTransformFunc> funcs) {
            funcs = null;
            string v = value?.Trim();
            if (string.IsNullOrEmpty(v)
                || v.Equals("none", StringComparison.OrdinalIgnoreCase)) {
                return false;
            }
            List<CssTransformFunc> result = [];
            int pos = 0;
            while (pos < v.Length) {
                if (char.IsWhiteSpace(v[pos])) {
                    pos++;
                    continue;
                }
                int open = v.IndexOf('(', pos);
                if (open < 0) {
                    return false;
                }
                string name = v[pos..open].Trim().ToLowerInvariant();
                int close = v.IndexOf(')', open + 1);
                if (close < 0
                    || name.Length == 0) {
                    return false;
                }
                string[] args = SplitArgs(v[(open + 1)..close]);
                if (!TryParseFunc(name, args, out CssTransformFunc func)) {
                    return false;
                }
                result.Add(func);
                pos = close + 1;
            }
            if (result.Count == 0) {
                return false;
            }
            funcs = result;
            return true;
        }

        // 单函数解析：translate(x[,y])/translateX(x)/translateY(y)、scale(s[,sy])/scaleX/scaleY
        //（0 拒——scale(0) 塌缩不可见，按无效值丢弃口径）、rotate(a)（deg/rad，负角允许）。
        // 其余函数名一律 false（整条拒，见类注）
        static bool TryParseFunc(string name, string[] args, out CssTransformFunc func) {
            func = null;
            switch (name) {
                case "translate":
                    if (args.Length is < 1 or > 2
                        || !TryParseLengthOrPercent(args[0], out (float, bool) x)) {
                        return false;
                    }
                    (float, bool) y = (0f, false);
                    if (args.Length == 2
                        && !TryParseLengthOrPercent(args[1], out y)) {
                        return false;
                    }
                    func = new CssTransformFunc { Kind = CssTransformFuncKind.Translate, X = x, Y = y };
                    return true;
                case "translatex":
                    if (args.Length != 1
                        || !TryParseLengthOrPercent(args[0], out (float, bool) tx)) {
                        return false;
                    }
                    func = new CssTransformFunc { Kind = CssTransformFuncKind.Translate, X = tx };
                    return true;
                case "translatey":
                    if (args.Length != 1
                        || !TryParseLengthOrPercent(args[0], out (float, bool) ty)) {
                        return false;
                    }
                    func = new CssTransformFunc { Kind = CssTransformFuncKind.Translate, Y = ty };
                    return true;
                case "scale":
                    if (args.Length is < 1 or > 2
                        || !TryParseUnitless(args[0], out float sx)
                        || !IsUsableScale(sx)) {
                        return false;
                    }
                    float sy = sx;
                    if (args.Length == 2
                        && (!TryParseUnitless(args[1], out sy) || !IsUsableScale(sy))) {
                        return false;
                    }
                    func = new CssTransformFunc { Kind = CssTransformFuncKind.Scale, X = (sx, false), Y = (sy, false) };
                    return true;
                case "scalex":
                    if (args.Length != 1
                        || !TryParseUnitless(args[0], out float sxOnly)
                        || !IsUsableScale(sxOnly)) {
                        return false;
                    }
                    func = new CssTransformFunc { Kind = CssTransformFuncKind.Scale, X = (sxOnly, false), Y = (1f, false) };
                    return true;
                case "scaley":
                    if (args.Length != 1
                        || !TryParseUnitless(args[0], out float syOnly)
                        || !IsUsableScale(syOnly)) {
                        return false;
                    }
                    func = new CssTransformFunc { Kind = CssTransformFuncKind.Scale, X = (1f, false), Y = (syOnly, false) };
                    return true;
                case "rotate":
                    if (args.Length != 1
                        || !TryParseAngleDegrees(args[0], out float degrees)) {
                        return false;
                    }
                    func = new CssTransformFunc { Kind = CssTransformFuncKind.Rotate, X = (degrees, false) };
                    return true;
                default:
                    // matrix/matrix3d/skew/skewX/skewY/translate3d/scale3d/rotate3d/perspective
                    // 等不识别：整条 false（不支持整体拒）
                    return false;
            }
        }

        static bool IsUsableScale(float s) => float.IsFinite(s) && s != 0f;

        // 平移参数：%/px/em/rem/无单位数（负值允许）。% 存百分数愿望（50% → 50），px 类归一 px。
        // ∞/NaN 拒（IsUsableScale 同口径；.NET TryParse 接受 "Infinity" 字面量，须显式设防）
        static bool TryParseLengthOrPercent(string token, out (float Value, bool IsPercent) part) {
            part = (0f, false);
            string v = token.Trim();
            if (v.Length == 0) {
                return false;
            }
            if (v.EndsWith("%", StringComparison.Ordinal)) {
                if (!float.TryParse(v[..^1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float percent)
                    || !float.IsFinite(percent)) {
                    return false;
                }
                part = (percent, true);
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
            if (!float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float px)) {
                return false;
            }
            // 有限数×16 仍可溢出 ∞：乘完再验
            float value = px * scale;
            if (!float.IsFinite(value)) {
                return false;
            }
            part = (value, false);
            return true;
        }

        // 缩放参数：无单位数（大小写敏感无关，纯数字），负值允许（镜像）
        static bool TryParseUnitless(string token, out float number) => float.TryParse(
            token.Trim(),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out number
        );

        // 角度：deg/rad（大小写不敏感）归一为度；裸数从宽只认 0 的 CSS 合法形（rotate(0)），
        // 非零裸数拒（CSS 要求非零角带单位）。尾部 IsFinite 设防（"Infinity" 字面量可过
        // TryParse；NaN/∞ 一并拒）
        static bool TryParseAngleDegrees(string token, out float degrees) {
            degrees = 0f;
            string v = token.Trim();
            bool hasUnit = false;
            float scale = 1f;
            if (v.EndsWith("deg", StringComparison.OrdinalIgnoreCase)) {
                v = v[..^3].Trim();
                hasUnit = true;
            }
            else if (v.EndsWith("rad", StringComparison.OrdinalIgnoreCase)) {
                v = v[..^3].Trim();
                scale = 180f / MathF.PI;
                hasUnit = true;
            }
            if (!float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float number)) {
                return false;
            }
            if (!hasUnit
                && number != 0f) {
                return false;
            }
            degrees = number * scale;
            return float.IsFinite(degrees);
        }

        // 函数参数切分：逗号或空白均作分隔（CSS transform 函数两形并存），空段跳过
        static string[] SplitArgs(string text) {
            List<string> args = [];
            int start = -1;
            for (int i = 0; i < text.Length; i++) {
                char c = text[i];
                if (c == ','
                    || char.IsWhiteSpace(c)) {
                    if (start >= 0) {
                        args.Add(text[start..i]);
                        start = -1;
                    }
                }
                else if (start < 0) {
                    start = i;
                }
            }
            if (start >= 0) {
                args.Add(text[start..]);
            }
            return [.. args];
        }

        // origin 分量槽位定向（CSS 关键字定轴）：仅水平（left/right）、仅垂直（top/bottom）、
        // 轴无涉（center/%/长度）
        const int OriginAxisHorizontal = 0;

        const int OriginAxisVertical = 1;

        const int OriginAxisAny = -1;

        // transform-origin 解析：1~2 分量（第三分量起忽略——z 轴超范围，2D 壳无意义）。
        // 关键字 left=0/right=100%、top=0/bottom=100%、center=50%；%→比例愿望（100%→1.0）；
        // px/em/rem/无单位数→绝对 px。槽位分配按 CSS 口径：left/right 恒入水平槽、top/bottom
        // 恒入垂直槽（单值 top=(50%,0) 而非 (0,50%)；top center/center top 双关键字交换序同归
        // (50%,0)），轴无涉值（center/%/长度）按序填首个空位（1 值=x、2 值=x/y）；空位缺省
        // center。输出编码：比例分量存 ratios、px 分量存 px（另一路置 0）——消费方
        // origin=ratios×盒尺寸+px 逐分量加和即得，混合声明（25% 10px）同式成立。
        // 解析失败/无声明 → false（调用方回落默认 50%,50%，不拒整条 transform）
        public static bool TryParseTransformOrigin(string value, out Vector2 ratios, out Vector2 px) {
            ratios = default;
            px = default;
            string[] tokens = (value ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 1) {
                return false;
            }
            (float Value, bool IsRatio, int Axis)? x = null;
            (float Value, bool IsRatio, int Axis)? y = null;
            int count = Math.Min(tokens.Length, 2);
            for (int i = 0; i < count; i++) {
                if (!TryParseOriginComponent(tokens[i], out (float Value, bool IsRatio, int Axis) part)) {
                    return false;
                }
                if (part.Axis == OriginAxisHorizontal) {
                    if (x.HasValue) {
                        return false;
                    }
                    x = part;
                }
                else if (part.Axis == OriginAxisVertical) {
                    if (y.HasValue) {
                        return false;
                    }
                    y = part;
                }
                else {
                    if (!x.HasValue) {
                        x = part;
                    }
                    else if (!y.HasValue) {
                        y = part;
                    }
                    else {
                        return false;
                    }
                }
            }
            (float Value, bool IsRatio, int Axis) xv = x ?? (0.5f, true, OriginAxisAny);
            (float Value, bool IsRatio, int Axis) yv = y ?? (0.5f, true, OriginAxisAny);
            ratios = new Vector2(xv.IsRatio ? xv.Value : 0f, yv.IsRatio ? yv.Value : 0f);
            px = new Vector2(xv.IsRatio ? 0f : xv.Value, yv.IsRatio ? 0f : yv.Value);
            return true;
        }

        // 单分量：left/top→0、right/bottom→100%、center→50%（比例）；%→比例（÷100）；
        // 长度→px（em/rem×16 同 translate 口径）。Axis=槽位定向（见槽位常量）
        static bool TryParseOriginComponent(string token, out (float Value, bool IsRatio, int Axis) part) {
            part = (0f, true, OriginAxisAny);
            string v = token.Trim().ToLowerInvariant();
            switch (v) {
                case "left":
                    part = (0f, true, OriginAxisHorizontal);
                    return true;
                case "right":
                    part = (1f, true, OriginAxisHorizontal);
                    return true;
                case "top":
                    part = (0f, true, OriginAxisVertical);
                    return true;
                case "bottom":
                    part = (1f, true, OriginAxisVertical);
                    return true;
                case "center":
                    part = (0.5f, true, OriginAxisAny);
                    return true;
            }
            if (v.EndsWith("%", StringComparison.Ordinal)) {
                if (!float.TryParse(v[..^1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float percent)
                    || !float.IsFinite(percent)) {
                    return false;
                }
                part = (percent / 100f, true, OriginAxisAny);
                return true;
            }
            if (!TryParseLengthOrPercent(v, out (float Value, bool IsPercent) length)
                || length.IsPercent) {
                return false;
            }
            part = (length.Value, false, OriginAxisAny);
            return true;
        }
    }
}