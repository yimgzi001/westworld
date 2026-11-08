# 生存战争 AI 接口摸底（2026-11-07）

## 已实测
实际运行获准社区 SCNET X26.07.01.01，不是 Minecraft 或模拟世界。真实游戏读取位置、背包26槽、附近125方块；脚本经文件接口移动、挖沙并拾取、放沙，均由游戏逻辑处理，无瞬移/刷物品/直接改地形。证据见 evidence。
单人移动 (570.5,65,-132.5) → (571.1317,65,-134.23647)；向下挖沙后 y65→64、沙子2个；放置后沙子2→1且目标方块变为沙子。
两个独立游戏进程已连接同一个私有世界，生成不同角色。这不是同一进程中的两个机器人。首次双角色动作测试受狼袭死亡干扰；切换无害白天、使用InputRunner后，已通过同时独立动作测试：两角色ack=true、health=1，位移约0.598格与0.900格；服务器观察client位置一致，client观察host误差约0.004格。见dual-live-v2.json。

## 资源与限制
新同配置softpipe连续30秒、31点测量：单host RSS696.91MiB / CPU100.32%；双进程RSS716.37+742.62=1458.99MiB / CPU100.42+100.52=200.94%。关闭第二客户端后测单基线，host分辨率/视距/渲染器未改；观察增量约762.08MiB和1.006核。无模型同时运行。这是新增完整客户端，不是纯角色成本；时间/视角有变化且没有N角色压力测试，不可线性外推。

原渲染配置30秒样本：单进程平均RSS605.6MiB，CPU400.9%（约4核）；双进程RSS638.7+652.2MiB，CPU284.6%+206.5%。它们包含完整客户端、渲染和世界模拟。双进程测试有死亡、分辨率与前后台变化，不能相减推算每AI增量，更不是无头服务端成本。未取得可靠FPS或长期稳定性数据。
重启遇到X11 MIT-SHM渲染错误，后改应用级 GALLIUM_DRIVER=softpipe LIBGL_ALWAYS_SOFTWARE=1 成功恢复，但非常慢；不能把此配置与旧资源数据直接比较。
SCNET入口无条件Window.Run，未找到该入口可用headless模式。IsMainPlayer绑定网络Self.PlayerGuid，本地模式排除非主角色；网络服务端单进程bot仍未实现；后续纯本地双主角色实验已成功，见下一节。
模型工作另见Qwen报告；本包的三项初始动作证据来自脚本，不冒充模型决策。

## 版本
https://github.com/survivalcraft-net/survivalcraft-net/releases/tag/X26.07.01.01
发布包x26.07.01.01.zip SHA256: 8078d67a6696cb86ef50ba84013301257da1d4f1a28498b08b1d698d5918e230
窗口标题x26.06.19与发布标签不同。实际模组ApiVersion=1.42，运行Microsoft.NETCore.App10.0，构建用官方SDK10.0.403。
本包只有自行编写的代码和脱敏证据，不含游戏DLL、素材、账号、密钥或世界存档。授权许可范围仍需发布前确认，不能据此再分发游戏。

## 本地文件API
每个角色 GUID.state.json / GUID.command.json；只对本进程IsMainPlayer消费动作，其他角色仅观察。状态期望5Hz，但依赖游戏tick。状态含health、position、inventory、nearby原始方块值（包含光照位，不可把非零都视为固体）。
动作Id单调递增，Action=move/dig/place/stop/select，X/Y/Z，Seconds最多0.5，Slot可选。move的X/Z为本地横向/前向，dig/place为世界射线方向。命令原子重命名，读取ack(lastCommand)后等待游戏tick而非固定墙钟时间，再验位置和背包。模型不可发任意系统命令。
输入只在游戏线程应用，InputRunner位于Input后、Player前，尝试解决失焦重置。还未做持续运行/重新载入/跨世界清理完整验证，仅用于一次性私有世界。GUID可能关联访客凭据，禁止公开原始ipc或Game.log。

