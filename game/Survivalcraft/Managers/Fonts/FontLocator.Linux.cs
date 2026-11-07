#if LINUX
// Linux 系统字体发现：fontconfig 主路线（libfontconfig.so.1 P/Invoke，仅 FcFontMatch 求值）
// + 目录扫描候选表兜底。发行版碎片化恰是 fontconfig 存在的意义——主流发行版桌面
// （Debian/Fedora/Arch/openSUSE）全以其为字体抽象层（GTK/Qt 硬依赖），Steam runtime 与
// Flatpak 沙箱亦保证（Flatpak 宿主字体挂 /run/host/fonts）；极简容器/无 fontconfig 环境
// 落目录扫描（候选文件名打表，精神同 Windows 注册表路线）。两路全空 → Discover 返回空，
// 链由内置头/槽位/模组/用户字体构成（FontManager 平台说明）。
using System.Runtime.InteropServices;
using Engine;
using Engine.Media;

namespace Game {
    static partial class FontLocator {
        // XDG 标准目录（~ 前缀 Discover 时展开）+ Flatpak 宿主字体挂载点；
        // 发行版字体树为多级目录（/usr/share/fonts/truetype/noto/...），须递归枚举
        static readonly string[] FontDirectories = [
            "/usr/share/fonts",
            "/usr/local/share/fonts",
            "~/.local/share/fonts",
            "~/.fonts",
            "/run/host/fonts"
        ];

        // 兜底打表（fontconfig 不可用/求值全空时用）：候选名覆盖主流发行版默认字体
        // （Noto 系=Debian/Fedora/Arch 现役默认，WenQuanYi=老 Ubuntu 中文，Liberation/DejaVu=
        // 拉丁基座）。组内按序取首个可用文件；序=链内回退优先序：CJK 垫首位（拉丁已由
        // Pericles 链头供给），Liberation/DejaVu 垫底仅作纯拉丁系统的 sans 回落
        static readonly (string[] Names, string Family, VectorFontStyle Style)[] BodyCandidates = [
            (["NotoSansCJK-Regular.ttc", "NotoSansCJKsc-Regular.otf", "NotoSansSC-Regular.otf", "NotoSansSC-Regular.ttf"], "Noto Sans CJK SC", VectorFontStyle.Regular),
            (["NotoSansCJK-Bold.ttc", "NotoSansCJKsc-Bold.otf", "NotoSansSC-Bold.otf", "NotoSansSC-Bold.ttf"], "Noto Sans CJK SC", VectorFontStyle.Bold),
            (["wqy-microhei.ttc", "wqy-zenhei.ttc"], "WenQuanYi", VectorFontStyle.Regular),
            (["LiberationSans-Regular.ttf", "DejaVuSans.ttf"], "Sans", VectorFontStyle.Regular),
            (["LiberationSans-Bold.ttf", "DejaVuSans-Bold.ttf"], "Sans", VectorFontStyle.Bold),
        ];

        static readonly (string[] Names, string Family, VectorFontStyle Style)[] MonoCandidates = [
            (["DejaVuSansMono.ttf", "LiberationMono-Regular.ttf", "NotoSansMono-Regular.ttf", "UbuntuMono-R.ttf"], "Mono", VectorFontStyle.Regular),
            (["DejaVuSansMono-Bold.ttf", "LiberationMono-Bold.ttf", "NotoSansMono-Bold.ttf", "UbuntuMono-B.ttf"], "Mono", VectorFontStyle.Bold),
        ];

        public static List<SystemFontFace> Discover() {
            List<SystemFontFace> result = [];
            if (TryDiscoverViaFontconfig(result)) {
                return result;
            }
            Log.Information("[FontLocator] fontconfig unavailable/empty, falling back to candidate table.");
            try {
                Dictionary<string, string> files = EnumerateFontFiles();
                MatchAndAdd(result, files, BodyCandidates, mono: false);
                MatchAndAdd(result, files, MonoCandidates, mono: true);
            }
            catch (Exception ex) {
                Log.Warning($"[FontLocator] Linux discover failed: {ex.Message}");
            }
            return result;
        }

        /// <summary>每次重建即系统发现尝试（RebuildChainsCore 系统槽位回退前置门）。</summary>
        internal static bool SystemFontsScanAttempted => true;

