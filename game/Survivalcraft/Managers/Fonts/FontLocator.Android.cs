#if ANDROID
using System.Globalization;
using System.Runtime.InteropServices;
using Android.OS;
using Engine;
using Engine.Media;
using SharpAstro.Fonts;

namespace Game {
    static partial class FontLocator {
        // /system/fonts 世界可读（API 23→36 核验）；/system/product/fonts、/vendor/fonts 部分设备存在（存在性探测）
        static readonly string[] FontDirectories = ["/system/fonts", "/system/product/fonts", "/vendor/fonts"];

        // 兜底打表（API<29 或 AFontMatcher 失败时用）：候选名覆盖 AOSP+主流 OEM；
        // 组内按序取首个可用文件。AOSP 系统侧 CJK 无 Bold 文件（NotoSansCJK-Bold 系
        // OEM/MTK 定制 ROM 才有），Bold 组以 OEM 文件为主——缺真 bold 落 Stroked1 兜底
        static readonly (string[] Names, string Family, VectorFontStyle Style)[] BodyCandidates = [
            (["NotoSansCJK-Regular.ttc", "NotoSansCJKsc-Regular.otf", "NotoSansSC-Regular.otf", "DroidSansFallback.ttf"], "Noto Sans CJK SC", VectorFontStyle.Regular),
            (["MiSans-Regular.ttf", "MIUI_Normal.ttf", "HarmonyOS_Sans_SC_Medium.ttf"], "OEM Sans", VectorFontStyle.Regular),
        ];

        static readonly (string[] Names, string Family, VectorFontStyle Style)[] BoldCandidates = [
            (["NotoSansCJK-Bold.ttc", "NotoSansCJKsc-Bold.otf", "DroidSansFallbackBold.ttf", "MiSans-Bold.ttf", "MiSans-Demibold.ttf", "HarmonyOS_Sans_SC_Bold.ttf"], "Bold", VectorFontStyle.Bold),
        ];

        static readonly (string[] Names, string Family, VectorFontStyle Style)[] MonoCandidates = [
            (["DroidSansMono.ttf", "NotoSansMono-Regular.ttf", "RobotoMono-Regular.ttf", "CutiveMono.ttf"], "Mono", VectorFontStyle.Regular),
        ];

        public static List<SystemFontFace> Discover() {
            List<SystemFontFace> result = [];
            // 主路线（API 29+）：系统级 AFontMatcher 按 通用族+文本+语言+字重 选字体——
            // fonts.xml/font_fallback.xml/font_customization.xml 的合并结果由系统内部消化
            // （OEM 覆写自动生效），NotoSansCJK ttc 的 JP/KR/SC/TC 面序由系统按 locale 选对，
            // 免解析 XML 的 schema 漂移。API<29 或匹配失败落兜底打表
            if (TryDiscoverViaMatcher(result)) {
                // 匹配面探针可能拒收 body Regular（Android 16 起 AOSP NotoSansCJK-Regular.ttc
                // 改 CFF2 变量字体，SharpAstro 不支持该轮廓），缺口从候选表补位——OEM glyf 面
                // （MiSans 等）顶上，纯 AOSP 无候选则该角色落位图兜底。bold/mono 缺口维持既有
                // 语义不动（bold 缺真面=Stroked 兜底，mono 拉丁由链头供给）
                if (!result.Any(face => !face.IsMono && face.Style == VectorFontStyle.Regular)) {
                    Log.Information("[FontLocator] no usable body Regular face from matcher, supplementing from candidate table.");
                    try {
                        Dictionary<string, string> files = EnumerateFontFiles();
                        MatchAndAdd(result, files, BodyCandidates, mono: false);
                    }
                    catch (Exception ex) {
                        Log.Warning($"[FontLocator] Android body supplement failed: {ex.Message}");
                    }
                }
                return result;
            }
            Log.Information("[FontLocator] matcher unavailable/empty, falling back to candidate table.");
            try {
                Dictionary<string, string> files = EnumerateFontFiles();
                MatchAndAdd(result, files, BodyCandidates, mono: false);
                MatchAndAdd(result, files, BoldCandidates, mono: false);
                MatchAndAdd(result, files, MonoCandidates, mono: true);
            }
            catch (Exception ex) {
                Log.Warning($"[FontLocator] Android discover failed: {ex.Message}");
            }
            return result;
        }