## 复现
1. 自行取得获准发布包并解压为../runtime；本包不含第三方游戏。
2. 安装官方.NET10 SDK：dotnet build Bridge.csproj；python3 -m unittest -v test_protocol。
3. 把自己编译的Bridge.dll与modinfo.json放在ZIP根目录，扩展名.netmod，装入该版NetMods目录；.scmod不会加载。
4. 在图形桌面设置SC_AGENT_DIR为私有目录，启动私有一次性无害白天世界。Linux可能需在应用私有native-compat目录提供libX11.so、libXi.so、libXrandr.so、libGL.so指向系统已有版本的链接，并设置LD_LIBRARY_PATH。无音频可ALSOFT_DRIVERS=null；普通渲染失败可尝试上述softpipe，勿更改系统权限。
5. 用controller.py读取真实GUID发短命令，确认新鲜状态、health>0，观察ack及游戏tick后验证效果，再stop。
6. 网络双角色需第二个隔离运行目录、不同本地访客标识及同一私有房间；当前不提供凭据或自动加入公共服务器。

## 下一阶段验收
固定相同渲染设置/视距/分辨率，先测0/1/2角色；扩展两角色稳定性与长时同步测试；再考虑服务器内bot组件替代每AI完整客户端。模型只做低频高层目标，移动/挖掘由规则执行器逐tick完成。

## 单进程多角色静态候选
已确认AddPlayerData会注册唯一GUID及玩家索引并触发PlayerAdded；PlayerData提供SetMain，但FirstUpdate在网络模式仍要求存在对应客户端，否则MakePlayerOffline。因此可研究本地模式SetMain或服务端专用bot生命周期，不过还涉及Widget、物理归属、重生和存档。该候选随后已在纯本地模式实装并通过，见后续实验记录；网络服务端bot仍未验证。

## 后续实测：一个进程、两个独立角色
在独立bot-runtime及一次性存档副本中，关闭LAN连接，用本地模式加载。BotBridge通过new PlayerData(project)、唯一GUID、SetMain、AddPlayerData创建LocalBotB。实际出现上下分屏、两个health=1的新鲜状态，只有一个游戏进程。现有runtime和原证据未修改。
两角色同时不同move命令均ack，1.6游戏秒后分别位移约0.657与0.651格；再只动一位、另一位stop，分别位移0.654与0.014格（后者在水中轻微漂移），未出现明显串控。测试末已stop。证据singleprocess-bots-actions.json、singleprocess-isolation-actions.json。
30秒31采样始终1进程，平均RSS675.47MiB，CPU100.23%约1核，无模型。不可用675减旧697推算角色负增量：本地/网络模式、视角、存活时长和GC不同。本轮没有同一实例移除bot的严格基线。
这是本地双主角色分屏方案，不是无头服务端；尚未测存档重载、死亡重生、超过两个角色、取消额外视角、网络真人加入。实验模组启动时自动新增一位，重启可能重复创建，因此只用于一次性副本，不装入正式存档。BotBridge目录仅覆盖修改的BridgeMod.cs，其余文件沿用Bridge目录并自行构建。

## 最新对照：隐藏玩家视图，保留游戏窗口
新实验模组支持在SC_AGENT_DIR创建hide-views标记，令所有PlayerData.GameWidget.IsVisible=false。两块3D视图停止绘制，OpenGL窗口和游戏循环仍在，因此不是脱离图形环境的真正headless。隐藏后两角色仍可通过接口移动、ack并得到真实位置反馈；见hidden-two-bots-actions.json。
同一个进程同一世界、同视图隐藏状态，各30秒31采样：双角色693.79MiB/CPU96.54%/游戏时间墙钟比0.999；通过RemovePlayerData正常移除本轮实验bot后，单角色678.87MiB/CPU95.91%/比率1.002。观察差+14.93MiB、+0.63个百分点CPU，后者接近噪声，前者受GC与缓存影响。不是稳定每AI配额，不应线性外推多人。两阶段均无模型推理。
相比开启双视图时5秒只推进0.8游戏秒（0.16倍），隐藏视图已接近实时，说明此前软件渲染是关键瓶颈。仍需更长重复测试、固定世界活动、进程堆分配统计和N角色扩展。最新构建0错误/1个可空引用警告（RemovePlayerData前获取ComponentInput），本次正常运行无接口错误。
标记remove-bot仅通过正常生命周期移除本轮创建的实验bot，保留原主角色；旧实验存档与原runtime存档分别保留，不覆盖已验证证据。所有这些开关仅供一次性实验。
