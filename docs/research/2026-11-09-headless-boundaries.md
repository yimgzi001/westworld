# 无图形宿主边界核查
2026-11-09；只读源码核查，不是原生运行通过。

## 当前能确认
- Engine.Graphics/Display.cs与GLWrapper.cs已有InitializeHeadless，但在WINDOWS条件编译中，使用EGL Pbuffer/OpenGL ES3。它是离屏图形，不是零GL。
- Game/Program.cs的EntryPoint通过Window.Run启动；初始化与时钟/派发/输入/音频揉在Window的InitializeAll及帧生命周期。
- Screen/LoadingScreen.cs把数据库、方块、配方、模组、语言等规则初始化与UI加载混在一起。
- Managers/GameManager.cs的LoadProject注入Views.GamesWidget。
- SubsystemTerrain.Load创建TerrainRenderer，后者编译Shader；SubsystemBlocksTexture上传Texture2D；ComponentModel.Load加载Model/纹理；SubsystemAnimatedTextures.Update创建RenderTarget并绘制。因此不调用Draw或传空Widget不能构成真正无图形。
- ComponentCreatureModel的EyePosition/EyeRotation仍是局部感知所需身体逻辑，不能连同GPU模型直接删除。
- SubsystemUpdate吞掉组件异常。仅进程还活着不能证明生命、物理或采集推进。

## 改造路径
1. Actor身份/模拟活动中心与RenderViews分离。当前纯核心49项测试只覆盖元数据契约，不是实体能力。
2. 抽共享Bootstrap规则初始化；渲染/声音/输入设备初始化作为可选宿主能力。
3. 正式模拟帧入口管理Time与Dispatcher，不通过反射修改内部时钟。
4. SimulationOnly模式不建TerrainRenderer/动画纹理/GPU缓冲；碰撞、姿态、眼睛和规则数据保留。
5. 地形与实体生成由actor活动中心推动，不再由摄像机决定；模拟就绪状态与网格就绪状态需分别定义。
6. 保留原生Project、Body、Locomotion、Miner、Inventory、PickableGatherer和生命组件。原生五actor、零AI视口且0/1人类视口，跨区块活动/死亡/存读档才算集成通过。

可单独研究Linux EGL surfaceless/pbuffer作为更快的过渡验证，明确仍有图形资源和软件渲染/GPU依赖，不能称零GL；当前没有实现或实测该路径。禁止绕过云环境限制或回到用户电脑测试。

## 后续验收必须包含
无GLFW/视口依赖（真正零图形模式还须无GL上下文），实际世界时钟与重力碰撞、移动、挖掘、掉落拾取、库存扣减、生存状态、无吞异常、存档身份与世界结果复载。纯C#身份记录不能代替这些证据。
