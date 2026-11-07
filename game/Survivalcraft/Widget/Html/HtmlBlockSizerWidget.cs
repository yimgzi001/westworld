using Engine;

namespace Game {
    // CSS width/max-width 块级尺寸件（单子件容器，builder ApplyBlockSizing 包裹产，恒最外——
    // 盒壳在内层）：测量期把宿主可用宽按声明钳成目标宽传给子件（子件
    // HorizontalAlignment=Stretch 由 builder 设置，Arrange 铺满钳后宽），自身 DesiredSize.X
    // 报钳后宽——子件自然宽更大时保溢出按内容宽报（表格 min-content 溢出现状，不截断）。
    // 宽度口径：FixedWidth>=0 优先（∞ 宽宿主下恒生效，绝对值语义）；否则 WidthPercent 声明
    // 时×宿主可用宽；都无=宿主可用宽（max-width 仍可钳小）。% 的解析基准=测量期宿主可用宽
    //（构建期不可知），宿主给 ∞ 宽时 % 无解析基准，跳过 % 与 % 上限走子件自然宽/固定值。
    // margin:auto 居中不由本件承担（builder 检出后设 HorizontalAlignment=Center，宿主排列期
    // 居中）；margin/padding/border/background 归壳层 WrapInStyleBox，本件不管。
    // 已知限制（留档）：
    // - flex 夹层基准≠flex 分配宽：% 的回填分母=回填宿主 contentWidth（容器净宽链），flex
    //   容器内 sizer 子树的实际分配宽由 flex 主轴协商决定（构建期 basis/运行期 item 宽），
    //   两基准可以不等——flex 内 shell 大多经 IsInsideFlex 跳过回填，残余仅在夹层组合处按
    //   净宽链近似（留档）
    // - FixedWidth 超宿主可用宽：引擎 Arrange 把本件钳到宿主格宽裁切（故意保持绝对值语义，
    //   不随宿主缩放）
    // - % 分母含自身 margin 近似：水平 margin 迁到本件后，宿主测量扣 margin 再传入，% 实际
    //   分母=宿主净宽−自身 margin（CSS 应为不含自身 margin 的包含块 content 宽）
    public class HtmlBlockSizerWidget : StackPanelWidget {
        // 祖先 sizer 的内容宽上限（回填感知共享版，HtmlWidget/RichContentPanel/MarkdownWidget
        // 测量期回填共用）：基准=宿主回填 contentWidth（baseWidth，三调用点各传自己的），自
        // stopAt（宿主壳/格 panel）侧向 frame 自顶向下递推。沿途每节点先扣该节点自身水平
        // margin（口径=引擎 StackPanel 子件测量输入 parentAvail−child.MarginHorizontalSumAnd
        // VerticalSum 的水平分量，只扣 MarginHorizontalSum；方向=父到子不得扣反。壳 pad、
        // 引用/列表缩进都是路径上的 Margin，与构建期 IndentX 记账同源），遇 HtmlBlockSizerWidget
        // 按 ResolveTargetWidth 消费当层可用宽（公式与测量期同源零漂移）。语义核对：px width
        // 绝对值语义=覆写非相对外层钳（width:300 在 width:100 内=内层 300，与本件测量期文档
        // 一致）；嵌套 % 复合（50% 套 50%=25%）自顶向下天然成立；嵌套内层胜（后套的 sizer
        // 递推覆盖 avail）。到 frame 的递推终值即 cap——每节点 margin 恰扣一次（引擎按层扣
        // 子件 margin，此处同序扣同一批），无双重扣减。% 声明参与测量期钳制（分母=baseWidth=
        // 回填宿主 contentWidth）；baseWidth 恒有限（宿主仅在有限基准分支调用），% 恒可解，
        // ∞ 跳 % 守卫仅防御路径（含在 ResolveTargetWidth 内）
        // 收链暂存池：回填热路径每 frame 每 Measure 调用本方法，静态复用免逐调分配（旧版
        // 自底向上零分配的口径补回）。UI 单线程无并发；本方法收链/递推全程只读树不 Measure，
        // 调用期间不会重入本方法（嵌套宿主 Measure 也是先整池清空用尽再返回），池安全
        static readonly Stack<Widget> s_capChain = [];

