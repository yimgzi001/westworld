using System.Text;
using Engine;
using Engine.Graphics;

namespace Game {
    // HtmlAgilityPack DOM → widget 树（块级遍历）。外部 HTML 片段入口（P2：HtmlWidget 壳、
    // CommunityPostDetailsDialog 正文）；MarkdownToWidgetsBuilder 的 HtmlBlock 分支也转发到此，
    // 产出的块注册表（CodeBlocks/ImageBlocks）由转发方合并回自己的集合供宿主测量期回填宽度。
    // 行内内容走 HtmlSpanParser
    public class HtmlToWidgetsBuilder : RichContentBuilder {
        // body bgcolor 根回退（cell>row>table>body 链最外级）：表格格/行/表均无 bgcolor 时回退到此。
        // 构建入口重置
        Color? m_bodyBackground;

        // body text=/link=：整页默认文字色/链接色（老帖源码常带）。vlink/alink 无访问状态追踪，
        // 静默忽略。构建入口重置
        Color? m_bodyTextColor;

        Color? m_bodyLinkColor;

        // 锚点注册表（id=/a name= → 块件）：构建期登记、HtmlWidget 持有消费（同 CodeBlocks/
        // ImageBlocks 所有权模型）。同名先到先得（浏览器 duplicate-id 口径）
        public readonly Dictionary<string, Widget> Anchors = [];

        // 待挂锚点名（id= 任意标签 / name= 仅 <a>，块级入口与行内子入口双挂）：首个产块点
        //（Flush 段落 / AttachBlock 块件挂载）领取——行内锚点落容纳块（裁定粒度）。构建入口清空
        //（同 m_pendingIndent 防传染）
        readonly List<string> m_pendingAnchors = [];

        // text-indent 待挂缩进（块级 case 入口 Flush 关边界后挂，Flush 产段落件时消费清零）。
        // 消费模型=「声明节点的行内内容首个段落吃缩进」：块级子经 Flush 产段时先到先得
        //（CSS text-indent 继承的近似——子块首段继承吃缩进）。所有权跟踪：m_pendingIndentOwner
        // 记挂载节点，各挂接 case 在子树递归完成后判所有者回收（ClearUnconsumedTextIndent）——
        // 空段/仅直建块（标题/pre/table）/仅列表（FlushLi 不读 pending）等未消费情形在块外
        // 截断，防泄漏给后续兄弟块；被子树内段落消费或被更深声明覆写（所有者已换人/清零）则不动
        float m_pendingIndentPx;

        float? m_pendingIndentPercent;

        HtmlAgilityPack.HtmlNode m_pendingIndentOwner;

        // flex 容器嵌套深度（CreateFlexContainer 子项构建期递增/递减）：>0 时 CreateCodeBlock
        // 走 flexNaturalBasis（主轴 basis=自然宽）。构建入口清零——构建中途异常留下的脏深度
        // 不得传染下一次构建（同 m_pendingIndent 防传染口径）
        int m_flexDepth;

        // HTML 片段入口：解析为 DOM 后做块级遍历，块收进自建面板返回（HtmlWidget 壳用）
        public StackPanelWidget BuildHtml(string html, int listDepth, Color? colorOverride, int depth) {
            StackPanelWidget panel = CreatePanel();
            BuildHtmlInto(html, panel, listDepth, colorOverride, depth);
            return panel;
        }

        // 平铺入口：块直接追加进调用方面板（Markdown HtmlBlock 转发用，避免多包一层嵌套面板）
        public void BuildHtmlInto(string html, StackPanelWidget panel, int listDepth, Color? colorOverride, int depth) {
            m_bodyBackground = null;
            m_bodyTextColor = null;
            m_bodyLinkColor = null;
            // 待挂缩进同步清零：上次构建尾部残留（如收尾空段未消费）不得流入本次构建
            m_pendingIndentPx = 0f;
            m_pendingIndentPercent = null;
            m_pendingIndentOwner = null;
            // flex 深度同步清零：构建中途异常留下的脏深度不得传染下一次构建
            m_flexDepth = 0;
            m_pendingAnchors.Clear();
            // 宽约束栈同步清空：构建中途异常留下的脏种子不得传染下一次构建
            WidthConstraints.Clear();
            HtmlAgilityPack.HtmlDocument doc = new();
            doc.LoadHtml(html ?? string.Empty);
            BuildHtmlNodes(doc.DocumentNode.ChildNodes, panel, listDepth, colorOverride, depth);
        }

        // 锚点挂起：id=（任意标签）+ name=（仅 <a>，裁定 E 双轨）。留档：标题/表格格自身的
        // 行内子级锚点不经此处（heading span 直解、纯行内格整节点 span 解析，子节点形不可见）
        void PendAnchor(HtmlAgilityPack.HtmlNode node) {
            string id = node.GetAttributeValue("id", null);
            if (!string.IsNullOrEmpty(id)) {
                m_pendingAnchors.Add(id);
            }
            if (node.Name.Equals("a", StringComparison.OrdinalIgnoreCase)) {
                string name = node.GetAttributeValue("name", null);
                if (!string.IsNullOrEmpty(name)) {
                    m_pendingAnchors.Add(name);
                }
            }
        }

        // 领取全部挂起锚点名 → block（行内 a name/id 落容纳块、块 id 落块根，未产件的空容器
        // 自然滚存给后继块——空目标与后继块顶同位近似）；同名先到先得（浏览器 duplicate-id 口径）
        void ClaimAnchors(Widget block) {
            foreach (string anchorName in m_pendingAnchors) {
                if (!Anchors.ContainsKey(anchorName)) {
                    Anchors.Add(anchorName, block);
                }
            }
            m_pendingAnchors.Clear();
        }

        // 默认文字色口径：显式 colorOverride > body text= > 调色板 DefaultColor
        Color EffectiveTextColor(Color? colorOverride) => colorOverride ?? m_bodyTextColor ?? DefaultColor;

        // base.CreateHeading 走虚分发回到这里：标题色由 HeadingColor 固定（产品配色，非 body
        // text= 语义——浏览器标题继承 body 色是另一口径，留档不跟）
        protected override RichTextWidget CreateRichText(Color? colorOverride) {
            RichTextWidget widget = base.CreateRichText(colorOverride);
            if (colorOverride == null
                && m_bodyTextColor.HasValue) {
                widget.Color = m_bodyTextColor.Value;
            }
            return widget;
        }

        // 盒属性 em/% 上下文（壳方案 WrapInStyleBox/TryBeginStyleBox + ApplyBlockSizing 补偿 +
        // li 行框收窄四个 TryGetBox 消费点同源，防解析口径漂移）。tier-1.5 口径（tier-1 升级，
        // builder 走链字号 ctx 线程）：em 基准=16×fontScaleCtx，本元素 style font-size 声明存在时
        // 按单位分派——绝对单位（px/pt/rem，TryParseCssFontSizeUnit absolute）=16×ratio（ctx 不乘，
        // 绝对值覆盖）；相对单位（em/%）=16×ctx×ratio；无声明/声明解析失败=16×ctx（垃圾 font-size
        // 不丢整条盒声明）。ctx 来源（ResolveFontScaleCtx/ResolveUaTagFontScale/HeadingScale 三乘点）：
        // 标题阶梯（h1-h6 UA font-size 对自身与子树同生效）+ UA 标签（small/big/sub/sup/font 包块
        // 时 default 块级重入口乘）+ style font-size 块级继承（通用块 case 对子树的传导——绝对
        // 声明换底 ratio、相对声明 ctx×ratio）。留档残余：CSS font-size 作者声明按级联应整体替换
        // UA 置位（<h2 style="font-size:200%"> 严格口径=32px、ctx 组合口径=44.8px），此处取 ctx
        // 组合近似（与行内 inheritedScale 乘链同款取舍）；深嵌套 UA 标签链仅在 default
        // ContainsBlockChild 重入口可达——li 直发块名单/格 hasBlockChild 检出的 IsBlockTag 名单
        // 均不含 small/big/sub/sup/font，<li><small><div>、<td><small><div> 被 span 平铺（既有限
        // 制，非本波引入）。% 基准=NestedContentWidth()（包含块内容宽近似：含祖先左缩进，下限 50
        // 由基类钳制），ContentWidth<=0（跟随宿主）传 -1=该边 % 声明维持丢弃
        (float EmBase, float PercentBase) BoxLengthContext(string style, float fontScaleCtx = 1f) {
            float emBase = 16f * fontScaleCtx;
            if (CssStyleParser.TryGetDeclaration(style, "font-size", out string sizeDecl)
                && HtmlSpanParser.TryParseCssFontSizeUnit(sizeDecl, out float ratio, out bool absolute)) {
                emBase = absolute ? 16f * ratio : 16f * fontScaleCtx * ratio;
            }
            float nestedWidth = NestedContentWidth();
            return (emBase, nestedWidth > 0f ? nestedWidth : -1f);
        }

        // style 盒属性（padding/margin/background-color）壳方案：带声明时包一层 StackPanel 壳——
        // 壳 Margin=CSS margin（壳外距），子件四边距=CSS padding（壳内缘到内容），背景/边框由壳
        // Draw 铺整幅（背景垫边框之下）。浏览器盒子模型同构：border 波边框画在壳矩形上时 padding
        // 天然落框内。无相关声明返回 null（零开销直通）。
        // 单件版：叶子块（标题/引用/代码块/hr/表格/列表整体）。
        // fontScaleCtx：所在元素继承的字号 ctx（tier-1.5，BoxLengthContext 消费；默认 1f=
        // tier-1 口径，Markdown 转发路径零改）
        Widget WrapInStyleBox(HtmlAgilityPack.HtmlNode node,
            Widget widget,
            Color? colorOverride,
            bool backgroundViaShell = true,
            float fontScaleCtx = 1f) {
            string style = node.GetAttributeValue("style", null);
            // margin/padding em/% 上下文（tier-1.5 口径见 BoxLengthContext 留档）
            (float emBase, float percentBase) = BoxLengthContext(style, fontScaleCtx);
            bool hasMargin = CssStyleParser.TryGetBox(style, "margin", out Vector4 margin, emBase, percentBase);
            bool hasPadding = CssStyleParser.TryGetBox(style, "padding", out Vector4 padding, emBase, percentBase);
            // TryGetBorder 返回=有声明（含全禁），起壳查 HasAny（全禁=无边框不起壳）
            bool hasBorder = CssStyleParser.TryGetBorder(style, out CssStyleParser.CssBorderSpec border) && border.HasAny;
            // background-color：TryParse 失败=无效值整条丢弃不起壳（CSS 解析期丢弃口径）。
            // bgColor 预声明：&& 右侧 out 隔着 bool 变量不保明确赋值（C# 条件调用模式）
            Color bgColor = default;
            // backgroundViaShell=false：自绘背景的宿主（table 格链）豁免壳铺底——壳整幅填充
            // 会与格底双重混色且把 Margin 带染色（CSS margin 区不着色）
            bool hasBackground = backgroundViaShell
                && CssStyleParser.TryGetDeclaration(style, "background-color", out string bgDecl)
                && CssColorParser.TryParse(bgDecl, out bgColor)
                // transparent（A=0）=无背景：免壳省每帧 Draw 调度
                && bgColor.A > 0;
            // opacity：声明有效且 v<1 才参与起壳（v==1 无壳无意义）；壳只承担装饰淡出，
            // 文字淡出走 ctx 递归链（调用点 ResolveOpacityMultiplier 同源解析）
            bool hasOpacity = ParseOpacityStyle(node, out float nodeOpacity) && nodeOpacity < 1f;
            // border-radius：bg/border 的形状修饰，不参与起壳判定（单独声明无壳不画——壳是
            // 绘制物本体）；px 愿望 + % 两路存（Draw 期 ActualSize 解析）
            bool hasRadius = CssStyleParser.TryParseBorderRadius(style, out Vector4 cornerPx, out Vector4 cornerPct);
            // 纯 opacity 不起壳：壳无装饰可淡（文字淡出走 ctx 递归链），白付一层 wrapper+
            // 对齐移交；有 margin/padding/border/background 时壳装饰随 v 淡出
            if (!hasMargin
                && !hasPadding
                && !hasBorder
                && !hasBackground) {
                return null;
            }
            // CSS border-box：border 宽并入内缩（内容区=壳宽−border−padding），边框画壳外缘不压内容
            ApplyBoxEdges(widget, padding + (hasBorder ? border.Widths : Vector4.Zero), true, true);
            HtmlStyleBoxWidget shell = new() { Direction = LayoutDirection.Vertical };

            // 壳装饰色乘 v（预乘淡出，与 GlobalColorTransform 乘法交换；==1 恒等零回归）——
            // 壳不走 ColorTransform：Arrange 会级联浸透子树，与 ctx 落点双淡
            Color? Decoration(Color? c) => hasOpacity && c.HasValue ? c.Value * nodeOpacity : c;
            if (hasBackground) {
                shell.BackgroundColor = Decoration(bgColor);
            }
            if (hasBorder) {
                shell.BorderWidths = border.Widths;
                shell.BorderLeftColor = Decoration(border.Left);
                shell.BorderTopColor = Decoration(border.Top);
                shell.BorderRightColor = Decoration(border.Right);
                shell.BorderBottomColor = Decoration(border.Bottom);
                shell.CurrentColor = Decoration(EffectiveTextColor(colorOverride)).Value;
            }
            if (hasMargin) {
                shell.Margin4 = margin;
            }
            if (hasRadius) {
                shell.CornerRadius = cornerPx;
                shell.CornerPercent = cornerPct;
            }
            // 对齐移交：壳顶替子件原外层对齐位，子件改 Stretch 填壳（Near/Center/Stretch 视觉不变）
            shell.HorizontalAlignment = widget.HorizontalAlignment;
            widget.HorizontalAlignment = WidgetAlignment.Stretch;
            shell.Children.Add(widget);
            return shell;
        }

        // 块级 CSS width/max-width 咽喉点（单件出口统一经此）：先盒壳（margin/padding/border/
        // background）后尺寸件——sizer 恒最外，两族（壳出口/容器壳出口）同 style 同几何：
        // px 声明经 content-box 补偿后壳内内容恒=声明值，% 分母=宿主净宽。无 sizing 声明=
        // ApplyBlockSizing 原样直通（零回归）。仅用于 WrapInStyleBox 出口（h1-6/blockquote/
        // pre/hr/table/dd）；TryBeginStyleBox 容器/段落壳出口的盒已被壳消费，直接
        // ApplyBlockSizing（壳作内层，同归 sizer 最外，不再重包壳防二次吃盒属性）
        // transform 壳也由本出口收口（ApplyBlockSizing 尾部统一 ApplyBlockTransform）：
        // 变换壳恒最外（sizer/盒壳之上），CSS transform 作用于整个盒含 margin 效果近似。
        // fontScaleCtx：所在元素继承的字号 ctx（标题 case 传入=进 case 已乘阶梯，自身盒壳
        // em/% 按阶梯字号解析——<h2 style="padding:0.5em">=11.2px；默认 1f=tier-1 口径）
        Widget FinalizeBlock(HtmlAgilityPack.HtmlNode node,
            Widget widget,
            Color? colorOverride,
            bool backgroundViaShell = true,
            float fontScaleCtx = 1f) {
            Widget shell = WrapInStyleBox(node, widget, colorOverride, backgroundViaShell, fontScaleCtx);
            return ApplyBlockSizing(node, shell ?? widget, fontScaleCtx);
        }

        // 块级 CSS transform 出口（ApplyBlockSizing 尾部统一入口，块级 img case 单独调用）：
        // transform 声明有效（TryParseTransform 通过，matrix/skew/3d 整条拒同解析口径）则包
        // HtmlTransformBoxWidget（恒最外），transform-origin 同源解析（无效/缺失回落默认
        // 50%,50%，不拒整条 transform）。无声明/解析失败原样直通（零回归）。
        // 范围与 width 同面：顶层块级 case +块级 img；li 直发块/td 自身声明不吃，li 内、
        // 格内 p/div/table 经重入吃（span 行内盒变换依赖 advance 计费，留档不做）。
        // 对齐移交同 WrapInStyleBox：变换壳顶替子件原外层对齐位，子件改 Stretch
        //（Near/Center/Stretch 视觉不变）
        Widget ApplyBlockTransform(HtmlAgilityPack.HtmlNode node, Widget widget) {
            string style = node.GetAttributeValue("style", null);
            if (!CssStyleParser.TryGetDeclaration(style, "transform", out string transformDecl)
                || !CssTransformParser.TryParseTransform(transformDecl, out List<CssTransformFunc> funcs)) {
                return widget;
            }
            HtmlTransformBoxWidget box = new() { Transforms = funcs };
            if (CssStyleParser.TryGetDeclaration(style, "transform-origin", out string originDecl)
                && CssTransformParser.TryParseTransformOrigin(originDecl, out Vector2 originRatios, out Vector2 originPx)) {
                box.OriginRatios = originRatios;
                box.OriginPx = originPx;
            }
            box.HorizontalAlignment = widget.HorizontalAlignment;
            widget.HorizontalAlignment = WidgetAlignment.Stretch;
            box.Children.Add(widget);
            return box;
        }

        // width/max-width 声明 → 包 HtmlBlockSizerWidget（% 与 max 的测量期钳制见该件）。
        // 无任何有效声明返回原 widget。
        // content-box 补偿：width/max-width 声明=内容宽口径，sizer 最外时内层壳的 pad/border
        // 占 sizer 宽——px 声明加水平 pad/border 合计（与 TryBeginStyleBox 同源解析：
        // TryGetBox+TryGetBorder+MergeBoxEdges），补偿后壳内内容恰=声明值；% 与自身盒边无关不补。
        // margin 水平 auto（级联后）：双侧→sizer 居中；左 auto 右定且带宽度约束
        //（width/max-width，HasBlockSizing）→靠右（Far，右侧 margin 迁由 sizer 承担，Far
        // 排位恰扣 MarginRight）；壳侧 TryGetBox 把含 auto 的 margin 声明整条丢弃（当 0），
        // 对齐由 sizer 的宿主排列承担
        Widget ApplyBlockSizing(HtmlAgilityPack.HtmlNode node, Widget widget, float fontScaleCtx = 1f) {
            string style = node.GetAttributeValue("style", null);
            if (!HasBlockSizing(style)) {
                // transform 壳统一收口：无 sizing 声明的块也吃 transform（两族出口共用本方法，
                // 变换壳恒最外）
                return ApplyBlockTransform(node, widget);
            }
            // content-box 补偿的 padding 同源解析（与壳侧 BoxLengthContext 同参同结果，em/%
            // 随解析结果自动正确——width 补偿含 pad/border 合计；tier-1.5 口径见 BoxLengthContext）
            (float emBase, float percentBase) = BoxLengthContext(style, fontScaleCtx);
            CssStyleParser.TryGetBox(style, "padding", out Vector4 sizingEdges, emBase, percentBase);
            if (CssStyleParser.TryGetBorder(style, out CssStyleParser.CssBorderSpec sizingBorder)) {
                sizingEdges = MergeBoxEdges(sizingEdges, sizingBorder);
            }
            float horizontalEdges = sizingEdges.X + sizingEdges.Z;
            HtmlBlockSizerWidget sizer = new();
            bool widthIsPercent = false;
            float widthNumber = 0f;
            bool hasWidth = CssStyleParser.TryGetDeclaration(style, "width", out string widthDecl)
                && CssStyleParser.TryParseWidthValue(widthDecl, out widthIsPercent, out widthNumber);
            if (hasWidth) {
                if (widthIsPercent) {
                    sizer.WidthPercent = widthNumber;
                }
                else {
                    sizer.FixedWidth = widthNumber + horizontalEdges;
                }
            }
            if (CssStyleParser.TryGetDeclaration(style, "max-width", out string maxWidthDecl)
                && CssStyleParser.TryParseWidthValue(maxWidthDecl, out bool maxIsPercent, out float maxNumber)) {
                if (maxIsPercent) {
                    sizer.MaxWidthPercent = maxNumber;
                }
                else {
                    sizer.MaxFixedWidth = maxNumber + horizontalEdges;
                }
            }
            (bool leftAuto, bool rightAuto) = MarginHorizontalAuto(style);
            if (leftAuto && rightAuto) {
                sizer.HorizontalAlignment = WidgetAlignment.Center;
            }
            else if (leftAuto && HasBlockSizing(style)) {
                // 单侧 auto 须 sizer 带宽度约束（width/max-width 任一有效声明，HasBlockSizing
                // 即本方法入口门）才有剩余空间可归：左 auto→剩余归左盒子靠右，右 auto→靠左
                // 即默认不设
                sizer.HorizontalAlignment = WidgetAlignment.Far;
            }
            // sizer 恒最外：内层壳的水平 Margin 迁给 sizer（壳清零）——margin 归宿主排列承担，
            // 不占 sizer 钳制的测量分母（% 分母近似见 sizer 留档），防壳内双重 margin 挤压内容宽
            if (widget is HtmlStyleBoxWidget innerShell) {
                sizer.MarginLeft = innerShell.MarginLeft;
                sizer.MarginRight = innerShell.MarginRight;
                innerShell.MarginLeft = 0f;
                innerShell.MarginRight = 0f;
            }
            // 子件撑满钳后宽（Arrange 期 Stretch），原对齐语义（Near 段落/Near 壳）在钳宽内不变
            widget.HorizontalAlignment = WidgetAlignment.Stretch;
            sizer.Children.Add(widget);
            // 变换壳包最外（sizer 之上）：CSS transform 作用于整个盒含 margin 效果近似
            return ApplyBlockTransform(node, sizer);
        }

        // 块级 sizing 声明检出：width/max-width 任一有效（解析通过）即 true
        static bool HasBlockSizing(string style) =>
            (CssStyleParser.TryGetDeclaration(style, "width", out string widthDecl) && CssStyleParser.TryParseWidthValue(widthDecl, out _, out _))
            || (CssStyleParser.TryGetDeclaration(style, "max-width", out string maxWidthDecl)
                && CssStyleParser.TryParseWidthValue(maxWidthDecl, out _, out _));