        // fontconfig 主路线：body=sans-serif+游戏语言 lang（跟 LanguageControl 而非系统
        // locale，两者可不同；lang 失配只重罚评分不排除候选，纯英文装机求 zh 仍回系统默认
        // sans，语义正确），mono=monospace 不带 lang（等宽取系统默认族）。无斜体请求：
        // 与 Windows/Android 打表同口径（CJK 斜体由切变降级承担）。
        // 部分成功也算成功（如仅 body regular 一件，bold 缺席=降级语义正确）
        static bool TryDiscoverViaFontconfig(List<SystemFontFace> result) {
            try {
                IntPtr config = NativeFont.FcConfigGetCurrent();
                if (config == IntPtr.Zero) {
                    return false;
                }
                string lang = LanguageControl.CurrentLanguageName.ToLowerInvariant();
                // 文件字节跨 4 次 match 调用共享读盘（CJK ttc/otc 可到几十 MB）
                Dictionary<string, byte[]> fileCache = [];
                bool any = false;
                any |= MatchFace(result, config, "sans-serif", lang, NativeFont.FcWeightRegular, mono: false, "System Sans-Serif", fileCache);
                any |= MatchFace(result, config, "sans-serif", lang, NativeFont.FcWeightBold, mono: false, "System Sans-Serif", fileCache);
                any |= MatchFace(result, config, "monospace", null, NativeFont.FcWeightRegular, mono: true, "System Mono", fileCache);
                any |= MatchFace(result, config, "monospace", null, NativeFont.FcWeightBold, mono: true, "System Mono", fileCache);
                return any;
            }
            catch (Exception ex) {
                // DllNotFoundException（无 fontconfig 的极简环境）/EntryPointNotFound 等一律落目录扫描兜底
                Log.Warning($"[FontLocator] fontconfig discover failed: {ex.Message}");
                return false;
            }
        }

        static bool MatchFace(List<SystemFontFace> result, IntPtr config, string familyName, string lang, int weight, bool mono, string displayFamily, Dictionary<string, byte[]> fileCache) {
            IntPtr pattern = NativeFont.FcPatternCreate();
            if (pattern == IntPtr.Zero) {
                return false;
            }
            try {
                if (!NativeFont.FcPatternAddString(pattern, "family", familyName)
                    || !NativeFont.FcPatternAddInteger(pattern, "weight", weight)
                    || (lang != null && !AddLang(pattern, lang))) {
                    return false;
                }
                // 标准三步匹配序的前两步（fc-match CLI/Xft 同款，FcFontMatch(3) 明示先于此调用）：
                // FcConfigSubstitute 触发 /etc/fonts/conf.d 的 alias/edit 规则——generic 族名
                // （sans-serif/monospace）到真实族偏好序的实体化全靠它，跳过则 family 对所有
                // 字体同罚分无区分度（mono 路线可任选比例字体）；FcDefaultSubstitute 补缺省项
                // （slant=roman 等，防同权重斜体面平分顶替正体）。只填缺失项，已设的
                // family/weight/lang 不被覆盖
                if (!NativeFont.FcConfigSubstitute(config, pattern, NativeFont.FcMatchPattern)) {
                    return false;
                }
                NativeFont.FcDefaultSubstitute(pattern);
                IntPtr match = NativeFont.FcFontMatch(config, pattern, out int fcResult);
                if (match == IntPtr.Zero) {
                    return false;
                }
                try {
                    // 防御分支（现行实现失败恒返 NULL、非 Null ⇒ FcResultMatch，实际不可达）
                    if (fcResult != 0) { // FcResultMatch=0
                        return false;
                    }
                    string path = GetString(match, "file");
                    if (string.IsNullOrEmpty(path)
                        || !File.Exists(path)) {
                        return false;
                    }
                    // 样式按返回面实际位判读：bold 请求落到 <demibold 面（系统无真 bold）→ 丢弃，
                    // Stroked 兜底接管（Android matcher 同语义）；demibold 计入 bold（MiSans Demibold 档），
                    // 但 Regular 请求落 demibold 面（系统唯一 sans 为 demibold 的罕见装机）按 Regular
                    // 判读先入链——bold 请求同面同 path#face 由 AddSystemEntries 去重消化，链仍保有
                    // Regular 面不整链退位图
                    int matchedWeight = GetInteger(match, "weight", weight);
                    bool bold = matchedWeight >= NativeFont.FcWeightBold
                        || (weight >= NativeFont.FcWeightBold
                            && matchedWeight >= NativeFont.FcWeightDemibold);
                    if (weight >= NativeFont.FcWeightBold
                        && matchedWeight < NativeFont.FcWeightDemibold) {
                        return false;
                    }
                    bool italic = GetInteger(match, "slant", 0) >= NativeFont.FcSlantItalic;
                    VectorFontStyle style = bold
                        ? (italic ? VectorFontStyle.BoldItalic : VectorFontStyle.Bold)
                        : (italic ? VectorFontStyle.Italic : VectorFontStyle.Regular);
                    // fontconfig ≥2.13.93 的 index 高位编码 FreeType named instance（<<16），
                    // SharpAstro 只吃裸面序 → 掩低 16 位（本路线候选域全为静态面，实例位不出现；
                    // 真出实例面时掩掉=回落默认实例，无害降级）
                    int faceIndex = GetInteger(match, "index", 0) & 0xFFFF;
                    // 轮廓探针：CFF2 变量面（部分发行版 Noto CJK VF）SharpAstro 渲染必炸，匹配期拒收
                    if (!FontLocator.ProbeFaceOutlines(path, faceIndex, fileCache)) {
                        return false;
                    }
                    Log.Information($"[FontLocator] fontconfig [{familyName}] lang={lang ?? "-"} weight={matchedWeight} file={path} face={faceIndex} -> {style}");
                    result.Add(new SystemFontFace {
                        FilePath = path,
                        Family = displayFamily,
                        Style = style,
                        FaceIndex = faceIndex,
                        IsMono = mono
                    });
                    return true;
                }
                finally {
                    NativeFont.FcPatternDestroy(match);
                }
            }
            finally {
                NativeFont.FcPatternDestroy(pattern);
            }
        }

