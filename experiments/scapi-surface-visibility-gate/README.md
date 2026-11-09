# 原生表面可见性准入

复用 `scapi-intent-gate` 的 Main/Frame、独立世界、完整无视口 Player、NativeActorSession 与活动中心。只执行受控验收，不调用模型，不注入观察值。

fixture：保存并暂改 9×9 地板及上方空气，角色初始定位到格中心；通过原生 FaceTowards 面向前方3格地板顶面。测试顶面可见、中心射线实际被地板挡、背面FOV未知、中心范围外未知；放置完整4格高7格宽不透明墙确认目标所有样点被挡，再移墙确认恢复。结束恢复原格值并移除updateables/活动中心。没有测试原生墙角接缝；其纯supercover测试不能冒充本gate结果。

状态：源码编译通过，尚待dot桌面真实运行。首次编译因错误调用不存在的AtomicActions.Dispose失败，已改为RemoveUpdateable后成功；最终增量编译0 errors/0 warnings不代表依赖漏洞修复。

准备目录 `work/surface-visibility01`，由协调者在dot桌面终端运行 `bash work/surface-visibility01/run.sh`（从仓库根目录），GUI总上限60秒、场景40秒。日志 `surface.jsonl`、`console.log`、`exit.txt`；实际部署DLL哈希在manifest.json。等待真实结果前不得声称原生验收通过。世界/Content.zip/DLL仅work运行，不提交游戏资产。

## 真实运行结果：surface-visibility01

父协调器在 dot 云桌面运行完成：**exit 0，9/9 断言通过，7.6143737 秒，updateFailures=0**。已逐条核对 `surface.jsonl`，不是以退出码代替断言。

实际顺序：原生转向地板顶面成功；前方3格地板 known；后方地板 unknown；范围外中心 unknown；实际 terrain 的中心射线被挡而顶面可见；完整墙遮挡后 unknown；移墙恢复 known；0 AI视口；unknown没有value。受控初始定位与地形明确为fixture，非自主发现或移动成功；没有模型、库存注入、观察结果注入。

`evidence/surface-visibility01.jsonl.gz.b64` 是完整原始 JSONL 的确定性 gzip/base64，解码即可核对全部3份真实观察、fixture、9条断言和result。`result.json` 记录原始SHA256、真实部署DLL哈希及对照route17哈希。`original-deployment-manifest.json` 保留运行前原始prepared-not-run文本，不倒改历史状态。

### 版本与证据边界

surface01 的 Adapter DLL 为 `aac793fa0998fd694e005e1a4c275f980c21dc980eecfa7b9b5c528200d75241`；route17 为 `bec7bfd575f962c9ca4f3590d7e5fa8c5fe42febdf52dd45ab0a4d5e3fda8601`，**不是同DLL对照**；Survivalcraft DLL亦不同。不能据本gate推导route17导航性能或同构建行为。

surface01部署时保存了DLL哈希，未保存完整构建前源码快照。本次 `source-review-snapshot` 是**运行后审查副本**，不冒充构建前快照：传感器 `VoxelSurfaceVisibility.cs`、`NativeActorSession.cs` 与route17部署源码逐字节相同，且此前已冻结；gate自身源码是本次编译/部署的文件。`NavigationLookPlan.cs` 此时已被下一轮修改，不把当前版本归档成实测版本，也不凭此声称完整程序集可逐字节复建。

原生墙角/边缝未覆盖；supercover纯测试不替代原生角缝验收。也未主动制造卸载chunk，不扩大为“所有loaded失效场景通过”。result在宿主Finish进入cleanup前写出，exit0表示cleanup未抛异常，但没有单独逐字段清理后快照，故不声称资源释放全部另行验收。

提交内容仅源码、说明、JSONL文本及哈希；不含DLL、Content.zip、存档或用户数据。
