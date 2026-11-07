using Engine;
using Engine.Media;

namespace Game {
    // HTML 部件：竖排块级布局（StackPanelWidget）。Html setter 仅标脏，首次 Measure 时重建
    // （字体/父宽在构建期不可得）；块级解析走 HtmlToWidgetsBuilder，行内走 HtmlSpanParser。
    // 子 RichTextWidget 的 LinkClicked 聚合转发。
    // 图片管线：本部件持有 RichContentImageLoader（纹理句柄生命周期归部件）——
    // UpdateCeases（被移出更新树，ContainerWidget 级联）与 Rebuild（内容变更）时全量释放
    public class HtmlWidget : StackPanelWidget {
        string m_html;

        bool m_needsBuild = true;

        public string Html {
            get => m_html;
            set {
                if (value != m_html) {
                    m_html = value;
                    m_needsBuild = true;
                }
            }
        }

        // 字体；null 回退 FontManager.FallbackBitmapFont（初始化失败已自动回退 DebugFont）
        public BitmapFont Font { get; set; }

        // 基础字号（标题阶梯与行内 FontScale 均以此为基准）
        public float FontScale { get; set; } = 1f;

        // span 未指定颜色时的默认色
        public Color DefaultColor { get; set; }

        // <0 跟随父可用宽
        public float ContentWidth { get; set; } = -1f;

        // 相对图片 URL 的解析基准；null = 仅接受绝对 URL
        public string ImageBaseUrl {
            get => m_imageLoader.BaseUrl;
            set => m_imageLoader.BaseUrl = value;
        }

        // Rebuild 时保留图片纹理缓存（默认释放）：自动翻译重建等新旧内容含相同图片的场景，
        // 保留可复用已下载纹理，不重下（旧 frame 随 Children 更换丢弃，纹理由 UpdateCeases 统一释放）
        public bool PreserveImagesOnRebuild { get; set; }

        public event Action<string> LinkClicked;

        readonly HtmlToWidgetsBuilder m_builder = new();

        // 图片纹理生命周期归本部件（widget 树无 Dispose 级联，UpdateCeases 释放）
        readonly RichContentImageLoader m_imageLoader = new();

        public HtmlWidget() {
            Direction = LayoutDirection.Vertical;
            DefaultColor = Color.White;
            // 块容器横向撑满宿主格：内部 Center 对齐（图片块等）的参考系恒为宿主内容宽，
            // 不随内容最宽子件塌缩（窄内容居中会整体左漂）
            HorizontalAlignment = WidgetAlignment.Stretch;
        }

        public override void MeasureOverride(Vector2 parentAvailableSize) {
            IsDrawRequired = true;
            if (m_needsBuild) {
                Rebuild();
            }
            // 代码块外层宽度：构建时父宽未知，测量期按可用宽解析（扣减构建时记录的嵌套缩进，
            // 列表项/引用块内不占全宽）；祖先有定宽 sizer 时钳到 sizer 内容宽（防回填击穿）。
            // flex 容器内跳过：主轴 basis=构建期自然宽（CreateCodeBlock flexNaturalBasis），
            // 容器净宽回填与 flex 排布互斥（FlexPanelWidget.IsInsideFlex）
            float contentWidth = ContentWidth > 0f ? ContentWidth : parentAvailableSize.X;
            foreach ((CanvasWidget codeBlock, float indentX) in m_builder.CodeBlocks) {
                if (FlexPanelWidget.IsInsideFlex(codeBlock, this)) {
                    continue;
                }
                codeBlock.Size = new Vector2(
                    MathUtils.Max(MathUtils.Min(contentWidth - indentX, HtmlBlockSizerWidget.ResolveAncestorCap(codeBlock, this, contentWidth)), 50f),
                    codeBlock.Size.Y
                );
            }
            // 图片块同机制；已加载完成的 frame 宽=图宽、width/height 属性帧宽=属性值，均不覆盖
            //（HorizontalAlignment=Center 居中）；pending 帧回填保留（含 flex 内——frame.Size.X>0
            // 才能被 ResolvePendingWidths 收口适配上屏）
            foreach ((CanvasWidget imageBlock, float indentX) in m_builder.ImageBlocks) {
                if (!m_imageLoader.IsFrameApplied(imageBlock)
                    && !m_imageLoader.IsFrameExplicit(imageBlock)) {
                    imageBlock.Size = new Vector2(
                        MathUtils.Max(
                            MathUtils.Min(contentWidth - indentX, HtmlBlockSizerWidget.ResolveAncestorCap(imageBlock, this, contentWidth)),
                            50f
                        ),
                        imageBlock.Size.Y
                    );
                }
            }
            // 宽已解析的挂起 frame（纹理先于测量就绪：缓存命中/秒图）此刻补适配上屏
            m_imageLoader.ResolvePendingWidths();
            base.MeasureOverride(parentAvailableSize);
        }