        // margin 水平 auto 检出（级联 last-wins，镜像 TryGetBox 的简写/长手结构）：margin 简写
        // 按声明序整条重置两侧（任一 token 非法=简写丢弃不重置），margin-left/right 长手覆写
        // 对应侧（非法值仅丢该条）。返回级联后两侧 auto 位：双侧=居中；单侧=剩余空间归该侧、
        // 盒子靠另一侧（CSS 口径，W7 钉 margin:0 auto 后 margin-left:20px 不居中、
        // W12 钉 margin-left:auto;margin-right:50px 靠右、W14 钉 max-width:300px;
        // margin-left:auto 靠右）；双侧皆定=无 auto
        static (bool LeftAuto, bool RightAuto) MarginHorizontalAuto(string style) {
            bool? leftAuto = null;
            bool? rightAuto = null;
            foreach (KeyValuePair<string, string> declaration in CssStyleParser.ParseDeclarations(style)) {
                if (declaration.Key == "margin") {
                    string[] values = declaration.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (values.Length is < 1 or > 4) {
                        continue;
                    }
                    // 简写原子性：全 token 须合法（auto 或长度），否则整条丢弃不重置。
                    // 合法性用 TryParseLengthUnit（em/% token 亦计）：与 TryGetBox 的 em/%
                    // 收口同口径——否则 margin:auto 后带单位长手覆写不上 auto 位（两侧漂移：
                    // 壳 Margin 已生效而对齐仍居中，审查 P2）
                    bool valid = true;
                    foreach (string token in values) {
                        if (!token.Equals("auto", StringComparison.OrdinalIgnoreCase)
                            && !CssStyleParser.TryParseLengthUnit(token, out _, out _, out _)) {
                            valid = false;
                            break;
                        }
                    }
                    if (!valid) {
                        continue;
                    }
                    // TRBL→左右位：1 值=同值；2/3 值=左右取第 2 位；4 值=右 2 左 4
                    leftAuto = values.Length switch {
                        1 => IsAutoToken(values[0]),
                        4 => IsAutoToken(values[3]),
                        _ => IsAutoToken(values[1])
                    };
                    rightAuto = values.Length == 1 ? leftAuto : IsAutoToken(values[1]);
                }
                else if (declaration.Key is "margin-left" or "margin-right") {
                    bool isAuto = declaration.Value.Trim().Equals("auto", StringComparison.OrdinalIgnoreCase);
                    // 长手合法性同简写口径（TryParseLengthUnit：em/% token 也能覆写 auto 位；
                    // 数值换算归 TryGetBox，本函数只判 auto 位）
                    if (isAuto || CssStyleParser.TryParseLengthUnit(declaration.Value, out _, out _, out _)) {
                        if (declaration.Key == "margin-left") {
                            leftAuto = isAuto;
                        }
                        else {
                            rightAuto = isAuto;
                        }
                    }
                }
            }
            return (leftAuto ?? false, rightAuto ?? false);
        }

        static bool IsAutoToken(string token) => token.Equals("auto", StringComparison.OrdinalIgnoreCase);

        // 容器版开头：子树重入目标换成壳（返回 false=无声明直通）。padding.Left 同步入
        // CurrentIndentX——嵌套代码块/图片块构建宽与 shell 注册 IndentX 按左 pad 收窄（右 pad
        // 不进回填：IndentX 只有左语义，格内/引用内嵌套壳的右 pad 回填留档不做）。
        // border 宽并入 padding 返回值：EndStyleBox 铺边距与 CurrentIndentX 自动含 border。
        // fontScaleCtx：所在元素继承的字号 ctx（tier-1.5，BoxLengthContext 消费；默认 1f=
        // tier-1 口径）
        bool TryBeginStyleBox(HtmlAgilityPack.HtmlNode node,
            Color? colorOverride,
            out StackPanelWidget shell,
            out Vector4 padding,
            float fontScaleCtx = 1f) {
            string style = node.GetAttributeValue("style", null);
            // margin/padding em/% 上下文（tier-1.5 口径见 BoxLengthContext 留档）
            (float emBase, float percentBase) = BoxLengthContext(style, fontScaleCtx);
            bool hasMargin = CssStyleParser.TryGetBox(style, "margin", out Vector4 margin, emBase, percentBase);
            bool hasPadding = CssStyleParser.TryGetBox(style, "padding", out padding, emBase, percentBase);
            // TryGetBorder 返回=有声明（含全禁），起壳查 HasAny（全禁=无边框不起壳）
            bool hasBorder = CssStyleParser.TryGetBorder(style, out CssStyleParser.CssBorderSpec border) && border.HasAny;
            // background-color：TryParse 失败=无效值整条丢弃不起壳（CSS 解析期丢弃口径）。
            // bgColor 预声明：&& 右侧 out 隔着 bool 变量不保明确赋值（C# 条件调用模式）；
            // transparent（A=0）=无背景免壳省每帧 Draw 调度
            Color bgColor = default;
            bool hasBackground = CssStyleParser.TryGetDeclaration(style, "background-color", out string bgDecl)
                && CssColorParser.TryParse(bgDecl, out bgColor)
                && bgColor.A > 0;
            // opacity：声明有效且 v<1 才参与起壳（v==1 无壳无意义）；壳承担装饰淡出，文字淡出
            // 由调用点把同源系数乘进递归 ctx（ResolveOpacityMultiplier），两处不双乘——壳只管壳装饰
            bool hasOpacity = ParseOpacityStyle(node, out float nodeOpacity) && nodeOpacity < 1f;
            // CSS width/max-width：块级尺寸声明也起壳——壳只作子树收集器（不承载尺寸属性），
            // 钳宽由出口 ApplyBlockSizing 包 sizer 承担；无壳时容器/段落块没有实体件可钳
            //（子块直挂宿主面板）。出口必须配套 ApplyBlockSizing，否则宽度声明被静默丢弃
            bool hasSizing = HasBlockSizing(style);
            // CSS transform：纯变换声明也起壳（同 hasSizing 口径——纯 transform 容器/段落块
            // 没有实体件可包，壳只作子树收集器）；壳本身无装饰，变换由出口 ApplyBlockTransform
            // 包 HtmlTransformBoxWidget 承担（解析两处同源同结果，重复解析无害）
            bool hasTransform = CssStyleParser.TryGetDeclaration(style, "transform", out string transformDecl)
                && CssTransformParser.TryParseTransform(transformDecl, out _);
            // border-radius：bg/border 的形状修饰，不参与起壳判定（同 WrapInStyleBox 口径）
            bool hasRadius = CssStyleParser.TryParseBorderRadius(style, out Vector4 cornerPx, out Vector4 cornerPct);
            // 纯 opacity 不起壳：壳无装饰可淡（文字淡出走 ctx 递归链），白付一层 wrapper+
            // 对齐移交；有 margin/padding/border/background 时壳装饰随 v 淡出
            if (!hasMargin
                && !hasPadding
                && !hasBorder
                && !hasBackground
                && !hasSizing
                && !hasTransform) {
                shell = null;
                return false;
            }
            HtmlStyleBoxWidget styleShell = new() { Direction = LayoutDirection.Vertical };

            // 壳装饰色乘 v（预乘淡出，与 GlobalColorTransform 乘法交换；==1 恒等零回归）——
            // 壳不走 ColorTransform：Arrange 会级联浸透子树，与 ctx 落点双淡
            Color? Decoration(Color? c) => hasOpacity && c.HasValue ? c.Value * nodeOpacity : c;
            if (hasBackground) {
                styleShell.BackgroundColor = Decoration(bgColor);
            }
            if (hasBorder) {
                styleShell.BorderWidths = border.Widths;
                styleShell.BorderLeftColor = Decoration(border.Left);
                styleShell.BorderTopColor = Decoration(border.Top);
                styleShell.BorderRightColor = Decoration(border.Right);
                styleShell.BorderBottomColor = Decoration(border.Bottom);
                styleShell.CurrentColor = Decoration(EffectiveTextColor(colorOverride)).Value;
                // border-box 并入唯一语义点（ApplyBlockSizing 的 px 补偿同源此 helper）：
                // HasAny 为 false 时 Widths 本就全 0，无条件相加恒等
                padding = MergeBoxEdges(padding, border);
            }
            shell = styleShell;
            if (hasMargin) {
                shell.Margin4 = margin;
            }
            if (hasRadius) {
                styleShell.CornerRadius = cornerPx;
                styleShell.CornerPercent = cornerPct;
            }
            // 对齐移交：容器壳恒 Stretch 顶替子件原外层对齐位（原直挂 host panel 的子件在
            // center/align 容器里按宿主拉伸排满；Stretch 只影响 arrange 不改 measure，壳内
            // Near 子件视觉不变——审查 P1：Near 壳会把居中/撑满塌成内容宽左对齐）
            shell.HorizontalAlignment = WidgetAlignment.Stretch;
            CurrentIndentX += padding.X;
            // width/max-width 播种子树构建期宽约束（px 声明入 AvailWidth；% 声明无构建期基准，
            // 存 Percent 载荷待 ConstrainedContentWidth 以宿主显式 ContentWidth 为基准现场解析，
            // 跟随宿主 ContentWidth<=0 时跳过、由测量期回填路接住）；无任何声明的壳压 ∞ 宽占位
            // 条目，与 EndStyleBox 弹出配对（种子消费见 AddImageBlock）
            WidthConstraints.Push(SeedWidthConstraint(style));
            return true;
        }

        // 子树宽约束种子：px 声明的 width/max-width 取小（content-box 口径=声明值，自身水平
        // pad/border 已折入种子缩进右侧的壳内缩）；% 声明存 Percent 载荷（width/max-width 的
        // % 取小，与 px 可并存），消费端现场解析见 ConstrainedContentWidth。两者皆无返回 ∞ 宽
        // 占位（解析器拒负值，"1e40" 类溢出解析为 ∞，作为占位条目自然失效）
        (float AvailWidth, float Percent, float IndentX) SeedWidthConstraint(string style) {
            float width = float.PositiveInfinity;
            float percent = -1f;
            if (CssStyleParser.TryGetDeclaration(style, "width", out string widthDecl)
                && CssStyleParser.TryParseWidthValue(widthDecl, out bool widthIsPercent, out float widthNumber)) {
                if (widthIsPercent) {
                    percent = widthNumber;
                }
                else {
                    width = widthNumber;
                }
            }
            if (CssStyleParser.TryGetDeclaration(style, "max-width", out string maxWidthDecl)
                && CssStyleParser.TryParseWidthValue(maxWidthDecl, out bool maxIsPercent, out float maxNumber)) {
                if (maxIsPercent) {
                    percent = percent >= 0f ? MathUtils.Min(percent, maxNumber) : maxNumber;
                }
                else {
                    width = MathUtils.Min(width, maxNumber);
                }
            }
            return (width, percent, CurrentIndentX);
        }

        // 容器壳收尾：padding 铺给直接子件（左右=每件、首件补上、末件补下——CSS 容器 padding
        // 口径，内容宽同步收窄）；空壳（纯空白子树）不产部件返回 false
        bool EndStyleBox(StackPanelWidget shell, Vector4 padding) {
            CurrentIndentX -= padding.X;
            WidthConstraints.Pop();
            if (shell.Children.Count == 0) {
                return false;
            }
            for (int i = 0; i < shell.Children.Count; i++) {
                ApplyBoxEdges(shell.Children[i], padding, i == 0, i == shell.Children.Count - 1);
            }
            return true;
        }

        // 盒边合并（content-box 口径唯一语义点）：padding 声明 + border 四边宽。TryBeginStyleBox
        // 的壳内缩与 ApplyBlockSizing 的 px 补偿同源此处，防两份解析口径漂移；HasAny 为 false
        // 时 Widths 本就全 0，无条件相加恒等
        static Vector4 MergeBoxEdges(Vector4 padding, CssStyleParser.CssBorderSpec border) => padding + border.Widths;

        // 盒四边铺到子件四边距（+=：与子件已有装饰 margin 线性叠加，作者值不覆盖引擎值）
        static void ApplyBoxEdges(Widget widget, Vector4 edges, bool applyTop, bool applyBottom) {
            if (edges.X != 0f) {
                widget.MarginLeft += edges.X;
            }
            if (edges.Y != 0f && applyTop) {
                widget.MarginTop += edges.Y;
            }
            if (edges.Z != 0f) {
                widget.MarginRight += edges.Z;
            }
            if (edges.W != 0f && applyBottom) {
                widget.MarginBottom += edges.W;
            }
        }

