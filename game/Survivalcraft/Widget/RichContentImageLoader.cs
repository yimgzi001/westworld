using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Engine;
using Engine.Graphics;
using Engine.Media;

namespace Game {
    /// <summary>
    /// 富内容图片异步加载管线，HtmlWidget/HtmlToWidgetsBuilder（HTML）与 Markdown 系 widget 共用：
    /// 占位 frame（底框 + 隐藏 image + 状态字）→ 后台下载并解码（ImageSharp 纯 CPU，不碰 GL）→
    /// Dispatcher 回主线程做 GL 上传换 Subtexture。
    /// 另持有已加载图片纹理的生命周期（引擎 Dialog/Widget 树无 Dispose 级联）：
    /// 收集每张纹理统一释放，避免每次开详情累积一个不可回收句柄。
    /// 图片源除 http(s) 外支持三类专有 scheme：base64 档 data URI（仅 base64，不支持 urlencoded
    /// 档）走同一异步解码管线；app:/data: 按 Storage 本地文件（app:=exe 安装目录，
    /// data:=LocalApplicationData）后台读盘；content: 走 ContentManager 纹理缓存主线程同步
    /// 加载（所有权归 ContentManager，统一释放不得 dispose）。失败字/日志口径与下载失败一致。
    /// </summary>
    sealed class RichContentImageLoader {
        static readonly CommunityHttpDownloader s_imageDownloader = new(HttpClientProvider.Community);

        // 相对 URL 解析基准；null = 仅接受绝对 URL
        public string BaseUrl;

        // Texture2D 持有 GL 纹理句柄（GraphicsResource:IDisposable）。
        // 收集每张加载的纹理，重建内容或对话框移除时统一释放。
        readonly List<Texture2D> m_loadedTextures = [];

        // URL → 纹理：内容重建（自动翻译等）时复用已下载图片，避免同一张图下载两次。
        // 纹理对象与 m_loadedTextures 同源（后者负责释放），ReleaseTextures 时一并清空。
        // 比较器必须 Ordinal：base64 data URI 大小写敏感（大小写不同=不同图片，IgnoreCase
        // 会串图），http 路径按 RFC 也是大小写敏感的
        readonly Dictionary<string, Texture2D> m_imageTextureCache = new(StringComparer.Ordinal);

        // 代际计数：ReleaseTextures 递增。图片下载/解码是异步的，回调捕获发起时的代际，
        // 回主线程后代际已变（宿主已释放/重建内容）即就地释放新纹理，既防 GL 句柄泄漏，
        // 也防旧内容的晚到回调动新内容的 frame；ReleaseTextures 后 loader 可继续复用（Markdown 重建场景）。
        int m_generation;

        // 本批未决图片请求数：CreateImageBlock 发起下载时 +1，终态 -1（只在上屏线程增减，无锁）。
        // ReleaseTextures 归零换批：旧批晚到终态凭代际失配跳过扣减，不把新批计数扣负
        int m_outstandingRequests;

        // 本内容全部图片请求均已到终态（成功上屏/挂宽待测量回填，或失败字）信号：计数 >0→0
        // 归零瞬间在上屏线程触发，每批各一次；归零后再发起的请求开新批、再归零再触发。
        // 无图内容（含空 src）不发起请求，不触发；全缓存命中同样零请求不触发——
        // 勿当"内容完成"信号消费，重建全缓存场景会等不到
        public event Action AllLoadsSettled;

        // 已换上真实纹理的 frame 及其适配参数：宿主（MarkdownWidget）测量期只回填未完成 frame
        // 的宽度；已完成 frame 宽=图宽不可直改，但格内场景需随格宽重适配（RefitFrame 查此表
        // 取纹理与高上限）。Alt/Url 仅为条目完整性保留（与 pending 条目同构），重适配不消费。
        // ExplicitSize=img width/height 属性（null=无属性走自适应；有则显示尺寸以属性为准）；
        // MaxWidth=CSS max-width px（<0=未声明），显示尺寸在适配出口统一钳制（等比，只缩不放）
        readonly Dictionary<CanvasWidget, (RectangleWidget Image, Texture2D Texture, string Alt, string Url, float MaxHeight, Vector2? ExplicitSize, float MaxWidth)> m_appliedFrames = [];

