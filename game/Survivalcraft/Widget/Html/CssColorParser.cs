using System.Globalization;
using Engine;

namespace Game {
    // CSS 颜色值解析（HTML 旧属性 color/bgcolor 专用；style 属性不在本类范围）：
    // #hex（#rgb/#rgba/#rrggbb/#rrggbbaa）、rgb()/rgba()/hsl()/hwb()/lab()/lch()/oklab()/oklch()、
    // 命名色全表（MDN named-color 页 140 项，含 grey 系与 cyan/magenta 别名）+ transparent。
    // 函数语法新旧双认：legacy 逗号分隔（rgba(255,0,0,0.5)）与新式空格 + `/ alpha`（rgb(255 0 0 / 50%)）；
    // 角度单位 deg/grad/rad/turn，无单位按 deg。无插值（color-mix 等不做）；lab/lch/oklab/oklch
    // 广色域直转 sRGB 后逐通道钳制（浏览器 gamut mapping 的最简近似）
    public static class CssColorParser {
        // 命名色表（MDN 全表手抄，十六进制逐项核对）：小写键，查表前 ToLowerInvariant
        static readonly Dictionary<string, Color> s_namedColors = new(StringComparer.Ordinal) {
            ["aliceblue"] = new Color(240, 248, 255),
            ["antiquewhite"] = new Color(250, 235, 215),
            ["aqua"] = new Color(0, 255, 255),
            ["aquamarine"] = new Color(127, 255, 212),
            ["azure"] = new Color(240, 255, 255),
            ["beige"] = new Color(245, 245, 220),
            ["bisque"] = new Color(255, 228, 196),
            ["black"] = new Color(0, 0, 0),
            ["blanchedalmond"] = new Color(255, 235, 205),
            ["blue"] = new Color(0, 0, 255),
            ["blueviolet"] = new Color(138, 43, 226),
            ["brown"] = new Color(165, 42, 42),
            ["burlywood"] = new Color(222, 184, 135),
            ["cadetblue"] = new Color(95, 158, 160),
            ["chartreuse"] = new Color(127, 255, 0),
            ["chocolate"] = new Color(210, 105, 30),
            ["coral"] = new Color(255, 127, 80),
            ["cornflowerblue"] = new Color(100, 149, 237),
            ["cornsilk"] = new Color(255, 248, 220),
            ["crimson"] = new Color(220, 20, 60),
            ["cyan"] = new Color(0, 255, 255),
            ["darkblue"] = new Color(0, 0, 139),
            ["darkcyan"] = new Color(0, 139, 139),
            ["darkgoldenrod"] = new Color(184, 134, 11),
            ["darkgray"] = new Color(169, 169, 169),
            ["darkgreen"] = new Color(0, 100, 0),
            ["darkgrey"] = new Color(169, 169, 169),
            ["darkkhaki"] = new Color(189, 183, 107),
            ["darkmagenta"] = new Color(139, 0, 139),
            ["darkolivegreen"] = new Color(85, 107, 47),
            ["darkorange"] = new Color(255, 140, 0),
            ["darkorchid"] = new Color(153, 50, 204),
            ["darkred"] = new Color(139, 0, 0),
            ["darksalmon"] = new Color(233, 150, 122),
            ["darkseagreen"] = new Color(143, 188, 143),
            ["darkslateblue"] = new Color(72, 61, 139),
            ["darkslategray"] = new Color(47, 79, 79),
            ["darkslategrey"] = new Color(47, 79, 79),
            ["darkturquoise"] = new Color(0, 206, 209),
            ["darkviolet"] = new Color(148, 0, 211),
            ["deeppink"] = new Color(255, 20, 147),
            ["deepskyblue"] = new Color(0, 191, 255),
            ["dimgray"] = new Color(105, 105, 105),
            ["dimgrey"] = new Color(105, 105, 105),
            ["dodgerblue"] = new Color(30, 144, 255),
            ["firebrick"] = new Color(178, 34, 34),
            ["floralwhite"] = new Color(255, 250, 240),
            ["forestgreen"] = new Color(34, 139, 34),
            ["fuchsia"] = new Color(255, 0, 255),
            ["gainsboro"] = new Color(220, 220, 220),
            ["ghostwhite"] = new Color(248, 248, 255),
            ["gold"] = new Color(255, 215, 0),
            ["goldenrod"] = new Color(218, 165, 32),
            ["gray"] = new Color(128, 128, 128),
            ["green"] = new Color(0, 128, 0),
            ["greenyellow"] = new Color(173, 255, 47),
            ["grey"] = new Color(128, 128, 128),
            ["honeydew"] = new Color(240, 255, 240),
            ["hotpink"] = new Color(255, 105, 180),
            ["indianred"] = new Color(205, 92, 92),
            ["indigo"] = new Color(75, 0, 130),
            ["ivory"] = new Color(255, 255, 240),
            ["khaki"] = new Color(240, 230, 140),
            ["lavender"] = new Color(230, 230, 250),
            ["lavenderblush"] = new Color(255, 240, 245),
            ["lawngreen"] = new Color(124, 252, 0),
            ["lemonchiffon"] = new Color(255, 250, 205),
            ["lightblue"] = new Color(173, 216, 230),
            ["lightcoral"] = new Color(240, 128, 128),
            ["lightcyan"] = new Color(224, 255, 255),
            ["lightgoldenrodyellow"] = new Color(250, 250, 210),
            ["lightgray"] = new Color(211, 211, 211),
            ["lightgreen"] = new Color(144, 238, 144),
            ["lightgrey"] = new Color(211, 211, 211),
            ["lightpink"] = new Color(255, 182, 193),
            ["lightsalmon"] = new Color(255, 160, 122),
            ["lightseagreen"] = new Color(32, 178, 170),
            ["lightskyblue"] = new Color(135, 206, 250),
            ["lightslategray"] = new Color(119, 136, 153),
            ["lightslategrey"] = new Color(119, 136, 153),
            ["lightsteelblue"] = new Color(176, 196, 222),
            ["lightyellow"] = new Color(255, 255, 224),
            ["lime"] = new Color(0, 255, 0),
            ["limegreen"] = new Color(50, 205, 50),
            ["linen"] = new Color(250, 240, 230),
            ["magenta"] = new Color(255, 0, 255),
            ["maroon"] = new Color(128, 0, 0),
            ["mediumaquamarine"] = new Color(102, 205, 170),
            ["mediumblue"] = new Color(0, 0, 205),
            ["mediumorchid"] = new Color(186, 85, 211),
            ["mediumpurple"] = new Color(147, 112, 219),
            ["mediumseagreen"] = new Color(60, 179, 113),
            ["mediumslateblue"] = new Color(123, 104, 238),
            ["mediumspringgreen"] = new Color(0, 250, 154),
            ["mediumturquoise"] = new Color(72, 209, 204),
            ["mediumvioletred"] = new Color(199, 21, 133),
            ["midnightblue"] = new Color(25, 25, 112),
            ["mintcream"] = new Color(245, 255, 250),
            ["mistyrose"] = new Color(255, 228, 225),
            ["moccasin"] = new Color(255, 228, 181),
            ["navajowhite"] = new Color(255, 222, 173),
            ["navy"] = new Color(0, 0, 128),
            ["oldlace"] = new Color(253, 245, 230),
            ["olive"] = new Color(128, 128, 0),
            ["olivedrab"] = new Color(107, 142, 35),
            ["orange"] = new Color(255, 165, 0),
            ["orangered"] = new Color(255, 69, 0),
            ["orchid"] = new Color(218, 112, 214),
            ["palegoldenrod"] = new Color(238, 232, 170),
            ["palegreen"] = new Color(152, 251, 152),
            ["paleturquoise"] = new Color(175, 238, 238),
            ["palevioletred"] = new Color(219, 112, 147),
            ["papayawhip"] = new Color(255, 239, 213),
            ["peachpuff"] = new Color(255, 218, 185),
            ["peru"] = new Color(205, 133, 63),
            ["pink"] = new Color(255, 192, 203),
            ["plum"] = new Color(221, 160, 221),
            ["powderblue"] = new Color(176, 224, 230),
            ["purple"] = new Color(128, 0, 128),
            ["rebeccapurple"] = new Color(102, 51, 153),
            ["red"] = new Color(255, 0, 0),
            ["rosybrown"] = new Color(188, 143, 143),
            ["royalblue"] = new Color(65, 105, 225),
            ["saddlebrown"] = new Color(139, 69, 19),
            ["salmon"] = new Color(250, 128, 114),
            ["sandybrown"] = new Color(244, 164, 96),
            ["seagreen"] = new Color(46, 139, 87),
            ["seashell"] = new Color(255, 245, 238),
            ["sienna"] = new Color(160, 82, 45),
            ["silver"] = new Color(192, 192, 192),
            ["skyblue"] = new Color(135, 206, 235),
            ["slateblue"] = new Color(106, 90, 205),
            ["slategray"] = new Color(112, 128, 144),
            ["slategrey"] = new Color(112, 128, 144),
            ["snow"] = new Color(255, 250, 250),
            ["springgreen"] = new Color(0, 255, 127),
            ["steelblue"] = new Color(70, 130, 180),
            ["tan"] = new Color(210, 180, 140),
            ["teal"] = new Color(0, 128, 128),
            ["thistle"] = new Color(216, 191, 216),
            ["tomato"] = new Color(255, 99, 71),
            ["turquoise"] = new Color(64, 224, 208),
            ["violet"] = new Color(238, 130, 238),
            ["wheat"] = new Color(245, 222, 179),
            ["white"] = new Color(255, 255, 255),
            ["whitesmoke"] = new Color(245, 245, 245),
            ["yellow"] = new Color(255, 255, 0),
            ["yellowgreen"] = new Color(154, 205, 50)
        };

