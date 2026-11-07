using System.Xml.Linq;
using Engine;
using Engine.Media;

namespace Game {
    /// <summary>字体设置屏槽位标识（8 槽位，与 SettingsManager.Font* 设置一一对应）。</summary>
    internal enum FontSlot {
        Body,

        BodyBold,

        BodyItalic,

        BodyBoldItalic,

        Mono,

        MonoBold,

        MonoItalic,

        MonoBoldItalic
    }

    /// <summary>
    /// 字体设置屏（8 槽位 + 原版风格英文开关）。<br/>
    /// 线程设计（控制器裁定 2026-10-07）：字体导入校验（SharpAstro 试解析，文件可能数十 MB）
    /// 整体在 Task.Run 线程池执行，结果经 <see cref="Dispatcher.Dispatch"/> 编组回主线程；
    /// 槽位应用后的链重建由 FontManager.RebuildChains 内建线程池执行，onCompleted 编组回
    /// 主线程后做全屏重建热切换——一次 apply 恰触发一次 RebuildChains。
    /// </summary>
    public class SettingsFontScreen : Screen {
        public static string fName = "SettingsFontScreen";

        public ButtonWidget m_bodyButton;

        public ButtonWidget m_bodyBoldButton;

        public ButtonWidget m_bodyItalicButton;

        public ButtonWidget m_bodyBoldItalicButton;

        public ButtonWidget m_monoButton;

        public ButtonWidget m_monoBoldButton;

        public ButtonWidget m_monoItalicButton;

        public ButtonWidget m_monoBoldItalicButton;

        public ButtonWidget m_vanilaEnglishButton;

        public SettingsFontScreen() {
            XElement node = ContentManager.Get<XElement>("Screens/SettingsFontScreen");
            LoadContents(this, node);
            m_bodyButton = Children.Find<ButtonWidget>("BodyButton");
            m_bodyBoldButton = Children.Find<ButtonWidget>("BodyBoldButton");
            m_bodyItalicButton = Children.Find<ButtonWidget>("BodyItalicButton");
            m_bodyBoldItalicButton = Children.Find<ButtonWidget>("BodyBoldItalicButton");
            m_monoButton = Children.Find<ButtonWidget>("MonoButton");
            m_monoBoldButton = Children.Find<ButtonWidget>("MonoBoldButton");
            m_monoItalicButton = Children.Find<ButtonWidget>("MonoItalicButton");
            m_monoBoldItalicButton = Children.Find<ButtonWidget>("MonoBoldItalicButton");
            m_vanilaEnglishButton = Children.Find<ButtonWidget>("VanilaEnglishButton");
        }

        public override void Enter(object[] parameters) {
            // 联动禁用态预热：缓存未命中时后台探测（多 MB ttc 读盘/解析离 UI 线程），
            // 回调落缓存后由 Update 的 TryGetSourceStyles 命中刷新；命中时同步返回不闪帧。
            // 浏览器系统字体扫描不在 Enter 做：boot 期 FontManager.Initialize 已自动扫描
            // （已授权免手势；未授权自动回退内置），Enter 发起扫描曾被 RebuildAllScreens
            // 重建本屏再 Enter 放大成扫描→重建→扫描反馈环（2026-10-11 崩页实录）
            FontManager.ProbeSourceStyles(mono: false, SettingsManager.FontBody, null);
            FontManager.ProbeSourceStyles(mono: true, SettingsManager.FontMono, null);
        }

        public override void Update() {
            m_bodyButton.Text = DescribeSlotValue(FontSlot.Body);
            m_bodyBoldButton.Text = DescribeSlotValue(FontSlot.BodyBold);
            m_bodyItalicButton.Text = DescribeSlotValue(FontSlot.BodyItalic);
            m_bodyBoldItalicButton.Text = DescribeSlotValue(FontSlot.BodyBoldItalic);
            m_monoButton.Text = DescribeSlotValue(FontSlot.Mono);
            m_monoBoldButton.Text = DescribeSlotValue(FontSlot.MonoBold);
            m_monoItalicButton.Text = DescribeSlotValue(FontSlot.MonoItalic);
            m_monoBoldItalicButton.Text = DescribeSlotValue(FontSlot.MonoBoldItalic);
            m_vanilaEnglishButton.Text = SettingsManager.FontVanilaEnglish ? LanguageControl.Yes : LanguageControl.No;
            UpdateLinkedDisableStates();
            if (m_bodyButton.IsClicked) {
                ShowSlotDialog(FontSlot.Body);
            }
            if (m_bodyBoldButton.IsClicked) {
                ShowSlotDialog(FontSlot.BodyBold);
            }
            if (m_bodyItalicButton.IsClicked) {
                ShowSlotDialog(FontSlot.BodyItalic);
            }
            if (m_bodyBoldItalicButton.IsClicked) {
                ShowSlotDialog(FontSlot.BodyBoldItalic);
            }
            if (m_monoButton.IsClicked) {
                ShowSlotDialog(FontSlot.Mono);
            }
            if (m_monoBoldButton.IsClicked) {
                ShowSlotDialog(FontSlot.MonoBold);
            }
            if (m_monoItalicButton.IsClicked) {
                ShowSlotDialog(FontSlot.MonoItalic);
            }
            if (m_monoBoldItalicButton.IsClicked) {
                ShowSlotDialog(FontSlot.MonoBoldItalic);
            }
            if (m_vanilaEnglishButton.IsClicked) {
                SettingsManager.FontVanilaEnglish = !SettingsManager.FontVanilaEnglish;
                SettingsManager.SaveSettings();
                RebuildChainsAndScreens();
            }
            if (Input.Back
                || Input.Cancel
                || Children.Find<ButtonWidget>("TopBar.Back").IsClicked) {
                ScreensManager.GoBack();
            }
        }

