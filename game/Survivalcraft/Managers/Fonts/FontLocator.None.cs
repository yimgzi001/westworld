// 非 WINDOWS/ANDROID/LINUX/BROWSER 平台（IOS 及裸 net10.0 的 API 库构建）：系统字体发现空实现。
// 有意不用 "#if IOS" 枚举：裸 net10.0（Survivalcraft.csproj）不定义任何平台常量，枚举写法会使
// FontLocator.Discover 缺失、FontManager 编译失败；穷举反集保证任何 TFM 下恰好有一个 Discover
// 实现（用户口径"只有 iOS 端不支持系统字体"——裸 net10.0 是非平台 API 库构建，随 IOS 一并落本实现）。
// Discover 恒空 → 系统尾部无条目、"system" 槽位解析 null（加载期迁移为 builtin，
// 见 FontManager.MigrateSystemSlotsToBuiltin），矢量链由内置头/槽位/模组/用户字体构成。
#if !(WINDOWS || ANDROID || LINUX || BROWSER)
namespace Game {
    static partial class FontLocator {
        public static List<SystemFontFace> Discover() => [];

        /// <summary>本实现无系统发现，恒未尝试（SystemFontsSupported=false 下回退门不可达，编译占位）。</summary>
        internal static bool SystemFontsScanAttempted => false;
    }
}
#endif