        // 块级 HTML 节点遍历：行内节点累积成段，遇块级标签先冲刷当前段。
        // 参数放宽 IEnumerable：li 内块级子节点以单节点数组重入（复用顶层分发口径）。
        // blockAlign：容器块级文本对齐（center/figure/figcaption = HorizontalCenter，div/p/h 的
        // align 属性可再覆写），TextAnchor.Default=继承外层——全部重入点（div/dl/列表/引用/表格格/
        // 未知容器）透传（Chrome text-align 继承口径；显式 left 属性映射 Default=Left 同值，天然覆盖）
        // wordBreak/noWrap/lineHeight：CSS 继承属性上下文（lineHeight 0=未声明，解析归一见
        // ResolveLineHeightCtx）——与 word-break 同链同挂载点透传。
        // opacityCtx：CSS opacity 继承上下文（叠乘语义，见 ResolveOpacityMultiplier）——各容器
        // 挂载点把「ctx × 本节点系数」传给递归，Flush 落到段落件 ColorTransform（White×v 全通道
        // 缩放，Arrange 期级联浸透子树；只缩 A 是直色×预乘缺陷陷阱）；壳装饰淡出=壳色乘
        //（不走 ColorTransform 防子树双淡），引用框/表格根/图片块/代码块/hr/marker 在各自
        // 挂载点或出口直设 ColorTransform
        // letterSpacingCtx：CSS letter-spacing 继承上下文（长度替换语义，见 ResolveLetterSpacingCtx）
        // ——与 word-break/line-height 同链同挂载点透传，Flush 落到段落件 RichTextWidget.LetterSpacing
        // fontScaleCtx：盒属性 em 基准的字号 ctx（tier-1.5，BoxLengthContext 消费；文字字号
        // 不走此链——行内随 HtmlSpanParser 自有 Scale 链）。乘点：标题 case 进口乘阶梯
        //（HeadingScale，自身盒壳+子树同生效）、default ContainsBlockChild 重入口乘 UA 标签
        //（ResolveUaTagFontScale，small/big/sub/sup/font 包块可达流）、通用块 case 乘本节点
        // style font-size 声明（ResolveFontScaleCtx——CSS font-size 块级继承：绝对单位换底
        // ratio、相对单位 ctx×ratio）。默认 1f=tier-1 口径
        void BuildHtmlNodes(IEnumerable<HtmlAgilityPack.HtmlNode> nodes,
            StackPanelWidget panel,
            int listDepth,
            Color? colorOverride,
            int depth,
            TextAnchor blockAlign = TextAnchor.Default,
            TextWordBreak wordBreakCtx = TextWordBreak.BreakWord,
            bool noWrapCtx = false,
            float lineHeightMul = 0f,
            float lineHeightPx = 0f,
            float opacityCtx = 1f,
            float letterSpacingCtx = 0f,
            float fontScaleCtx = 1f) {
            if (depth > MaxDepth) {
                return;
            }
            List<TextSpan> pendingSpans = [];
            // 行内段冲刷目标：带 style 盒属性的段落类标签（p/dt/summary/footer 行内形态）重定向
            // 进壳，其余场景恒等于 panel（壳方案 WrapInStyleBox 只覆盖单件块，段落壳在此换目标）
            StackPanelWidget flowPanel = panel;

            // paraOverride 四元组：word-break / white-space / line-height 段落级声明（p/dt/summary/
            // footer-inline 等在调用点已 Resolve 自身），null=纯继承上下文。LetterSpacing 第五元同链
            // 并入（ResolveParaOverrideCtx 打包）——「段落级声明经 override 叠入」与 opacityOverride
            // 同点位，footer 行内形态教训沿用：footer 解析节点自身，opacity 不重复乘（span.Opacity
            // 已落），letter-spacing 是替换语义且 span 解析不消费该声明，走 paraOverride 无双算
            // opacityOverride：段落类元素（p/dt/summary/footer 行内形态等）自身 opacity 需叠进
            // 段落件——内容冲刷时闭包里的 opacityCtx 还是外层值（本节点声明不落 span 上下文），
            // 调用点把 resolve 后的值经此传入；null=纯继承 ctx（其余 Flush 调用点）
            void Flush(TextAnchor? alignOverride = null,
                (TextWordBreak WordBreak, bool NoWrap, float LineHeightMul, float LineHeightPx, float LetterSpacing)? paraOverride = null,
                float? opacityOverride = null) {
                TextAnchor effectiveAlign = alignOverride ?? blockAlign;
                if (pendingSpans.Count > 0) {
                    HtmlSpanParser.TrimLeadingWhitespace(pendingSpans);
                    if (pendingSpans.Count == 0) {
                        // 纯空白段蒸发：随段挂的缩进连同所有者一并清除（防泄漏给相邻块；空边界
                        // Flush 不消费——pending 属尚未冲刷的挂载点）
                        m_pendingIndentPx = 0f;
                        m_pendingIndentPercent = null;
                        m_pendingIndentOwner = null;
                        return;
                    }
                    RichTextWidget widget = CreateRichText(colorOverride);
                    if (effectiveAlign != TextAnchor.Default) {
                        widget.HorizontalAlignment = WidgetAlignment.Stretch;
                        widget.TextAnchor = effectiveAlign;
                    }
                    // 段落级 word-break/white-space/line-height/letter-spacing 声明覆盖容器继承上下文（CSS 元素级口径）
                    (TextWordBreak paraBreak, bool paraNoWrap, float paraLhMul, float paraLhPx, float paraLs) = paraOverride
                        ?? (wordBreakCtx, noWrapCtx, lineHeightMul, lineHeightPx, letterSpacingCtx);
                    widget.WordBreak = paraBreak;
                    if (paraNoWrap) {
                        widget.WordWrap = false;
                    }
                    // letter-spacing：段落级声明覆盖继承（替换语义），无声明=继承 ctx（默认 0）
                    widget.LetterSpacing = paraLs;
                    // text-indent：块级 case 挂的待挂缩进由首个产段 Flush 消费（先到先得），空
                    // 边界 Flush 不动
                    if (m_pendingIndentPx != 0f
                        || m_pendingIndentPercent.HasValue) {
                        widget.FirstLineIndent = m_pendingIndentPx;
                        widget.FirstLineIndentPercent = m_pendingIndentPercent ?? 0f;
                        m_pendingIndentPx = 0f;
                        m_pendingIndentPercent = null;
                        m_pendingIndentOwner = null;
                    }
                    // CSS opacity：段落件 ColorTransform=White×v（全通道缩放，Arrange 期级联浸透
                    // 子树；只缩 A 是直色×预乘缺陷陷阱，参照 ComponentGui 模态过渡先例）。
                    // 行内 span 级透明度已在 span.Opacity，Draw 期叠乘
                    widget.ColorTransform = Color.White * (opacityOverride ?? opacityCtx);
                    // line-height：段落级声明覆盖继承，倍率缺省 1；绝对 px>0 优先于倍数（layout 侧同规）
                    widget.LineHeightMultiplier = paraLhMul > 0f ? paraLhMul : 1f;
                    if (paraLhPx > 0f) {
                        widget.LineHeightPx = paraLhPx;
                    }
                    widget.Spans.AddRange(pendingSpans);
                    AttachBlock(flowPanel, widget);
                    pendingSpans.Clear();
                }
            }

            // 行内上下文透传：子节点里的 <img> 升级为图片块（富文本编辑器帖图最常见 <p><img> 包裹，
            // img 无文本子节点会被 span 解析静默丢弃），其余照常 span 累积。
            // fontCtx：img 升级块自身盒壳（AddImageBlockWithAttributes→WrapInStyleBox）的字号
            // ctx=声明节点子树 ctx（CSS font-size 继承——img 随声明元素字号），文字 span 不用
            void AppendInlineChildren(HtmlAgilityPack.HtmlNode node, Color? inlineColorOverride, float? opacityOverride = null, float fontCtx = 1f) {
                foreach (HtmlAgilityPack.HtmlNode child in node.ChildNodes) {
                    if (child.NodeType == HtmlAgilityPack.HtmlNodeType.Element
                        && child.Name.Equals("img", StringComparison.OrdinalIgnoreCase)
                        && ImageLoader != null) {
                        // img 升级前预冲刷带本节点 opacity（否则 img 前段按外层 ctx、后段按叠加
                        // 值，同段淡出断裂）
                        Flush(null, null, opacityOverride);
                        PendAnchor(child);
                        // 图片块随段淡出：ctx 进工厂分层落点（img 声明落 frame、ctx 落壳/并入 frame）
                        Widget imgBlock = AddImageBlockWithAttributes(flowPanel, child, inlineColorOverride, opacityOverride ?? opacityCtx, fontCtx);
                        AttachBlock(flowPanel, imgBlock);
                        continue;
                    }
                    if (child.NodeType == HtmlAgilityPack.HtmlNodeType.Element) {
                        PendAnchor(child);
                    }
                    HtmlSpanParser.ParseHtmlNode(child, pendingSpans, default, EffectiveTextColor(inlineColorOverride), linkColor: m_bodyLinkColor);
                }
            }

            // text-indent 挂载（声明节点自身行内内容首段吃缩进）：调用点须在块边界 Flush 之后
            //——先关上一段边界再挂，防被上一段的收尾 Flush 误消费
            void PendTextIndent(HtmlAgilityPack.HtmlNode indentNode) {
                if (TryParseTextIndent(indentNode, out float px, out float? percent)) {
                    m_pendingIndentPx = px;
                    m_pendingIndentPercent = percent;
                    m_pendingIndentOwner = indentNode;
                }
            }

            // 待挂缩进所有权回收：挂接 case 在子树递归完成后、下一块边界 Flush 前调用——
            // 所有者仍是本节点=子树内无任何段落消费（空段/仅直建块/仅列表等），块外截断
            void ClearUnconsumedTextIndent(HtmlAgilityPack.HtmlNode owner) {
                if (m_pendingIndentOwner == owner) {
                    m_pendingIndentPx = 0f;
                    m_pendingIndentPercent = null;
                    m_pendingIndentOwner = null;
                }
            }

            // 块件挂载统一咽喉：先领取挂起锚点再挂树
            void AttachBlock(StackPanelWidget target, Widget block) {
                ClaimAnchors(block);
                target.Children.Add(block);
            }

            foreach (HtmlAgilityPack.HtmlNode node in nodes) {
                if (node.NodeType != HtmlAgilityPack.HtmlNodeType.Element
                    && node.NodeType != HtmlAgilityPack.HtmlNodeType.Text) {
                    continue;
                }
                if (node.NodeType == HtmlAgilityPack.HtmlNodeType.Text) {
                    HtmlSpanParser.ParseHtmlNode(node, pendingSpans, default, EffectiveTextColor(colorOverride));
                    continue;
                }
                // 全局 hidden 属性：任意值（含空串）都隐藏，块级元素连子树跳过（行内路径在
                // HtmlSpanParser.ParseHtmlNode 拦）
                if (node.GetAttributeValue("hidden", null) != null) {
                    continue;
                }
                // display:flex/inline-flex → flex 容器（块级单一判定点：先于标签 switch，覆盖
                // div/p/span/未知标签等全部元素入口——span 等行内标签声明 flex 按 CSS
                // blockification 升块，与 flex 子容器拦截同口径；script/style 体源码非可见内容
                // 须在拦截点显式排除，否则带 display:flex 的 script 会把源码经 flex 路径渲染成
                // 正文；范围与 transform 同面——li 直发块/td 自身不吃，li 内与格内经重入吃）。
                // inline-flex 行内态按 flex 块近似（留档）；block/inline-block 等其他值不拦走
                // 原路（inline-block 留档不做）
                if (node.Name.ToLowerInvariant() is not ("script" or "style")
                    && IsFlexDisplay(node)) {
                    Flush();
                    // 本节点锚点在入口 Flush 之后挂（教训同 text-indent：入口 Flush 冲刷的前段
                    // 经 AttachBlock 会把本节点 id 抢注给上一段，定位差一块）
                    PendAnchor(node);
                    // 字号 ctx：容器壳（FinalizeBlock）吃继承 ctx，容器内子项吃本节点声明叠加
                    //（CreateFlexContainer 内 ResolveFontScaleCtx，与 div 容器同口径）
                    Widget flexBlock = CreateFlexContainer(
                        node,
                        listDepth,
                        colorOverride,
                        depth,
                        blockAlign,
                        wordBreakCtx,
                        noWrapCtx,
                        lineHeightMul,
                        lineHeightPx,
                        opacityCtx,
                        letterSpacingCtx,
                        fontScaleCtx
                    );
                    if (flexBlock != null) {
                        AttachBlock(panel, FinalizeBlock(node, flexBlock, colorOverride, fontScaleCtx: fontScaleCtx));
                    }
                    continue;
                }
                switch (node.Name.ToLowerInvariant()) {
                    case "script":
                    case "style":
                        // 体源码非可见内容（HasVisibleText 同口径），块级直挂场景兜底跳过
                        Flush(); break;
                    case "h1":
                    case "h2":
                    case "h3":
                    case "h4":
                    case "h5":
                    case "h6": {
                        Flush();
                        PendAnchor(node);
                        // CSS hN UA font-size（阶梯）对自身与子树同时生效：ctx 进 case 即乘——
                        // 标题自身盒壳（FinalizeBlock→WrapInStyleBox）的 em/% 按 1.4 等阶梯解析
                        //（<h2 style="padding:0.5em">=16×1.4×0.5=11.2px；标题无块级子树重入，
                        // 子内容走行内 span 解析不受此 ctx 影响）
                        float headingFontCtx = fontScaleCtx * HeadingScale(node.Name[1] - '0');
                        RichTextWidget widget = CreateHeading(node.Name[1] - '0', colorOverride);
                        TextAnchor headingAlign = ParseBlockAlignment(node) ?? blockAlign;
                        if (headingAlign != TextAnchor.Default) {
                            widget.HorizontalAlignment = WidgetAlignment.Stretch;
                            widget.TextAnchor = headingAlign;
                        }
                        (TextWordBreak headingBreak, bool headingNoWrap) = ResolveWordBreakCtx(node, wordBreakCtx, noWrapCtx);
                        (float headingLhMul, float headingLhPx) = ResolveLineHeightCtx(node, lineHeightMul, lineHeightPx);
                        float headingLs = ResolveLetterSpacingCtx(node, letterSpacingCtx);
                        ApplyParagraphBreakStyle(widget, (headingBreak, headingNoWrap, headingLhMul, headingLhPx, headingLs));
                        // CSS opacity：标题文字件 ColorTransform=White×(ctx × 本节点声明)
                        widget.ColorTransform = Color.White * (opacityCtx * ResolveOpacityMultiplier(node));
                        HtmlSpanParser.ParseHtmlNode(node, widget.Spans, default, widget.Color, linkColor: m_bodyLinkColor);
                        AttachBlock(panel, FinalizeBlock(node, widget, colorOverride, fontScaleCtx: headingFontCtx));
                        break;
                    }
                    case "p": {
                        // p align 属性：本段对齐覆写继承（CSS text-align 元素级口径）；
                        // style 盒属性：段落壳（行内内容+升级 img 块整体入壳）
                        Flush();
                        PendAnchor(node);
                        PendTextIndent(node);
                        bool styledPara = TryBeginStyleBox(node, colorOverride, out StackPanelWidget paraShell, out Vector4 paraPad, fontScaleCtx);
                        if (styledPara) {
                            flowPanel = paraShell;
                        }
                        AppendInlineChildren(
                            node,
                            colorOverride,
                            opacityCtx * ResolveOpacityMultiplier(node),
                            ResolveFontScaleCtx(node, fontScaleCtx)
                        );
                        // p 自身 opacity：壳只淡出壳装饰，文字件须叠进同一系数——Flush 闭包只看
                        // 外层 ctx，经 opacityOverride 把「ctx × p 自身系数」传入（无声明=×1 原值）
                        Flush(
                            ParseBlockAlignment(node),
                            ResolveParaOverrideCtx(node, wordBreakCtx, noWrapCtx, lineHeightMul, lineHeightPx, letterSpacingCtx),
                            opacityCtx * ResolveOpacityMultiplier(node)
                        );
                        if (styledPara) {
                            flowPanel = panel;
                            if (EndStyleBox(paraShell, paraPad)) {
                                AttachBlock(panel, ApplyBlockSizing(node, paraShell, fontScaleCtx));
                            }
                        }
                        ClearUnconsumedTextIndent(node);
                        break;
                    }
                    case "div": {
                        // 容器标签递归块级遍历（子级 p/pre/列表各归其位），不做 span 平铺——
                        // 否则 div 内 pre 被当普通元素折叠，代码缩进/换行全毁；align 属性覆写继承。
                        // style 盒属性：子树入壳（容器壳，padding 铺直接子件）
                        Flush();
                        PendAnchor(node);
                        PendTextIndent(node);
                        bool styledDiv = TryBeginStyleBox(node, colorOverride, out StackPanelWidget divShell, out Vector4 divPad, fontScaleCtx);
                        (TextWordBreak divBreak, bool divNoWrap) = ResolveWordBreakCtx(node, wordBreakCtx, noWrapCtx);
                        (float divLhMul, float divLhPx) = ResolveLineHeightCtx(node, lineHeightMul, lineHeightPx);
                        float divLs = ResolveLetterSpacingCtx(node, letterSpacingCtx);
                        // 子树 ctx 叠进本节点系数（壳只淡出壳装饰，文字淡出靠 ctx 传导）；
                        // 字号 ctx 叠本节点 style font-size 声明（CSS 块级继承，ResolveFontScaleCtx）
                        BuildHtmlNodes(
                            node.ChildNodes,
                            styledDiv ? divShell : panel,
                            listDepth,
                            colorOverride,
                            depth + 1,
                            ParseBlockAlignment(node) ?? blockAlign,
                            divBreak,
                            divNoWrap,
                            divLhMul,
                            divLhPx,
                            opacityCtx * ResolveOpacityMultiplier(node),
                            divLs,
                            ResolveFontScaleCtx(node, fontScaleCtx)
                        );
                        if (styledDiv && EndStyleBox(divShell, divPad)) {
                            AttachBlock(panel, ApplyBlockSizing(node, divShell, fontScaleCtx));
                        }
                        ClearUnconsumedTextIndent(node);
                        break;
                    }
                    case "html":
                    case "body": {
                        // 整页包装标签当 div（老帖源码常带 html/body 包裹，浏览器剥离外壳直排内容）；
                        // body bgcolor 作表格 bgcolor 回退链根，text=/link= 作默认文字/链接色
                        Flush();
                        PendAnchor(node);
                        if (node.Name.Equals("body", StringComparison.OrdinalIgnoreCase)) {
                            if (CssColorParser.TryParse(node.GetAttributeValue("bgcolor", null), out Color bodyBackground)) {
                                m_bodyBackground = bodyBackground;
                            }
                            // CSS background-color 声明覆写 bgcolor 属性（同一元素 CSS 胜旧属性）
                            if (CssStyleParser.TryGetDeclaration(node.GetAttributeValue("style", null), "background-color", out string bodyCssBg)
                                && CssColorParser.TryParse(bodyCssBg, out Color bodyCssColor)) {
                                m_bodyBackground = bodyCssColor;
                            }
                            if (CssColorParser.TryParse(node.GetAttributeValue("text", null), out Color bodyText)) {
                                m_bodyTextColor = bodyText;
                            }
                            if (CssColorParser.TryParse(node.GetAttributeValue("link", null), out Color bodyLink)) {
                                m_bodyLinkColor = bodyLink;
                            }
                        }
                        (TextWordBreak shellBreak, bool shellNoWrap) = ResolveWordBreakCtx(node, wordBreakCtx, noWrapCtx);
                        (float shellLhMul, float shellLhPx) = ResolveLineHeightCtx(node, lineHeightMul, lineHeightPx);
                        float shellLs = ResolveLetterSpacingCtx(node, letterSpacingCtx);
                        // body font-size 声明是 CSS 继承链经典根（同 div 容器口径）
                        BuildHtmlNodes(
                            node.ChildNodes,
                            panel,
                            listDepth,
                            colorOverride,
                            depth + 1,
                            ParseBlockAlignment(node) ?? blockAlign,
                            shellBreak,
                            shellNoWrap,
                            shellLhMul,
                            shellLhPx,
                            opacityCtx * ResolveOpacityMultiplier(node),
                            shellLs,
                            ResolveFontScaleCtx(node, fontScaleCtx)
                        );
                        break;
                    }
                    case "ul":
                    case "ol":
                    case "menu":
                        // menu 与 ul 同渲染（HTML 弃用标签，Chrome 按无序列表渲染）；
                        // ctx 原值传入，列表节点自身 opacity 由 BuildHtmlList 统一 Resolve（word-break 同口径）。
                        // 列表整体 CSS width/max-width 接线（sizer 包列表壳/面板，case 出口统一咽喉点）
                        Flush();
                        PendAnchor(node);
                        Widget listBlock = BuildHtmlList(
                            node,
                            node.Name.Equals("ol", StringComparison.Ordinal),
                            panel,
                            listDepth,
                            colorOverride,
                            depth,
                            blockAlign,
                            wordBreakCtx,
                            noWrapCtx,
                            lineHeightMul,
                            lineHeightPx,
                            opacityCtx,
                            letterSpacingCtx,
                            fontScaleCtx
                        );
                        if (listBlock != null) {
                            AttachBlock(panel, ApplyBlockSizing(node, listBlock, fontScaleCtx));
                        }
                        break;
                    case "blockquote": {
                        Flush();
                        PendAnchor(node);
                        PendTextIndent(node);
                        (TextWordBreak quoteBreak, bool quoteNoWrap) = ResolveWordBreakCtx(node, wordBreakCtx, noWrapCtx);
                        (float quoteLhMul, float quoteLhPx) = ResolveLineHeightCtx(node, lineHeightMul, lineHeightPx);
                        float quoteLs = ResolveLetterSpacingCtx(node, letterSpacingCtx);
                        // CSS opacity：标题文字件同主链口径（ctx × 自身声明）→ ColorTransform。
                        // 字号 ctx：引用内子树吃本节点 style font-size 声明（自身盒壳由 FinalizeBlock
                        // 按继承 ctx 解析，同 div 容器两段口径）
                        Widget quote = CreateHtmlQuote(
                            node,
                            listDepth,
                            colorOverride,
                            depth,
                            blockAlign,
                            quoteBreak,
                            quoteNoWrap,
                            quoteLhMul,
                            quoteLhPx,
                            opacityCtx * ResolveOpacityMultiplier(node),
                            quoteLs,
                            ResolveFontScaleCtx(node, fontScaleCtx)
                        );
                        AttachBlock(panel, FinalizeBlock(node, quote, colorOverride, fontScaleCtx: fontScaleCtx));
                        ClearUnconsumedTextIndent(node);
                        break;
                    }
                    case "pre": {
                        Flush();
                        PendAnchor(node);
                        HtmlAgilityPack.HtmlNode code = node.Element("code") ?? node;
                        ParseCodeTextStyle(
                            node,
                            out Color? codeTextColor,
                            out float? codeScaleOverride,
                            out float? codeRoundingRadius,
                            out Color? codeBackgroundColor
                        );
                        Widget codeBlock = CreateCodeBlock(
                            ReplaceBrWithNewline(code).Split('\n'),
                            codeTextColor,
                            codeScaleOverride,
                            codeRoundingRadius,
                            m_flexDepth > 0,
                            codeBackgroundColor
                        );
                        // 代码块随 ctx×自身声明淡出（BevelledRect 框+Label 行全子树浸透）
                        codeBlock.ColorTransform = Color.White * (opacityCtx * ResolveOpacityMultiplier(node));
                        AttachBlock(panel, FinalizeBlock(node, codeBlock, colorOverride, fontScaleCtx: fontScaleCtx));
                        break;
                    }
                    case "hr": {
                        Flush();
                        PendAnchor(node);
                        Widget thematicBreak = CreateHtmlThematicBreak(node);
                        // hr 线随 ctx×自身声明淡出（RectangleWidget 填充×GlobalColorTransform）
                        thematicBreak.ColorTransform = Color.White * (opacityCtx * ResolveOpacityMultiplier(node));
                        // CSS width/max-width 接线（与 width 属性同机制，sizer 包线件，CSS 胜属性：
                        // 属性 width 定死 Size.X 时子件被改 Stretch，排列宽随 sizer 钳后值）
                        AttachBlock(panel, FinalizeBlock(node, thematicBreak, colorOverride, fontScaleCtx: fontScaleCtx));
                        break;
                    }
                    case "img" when ImageLoader != null:
                        // 块级 <img>：与纯图段落同管线。行内 <img>（p 内）仍被丢弃（范围外）。
                        // 图片块随 ctx 淡出（img 自身声明工厂内落 frame）。
                        // 块级 img 也吃 transform（顶层 img case 出口接线，变换壳恒最外）
                        Flush();
                        PendAnchor(node);
                        Widget imgBlock = AddImageBlockWithAttributes(panel, node, colorOverride, opacityCtx, fontScaleCtx);
                        AttachBlock(panel, ApplyBlockTransform(node, imgBlock));
                        break;
                    case "table": {
                        Flush();
                        PendAnchor(node);
                        // caption（表题）：表上方居中块渲染（CreateHtmlTable 行收集本就跳过 caption，
                        // 不双渲染）；用户裁定 caption=居中块，与 center/figure 同款。
                        // caption 自身 opacity 叠进子树 ctx（caption 子节点不落 span 上下文）
                        // 空表（无任何行）返回 null 不产部件——零尺寸 widget 仍会靠 Margin 贡献 8px 空白。
                        // 表节点自身 opacity 落表根 ColorTransform（表线/格底/格内容全子树浸透
                        // ——缺口闭合），格内 ctx 归一防格内落点双淡（嵌套子块自身声明从 1 起步照吃）。
                        // 字号 ctx：caption 子树与格内容重入吃表节点 style font-size 声明叠加
                        float tableFontCtx = ResolveFontScaleCtx(node, fontScaleCtx);
                        HtmlAgilityPack.HtmlNode captionNode = node.Element("caption");
                        if (captionNode != null) {
                            BuildHtmlNodes(
                                captionNode.ChildNodes,
                                panel,
                                listDepth,
                                colorOverride,
                                depth + 1,
                                TextAnchor.HorizontalCenter,
                                wordBreakCtx,
                                noWrapCtx,
                                lineHeightMul,
                                lineHeightPx,
                                opacityCtx * ResolveOpacityMultiplier(captionNode),
                                letterSpacingCtx,
                                ResolveFontScaleCtx(captionNode, tableFontCtx)
                            );
                        }
                        TablePanelWidget table = CreateHtmlTable(
                            node,
                            colorOverride,
                            depth,
                            blockAlign,
                            opacityCtx * ResolveOpacityMultiplier(node),
                            tableFontCtx
                        );
                        if (table != null) {
                            AttachBlock(panel, FinalizeBlock(node, table, colorOverride, false, fontScaleCtx));
                        }
                        break;
                    }
                    case "center":
                    case "figure":
                    case "figcaption":
                    case "caption":
                        // 居中容器（center 老帖高频 / figure 图容器 / figcaption 题注 / caption 表题，
                        // 用户裁定同款居中块）：块边界+子节点重入，HorizontalCenter 贯穿段/标题行盒居中
                        // （Chrome text-align 语义，覆写外层继承）；table 直下 caption 已在 table case
                        // 预提取，此处兜 table 外裸 caption。style 盒属性：子树入壳
                        Flush();
                        PendAnchor(node);
                        PendTextIndent(node);
                        bool styledCenter = TryBeginStyleBox(
                            node,
                            colorOverride,
                            out StackPanelWidget centerShell,
                            out Vector4 centerPad,
                            fontScaleCtx
                        );
                        (TextWordBreak centerBreak, bool centerNoWrap) = ResolveWordBreakCtx(node, wordBreakCtx, noWrapCtx);
                        (float centerLhMul, float centerLhPx) = ResolveLineHeightCtx(node, lineHeightMul, lineHeightPx);
                        float centerLs = ResolveLetterSpacingCtx(node, letterSpacingCtx);
                        BuildHtmlNodes(
                            node.ChildNodes,
                            styledCenter ? centerShell : panel,
                            listDepth,
                            colorOverride,
                            depth + 1,
                            TextAnchor.HorizontalCenter,
                            centerBreak,
                            centerNoWrap,
                            centerLhMul,
                            centerLhPx,
                            opacityCtx * ResolveOpacityMultiplier(node),
                            centerLs,
                            ResolveFontScaleCtx(node, fontScaleCtx)
                        );
                        if (styledCenter && EndStyleBox(centerShell, centerPad)) {
                            AttachBlock(panel, ApplyBlockSizing(node, centerShell, fontScaleCtx));
                        }
                        ClearUnconsumedTextIndent(node);
                        break;
                    case "dl":
                    case "details":
                        // dl 定义列表容器 / details 折叠容器（无折叠交互，按已展开直排——用户裁定）：
                        // 块边界+子节点重入，dt/dd 各归其位。style 盒属性：子树入壳
                        Flush();
                        PendAnchor(node);
                        PendTextIndent(node);
                        bool styledDl = TryBeginStyleBox(node, colorOverride, out StackPanelWidget dlShell, out Vector4 dlPad, fontScaleCtx);
                        (TextWordBreak dlBreak, bool dlNoWrap) = ResolveWordBreakCtx(node, wordBreakCtx, noWrapCtx);
                        (float dlLhMul, float dlLhPx) = ResolveLineHeightCtx(node, lineHeightMul, lineHeightPx);
                        float dlLs = ResolveLetterSpacingCtx(node, letterSpacingCtx);
                        BuildHtmlNodes(
                            node.ChildNodes,
                            styledDl ? dlShell : panel,
                            listDepth,
                            colorOverride,
                            depth + 1,
                            blockAlign,
                            dlBreak,
                            dlNoWrap,
                            dlLhMul,
                            dlLhPx,
                            opacityCtx * ResolveOpacityMultiplier(node),
                            dlLs,
                            ResolveFontScaleCtx(node, fontScaleCtx)
                        );
                        if (styledDl && EndStyleBox(dlShell, dlPad)) {
                            AttachBlock(panel, ApplyBlockSizing(node, dlShell, fontScaleCtx));
                        }
                        ClearUnconsumedTextIndent(node);
                        break;
                    case "dt": {
                        // 术语段：独立成段不加粗（用户裁定），同 p 口径（含段落壳）
                        Flush();
                        PendAnchor(node);
                        PendTextIndent(node);
                        bool styledDt = TryBeginStyleBox(node, colorOverride, out StackPanelWidget dtShell, out Vector4 dtPad, fontScaleCtx);
                        if (styledDt) {
                            flowPanel = dtShell;
                        }
                        AppendInlineChildren(
                            node,
                            colorOverride,
                            opacityCtx * ResolveOpacityMultiplier(node),
                            ResolveFontScaleCtx(node, fontScaleCtx)
                        );
                        // dt 自身 opacity 叠进段落件（同 p 口径：Flush 闭包只看外层 ctx）
                        Flush(
                            null,
                            ResolveParaOverrideCtx(node, wordBreakCtx, noWrapCtx, lineHeightMul, lineHeightPx, letterSpacingCtx),
                            opacityCtx * ResolveOpacityMultiplier(node)
                        );
                        if (styledDt) {
                            flowPanel = panel;
                            if (EndStyleBox(dtShell, dtPad)) {
                                AttachBlock(panel, ApplyBlockSizing(node, dtShell, fontScaleCtx));
                            }
                        }
                        ClearUnconsumedTextIndent(node);
                        break;
                    }
                    case "dd": {
                        // 定义描述段：独立容器物理位移 ListIndentPerDepth（CurrentIndentX 只被代码/
                        // 图片块宽度扣减消费，文字段不读它——缩进须像引用块一样 SetPosition 落地）；
                        // CurrentIndentX 同步进档让 dd 内代码块/图片按缩进后净宽构建
                        Flush();
                        PendAnchor(node);
                        PendTextIndent(node);
                        float prevIndentDd = CurrentIndentX;
                        CurrentIndentX += ListIndentPerDepth;
                        StackPanelWidget ddPanel = CreatePanel();
                        if (blockAlign != TextAnchor.Default) {
                            // 对齐容器内 dd：撑满外壳剩余宽（Margin 左侧缩进扣余），锚点生效
                            ddPanel.HorizontalAlignment = WidgetAlignment.Stretch;
                        }
                        (TextWordBreak ddBreak, bool ddNoWrap) = ResolveWordBreakCtx(node, wordBreakCtx, noWrapCtx);
                        (float ddLhMul, float ddLhPx) = ResolveLineHeightCtx(node, lineHeightMul, lineHeightPx);
                        float ddLs = ResolveLetterSpacingCtx(node, letterSpacingCtx);
                        BuildHtmlNodes(
                            node.ChildNodes,
                            ddPanel,
                            listDepth,
                            colorOverride,
                            depth + 1,
                            blockAlign,
                            ddBreak,
                            ddNoWrap,
                            ddLhMul,
                            ddLhPx,
                            opacityCtx * ResolveOpacityMultiplier(node),
                            ddLs,
                            ResolveFontScaleCtx(node, fontScaleCtx)
                        );
                        CurrentIndentX = prevIndentDd;
                        CanvasWidget ddOuter = new() { Size = new Vector2(NestedContentWidth(), -1f) };
                        if (blockAlign != TextAnchor.Default) {
                            // 对齐容器内 dd：外壳撑满（同引用/列表行框口径）
                            ddOuter.HorizontalAlignment = WidgetAlignment.Stretch;
                        }
                        ddOuter.Children.Add(ddPanel);
                        // 缩进走 MarginLeft 不走 SetPosition（同 BuildHtmlList contentPanel 口径）
                        ddPanel.MarginLeft = ListIndentPerDepth;
                        // 格内注册 FlowShell（顶层不注册，行为不变）：dd 外壳宽归所在格 panel 按格净宽
                        // 回填（构建期 NestedContentWidth 是顶层近似值）；SetPosition 的 ListIndentPerDepth
                        // 是壳内内容位移，不进外壳条目（口径同 CreateHtmlQuote 的 +10）
                        CurrentPanel?.Shells.Add(new ShellEntry { Frame = ddOuter, IndentX = CurrentIndentX, Kind = ShellKind.FlowShell });
                        // style 盒属性：ddOuter 入壳（已知留档：dd 内代码块/图片构建宽不吃 padding，
                        // CurrentIndentX 补偿口径 dd 专属缩进语义纠缠，不做）
                        AttachBlock(panel, FinalizeBlock(node, ddOuter, colorOverride, fontScaleCtx: fontScaleCtx));
                        ClearUnconsumedTextIndent(node);
                        break;
                    }
                    case "summary": {
                        // 折叠标题：开头补 ▼ 代表已展开（用户裁定），余同 p 口径（含段落壳）
                        Flush();
                        PendAnchor(node);
                        PendTextIndent(node);
                        bool styledSummary = TryBeginStyleBox(
                            node,
                            colorOverride,
                            out StackPanelWidget summaryShell,
                            out Vector4 summaryPad,
                            fontScaleCtx
                        );
                        if (styledSummary) {
                            flowPanel = summaryShell;
                        }
                        HtmlSpanParser.AddText("▼ ", pendingSpans, default, EffectiveTextColor(colorOverride));
                        AppendInlineChildren(
                            node,
                            colorOverride,
                            opacityCtx * ResolveOpacityMultiplier(node),
                            ResolveFontScaleCtx(node, fontScaleCtx)
                        );
                        // summary 自身 opacity 叠进段落件（同 p 口径：Flush 闭包只看外层 ctx）
                        Flush(
                            null,
                            ResolveParaOverrideCtx(node, wordBreakCtx, noWrapCtx, lineHeightMul, lineHeightPx, letterSpacingCtx),
                            opacityCtx * ResolveOpacityMultiplier(node)
                        );
                        if (styledSummary) {
                            flowPanel = panel;
                            if (EndStyleBox(summaryShell, summaryPad)) {
                                AttachBlock(panel, ApplyBlockSizing(node, summaryShell, fontScaleCtx));
                            }
                        }
                        ClearUnconsumedTextIndent(node);
                        break;
                    }
                    case "footer":
                    case "address": {
                        // 页脚/联系地址：独立成段（footer 用户裁定无特殊视觉；address 斜体由
                        // ParseHtmlNode→ApplyOpenTag 自带）。含块级子标签时块边界优先走子树递归
                        //（否则 <footer><p>a</p><p>b</p> 两段被平铺合并）。style 盒属性：
                        // 块级子标签=容器壳，行内形态=段落壳
                        Flush();
                        PendAnchor(node);
                        PendTextIndent(node);
                        if (ContainsBlockChild(node)) {
                            bool styledFooter = TryBeginStyleBox(
                                node,
                                colorOverride,
                                out StackPanelWidget footerShell,
                                out Vector4 footerPad,
                                fontScaleCtx
                            );
                            (TextWordBreak footerBreak, bool footerNoWrap) = ResolveWordBreakCtx(node, wordBreakCtx, noWrapCtx);
                            (float footerLhMul, float footerLhPx) = ResolveLineHeightCtx(node, lineHeightMul, lineHeightPx);
                            float footerLs = ResolveLetterSpacingCtx(node, letterSpacingCtx);
                            BuildHtmlNodes(
                                node.ChildNodes,
                                styledFooter ? footerShell : panel,
                                listDepth,
                                colorOverride,
                                depth + 1,
                                blockAlign,
                                footerBreak,
                                footerNoWrap,
                                footerLhMul,
                                footerLhPx,
                                opacityCtx * ResolveOpacityMultiplier(node),
                                footerLs,
                                ResolveFontScaleCtx(node, fontScaleCtx)
                            );
                            if (styledFooter && EndStyleBox(footerShell, footerPad)) {
                                AttachBlock(panel, ApplyBlockSizing(node, footerShell, fontScaleCtx));
                            }
                        }
                        else {
                            bool styledFooterInline = TryBeginStyleBox(
                                node,
                                colorOverride,
                                out StackPanelWidget footerShell,
                                out Vector4 footerInlinePad,
                                fontScaleCtx
                            );
                            if (styledFooterInline) {
                                flowPanel = footerShell;
                            }
                            HtmlSpanParser.ParseHtmlNode(node, pendingSpans, default, EffectiveTextColor(colorOverride), linkColor: m_bodyLinkColor);
                            // footer 行内形态解析的是节点自身（address 斜体所需），自身 opacity 已落
                            // span.Opacity——Flush 用外层 ctx 原值，再乘即 v² 双淡出（与 p 的
                            // AppendInlineChildren 只解析子节点不同口径）。letter-spacing 不受此限：
                            // span 解析不消费该声明，经 paraOverride 叠入无重复计算（替换语义）
                            Flush(
                                null,
                                ResolveParaOverrideCtx(node, wordBreakCtx, noWrapCtx, lineHeightMul, lineHeightPx, letterSpacingCtx),
                                opacityCtx
                            );
                            if (styledFooterInline) {
                                flowPanel = panel;
                                if (EndStyleBox(footerShell, footerInlinePad)) {
                                    AttachBlock(panel, ApplyBlockSizing(node, footerShell, fontScaleCtx));
                                }
                            }
                        }
                        ClearUnconsumedTextIndent(node);
                        Flush();
                        break;
                    }
                    default:
                        // 未知标签：子树含块级标签（section/article 包裹 pre/列表等）时递归块级遍历；
                        // 含直接 img 子级时走逐子拦截（img 升级图片块——<a><img> 帖图最高频包装，
                        // 整节点 span 解析会把图片退化成占位文本；容器行内样式对图片块无意义）；
                        // 其余整节点进 span 解析——ApplyOpenTag/引号/色底后刷全生效（此前只透传
                        // 子节点，行内容器标签自带样式被静默跳过）。
                        // style 盒属性：块级子标签=容器壳，img 段形态=段落壳；纯行内形态不做
                        //（行内盒 padding/margin 依赖 advance 计费，与行内 border 同波）
                        // 不挂点清单（text-indent）：li 直发块/嵌套列表（FlushLi 不读 pending）、
                        // h1-6/cell 纯行内格/table/img/pre/hr（直建不经 Flush 产段或缩进罕见）、
                        // 行内容器纯行内形态（Chrome 行内元素 text-indent 无效，挂了会让下段误缩）。
                        // 字号 ctx（tier-1.5 UA 标签可达流）：块级重入口先乘 UA 标签倍率
                        //（ResolveUaTagFontScale——small/big/sub/sup/font 包块时 UA font-size 对
                        // 自身壳与子树同生效；留档：li 直发块名单/格 IsBlockTag 名单不含这些标签，
                        // 该两处 <li><small><div> 类被 span 平铺，ctx 不可达=既有口径）后按
                        // ResolveFontScaleCtx 叠自身 style font-size 声明传导子树
                        if (ContainsBlockChild(node)) {
                            Flush();
                            PendAnchor(node);
                            PendTextIndent(node);
                            float uaFontCtx = fontScaleCtx * ResolveUaTagFontScale(node);
                            bool styledUnknown = TryBeginStyleBox(
                                node,
                                colorOverride,
                                out StackPanelWidget unknownShell,
                                out Vector4 unknownPad,
                                uaFontCtx
                            );
                            (TextWordBreak unknownBreak, bool unknownNoWrap) = ResolveWordBreakCtx(node, wordBreakCtx, noWrapCtx);
                            (float unknownLhMul, float unknownLhPx) = ResolveLineHeightCtx(node, lineHeightMul, lineHeightPx);
                            float unknownLs = ResolveLetterSpacingCtx(node, letterSpacingCtx);
                            BuildHtmlNodes(
                                node.ChildNodes,
                                styledUnknown ? unknownShell : panel,
                                listDepth,
                                colorOverride,
                                depth + 1,
                                blockAlign,
                                unknownBreak,
                                unknownNoWrap,
                                unknownLhMul,
                                unknownLhPx,
                                opacityCtx * ResolveOpacityMultiplier(node),
                                unknownLs,
                                ResolveFontScaleCtx(node, uaFontCtx)
                            );
                            if (styledUnknown && EndStyleBox(unknownShell, unknownPad)) {
                                AttachBlock(panel, ApplyBlockSizing(node, unknownShell, uaFontCtx));
                            }
                            ClearUnconsumedTextIndent(node);
                        }
                        else if (ImageLoader != null
                            && HasElementChild(node, "img")) {
                            PendAnchor(node);
                            PendTextIndent(node);
                            float uaFontCtx = fontScaleCtx * ResolveUaTagFontScale(node);
                            bool styledImgPara = TryBeginStyleBox(
                                node,
                                colorOverride,
                                out StackPanelWidget imgParaShell,
                                out Vector4 imgParaPad,
                                uaFontCtx
                            );
                            if (styledImgPara) {
                                flowPanel = imgParaShell;
                            }
                            AppendInlineChildren(
                                node,
                                colorOverride,
                                opacityCtx * ResolveOpacityMultiplier(node),
                                ResolveFontScaleCtx(node, uaFontCtx)
                            );
                            if (styledImgPara) {
                                // 尾冲刷：img 之后的尾随文本仍滞 pendingSpans，不冲就逃出壳落外层
                                //（审查 P0，同 p/dt/summary/footer 行内分支口径）；
                                // img 段自身 opacity 叠进段落件（同 p 口径）
                                Flush(null, null, opacityCtx * ResolveOpacityMultiplier(node));
                                flowPanel = panel;
                                if (EndStyleBox(imgParaShell, imgParaPad)) {
                                    AttachBlock(panel, ApplyBlockSizing(node, imgParaShell, uaFontCtx));
                                }
                            }
                            ClearUnconsumedTextIndent(node);
                        }
                        else {
                            PendAnchor(node);
                            HtmlSpanParser.ParseHtmlNode(node, pendingSpans, default, EffectiveTextColor(colorOverride), linkColor: m_bodyLinkColor);
                        }
                        break;
                }
            }
            Flush();
        }