        // 入口：解析失败返回 false（调用方不染色，照旧默认）
        public static bool TryParse(string value, out Color color) {
            color = default;
            if (string.IsNullOrEmpty(value)) {
                return false;
            }
            string trimmed = value.Trim();
            if (trimmed.Length == 0) {
                return false;
            }
            if (trimmed[0] == '#') {
                return TryParseHex(trimmed, out color);
            }
            string lower = trimmed.ToLowerInvariant();
            if (lower == "transparent") {
                color = new Color(0, 0, 0, 0);
                return true;
            }
            // currentColor（随级联上下文变）/system 色族无对应语义，不解析
            int open = lower.IndexOf('(');
            if (open < 0
                || !lower.EndsWith(")")) {
                return s_namedColors.TryGetValue(lower, out color);
            }
            string function = lower[..open].Trim();
            string argumentList = lower[(open + 1)..^1];
            return function switch {
                "rgb" or "rgba" => TryParseRgb(argumentList, out color),
                "hsl" or "hsla" => TryParseHsl(argumentList, out color),
                "hwb" => TryParseHwb(argumentList, out color),
                "lab" => TryParseLab(argumentList, out color, false),
                "lch" => TryParseLch(argumentList, out color, false),
                "oklab" => TryParseLab(argumentList, out color, true),
                "oklch" => TryParseLch(argumentList, out color, true),
                _ => false
            };
        }