        // 纹理就绪但宿主宽未解析（frame.Size.X<=0，跟随父容器的宿主在测量期才回填）的 frame。
        // 挂起到宿主调 ResolvePendingWidths()（测量期回填之后）再适配上屏；
        // 不挂起就会按兜底宽适配，窄容器/列表项内溢出
        readonly List<(CanvasWidget Frame, RectangleWidget Image, FontTextWidget Status, string Alt, string Url, float MaxHeight, Vector2? ExplicitSize, float MaxWidth)> m_pendingFrames = [];

        // img width/height 属性帧登记：宿主测量期宽度回填的豁免依据（显式帧宽=属性值，
        // 不可被容器宽覆写；RefitFrame 路径仍会按容器钳制防溢出）。ReleaseTextures 清空
        readonly HashSet<CanvasWidget> m_explicitFrames = [];

        // frame 是否带 width/height 属性显式尺寸（含纹理未就绪的占位期）
        public bool IsFrameExplicit(CanvasWidget frame) => m_explicitFrames.Contains(frame);

        // frame 是否已换上真实纹理（见 m_appliedFrames）
        public bool IsFrameApplied(CanvasWidget frame) => m_appliedFrames.ContainsKey(frame);

        // 格内随格宽重适配已加载 frame（网页 max-width:100% 同义）：GetImageDisplaySize 等比且
        // scale≤1（只缩不放），frame/image Size 重设后宿主 panel 重测 → 行高自动联动。
        // 未注册（占位/顶层未完成 frame）no-op——宽回填归各宿主测量期分支
        public void RefitFrame(CanvasWidget frame, float availableWidth) {
            if (!m_appliedFrames.TryGetValue(frame, out (RectangleWidget Image, Texture2D Texture, string Alt, string Url, float MaxHeight, Vector2? ExplicitSize, float MaxWidth) entry)) {
                return;
            }
            // 显式尺寸 frame：属性口径随可用宽钳制（防溢出，等比缩，只缩不放），再钳 CSS max-width
            Vector2 size = entry.ExplicitSize.HasValue
                ? ClampExplicitSize(entry.Texture, ResolveExplicitSize(entry.Texture, entry.ExplicitSize.Value), availableWidth)
                : GetImageDisplaySize(entry.Texture, availableWidth, entry.MaxHeight);
            size = ClampDisplayWidth(size, entry.MaxWidth);
            frame.Size = size;
            entry.Image.Size = size;
        }