        // display:flex 值检出（style display 声明，级联 last-wins）：flex/inline-flex 两值命中
        static bool IsFlexDisplay(HtmlAgilityPack.HtmlNode node) {
            if (!CssStyleParser.TryGetDeclaration(node.GetAttributeValue("style", null), "display", out string displayDecl)) {
                return false;
            }
            return displayDecl.Trim().ToLowerInvariant() is "flex" or "inline-flex";
        }

        // display:flex 容器构建：FlexPanelWidget 直产 + 容器属性映射（flex-direction/flex-wrap/
        // flex-flow 简写/gap 简写+长手/justify-content/align-items，声明序迭代：简写重置槽位、
        // 长手随序覆写，无效值整条丢不回退前条）+ 子项二分（块级元素子=单节点重入顶层分发产
        // item，空元素子补零尺寸载体件照吃 flex 属性；行内元素子=累积成匿名段落 item，块级
        // 边界与收尾处冲刷、纯空白蒸发不产件）。返回件走调用方 FinalizeBlock 块级咽喉——
        // bg/border/margin/padding 壳、width/max-width、transform 同面享受；嵌套 flex 经子树
        // 重入自然支持。留档近似：inline-flex 行内态按块；align-items 未声明=引擎缺省 Near
        //（≈flex-start，非 CSS 默认 stretch）；baseline 映 stretch；% gap/basis 拒（引擎布局期
        // 无主轴尺寸可折算）；flex 简写全值展开见 ApplyFlexItemProps（shrink 预种 1=CSS 初始
        // 值，`flex:1`≡1 1 0%）；order/align-self 不做（留档）；text-indent 不挂（flex 容器非
        // 块容器，CSS 缩进不适用于 flex 件）。
        // opacity：容器 ctx×自身声明落 FlexPanelWidget.ColorTransform 一并浸透子树（quote 外壳
        // 同款），子项子树 ctx 归一传 1 防双淡，嵌套子块自身声明从 1 起步照吃
        // 回填口径（留档，已接受残余）：flex 内代码块构建期 basis=自然宽（m_flexDepth→
        // flexNaturalBasis，回填循环经 IsInsideFlex 跳过）；pending 图占位基准=容器净宽
        //（瞬时通胀，应用后归位显示宽）；窄 flex item 内嵌套内容应用后超 item 宽由 arrange
        // 钳制/溢出；FlowShell（引用外壳/li 行框）flex 内维持现状回填。
        // fontScaleCtx：容器继承字号 ctx——容器自身 style font-size 声明经 ResolveFontScaleCtx
        // 叠加后传块级子树重入（匿名行内 item 走 span 解析不吃）；子项透明度归 1 防双淡的
        // ColorTransform 路线对字号 ctx 不适用（emBase 无壳可落），必须显式透传
        Widget CreateFlexContainer(HtmlAgilityPack.HtmlNode node,
            int listDepth,
            Color? colorOverride,
            int depth,
            TextAnchor blockAlign,
            TextWordBreak wordBreakCtx,
            bool noWrapCtx,
            float lineHeightMul,
            float lineHeightPx,
            float opacityCtx,
            float letterSpacingCtx,
            float fontScaleCtx = 1f) {
            if (depth > MaxDepth) {
                return null;
            }
            FlexPanelWidget flexPanel = new() {
                // 块级盒语义：display:flex 占满宿主宽（Near 塌缩内容宽会让 justify-content/
                // flex-grow 失去可分配空间），对齐移交由壳/sizer 出口按既有链覆盖
                HorizontalAlignment = WidgetAlignment.Stretch, ColorTransform = Color.White * (opacityCtx * ResolveOpacityMultiplier(node))
            };
            string style = node.GetAttributeValue("style", null);
            // 容器属性声明序循环（简写重置槽位、长手随序覆写，CSS 级联口径统一）：
            // flex-flow=direction+wrap 双槽简写、gap 双轴简写；无效值整条丢不回退前条
            FlexDirection direction = FlexDirection.Horizontal;
            FlexWrap wrap = FlexWrap.NoWrap;
            FlexJustification justify = FlexJustification.Start;
            WidgetAlignment align = WidgetAlignment.Near;
            // 轴序：引擎 Gap.X 恒水平=列间距、Y=行间距（CSS gap 首值=row-gap、次值=column-gap）
            Vector2 gap = Vector2.Zero;
            foreach (KeyValuePair<string, string> declaration in CssStyleParser.ParseDeclarations(style)) {
                switch (declaration.Key) {
                    case "flex-direction": direction = ParseFlexDirection(declaration.Value) ?? direction; break;
                    case "flex-wrap": wrap = ParseFlexWrap(declaration.Value) ?? wrap; break;
                    case "flex-flow": {
                        // <direction> || <wrap> 双 token 任意序分发（`row wrap`/`column` 单 token
                        // 合法）；任一 token 不识别整条拒（简写原子性）
                        FlexDirection flowDirection = direction;
                        FlexWrap flowWrap = wrap;
                        bool flowValid = true;
                        foreach (string flowToken in declaration.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)) {
                            if (ParseFlexDirection(flowToken) is { } parsedDirection) {
                                flowDirection = parsedDirection;
                            }
                            else if (ParseFlexWrap(flowToken) is { } parsedWrap) {
                                flowWrap = parsedWrap;
                            }
                            else {
                                flowValid = false;
                                break;
                            }
                        }
                        if (flowValid) {
                            direction = flowDirection;
                            wrap = flowWrap;
                        }
                        break;
                    }
                    case "gap": {
                        string[] tokens = declaration.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        // columnGap 预声明防 CS0165：短路与 out 内联变量跨条件赋值不可追踪
                        float rowGap;
                        float columnGap = 0f;
                        if (tokens.Length is < 1 or > 2
                            || !TryParseFlexGapValue(tokens[0], out rowGap)
                            || (tokens.Length == 2 && !TryParseFlexGapValue(tokens[1], out columnGap))) {
                            break;
                        }
                        gap = new Vector2(tokens.Length == 2 ? columnGap : rowGap, rowGap);
                        break;
                    }
                    case "row-gap":
                        if (TryParseFlexGapValue(declaration.Value, out float rowGapOnly)) {
                            gap = new Vector2(gap.X, rowGapOnly);
                        }
                        break;
                    case "column-gap":
                        if (TryParseFlexGapValue(declaration.Value, out float columnGapOnly)) {
                            gap = new Vector2(columnGapOnly, gap.Y);
                        }
                        break;
                    case "justify-content": justify = ParseFlexJustify(declaration.Value) ?? justify; break;
                    case "align-items": align = ParseFlexAlign(declaration.Value) ?? align; break;
                }
            }
            flexPanel.Direction = direction;
            flexPanel.Wrap = wrap;
            flexPanel.Gap = gap;
            flexPanel.JustifyContent = justify;
            flexPanel.AlignItems = align;
            // 子项继承上下文（容器声明随子树重入链透传，与 div 容器同口径）；容器自身
            // text-align/align 属性覆写继承（div case 同款 ParseBlockAlignment 链）
            TextAnchor itemAlign = ParseBlockAlignment(node) ?? blockAlign;
            (TextWordBreak itemBreak, bool itemNoWrap) = ResolveWordBreakCtx(node, wordBreakCtx, noWrapCtx);
            (float itemLhMul, float itemLhPx) = ResolveLineHeightCtx(node, lineHeightMul, lineHeightPx);
            float itemLs = ResolveLetterSpacingCtx(node, letterSpacingCtx);
            float itemFontCtx = ResolveFontScaleCtx(node, fontScaleCtx);
            List<TextSpan> pendingSpans = [];

            // 行内子节点累积 → 匿名段落 item（块级元素子边界与收尾处冲刷；纯空白经
            // TrimLeadingWhitespace 蒸发不产件，同 Flush 口径）。容器 ctx 已落
            // flexPanel.ColorTransform 浸透，匿名件不再叠（叠乘即双淡）
            void FlushAnonymousItem() {
                if (pendingSpans.Count == 0) {
                    return;
                }
                HtmlSpanParser.TrimLeadingWhitespace(pendingSpans);
                if (pendingSpans.Count == 0) {
                    pendingSpans.Clear();
                    return;
                }
                RichTextWidget widget = CreateRichText(colorOverride);
                if (itemAlign != TextAnchor.Default) {
                    widget.HorizontalAlignment = WidgetAlignment.Stretch;
                    widget.TextAnchor = itemAlign;
                }
                widget.WordBreak = itemBreak;
                if (itemNoWrap) {
                    widget.WordWrap = false;
                }
                widget.LetterSpacing = itemLs;
                widget.LineHeightMultiplier = itemLhMul > 0f ? itemLhMul : 1f;
                if (itemLhPx > 0f) {
                    widget.LineHeightPx = itemLhPx;
                }
                widget.ColorTransform = Color.White;
                widget.Spans.AddRange(pendingSpans);
                ClaimAnchors(widget);
                flexPanel.Children.Add(widget);
                pendingSpans.Clear();
            }

            // flex 子项构建期深度 +1（覆盖全子项构建循环：img 升级件/匿名行内累积/块级子树
            // 重入 BuildHtmlNodes——子树内 pre 直发块与顶层 pre case 均经 m_flexDepth 判定走
            // flexNaturalBasis）。构建中途异常的减配平由构建入口 m_flexDepth=0 统一清零兜底
            m_flexDepth++;
            foreach (HtmlAgilityPack.HtmlNode child in node.ChildNodes) {
                if (child.NodeType == HtmlAgilityPack.HtmlNodeType.Text) {
                    HtmlSpanParser.ParseHtmlNode(child, pendingSpans, default, EffectiveTextColor(colorOverride));
                    continue;
                }
                if (child.NodeType != HtmlAgilityPack.HtmlNodeType.Element) {
                    continue;
                }
                // 全局 hidden 属性（同 BuildHtmlNodes 口径）：任意值隐藏且不断开匿名行内累积
                //（display:none 不拆匿名行内盒，浏览器同口径）
                if (child.GetAttributeValue("hidden", null) != null) {
                    continue;
                }
                FlushAnonymousItem();
                if (child.Name.Equals("img", StringComparison.OrdinalIgnoreCase)
                    && ImageLoader != null) {
                    // 行内 img 升级为独立 item（AppendInlineChildren 同款管线；临时面板仅作
                    // AddImageBlock 挂/摘载体，子树 ctx 归一传 1——容器淡出走 ColorTransform）
                    PendAnchor(child);
                    StackPanelWidget imgPanel = CreatePanel();
                    Widget imgItem = AddImageBlockWithAttributes(imgPanel, child, colorOverride, 1f, itemFontCtx);
                    ApplyFlexItemProps(child, imgItem);
                    ClaimAnchors(imgItem);
                    flexPanel.Children.Add(imgItem);
                    continue;
                }
                if (!IsFlexDisplay(child)
                    && !IsBlockTag(child.Name)
                    && !ContainsBlockChild(child)) {
                    // 行内桶（span/b/i/a 等纯行内标签）：并入匿名行内累积——CSS 匿名 flex item
                    // 口径，连续行内内容（`<span>x</span>tail` 混合形态）合并单一段落件。
                    // display:flex 行内标签不入桶（CSS blockification 升块，下方块级路径经顶层
                    // 拦截产独立 item）；header/main/nav/aside/form/hgroup 等不在 IsBlockTag
                    // 名单的容器标签含块级子时同走块级路径（与 div case ContainsBlockChild 重入
                    // 口径一致，防整子树被 span 平铺拆散）
                    PendAnchor(child);
                    HtmlSpanParser.ParseHtmlNode(child, pendingSpans, default, EffectiveTextColor(colorOverride), linkColor: m_bodyLinkColor);
                    continue;
                }
                // 块级元素子（含 display:flex 升块）→ 子树块级构建（单节点重入顶层分发，li 直发
                // 块 default 同口径）：临时面板收集，单件上提直挂（少一层无谓嵌套），多件（一元素
                // 拆多块）整面板作一件。子项透明度归 1（容器 ColorTransform 担），字号 ctx 照传
                StackPanelWidget itemPanel = CreatePanel();
                BuildHtmlNodes(
                    [child],
                    itemPanel,
                    listDepth,
                    colorOverride,
                    depth + 1,
                    itemAlign,
                    itemBreak,
                    itemNoWrap,
                    itemLhMul,
                    itemLhPx,
                    1f,
                    itemLs,
                    itemFontCtx
                );
                if (itemPanel.Children.Count == 0) {
                    // 空元素子（如 `<div style="flex:1"></div>`）：CSS 口径仍是 flex item（空盒
                    // 参与 grow 分配）——补零尺寸空载体件承接 attached 属性（CanvasWidget 无子
                    // 件 desired=(0,0)），无视觉副作用
                    Widget emptyItem = new CanvasWidget();
                    ApplyFlexItemProps(child, emptyItem);
                    flexPanel.Children.Add(emptyItem);
                    continue;
                }
                Widget item = itemPanel.Children.Count == 1 ? itemPanel.Children[0] : itemPanel;
                ApplyFlexItemProps(child, item);
                flexPanel.Children.Add(item);
            }
            FlushAnonymousItem();
            m_flexDepth--;
            // 纯空白子树不产件（零尺寸 widget 仍会靠 Margin 贡献空白，同空壳不产口径）
            return flexPanel.Children.Count > 0 ? flexPanel : null;
        }

        // gap/basis 值解析：TryParseWidthValue 收 px/em/rem/pt；% 拒（相对主轴尺寸布局期不可
        // 得）；∞/NaN 拒（"Infinity"/"1e40" 字面量会经 float.TryParse 通过——∞ gap 无限放大
        // 行宽、∞ basis 把主轴期望带成 ∞，均毁排）
        static bool TryParseFlexGapValue(string token, out float px) => CssStyleParser.TryParseWidthValue(token, out bool isPercent, out px)
            && !isPercent
            && float.IsFinite(px);