        // #rgb/#rgba/#rrggbb/#rrggbbaa（CSS4 短 alpha 写法一并收）
        static bool TryParseHex(string value, out Color color) {
            color = default;
            string hex = value[1..];
            if (hex.Length is not (3 or 4 or 6 or 8)) {
                return false;
            }
            if (hex.Length is 3 or 4) {
                hex = string.Concat(hex.Select(c => new string(c, 2)));
            }
            if (!TryParseHexByte(hex[..2], out int r)
                || !TryParseHexByte(hex.Substring(2, 2), out int g)
                || !TryParseHexByte(hex.Substring(4, 2), out int b)) {
                return false;
            }
            int a = 255;
            if (hex.Length == 8
                && !TryParseHexByte(hex.Substring(6, 2), out a)) {
                return false;
            }
            color = new Color(r, g, b, a);
            return true;
        }

        static bool TryParseHexByte(string text, out int value) =>
            int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);

        // 组件词法：数值 / 百分号 / 角度单位三态。百分号与单位互斥；Unit 空串=无单位
        readonly record struct Component(float Value, bool IsPercent, string Unit);

        static bool TryParseComponent(string token, out Component component) {
            component = default;
            token = token.Trim();
            if (token.Length == 0) {
                return false;
            }
            if (token[^1] == '%') {
                if (!TryParseFloat(token[..^1], out float percent)) {
                    return false;
                }
                component = new Component(percent, true, string.Empty);
                return true;
            }
            foreach (string unit in (string[])["deg", "grad", "rad", "turn"]) {
                if (token.EndsWith(unit, StringComparison.Ordinal)) {
                    if (!TryParseFloat(token[..^unit.Length], out float angle)) {
                        return false;
                    }
                    component = new Component(angle, false, unit);
                    return true;
                }
            }
            if (TryParseFloat(token, out float number)) {
                component = new Component(number, false, null);
                return true;
            }
            return false;
        }

