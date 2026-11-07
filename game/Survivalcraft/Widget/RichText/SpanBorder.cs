using Engine;

namespace Game {
    // 行内边框（CSS border-*）：TextSpan 可选载荷。每行盒片段（TextRun）独立成框——
    // 片段推进量含左右边宽（RichTextLayout），绘制时上下横条贴行盒、左右竖条贴片段盒
    //（垂直不撑行高，浏览器行内 border 口径）。Widths 轴序=X左 Y上 Z右 W下（同 Margin4）
    public class SpanBorder {
        public Vector4 Widths;

        // null=currentColor（绘制期回退 run.Span.Color ?? widget.Color）
        public Color? Left;

        public Color? Top;

        public Color? Right;

        public Color? Bottom;
    }
}