        // flex-grow/shrink 数值（CSS 非负 number；负值/垃圾/∞/NaN 拒——grow=∞ 会让
        // free×grow/Σgrow 除零出 NaN 毁行分配）
        static bool TryParseFlexNumber(string token, out float value) => float.TryParse(
                token.Trim(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out value
            )
            && value >= 0f
            && float.IsFinite(value);

        // flex-direction 值解析（无效返回 null 整条丢）
        static FlexDirection? ParseFlexDirection(string value) {
            return value.Trim().ToLowerInvariant() switch {
                "row" => FlexDirection.Horizontal,
                "row-reverse" => FlexDirection.HorizontalReverse,
                "column" => FlexDirection.Vertical,
                "column-reverse" => FlexDirection.VerticalReverse,
                _ => null
            };
        }

        // flex-wrap 值解析（nowrap=引擎缺省；无效返回 null 整条丢）
        static FlexWrap? ParseFlexWrap(string value) {
            return value.Trim().ToLowerInvariant() switch {
                "nowrap" => FlexWrap.NoWrap,
                "wrap" => FlexWrap.Wrap,
                "wrap-reverse" => FlexWrap.WrapReverse,
                _ => null
            };
        }

        // justify-content 值解析（start/end 裸值也收；无效返回 null 整条丢）
        static FlexJustification? ParseFlexJustify(string value) {
            return value.Trim().ToLowerInvariant() switch {
                "flex-start" or "start" => FlexJustification.Start,
                "flex-end" or "end" => FlexJustification.End,
                "center" => FlexJustification.Center,
                "space-between" => FlexJustification.SpaceBetween,
                "space-around" => FlexJustification.SpaceAround,
                "space-evenly" => FlexJustification.SpaceEvenly,
                _ => null
            };
        }

        // align-items 值解析：stretch 与 baseline（无基线感知排版，近 stretch）；flex-start/
        // start 显式归引擎缺省 Near；无效返回 null 整条丢
        static WidgetAlignment? ParseFlexAlign(string value) {
            return value.Trim().ToLowerInvariant() switch {
                "stretch" or "baseline" => WidgetAlignment.Stretch,
                "flex-start" or "start" => WidgetAlignment.Near,
                "flex-end" or "end" => WidgetAlignment.Far,
                "center" => WidgetAlignment.Center,
                _ => null
            };
        }

        // flex 子项属性（子元素 style → attached，须落在 flex 面板直接子件上）。CSS 缺省预种：
        // shrink=1（flex-shrink 初始值——引擎缺省 0 会让超宽行无恢复通道：文本件 basis=auto
        // ≈整行宽、多件行必溢出即坏主用例）；grow=0；basis 不落=引擎按子件期望（引擎 Basis<0
        // 为 auto 哨兵，FlexPanelWidget.MainBasis 读码确认 >=0 皆合法定值——0 是合法固定
        // basis 非哨兵，直接落无歧义）。
        // flex 简写按 CSS 全值展开（声明序迭代：后置简写重置三槽覆盖先置长手，级联序口径）：
        // number 形省略 basis 位=0%（flex:1 ≡ 1 1 0%，basis=0+grow 支撑多件等分）；关键字形
        // none=0 0 auto、auto=1 1 auto、initial=0 1 auto。不认识的关键字 token 跳过不整条拒
        //（宽松口径）；order/align-self 不做（留档）
        static void ApplyFlexItemProps(HtmlAgilityPack.HtmlNode node, Widget item) {
            float grow = 0f;
            float shrink = 1f;
            float? basis = null;
            foreach (KeyValuePair<string, string> declaration in CssStyleParser.ParseDeclarations(node.GetAttributeValue("style", null))) {
                switch (declaration.Key) {
                    case "flex": {
                        // 简写重置三槽（CSS 级联：后置简写覆盖先置长手）再按位填充
                        grow = 0f;
                        shrink = 1f;
                        basis = null;
                        bool sawNumber = false;
                        bool basisSeen = false;
                        foreach (string token in declaration.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)) {
                            if (TryParseFlexNumber(token, out float number)) {
                                if (!sawNumber) {
                                    grow = number;
                                    sawNumber = true;
                                }
                                else {
                                    shrink = number;
                                }
                            }
                            else if (token.Trim().ToLowerInvariant() == "none") {
                                grow = 0f;
                                shrink = 0f;
                                basisSeen = true;
                            }
                            else if (token.Trim().ToLowerInvariant() == "auto") {
                                grow = 1f;
                                shrink = 1f;
                                basisSeen = true;
                            }
                            else if (token.Trim().ToLowerInvariant() == "initial") {
                                grow = 0f;
                                shrink = 1f;
                                basisSeen = true;
                            }
                            else if (TryParseFlexGapValue(token, out float basisValue)) {
                                basis = basisValue;
                                basisSeen = true;
                            }
                        }
                        // number 形省略 basis 位按 CSS 补 0%（flex:1 ≡ 1 1 0%）
                        if (sawNumber && !basisSeen) {
                            basis = 0f;
                        }
                        break;
                    }
                    case "flex-grow":
                        if (TryParseFlexNumber(declaration.Value, out float growValue)) {
                            grow = growValue;
                        }
                        break;
                    case "flex-shrink":
                        if (TryParseFlexNumber(declaration.Value, out float shrinkValue)) {
                            shrink = shrinkValue;
                        }
                        break;
                    case "flex-basis":
                        // % 拒（相对主轴尺寸不可得）；auto/content 重置为按期望（不落=引擎 -1）
                        if (TryParseFlexGapValue(declaration.Value, out float basisNumber)) {
                            basis = basisNumber;
                        }
                        else if (declaration.Value.Trim().ToLowerInvariant() is "auto" or "content") {
                            basis = null;
                        }
                        break;
                }
            }
            FlexPanelWidget.SetGrow(item, grow);
            FlexPanelWidget.SetShrink(item, shrink);
            if (basis.HasValue) {
                FlexPanelWidget.SetBasis(item, basis.Value);
            }
        }

        // 返回待挂件不直接挂树（调用方统一挂）：顶层 case 出口接 ApplyBlockSizing（CSS width
        // 钳列表整体），li 内嵌列表由递归调用方直挂（li 级 width 不吃）。
        // fontScaleCtx：列表继承字号 ctx（tier-1.5）——列表节点自身 style font-size 声明经
        // ResolveFontScaleCtx 叠加进 li 子树（li 内容段落/li 直发块/嵌套列表/行内 img 同吃）；
        // 列表自身壳（WrapInStyleBox(listNode)）吃继承 ctx，自身声明归 BoxLengthContext 内部
        Widget BuildHtmlList(HtmlAgilityPack.HtmlNode listNode,
            bool ordered,
            StackPanelWidget panel,
            int listDepth,
            Color? colorOverride,
            int depth,
            TextAnchor blockAlign = TextAnchor.Default,
            TextWordBreak wordBreakCtx = TextWordBreak.BreakWord,
            bool noWrapCtx = false,
            float lineHeightMul = 0f,
            float lineHeightPx = 0f,
            float opacityCtx = 1f,
            float letterSpacingCtx = 0f,
            float fontScaleCtx = 1f) {
            if (depth > MaxDepth) {
                return null;
            }
            // 列表节点自身 word-break/white-space/line-height 声明在此统一 Resolve（顶层与 li 内
            // 嵌套同口径，调用点只传继承上下文）
            (TextWordBreak listBreak, bool listNoWrap) = ResolveWordBreakCtx(listNode, wordBreakCtx, noWrapCtx);
            (float listLhMul, float listLhPx) = ResolveLineHeightCtx(listNode, lineHeightMul, lineHeightPx);
            wordBreakCtx = listBreak;
            noWrapCtx |= listNoWrap;
            lineHeightMul = listLhMul;
            lineHeightPx = listLhPx;
            letterSpacingCtx = ResolveLetterSpacingCtx(listNode, letterSpacingCtx);
            // 列表节点自身 opacity 同在此 Resolve（叠乘语义）：marker 标签随列表声明淡出
            //（ColorTransform 浸透，li 内容文字经 liCtx 各段落件自担）
            opacityCtx *= ResolveOpacityMultiplier(listNode);
            // 列表节点自身 style font-size 声明叠加（CSS 块级继承，li 子树同吃）
            float listFontCtx = ResolveFontScaleCtx(listNode, fontScaleCtx);
            StackPanelWidget listPanel = CreatePanel();
            listPanel.Margin = new Vector2(0f, 2f);
            if (blockAlign != TextAnchor.Default) {
                // 对齐容器内列表：列表面板撑满宿主（否则 Near 收缩到内容宽，行框 Stretch 无意义）
                listPanel.HorizontalAlignment = WidgetAlignment.Stretch;
            }
            // <ol start="4">：解析失败回退 1
            int number = int.TryParse(listNode.GetAttributeValue("start", null), out int parsedStart) ? parsedStart : 1;
            // 列表级 type 属性（li 内同名属性逐项覆写）：ol 取 1/a/A/i/I、ul 取 disc/circle/square
            char listType = ParseListType(listNode, ordered) ?? '\0';
            // 文字列按全列表最宽 marker 对齐（"10." 比 "1." 宽），保底 marker→文字间隙；
            // 嵌套列表挂在本级文字列内，缩进节奏恒一档 ListIndentPerDepth。
            // marker 文本两遍共用一遍算出（li value 重编号/type 覆写逐项生效，宽度扫描吃同一序列）
            List<HtmlAgilityPack.HtmlNode> items = [.. listNode.Elements("li")];
            float markerScale = FontScale;
            float maxMarkerWidth = 0f;
            string[] markers = new string[items.Count];
            int scanNumber = number;
            for (int i = 0; i < items.Count; i++) {
                markers[i] = MarkerText(items[i], ordered, listType, scanNumber, listDepth + 1, out int nextNumber);
                maxMarkerWidth = MathUtils.Max(maxMarkerWidth, Font.MeasureText(markers[i], new Vector2(markerScale), Vector2.Zero).X);
                scanNumber = nextNumber;
            }
            float contentX = MathUtils.Max(ListIndentPerDepth, MarkerOffsetX + maxMarkerWidth + MarkerTextGap);
            for (int itemIndex = 0; itemIndex < items.Count; itemIndex++) {
                HtmlAgilityPack.HtmlNode li = items[itemIndex];
                // 行框宽随嵌套收窄（同 MarkdownToWidgetsBuilder.CreateListItem）
                CanvasWidget row = new() { Size = new Vector2(NestedContentWidth(), -1f) };
                if (blockAlign != TextAnchor.Default) {
                    // 对齐容器内列表：行框撑满列表宽（NestedContentWidth()=-1 时 desired 收缩），
                    // 文字列 Stretch+锚点才有居中空间
                    row.HorizontalAlignment = WidgetAlignment.Stretch;
                }
                // li 自身 id（裁定 id=任意标签）：行框即 li 视觉根，挂内容前领取——内容锚点
                // 随后各自挂起，互不抢占
                PendAnchor(li);
                ClaimAnchors(row);
                // 格内注册 FlowShell（顶层不注册，行为不变）：li 行框宽归所在格 panel 按格净宽
                // 回填（IndentX=行框挂载点缩进；消除格内 li 定宽/natural 污染列协商的 D1 源）
                CurrentPanel?.Shells.Add(new ShellEntry { Frame = row, IndentX = CurrentIndentX, Kind = ShellKind.FlowShell });
                FontTextWidget markerLabel = new() {
                    Text = markers[itemIndex], Font = Font, Color = EffectiveTextColor(colorOverride), FontScale = markerScale
                };
                // marker 随列表 opacity 声明淡出（FontTextWidget 色×GlobalColorTransform；
                // 不含 li 自身声明——li 声明只管 li 内容，marker 归列表级）
                markerLabel.ColorTransform = Color.White * opacityCtx;
                row.Children.Add(markerLabel);
                CanvasWidget.SetPosition(markerLabel, new Vector2(MarkerOffsetX, MarkerOffsetY));
                StackPanelWidget contentPanel = CreatePanel();
                // 文字列缩进走 MarginLeft 不走 SetPosition（Canvas 对 SetPosition 子件按 desired 排布，
                // alignment 失效）：对齐容器内列表由 Stretch 撑满行框剩余宽，锚点才有居中/靠右空间；
                // Near 时 x=MarginLeft 视觉不变
                contentPanel.MarginLeft = contentX;
                if (blockAlign != TextAnchor.Default) {
                    contentPanel.HorizontalAlignment = WidgetAlignment.Stretch;
                }
                row.Children.Add(contentPanel);
                // 嵌套上下文：li 内代码块/图片块宽度按文字列收窄。contentX 相对本级行挂载点，
                // 绝对缩进=父累计+本级（引用内列表、li 套 li 靠累加保持口径）
                float prevIndent = CurrentIndentX;
                CurrentIndentX += contentX;
                // li 内行内内容累积成段；内嵌块级标签递归处理
                List<TextSpan> pendingSpans = [];
                (TextWordBreak liBreak, bool liNoWrap) = ResolveWordBreakCtx(li, wordBreakCtx, noWrapCtx);
                (float liLhMul, float liLhPx) = ResolveLineHeightCtx(li, lineHeightMul, lineHeightPx);
                float liLs = ResolveLetterSpacingCtx(li, letterSpacingCtx);
                // li 自身 opacity 叠进子树 ctx：行内段落（FlushLi）与内嵌块级递归同吃；
                // li 壳装饰（WrapInStyleBox）走壳色乘，两路同源不双乘。
                // li 自身 style font-size 声明叠加（CSS 块级继承，li 内容各流同吃）
                float liOpacityCtx = opacityCtx * ResolveOpacityMultiplier(li);
                float liFontCtx = ResolveFontScaleCtx(li, listFontCtx);

                void FlushLi() {
                    if (pendingSpans.Count > 0) {
                        HtmlSpanParser.TrimLeadingWhitespace(pendingSpans);
                        if (pendingSpans.Count > 0) {
                            RichTextWidget widget = CreateRichText(colorOverride);
                            if (blockAlign != TextAnchor.Default) {
                                widget.HorizontalAlignment = WidgetAlignment.Stretch;
                                widget.TextAnchor = blockAlign;
                            }
                            widget.WordBreak = liBreak;
                            if (liNoWrap) {
                                widget.WordWrap = false;
                            }
                            // line-height：li 级声明覆盖列表继承（Resolve 已并入），口径同 Flush
                            widget.LineHeightMultiplier = liLhMul > 0f ? liLhMul : 1f;
                            if (liLhPx > 0f) {
                                widget.LineHeightPx = liLhPx;
                            }
                            // letter-spacing：li 级声明覆盖列表继承（Resolve 已并入，替换语义）
                            widget.LetterSpacing = liLs;
                            widget.ColorTransform = Color.White * liOpacityCtx;
                            widget.Spans.AddRange(pendingSpans);
                            ClaimAnchors(widget);
                            contentPanel.Children.Add(widget);
                        }
                        pendingSpans = [];
                    }
                }

                foreach (HtmlAgilityPack.HtmlNode child in li.ChildNodes) {
                    bool isBlock = child.NodeType == HtmlAgilityPack.HtmlNodeType.Element
                        && child.Name.ToLowerInvariant() is "ul"
                            or "ol"
                            or "menu"
                            or "blockquote"
                            or "pre"
                            or "p"
                            or "div"
                            or "section"
                            or "article"
                            or "table"
                            or "footer"
                            or "address"
                            or "center"
                            or "dl"
                            or "dt"
                            or "dd"
                            or "figure"
                            or "figcaption"
                            or "details"
                            or "summary"
                            or "caption"
                            or "h1"
                            or "h2"
                            or "h3"
                            or "h4"
                            or "h5"
                            or "h6"
                            or "hr"
                            or "html"
                            or "body";
                    if (isBlock) {
                        // li 直发块不经 BuildHtmlNodes 分发，hidden 在此补拦（default case 重入
                        // 顶层分发自带拦截，blockquote/pre/hr/h1-6/列表直发只认此处）
                        if (child.GetAttributeValue("hidden", null) != null) {
                            continue;
                        }
                        FlushLi();
                        // li 直发块锚点同顶层口径：边界 FlushLi 后挂（防冲刷段抢注本节点 id）
                        PendAnchor(child);
                        // li 级 width 不吃（留档）：li 直发块（blockquote/pre/h1-6/hr/嵌套列表）
                        // 均不经 ApplyBlockSizing/FinalizeBlock——行框内 % 分母与钳制口径未定
                        switch (child.Name.ToLowerInvariant()) {
                            case "ul":
                            case "ol":
                            case "menu":
                                // li 级 width 不吃：嵌套列表不经 ApplyBlockSizing（li 行框内
                                // % 分母/钳制口径未定，留档不做；顶层列表整体已接）
                                contentPanel.Children.Add(
                                    BuildHtmlList(
                                        child,
                                        child.Name.Equals("ol", StringComparison.Ordinal),
                                        contentPanel,
                                        listDepth + 1,
                                        colorOverride,
                                        depth + 1,
                                        blockAlign,
                                        liBreak,
                                        liNoWrap,
                                        liLhMul,
                                        liLhPx,
                                        liOpacityCtx,
                                        liLs,
                                        liFontCtx
                                    )
                                ); break;
                            case "blockquote": {
                                (TextWordBreak liQuoteBreak, bool liQuoteNoWrap) = ResolveWordBreakCtx(child, liBreak, liNoWrap);
                                (float liQuoteLhMul, float liQuoteLhPx) = ResolveLineHeightCtx(child, liLhMul, liLhPx);
                                float liQuoteLs = ResolveLetterSpacingCtx(child, liLs);
                                Widget quote = CreateHtmlQuote(
                                    child,
                                    listDepth + 1,
                                    colorOverride,
                                    depth + 1,
                                    blockAlign,
                                    liQuoteBreak,
                                    liQuoteNoWrap,
                                    liQuoteLhMul,
                                    liQuoteLhPx,
                                    liOpacityCtx * ResolveOpacityMultiplier(child),
                                    liQuoteLs,
                                    ResolveFontScaleCtx(child, liFontCtx)
                                );
                                Widget liQuote = WrapInStyleBox(child, quote, colorOverride, fontScaleCtx: liFontCtx) ?? quote;
                                ClaimAnchors(liQuote);
                                contentPanel.Children.Add(liQuote);
                                break;
                            }
                            case "pre": {
                                HtmlAgilityPack.HtmlNode code = child.Element("code") ?? child;
                                ParseCodeTextStyle(
                                    child,
                                    out Color? liCodeTextColor,
                                    out float? liCodeScaleOverride,
                                    out float? liCodeRoundingRadius,
                                    out Color? liCodeBackgroundColor
                                );
                                Widget codeBlock = CreateCodeBlock(
                                    ReplaceBrWithNewline(code).Split('\n'),
                                    liCodeTextColor,
                                    liCodeScaleOverride,
                                    liCodeRoundingRadius,
                                    m_flexDepth > 0,
                                    liCodeBackgroundColor
                                );
                                // 代码块随 ctx×自身声明淡出（同顶层 pre 口径）
                                codeBlock.ColorTransform = Color.White * (liOpacityCtx * ResolveOpacityMultiplier(child));
                                Widget liCode = WrapInStyleBox(child, codeBlock, colorOverride, fontScaleCtx: liFontCtx) ?? codeBlock;
                                ClaimAnchors(liCode);
                                contentPanel.Children.Add(liCode);
                                break;
                            }
                            case "h1":
                            case "h2":
                            case "h3":
                            case "h4":
                            case "h5":
                            case "h6": {
                                RichTextWidget widget = CreateHeading(child.Name[1] - '0', colorOverride);
                                TextAnchor headingAlign = ParseBlockAlignment(child) ?? blockAlign;
                                if (headingAlign != TextAnchor.Default) {
                                    widget.HorizontalAlignment = WidgetAlignment.Stretch;
                                    widget.TextAnchor = headingAlign;
                                }
                                (TextWordBreak liHeadingBreak, bool liHeadingNoWrap) = ResolveWordBreakCtx(child, liBreak, liNoWrap);
                                (float liHeadingLhMul, float liHeadingLhPx) = ResolveLineHeightCtx(child, liLhMul, liLhPx);
                                float liHeadingLs = ResolveLetterSpacingCtx(child, liLs);
                                ApplyParagraphBreakStyle(widget, (liHeadingBreak, liHeadingNoWrap, liHeadingLhMul, liHeadingLhPx, liHeadingLs));
                                // CSS opacity：li 内标题同主链口径（ctx × 自身声明）→ ColorTransform。
                                // 字号 ctx：li ctx × 标题阶梯（UA font-size 对自身壳同生效，主链 h1-6
                                // case 同款）——li 直发 h2 自身 padding em 按 1.4 阶梯解析
                                widget.ColorTransform = Color.White * (liOpacityCtx * ResolveOpacityMultiplier(child));
                                HtmlSpanParser.ParseHtmlNode(child, widget.Spans, default, widget.Color, linkColor: m_bodyLinkColor);
                                Widget liHeading = WrapInStyleBox(
                                        child,
                                        widget,
                                        colorOverride,
                                        fontScaleCtx: liFontCtx * HeadingScale(child.Name[1] - '0')
                                    )
                                    ?? widget;
                                ClaimAnchors(liHeading);
                                contentPanel.Children.Add(liHeading);
                                break;
                            }
                            case "hr": {
                                Widget thematicBreak = CreateHtmlThematicBreak(child);
                                // hr 线随 ctx×自身声明淡出（同顶层 hr 口径）
                                thematicBreak.ColorTransform = Color.White * (liOpacityCtx * ResolveOpacityMultiplier(child));
                                Widget liBreakRule = WrapInStyleBox(child, thematicBreak, colorOverride, fontScaleCtx: liFontCtx) ?? thematicBreak;
                                ClaimAnchors(liBreakRule);
                                contentPanel.Children.Add(liBreakRule);
                                break;
                            }
                            default: {
                                // p/div/section/article/table：单节点重入顶层分发（BuildHtmlNodes），
                                // 与顶层同口径——纯行内整块成段、嵌块递归、li 内 p>img 不再静默丢
                                BuildHtmlNodes(
                                    [child],
                                    contentPanel,
                                    listDepth + 1,
                                    colorOverride,
                                    depth + 1,
                                    blockAlign,
                                    liBreak,
                                    liNoWrap,
                                    liLhMul,
                                    liLhPx,
                                    liOpacityCtx,
                                    liLs,
                                    liFontCtx
                                );
                                break;
                            }
                        }
                    }
                    else if (child.NodeType == HtmlAgilityPack.HtmlNodeType.Element
                        && child.Name.Equals("img", StringComparison.OrdinalIgnoreCase)
                        && ImageLoader != null) {
                        // li 内行内 <img> 升级为图片块（同 AppendInlineChildren 口径）
                        FlushLi();
                        PendAnchor(child);
                        Widget imgBlock = AddImageBlockWithAttributes(contentPanel, child, colorOverride, liOpacityCtx, liFontCtx);
                        ClaimAnchors(imgBlock);
                        contentPanel.Children.Add(imgBlock);
                    }
                    else {
                        if (child.NodeType == HtmlAgilityPack.HtmlNodeType.Element) {
                            PendAnchor(child);
                        }
                        HtmlSpanParser.ParseHtmlNode(child, pendingSpans, default, EffectiveTextColor(colorOverride), linkColor: m_bodyLinkColor);
                    }
                }
                FlushLi();
                CurrentIndentX = prevIndent;
                // li 自身 style 盒属性：整行（marker+内容）入壳。row 固定 Size.X=NCW，border/pad
                // 铺成 margin 会右扩列表列——border-box 收内容：入壳前按左右横宽收窄 row
                //（CSS margin 外扩同 ddOuter 既有留档口径，不在此收）
                float liShrink = 0f;
                string liStyle = li.GetAttributeValue("style", null);
                if (!string.IsNullOrEmpty(liStyle)) {
                    // em/% 上下文与下方 WrapInStyleBox(li,…) 同源（BoxLengthContext 同参同结果）：
                    // row 固定宽，收窄补偿必须吃到壳侧同一 em/% 解析结果，否则壳内缩把行顶出列。
                    // 字号 ctx 同传 liFontCtx（li 声明归 BoxLengthContext 内部，此处只传继承链）
                    (float liEmBase, float liPercentBase) = BoxLengthContext(liStyle, liFontCtx);
                    CssStyleParser.TryGetBox(liStyle, "padding", out Vector4 liPad, liEmBase, liPercentBase);
                    liShrink += MathUtils.Max(liPad.X, 0f) + MathUtils.Max(liPad.Z, 0f);
                    if (CssStyleParser.TryGetBorder(liStyle, out CssStyleParser.CssBorderSpec liBorder)
                        && liBorder.HasAny) {
                        liShrink += MathUtils.Max(liBorder.Widths.X, 0f) + MathUtils.Max(liBorder.Widths.Z, 0f);
                    }
                }
                if (liShrink > 0f) {
                    row.Size = new Vector2(MathUtils.Max(row.Size.X - liShrink, 0f), row.Size.Y);
                }
                listPanel.Children.Add(WrapInStyleBox(li, row, colorOverride, fontScaleCtx: liFontCtx) ?? row);
                MarkerText(li, ordered, listType, number, listDepth + 1, out number);
            }
            // style 盒属性：列表整体入壳（listPanel 自带 margin(0,2) 与 padding 线性叠加）；
            // 字号 ctx=继承 ctx（列表自身声明归 BoxLengthContext 内部口径）
            return WrapInStyleBox(listNode, listPanel, colorOverride, fontScaleCtx: fontScaleCtx) ?? listPanel;
        }

