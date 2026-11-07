using Engine;
using Engine.Graphics;

namespace Game {
    // style 盒属性壳：StackPanel 布局全继承，Draw 补画整幅背景（BackgroundColor）与四边边框
    // （外缘贴 ActualSize、向内 BorderWidths）。子件四边距已由 builder 并入 border+padding
    // （CSS border-box），边框区不压内容。照 TablePanelWidget 先例：FlatBatch2D layer=-1、
    // 局部坐标四角过 GlobalTransform、颜色×GlobalColorTransform、A<=0 跳过；横条全宽+竖条全高
    // 在四角重叠（同表格外框口径）
    public class HtmlStyleBoxWidget : StackPanelWidget {
        public Vector4 BorderWidths;

        // null=无背景（A<=0 同义，入批前统一跳过）
        public Color? BackgroundColor;

        // null=currentColor（CurrentColor 由 builder 按继承文字色解析）
        public Color? BorderLeftColor;

        public Color? BorderTopColor;

        public Color? BorderRightColor;

        public Color? BorderBottomColor;

        public Color CurrentColor = Color.White;

        // 圆角愿望值：四角 px（border-radius 斜杠前半解析产物）；CornerPercent=对应角百分数
        //（50=50%）。px>0 优先、否则 Draw 期按 min(w,h)×pct/100 解析（ActualSize 布局后才知道）。
        // 全 0=直角原路径逐位不变。轴序=CSS 角序 (左上,右上,右下,左下)，非 Margin4 边序
        public Vector4 CornerRadius;

        public Vector4 CornerPercent;

        // Widget.Draw 只在 IsDrawRequired 时调度（Widget.cs DrawWidgetsHierarchy 过滤），边框壳
        // 每帧都画（照 TablePanelWidget.MeasureOverride 先例）
        public override void MeasureOverride(Vector2 parentAvailableSize) {
            IsDrawRequired = true;
            base.MeasureOverride(parentAvailableSize);
        }