        static bool TryParseFloat(string text, out float value) => float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

        // 实参序列化一：逗号 legacy（rgb(255,0,0,0.5)）与新式空格（rgb(255 0 0 / 50%)）归一为
        // Component 列表 + 可选 alpha。解析失败返回 false
        static bool TryParseArguments(string argumentList, out Component[] components, out Component? alpha) {
            components = [];
            alpha = null;
            string mainPart = argumentList;
            int slash = argumentList.IndexOf('/');
            if (slash >= 0) {
                mainPart = argumentList[..slash];
                string alphaText = argumentList[(slash + 1)..].Trim();
                if (!TryParseComponent(alphaText, out Component alphaComponent)) {
                    return false;
                }
                alpha = alphaComponent;
            }
            string[] tokens = mainPart.Contains(',') ? mainPart.Split(',') : mainPart.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            components = new Component[tokens.Length];
            for (int i = 0; i < tokens.Length; i++) {
                if (!TryParseComponent(tokens[i], out components[i])) {
                    return false;
                }
            }
            // legacy 逗号式的第 4 参是 alpha（rgb(255,0,0,0.5)）
            if (slash < 0
                && mainPart.Contains(',')
                && components.Length == 4) {
                alpha = components[3];
                components = components[..3];
            }
            return components.Length == 3;
        }

        static bool TryParseAlphaValue(Component? alpha, out float a) {
            a = 1f;
            if (alpha == null) {
                return true;
            }
            Component value = alpha.Value;
            float raw = value.Value * (value.IsPercent ? 0.01f : 1f);
            a = MathUtils.Clamp(raw, 0f, 1f);
            return true;
        }

        static bool TryParseRgb(string argumentList, out Color color) {
            color = default;
            if (!TryParseArguments(argumentList, out Component[] components, out Component? alpha)) {
                return false;
            }
            // 通道口径：255 基准；百分号按 2.55×p。CSS none 组件不支持（返回 false 不染色）
            float[] channels = new float[3];
            for (int i = 0; i < 3; i++) {
                channels[i] = MathUtils.Clamp(components[i].Value * (components[i].IsPercent ? 2.55f : 1f), 0f, 255f);
            }
            TryParseAlphaValue(alpha, out float a);
            color = new Color(
                (int)MathF.Round(channels[0]),
                (int)MathF.Round(channels[1]),
                (int)MathF.Round(channels[2]),
                (int)MathF.Round(a * 255f)
            );
            return true;
        }

        // 角度 → 度数（无单位按 deg；deg/grad/rad/turn 四单位）
        static float AngleToDegrees(Component component) {
            return component.Unit switch {
                "grad" => component.Value * 0.9f,
                "rad" => component.Value * 180f / MathF.PI,
                "turn" => component.Value * 360f,
                _ => component.Value
            };
        }