        public override void UpdateCeases() {
            // 框架把本部件移出更新树时调用（ContainerWidget 级联）：释放图片纹理句柄并标记已释放，
            // 晚到的图片回调就地销毁新纹理，不再加入列表
            m_imageLoader.ReleaseTextures();
            base.UpdateCeases();
        }

        // 锚点跳转（宿主把 <a href="#name"> 的点击转发进来）：滚动最近宿主 ScrollPanelWidget
        // 使目标块顶对齐视口顶（Chrome fragment 语义，不判可见性）。空名=回顶；未知名/无滚动
        // 宿主=false（宿主据此决定不外抛浏览器）。定位基于当下布局，异步图片加载后的高度变化
        // 不重定位（陈旧已知，v1 接受）
        public bool TryScrollToAnchor(string name) {
            bool isTopRequest = string.IsNullOrEmpty(name);
            Widget target = null;
            if (!isTopRequest
                && !m_builder.Anchors.TryGetValue(name, out target)) {
                return false;
            }
            for (Widget ancestor = ParentWidget; ancestor != null; ancestor = ancestor.ParentWidget) {
                if (ancestor is ScrollPanelWidget scrollPanel) {
                    // 子件排布于 −ScrollPosition：面板空间 Y + ScrollPosition=内容空间 Y
                    scrollPanel.ScrollPosition = isTopRequest
                        ? 0f
                        : MathUtils.Max(scrollPanel.ScreenToWidget(target.WidgetToScreen(Vector2.Zero)).Y + scrollPanel.ScrollPosition, 0f);
                    return true;
                }
            }
            return false;
        }

        void Rebuild() {
            // 内容变更：默认旧内容的图片纹理立即全量释放（loader 代际递增使旧下载晚到回调就地销毁），
            // 缓存一并清空；PreserveImagesOnRebuild 时保留缓存供新内容复用（翻译重建）。
            // 同类内 Html 相同值不会走到这（setter 判等）
            if (!PreserveImagesOnRebuild) {
                m_imageLoader.ReleaseTextures();
            }
            // 缩进栈归零：构建中途异常留下的脏缩进不得传染下一次构建；
            // CurrentPanel 同族防护——脏 panel 会把下一次构建的顶层块路由进离树格，
            // 宿主回填循环触不到，shell 宽停留构建期值
            m_builder.CurrentIndentX = 0;
            m_builder.CurrentPanel = null;
            Children.Clear();
            m_builder.CodeBlocks.Clear();
            m_builder.ImageBlocks.Clear();
            m_builder.Anchors.Clear();
            m_builder.Font = Font ?? FontManager.FallbackBitmapFont;
            m_builder.FontScale = FontScale;
            m_builder.DefaultColor = DefaultColor;
            m_builder.ContentWidth = ContentWidth;
            m_builder.ImageLoader = m_imageLoader;
            m_builder.LinkClicked = null;
            if (LinkClicked != null) {
                m_builder.LinkClicked += href => LinkClicked(href);
            }
            try {
                StackPanelWidget content = m_builder.BuildHtml(m_html, 0, null, 0);
                // content 面板仅作构建容器，块级 widget 直接上提到本部件，减少一层嵌套
                foreach (Widget block in content.Children.ToArray()) {
                    content.Children.Remove(block);
                    Children.Add(block);
                }
            }
            catch (Exception e) {
                // 解析/构建失败不能留在半空状态：清残留 + 兜底错误段落。
                // 同一输入结果确定，清脏防每帧 Measure 重试
                m_builder.CodeBlocks.Clear();
                m_builder.ImageBlocks.Clear();
                m_builder.Anchors.Clear();
                Children.Add(
                    new FontTextWidget { Text = "(HTML 渲染失败：" + e.Message + ")", Font = m_builder.Font, Color = DefaultColor, WordWrap = true }
                );
            }
            m_needsBuild = false;
        }
    }
}