        // FC_LANG 的正规类型为 LangSet（string 直加依赖 FcCompareLang 的串转换，旧版
        // fontconfig 会 TypeMismatch），走 LangSet API 稳
        static bool AddLang(IntPtr pattern, string lang) {
            IntPtr langSet = NativeFont.FcLangSetCreate();
            if (langSet == IntPtr.Zero) {
                return false;
            }
            try {
                return NativeFont.FcLangSetAdd(langSet, lang)
                    && NativeFont.FcPatternAddLangSet(pattern, "lang", langSet);
            }
            finally {
                NativeFont.FcLangSetDestroy(langSet);
            }
        }

        static string GetString(IntPtr pattern, string obj) {
            if (NativeFont.FcPatternGetString(pattern, obj, 0, out IntPtr value) != 0
                || value == IntPtr.Zero) {
                return null;
            }
            // FcChar8*=UTF-8；字体路径可含非 ASCII（~/.fonts 中文目录名）
            return Marshal.PtrToStringUTF8(value);
        }

        static int GetInteger(IntPtr pattern, string obj, int fallback) =>
            NativeFont.FcPatternGetInteger(pattern, obj, 0, out int value) == 0 ? value : fallback;

        static Dictionary<string, string> EnumerateFontFiles() {
            Dictionary<string, string> files = new(StringComparer.OrdinalIgnoreCase);
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            foreach (string directory in FontDirectories) {
                bool homeRelative = directory.StartsWith('~');
                if (homeRelative
                    && string.IsNullOrEmpty(profile)) {
                    continue; // HOME 未设（系统服务/极简容器）：跳过，防 Combine 空串生成 CWD 相对路径
                }
                string root = homeRelative
                    ? Path.Combine(profile, directory[1..].TrimStart('/'))
                    : directory;
                WalkFontDirectory(root, files, new HashSet<string>(StringComparer.Ordinal));
            }
            return files;
        }

        // 逐目录 try/catch 递归：SearchOption.AllDirectories 一遇拒绝即整体中断
        //（/run/host 等权限隔离树常见），分层走只损失单个子树。
        // symlink 环防护：EnumerateDirectories 在 Unix 返回目录符号链接且照跟，字体树内指向上级
        // 的环=无限递归 StackOverflowException（不可捕获，且崩在 RebuildChains 持锁段）——按
        // 解析后终靶全路径去重防环（链接与直路指向同目录只走一次）；不用 ReparsePoint 属性跳过，
        // 会连带符号链接形式的字体文件（/run/host/fonts 内可能出现）
        static void WalkFontDirectory(string directory, Dictionary<string, string> files, HashSet<string> visited) {
            string key;
            try {
                FileSystemInfo resolved = Directory.ResolveLinkTarget(directory, returnFinalTarget: true);
                key = resolved?.FullName ?? Path.GetFullPath(directory);
            }
            catch {
                return;
            }
            if (!visited.Add(key)) {
                return;
            }
            try {
                foreach (string file in Directory.EnumerateFiles(directory)) {
                    files.TryAdd(Path.GetFileName(file), file);
                }
            }
            catch (Exception ex) {
                Log.Warning($"[FontLocator] enumerate \"{directory}\" failed: {ex.Message}");
            }
            try {
                foreach (string sub in Directory.EnumerateDirectories(directory)) {
                    WalkFontDirectory(sub, files, visited);
                }
            }
            catch (Exception ex) {
                Log.Warning($"[FontLocator] descend \"{directory}\" failed: {ex.Message}");
            }
        }

