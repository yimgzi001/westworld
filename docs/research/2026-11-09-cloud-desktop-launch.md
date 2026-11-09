# 2026-11-09 dot 云桌面原生启动核验

## 结论修正
此前的 GLFW 无图形平台、Xvfb 无法建立 socket，只证明 **exec 命令工具上下文** 受限，不能推出整台 dot 云电脑没有桌面。
本次通过受支持的 CUA 桌面工具打开云电脑 Xfce 终端，确认 Linux、DISPLAY=:0，且可读取云工作目录中的构建产物。
在这一正常桌面执行上下文中，SCAPI 1.9.5.1 成功启动并完成主菜单初始化，探针自动关闭，退出码 0。没有更改安全设置、绕过 socket 限制，也没有使用用户电脑。

## 已观察证据
2026-11-09 13:03:32–13:03:34 UTC，桌面终端截图可见：
- GameVersion=2.4.0.0，Platform=Linux，API Version=1.9.5.1，64bit=True。
- S C P A K 资源加载 862 项，数据库、方块、衣物、合成配方和屏幕初始化完成。
- CLOUD_MAIN_MENU_READY
- PROBE_EXIT=0
- OpenAL 报 IllegalCommand；未阻止此次主菜单完成。系统字体回退至内置字体。

观察方式为实际桌面截图，不是根据启动命令推断成功。探针不创建或加载世界，仅触发游戏自身常规设置保存。

## 探针逻辑（此次实际执行）
使用官方上游 API_1.9.5.1 源码与已核验 .NET 10 SDK 编译，引用 Survivalcraft.Linux 项目。构建输出包含 Content.zip 和运行依赖；从输出目录运行 Probe.dll。
```csharp
using Engine;
using Game;
using System.Diagnostics;
var clock=Stopwatch.StartNew();bool ready=false;
Window.Created+=()=>Window.Frame+=()=>{
 if(ScreensManager.CurrentScreen is MainMenuScreen&&!ScreensManager.IsAnimating){
  ready=true;Console.WriteLine("CLOUD_MAIN_MENU_READY");Window.Close();
 }
 else if(clock.Elapsed.TotalSeconds>45){
  Console.WriteLine("CLOUD_WINDOW_TIMEOUT");Window.Close();
 }
};
try{Game.Program.EntryPoint();return ready?0:1;}
catch(Exception e){Console.WriteLine(e);return 1;}
```
45 秒检查是每帧检查，并非能中止挂死线程的外部硬超时。此次正常完成约 2 秒，不依赖超时退出。

## 边界与下一步
- **没有创建世界、生成五个 actor、验证角色存读档或区块激活。**
- 桌面可运行不等于实现了零图形宿主；此次仍使用现有图形初始化。
- 70 项核心契约、7 项原生类型钩子测试与此次主菜单核验是三种不同证据，不能合并宣称多 AI 生存已完成。
- 下一步可以在此正常云桌面上下文中运行独立实验世界，核验活动中心适配器和真实无视口 actor。始终不使用用户电脑。