        /// <summary>每次重建即系统发现尝试（RebuildChainsCore 系统槽位回退前置门）。</summary>
        internal static bool SystemFontsScanAttempted => true;

        // AFontMatcher（libandroid，API 29+）：body=CJK 探针字（拉丁已由 Pericles 链头供给，
        // 不依赖系统），mono=拉丁探针。bold 请求在系统无真 bold 文件时 matcher 返回 Regular
        // 面（getWeight<600）→ 丢弃，由 Stroked1 兜底；OEM 有 bold（MiSans 等）则自动命中。
        // 部分成功也算成功（如仅 body regular+mono regular 三件，bold 缺席=降级语义正确）
        static bool TryDiscoverViaMatcher(List<SystemFontFace> result) {
#pragma warning disable CA1416
            if (Build.VERSION.SdkInt < BuildVersionCodes.Q) {
#pragma warning restore CA1416
                return false;
            }
            IntPtr matcher = NativeFont.AFontMatcher_create();
            if (matcher == IntPtr.Zero) {
                return false;
            }
            // 文件字节跨 4 次 match 调用共享读盘（NotoSansCJK ttc ~19MB）
            Dictionary<string, byte[]> fileCache = [];
            try {
                // matcher locale=游戏内语言设置（LanguageControl），进程 CultureInfo.CurrentUICulture
                // 只作垫后——.NET Android 进程区域文化默认不随系统/游戏语言同步（恒 en-US），
                // 无 zh 偏好时 NotoSansCJK ttc 会落到容器首面=JP 面（"置"显示日文字形）；
                // locale 列表按优先序排列，首标签即游戏渲染目标语言
                string locales = $"{LanguageControl.CurrentLanguageName},{CultureInfo.CurrentUICulture.Name}";
                bool any = false;
                any |= TryMatchFace(result, matcher, locales, "sans-serif", "生存", 400, false, false, "System Sans-Serif", fileCache);
                any |= TryMatchFace(result, matcher, locales, "sans-serif", "生存", 700, false, false, "System Sans-Serif Bold", fileCache);
                any |= TryMatchFace(result, matcher, locales, "monospace", "A0", 400, false, true, "System Mono", fileCache);
                any |= TryMatchFace(result, matcher, locales, "monospace", "A0", 700, false, true, "System Mono Bold", fileCache);
                return any;
            }
            catch (Exception ex) {
                // EntryPointNotFound（API 门误判/OEM 阉割 libandroid）等一律落兜底打表
                Log.Warning($"[FontLocator] AFontMatcher discover failed: {ex.Message}");
                return false;
            }
            finally {
                NativeFont.AFontMatcher_destroy(matcher);
            }
        }

