# 西部世界

让多个 AI 个体在 Survivalcraft 的世界里生活、生存、交流、合作与建造。世界中的动作由游戏规则结算，每个人依据自己能够感知的信息和经历做决定。

## 仓库内容

- [游戏源码](game/README.md)采用社区插件版 SCAPI 1.9.5.1，包含引擎、实体系统、游戏逻辑、资源和上游文档。
- [设计文档](docs/README.md)保留原有的人物、记忆、世界、社会关系、学习传承、运行与验收目标，已移除旧技术栈、服务器部署和旧实现验收记录。
- [源码来源](game/UPSTREAM.md)记录上游版本、导入范围及签名文件处理。

## 当前状态

当前基线已从源码编译出 Windows 游戏，并完成生存世界生成和角色出生的本地验证。自主 AI 人群接入属于后续工作。

设计文档中的球面地表、生命周期、开放制造和认知机制是迁移保留的目标，需要按游戏实际能力重新核对。它们不作为当前游戏功能或 AI 接入完成的声明。

Windows 构建需 .NET 10 SDK，在仓库根目录运行下面的命令。

```powershell
dotnet build game/Survivalcraft.Windows/Survivalcraft.Windows.csproj --configuration Release
```

旧 Go 实现及其原始文档、实验记录已保存在本地归档。仓库地址使用 `westworld`，项目名称为“西部世界”。

原作来自 Candy Rufus Games，社区插件版来自 SC-SPM。上游署名、版权声明与相关资料随源码保留，新增项目文件不对上游内容重新授予许可。