        // 列表项 marker 文本与下一项编号（两遍共用口径）：li value 先行重编号（HTML 渲染语义，
        // 后续项顺延）、li type 覆写列表级 type。next 输出编号已 +1（ordered 与否调用方皆直用）
        static string MarkerText(HtmlAgilityPack.HtmlNode li, bool ordered, char listType, int number, int depth, out int next) {
            if (ordered && int.TryParse(li.GetAttributeValue("value", null), out int value)) {
                number = value;
            }
            char type = ParseListType(li, ordered) ?? listType;
            next = number + 1;
            return HtmlTextUtils.GetListMarker(ordered, type, number, depth);
        }

        // list/li type 属性解析：ol 认 1/a/A/i/I（原始大小写敏感——小写化会并掉 A/I 两档），
        // ul/menu 认 disc/circle/square（映射 marker 字形）。非法/缺省返回 null（回退默认口径）
        static char? ParseListType(HtmlAgilityPack.HtmlNode node, bool ordered) {
            string type = node.GetAttributeValue("type", null)?.Trim();
            if (ordered) {
                return type switch {
                    "1" => '1',
                    "a" => 'a',
                    "A" => 'A',
                    "i" => 'i',
                    "I" => 'I',
                    _ => null
                };
            }
            return type?.ToLowerInvariant() switch {
                "disc" => '•',
                "circle" => '◦',
                "square" => '▪',
                _ => null
            };
        }

        CanvasWidget CreateHtmlQuote(HtmlAgilityPack.HtmlNode quoteNode,
            int listDepth,
            Color? colorOverride,
            int depth,
            TextAnchor blockAlign = TextAnchor.Default,
            TextWordBreak wordBreakCtx = TextWordBreak.BreakWord,
            bool noWrapCtx = false,
            float lineHeightMul = 0f,
            float lineHeightPx = 0f,
            float opacityCtx = 1f,
            float letterSpacingCtx = 0f,
            float fontScaleCtx = 1f) {
            CanvasWidget outer = CreateQuoteFrame(out StackPanelWidget inner);
            if (blockAlign != TextAnchor.Default) {
                // 对齐容器内引用：外壳与 inner 双层 Stretch 撑满（HtmlWidget 壳路径 ContentWidth=-1，
                // NestedContentWidth()=-1 时外壳 desired 收缩到内容宽，仅 inner Stretch 无居中空间）
                outer.HorizontalAlignment = WidgetAlignment.Stretch;
                inner.HorizontalAlignment = WidgetAlignment.Stretch;
            }
            // 格内注册 FlowShell（顶层不注册，行为不变）：quote 外壳宽归所在格 panel 按格净宽
            // 回填——构建期 NestedContentWidth 是顶层近似值。IndentX=本块挂载点缩进（inner 的
            // +10 是壳内内容缩进，不进外壳条目）
            CurrentPanel?.Shells.Add(new ShellEntry { Frame = outer, IndentX = CurrentIndentX, Kind = ShellKind.FlowShell });
            float prevIndent = CurrentIndentX;
            CurrentIndentX += 10f;
            // opacity 落外壳 ColorTransform（竖条+子树全浸透——竖条 RectangleWidget 非
            // 文字件缺口随之闭合）；子树 ctx 归一防双淡：外壳已担 v，内部段落件落点按 1
            // 恒等，嵌套子块自身声明从 1 起步照吃
            outer.ColorTransform = Color.White * opacityCtx;
            // fontScaleCtx：调用点已叠引用节点 style font-size 声明（CSS 块级继承），直传子树
            BuildHtmlNodes(
                quoteNode.ChildNodes,
                inner,
                listDepth,
                QuoteColor,
                depth + 1,
                blockAlign,
                wordBreakCtx,
                noWrapCtx,
                lineHeightMul,
                lineHeightPx,
                1f,
                letterSpacingCtx,
                fontScaleCtx
            );
            CurrentIndentX = prevIndent;
            return outer;
        }

        // HTML 表格 → TablePanelWidget 直产（spec §7.1）：布局/列协商/格线全由底座承担，本方法只做
        // DOM→行/格映射。行收集：table 直下 tr 与 thead/tbody 内 tr 展平、tfoot 行沉底（HTML 渲染
        // 语义，FooterRows 记行数）；caption 无对应视觉语义，跳过（caption 已在表 case 预提取
        // 渲染）；colgroup/col 宽偏好提取为列 px/% 偏好数组（底座 Measure 期 min+max 双 floor）。
        // thead 行或含 th 的行为表头（粗体由 builder 侧染 span，同 Markdown CreateTable
        // 口径）。无滚动兜底（包装类超宽横向滚动不随迁）：Σmin 超宿主时底座定格列 min 溢出。
        // fontScaleCtx：表节点 style font-size 声明叠加后的格内容子树 ctx（调用点 Resolve，
        // 逐格透传 CreateHtmlCell）
        TablePanelWidget CreateHtmlTable(HtmlAgilityPack.HtmlNode tableNode,
            Color? colorOverride,
            int depth,
            TextAnchor blockAlign = TextAnchor.Default,
            float opacityCtx = 1f,
            float fontScaleCtx = 1f) {
            // 格内容相对格左缘定位（spec §5.3）：入口清零外层嵌套缩进——格 shell 的回填扣减基准
            // 是格净宽，li 内嵌套表格时外层缩进已由挂载位置体现，再计入会双扣；全部出口恢复
            //（prevIndent 纪律同 CreateHtmlQuote，含两处早退）
            float prevIndent = CurrentIndentX;
            CurrentIndentX = 0f;
            if (depth > MaxDepth) {
                CurrentIndentX = prevIndent;
                return null;
            }
            // table cellpadding 属性（px）：CellPadding 横向直挂，纵向保持默认；纯行内格 Size.X
            // 锚定同参（cellPaddingX 透传 CreateHtmlCell）
            float cellPaddingX = CellPaddingX;
            if (int.TryParse(tableNode.GetAttributeValue("cellpadding", null), out int cellPadding)
                && cellPadding >= 0) {
                cellPaddingX = cellPadding;
            }
            // 视觉对齐 MD 表现状：四开关全开+同线色（InnerBorder 在场时 Header/Footer
            // Separator 不双画，仅 InnerBorder=false 的自定义场景生效；FooterSeparator 对称补齐）
            TablePanelWidget widget = new() {
                CellPadding = new Vector2(cellPaddingX, 3f),
                BorderColor = MarkdownTableWidget.GridLineColor,
                OuterBorder = true,
                InnerBorder = true,
                HeaderSeparator = true,
                FooterSeparator = true,
                Margin = new Vector2(0f, 4f)
            };
            // table align=center：表格块整体居中（desired=Σtracks 非拉伸）；left/right 浮动语义
            // 不做（文字环绕无对应排版模型）
            if (ParseBlockAlignment(tableNode) == TextAnchor.HorizontalCenter) {
                widget.HorizontalAlignment = WidgetAlignment.Center;
            }
            // 祖先 ctx×表节点自身声明落表根 ColorTransform：表线/格底（Draw 全×GlobalColorTransform）
            // 与格内子树一次性浸透；格内 ctx 传 1 防格内落点双淡（见 CreateHtmlCell 调用点）
            widget.ColorTransform = Color.White * opacityCtx;
            // bgcolor 回退链根：table CSS 声明 → table 属性 → body 属性（整页包装场景）。
            // 同一元素 CSS 声明胜旧属性（与 text-align 同规），链结构不变
            Color? tableBackground =
                CssStyleParser.TryGetDeclaration(tableNode.GetAttributeValue("style", null), "background-color", out string tableCssBg)
                && CssColorParser.TryParse(tableCssBg, out Color tableCssColor) ? tableCssColor :
                CssColorParser.TryParse(tableNode.GetAttributeValue("bgcolor", null), out Color tableBg) ? tableBg : m_bodyBackground;
            List<(HtmlAgilityPack.HtmlNode Row, bool FromThead)> rows = [];
            // tfoot 行收集沉底（HTML 渲染语义：footer 行无论 DOM 位置恒在表尾），行数设
            // FooterRows 驱动底座表尾结构（FooterSeparator 语义对称 HeaderSeparator）
            List<HtmlAgilityPack.HtmlNode> footerRows = [];
            foreach (HtmlAgilityPack.HtmlNode child in tableNode.ChildNodes) {
                if (child.NodeType != HtmlAgilityPack.HtmlNodeType.Element) {
                    continue;
                }
                // 行收集路径不经 BuildHtmlNodes 分发，hidden 在此补拦（thead/tbody/tfoot 容器同口径）
                if (IsHiddenNode(child)) {
                    continue;
                }
                string childTag = child.Name.ToLowerInvariant();
                if (childTag == "tr") {
                    rows.Add((child, false));
                }
                else if (childTag is "thead" or "tbody" or "tfoot") {
                    if (childTag == "tfoot") {
                        footerRows.AddRange(child.Elements("tr"));
                    }
                    else {
                        foreach (HtmlAgilityPack.HtmlNode tr in child.Elements("tr")) {
                            rows.Add((tr, childTag == "thead"));
                        }
                    }
                }
            }
            foreach (HtmlAgilityPack.HtmlNode tr in footerRows) {
                rows.Add((tr, false));
            }
            if (rows.Count == 0) {
                CurrentIndentX = prevIndent;
                return null;
            }
            // 剔除空行（无 td/th——本方法不产行、底座无对应布局行），使行序号=ExpandSlots 布局行号；
            // FooterRows 计剔除后的真实 footer 行数
            int footerRowCount = footerRows.Count(HasCellElement);
            rows.RemoveAll(entry => !HasCellElement(entry.Row));
            // 自然列数上限（各行可见 td/th 格数最大值）——ExpandSlots 同名口径：span 展开不越过它，
            // 下列对齐模拟的 cursor 推进须与底座逐格一致（M1），单改一侧即错列。hidden 格格构建
            // 跳挂（不进底座行），此处同口径不计，否则列偏好/列对齐映射到不存在的 track
            int naturalColumns = 0;
            foreach ((HtmlAgilityPack.HtmlNode Row, bool _) in rows) {
                int cellCount = 0;
                foreach (HtmlAgilityPack.HtmlNode child in Row.ChildNodes) {
                    if (child.NodeType == HtmlAgilityPack.HtmlNodeType.Element
                        && child.Name.ToLowerInvariant() is "td" or "th"
                        && !IsHiddenNode(child)) {
                        cellCount++;
                    }
                }
                naturalColumns = MathUtils.Max(naturalColumns, cellCount);
            }
            // colgroup/col 宽偏好（col 直挂 table 也认；colgroup 嵌套非法忽略）：col 无自身声明
            // 继承 colgroup（style-or-attr），span=N 覆盖的每条 track 各得全量偏好（CSS 规范
            // 语义）；无 col 子件的 colgroup 组 span=N 同上。偏好越过 naturalColumns 的 track
            // 静默丢弃（ExpandSlots 列数上限——与浏览器虚拟列的口径差异）。% 存百分数，底座
            // Measure 期按宿主可用宽解析
            float[] prefPx = null;
            float[] prefPct = null;
            // col 序列按 DOM 列位推进，隐藏格位不占 track（格构建跳挂同口径）：位 p 在所有出现
            // 它的行里全 hidden 即死位，col 偏好按"活位 rank"落轨（死位后的位前移）。混合行部分
            // 隐藏为病理取保守近似；rowspan 占位/仅含隐藏格行（HasCellElement 仍计入、底座不挂
            // 壳）的行号错位是 M1 模拟既有口径，col 映射不另建模拟
            int domColumns = 0;
            foreach ((HtmlAgilityPack.HtmlNode Row, bool _) in rows) {
                int cellCount = 0;
                foreach (HtmlAgilityPack.HtmlNode child in Row.ChildNodes) {
                    if (child.NodeType == HtmlAgilityPack.HtmlNodeType.Element
                        && child.Name.ToLowerInvariant() is "td" or "th") {
                        cellCount++;
                    }
                }
                domColumns = MathUtils.Max(domColumns, cellCount);
            }
            bool[] hiddenSlot = new bool[domColumns];
            bool[] visibleSlot = new bool[domColumns];
            foreach ((HtmlAgilityPack.HtmlNode Row, bool _) in rows) {
                int position = 0;
                foreach (HtmlAgilityPack.HtmlNode child in Row.ChildNodes) {
                    if (child.NodeType != HtmlAgilityPack.HtmlNodeType.Element
                        || child.Name.ToLowerInvariant() is not ("td" or "th")) {
                        continue;
                    }
                    if (IsHiddenNode(child)) {
                        hiddenSlot[position] = true;
                    }
                    else {
                        visibleSlot[position] = true;
                    }
                    position++;
                }
            }
            int[] liveRank = new int[domColumns + 1]; // [p]=p 之前活位数（前缀和）
            for (int p = 0; p < domColumns; p++) {
                liveRank[p + 1] = liveRank[p] + (hiddenSlot[p] && !visibleSlot[p] ? 0 : 1);
            }

            void ApplyColPreference(int domStart, int domSpan, float px, float pct) {
                if (px <= 0f
                    && pct <= 0f) {
                    return;
                }
                for (int p = domStart; p < MathUtils.Min(domStart + domSpan, domColumns); p++) {
                    if ((hiddenSlot[p] && !visibleSlot[p])
                        || liveRank[p] >= naturalColumns) {
                        continue;
                    }
                    int track = liveRank[p];
                    prefPx ??= new float[naturalColumns];
                    prefPct ??= new float[naturalColumns];
                    prefPx[track] = MathUtils.Max(prefPx[track], px);
                    prefPct[track] = MathUtils.Max(prefPct[track], pct);
                }
            }

            int domCursor = 0;
            foreach (HtmlAgilityPack.HtmlNode child in tableNode.ChildNodes) {
                if (child.NodeType != HtmlAgilityPack.HtmlNodeType.Element) {
                    continue;
                }
                string structuralTag = child.Name.ToLowerInvariant();
                if (structuralTag == "col") {
                    int span = ParseSpanAttribute(child, "span");
                    (float colPx, float colPct) = ParseColWidthPreference(child);
                    ApplyColPreference(domCursor, span, colPx, colPct);
                    domCursor += span;
                }
                else if (structuralTag == "colgroup") {
                    (float groupPx, float groupPct) = ParseColWidthPreference(child);
                    List<HtmlAgilityPack.HtmlNode> groupCols = [];
                    foreach (HtmlAgilityPack.HtmlNode colNode in child.Elements("col")) {
                        groupCols.Add(colNode);
                    }
                    if (groupCols.Count == 0) {
                        int span = ParseSpanAttribute(child, "span");
                        ApplyColPreference(domCursor, span, groupPx, groupPct);
                        domCursor += span;
                    }
                    else {
                        // 有 col 子件：组宽只作缺省继承（col 无自身声明才吃），组 span 被忽略
                        foreach (HtmlAgilityPack.HtmlNode colNode in groupCols) {
                            int span = ParseSpanAttribute(colNode, "span");
                            (float colPx, float colPct) = ParseColWidthPreference(colNode);
                            bool hasOwn = colPx > 0f || colPct > 0f;
                            ApplyColPreference(domCursor, span, hasOwn ? colPx : groupPx, hasOwn ? colPct : groupPct);
                            domCursor += span;
                        }
                    }
                }
            }
            if (prefPx != null) {
                widget.ColumnPreferredWidths = prefPx;
                widget.ColumnPreferredPercents = prefPct;
            }
            // 列对齐：格级属性降列级——自上而下取每列第一个出现的对齐（td/th 与 tr 的 align 属性、
            // style text-align 都认），此后该列锚点固定（照包装类 SetColumnAlignments 语义）；
            // 列号照底座 ExpandSlots 口径模拟（M1）：cursor 跳过被前行 rowspan 登记的占格，span
            // 展开同钳自然列数（naturalColumns）——两侧任一单改即错列。越界 colspan 二次钳
            //（columns−Col）发生在全部 slot.Col 定值之后，不影响列号，故模拟对全部输入与布局期
            // 定列逐槽一致
            List<MarkdownTableAlignment?> alignments = [];
            HashSet<(int Row, int Col)> occupied = [];
            for (int r = 0; r < rows.Count; r++) {
                (HtmlAgilityPack.HtmlNode tr, bool fromThead) = rows[r];
                // 表头行判定行级统一（F3）：thead 行或行内含 th，与 th 的格序无关——原实现随格
                // 遍历置位，th 前的 td 格以 false 建格漏染粗；整行格统一染粗（同 Markdown CreateTable 口径）
                bool isHeader = fromThead || HasHeaderCell(tr);
                // bgcolor 链第二级：tr CSS 声明 → tr 属性 → 表/body 根（同一元素 CSS 胜旧属性）。
                // 挂行壳整体铺（覆盖 rowspan 空隙，浏览器同视觉），格级 CSS/属性由 SetCellBackground
                // 覆盖其上
                Color? rowBackground = CssStyleParser.TryGetDeclaration(tr.GetAttributeValue("style", null), "background-color", out string rowCssBg)
                    && CssColorParser.TryParse(rowCssBg, out Color rowCssColor) ? rowCssColor :
                    CssColorParser.TryParse(tr.GetAttributeValue("bgcolor", null), out Color rowBg) ? rowBg : tableBackground;
                TextAnchor? rowVerticalAlignment = ParseVerticalAlignment(tr, tr);
                List<Widget> cells = [];
                int column = 0;
                foreach (HtmlAgilityPack.HtmlNode cellNode in tr.ChildNodes) {
                    if (cellNode.NodeType != HtmlAgilityPack.HtmlNodeType.Element) {
                        continue;
                    }
                    string cellTag = cellNode.Name.ToLowerInvariant();
                    if (cellTag is not ("td" or "th")) {
                        continue;
                    }
                    // td/th hidden：格收集同走不到 BuildHtmlNodes，补拦（行收集已拦 tr/行组容器）
                    if (IsHiddenNode(cellNode)) {
                        continue;
                    }
                    // colspan/rowspan 属性：空/非法/≤0 → 1（Set 后仅非平凡值登记 attached 状态）
                    int colSpan = ParseSpanAttribute(cellNode, "colspan");
                    int rowSpan = ParseSpanAttribute(cellNode, "rowspan");
                    // 格内 ctx 恒传 1：表 ctx 已落表根 ColorTransform 浸透全子树，格内再叠即
                    // 双淡；格自身声明由 CreateHtmlCell 内 Resolve 从 1 起步照吃。
                    // 字号 ctx 照传（emBase 无 ColorTransform 等价壳，必须显式透传）
                    Widget cell = CreateHtmlCell(
                        cellNode,
                        isHeader,
                        colorOverride,
                        depth,
                        blockAlign,
                        cellPaddingX,
                        1f,
                        fontScaleCtx
                    );
                    if (colSpan > 1) {
                        TablePanelWidget.SetColSpan(cell, colSpan);
                    }
                    if (rowSpan > 1) {
                        TablePanelWidget.SetRowSpan(cell, rowSpan);
                    }
                    // 格级 bgcolor（链最内级）：格 CSS 声明 → 格属性（同一元素 CSS 胜旧属性），
                    // 均无则不挂（行壳底色兜其余）
                    if (CssStyleParser.TryGetDeclaration(cellNode.GetAttributeValue("style", null), "background-color", out string cellCssBg)
                        && CssColorParser.TryParse(cellCssBg, out Color cellCssColor)) {
                        TablePanelWidget.SetCellBackground(cell, cellCssColor);
                    }
                    else if (CssColorParser.TryParse(cellNode.GetAttributeValue("bgcolor", null), out Color cellBg)) {
                        TablePanelWidget.SetCellBackground(cell, cellBg);
                    }
                    // M1：跳过被前行 rowspan 占住的槽位定真实列号（照 ExpandSlots cursor 口径）
                    while (occupied.Contains((r, column))) {
                        column++;
                    }
                    while (alignments.Count <= column) {
                        alignments.Add(null);
                    }
                    alignments[column] ??= ParseCellAlignment(cellNode, tr);
                    // valign：格属性 → 行属性（tr 与格同查；行级传自身节点即行属性直取）
                    ApplyCellAnchor(cell, ToTextAnchor(alignments[column]), ParseVerticalAlignment(cellNode, tr) ?? rowVerticalAlignment);
                    cells.Add(cell);
                    // rowspan 向后续行登记占格（照 ExpandSlots：行覆盖至 min(r+rowSpan, 行数) 前，
                    // 列覆盖 [column, column+advance)，advance=span 展开钳自然列数与底座同口径）
                    int advance = MathUtils.Min(MathUtils.Max(colSpan, 1), MathUtils.Max(naturalColumns - column, 1));
                    for (int rr = r + 1; rr < MathUtils.Min(r + rowSpan, rows.Count); rr++) {
                        for (int cc = column; cc < column + advance; cc++) {
                            occupied.Add((rr, cc));
                        }
                    }
                    column += advance;
                }
                if (cells.Count > 0) {
                    TableRowWidget row = new() { Header = isHeader, BackgroundColor = rowBackground };
                    foreach (Widget cell in cells) {
                        row.Children.Add(cell);
                    }
                    widget.Children.Add(row);
                }
            }
            if (footerRowCount > 0) {
                widget.FooterRows = footerRowCount;
            }
            CurrentIndentX = prevIndent;
            return widget;
        }

        // 列宽协商语义（照包装类 AddRow 口径）：自然宽+2×CellPadding
        const float CellPaddingX = 6f;