        // 创建图片块 widget 并触发异步加载。占位宽 <=0（宽度跟随父容器）时由宿主在测量期回填 frame.Size.X。
        // explicitSize：img width/height 属性（分量 <=0 视为未给）——该维以属性为准（浏览器同款，
        // 可放大可变形），加载完成后不再按可用宽自适应（RefitFrame 仍会按容器钳制防溢出）。
        // 调用方负责把它挂进内容面板（图片块上方通常需要 spacer，由调用方按各自版式控制）。
        // 占位态视觉=网页破图样式：1px 灰框紧贴状态字并整体居中于 frame。框不能直接用 Rectangle
        // （无内容，-1 尽量小惯例下缩成线），外包 Canvas 的 desired=文字+边距把框撑起
        public CanvasWidget CreateImageBlock(string url, string alt, float placeholderWidth, float maxHeight, Vector2? explicitSize = null, float maxWidth = -1f) {
            url = ResolveUrl(url);
            alt = string.IsNullOrWhiteSpace(alt) ? LanguageControl.Get("PostDetailsContentBuilder", "Image") : alt.Trim();
            CanvasWidget frame = new() {
                Size = new Vector2(
                    explicitSize is { X: > 0 } v1 ? v1.X : placeholderWidth,
                    explicitSize is { Y: > 0 } v2 ? v2.Y : -1f
                ),
                HorizontalAlignment = WidgetAlignment.Center
            };
            if (explicitSize is { X: > 0 } or { Y: > 0 }) {
                m_explicitFrames.Add(frame);
            }
            RectangleWidget image = new() {
                Size = new Vector2(-1f, -1f),
                HorizontalAlignment = WidgetAlignment.Stretch,
                VerticalAlignment = WidgetAlignment.Stretch,
                FillColor = Color.White, OutlineThickness = 0f, TextureLinearFilter = true, IsVisible = false
            };
            frame.Children.Add(image);
            CanvasWidget statusBox = new() {
                HorizontalAlignment = WidgetAlignment.Center,
                VerticalAlignment = WidgetAlignment.Center
            };
            statusBox.Children.Add(
                new RectangleWidget {
                    Size = new Vector2(-1f, -1f),
                    HorizontalAlignment = WidgetAlignment.Stretch,
                    VerticalAlignment = WidgetAlignment.Stretch,
                    FillColor = Color.Transparent,
                    OutlineColor = new Color(128, 128, 128, 128),
                    OutlineThickness = 1f
                }
            );
            FontTextWidget status = new() {
                Text = string.IsNullOrWhiteSpace(url)
                    ? FailedToLoadText(alt)
                    : LoadingText(alt),
                Color = new Color(128, 128, 128),
                FontScale = 0.8f,
                WordWrap = true,
                Margin = new Vector2(4f, 4f)
            };
            statusBox.Children.Add(status);
            frame.Children.Add(statusBox);
            if (string.IsNullOrWhiteSpace(url)) {
                return frame;
            }
            if (m_imageTextureCache.TryGetValue(url, out Texture2D cachedTexture)) {
                // 缓存命中（内容重建，如自动翻译）：复用已下载纹理，不再重复下载。
                ApplyImageTexture(cachedTexture, frame, image, status, alt, url, maxHeight, explicitSize, maxWidth);
                return frame;
            }
            if (url.StartsWith(ContentScheme, StringComparison.OrdinalIgnoreCase)) {
                // content: 同步分支（主线程）：ContentManager.Get 缓存命中即廉价，未命中首载
                // 同游戏常规内容加载。不进异步批（不 m_outstandingRequests++，不触发
                // AllLoadsSettled），未命中与空 src 同口径只落失败字。
                // 所有权约束：content: 取得的纹理归 ContentManager 所有（其缓存与设备重置自管），
                // 只入 m_imageTextureCache 供内容重建复用，不入 m_loadedTextures——
                // ReleaseTextures 统一释放时不得 dispose 它
                Texture2D contentTexture;
                try {
                    contentTexture = GetContentTexture(url);
                }
                catch (Exception ex) {
                    // 损坏内容会从 ContentManager.Get 同步抛出（流已 dispose 时
                    // ContentInfo.Duplicate 抛、坏图数据 Texture2D.Load 抛）：与异步管线失败
                    // 口径对齐——记同格式日志，置 null 落失败字，异常不穿出崩溃
                    Log.Information(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            LanguageControl.Get("PostDetailsContentBuilder", "RequestPostDetailsImageFailed"),
                            url,
                            ex.Message
                        )
                    );
                    contentTexture = null;
                }
                if (contentTexture != null) {
                    m_imageTextureCache[url] = contentTexture;
                }
                ApplyImageTexture(contentTexture, frame, image, status, alt, url, maxHeight, explicitSize, maxWidth);
                return frame;
            }
            m_outstandingRequests++;
            _ = LoadImageAsync(url, alt, frame, image, status, maxHeight, explicitSize, maxWidth);
            return frame;
        }

