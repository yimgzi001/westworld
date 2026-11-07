using System.Globalization;
using System.Xml.Linq;
using Engine;

namespace Game {
    public sealed class CommunityPostDetailsDialog : Dialog {
        readonly StackPanelWidget m_contentPanel;
        readonly ButtonWidget m_viewInBrowserButton;
        readonly ButtonWidget m_primaryButton;
        readonly ButtonWidget m_closeButton;
        readonly LabelWidget m_titleLabel;
        readonly LabelWidget m_translateStatusLabel;
        readonly Action m_primaryAction;
        readonly string m_baseUrl;

        readonly string m_postUrl;

        // content 区只放服务端来源内容（HTML 或纯文本二选一）；header 区放本地结构化数据，
        // 不参与自动翻译——翻译只发送 m_contentHtml，重建时 header 原地不动。
        readonly string m_contentHtml;

        readonly string m_contentPlainText;

        // content 区正文：HtmlWidget 持有 HTML→widget 构建与图片纹理生命周期，
        // UpdateCeases（对话框移除，级联）释放纹理；翻译重建只换 Html 属性（loader 缓存复用）。
        readonly HtmlWidget m_htmlWidget;
        Task<AutoTranslateResult> m_autoTranslateTask;
        CancellationTokenSource m_autoTranslateCancellation;
        int m_autoTranslateRequestId;

        public CommunityPostDetailsDialog(string title,
            string html,
            string baseUrl = null,
            string primaryButtonText = null,
            Action primaryAction = null,
            bool enableAutoTranslate = false,
            Action<StackPanelWidget> buildHeader = null,
            string contentPlainText = null,
            int postId = 0) {
            m_baseUrl = baseUrl;
            m_primaryAction = primaryAction;
            m_postUrl = BuildPostWebUrl(CommunityServerManager.CurrentChineseInfo.WebsiteUrl, postId);
            m_contentHtml = html ?? string.Empty;
            m_contentPlainText = contentPlainText;
            XElement node = ContentManager.Get<XElement>("Dialogs/CommunityPostDetailsDialog");
            LoadContents(this, node);
            m_titleLabel = Children.Find<LabelWidget>("PostDetailsDialog.Title");
            m_titleLabel.Text = string.IsNullOrWhiteSpace(title) ? LanguageControl.Get("PostDetailsDialog", "PostDetails") : title;
            m_contentPanel = Children.Find<StackPanelWidget>("PostDetailsDialog.ContentPanel");
            StackPanelWidget headerPanel = Children.Find<StackPanelWidget>("PostDetailsDialog.HeaderPanel");
            Widget actionsRow = Children.Find<Widget>("PostDetailsDialog.ActionsRow");
            m_viewInBrowserButton = Children.Find<ButtonWidget>("PostDetailsDialog.ViewInBrowserButton");
            m_primaryButton = Children.Find<ButtonWidget>("PostDetailsDialog.PrimaryButton");
            m_closeButton = Children.Find<ButtonWidget>("PostDetailsDialog.CloseButton");
            m_translateStatusLabel = Children.Find<LabelWidget>("PostDetailsDialog.TranslateStatus");
            // DefaultColor 对齐 ReleasesScreen 约定（正文 224 灰）；FontScale 维持 1f——富文本正文
            // 字号大于旧降级渲染（0.86）属有意取舍，勿当遗漏补小
            m_htmlWidget = new HtmlWidget { ImageBaseUrl = m_baseUrl, PreserveImagesOnRebuild = true, DefaultColor = new Color(224, 224, 224) };
            // # 开头=站内锚点：滚动正文锚点（空 fragment=回顶），命中与否都不外抛浏览器；
            // 带 fragment 的外部 URL（page.html#x）仍按外链处理
            m_htmlWidget.LinkClicked += href => {
                if (href.StartsWith("#", StringComparison.Ordinal)) {
                    m_htmlWidget.TryScrollToAnchor(href.Substring(1));
                    return;
                }
                WebBrowserManager.LaunchBrowser(href);
            };
            m_contentPanel.Children.Add(m_htmlWidget);
            bool hasPrimaryAction = !string.IsNullOrWhiteSpace(primaryButtonText) && primaryAction != null;
            // 行内含关闭按钮，恒可见；主按钮/浏览器按钮按有无动作与链接各自显隐。
            actionsRow.IsVisible = true;
            m_primaryButton.IsVisible = hasPrimaryAction;
            m_viewInBrowserButton.IsVisible = !string.IsNullOrWhiteSpace(m_postUrl);
            if (hasPrimaryAction) {
                m_primaryButton.Text = primaryButtonText;
            }
            if (buildHeader != null) {
                buildHeader(headerPanel);
                headerPanel.IsVisible = headerPanel.Children.Count > 0;
            }
            BuildContent();
            if (enableAutoTranslate) {
                StartAutoTranslate();
            }
        }