        // HSL → sRGB（标准公式：C=色度、X=第二分量、m=亮度偏移）
        static bool TryParseHsl(string argumentList, out Color color) {
            color = default;
            if (!TryParseArguments(argumentList, out Component[] components, out Component? alpha)) {
                return false;
            }
            float h = NormalizeHue(AngleToDegrees(components[0]));
            // s/l 百分号与裸数值同口径（CSS4 允许 number 直写，值域同为 0-100）
            float s = MathUtils.Clamp(components[1].Value / 100f, 0f, 1f);
            float l = MathUtils.Clamp(components[2].Value / 100f, 0f, 1f);
            Vector3 rgb = HslToRgb(h, s, l);
            TryParseAlphaValue(alpha, out float a);
            color = ToByteColor(rgb, a);
            return true;
        }

        static float NormalizeHue(float degrees) {
            degrees %= 360f;
            return degrees < 0f ? degrees + 360f : degrees;
        }

        static Vector3 HslToRgb(float h, float s, float l) {
            float chroma = (1f - MathF.Abs(2f * l - 1f)) * s;
            float hp = h / 60f;
            float x = chroma * (1f - MathF.Abs(hp % 2f - 1f));
            Vector3 rgb = (int)hp switch {
                0 => new Vector3(chroma, x, 0f),
                1 => new Vector3(x, chroma, 0f),
                2 => new Vector3(0f, chroma, x),
                3 => new Vector3(0f, x, chroma),
                4 => new Vector3(x, 0f, chroma),
                _ => new Vector3(chroma, 0f, x)
            };
            float offset = l - chroma / 2f;
            return rgb + new Vector3(offset);
        }

        // HWB → sRGB：理想纯色（hsl(h,1,0.5)）白混黑混；w+b≥1 退化为灰
        static bool TryParseHwb(string argumentList, out Color color) {
            color = default;
            if (!TryParseArguments(argumentList, out Component[] components, out Component? alpha)) {
                return false;
            }
            float h = NormalizeHue(AngleToDegrees(components[0]));
            float w = MathUtils.Clamp(components[1].Value / 100f, 0f, 1f);
            float b = MathUtils.Clamp(components[2].Value / 100f, 0f, 1f);
            Vector3 rgb;
            if (w + b >= 1f) {
                rgb = new Vector3(w / (w + b));
            }
            else {
                rgb = HslToRgb(h, 1f, 0.5f) * (1f - w - b) + new Vector3(w);
            }
            TryParseAlphaValue(alpha, out float a);
            color = ToByteColor(rgb, a);
            return true;
        }

        // CIE Lab → 线性 sRGB：XYZ(D50) 直接走 CSS Color 4 参考实现的合并矩阵（Bradford 适配
        // 与 sRGB 原色变换合一步）。L 为 0-100 口径（50% 与 50 同值）；oklab 走 Ottosson 矩阵，
        // L 为 0-1 口径（50%=0.5）
        static bool TryParseLab(string argumentList, out Color color, bool oklab) {
            color = default;
            if (!TryParseArguments(argumentList, out Component[] components, out Component? alpha)) {
                return false;
            }
            float l = components[0].Value * (components[0].IsPercent ? oklab ? 0.01f : 1f : 1f);
            float aAxis = components[1].Value;
            float bAxis = components[2].Value;
            TryParseAlphaValue(alpha, out float alphaValue);
            Vector3 linearRgb = oklab ? OklabToLinearRgb(l, aAxis, bAxis) : LabToLinearRgb(l, aAxis, bAxis);
            color = ToByteColor(LinearRgbToGamma(linearRgb), alphaValue);
            return true;
        }

