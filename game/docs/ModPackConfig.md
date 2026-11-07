# Survivalcraft 整合包配置文件 (packinfo.json) 配置指南

本文档介绍了生存战争插件版整合包配置文件（通常为 JSON 格式）的各项参数含义及填写规范。

整合包用于把一组游戏内容（模组、世界、材质包、角色皮肤、家具包、自定义数据文件）打包为一个整体：玩家导入整合包后可一键补全下载全部资源，并整体启用/停用。整合包管理界面从游戏主菜单「内容」界面的「整合包」按钮进入。

## 分发形式

整合包支持两种分发形式，导入时按扩展名自动识别（仅支持 `.zip` 与 `.json`）。清单文件使用 UTF-8 编码。

### ZIP 形式（推荐）

压缩包根目录必须有一个 `packinfo.json` 清单文件（文件名不区分大小写），其余条目为内嵌的资源文件，条目的 `url` 字段填写**包内相对路径**来引用它们。

约束：

- `packinfo.json` 必须位于压缩包根目录
- 单个条目解压后不超过 256MB，内嵌文件总大小不超过 512MB
- 条目路径中出现 `..` 的文件会被忽略
- 条目文件名编码自动探测：ZIP 头带 UTF-8 标志位时按 UTF-8 解码，否则按 GBK 解码（兼容 Windows 中文打包工具）

### 纯 JSON 清单形式

一个不含资源文件的 `.json` 清单，条目的 `url` 字段必须填写 **http(s) 链接**（或改用 `fileId` 引用社区平台文件）；`icon` 必须为 http(s) 链接（规则见下文）。

> **注意**：清单 JSON 的解析较为宽松——允许注释与尾随逗号，字段名大小写不敏感，容忍 UTF-8 BOM

## 参数一览（顶层）

