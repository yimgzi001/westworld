#if BROWSER
// 浏览器系统字体发现（Local Font Access API，仅 Chromium 103+）：设置屏"系统默认"槽位应用时
// 借用户手势现场扫描（window.queryLocalFonts 权限弹窗，local-fonts 权限跨会话持久），全量枚举
// FontData 元数据（family/style/postscriptName——无文件路径/face 序/lang），候选表匹配后按
// postscriptName 拉取命中面 blob=SFNT 原始字节进内存（用户裁定系统字体不落盘，唯一落盘=手动
// 导入字体到 ModsManager.FontsPath），GetFileEntry 经 SystemFontFace.Data 直用免读盘。
// ttc 的每个面是独立 FontData 且共享整 collection blob、face 序不暴露 → 字节 LoadAll 后按
// OpenTypeFont.Name.PostScriptName 精确定位面序（容错退样式位匹配，同 93808b9f JP 面教训口径）。
// Firefox/Safari 无 API：SystemFontsSupported 运行时探测为 false，设置屏隐藏"系统默认"，
// 链走内置头/槽位/用户字体（位图回退不变）。
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Runtime.InteropServices.JavaScript;
using Engine;
using Engine.Browser;
using Engine.Media;
using SharpAstro.Fonts;

namespace Game {
    static partial class FontLocator {
        // 候选表（序=链内回退优先序，游戏默认 zh-CN 故 CJK 简体垫首位；拉丁已由 Pericles 链头
        // 供给，尾部拉丁仅作"原版英文风格关闭"后的回落）。FontData 只有 family/style 文本
        // （style 串如 "Regular"/"Bold"/"Semibold"/"Bold Italic"），无 lang——与 Windows 注册表
        // 路线同构：family 精确匹配（OrdinalIgnoreCase）+ style 串判样式。
        // family 别名双打：FontData.family 可能是本地化名（macOS 回 "苹方-简"），英文名+本地化名
        // 都要；"XXX UI" 是同文件独立族（msyh.ttc 两族），主族名缺失时按序顶替（命中即止非并存入链）。覆盖面按宿主 OS 打：Windows/macOS（苹方无真 Bold，
        // Semibold=600 计 Bold，同 Android weight≥600 口径）/Linux/ChromeOS（fontconfig 族名）；
        // iOS 浏览器无此 API（LFA 是桌面 Chromium 面），表内不列 iOS 族名（列了也永不命中）。
        static readonly (string[] Families, bool Mono)[] Candidates = [
            (["Microsoft YaHei", "Microsoft YaHei UI", "微软雅黑"], false),
            (["Microsoft JhengHei", "Microsoft JhengHei UI", "微軟正黑體"], false),
            (["PingFang SC", "苹方-简"], false),
            (["PingFang TC", "苹方-繁"], false),
            (["PingFang HK", "苹方-港"], false),
            (["Hiragino Sans GB", "冬青黑体简体中文"], false),
            (["Meiryo", "Meiryo UI", "メイリオ"], false),
            (["Yu Gothic", "Yu Gothic UI", "游ゴシック", "游ゴシック体"], false),
            (["Hiragino Sans", "ヒラギノ角ゴシック"], false),
            (["Hiragino Kaku Gothic ProN", "ヒラギノ角ゴ ProN"], false),
            (["Malgun Gothic", "맑은 고딕"], false),
            (["Apple SD Gothic Neo", "애플 SD 산돌고딕 Neo"], false),
            (["Noto Sans CJK SC", "Noto Sans SC"], false),
            (["Noto Sans CJK TC", "Noto Sans TC"], false),
            (["Noto Sans CJK HK"], false),
            (["Noto Sans CJK JP", "Noto Sans JP"], false),
            (["Noto Sans CJK KR", "Noto Sans KR"], false),
            (["Source Han Sans SC", "Source Han Sans"], false),
            (["Source Han Sans TC"], false),
            (["Segoe UI"], false),
            (["Helvetica Neue"], false),
            (["Roboto"], false),
            (["DejaVu Sans"], false),
            (["Liberation Sans"], false),
            (["Consolas"], true),
            (["Cascadia Mono"], true),
            (["Cascadia Code"], true),
            (["Menlo"], true),
            (["SF Mono"], true),
            (["DejaVu Sans Mono"], true),
            (["Liberation Mono"], true),
            (["Noto Sans Mono"], true),
            (["Roboto Mono"], true),
            (["Courier New"], true),
        ];