        // 单格构建（spec §7.1 cell 内容升级）：纯行内 → RichTextWidget（WordWrap + Stretch 拉满格矩形，
        // Size.X 锚定 自然宽+2×CellPadding——底座 track 即旧列宽，高度轮按 track−2×CellPadding 重测）；
        // 含块级子标签（p/div/section/article/table/h1-h6/hr/pre/blockquote/ul/ol），或有图片管线时
        // img 为唯一元素内容 → RichContentPanel 直装块：BuildHtmlNodes 整格重入（p/li 内
        // 块级同口径），img 升级图片块、嵌套 table 递归本方法。纯行内 cell 的 img 照旧 span 占位
        // （HtmlSpanParser 分支保留）。格内块 shell 宽由所在格 panel 按格净宽回传（本方法 push
        // CurrentPanel，见 RichContentPanel MeasureOverride）。
        // opacityCtx：格内继承透明度（表 ctx 已落表根 ColorTransform，调用点恒传 1——格内只
        // 吃格自身及更深层声明：块级格递归乘 cellNode 声明、纯行内格直设 widget ColorTransform；
        // 格自身声明走行内 span 路径已落 span.Opacity，widget 只补祖先 ctx，避免双乘）。
        // fontScaleCtx：格继承字号 ctx——块级格递归叠格自身 style font-size 声明
        //（ResolveFontScaleCtx，CSS 块级继承）；纯行内格走 span 解析不吃
        Widget CreateHtmlCell(HtmlAgilityPack.HtmlNode cellNode,
            bool isHeader,
            Color? colorOverride,
            int depth,
            TextAnchor blockAlign = TextAnchor.Default,
            float cellPaddingX = 6f,
            float opacityCtx = 1f,
            float fontScaleCtx = 1f) {
            bool hasBlockChild = false;
            bool imgOnly = ImageLoader != null;
            foreach (HtmlAgilityPack.HtmlNode child in cellNode.ChildNodes) {
                if (child.NodeType == HtmlAgilityPack.HtmlNodeType.Element) {
                    if (IsBlockTag(child.Name)) {
                        hasBlockChild = true;
                        break;
                    }
                    if (imgOnly && !child.Name.Equals("img", StringComparison.OrdinalIgnoreCase)) {
                        imgOnly = false;
                    }
                }
                else if (child.NodeType == HtmlAgilityPack.HtmlNodeType.Text
                    && imgOnly
                    && !string.IsNullOrWhiteSpace(child.InnerText)) {
                    imgOnly = false;
                }
            }
            if (hasBlockChild || imgOnly) {
                // 格容器升级 RichContentPanel：本格块级 shell（代码块/图片/引用外壳/列表行框）
                // 注册进本格 panel，由其 MeasureOverride 在底座高度轮按格净宽回填宽度——构建期
                // ContentWidth 是顶层近似值，对格内不成立（task-8-report 局限的机制化消解）
                RichContentPanel panel = new() { ImageLoader = ImageLoader };
                // 横向拉满格矩形：格内锚点/换行参考系=整格净宽，不随最宽子件塌缩
                panel.HorizontalAlignment = WidgetAlignment.Stretch;
                // 格自身 id：格 panel 即格视觉根，重入构建前领取（格内内容锚点随后各自挂起）
                PendAnchor(cellNode);
                ClaimAnchors(panel);
                // push/pop：构建本格内容期间 shell 注册路由进本格 panel；逐格 push/pop 使嵌套
                // 表格（内表格 CreateHtmlCell 再 push）与本格结束后恢复外层上下文正确嵌套
                RichContentPanel prevPanel = CurrentPanel;
                CurrentPanel = panel;
                (TextWordBreak cellBreak, bool cellNoWrap) = ResolveWordBreakCtx(cellNode, null, false);
                // 格自身 line-height 声明（word-break 同口径：格不从表级继承，Resolve 基=未声明）
                (float cellLhMul, float cellLhPx) = ResolveLineHeightCtx(cellNode, 0f, 0f);
                // 格自身 letter-spacing 声明（同口径基 0：格只吃自身声明，块级格递归传）
                float cellLs = ResolveLetterSpacingCtx(cellNode, 0f);
                // 块级格：格自身 opacity 叠进子树 ctx（格子节点不落 span 上下文）；
                // 格自身 style font-size 声明叠加进字号 ctx（CSS 块级继承）
                BuildHtmlNodes(
                    cellNode.ChildNodes,
                    panel,
                    0,
                    colorOverride,
                    depth + 1,
                    blockAlign,
                    cellBreak,
                    cellNoWrap,
                    cellLhMul,
                    cellLhPx,
                    opacityCtx * ResolveOpacityMultiplier(cellNode),
                    cellLs,
                    ResolveFontScaleCtx(cellNode, fontScaleCtx)
                );
                CurrentPanel = prevPanel;
                if (isHeader) {
                    BoldWidgetSpans(panel);
                }
                return panel;
            }
            // 纯行内：span 解析（img 命中 HtmlSpanParser 占位分支，照旧保留）
            PendAnchor(cellNode);
            List<TextSpan> spans = [];
            HtmlSpanParser.ParseHtmlNode(cellNode, spans, default, EffectiveTextColor(colorOverride), linkColor: m_bodyLinkColor);
            if (isHeader) {
                BoldCellSpans(spans);
            }
            // 格级 word-break/white-space 声明（CSS 胜旧 nowrap 属性）；white-space:nowrap →
            // WordWrap 关（纯行内格与块级格两路同口径）
            bool hasCellBreak = ParseWordBreakStyle(cellNode, out TextWordBreak? cellInlineBreak, out bool cellInlineNoWrap);
            // 格级 line-height 声明直设（无声明不动默认）
            bool hasCellLineHeight = ParseLineHeightStyle(cellNode, out float cellInlineLhMul, out float cellInlineLhPx);
            RichTextWidget widget = new() {
                Font = Font,
                FontScale = FontScale,
                Color = EffectiveTextColor(colorOverride),
                WordWrap = !cellInlineNoWrap,
                // td nowrap：老版防换行属性 → KeepAll（CJK/单词整体不破，浏览器 white-space:nowrap
                // 对纯文本格的近似口径）
                WordBreak = hasCellBreak && cellInlineBreak.HasValue ? cellInlineBreak.Value :
                    cellNode.GetAttributeValue("nowrap", null) != null ? TextWordBreak.KeepAll : TextWordBreak.BreakWord,
                // 拉满格矩形（track−2×CellPadding）：TextAnchor 居中/右对齐按 ActualSize 定偏移，
                // Near 会塌缩到内容宽
                HorizontalAlignment = WidgetAlignment.Stretch,
                VerticalAlignment = WidgetAlignment.Stretch
            };
            if (hasCellLineHeight) {
                // px 形式 mul 输出 0：置 1 保底（px>0 时 layout 侧本就优先，防 px 声明被链上改写后零行距）
                widget.LineHeightMultiplier = cellInlineLhMul > 0f ? cellInlineLhMul : 1f;
                widget.LineHeightPx = cellInlineLhPx;
            }
            // 纯行内格 letter-spacing 直设（基 0 同块级格口径：格只吃自身声明，不从表级继承）
            widget.LetterSpacing = ResolveLetterSpacingCtx(cellNode, 0f);
            // 纯行内格 opacity：widget 只补祖先 ctx——格自身声明已经行内 span 路径
            //（ParseHtmlNode→ApplyOpenTag→ApplyInlineStyle）落到 span.Opacity，此处再乘会双淡出
            widget.ColorTransform = Color.White * opacityCtx;
            widget.Spans.AddRange(spans);
            if (blockAlign != TextAnchor.Default) {
                // 对齐容器内的表格格文本（Chrome text-align 继承进 td；列对齐缺省时不覆写，见 ApplyCellAnchor）
                widget.TextAnchor = blockAlign;
            }
            if (LinkClicked != null) {
                widget.LinkClicked += href => LinkClicked(href);
            }
            // 按 float.MaxValue 自测拿自然宽（与旧 scratch Layout(float.MaxValue,∞) 同参对齐），
            // Size.X 锚定让底座 track=自然宽+2×CellPadding（cellpadding 属性时同参）；Size.Y=-1
            // 保持折行高自动
            widget.Measure(new Vector2(float.MaxValue, float.PositiveInfinity));
            widget.Size = new Vector2(widget.ParentDesiredSize.X + 2f * cellPaddingX, -1f);
            ClaimAnchors(widget);
            return widget;
        }

        // td 直下块级子标签判定（li 内块级同名单）：命中即整格走块级直装。footer/address 独立成段
        //（footer 用户裁定；address HTML 块级语义），switch 有显式 case；center/dl/dt/dd/figure/
        // figcaption/details/summary 同批块级化（article 本就在名单）
        static bool IsBlockTag(string tag) => tag.ToLowerInvariant() is "p"
            or "div"
            or "section"
            or "article"
            or "table"
            or "h1"
            or "h2"
            or "h3"
            or "h4"
            or "h5"
            or "h6"
            or "hr"
            or "pre"
            or "blockquote"
            or "ul"
            or "ol"
            or "menu"
            or "footer"
            or "address"
            or "center"
            or "dl"
            or "dt"
            or "dd"
            or "figure"
            or "figcaption"
            or "details"
            or "summary"
            or "caption"
            or "html"
            or "body";

        // colspan/rowspan 属性解析：空/非法/≤0 → 1，上限钳 1000（HTML 标准同值）——不可信源
        // `<td colspan="2000000000">` 不钳会经 alignments 扩容 + 底座 new float[columns] 双重内存爆炸；
        // SetColSpan/SetRowSpan 内部还有 max(·,1) 钳制兜底，越界 span 由底座 ExpandSlots 钳到实际列数
        static int ParseSpanAttribute(HtmlAgilityPack.HtmlNode cell, string name) =>
            int.TryParse(cell.GetAttributeValue(name, null), out int value) && value > 0 ? Math.Min(value, 1000) : 1;

        // col/colgroup 宽偏好解析：style width 声明胜旧 width 属性（同 bgcolor/text-align 规），
        // TryParseWidthValue 解析 px/%/em/rem/pt（负/auto 拒）。返回 (px, %) 之一，无效 (0,0)
        static (float Px, float Pct) ParseColWidthPreference(HtmlAgilityPack.HtmlNode node) {
            string width = null;
            if (CssStyleParser.TryGetDeclaration(node.GetAttributeValue("style", null), "width", out string cssWidth)) {
                width = cssWidth;
            }
            else {
                width = node.GetAttributeValue("width", null)?.Trim();
            }
            if (string.IsNullOrEmpty(width)
                || !CssStyleParser.TryParseWidthValue(width, out bool isPercent, out float number)) {
                return (0f, 0f);
            }
            return isPercent ? (0f, number) : (number, 0f);
        }

        // 行直下是否含 th 格（行级表头判定辅助，F3；thead 判定由调用侧 fromThead 承担）
        static bool HasHeaderCell(HtmlAgilityPack.HtmlNode row) {
            foreach (HtmlAgilityPack.HtmlNode child in row.ChildNodes) {
                if (child.NodeType == HtmlAgilityPack.HtmlNodeType.Element
                    && child.Name.ToLowerInvariant() == "th") {
                    return true;
                }
            }
            return false;
        }

        // 行直下是否含 td/th 格（M1 空行剔除辅助：空行不产行、底座无对应布局行）
        static bool HasCellElement(HtmlAgilityPack.HtmlNode row) {
            foreach (HtmlAgilityPack.HtmlNode child in row.ChildNodes) {
                if (child.NodeType == HtmlAgilityPack.HtmlNodeType.Element
                    && child.Name.ToLowerInvariant() is "td" or "th") {
                    return true;
                }
            }
            return false;
        }

        // 全局 hidden 属性（任意值含空串都隐藏）：表路径三处同源（行收集/格构建/自然列数与列
        // 偏好映射），与 BuildHtmlNodes 块级拦截同款判定
        static bool IsHiddenNode(HtmlAgilityPack.HtmlNode node) => node.GetAttributeValue("hidden", null) != null;

        // 块格表头染粗：递归格内全部 RichTextWidget（嵌套表格一并——CSS th 粗体继承口径）
        static void BoldWidgetSpans(Widget widget) {
            if (widget is RichTextWidget rich) {
                BoldCellSpans(rich.Spans);
            }
            else if (widget is ContainerWidget container) {
                foreach (Widget child in container.Children) {
                    BoldWidgetSpans(child);
                }
            }
        }

        // 格锚点下写：RichTextWidget 直写 TextAnchor 并横向拉满容器（留锚点偏移余量）；容器格递归
        // （CSS text-align 继承口径，嵌套表格一并）。列对齐只补缺省锚点：容器内已显式居中的段/标题
        //（blockAlign）优先于列对齐属性——<td><center> 老帖高频组合，无条件赋值会击穿居中；
        // 浏览器同款取舍（center 元素自身 text-align 胜过外层 td 属性），left 属性映射 Default 不写。
        // valign 走垂直位 OR 合成（HorizontalCenter/Right 与 Top/Bottom 位互不冲突，flags 枚举），
        // 并确保纵向 Stretch 留锚点偏移余量；块级格（RichContentPanel）内容纵排无法整体居中，
        // 递归只达内部文本件（单行文本格即全效，多行块近似），留档局限
        static void ApplyCellAnchor(Widget widget, TextAnchor anchor, TextAnchor? verticalAnchor = null) {
            if (widget is RichTextWidget rich) {
                if (anchor != TextAnchor.Default
                    && rich.TextAnchor == TextAnchor.Default) {
                    rich.TextAnchor = anchor;
                }
                if (verticalAnchor != null) {
                    rich.TextAnchor |= verticalAnchor.Value;
                    rich.VerticalAlignment = WidgetAlignment.Stretch;
                }
                rich.HorizontalAlignment = WidgetAlignment.Stretch;
            }
            else if (widget is ContainerWidget container) {
                foreach (Widget child in container.Children) {
                    ApplyCellAnchor(child, anchor, verticalAnchor);
                }
            }
        }

        // valign 属性 → 垂直锚点位：top/middle/bottom/baseline（baseline 近似 top——等字号行基线天然对齐，
        // 矢量字统一纵向度量后跨字体源亦成立，破绽仅在同行首行字号混排；真基线对齐需格内首行基线行走
        // + 行基线 max 平移，用户裁定不做留档）。TextAnchor.Top==0==Default，故返回 nullable——null=无
        // 属性（不写），Top=显式置顶（OR 0 无副作用但保 Stretch 赋值）。缺省查所在行（tr valign
        // 行级回退），皆无返回 null
        static TextAnchor? ParseVerticalAlignment(HtmlAgilityPack.HtmlNode cell, HtmlAgilityPack.HtmlNode row) {
            string token = cell.GetAttributeValue("valign", null)?.Trim().ToLowerInvariant()
                ?? row.GetAttributeValue("valign", null)?.Trim().ToLowerInvariant();
            return token switch {
                "top" or "baseline" => TextAnchor.Top,
                "middle" => TextAnchor.VerticalCenter,
                "bottom" => TextAnchor.Bottom,
                _ => null
            };
        }

        // 块级 align 属性 / style text-align → 文本锚点：style 声明胜旧属性（CSS 级联口径）。
        // center/right/left 三值生效，justify/char 不识别返回 null（继承外层）。left 显式映射
        // Default=Left 同值，nullable 装箱保住"显式 left 覆盖继承的 right"语义（否则 ?? 链会
        // 回退到继承值）
        static TextAnchor? ParseBlockAlignment(HtmlAgilityPack.HtmlNode node) {
            if (CssStyleParser.TryGetDeclaration(node.GetAttributeValue("style", null), "text-align", out string textAlign)) {
                return textAlign.ToLowerInvariant() switch {
                    "center" => TextAnchor.HorizontalCenter,
                    "right" => TextAnchor.Right,
                    "left" => TextAnchor.Default,
                    // justify（引擎无逐行对齐）等：该声明不生效，退回 align 属性口径
                    _ => null
                };
            }
            return node.GetAttributeValue("align", null)?.Trim().ToLowerInvariant() switch {
                "center" => TextAnchor.HorizontalCenter,
                "right" => TextAnchor.Right,
                "left" => TextAnchor.Default,
                _ => null
            };
        }

        // 块级 style word-break / white-space:nowrap → 段落换行口径（CSS word-break 继承属性，
        // 随容器重入链透传；white-space 只认 nowrap=WordWrap 关，pre/pre-wrap 需空白保留机制
        // 留档不做）。两者都未声明返回 false
        static bool ParseWordBreakStyle(HtmlAgilityPack.HtmlNode node, out TextWordBreak? wordBreak, out bool noWrap) {
            wordBreak = null;
            noWrap = false;
            string style = node.GetAttributeValue("style", null);
            bool has = false;
            if (CssStyleParser.TryGetDeclaration(style, "word-break", out string wb)) {
                // 只认有效值（CSS：非法声明整条丢弃→继承外层）；break-word（deprecated 别名）
                // =默认行为映射 BreakWord
                switch (wb.Trim().ToLowerInvariant()) {
                    case "normal":
                        wordBreak = TextWordBreak.Normal;
                        has = true;
                        break;
                    case "break-all":
                        wordBreak = TextWordBreak.BreakAll;
                        has = true;
                        break;
                    case "keep-all":
                        wordBreak = TextWordBreak.KeepAll;
                        has = true;
                        break;
                    case "break-word":
                        wordBreak = TextWordBreak.BreakWord;
                        has = true;
                        break;
                }
            }
            if (CssStyleParser.TryGetDeclaration(style, "white-space", out string ws)) {
                noWrap = ws.Trim().ToLowerInvariant() == "nowrap";
                has |= noWrap;
            }
            return has;
        }

        // 节点级 word-break/nowrap 解析并入继承上下文：节点声明覆盖（CSS 继承属性口径），
        // 无声明沿用继承值。white-space 显式 normal 撤销继承 nowrap 不支持（bool ctx 无三态，
        // 留档：nowrap 只会向下传导）
        static (TextWordBreak WordBreak, bool NoWrap) ResolveWordBreakCtx(HtmlAgilityPack.HtmlNode node,
            TextWordBreak? inherited,
            bool inheritedNoWrap) {
            bool has = ParseWordBreakStyle(node, out TextWordBreak? wordBreak, out bool noWrap);
            return (has && wordBreak.HasValue ? wordBreak.Value : inherited ?? TextWordBreak.BreakWord, noWrap || inheritedNoWrap);
        }

        // style line-height 解析：number/%（÷100）/em（字号倍率同义）→ 倍率；px 直取、pt×4/3、
        // rem×16（根字号口径）→ 绝对 px（px 形式 mul 输出 0，消费侧 px 优先）。normal/unset/
        // initial/revert 与无效值→false 不动（整条忽略沿用继承；留档与 CSS 有差——CSS normal/
        // initial 声明会重置为初始值，此处按未声明继承，取舍=省三态上下文）。负值/0/解析失败=
        // 无效（CSS 2.1 line-height 负值非法；0 行盒合法但引擎无消费场景，按未声明）。
        // 返回=有可用声明
        static bool ParseLineHeightStyle(HtmlAgilityPack.HtmlNode node, out float mul, out float px) {
            mul = 0f;
            px = 0f;
            if (!CssStyleParser.TryGetDeclaration(node.GetAttributeValue("style", null), "line-height", out string decl)) {
                return false;
            }
            string v = decl.Trim();
            if (v.Equals("normal", StringComparison.OrdinalIgnoreCase)
                || v.Equals("unset", StringComparison.OrdinalIgnoreCase)) {
                return false;
            }

            // 值体解析（单位大小写不敏感）；rem 判定须先于 em（"1.5rem" 以 "em" 结尾）
            bool TryMagnitude(string text, out float value) => float.TryParse(
                    text.Trim(),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out value
                )
                && value > 0f;

            if (v.EndsWith("%", StringComparison.Ordinal)) {
                if (TryMagnitude(v[..^1], out float percent)) {
                    mul = percent / 100f;
                    return true;
                }
                return false;
            }
            if (v.EndsWith("rem", StringComparison.OrdinalIgnoreCase)) {
                if (TryMagnitude(v[..^3], out float rems)) {
                    px = rems * 16f;
                    return true;
                }
                return false;
            }
            if (v.EndsWith("em", StringComparison.OrdinalIgnoreCase)) {
                if (TryMagnitude(v[..^2], out float em)) {
                    mul = em;
                    return true;
                }
                return false;
            }
            if (v.EndsWith("px", StringComparison.OrdinalIgnoreCase)) {
                if (TryMagnitude(v[..^2], out float length)) {
                    px = length;
                    return true;
                }
                return false;
            }
            if (v.EndsWith("pt", StringComparison.OrdinalIgnoreCase)) {
                if (TryMagnitude(v[..^2], out float points)) {
                    px = points * 4f / 3f;
                    return true;
                }
                return false;
            }
            // 无单位倍率（CSS number）
            if (TryMagnitude(v, out float number)) {
                mul = number;
                return true;
            }
            return false;
        }

        // 节点级 line-height 解析并入继承上下文：节点有声明整体替换（不做倍率/绝对值混算），
        // 无声明沿用继承值（CSS 继承属性口径）。随块级容器递归链透传
        static (float Mul, float Px) ResolveLineHeightCtx(HtmlAgilityPack.HtmlNode node, float inheritedMul, float inheritedPx) =>
            ParseLineHeightStyle(node, out float mul, out float px) ? (mul, px) : (inheritedMul, inheritedPx);

