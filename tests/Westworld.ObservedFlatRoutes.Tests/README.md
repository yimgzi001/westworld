# 已观察平地路线投影

`ObservedFlatRoutes.Project(window,destination)` 只读取真实感知形成的 window，不查询 terrain/entity/寻路。首版仅轴向、同高、1–4m，返回至多4个间隔不超过1m的不可变 waypoint；未知、空洞、实体方块挡路或过期证据返回 null。不是任意地形寻路。

逐格检查0.5m宽、1.9m高的整条swept AABB空气与完整立方地面，不依赖离散采样跳过角落。轴向与1m下限最多容忍1mm浮点/横漂误差，超过即拒绝；4m上限及时间不放宽。adapter另核对真实Body BoundingBox尺寸不超过此保守包络，并每帧核对真实身体所在缓存证据；不会每帧重新查询全地图。Dynamic obstacle仍由原生碰撞/失败检测终止，不把缓存当保证。

证据保留原 observationId/time，至多3墙钟秒；adapter只派发初始最大年龄<=2秒的整路线，运行继续检查Age+真实墙钟<=3。不能刷新TTL来等模型。证据不足回退已有局部单段。完整route一个动作run和lease，不每米重新Start；最终完成/失败/取消仍正常settling。

纯测试54项通过：四个轴向3m、4m边界、过长/过短/斜向/跨高/NaN/死亡拒绝，空气/支撑/头部缺失拒绝、实体阻挡/空洞/unknown拒绝，短时历史标签与过期拒绝。仅投影纯测，不代表原生连续动作通过。

运行：`dotnet run --project tests/Westworld.ObservedFlatRoutes.Tests -c Release`。测试引用现有Core DLL，不构建共享游戏。真实适配依赖父线程协调的新 `Game.Westworld.MoveAlongObservedRoute`，原生后端以实际Player组件为准，不声明其必有Pilot。

遥测：BoundedLocalNavigation每busy帧产生navigation_sample，bounded requestId跨内部waypoint保持；实际position、velocity、horizontalSpeed、grounded、skill state、monotonicSeconds。Busy结束后，只要宿主仍每帧调用Update，继续记录最近request的真实body，state使用bounded终态，busy=false/ownsLease明确为false；宿主停止调用后的缺样仍不能判静止。日志本身开销需同样对照。

Inspect 返回实际必需格数量与 missing_or_unknown/stale_evidence/invalid_evidence/observed_solid/unsupported_ground，几何拒绝另给精确reason。日志最多前16格并附总issueCount，不泄漏未查询世界数据。只有缺失/过期观察时，adapter优先最多4次目标走廊补看；已知实体阻挡/不支持地面不当作未知。补看不足再保守回退，原TTL和dwell保持不变。新浮点/角掠测试验证微横漂走廊的第二行缺格必须拒绝。

派发刷新修复：`Inspect(..., maximumEvidenceAge:2)` 对仍在3秒TTL内但超过2秒的格返回 `refresh_needed`，而不是完整路线且无issues。对应走廊 `NavigationLookPlan.Next(..., maximumKnownAge:2)` 允许真实补看这些格；默认纯投影/补看门槛仍为3秒。补看最多4次、每格一次，失败不会伪造观测时间或扩大TTL。新增纯测包含精确2秒、2.001秒、2.5秒、非法阈值、真实新观察恢复及预算不重置；尚需原生续段复测。