        static string GetSetting(FontSlot slot) => slot switch {
            FontSlot.Body => SettingsManager.FontBody,
            FontSlot.BodyBold => SettingsManager.FontBodyBold,
            FontSlot.BodyItalic => SettingsManager.FontBodyItalic,
            FontSlot.BodyBoldItalic => SettingsManager.FontBodyBoldItalic,
            FontSlot.Mono => SettingsManager.FontMono,
            FontSlot.MonoBold => SettingsManager.FontMonoBold,
            FontSlot.MonoItalic => SettingsManager.FontMonoItalic,
            _ => SettingsManager.FontMonoBoldItalic
        };

        static void SetSetting(FontSlot slot, string value) {
            switch (slot) {
                case FontSlot.Body: SettingsManager.FontBody = value; break;
                case FontSlot.BodyBold: SettingsManager.FontBodyBold = value; break;
                case FontSlot.BodyItalic: SettingsManager.FontBodyItalic = value; break;
                case FontSlot.BodyBoldItalic: SettingsManager.FontBodyBoldItalic = value; break;
                case FontSlot.Mono: SettingsManager.FontMono = value; break;
                case FontSlot.MonoBold: SettingsManager.FontMonoBold = value; break;
                case FontSlot.MonoItalic: SettingsManager.FontMonoItalic = value; break;
                case FontSlot.MonoBoldItalic: SettingsManager.FontMonoBoldItalic = value; break;
            }
        }

        static bool IsMonoSlot(FontSlot slot) => slot >= FontSlot.Mono;

        static bool IsBaseSlot(FontSlot slot) => slot is FontSlot.Body or FontSlot.Mono;

        /// <summary>槽位按钮显示当前取值（内置字体/系统默认/基于正文字体/基于等宽字体/文件名），从对话框返回即随设置刷新。</summary>
        string DescribeSlotValue(FontSlot slot) {
            FontSlotSetting setting = FontSlotSetting.Parse(GetSetting(slot));
            return setting.Kind switch {
                FontSlotKind.BuiltIn => LanguageControl.GetContentWidgets(fName, "17"),
                FontSlotKind.ReferenceBody => LanguageControl.GetContentWidgets(fName, "12"),
                FontSlotKind.ReferenceMono => LanguageControl.GetContentWidgets(fName, "13"),
                FontSlotKind.UserFile => setting.FileName,
                _ => LanguageControl.GetContentWidgets(fName, "11")
            };
        }

        /// <summary>
        /// 联动禁用态（spec §4）：基础槽所选源含对应样式面（探测缓存命中时）→ 下游样式槽禁用。
        /// 未命中（探测进行中）保持现状，回调落缓存后由下一帧刷新。
        /// </summary>
        void UpdateLinkedDisableStates() {
            if (FontManager.TryGetSourceStyles(mono: false, SettingsManager.FontBody, out HashSet<VectorFontStyle> bodyStyles)) {
                m_bodyBoldButton.IsEnabled = !bodyStyles.Contains(VectorFontStyle.Bold);
                m_bodyItalicButton.IsEnabled = !bodyStyles.Contains(VectorFontStyle.Italic);
                m_bodyBoldItalicButton.IsEnabled = !bodyStyles.Contains(VectorFontStyle.BoldItalic);
            }
            if (FontManager.TryGetSourceStyles(mono: true, SettingsManager.FontMono, out HashSet<VectorFontStyle> monoStyles)) {
                m_monoBoldButton.IsEnabled = !monoStyles.Contains(VectorFontStyle.Bold);
                m_monoItalicButton.IsEnabled = !monoStyles.Contains(VectorFontStyle.Italic);
                m_monoBoldItalicButton.IsEnabled = !monoStyles.Contains(VectorFontStyle.BoldItalic);
            }
        }