| 字段名 | 类型 | 中文名 | 必须 |
| :---: | :---: | :----- | :--: |
| **name** | 字符串 | 整合包名称 | 是 |
| **packageName** | 字符串 | 整合包包名，用于区分不同整合包 | 是 |
| **version** | 字符串 | 整合包版本。建议符合 [Semver 语义化版本](https://semver.org/lang/zh-CN/) 标准；仅用于展示与记录，不做范围比较 | 是 |
| **description** | 字符串 | 整合包描述 | 是 |
| **author** | 字符串 | 整合包作者 | 否 |
| **homepage** | 字符串 | 整合包主页链接 | 否 |
| **apiVersion** | 字符串 | 整合包适配的 API 版本，仅作展示，没有实际作用 | 否 |
| **icon** | 字符串 | 整合包图标，仅支持 `.png` 与 `.webp`，规则另见下方 | 否 |
| **items** | 数组 | 资源条目列表，至少一项，规则另见下方 | 是 |

> `author`、`homepage`、`apiVersion` 为展示性可选字段，旧清单缺省时整合包详情界面显示「无」；由社区平台服务端生成 `packinfo.json` 时会自动写入

## items 资源条目

| 字段名 | 类型 | 中文名 | 必须 |
| :---: | :---: | :----- | :--: |
| **targetDirectory** | 字符串 | 目标目录，决定该条目的内容类型，规则另见下方 | 是 |
| **url** | 字符串 | 资源来源：http(s) 下载链接，或 ZIP 形式下的包内相对路径 | 与 fileId 至少填一个 |
| **fileId** | 字符串/数字 | 社区平台文件 ID，`url` 缺失或下载失败时兜底；填写后 API 返回的文件名、包名、版本会自动回填到缺失字段 | 与 url 至少填一个 |
| **category** | 字符串 | 内容类别，仅在 targetDirectory 缺失时用于判定类型（兼容旧清单），取值另见下方 | 否 |
| **fileName** | 字符串 | 保存文件名，缺省时依次取：社区 API 文件名、下载响应文件名、url 尾段、targetPath 尾段；没有扩展名时按内容类型补默认扩展名 | 否 |
| **packageName** | 字符串 | 期望的模组包名，用于校验包内模组完整性，规则另见下方 | 否 |
| **version** | 字符串 | 期望的模组版本，用于校验包内模组完整性，规则另见下方 | 否 |
| **targetPath** | 字符串 | 完整落盘相对路径，主要用于自定义数据文件；targetDirectory 为空时也能反推出 targetDirectory 与 fileName | 否 |

### targetDirectory 与内容类型

targetDirectory 是相对游戏数据目录的路径，按首段路径（大小写不敏感）判定内容类型：

| 首段 | 内容类型 | 说明 |
| :--: | :------: | :--- |
| `mods` | 模组 | 走正常模组导入流程，导入后默认停用 |
| `worlds` | 世界 | |
| `texturepacks` | 材质包 | |
| `characterskins` | 角色皮肤 | |
| `furniturepacks` | 家具包 | |
| 其他 | 自定义数据文件 | 直接写入游戏数据目录，规则另见下方 |

targetDirectory 以 `/` 结尾时视为目录，文件名取 `fileName` 或 url 尾段；不以 `/` 结尾时视为完整的落盘相对路径（等效 targetPath）。

### category 类别对照

| 可选值 | 内容类型 |
| :----: | :------: |
| `mod` / `scmod` | 模组 |
| `world` / `scworld` | 世界 |
| `texture` / `scbtex` / `png` | 材质包 |
| `skin` / `scskin` | 角色皮肤 |
| `furniture` / `scfpack` | 家具包 |
| `data` | 自定义数据文件 |

### 资源来源与下载顺序

每个条目按以下顺序取得数据：

1. `url` 为 http(s) 链接：直接下载（单文件上限 256MB）
2. `url` 为其他相对路径且是 ZIP 形式：取压缩包内对应条目；条目不存在时报错（除非还填了 `fileId`）
3. 以上都没有取得数据且填了 `fileId`：向社区平台查询该文件的真实下载地址后下载；`url` 下载失败时也会走这一步兜底

### packageName 与 version 校验（模组类条目）

模组类条目填写了 `packageName` 或 `version` 时，会与该 scmod 内 `modinfo.json` 的实际值做**忽略大小写的整串比较**（注意：不是版本范围比较），不一致时该条目按失败跳过。

这两个字段用于保证整合包完整性：防止社区文件被更新后，整合包静默装入错误版本。

### 自定义数据文件

内容类型判定为「自定义数据文件」的条目，会按 `targetPath`（或 targetDirectory）把文件直接写入游戏数据目录的相对路径，适用于模组的配置文件等外围数据。

- 路径净化：`\` 按 `/` 处理；去掉开头的 `/` 与 `.`、`..` 路径段；每段做文件名合法化处理
- 首段路径不得为受保护名称，否则整条目被拒绝：`project.xml`、`saves`、`mods`、`settings`、`modpacks`、`worlds`、`texturepacks`、`characterskins`、`furniturepacks`、`shadercaches`、`Settings.xml`、`ModSettings.xml`、`Configs.xml`、`UserId.dat`、`CommunityContentCache.xml`
- 启用整合包时，已有的同名文件会先备份；停用整合包时恢复原文件，整合包启用期间的修改会被保留存档

## icon 图标规则

| 形式 | 规则 |
| :--: | :--- |
| ZIP 形式 | 可填包内相对路径或 http(s) 链接；缺省时按 `icon.webp` → `icon.png` → `Icon.webp` → `Icon.png` 顺序在包内自动探测 |
| JSON 形式 | 必须为 http(s) 链接 |

仅支持 `.png` 与 `.webp` 扩展名。

## 示例

### ZIP 形式（packinfo.json）

```json
{
    "name": "示例整合包",
    "packageName": "Com.Example.Modpack",
    "version": "1.0.0",
    "author": "Someone",
    "homepage": "https://example.com/modpack",
    "apiVersion": "1.9.4",
    "description": "一个示例整合包",
    "icon": "icon.png",
    "items": [
        {
            "targetDirectory": "mods/",
            "url": "files/ExampleMod.scmod",
            "packageName": "Com.Example.Mod",
            "version": "1.2.0"
        },
        {
            "targetDirectory": "texturepacks/",
            "url": "files/TexturePack.scbtex"
        },
        {
            "targetDirectory": "data/",
            "targetPath": "ExampleModPack/config.json",
            "url": "files/config.json"
        }
    ]
}
```

### 纯 JSON 清单形式

```json
{
    "name": "示例整合包",
    "packageName": "Com.Example.Modpack",
    "version": "1.0.0",
    "description": "一个示例整合包",
    "icon": "https://example.com/icon.png",
    "items": [
        {
            "targetDirectory": "mods/",
            "url": "https://example.com/files/ExampleMod.scmod",
            "packageName": "Com.Example.Mod",
            "version": "1.2.0"
        },
        {
            "targetDirectory": "mods/",
            "fileId": 12345
        }
    ]
}
```

## 工作方式

- **下载**：导入整合包后，内嵌资源直接从 ZIP 解压，远程资源按上文顺序下载；本地已有同名内容时跳过，完成后统一汇总
- **启用**：同一时间只允许一个整合包处于启用状态；启用时记录当前所有模组的启停状态作为备份，然后启用包内模组、停用其余全部模组
- **停用**：包内模组停用，其余模组恢复到启用前的启停状态；自定义数据文件恢复为备份的原文件