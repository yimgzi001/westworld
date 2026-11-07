using Engine;

namespace Game {
    // CSS transform 块级壳（单子件，builder ApplyBlockTransform 包裹产，恒最外——sizer/盒壳都
    // 在内层）。三条口径（约束留档）：
    // - 布局零参与：CSS transform 不影响布局流，MeasureOverride 照 StackPanelWidget 原样透传，
    //   DesiredSize 不含变换——变换后视觉可能溢出宿主=CSS 溢出语义；外层 ClampToBounds 按
    //   GlobalBounds（旋转后包围盒 AABB）与父 scissor 求交，旋转件外接框裁切、无撕裂
    // - 乘序：引擎 Matrix 行向量口径（Vector2.Transform/p*M，左矩阵先作用），照抄
    //   ScreensManager/ComponentGui 的绕中心模式（CreateTranslation(-c)×函数×CreateTranslation(c)）；
    //   CSS 列向量 M=F1×…×Fn 转行向量后函数按声明逆序相乘（见 ArrangeOverride）
    // - 角向：引擎 CreateRotationZ 正角在 y 向下屏幕坐标=视觉顺时针，与 CSS rotate 正角同向，
    //   不取负
    public class HtmlTransformBoxWidget : StackPanelWidget {
        // 已解析函数列表（CSS 声明序，解析器 CssTransformParser）；null/空=不设变换（保
        // Widget m_isRenderTransformIdentity 恒等快路径）
        public List<CssTransformFunc> Transforms;

        // transform-origin：比例分量（1=100%）与绝对 px 分量各存一路（解析器编码：比例分量
        // px 路置 0、反之比例路置 0），origin=比例×ActualSize+px 逐分量加和；默认 50%,50%
        public Vector2 OriginRatios = new(0.5f, 0.5f);

        public Vector2 OriginPx;

        public HtmlTransformBoxWidget() => Direction = LayoutDirection.Vertical;

        public override void ArrangeOverride() {
            // ActualSize 此刻已定（Widget.Arrange 先解析后调 ArrangeOverride）；% 参与 origin
            // 的解析基准=本盒排列尺寸≈CSS 边框盒（近似留档：含直接子件水平 margin——子件
            // margin 落在盒 ActualSize 内侧，% 分母随之含之；壳内 padding 同理含之）
            if (Transforms != null
                && Transforms.Count > 0) {
                Vector2 origin = OriginRatios * ActualSize + OriginPx;
                // 合成 M=T(-o)·Fn·…·F1·T(o)：先移回原点，函数按声明逆序（行向量口径），
                // 最后包回 origin；translate 的 % 参在 ToMatrix 内按 ActualSize 解析
                Matrix transform = Matrix.CreateTranslation(-origin.X, -origin.Y, 0f);
                for (int i = Transforms.Count - 1; i >= 0; i--) {
                    transform *= ToMatrix(Transforms[i]);
                }
                transform *= Matrix.CreateTranslation(origin.X, origin.Y, 0f);
                // 先设再 Arrange：Widget.Arrange 内部消费 RenderTransform 合成 GlobalTransform
                //（文字批 TransformTriangles/queue 四角皆过该矩阵）
                foreach (Widget child in Children) {
                    child.RenderTransform = transform;
                }
            }
            base.ArrangeOverride();
        }

        // 单函数 → 行向量矩阵（translate/scale/rotate 三族）
        Matrix ToMatrix(CssTransformFunc func) {
            switch (func.Kind) {
                case CssTransformFuncKind.Translate:
                    float x = func.X.IsPercent ? func.X.Value * ActualSize.X : func.X.Value;
                    float y = func.Y.IsPercent ? func.Y.Value * ActualSize.Y : func.Y.Value;
                    return Matrix.CreateTranslation(x, y, 0f);
                case CssTransformFuncKind.Scale: return Matrix.CreateScale(func.X.Value, func.Y.Value, 1f);
                default: return Matrix.CreateRotationZ(MathUtils.DegToRad(func.X.Value));
            }
        }
    }
}