        internal static float ResolveAncestorCap(Widget frame, Widget stopAt, float baseWidth) {
            // 压栈序=frame→stopAt，弹栈即自 stopAt 侧向 frame 递推（父到子，方向不得反）
            s_capChain.Clear();
            for (Widget w = frame; w != null && w != stopAt; w = w.ParentWidget) {
                s_capChain.Push(w);
            }
            float avail = baseWidth;
            while (s_capChain.Count > 0) {
                Widget w = s_capChain.Pop();
                avail = MathUtils.Max(avail - w.MarginHorizontalSum, 0f);
                if (w is HtmlBlockSizerWidget sizer) {
                    avail = sizer.ResolveTargetWidth(avail);
                }
            }
            return avail;
        }

        // 固定像素宽（width:300px → 300；builder 已做 content-box 补偿=声明值+水平 pad/border），
        // <0=未声明
        public float FixedWidth = -1f;

        // 百分比宽（width:50% → 50），null=未声明；解析基准=测量期宿主可用宽
        public float? WidthPercent;

        // 固定像素宽上限（max-width:200px → 200；补偿口径同 FixedWidth），<0=未声明
        public float MaxFixedWidth = -1f;

        // 百分比宽上限（max-width:50% → 50），null=未声明；解析基准=测量期宿主可用宽
        public float? MaxWidthPercent;

        public HtmlBlockSizerWidget() => Direction = LayoutDirection.Vertical;

        public override void MeasureOverride(Vector2 parentAvailableSize) {
            float availX = parentAvailableSize.X;
            float width = ResolveTargetWidth(availX);
            float fixedHeight = 0f;
            float contentWidth = 0f;
            int fillCount = 0;
            foreach (Widget child in Children) {
                if (!child.IsVisible) {
                    continue;
                }
                // 引擎口径：可用宽先扣子件四边距（垂直栈高度不限）
                child.Measure(Vector2.Max(new Vector2(width, float.PositiveInfinity) - child.MarginHorizontalSumAndVerticalSum, Vector2.Zero));
                if (child.ParentDesiredSize.Y != float.PositiveInfinity) {
                    fixedHeight += child.ParentDesiredSize.Y + child.MarginVerticalSum;
                }
                else {
                    fillCount++;
                }
                contentWidth = MathUtils.Max(contentWidth, child.ParentDesiredSize.X + child.MarginHorizontalSum);
            }
            // m_fixedSize/m_fillCount 供 base ArrangeOverride 分配 ∞ 高子件（与 StackPanel 同语义）
            m_fixedSize = fixedHeight;
            m_fillCount = fillCount;
            // 目标宽解析为 ∞（无固定值且宿主 ∞ 宽/% 无基准）时按子件自然宽报，不产 ∞ 期望宽
            DesiredSize = new Vector2(
                float.IsPositiveInfinity(width) ? contentWidth : MathUtils.Max(width, contentWidth),
                fillCount == 0 ? fixedHeight : float.PositiveInfinity
            );
        }

        // 目标宽：width 声明优先（固定 > 百分比），缺省=宿主可用宽；max-width 声明再取小。
        // 固定值在 ∞ 宽宿主下恒生效（绝对值语义）；仅 % 在 ∞ 时无解析基准跳过
        float ResolveTargetWidth(float availX) {
            float width = FixedWidth >= 0f ? FixedWidth :
                float.IsPositiveInfinity(availX) ? float.PositiveInfinity :
                WidthPercent.HasValue ? availX * WidthPercent.Value / 100f : availX;
            float maxWidth = float.PositiveInfinity;
            if (MaxFixedWidth >= 0f) {
                maxWidth = MaxFixedWidth;
            }
            else if (MaxWidthPercent.HasValue
                && !float.IsPositiveInfinity(availX)) {
                maxWidth = availX * MaxWidthPercent.Value / 100f;
            }
            return MathUtils.Min(width, maxWidth);
        }
    }
}