        /// <summary>
        /// 槽位选项清单：内置字体（全平台）；系统默认仅系统发现平台（SystemFontsSupported）出现；
        /// body 基础槽无引用项；mono 基础槽引用正文；mono 样式槽引用正文+等宽。
        /// </summary>
        void ShowSlotDialog(FontSlot slot) {
            List<KeyValuePair<string, string>> options = [];
            if (FontManager.SystemFontsSupported) {
                options.Add(new(LanguageControl.GetContentWidgets(fName, "11"), "system"));
            }
            options.Add(new(LanguageControl.GetContentWidgets(fName, "17"), FontManager.BuiltinSlotValue));
            if (slot != FontSlot.Body) {
                options.Add(new(LanguageControl.GetContentWidgets(fName, "12"), "ref:body"));
            }
            if (IsMonoSlot(slot)
                && slot != FontSlot.Mono) {
                options.Add(new(LanguageControl.GetContentWidgets(fName, "13"), "ref:mono"));
            }
            options.Add(new(LanguageControl.GetContentWidgets(fName, "14"), "import"));
            options.Add(new(LanguageControl.GetContentWidgets(fName, "15"), "list"));
            DialogsManager.ShowDialog(
                null,
                new ListSelectionDialog(
                    null,
                    options,
                    60f,
                    item => ((KeyValuePair<string, string>)item).Key,
                    item => {
                        string value = ((KeyValuePair<string, string>)item).Value;
                        if (value == "import") {
                            ImportFontFlow(slot);
                        }
                        else if (value == "list") {
                            ShowImportedListDialog(slot);
                        }
                        else {
                            ApplySlot(slot, value);
                        }
                    }
                )
            );
        }

        /// <summary>
        /// 应用槽位取值并热切换：存档 → 一次 RebuildChains → 全屏重建。<br/>
        /// 基础槽先探测新源样式面（后台）做联动锁定，再重建——锁定与重建仍同一次 apply。
        /// </summary>
        void ApplySlot(FontSlot slot, string value) {
            SetSetting(slot, value);
            SettingsManager.SaveSettings();
            // 基础槽共用路径：探测新源样式面（后台）做联动锁定，再重建——锁定与重建仍同一次 apply
            void ApplyBaseSlot() {
                bool mono = IsMonoSlot(slot);
                FontManager.ProbeSourceStyles(
                    mono,
                    value,
                    delegate (HashSet<VectorFontStyle> styles) {
                        ApplyLinkedLocks(mono, styles);
                        SettingsManager.SaveSettings();
                        RebuildChainsAndScreens();
                    }
                );
            }
#if BROWSER
            // 浏览器系统字体跟随：基础槽取"系统默认"时借本次点击手势现场扫描（queryLocalFonts
            // 权限弹窗→枚举→命中面字节内存缓存，不落盘），完成后续走既有探测锁定+重建路径。
            // 未扫描/拒绝时 Discover 恒空，链回落 builtin 头+槽位。
            // 本会话扫描已成功过则直接用内存缓存应用（全量 blob 重取 ~250MB 免了）；
            // 未成功过（含此前拒绝）重借手势扫描=人工点击触发，无自动反馈环
            if (FontManager.SystemFontsSupported
                && IsBaseSlot(slot)
                && value == "system") {
                if (FontLocator.SystemFontsScanSucceeded) {
                    ApplyBaseSlot();
                }
                else {
                    FontManager.ScanSystemFontsAsync(ApplyBaseSlot);
                }
                return;
            }
#endif
            if (IsBaseSlot(slot)) {
                ApplyBaseSlot();
            }
            else {
                RebuildChainsAndScreens();
            }
        }

        /// <summary>联动锁定（spec §4）：基础槽源含样式面 → 对应样式槽锁定为引用值；源改回不含该样式时恢复可用并保持引用值。</summary>
        void ApplyLinkedLocks(bool mono, HashSet<VectorFontStyle> styles) {
            string lockValue = mono ? "ref:mono" : "ref:body";
            if (styles.Contains(VectorFontStyle.Bold)) {
                SetSetting(mono ? FontSlot.MonoBold : FontSlot.BodyBold, lockValue);
            }
            if (styles.Contains(VectorFontStyle.Italic)) {
                SetSetting(mono ? FontSlot.MonoItalic : FontSlot.BodyItalic, lockValue);
            }
            if (styles.Contains(VectorFontStyle.BoldItalic)) {
                SetSetting(mono ? FontSlot.MonoBoldItalic : FontSlot.BodyBoldItalic, lockValue);
            }
        }