        // style letter-spacing 解析：px 直取、pt×4/3、rem×16（根字号口径）→ 绝对 px；em×16
        // 为根字号近似（严格 CSS 应随元素字号，引擎块级链无字号 ctx，留档取舍）。normal/unset
        // 与无效值→false 整条忽略（沿用继承）。负值合法不钳（CSS 允许负字距收紧）。返回=有可用声明
        static bool ParseLetterSpacingStyle(HtmlAgilityPack.HtmlNode node, out float px) {
            px = 0f;
            if (!CssStyleParser.TryGetDeclaration(node.GetAttributeValue("style", null), "letter-spacing", out string decl)) {
                return false;
            }
            string v = decl.Trim();
            if (v.Equals("normal", StringComparison.OrdinalIgnoreCase)
                || v.Equals("unset", StringComparison.OrdinalIgnoreCase)) {
                return false;
            }

            // 值体解析（单位大小写不敏感）；rem 判定须先于 em（"1.5rem" 以 "em" 结尾）。
            // 负值/0 均合法（与 line-height 的 >0 门槛不同），只拒解析失败
            bool TryLength(string text, out float value) => float.TryParse(
                text.Trim(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out value
            );

            if (v.EndsWith("rem", StringComparison.OrdinalIgnoreCase)) {
                if (TryLength(v[..^3], out float rems)) {
                    px = rems * 16f;
                    return true;
                }
                return false;
            }
            if (v.EndsWith("em", StringComparison.OrdinalIgnoreCase)) {
                if (TryLength(v[..^2], out float ems)) {
                    px = ems * 16f;
                    return true;
                }
                return false;
            }
            if (v.EndsWith("px", StringComparison.OrdinalIgnoreCase)) {
                if (TryLength(v[..^2], out float pixels)) {
                    px = pixels;
                    return true;
                }
                return false;
            }
            if (v.EndsWith("pt", StringComparison.OrdinalIgnoreCase)) {
                if (TryLength(v[..^2], out float points)) {
                    px = points * 4f / 3f;
                    return true;
                }
                return false;
            }
            // 无单位数字：CSS letter-spacing 除 0 外须带长度单位，按无效整条忽略
            return false;
        }

        // 节点级 letter-spacing 解析并入继承上下文：节点有声明整体替换，无声明沿用继承值。
        // 替换语义（与 opacity 叠乘不同——字距是长度值，嵌套声明不累加，浏览器同口径）。
        // 随块级容器递归链透传（与 word-break/line-height 同链同挂载点）
        static float ResolveLetterSpacingCtx(HtmlAgilityPack.HtmlNode node, float inherited) =>
            ParseLetterSpacingStyle(node, out float px) ? px : inherited;

        // style text-indent 解析（块级首行缩进，非继承属性——只吃声明节点自身行内内容首段）：
        // px 直取、pt×4/3、rem/em×16（根字号口径）→ 绝对 px；% → percent=pct 数值（50=50%，
        // 消费端相对本件排版可用宽折算）；无单位数字按 px（老 HTML 长度宽松口径）。normal/unset
        // 与负值（hanging 悬挂缩进）不支持 → false 整条忽略。返回=有可用声明
        static bool TryParseTextIndent(HtmlAgilityPack.HtmlNode node, out float pxIndent, out float? percentIndent) {
            pxIndent = 0f;
            percentIndent = null;
            if (!CssStyleParser.TryGetDeclaration(node.GetAttributeValue("style", null), "text-indent", out string decl)) {
                return false;
            }
            string v = decl.Trim();
            if (v.Equals("normal", StringComparison.OrdinalIgnoreCase)
                || v.Equals("unset", StringComparison.OrdinalIgnoreCase)) {
                return false;
            }

            // 值体解析（单位大小写不敏感）；rem 判定须先于 em（"1.5rem" 以 "em" 结尾）。
            // IsFinite 拒 ∞（1e40px 之类）：∞ resolved 会把首行预算钳 1px 且 Draw 偏移成 ∞
            bool TryMagnitude(string text, out float value) => float.TryParse(
                    text.Trim(),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out value
                )
                && float.IsFinite(value);

            if (v.EndsWith("%", StringComparison.Ordinal)) {
                if (TryMagnitude(v[..^1], out float percent)
                    && percent > 0f) {
                    percentIndent = percent;
                    return true;
                }
                return false;
            }
            if (v.EndsWith("rem", StringComparison.OrdinalIgnoreCase)) {
                if (TryMagnitude(v[..^3], out float rems)
                    && rems > 0f) {
                    pxIndent = rems * 16f;
                    return true;
                }
                return false;
            }
            if (v.EndsWith("em", StringComparison.OrdinalIgnoreCase)) {
                if (TryMagnitude(v[..^2], out float ems)
                    && ems > 0f) {
                    pxIndent = ems * 16f;
                    return true;
                }
                return false;
            }
            if (v.EndsWith("px", StringComparison.OrdinalIgnoreCase)) {
                if (TryMagnitude(v[..^2], out float pixels)
                    && pixels > 0f) {
                    pxIndent = pixels;
                    return true;
                }
                return false;
            }
            if (v.EndsWith("pt", StringComparison.OrdinalIgnoreCase)) {
                if (TryMagnitude(v[..^2], out float points)
                    && points > 0f) {
                    pxIndent = points * 4f / 3f;
                    return true;
                }
                return false;
            }
            // 无单位数字：按 px（设计口径）
            if (TryMagnitude(v, out float number)
                && number > 0f) {
                pxIndent = number;
                return true;
            }
            return false;
        }

        // 节点 style opacity 声明解析：值域同行内口径（HtmlSpanParser.TryParseCssOpacity——
        // number/%、负值/>1 钳 [0,1]、垃圾值整条丢弃）。返回=有有效声明
        static bool ParseOpacityStyle(HtmlAgilityPack.HtmlNode node, out float opacity) {
            opacity = 1f;
            return CssStyleParser.TryGetDeclaration(node.GetAttributeValue("style", null), "opacity", out string decl)
                && HtmlSpanParser.TryParseCssOpacity(decl, out opacity);
        }

        // 节点 opacity 叠乘系数：有有效声明=声明值，否则 1。Resolve 语义=乘进 ctx（opacity 可
        // 叠乘嵌套，与 word-break/line-height 的替换语义不同——CSS opacity 是可组合数值，嵌套
        // 元素各施各的 0..1 相乘得合成透明度，浏览器同款）。与壳装饰色乘同源解析：壳只承担
        // 装饰淡出、ctx 只承担子树 ColorTransform 淡出，同一声明两路消费不双乘
        static float ResolveOpacityMultiplier(HtmlAgilityPack.HtmlNode node) => ParseOpacityStyle(node, out float v) ? v : 1f;

        // 子树字号 ctx 传导（tier-1.5 style font-size 块级继承，CSS font-size 继承口径）：
        // 声明绝对单位（px/pt/rem）=ctx 整体换底 ratio（16px 根基准，绝对值覆盖继承链）；
        // 相对单位（em/%）=ctx×ratio（父字号基准）；无声明/解析失败沿用继承值。
        // 调用点只用于子树重入 ctx——元素自身盒壳的声明解析归 BoxLengthContext 内部
        //（同参不同消费面，两处不双乘）
        static float ResolveFontScaleCtx(HtmlAgilityPack.HtmlNode node, float inheritedCtx) {
            if (!CssStyleParser.TryGetDeclaration(node.GetAttributeValue("style", null), "font-size", out string decl)
                || !HtmlSpanParser.TryParseCssFontSizeUnit(decl, out float ratio, out bool absolute)) {
                return inheritedCtx;
            }
            return absolute ? ratio : inheritedCtx * ratio;
        }

        // UA 标签字号倍率（tier-1.5 块级可达处 ctx 乘点，default ContainsBlockChild 重入口/
        // img 段形态用）：small 缩小、big 放大（×1.2，与行内 ApplyOpenTag 同值）、sub/sup 同
        // CSS smaller 惯例 0.833、font size 属性倍率表（TryParseFontSizeAttribute 同行内口径）；
        // 其余标签 1f。仅包块可达流消费——纯行内形态的字号走 span 解析自有 Scale 链
        static float ResolveUaTagFontScale(HtmlAgilityPack.HtmlNode node) {
            switch (node.Name.ToLowerInvariant()) {
                case "small": return 0.8f;
                case "big": return 1.2f;
                case "sub":
                case "sup": return 0.833f;
                case "font": return HtmlSpanParser.TryParseFontSizeAttribute(node.GetAttributeValue("size", null), out float ratio) ? ratio : 1f;
                default: return 1f;
            }
        }

        // pre/code style 直通解析（代码块便宜档）：color/font-size/border-radius 经 CreateCodeBlock
        // 可选参直通 Label 行/frame；background-color 直通 frame.CenterColor（backgroundColor 新
        // 可选参，null=调色板 CodeCenterColor 零改，Markdown 路径零改）。内层 code 元素（pre 直下
        // 第一个 code）自带 style 同样读取：内层更具体，逐属性 pre 先读、code 声明覆盖（code 声明
        // 胜 pre 的级联口径）；无 code 子元素只读 pre。border-radius 出四角最大 px 值直通引擎真圆
        // 弧（% 忽略、斜杠取前半在解析层——引擎 BevelledShapeRenderer 石皮斜切观感 vs CSS 扁平圆
        // 角有差，便宜档近似留档）。其余行内属性（font-weight 等）等宽管线不进 span 解析，维持
        // 不吃（留档）。内容提取逻辑不受影响——只读 code 节点属性，不碰子树
        void ParseCodeTextStyle(HtmlAgilityPack.HtmlNode preNode,
            out Color? textColor,
            out float? fontScaleOverride,
            out float? roundingRadius,
            out Color? backgroundColor) {
            textColor = null;
            fontScaleOverride = null;
            roundingRadius = null;
            backgroundColor = null;
            HtmlAgilityPack.HtmlNode codeNode = null;
            foreach (HtmlAgilityPack.HtmlNode child in preNode.ChildNodes) {
                if (child.NodeType == HtmlAgilityPack.HtmlNodeType.Element
                    && child.Name.Equals("code", StringComparison.OrdinalIgnoreCase)) {
                    codeNode = child;
                    break;
                }
            }
            string preStyle = preNode.GetAttributeValue("style", null);
            string codeStyleAttr = codeNode?.GetAttributeValue("style", null);
            if (string.IsNullOrEmpty(preStyle)
                && string.IsNullOrEmpty(codeStyleAttr)) {
                return;
            }
            if (CssStyleParser.TryGetDeclaration(preStyle, "color", out string colorDecl)
                && CssColorParser.TryParse(colorDecl, out Color color)) {
                textColor = color;
            }
            if (CssStyleParser.TryGetDeclaration(codeStyleAttr, "color", out string codeColorDecl)
                && CssColorParser.TryParse(codeColorDecl, out Color codeColor)) {
                textColor = codeColor;
            }
            if (CssStyleParser.TryGetDeclaration(preStyle, "font-size", out string sizeDecl)
                && HtmlSpanParser.TryParseCssFontSize(sizeDecl, out float ratio)) {
                fontScaleOverride = ratio;
            }
            if (CssStyleParser.TryGetDeclaration(codeStyleAttr, "font-size", out string codeSizeDecl)
                && HtmlSpanParser.TryParseCssFontSize(codeSizeDecl, out float codeRatio)) {
                fontScaleOverride = codeRatio;
            }
            // border-radius：code 声明胜 pre（code 未声明回退 pre）
            if (CssStyleParser.TryParseBorderRadius(codeStyleAttr, out Vector4 cornerPx, out _)
                || CssStyleParser.TryParseBorderRadius(preStyle, out cornerPx, out _)) {
                roundingRadius = MathUtils.Max(MathUtils.Max(cornerPx.X, cornerPx.Y), MathUtils.Max(cornerPx.Z, cornerPx.W));
            }
            if (CssStyleParser.TryGetDeclaration(preStyle, "background-color", out string bgDecl)
                && CssColorParser.TryParse(bgDecl, out Color bgColor)) {
                backgroundColor = bgColor;
            }
            if (CssStyleParser.TryGetDeclaration(codeStyleAttr, "background-color", out string codeBgDecl)
                && CssColorParser.TryParse(codeBgDecl, out Color codeBg)) {
                backgroundColor = codeBg;
            }
        }

        // 段落级覆盖五元组打包（Flush/ApplyParagraphBreakStyle 消费）：word-break/white-space 沿用
        // ResolveWordBreakCtx，line-height 并入 ResolveLineHeightCtx，letter-spacing 并入
        // ResolveLetterSpacingCtx——p/dt/summary/footer-inline 与标题直建路径共用，消除手写组合
        static (TextWordBreak WordBreak, bool NoWrap, float LineHeightMul, float LineHeightPx, float LetterSpacing) ResolveParaOverrideCtx(
            HtmlAgilityPack.HtmlNode node,
            TextWordBreak? inheritedBreak,
            bool inheritedNoWrap,
            float inheritedLhMul,
            float inheritedLhPx,
            float inheritedLs) {
            (TextWordBreak wordBreak, bool noWrap) = ResolveWordBreakCtx(node, inheritedBreak, inheritedNoWrap);
            (float lhMul, float lhPx) = ResolveLineHeightCtx(node, inheritedLhMul, inheritedLhPx);
            float ls = ResolveLetterSpacingCtx(node, inheritedLs);
            return (wordBreak, noWrap, lhMul, lhPx, ls);
        }

        // 直建段落件（标题路径不经 Flush）铺 word-break/nowrap/line-height/letter-spacing
        //（标题同吃容器继承与自身声明）
        static void ApplyParagraphBreakStyle(RichTextWidget widget,
            (TextWordBreak WordBreak, bool NoWrap, float LineHeightMul, float LineHeightPx, float LetterSpacing) ctx) {
            widget.WordBreak = ctx.WordBreak;
            if (ctx.NoWrap) {
                widget.WordWrap = false;
            }
            // line-height 口径同 Flush：倍率缺省 1，绝对 px>0 优先（layout 侧同规）
            widget.LineHeightMultiplier = ctx.LineHeightMul > 0f ? ctx.LineHeightMul : 1f;
            if (ctx.LineHeightPx > 0f) {
                widget.LineHeightPx = ctx.LineHeightPx;
            }
            // letter-spacing：声明覆盖继承（替换语义），无声明=继承 ctx（默认 0）
            widget.LetterSpacing = ctx.LetterSpacing;
        }

        // hr 旧属性四件套：width（px / % 相对容器，容器宽未知退全宽）/size（厚度 px）/color/
        // align（仅定宽时有偏移空间；全宽时 Stretch 已占满无意义）。noshade 忽略（本就纯色平线）
        RectangleWidget CreateHtmlThematicBreak(HtmlAgilityPack.HtmlNode node) {
            RectangleWidget line = CreateThematicBreak();
            if (CssColorParser.TryParse(node.GetAttributeValue("color", null), out Color hrColor)) {
                line.FillColor = hrColor;
            }
            float width = -1f;
            string widthAttr = node.GetAttributeValue("width", null)?.Trim();
            if (!string.IsNullOrEmpty(widthAttr)) {
                if (widthAttr.EndsWith("%", StringComparison.Ordinal)
                    && float.TryParse(
                        widthAttr[..^1],
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out float percent
                    )
                    && NestedContentWidth() > 0f) {
                    width = NestedContentWidth() * MathUtils.Clamp(percent / 100f, 0f, 1f);
                }
                else if (float.TryParse(
                        widthAttr,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out float pixels
                    )) {
                    width = MathUtils.Max(pixels, 0f);
                }
            }
            float thickness = -1f;
            if (int.TryParse(node.GetAttributeValue("size", null), out int sizeAttribute)
                && sizeAttribute > 0) {
                thickness = MathUtils.Min(sizeAttribute, 100f);
            }
            if (width >= 0f
                || thickness > 0f) {
                // 显式尺寸：脱离 Stretch 定宽定厚（Rectangle 无内容 -1 缩线惯例，厚度必显式给定）
                line.HorizontalAlignment = ParseBlockAlignment(node) switch {
                    TextAnchor.HorizontalCenter => WidgetAlignment.Center,
                    TextAnchor.Right => WidgetAlignment.Far,
                    _ => WidgetAlignment.Near
                };
                line.Size = new Vector2(width >= 0f ? width : -1f, thickness > 0f ? thickness : 1f);
            }
            return line;
        }

        // <img> 属性口径统一入口（块级/img 直下级/li 行内级三调用点）：width/height 显式尺寸 +
        // hspace/vspace 外边距（各侧独立计，横向相加得双边距）。其余 align 属性留档不做
        //（left/right 浮动环绕无对应排版模型）
        // 返回待挂树 widget（调用方统一 Children.Add）：无 style 盒属性=frame 本体，
        // 有=透明壳（frame 包入壳，壳 Margin=CSS margin、frame 四边距=CSS padding；
        // 图片块升级为块级，块级元素 style 盒属性同权适用——审查 P2 补接）。
        // opacityCtx（祖先链叠乘系数）分层落点：img 自身声明恒落内层 frame（壳装饰
        // Decoration 与之同源解析不双乘）；祖先 ctx 落壳 ColorTransform 浸透子树（装饰
        // 与内容各恰一次），无壳时并入 frame 一并承担——审查 P0：CT 设在包装返回值上
        // 会与壳装饰 Decoration 叠成装饰 v²。
        // fontScaleCtx：img 自身盒壳（WrapInStyleBox）em/% 基准的继承字号 ctx（tier-1.5，
        // 调用点按所在流传——p/img 升级块=声明元素子树 ctx、li 行内=liFontCtx 等）
        Widget AddImageBlockWithAttributes(StackPanelWidget target,
            HtmlAgilityPack.HtmlNode img,
            Color? colorOverride,
            float opacityCtx = 1f,
            float fontScaleCtx = 1f) {
            Vector2? explicitSize = ParseImageSize(img);
            // CSS max-width：px 直取；% 相对容器净宽（与 width 属性 % 同口径，钳 1-4096，容器宽
            // 未知跳过）。消费分两路：显式 width 属性直接 min 钳（构建期可算）；仅 height（X=-1
            // 缺维按纹理宽高比反推）与自适应（无属性）路径构建期不知宽，maxWidth 下沉加载/重适配
            // 适配出口统一钳（ClampDisplayWidth）。自适应占位基准宽不做钳制——宿主测量回填每帧
            // 按根容器净宽覆写未完成 frame 宽，构建期钳值必然被覆盖（回填子系统本轮不动，留档）
            float cssMaxWidth = -1f;
            if (CssStyleParser.TryGetDeclaration(img.GetAttributeValue("style", null), "max-width", out string maxWidthDecl)
                && CssStyleParser.TryParseWidthValue(maxWidthDecl, out bool maxIsPercent, out float maxNumber)) {
                cssMaxWidth = maxIsPercent
                    ? NestedContentWidth() > 0f ? MathUtils.Clamp(NestedContentWidth() * maxNumber / 100f, 1f, 4096f) : -1f
                    : MathUtils.Clamp(maxNumber, 1f, 4096f);
            }
            if (explicitSize.HasValue
                && explicitSize.Value.X > 0f
                && cssMaxWidth > 0f) {
                explicitSize = new Vector2(MathUtils.Min(explicitSize.Value.X, cssMaxWidth), explicitSize.Value.Y);
            }
            Vector2 margin = new(0f, 8f);
            if (float.TryParse(img.GetAttributeValue("hspace", null), out float hspace)
                && hspace > 0f) {
                margin.X = MathUtils.Min(hspace * 2f, 100f);
            }
            if (float.TryParse(img.GetAttributeValue("vspace", null), out float vspace)
                && vspace > 0f) {
                margin.Y = MathUtils.Min(vspace * 2f, 100f);
            }
            CanvasWidget frame = AddImageBlock(
                target,
                img.GetAttributeValue("src", null),
                img.GetAttributeValue("alt", null),
                explicitSize,
                margin,
                cssMaxWidth
            );
            // AddImageBlock 已挂树；摘回统一由调用方挂（摘下重挂同位追加，顺序不变）
            target.Children.Remove(frame);
            frame.ColorTransform = Color.White * ResolveOpacityMultiplier(img);
            Widget shell = WrapInStyleBox(img, frame, colorOverride, fontScaleCtx: fontScaleCtx);
            if (shell != null) {
                shell.ColorTransform = Color.White * opacityCtx;
                return shell;
            }
            frame.ColorTransform = Color.White * (opacityCtx * ResolveOpacityMultiplier(img));
            return frame;
        }

        // img width/height：px 直取；width 的 % 相对容器（容器宽未知跳过该维）；height 的 %
        // 不做（无解析基准）。两维均未给返回 null（走自适应管线）。显式值钳 1-4096 防御
        Vector2? ParseImageSize(HtmlAgilityPack.HtmlNode img) {
            float width = ParseImageDimension(img.GetAttributeValue("width", null), true);
            float height = ParseImageDimension(img.GetAttributeValue("height", null), false);
            if (width <= 0f
                && height <= 0f) {
                return null;
            }
            return new Vector2(width, height);
        }

        float ParseImageDimension(string attribute, bool allowPercent) {
            if (string.IsNullOrWhiteSpace(attribute)) {
                return -1f;
            }
            string trimmed = attribute.Trim();
            if (trimmed.EndsWith("%", StringComparison.Ordinal)) {
                if (!allowPercent
                    || !float.TryParse(
                        trimmed[..^1],
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out float percent
                    )
                    || NestedContentWidth() <= 0f) {
                    return -1f;
                }
                return MathUtils.Clamp(NestedContentWidth() * percent / 100f, 1f, 4096f);
            }
            return float.TryParse(
                trimmed,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out float pixels
            )
                ? MathUtils.Clamp(pixels, 1f, 4096f)
                : -1f;
        }

        // MarkdownTableAlignment → TextAnchor（照包装类 AssignAnchors 映射；缺省=左对齐 Default）
        static TextAnchor ToTextAnchor(MarkdownTableAlignment? alignment) {
            return alignment switch {
                MarkdownTableAlignment.Center => TextAnchor.HorizontalCenter,
                MarkdownTableAlignment.Right => TextAnchor.Right,
                _ => TextAnchor.Default
            };
        }

        // 对齐取值顺序：cell 自身 align → 所在行 align → cell style text-align → 行 style text-align；
        // 皆缺省返回 null（MarkdownTableWidget 左对齐）。justify/inherit 等不识别即左对齐
        static MarkdownTableAlignment? ParseCellAlignment(HtmlAgilityPack.HtmlNode cell, HtmlAgilityPack.HtmlNode row) {
            MarkdownTableAlignment? alignment = ParseAlignmentToken(cell.GetAttributeValue("align", null))
                ?? ParseAlignmentToken(row.GetAttributeValue("align", null));
            if (alignment != null) {
                return alignment;
            }
            System.Text.RegularExpressions.Match cellMatch = s_textAlignRegex.Match(cell.GetAttributeValue("style", null) ?? string.Empty);
            if (cellMatch.Success) {
                return ParseAlignmentToken(cellMatch.Groups[1].Value);
            }
            System.Text.RegularExpressions.Match rowMatch = s_textAlignRegex.Match(row.GetAttributeValue("style", null) ?? string.Empty);
            return rowMatch.Success ? ParseAlignmentToken(rowMatch.Groups[1].Value) : null;
        }

        // 左边界 lookbehind：防自定义属性（如 --my-text-align）内的 text-align 子串误配（同 HtmlSpanParser 惯例）
        static readonly System.Text.RegularExpressions.Regex s_textAlignRegex = new(
            "(?<![\\w-])text-align\\s*:\\s*([a-zA-Z]+)",
            System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase
        );

        static MarkdownTableAlignment? ParseAlignmentToken(string token) {
            return token?.ToLowerInvariant() switch {
                "center" => MarkdownTableAlignment.Center,
                "right" => MarkdownTableAlignment.Right,
                "left" => MarkdownTableAlignment.Left,
                _ => null
            };
        }

        // 直接子级是否含指定元素（浅查一层）：default 行内容器分支的 img 升级判据
        static bool HasElementChild(HtmlAgilityPack.HtmlNode node, string tag) {
            foreach (HtmlAgilityPack.HtmlNode child in node.ChildNodes) {
                if (child.NodeType == HtmlAgilityPack.HtmlNodeType.Element
                    && child.Name.Equals(tag, StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }
            }
            return false;
        }

        // 子树（浅查 4 层够）是否含块级标签：决定未知容器标签走块级递归还是 span 透传
        static bool ContainsBlockChild(HtmlAgilityPack.HtmlNode node, int depth = 0) {
            if (depth > 4) {
                return false;
            }
            foreach (HtmlAgilityPack.HtmlNode child in node.ChildNodes) {
                if (child.NodeType != HtmlAgilityPack.HtmlNodeType.Element) {
                    continue;
                }
                if (child.Name.ToLowerInvariant() is "pre"
                    or "ul"
                    or "ol"
                    or "menu"
                    or "blockquote"
                    or "p"
                    or "div"
                    or "hr"
                    or "table"
                    or "h1"
                    or "h2"
                    or "h3"
                    or "h4"
                    or "h5"
                    or "h6"
                    or "footer"
                    or "address"
                    or "center"
                    or "dl"
                    or "dt"
                    or "dd"
                    or "figure"
                    or "figcaption"
                    or "details"
                    or "summary"
                    or "caption"
                    or "html"
                    or "body") {
                    return true;
                }
                if (ContainsBlockChild(child, depth + 1)) {
                    return true;
                }
            }
            return false;
        }

        // pre/code 内的 <br> 当换行；其余取原始文本
        static string ReplaceBrWithNewline(HtmlAgilityPack.HtmlNode node) => ReplaceBrWithNewline(node, 0);

        static string ReplaceBrWithNewline(HtmlAgilityPack.HtmlNode node, int depth) {
            if (depth > MaxDepth) {
                return string.Empty;
            }
            StringBuilder builder = new();
            foreach (HtmlAgilityPack.HtmlNode child in node.ChildNodes) {
                if (child.NodeType == HtmlAgilityPack.HtmlNodeType.Text) {
                    builder.Append(child.InnerText);
                }
                else if (child.NodeType == HtmlAgilityPack.HtmlNodeType.Element
                    && child.Name.Equals("br", StringComparison.Ordinal)) {
                    builder.Append('\n');
                }
                else {
                    builder.Append(ReplaceBrWithNewline(child, depth + 1));
                }
            }
            return builder.ToString();
        }
    }
}