        public override void Update() {
            if (Input.Back
                || Input.Cancel
                || m_closeButton.IsClicked
                || (Input.Tap.HasValue && !HitTest(Input.Tap.Value))) {
                DialogsManager.HideDialog(this);
                return;
            }
            if (m_viewInBrowserButton.IsClicked) {
                WebBrowserManager.LaunchBrowser(m_postUrl);
            }
            if (m_primaryButton.IsClicked) {
                DialogsManager.HideDialog(this);
                m_primaryAction?.Invoke();
            }
            UpdateAutoTranslateTask();
        }

        public override void UpdateCeases() {
            // 图片纹理由 m_htmlWidget.UpdateCeases 级联释放（ContainerWidget 移出更新树时）。
            // 取消在途翻译 HTTP 请求：对话框已移除，结果无消费方。
            m_autoTranslateCancellation?.Cancel();
            base.UpdateCeases();
        }

        void BuildContent(string translatedHtml = null) {
            // m_htmlWidget 恒为面板首子件，重建只截掉其后的降级/占位 label——不摘部件：
            // Children 移除会 ChangeParent(null)→UpdateCeases 释放纹理缓存，毁掉翻译重建复用。
            // 重建不释放纹理（PreserveImagesOnRebuild）：已加载图片入缓存供新内容复用
            // （自动翻译重建不再让第一批纹理成孤儿、同图下载两次），对话框关闭时统一释放
            while (m_contentPanel.Children.Count > 1) {
                m_contentPanel.Children.RemoveAt(1);
            }
            string html = translatedHtml ?? m_contentHtml;
            // 会产出可见内容的判定与构建器产出一致：可见文本（script/style 不计）或图片；
            // 两者皆无（空 DOM/纯 script）走纯文本降级，再否则占位文案
            bool appended = !string.IsNullOrWhiteSpace(html)
                && (HtmlTextUtils.HasVisibleText(html) || html.Contains("<img", StringComparison.OrdinalIgnoreCase));
            if (appended) {
                m_htmlWidget.Html = html;
                return;
            }
            // 正文无可见内容：清空部件旧内容（纹理保留），展示纯文本降级/占位
            m_htmlWidget.Html = null;
            if (!string.IsNullOrWhiteSpace(m_contentPlainText)) {
                AppendPlainText(m_contentPanel, m_contentPlainText);
                appended = true;
            }
            if (!appended) {
                AppendParagraph(m_contentPanel, LanguageControl.Get("PostDetailsDialog", "NoDetails"));
            }
        }

        void StartAutoTranslate() {
            string translateUrl = GetTranslateUrl();
            string targetLanguage = LanguageControl.CurrentLanguageSimpleEnglishName;
            bool hasText = HtmlTextUtils.HasVisibleText(m_contentHtml);
            bool hasTitle = !string.IsNullOrWhiteSpace(m_titleLabel?.Text);
            if (string.IsNullOrWhiteSpace(translateUrl)
                || string.IsNullOrWhiteSpace(targetLanguage)
                || string.IsNullOrWhiteSpace(m_contentHtml)
                || (!hasText && !hasTitle)) {
                return;
            }
            SetTranslateStatus(
                string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("PostDetailsDialog", "TranslatingTo"), targetLanguage),
                new Color(160, 210, 255)
            );
            // 服务端对相同内容有翻译缓存：先请求、返回后一次性建 Widget，不先建原文再重建。
            // 等待期清正文：只截 m_htmlWidget 之后的降级 label，部件不摘（摘除触发 UpdateCeases
            // 释放纹理缓存）；Html 置空重建即视觉清空，纹理保留待译文复用
            while (m_contentPanel.Children.Count > 1) {
                m_contentPanel.Children.RemoveAt(1);
            }
            m_htmlWidget.Html = null;
            int requestId = ++m_autoTranslateRequestId;
            string title = m_titleLabel?.Text;
            m_autoTranslateCancellation?.Cancel();
            m_autoTranslateCancellation?.Dispose();
            m_autoTranslateCancellation = new CancellationTokenSource();
            CancellationToken cancellationToken = m_autoTranslateCancellation.Token;
            m_autoTranslateTask = Task.Run(() => TranslateDialogAsync(
                    requestId,
                    translateUrl,
                    targetLanguage,
                    title,
                    m_contentHtml,
                    cancellationToken
                )
            );
        }