        /// <summary>扫描结果缓存（扫描前 null=Discover 空=链回落 builtin 头+槽位）。整表扫描完
        /// 一次性原子换入，构建线程（RebuildChainsCore/ProbeSourceStylesCore）只读。</summary>
        static List<SystemFontFace> m_cachedFaces;

        /// <summary>是否已实际发起过扫描（无论成败）——RebuildChainsCore 系统槽位回退的
        /// 前置门：扫描前字体并非"不可用"只是待手势扫描，回退会静默清掉用户持久化选择。</summary>
        static bool m_scanAttempted;

        /// <summary>W/A/L 每次重建即系统发现尝试恒 true；浏览器=至少发起过一次扫描。</summary>
        internal static bool SystemFontsScanAttempted => m_scanAttempted;

        /// <summary>本会话扫描是否已成功过（m_cachedFaces 非空）——设置屏扫描门：
        /// 已有结果时 Enter/ApplySlot 直接消费缓存，不再借手势重扫（每轮全量 blob 重取 ~250MB）。</summary>
        internal static bool SystemFontsScanSucceeded => m_cachedFaces != null;

        public static List<SystemFontFace> Discover() {
            List<SystemFontFace> faces = m_cachedFaces;
            return faces ?? [];
        }

        /// <summary>
        /// 全量扫描（须用户手势上下文调用，如设置屏槽位选择；权限已授时无弹窗静默刷新）：<br/>
        /// queryLocalFonts 元数据枚举 → 候选表匹配（family 精确 + style 串判样式）→ 命中面按
        /// postscriptName 逐个拉 blob（同内容字节引用去重：Noto CJK 各区域面共享同一 ttc，
        /// 只拉一次、LoadAll 一次、多面各取 face 序）→ SystemFontFace.Data 内存持有不落盘。<br/>
        /// 拒绝/不支持/枚举空/解析异常 = m_cachedFaces 不动（保持上一轮，首次则恒空）。
        /// </summary>
        public static async Task ScanLocalFontsAsync() {
            m_scanAttempted = true;
            if (!BrowserInterop.IsLocalFontsSupported()) {
                Log.Information("[FontLocator] local font access API unavailable.");
                return;
            }
            // 首调即权限弹窗（须在手势任务内发起）；授权后此处静默
            string json = await BrowserInterop.QueryLocalFontsData();
            List<LocalFontMeta> fonts;
            try {
                fonts = JsonSerializer.Deserialize(json, LocalFontJsonContext.Default.ListLocalFontMeta);
            }
            catch (JsonException ex) {
                Log.Warning($"[FontLocator] local font metadata parse failed: {ex.Message}");
                return;
            }
            if (fonts == null
                || fonts.Count == 0) {
                Log.Information("[FontLocator] local font enumeration empty.");
                return;
            }
            Dictionary<string, List<LocalFontMeta>> byFamily = new(StringComparer.OrdinalIgnoreCase);
            foreach (LocalFontMeta meta in fonts) {
                if (!string.IsNullOrEmpty(meta.Family)) {
                    if (!byFamily.TryGetValue(meta.Family, out List<LocalFontMeta> group)) {
                        group = [];
                        byFamily.Add(meta.Family, group);
                    }
                    group.Add(meta);
                }
            }
            // 组内挑 Regular（style=="Regular" 优先，退非粗非斜首个）与 Bold（style 含 bold 且
            // 不含 italic——覆盖 "Bold"/"Semibold"/"SemiBold"/"Demibold"）
            List<(LocalFontMeta Meta, string Family, VectorFontStyle Style, bool Mono)> selected = [];
            foreach ((string[] families, bool mono) in Candidates) {
                string matchedFamily = null;
                List<LocalFontMeta> group = null;
                foreach (string family in families) {
                    if (byFamily.TryGetValue(family, out group)) {
                        matchedFamily = family;
                        break;
                    }
                }
                if (matchedFamily == null) {
                    continue;
                }
                LocalFontMeta regular = PickStyle(group, bold: false);
                if (regular != null) {
                    selected.Add((regular, matchedFamily, VectorFontStyle.Regular, mono));
                }
                LocalFontMeta bold = PickStyle(group, bold: true);
                if (bold != null) {
                    selected.Add((bold, matchedFamily, VectorFontStyle.Bold, mono));
                }
            }
            if (selected.Count == 0) {
                Log.Information($"[FontLocator] no candidate family matched among {fonts.Count} local fonts.");
                return;
            }
            List<byte[]> fetchedBlobs = [];
            Dictionary<byte[], OpenTypeFont[]> parsedFonts = [];
            List<SystemFontFace> faces = [];
            foreach ((LocalFontMeta meta, string family, VectorFontStyle style, bool mono) in selected) {
                try {
                    JSObject blob = await BrowserInterop.GetLocalFontBlob(meta.Ps);
                    if (blob == null) {
                        continue;
                    }
                    byte[] data;
                    using (blob) {
                        data = BrowserInterop.JSObject2ByteArray(blob);
                    }
                    // 同内容去重（引用共享）：各区域面共享的 collection 字节只拉取/解析一次
                    byte[] shared = null;
                    foreach (byte[] existing in fetchedBlobs) {
                        if (existing.Length == data.Length
                            && data.AsSpan().SequenceEqual(existing)) {
                            shared = existing;
                            break;
                        }
                    }
                    if (shared == null) {
                        fetchedBlobs.Add(data);
                        shared = data;
                    }
                    if (!parsedFonts.TryGetValue(shared, out OpenTypeFont[] parsed)) {
                        parsed = OpenTypeFont.LoadAll(shared);
                        parsedFonts.Add(shared, parsed);
                    }
                    // 面序定位：postscriptName 精确匹配 → 退样式位匹配 → 退首面
                    int index = Array.FindIndex(parsed, font => font.Name?.PostScriptName == meta.Ps);
                    if (index < 0) {
                        index = Array.FindIndex(parsed, font => MapFaceStyle(font) == style);
                    }
                    if (index < 0) {
                        index = 0;
                    }
                    // 轮廓探针：CFF2 变量面（如 Linux 发行版 Noto CJK VF）SharpAstro 渲染必炸，
                    // 发现期拒收，免得坏面进链在 BuildChain 整链退位图
                    if (!FontLocator.HasRenderableOutlines(parsed[index])) {
                        Log.Warning($"[FontLocator] local font \"{meta.Ps}\" rejected: no renderable outlines (CFF2-only variable font?).");
                        continue;
                    }
                    faces.Add(new SystemFontFace {
                        FilePath = $"localfont://{meta.Ps}", // 展示/日志用；读盘路径不存在，字节由 Data 供给
                        Family = family,
                        Style = style,
                        FaceIndex = index,
                        IsMono = mono,
                        Data = shared
                    });
                    Log.Information($"[FontLocator] local font [{family}] style={meta.Style} ps={meta.Ps} face={index} bytes={shared.Length}");
                }
                catch (Exception ex) {
                    Log.Warning($"[FontLocator] local font \"{meta.Ps}\" fetch failed: {ex.Message}");
                }
            }
            if (faces.Count > 0) {
                m_cachedFaces = faces;
            }
        }