        public override void Draw(DrawContext dc) {
            Vector4 w = BorderWidths;
            float left = MathF.Max(w.X, 0f);
            float top = MathF.Max(w.Y, 0f);
            float right = MathF.Max(w.Z, 0f);
            float bottom = MathF.Max(w.W, 0f);
            bool hasBorder = left + top + right + bottom > 0f;
            // 背景与边框独立成立（有背景无边框也要画）；ActualSize 无效（未布局/零尺寸）两者都不画。
            // radius 单独存在但无 bg/border 仍不画——radius 是 bg/border 的形状修饰，不是独立绘制物；
            // CSS overflow 视觉裁切（内容溢出随圆角裁）不做，圆角只改绘制不改布局/命中（留档）
            if ((!hasBorder && !HasBackground)
                || ActualSize.X <= 0f
                || ActualSize.Y <= 0f) {
                return;
            }
            FlatBatch2D batch = dc.PrimitivesRenderer2D.FlatBatch(-1, DepthStencilState.None);
            float width = ActualSize.X;
            float height = ActualSize.Y;
            // 角半径 resolve（Draw 期 ActualSize 已知）：px 愿望优先（>0），否则 % × min(w,h)；
            // 钳 min(w,h)/2 防相邻角弧相接重叠
            float rTl = ResolveCornerRadius(CornerRadius.X, CornerPercent.X, width, height);
            float rTr = ResolveCornerRadius(CornerRadius.Y, CornerPercent.Y, width, height);
            float rBr = ResolveCornerRadius(CornerRadius.Z, CornerPercent.Z, width, height);
            float rBl = ResolveCornerRadius(CornerRadius.W, CornerPercent.W, width, height);
            if (rTl <= 0f
                && rTr <= 0f
                && rBr <= 0f
                && rBl <= 0f) {
                // 直角原路径（全 0 半径逐位不变，零回归）
                // 背景整幅先入批（FlatBatch 同层按入批序：背景垫在同壳边框之下）；opacity 淡出
                // 由 builder 侧把 v 乘进装饰色（预乘，与 GlobalColorTransform 乘法交换）
                if (HasBackground) {
                    QueueQuad(batch, 0f, 0f, width, height, BackgroundColor.Value * GlobalColorTransform);
                }
                if (top > 0f) {
                    QueueQuad(batch, 0f, 0f, width, top, EdgeColor(BorderTopColor));
                }
                if (bottom > 0f) {
                    QueueQuad(batch, 0f, height - bottom, width, bottom, EdgeColor(BorderBottomColor));
                }
                if (left > 0f) {
                    QueueQuad(batch, 0f, 0f, left, height, EdgeColor(BorderLeftColor));
                }
                if (right > 0f) {
                    QueueQuad(batch, width - right, 0f, right, height, EdgeColor(BorderRightColor));
                }
                return;
            }
            // —— 圆角路径 ——
            // 背景九宫分解（中心+四边带+四角扇）：边带取相邻两角半径的较大者作带宽。均匀半径下
            // 分解精确无重叠；某角严格小于两邻角时两相邻边带有一小块内部重叠，半透明背景该区
            // 双重混色（近似留档，不透明色无感）
            if (HasBackground) {
                Color bg = BackgroundColor.Value * GlobalColorTransform;
                float rTop = MathF.Max(rTl, rTr);
                float rBottom = MathF.Max(rBr, rBl);
                float rLeft = MathF.Max(rTl, rBl);
                float rRight = MathF.Max(rTr, rBr);
                QueueQuad(batch, rLeft, rTop, width - rLeft - rRight, height - rTop - rBottom, bg);
                QueueQuad(batch, rTl, 0f, width - rTl - rTr, rTop, bg);
                QueueQuad(batch, rBl, height - rBottom, width - rBl - rBr, rBottom, bg);
                QueueQuad(batch, 0f, rTl, rLeft, height - rTl - rBl, bg);
                QueueQuad(batch, width - rRight, rTr, rRight, height - rTr - rBr, bg);
                // 四角扇形：圆心=角内 (r,r) 点，θ 自一条边向另一边扫 90°（屏幕系 y 向下，
                // θ 增大顺时针）；r=0 角跳过（扇形零面积）
                if (rTl > 0f) {
                    QueueFan(
                        batch,
                        rTl,
                        rTl,
                        rTl,
                        MathF.PI,
                        1.5f * MathF.PI,
                        bg
                    );
                }
                if (rTr > 0f) {
                    QueueFan(
                        batch,
                        width - rTr,
                        rTr,
                        rTr,
                        1.5f * MathF.PI,
                        2f * MathF.PI,
                        bg
                    );
                }
                if (rBr > 0f) {
                    QueueFan(
                        batch,
                        width - rBr,
                        height - rBr,
                        rBr,
                        0f,
                        0.5f * MathF.PI,
                        bg
                    );
                }
                if (rBl > 0f) {
                    QueueFan(
                        batch,
                        rBl,
                        height - rBl,
                        rBl,
                        0.5f * MathF.PI,
                        MathF.PI,
                        bg
                    );
                }
            }
            // 边框：直边 quad 各端按相邻角 resolve 值让位（左右边让开上下角、上下边让开左右角，
            // 某侧宽≤0 跳过该侧），四角弯段由角弧条带补上
            if (hasBorder) {
                if (top > 0f) {
                    QueueQuad(batch, rTl, 0f, width - rTl - rTr, top, EdgeColor(BorderTopColor));
                }
                if (bottom > 0f) {
                    QueueQuad(batch, rBl, height - bottom, width - rBl - rBr, bottom, EdgeColor(BorderBottomColor));
                }
                if (left > 0f) {
                    QueueQuad(batch, 0f, rTl, left, height - rTl - rBl, EdgeColor(BorderLeftColor));
                }
                if (right > 0f) {
                    QueueQuad(batch, width - right, rTr, right, height - rTr - rBr, EdgeColor(BorderRightColor));
                }
                // 四角弧条带：两侧色各管一半（段中线角过对角线即换色，45° 归属近似）——与
                // Chrome 对角线渐变分色有差（近似留档）；某侧宽 0 时该侧段条带零宽自然隐去，
                // 无需特判
                if (rTl > 0f) {
                    QueueCornerArc(
                        batch,
                        rTl,
                        rTl,
                        rTl,
                        MathF.PI,
                        left,
                        BorderLeftColor,
                        top,
                        BorderTopColor
                    );
                }
                if (rTr > 0f) {
                    QueueCornerArc(
                        batch,
                        width - rTr,
                        rTr,
                        rTr,
                        1.5f * MathF.PI,
                        right,
                        BorderTopColor,
                        top,
                        BorderRightColor
                    );
                }
                if (rBr > 0f) {
                    QueueCornerArc(
                        batch,
                        width - rBr,
                        height - rBr,
                        rBr,
                        0f,
                        right,
                        BorderRightColor,
                        bottom,
                        BorderBottomColor
                    );
                }
                if (rBl > 0f) {
                    QueueCornerArc(
                        batch,
                        rBl,
                        height - rBl,
                        rBl,
                        0.5f * MathF.PI,
                        left,
                        BorderBottomColor,
                        bottom,
                        BorderLeftColor
                    );
                }
            }
        }

        Color EdgeColor(Color? color) => (color ?? CurrentColor) * GlobalColorTransform;

        // 背景成立判定：有值且非全透明
        bool HasBackground => BackgroundColor.HasValue && BackgroundColor.Value.A > 0;