        void UpdateAutoTranslateTask() {
            if (m_autoTranslateTask is null or { IsCompleted: false }) {
                return;
            }
            AutoTranslateResult result;
            try {
                result = m_autoTranslateTask.Result;
            }
            catch (Exception ex) {
                result = new AutoTranslateResult { Success = false, Error = ex.Message };
            }
            finally {
                m_autoTranslateTask = null;
            }
            if (result == null
                || result.RequestId != m_autoTranslateRequestId) {
                return;
            }
            if (!result.Success) {
                if (!string.IsNullOrWhiteSpace(result.Error)) {
                    Log.Error(
                        string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("PostDetailsDialog", "AutoTranslateFailed"), result.Error)
                    );
                    SetTranslateStatus(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            LanguageControl.Get("PostDetailsDialog", "AutomaticTranslationFailed"),
                            result.Error
                        ),
                        new Color(255, 170, 170)
                    );
                    BuildContent();
                }
                return;
            }
            if (!string.IsNullOrWhiteSpace(result.TranslatedTitle)) {
                m_titleLabel.Text = result.TranslatedTitle;
            }
            SetTranslateStatus(
                string.Format(
                    CultureInfo.InvariantCulture,
                    LanguageControl.Get("PostDetailsDialog", "AutomaticallyTranslatedTo"),
                    result.TargetLanguage
                ),
                new Color(170, 230, 170)
            );
            BuildContent(result.TranslatedHtml);
        }

        void SetTranslateStatus(string text, Color color) {
            m_translateStatusLabel.Text = text;
            m_translateStatusLabel.Color = color;
            m_translateStatusLabel.IsVisible = !string.IsNullOrWhiteSpace(text);
        }

        string GetTranslateUrl() =>
            Uri.TryCreate(m_baseUrl, UriKind.Absolute, out Uri baseUri) ? $"{baseUri.AbsoluteUri.TrimEnd('/')}/translate" : null;

        // —— 纯文本降级渲染（原 PostDetailsContentBuilder.AppendPlainText/AppendParagraph 本地化，样式同源）——
        // 纯文本（Git release notes 等）逐行渲染：行解析在 HtmlTextUtils.SplitPlainTextLines
        static void AppendPlainText(StackPanelWidget panel, string text) {
            foreach ((bool isListItem, string line) in HtmlTextUtils.SplitPlainTextLines(text)) {
                if (isListItem) {
                    AppendListItem(panel, "• " + line);
                }
                else {
                    AppendParagraph(panel, line);
                }
            }
        }

        static void AppendParagraph(StackPanelWidget panel, string text) {
            if (!string.IsNullOrWhiteSpace(text)) {
                panel.Children.Add(new CanvasWidget { Size = new Vector2(0f, 6f) });
                panel.Children.Add(
                    new LabelWidget { Text = text, Color = new Color(225, 225, 225), FontScale = 0.86f, WordWrap = true }
                );
            }
        }

        static void AppendListItem(StackPanelWidget panel, string text) {
            if (!string.IsNullOrWhiteSpace(text)) {
                panel.Children.Add(new CanvasWidget { Size = new Vector2(0f, 3f) });
                panel.Children.Add(
                    new LabelWidget { Text = text, Color = new Color(225, 225, 225), FontScale = 0.86f, WordWrap = true, MarginLeft = 22f }
                );
            }
        }

        /// <summary>
        /// 帖子网页链接：服务器网站根 + /#/postDetails/{postId}（形如 https://test.suancaixianyu.cn/#/postDetails/1059）。
        /// postId 无效或 WebsiteUrl 缺失/非 HTTP(S) 时返回 null，调用方隐藏"浏览器中查看"按钮。
        /// </summary>
        public static string BuildPostWebUrl(string websiteUrl, int postId) {
            if (postId <= 0
                || !Uri.TryCreate(websiteUrl, UriKind.Absolute, out Uri uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) {
                return null;
            }
            return $"{uri.Scheme}://{uri.Authority}/#/postDetails/{postId}";
        }

        static async Task<AutoTranslateResult> TranslateDialogAsync(int requestId,
            string translateUrl,
            string targetLanguage,
            string title,
            string originalHtml,
            CancellationToken cancellationToken) {
            try {
                CommunityTranslateClient client = CommunityTranslateClient.Shared;
                // 标题与正文两请求相互独立，并行发出再逐项取结果（详情打开省一次串行往返）。
                Task<string> titleTask = string.IsNullOrWhiteSpace(title)
                    ? Task.FromResult(title)
                    : client.TranslateAsync(translateUrl, title, targetLanguage, cancellationToken);
                Task<string> htmlTask = client.TranslateAsync(translateUrl, originalHtml, targetLanguage, cancellationToken);
                await Task.WhenAll(titleTask, htmlTask);
                return new AutoTranslateResult {
                    RequestId = requestId,
                    Success = true,
                    TargetLanguage = targetLanguage,
                    TranslatedTitle = titleTask.Result,
                    TranslatedHtml = htmlTask.Result
                };
            }
            catch (Exception ex) {
                return new AutoTranslateResult { RequestId = requestId, Success = false, Error = ex.Message, TargetLanguage = targetLanguage };
            }
        }

        sealed class AutoTranslateResult {
            public int RequestId { get; set; }
            public bool Success { get; set; }
            public string TargetLanguage { get; set; }
            public string TranslatedTitle { get; set; }
            public string TranslatedHtml { get; set; }
            public string Error { get; set; }
        }
    }
}