        static LocalFontMeta PickStyle(List<LocalFontMeta> group, bool bold) {
            LocalFontMeta fallback = null;
            foreach (LocalFontMeta meta in group) {
                string style = meta.Style ?? "";
                bool hasBold = style.Contains("bold", StringComparison.OrdinalIgnoreCase);
                bool hasItalic = style.Contains("italic", StringComparison.OrdinalIgnoreCase);
                if (bold) {
                    if (hasBold
                        && !hasItalic) {
                        return meta;
                    }
                }
                else if (!hasBold
                    && !hasItalic) {
                    if (style.Equals("Regular", StringComparison.OrdinalIgnoreCase)) {
                        return meta;
                    }
                    fallback ??= meta;
                }
            }
            return fallback;
        }
    }

    /// <summary>queryLocalFonts 元数据（JSON 序列化走源生成——Browser 端 full AOT 反射序列化不可用；
    /// 键名=JS 侧 JSON.stringify 的小写键，源生成默认大小写敏感）。</summary>
    sealed class LocalFontMeta {
        [JsonPropertyName("family")]
        public string Family { get; set; }

        [JsonPropertyName("style")]
        public string Style { get; set; }

        [JsonPropertyName("ps")]
        public string Ps { get; set; }
    }

    [JsonSerializable(typeof(List<LocalFontMeta>))]
    internal sealed partial class LocalFontJsonContext : JsonSerializerContext;
}
#endif