        // 单角半径 resolve：px 愿望（>0）优先，否则 % × min(w,h)（CSS 椭圆化不做，% 分母取两边
        // 较小者）；钳 min(w,h)/2 防相邻角弧相接，负值弃（解析层已拒，此处防御一致）
        static float ResolveCornerRadius(float px, float percent, float width, float height) {
            float r = px > 0f ? px : MathF.Min(width, height) * MathF.Max(percent, 0f) / 100f;
            return MathUtils.Clamp(r, 0f, MathF.Min(width, height) / 2f);
        }

        // 局部坐标任意四点 quad，四点各自经 GlobalTransform 变换后入批（p1=p2 时=三角扇楔，
        // FlatBatch2D 只有 quad 队列接口，三角形用两点重合的退化 quad 口径）
        void QueueQuadPoints(FlatBatch2D batch, Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4, Color color) {
            if (color.A <= 0) {
                return;
            }
            Matrix m = GlobalTransform;
            batch.QueueQuad(Vector2.Transform(p1, m), Vector2.Transform(p2, m), Vector2.Transform(p3, m), Vector2.Transform(p4, m), 0f, color);
        }

        // 背景角扇形：CornerFanSegments 段扇楔（p1=p2=圆心的退化 quad），θ 自 a0 扫到 a1
        const int CornerFanSegments = 6;

        void QueueFan(FlatBatch2D batch,
            float cx,
            float cy,
            float r,
            float a0,
            float a1,
            Color color) {
            Vector2 center = new(cx, cy);
            for (int i = 0; i < CornerFanSegments; i++) {
                float ta = a0 + (a1 - a0) * i / CornerFanSegments;
                float tb = a0 + (a1 - a0) * (i + 1) / CornerFanSegments;
                Vector2 pa = new(MathF.Cos(ta), MathF.Sin(ta));
                Vector2 pb = new(MathF.Cos(tb), MathF.Sin(tb));
                QueueQuadPoints(batch, center, center, pa * r + center, pb * r + center, color);
            }
        }

        // 角弧条带：CornerFanSegments 段环 quad（外弧 r 两点+内缘两点）。内缘=椭圆弧（审查
        // 修复：按段二值归属边各算 rInner，相邻两边宽不等且跨 r 时内缘跳变留月牙洞；改按轴
        // 偏移——x 偏移←该角相邻竖边（左/右）宽、y 偏移←相邻横边（上/下）宽（θ 端点 dir 退轴
        // 处内点与直边 quad 内角精确贴合，洞消；全等宽退化为圆，行为不变）。vertWidth/horizWidth
        // 即按各角几何位置传入的竖边/横边宽。分段颜色近似=按段中线角 45° 对角线归属相邻两侧
        // 之一（firstColor=θ 起始边侧色）；某侧宽 0 的段条带零宽自然隐去
        void QueueCornerArc(FlatBatch2D batch,
            float cx,
            float cy,
            float r,
            float a0,
            float vertWidth,
            Color? firstColor,
            float horizWidth,
            Color? secondColor) {
            Vector2 center = new(cx, cy);
            float a1 = a0 + 0.5f * MathF.PI;
            float rx = MathF.Max(r - MathF.Max(vertWidth, 0f), 0f);
            float ry = MathF.Max(r - MathF.Max(horizWidth, 0f), 0f);
            for (int i = 0; i < CornerFanSegments; i++) {
                float ta = a0 + (a1 - a0) * i / CornerFanSegments;
                float tb = a0 + (a1 - a0) * (i + 1) / CornerFanSegments;
                // 45° 归属：段中线角落在前半区间→firstColor，否则 secondColor
                bool first = (ta + tb) / 2f < a0 + 0.25f * MathF.PI;
                Color color = EdgeColor(first ? firstColor : secondColor);
                Vector2 dirA = new(MathF.Cos(ta), MathF.Sin(ta));
                Vector2 dirB = new(MathF.Cos(tb), MathF.Sin(tb));
                QueueQuadPoints(
                    batch,
                    dirA * r + center,
                    dirB * r + center,
                    new Vector2(dirB.X * rx, dirB.Y * ry) + center,
                    new Vector2(dirA.X * rx, dirA.Y * ry) + center,
                    color
                );
            }
        }

        // 局部坐标 quad 四角各自经 GlobalTransform 变换后入批（斜切/旋转变换下不塌陷）
        void QueueQuad(FlatBatch2D batch, float x, float y, float w, float h, Color color) {
            if (color.A <= 0) {
                return;
            }
            Matrix m = GlobalTransform;
            Vector2 p1 = Vector2.Transform(new Vector2(x, y), m);
            Vector2 p2 = Vector2.Transform(new Vector2(x + w, y), m);
            Vector2 p3 = Vector2.Transform(new Vector2(x + w, y + h), m);
            Vector2 p4 = Vector2.Transform(new Vector2(x, y + h), m);
            batch.QueueQuad(p1, p2, p3, p4, 0f, color);
        }
    }
}