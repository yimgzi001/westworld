using Engine.Media;
using SharpAstro.Fonts;

namespace Game.IContentReader {
    /// <summary>
    ///     矢量字体读取器：一个 ttc/ttf/otf → VectorFont 链（单条 ChainEntry，四面按 OS/2 fsSelection 样式位映射）<br/>
    ///     原始字节构建入口见 <see cref="BuildEntries"/>（FontManager 绕过 ContentInfo 路径时直接调用）
    /// </summary>
    public class VectorFontReader : IContentReader {
        public override string Type => "Engine.Media.VectorFont";
        public override string[] DefaultSuffix => ["ttc", "ttf", "otf"];

        public override object Get(ContentInfo[] contents) {
            if (contents.Length != 1) {
                throw new Exception("VectorFont expects exactly 1 content (ttc/ttf/otf)");
            }
            // FallbackBitmapFont 留空，由 FontManager 注入
            return new VectorFont(BuildEntries(contents[0].ContentStream.ToArray(), contents[0].Filename), null);
        }

        /// <summary>
        ///     从原始字体字节构建链条目（LoadAll 探测全部面，面序=ttc 容器序=FontSourceSpec 的 FaceIndex）。
        ///     同样式多面时先到先得（面序即优先级）。
        /// </summary>
        public static List<ChainEntry> BuildEntries(byte[] data, string origin) {
            OpenTypeFont[] faces = OpenTypeFont.LoadAll(data);
            return [new ChainEntry { Data = data, Origin = origin, StyleFaces = BuildStyleFaces(faces) }];
        }

        /// <summary>
        ///     构建单面链条目（系统发现路线专用）：只装 faceIndex 指定的那一个面，样式由发现方判定
        ///     （Android AFontMatcher 按字重、Windows ProbeFontFile 按样式位）。ttc 容器序首面未必是
        ///     目标地区变体（NotoSansCJK/SECCJK 面 0=JP），全面入链会让渲染回落撞到 JP 面字形。
        /// </summary>
        public static ChainEntry BuildEntry(byte[] data, string origin, int faceIndex, VectorFontStyle style)
            => new ChainEntry {
                Data = data,
                Origin = origin,
                StyleFaces = new Dictionary<VectorFontStyle, FontSourceSpec> { [style] = new FontSourceSpec(faceIndex) }
            };

        // 按 OS/2 fsSelection 样式位映射（bit5=Bold、bit0=Italic；PericlesPro.ttc 四面为 0x00/0x20/0x01/0x21）。
        // 缺 OS/2 表或全 0 位归 Regular；face index i 与 FontSourceSpec(i) 一一对应
        static Dictionary<VectorFontStyle, FontSourceSpec> BuildStyleFaces(OpenTypeFont[] faces) {
            var styleFaces = new Dictionary<VectorFontStyle, FontSourceSpec>();
            for (int i = 0; i < faces.Length; i++) {
                ushort fsSelection = faces[i].Os2?.FsSelection ?? 0;
                bool bold = (fsSelection & 0x20) != 0;
                bool italic = (fsSelection & 0x01) != 0;
                var style = bold && italic ? VectorFontStyle.BoldItalic
                    : bold ? VectorFontStyle.Bold
                    : italic ? VectorFontStyle.Italic
                    : VectorFontStyle.Regular;
                if (!styleFaces.ContainsKey(style)) {
                    styleFaces.Add(style, new FontSourceSpec(i));
                }
            }
            return styleFaces;
        }
    }
}