        static bool TryMatchFace(List<SystemFontFace> result, IntPtr matcher, string locales, string familyName, string probeText, int weight, bool italic, bool mono, string displayFamily, Dictionary<string, byte[]> fileCache) {
            NativeFont.AFontMatcher_setLocales(matcher, locales);
            NativeFont.AFontMatcher_setStyle(matcher, (ushort)weight, italic);
            ushort[] text = probeText.Select(c => (ushort)c).ToArray();
            IntPtr font = NativeFont.AFontMatcher_match(matcher, familyName, text, (uint)text.Length, out uint _);
            if (font == IntPtr.Zero) {
                return false;
            }
            try {
                string path = Marshal.PtrToStringAnsi(NativeFont.AFont_getFontFilePath(font));
                int collectionIndex = (int)NativeFont.AFont_getCollectionIndex(font);
                int matchedWeight = NativeFont.AFont_getWeight(font);
                if (string.IsNullOrEmpty(path)
                    || !File.Exists(path)) {
                    return false;
                }
                // bold 请求落到低字重面（系统无真 bold 文件）→ 不入链，Stroked1 兜底接管
                if (weight >= 600
                    && matchedWeight < 600) {
                    return false;
                }
                // 轮廓探针：CFF2 变量字体（Android 16 起 AOSP CJK）SharpAstro 渲染必炸，匹配期拒收
                if (!FontLocator.ProbeFaceOutlines(path, collectionIndex, fileCache)) {
                    return false;
                }
                VectorFontStyle style = matchedWeight >= 600
                    ? (NativeFont.AFont_isItalic(font) ? VectorFontStyle.BoldItalic : VectorFontStyle.Bold)
                    : (NativeFont.AFont_isItalic(font) ? VectorFontStyle.Italic : VectorFontStyle.Regular);
                Log.Information($"[FontLocator] matcher [{familyName}] locales={locales} weight={matchedWeight} file={path} face={collectionIndex} -> {style}");
                result.Add(new SystemFontFace {
                    FilePath = path,
                    Family = displayFamily,
                    Style = style,
                    FaceIndex = collectionIndex,
                    IsMono = mono
                });
                return true;
            }
            finally {
                NativeFont.AFont_close(font);
            }
        }

        // NDK font.h/font_matcher.h（API 29+）逐字面签名（本地 NDK 29 sysroot 核对）：
        // AFont_getCollectionIndex 返回 size_t（指针宽）→ UIntPtr；getLocale 可空不取用。
        // 符号在 API<29 的 libandroid.so 中不存在，调用时才解析 → EntryPointNotFound 由
        // 上层 catch 落兜底
        static class NativeFont {
            [DllImport("libandroid.so")] internal static extern IntPtr AFontMatcher_create();
            [DllImport("libandroid.so")] internal static extern void AFontMatcher_destroy(IntPtr matcher);
            [DllImport("libandroid.so")] internal static extern void AFontMatcher_setLocales(IntPtr matcher, [MarshalAs(UnmanagedType.LPStr)] string languageTags);
            // NDK C99 _Bool=1 字节，默认 bool marshal=4 字节 BOOL → 参数/返回均须 U1
            [DllImport("libandroid.so")] internal static extern void AFontMatcher_setStyle(IntPtr matcher, ushort weight, [MarshalAs(UnmanagedType.U1)] bool italic);
            [DllImport("libandroid.so")] internal static extern IntPtr AFontMatcher_match(IntPtr matcher, [MarshalAs(UnmanagedType.LPStr)] string familyName, ushort[] text, uint textLength, out uint runLengthOut);
            [DllImport("libandroid.so")] internal static extern void AFont_close(IntPtr font);
            [DllImport("libandroid.so")] internal static extern IntPtr AFont_getFontFilePath(IntPtr font);
            [DllImport("libandroid.so")] internal static extern ushort AFont_getWeight(IntPtr font);
            [return: MarshalAs(UnmanagedType.U1)]
            [DllImport("libandroid.so")] internal static extern bool AFont_isItalic(IntPtr font);
            [DllImport("libandroid.so")] internal static extern UIntPtr AFont_getCollectionIndex(IntPtr font);
        }

        static Dictionary<string, string> EnumerateFontFiles() {
            Dictionary<string, string> files = new(StringComparer.OrdinalIgnoreCase);
            foreach (string directory in FontDirectories) {
                if (!Directory.Exists(directory)) {
                    continue;
                }
                try {
                    foreach (string file in Directory.GetFiles(directory)) {
                        files.TryAdd(Path.GetFileName(file), file);
                    }
                }
                catch (Exception ex) {
                    Log.Warning($"[FontLocator] enumerate \"{directory}\" failed: {ex.Message}");
                }
            }
            return files;
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
    }
}
#endif