        static void MatchAndAdd(List<SystemFontFace> result, Dictionary<string, string> files, (string[] Names, string Family, VectorFontStyle Style)[] candidates, bool mono) {
            foreach ((string[] names, string family, VectorFontStyle style) in candidates) {
                foreach (string name in names) {
                    if (!files.TryGetValue(name, out string fullPath)) {
                        continue;
                    }
                    SystemFontFace face = ProbeFontFile(fullPath, family, style, mono);
                    if (face != null) {
                        result.Add(face);
                        break; // 组内首个可用候选即止（候选组=同角色的降级序列）
                    }
                }
            }
        }

        // fontconfig C API 逐字面签名（fcpattern.h/fclang.h/fcweight.h 核对）：
        // FcBool=C int（0/1， marshal I1 读低字节即真值）、FcChar8=unsigned char、FcResult=C int；
        // 库运行时真身=libfontconfig.so.1（裸名 .so 仅 -dev 包提供 symlink，直指全名免探测）；
        // 找不到库/符号由上层 catch 落目录扫描兜底。配置对象 FcConfigGetCurrent 惰性初始化
        // （fontconfig ≥2.12 全入口懒加载，无须 FcInit；进程常驻不做 FcFini）
        internal static class NativeFont {
            internal const string Library = "libfontconfig.so.1";

            // 样式判读阈值（fcweight.h/fcconst.h）：请求值与返回面实际字重/斜度共用一套
            internal const int FcWeightRegular = 80;
            internal const int FcWeightDemibold = 180;
            internal const int FcWeightBold = 200;
            internal const int FcSlantItalic = 100;
            internal const int FcMatchPattern = 0;

            [DllImport(Library)] internal static extern IntPtr FcConfigGetCurrent();
            [DllImport(Library)] internal static extern IntPtr FcPatternCreate();
            [DllImport(Library)] internal static extern void FcPatternDestroy(IntPtr pattern);
            [return: MarshalAs(UnmanagedType.I1)]
            [DllImport(Library)] internal static extern bool FcPatternAddString(IntPtr pattern, [MarshalAs(UnmanagedType.LPStr)] string obj, [MarshalAs(UnmanagedType.LPStr)] string value);
            [return: MarshalAs(UnmanagedType.I1)]
            [DllImport(Library)] internal static extern bool FcPatternAddInteger(IntPtr pattern, [MarshalAs(UnmanagedType.LPStr)] string obj, int value);
            // 标准三步匹配序前两步（fcconfig.h）：kind=FcMatchPattern(0) 对 pattern 施配置规则
            [return: MarshalAs(UnmanagedType.I1)]
            [DllImport(Library)] internal static extern bool FcConfigSubstitute(IntPtr config, IntPtr pattern, int kind);
            [DllImport(Library)] internal static extern void FcDefaultSubstitute(IntPtr pattern);
            [DllImport(Library)] internal static extern IntPtr FcFontMatch(IntPtr config, IntPtr pattern, out int result);
            [DllImport(Library)] internal static extern int FcPatternGetString(IntPtr pattern, [MarshalAs(UnmanagedType.LPStr)] string obj, int id, out IntPtr value);
            [DllImport(Library)] internal static extern int FcPatternGetInteger(IntPtr pattern, [MarshalAs(UnmanagedType.LPStr)] string obj, int id, out int value);
            [DllImport(Library)] internal static extern IntPtr FcLangSetCreate();
            [DllImport(Library)] internal static extern void FcLangSetDestroy(IntPtr langSet);
            [return: MarshalAs(UnmanagedType.I1)]
            [DllImport(Library)] internal static extern bool FcLangSetAdd(IntPtr langSet, [MarshalAs(UnmanagedType.LPStr)] string lang);
            [return: MarshalAs(UnmanagedType.I1)]
            [DllImport(Library)] internal static extern bool FcPatternAddLangSet(IntPtr pattern, [MarshalAs(UnmanagedType.LPStr)] string obj, IntPtr langSet);
        }
    }
}
#endif