        // 宿主销毁/重建前调用：释放纹理句柄并递增代际，晚到的旧代回调据此就地释放新纹理。
        // 未决计数一并作废：旧批晚到终态因代际失配不再扣减（新批从 0 计数，不被旧批压制/清空）
        public void ReleaseTextures() {
            m_generation++;
            m_outstandingRequests = 0;
            if (m_loadedTextures.Count == 0) {
                return;
            }
            foreach (Texture2D texture in m_loadedTextures) {
                try {
                    texture?.Dispose();
                }
                catch (Exception ex) {
                    Log.Information(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            LanguageControl.Get("PostDetailsContentBuilder", "DisposePostDetailsTextureFailed"),
                            ex.Message
                        )
                    );
                }
            }
            m_loadedTextures.Clear();
            m_imageTextureCache.Clear();
            m_appliedFrames.Clear();
            m_pendingFrames.Clear();
            m_explicitFrames.Clear();
        }

        // 宿主测量期回填 frame 宽度之后调用：宽已解析的挂起 frame 补适配上屏。
        // 每帧测量都会调，未解析的继续挂起
        public void ResolvePendingWidths() {
            if (m_pendingFrames.Count == 0) {
                return;
            }
            for (int i = m_pendingFrames.Count - 1; i >= 0; i--) {
                (CanvasWidget frame, RectangleWidget image, FontTextWidget status, string alt, string url, float maxHeight, Vector2? explicitSize, float maxWidth) = m_pendingFrames[i];
                if (frame.Size.X <= 0f) {
                    continue;
                }
                m_pendingFrames.RemoveAt(i);
                ApplyImageTexture(m_imageTextureCache.TryGetValue(url, out Texture2D texture) ? texture : null, frame, image, status, alt, url, maxHeight, explicitSize, maxWidth);
            }
        }

        // 请求终态收口：代际失配（ReleaseTextures 后的旧批残留）跳过扣减；>0→0 归零瞬间
        // 触发 AllLoadsSettled。仅在主线程被调（终态都在 Dispatcher 回调/主线程同步路径）
        void SettleRequest(int requestGeneration) {
            if (requestGeneration != m_generation) {
                return;
            }
            if (--m_outstandingRequests == 0) {
                AllLoadsSettled?.Invoke();
            }
        }

        async Task LoadImageAsync(string url,
            string alt,
            CanvasWidget frame,
            RectangleWidget image,
            FontTextWidget status,
            float maxHeight,
            Vector2? explicitSize,
            float maxWidth = -1f) {
            int generation = m_generation;
            byte[] data;
            try {
                if (IsDataUri(url)) {
                    // base64 档 image data URI 无需下载，直接解码。必须留在后台线程：本方法
                    // 首个 await 前仍跑在主线程，几 MB base64 的 FromBase64String 足以卡帧；
                    // 坏 base64 抛 FormatException，由下方既有 catch 统一落失败字 + 日志；
                    // 空 payload（如 data:image/png;base64,）解出空数组，自然落既有零长失败分支
                    data = await Task.Run(() => DecodeDataUri(url)).ConfigureAwait(false);
                }
                else if (url.StartsWith(AppScheme, StringComparison.OrdinalIgnoreCase)
                    || url.StartsWith(DataScheme, StringComparison.OrdinalIgnoreCase)) {
                    // app:/data: Storage 本地文件。desktop：app:=exe 安装目录（failIfApp=false
                    // 安全），data:=LocalApplicationData/<AssemblyName>；ANDROID：app: 走 Assets
                    //（OpenFile failIfApp=false→Assets.Open），data: 归 MyDocuments；iOS：两前缀
                    // 均归 MyDocuments。读盘留在后台线程不卡主线程；文件不存在抛
                    // FileNotFoundException，由下方既有 catch 统一落失败字 + 日志。
                    // 此处 data: 均为非 base64 image 档形态（base64 档已在上方分流）
                    data = await Task.Run(() => Storage.ReadAllBytes(url)).ConfigureAwait(false);
                }
                else {
                    using MemoryStream downloaded = await s_imageDownloader.DownloadToMemoryAsync(url, null, CancellationToken.None)
                        .ConfigureAwait(false);
                    data = downloaded.ToArray();
                }
                if (data.Length == 0) {
                    Dispatcher.Dispatch(delegate {
                        status.Text = FailedToLoadText(alt);
                        SettleRequest(generation);
                    });
                    return;
                }
            }
            catch (Exception ex) {
                Dispatcher.Dispatch(delegate {
                    status.Text = FailedToLoadText(alt);
                    SettleRequest(generation);
                });
                Log.Information(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        LanguageControl.Get("PostDetailsContentBuilder", "RequestPostDetailsImageFailed"),
                        url,
                        ex.Message
                    )
                );
                return;
            }
            // 解码是纯 CPU（ImageSharp，大图可达几十毫秒）：留在后台线程，主线程只做 GL 上传
            Image decodedImage;
            try {
                using MemoryStream stream = new(data);
                decodedImage = Image.Load(stream);
            }
            catch (Exception ex) {
                Dispatcher.Dispatch(delegate {
                    status.Text = FailedToLoadText(alt);
                    SettleRequest(generation);
                });
                Log.Information(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        LanguageControl.Get("PostDetailsContentBuilder", "DecodePostDetailsImageFailed"),
                        url,
                        ex.Message
                    )
                );
                return;
            }
            Dispatcher.Dispatch(
                delegate {
                    try {
                        if (generation != m_generation) {
                            return;
                        }
                        Texture2D texture = Texture2D.Load(decodedImage);
                        if (generation != m_generation) {
                            // 宿主已释放/重建：纹理不会被任何 ReleaseTextures 回收，就地销毁。
                            try {
                                texture.Dispose();
                            }
                            catch (Exception disposeEx) {
                                Log.Information(
                                    string.Format(
                                        CultureInfo.InvariantCulture,
                                        LanguageControl.Get("PostDetailsContentBuilder", "DisposeLatePostDetailsTextureFailed"),
                                        disposeEx.Message
                                    )
                                );
                            }
                            return;
                        }
                        if (m_imageTextureCache.TryGetValue(url, out Texture2D existingTexture)) {
                            // 重建期间并发下载了同一 URL：后到者就地释放，复用先到者。
                            try {
                                texture.Dispose();
                            }
                            catch (Exception disposeEx) {
                                Log.Information(
                                    string.Format(
                                        CultureInfo.InvariantCulture,
                                        LanguageControl.Get("PostDetailsContentBuilder", "DisposeDuplicatePostDetailsTextureFailed"),
                                        disposeEx.Message
                                    )
                                );
                            }
                            texture = existingTexture;
                        }
                        else {
                            m_imageTextureCache[url] = texture;
                            m_loadedTextures.Add(texture);
                        }
                        ApplyImageTexture(texture, frame, image, status, alt, url, maxHeight, explicitSize, maxWidth);
                    }
                    catch (Exception ex) {
                        status.Text = FailedToLoadText(alt);
                        Log.Information(
                            string.Format(
                                CultureInfo.InvariantCulture,
                                LanguageControl.Get("PostDetailsContentBuilder", "DecodePostDetailsImageFailed"),
                                url,
                                ex.Message
                            )
                        );
                    }
                    finally {
                        // 终态（成功上屏/挂宽或失败字）恰好扣减一次；上方陈旧批 return 会被
                        // SettleRequest 的代际守卫跳过
                        SettleRequest(generation);
                    }
                }
            );
        }

        void ApplyImageTexture(Texture2D texture,
            CanvasWidget frame,
            RectangleWidget image,
            FontTextWidget status,
            string alt,
            string url,
            float maxHeight,
            Vector2? explicitSize = null,
            float maxWidth = -1f) {
            try {
                if (texture is not { Width: > 0, Height: > 0 }) {
                    status.Text = FailedToLoadText(alt);
                    return;
                }
                if (frame.Size.X <= 0f) {
                    // 宿主宽未解析（跟随父容器的宿主测量期才回填）：挂起，宽解析后由
                    // ResolvePendingWidths 补适配上屏。现在适配只能按兜底宽，窄容器/列表项内必溢出
                    m_pendingFrames.Add((frame, image, status, alt, url, maxHeight, explicitSize, maxWidth));
                    return;
                }
                // 显式尺寸：width/height 属性口径（缺维按纹理宽高比补），不做可用宽钳制——
                // 浏览器同款（属性优先），超容器由 RefitFrame 路径（格内回传）钳制；
                // CSS max-width 在适配出口统一钳（等比只缩不放）：仅 height 的缺维反推后此处收口
                Vector2 size = explicitSize.HasValue
                    ? ResolveExplicitSize(texture, explicitSize.Value)
                    : GetImageDisplaySize(texture, frame.Size.X, maxHeight);
                size = ClampDisplayWidth(size, maxWidth);
                frame.Size = size;
                image.Size = size;
                image.Subtexture = new Subtexture(texture, Vector2.Zero, Vector2.One);
                image.IsVisible = true;
                status.IsVisible = false;
                m_appliedFrames[frame] = (image, texture, alt, url, maxHeight, explicitSize, maxWidth);
            }
            catch (Exception ex) {
                status.Text = FailedToLoadText(alt);
                Log.Warning(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        LanguageControl.Get("PostDetailsContentBuilder", "DecodePostDetailsImageFailed"),
                        url,
                        ex.Message
                    )
                );
            }
        }

        static string LoadingText(string alt) {
            return string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("PostDetailsDialog", "Loading"), alt);
        }

        static string FailedToLoadText(string alt) {
            return string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("PostDetailsDialog", "FailedToLoad"), alt);
        }

        // 图片显示尺寸：适配可用宽与高上限，不放大（scale ≤ 1）。
        // 可用宽=占位 frame 当前宽（宿主测量期回填过；宽未解析的 frame 在 ApplyImageTexture
        // 已挂起，不会走到这里）
        Vector2 GetImageDisplaySize(Texture2D texture, float availableWidth, float maxHeight) {
            float scale = MathUtils.Min(availableWidth / texture.Width, maxHeight / texture.Height);
            if (scale > 1f) {
                scale = 1f;
            }
            return new Vector2(texture.Width * scale, texture.Height * scale);
        }

        // img width/height 属性口径的显示尺寸：缺维按纹理宽高比补（单给 width/height 的老帖
        // 高频写法），双给按属性值（允许变形与放大——浏览器同款）
        Vector2 ResolveExplicitSize(Texture2D texture, Vector2 explicitSize) {
            float aspect = texture.Width / (float)texture.Height;
            float width = explicitSize.X > 0f ? explicitSize.X : explicitSize.Y * aspect;
            float height = explicitSize.Y > 0f ? explicitSize.Y : width / aspect;
            return new Vector2(width, height);
        }

        // 显式尺寸的容器钳制（RefitFrame 路径）：超可用宽时等比缩（只缩不放），防溢出；
        // 属性比容器窄则保持原值
        Vector2 ClampExplicitSize(Texture2D texture, Vector2 size, float availableWidth) {
            if (size.X > availableWidth
                && size.X > 0f) {
                return size * (availableWidth / size.X);
            }
            return size;
        }

        // CSS max-width 钳制（适配出口统一收口）：超上限等比缩（只缩不放），高随宽联动；
        // maxWidth<0（未声明）或尺寸未超时原样。仅 height 属性的宽由 ResolveExplicitSize 反推，
        // 反推后经此收口（构建期不知宽高比，无法提前钳）
        static Vector2 ClampDisplayWidth(Vector2 size, float maxWidth) {
            if (maxWidth > 0f
                && size.X > maxWidth) {
                return size * (maxWidth / size.X);
            }
            return size;
        }

        // 专有 scheme 前缀常量：前缀判定与 Length 切片统一引用，消字面量漂移
        private const string AppScheme = "app:";
        private const string DataScheme = "data:";
        private const string ContentScheme = "content:";

        // base64 档 image data URI 分类：^data:image/<token>;base64,（OrdinalIgnoreCase，
        // 静态编译只构造一次）。RFC 2397 允许 base64 标记前带参数（如
        // data:image/png;charset=utf-8;base64,…），带参档不认——按 Storage 路径落失败字，
        // 消歧取舍留档。命中=标准 base64 数据 URI，走既有解码管线；不命中时一切
        // data: 前缀一律按 Storage 本地文件路径处理——文件路径可含 ','，“含 ',' 即数据
        // URI”的旧判定不可靠；且非 image 档/非 base64 档 ImageSharp 本就解不了，失败语义不变
        static readonly Regex s_base64ImageDataUri = new(
            "^data:image/[a-z0-9.+-]+;base64,",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled
        );

        // data URI 是否 base64 档 image 形态：ResolveUrl 早退与 LoadImageAsync 分流共用
        static bool IsDataUri(string url) {
            return s_base64ImageDataUri.IsMatch(url);
        }

        // 解码 base64 档 data URI 载荷（IsDataUri 分类已保证 ^data:image/...;base64, 形态，
        // 无需再检 ',' 与档位）：payload 先剥空白（base64 字母表无空白，剥除无损，兼容 MIME
        // 换行折叠的脏数据）再 FromBase64String（坏字符/长度非法抛 FormatException）——
        // 由 LoadImageAsync 既有 catch 统一收口
        static byte[] DecodeDataUri(string url) {
            string payload = url[(url.IndexOf(',') + 1)..];
            bool hasWhitespace = false;
            foreach (char c in payload) {
                if (char.IsWhiteSpace(c)) {
                    hasWhitespace = true;
                    break;
                }
            }
            if (hasWhitespace) {
                char[] chars = payload.ToCharArray();
                int write = 0;
                foreach (char c in chars) {
                    if (!char.IsWhiteSpace(c)) {
                        chars[write++] = c;
                    }
                }
                payload = new string(chars, 0, write);
            }
            return Convert.FromBase64String(payload);
        }

        // content: URL → ContentManager 纹理（主线程同步调用，所有权归 ContentManager）。
        // "content:<path>" 拆后缀：最后一个 '.' 在最后一个 '/' 之后 → Get(name, suffix)
        //（suffix 不带点，ContentManager 内部拼 name + "." + suffix 精确查 Resources）；
        // 无后缀 → Get(name, null) 按 Texture2DReader.DefaultSuffix 逐个探测。
        // throwOnNotFound:false，未命中返回 null；命中但内容损坏可抛（流已 dispose 时
        // ContentInfo.Duplicate 抛、坏图数据 Texture2D.Load 抛），由调用方 catch 记日志置
        // null——未命中或加载异常均落失败字，不向 CreateImageBlock 外穿出
        static Texture2D GetContentTexture(string url) {
            string path = url[ContentScheme.Length..];
            int dot = path.LastIndexOf('.');
            int slash = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
            bool hasSuffix = dot > slash;
            string name = hasSuffix ? path[..dot] : path;
            string suffix = hasSuffix ? path[(dot + 1)..] : null;
            return ContentManager.Get<Texture2D>(name, suffix, throwOnNotFound: false);
        }

        string ResolveUrl(string url) {
            if (string.IsNullOrWhiteSpace(url)) {
                return string.Empty;
            }
            url = url.Trim();
            // base64 image data URI 早退：免对多 MB 串做 HtmlDecode 全文扫描；也不进下方
            // Uri.TryCreate 归一化（Uri.ToString 对 data 载荷的转义改写不可控，有串改载荷风险）
            if (IsDataUri(url)) {
                return url;
            }
            url = WebUtility.HtmlDecode(url);
            // 专有 scheme 短路：app:/data:（Storage 本地文件）与 content:（ContentManager
            // 纹理）不做 // 补全 / Uri.TryCreate / BaseUrl 相对解析——Uri.TryCreate 会把
            // app:foo 当绝对 URI 放过，显式短路既自档语义也保住路径原样大小写
            if (url.StartsWith(AppScheme, StringComparison.OrdinalIgnoreCase)
                || url.StartsWith(DataScheme, StringComparison.OrdinalIgnoreCase)
                || url.StartsWith(ContentScheme, StringComparison.OrdinalIgnoreCase)) {
                return url;
            }
            if (url.StartsWith("//", StringComparison.Ordinal)) {
                return $"https:{url}";
            }
            if (Uri.TryCreate(url, UriKind.Absolute, out Uri absoluteUri)) {
                return absoluteUri.ToString();
            }
            if (string.IsNullOrEmpty(BaseUrl)
                || !Uri.TryCreate(BaseUrl, UriKind.Absolute, out Uri baseUri)) {
                return url;
            }
            return url.StartsWith("/", StringComparison.Ordinal)
                ? new Uri(new Uri(baseUri.GetLeftPart(UriPartial.Authority)), url).ToString()
                : new Uri(baseUri, url).ToString();
        }
    }
}