        void RebuildChainsAndScreens() {
            FontManager.RebuildChains(RebuildScreens);
        }

        /// <summary>
        /// 导入字体流（控制器裁定：校验含 SharpAstro 试解析，可能数十 MB，整体离 UI 线程执行）。
        /// ChooseFile 的 await 与校验都在线程池，结果经 Dispatcher 编组回主线程后弹消息或应用。
        /// 过滤器格式按 Storage.ChooseFile 契约：WINDOWS/LINUX 走通配符（共享 Dialog.FileOpenEx 分支），
        /// BROWSER 走点前缀扩展名，安卓系统选择器忽略过滤器。
        /// </summary>
        void ImportFontFlow(FontSlot slot) {
            Task.Run(async () => {
                try {
#if WINDOWS || LINUX
                    KeyValuePair<string, string[]>[] filters = [new("Font", ["*.ttf", "*.otf", "*.ttc", "*.woff", "*.woff2"])];
#elif BROWSER
                    KeyValuePair<string, string[]>[] filters = [new("Font", [".ttf", ".otf", ".ttc", ".woff", ".woff2"])];
#else
                    KeyValuePair<string, string[]>[] filters = null; // 安卓：系统选择器忽略过滤器
#endif
                    (Stream stream, string fileName) = await Storage.ChooseFile(null, filters);
                    if (stream == null) {
                        return; // 用户取消（或选择器不可用且无文件名）
                    }
                    bool imported = false;
                    string error = null;
                    try {
                        imported = FontManager.ValidateAndImportFont(stream, fileName, out error);
                    }
                    finally {
                        await using (stream) { }
                    }
                    string safeName = imported
                        ? Storage.SanitizeFileNameSegment(Storage.GetFileName(fileName))
                        : null;
                    Dispatcher.Dispatch(
                        delegate {
                            if (!imported) {
                                DialogsManager.ShowDialog(
                                    null,
                                    new MessageDialog(LanguageControl.Error, error, LanguageControl.Ok, null, null)
                                );
                                return;
                            }
                            ApplySlot(slot, $"user:{safeName}");
                        }
                    );
                }
                catch (Exception ex) {
                    Dispatcher.Dispatch(
                        delegate {
                            DialogsManager.ShowDialog(
                                null,
                                new MessageDialog(LanguageControl.Error, ex.Message, LanguageControl.Ok, null, null)
                            );
                        }
                    );
                }
            });
        }

        /// <summary>已导入字体列表对话框（doc:Fonts，目录枚举轻量 IO，与语言选择对话框同类用法）。</summary>
        void ShowImportedListDialog(FontSlot slot) {
            List<string> fonts = FontManager.ListUserFonts();
            if (fonts.Count == 0) {
                DialogsManager.ShowDialog(
                    null,
                    new MessageDialog(LanguageControl.GetContentWidgets(fName, "16"), null, LanguageControl.Ok, null, null)
                );
                return;
            }
            fonts.Sort(StringComparer.OrdinalIgnoreCase);
            DialogsManager.ShowDialog(
                null,
                new ListSelectionDialog(
                    null,
                    fonts,
                    60f,
                    item => (string)item,
                    item => ApplySlot(slot, $"user:{item}")
                )
            );
        }

        /// <summary>
        /// 热切换的全屏重建：照 LanguageControl.ChangeLanguage 重建块（全表 Activator 重建 →
        /// 切屏 → 旧屏 Dispose）。不抽公共方法改写 ChangeLanguage：其块内含语言专属数据
        /// 重初始化（CraftingRecipes/Blocks/ClothingSlot/SubsystemPalette），字体热切换不需要，
        /// 参数化抽取反而增加耦合（控制器裁定允许两处各自保留）。<br/>
        /// 落点=用户当前屏（审查修复：探测是多 MB 后台任务，期间用户可能已按返回离开字体屏，
        /// 不得强行拉回）。返回引用修复（审查修复）：SwitchScreen 会把 PreviousScreen 覆写为旧
        /// 当前屏，故切换前先快照、切换后经旧→新映射恢复（不得等于 CurrentScreen，否则返回键
        /// 命中同屏早退把用户困死；快照缺失时退回设置主屏），历史栈同口径映射，均不指向已
        /// Dispose 旧屏。
        /// </summary>
        static void RebuildScreens() {
            // 整表重建共用口（ScreensManager.RebuildAllScreens）：GameScreen 世界绑定不可重建
            //（Activator 空壳=返回黑屏无输入），原样保留——其界面字体维持旧链直到重进世界
            ScreensManager.RebuildAllScreens(static s => s is GameScreen, "SettingsFont");
        }
    }
}