        // LCH / OKLCH：极坐标转 Lab 再走同管线；h 任意角（负值/超 360 由三角函数自然处理）
        static bool TryParseLch(string argumentList, out Color color, bool oklab) {
            color = default;
            if (!TryParseArguments(argumentList, out Component[] components, out Component? alpha)) {
                return false;
            }
            float l = components[0].Value * (components[0].IsPercent ? oklab ? 0.01f : 1f : 1f);
            float chroma = components[1].Value;
            float hueDegrees = AngleToDegrees(components[2]);
            float hueRadians = hueDegrees * MathF.PI / 180f;
            float aAxis = chroma * MathF.Cos(hueRadians);
            float bAxis = chroma * MathF.Sin(hueRadians);
            TryParseAlphaValue(alpha, out float alphaValue);
            Vector3 linearRgb = oklab ? OklabToLinearRgb(l, aAxis, bAxis) : LabToLinearRgb(l, aAxis, bAxis);
            color = ToByteColor(LinearRgbToGamma(linearRgb), alphaValue);
            return true;
        }

        // CIE Lab → 线性 sRGB：XYZ(D50) 一步合并矩阵（CSS Color 4 参考实现；lab(100 0 0)=白、
        // lab(54.29% 80.81 69.89)=sRGB 红逐值复现）。D50 白点 (0.9643, 1, 0.8251)
        static Vector3 LabToLinearRgb(float l, float a, float b) {
            const float Epsilon = 216f / 24389f;
            const float Kappa = 24389f / 27f;
            float fy = (l + 16f) / 116f;
            float fx = fy + a / 500f;
            float fz = fy - b / 200f;
            float xr = fx * fx * fx > Epsilon ? fx * fx * fx : (116f * fx - 16f) / Kappa;
            float yr = l > Kappa * Epsilon ? fy * fy * fy : l / Kappa;
            float zr = fz * fz * fz > Epsilon ? fz * fz * fz : (116f * fz - 16f) / Kappa;
            Vector3 xyzD50 = new(xr * 0.9642956764295677f, yr, zr * 0.8251046025104602f);
            return new Vector3(
                3.1338561f * xyzD50.X - 1.6168667f * xyzD50.Y - 0.4906146f * xyzD50.Z,
                -0.9787684f * xyzD50.X + 1.9161415f * xyzD50.Y + 0.0334540f * xyzD50.Z,
                0.0719453f * xyzD50.X - 0.2289914f * xyzD50.Y + 1.4052427f * xyzD50.Z
            );
        }

        // Oklab → 线性 sRGB（Björn Ottosson 参考矩阵：lab→lms' 立方展开→lms→线性 RGB）
        static Vector3 OklabToLinearRgb(float l, float a, float b) {
            float l_ = l + 0.3963377774f * a + 0.2158037573f * b;
            float m_ = l - 0.1055613458f * a - 0.0638541728f * b;
            float s_ = l - 0.0894841775f * a - 1.2914855480f * b;
            float lCubed = l_ * l_ * l_;
            float mCubed = m_ * m_ * m_;
            float sCubed = s_ * s_ * s_;
            return new Vector3(
                4.0767416621f * lCubed - 3.3077115913f * mCubed + 0.2309699292f * sCubed,
                -1.2684380046f * lCubed + 2.6097574011f * mCubed - 0.3413193965f * sCubed,
                -0.0041960863f * lCubed - 0.7034186147f * mCubed + 1.7076147010f * sCubed
            );
        }

        // 线性 sRGB → gamma sRGB（sRGB 传递函数）；广色域超界逐通道钳制
        static Vector3 LinearRgbToGamma(Vector3 linear) => new(GammaEncode(linear.X), GammaEncode(linear.Y), GammaEncode(linear.Z));

        static float GammaEncode(float channel) {
            channel = MathUtils.Clamp(channel, 0f, 1f);
            return channel <= 0.0031308f ? 12.92f * channel : 1.055f * MathF.Pow(channel, 1f / 2.4f) - 0.055f;
        }

        static Color ToByteColor(Vector3 gammaRgb, float alpha) => new(
            (int)MathF.Round(MathUtils.Clamp(gammaRgb.X, 0f, 1f) * 255f),
            (int)MathF.Round(MathUtils.Clamp(gammaRgb.Y, 0f, 1f) * 255f),
            (int)MathF.Round(MathUtils.Clamp(gammaRgb.Z, 0f, 1f) * 255f),
            (int)MathF.Round(MathUtils.Clamp(alpha, 0f, 1f) * 255f)
        );
    }
}