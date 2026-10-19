<div align="center">

<img src="assets/logo.png" width="560" alt="GravureX">

**让 Jellyfin 认识你的番号**

标题、演员、封面、预览图，一次刮齐。不用 API Key，不用登录，装完就能用。

[![Jellyfin](https://img.shields.io/badge/Jellyfin-10.11.x-00A4DC?style=for-the-badge&logo=jellyfin&logoColor=white)](https://jellyfin.org)
[![.NET](https://img.shields.io/badge/.NET-9.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com)
[![C#](https://img.shields.io/badge/C%23-239120?style=for-the-badge&logo=csharp&logoColor=white)](https://learn.microsoft.com/dotnet/csharp/)
[![License](https://img.shields.io/badge/License-MIT-22C55E?style=for-the-badge&logo=opensourceinitiative&logoColor=white)](LICENSE)
[![Release](https://img.shields.io/github/v/release/sdtafxy/jellyfin-plugin-gravurex?style=for-the-badge&label=Release&color=FF6B6B)](https://github.com/sdtafxy/jellyfin-plugin-gravurex/releases)
[![Build](https://img.shields.io/github/actions/workflow/status/sdtafxy/jellyfin-plugin-gravurex/build.yml?style=for-the-badge&label=Build&logo=githubactions&logoColor=white)](https://github.com/sdtafxy/jellyfin-plugin-gravurex/actions)
[![Downloads](https://img.shields.io/github/downloads/sdtafxy/jellyfin-plugin-gravurex/latest/total?style=for-the-badge&label=Downloads&logo=github&logoColor=white&color=0EA5E9)](https://github.com/sdtafxy/jellyfin-plugin-gravurex/releases)
[![Last commit](https://img.shields.io/github/last-commit/sdtafxy/jellyfin-plugin-gravurex?style=for-the-badge&label=Last%20commit&logo=git&logoColor=white&color=8B5CF6)](https://github.com/sdtafxy/jellyfin-plugin-gravurex/commits/main)
[![Metadata](https://img.shields.io/badge/Metadata-%E6%97%A5%E6%9C%AC%E8%AA%9E-EF4444?style=for-the-badge)](https://github.com/sdtafxy/jellyfin-plugin-gravurex)
[![API key](https://img.shields.io/badge/API%20key-not%20required-16A34A?style=for-the-badge)](https://github.com/sdtafxy/jellyfin-plugin-gravurex)

**简体中文** · [English](README.en.md)

</div>

## 它是怎么工作的

文件叫 `AB123-4567.mp4`，插件拿这个番号去搜，找到对应商品，把标题写成
`AB123-4567 タイトル`，再补上演员、发售日期、时长、厂商和图片。

同一个番号常常对应好几个版本——通常版、蓝光、限定特典版。自动刮削挑最普通的那版；
手动搜索时，你想选哪个都行。

## 能做什么

- **元数据**：标题、简介、发售日期、时长、厂商、类型、JAN 条码、评分、演员
- **标题带番号**：`AB123-4567 タイトル`，番号从文件名来，连字符保持你原来的写法
- **三种图片**：封面、背景、缩略图都给，不留空位
- **演员**：姓名、假名读音、演员 ID，有头像的话一并带上
- **番号写法随意**：`AB123-4567`、`ab1234567`、`AB-1234567`、`n_1234abcd5678` 都认得
- **网络可控**：代理、限速、超时、缓存都能调，默认值已经比较克制

## 开始之前

- Jellyfin 10.11.x
- .NET 9.0 —— Jellyfin 10.11 自带，不用另装
- 服务器能访问 `dmm.com` 和 `pics.dmm.com`；连不上就在插件设置里填代理

## 安装

1. 打开 **控制台 → 插件 → 仓库**，把这行加进去：

   ```
   https://raw.githubusercontent.com/sdtafxy/jellyfin-plugin-gravurex/main/manifest.json
   ```

2. 切到 **目录** 标签页，在 Metadata 分类里找到 **GravureX**，点安装
3. 重启 Jellyfin
4. 到 **控制台 → 插件 → GravureX** 配置一下
5. 进媒体库，在**元数据下载器**和**图片获取器**里勾上 GravureX

以后有新版本会直接出现在目录里，点更新就行。

<details>
<summary>不想用插件仓库</summary>

从 [Releases](https://github.com/sdtafxy/jellyfin-plugin-gravurex/releases) 下载 zip，
解压到 `plugins/Jellyfin.Plugin.GravureX/`，重启 Jellyfin。
</details>

<details>
<summary>想自己编译</summary>

```bash
dotnet publish Jellyfin.Plugin.GravureX/Jellyfin.Plugin.GravureX.csproj \
  -c Release -o ./artifacts
```

然后把这堆文件丢进 `plugins/Jellyfin.Plugin.GravureX/`。
</details>

## 文件怎么命名

番号从文件名里读，读不到就看上一级文件夹名。

```
AB123-4567.mp4          →  AB123-4567
ab1234567.mp4           →  ab1234567
AB-1234567.mp4          →  AB-1234567
[AB123-4567] 标题.mp4    →  AB123-4567
n_1234abcd5678.mp4      →  完整 ID，直接抓
```

连字符写哪儿都行。不同厂商补零位数不一样，这个也自动兼容。

## 设置

都在 **控制台 → 插件 → GravureX**。

**网络**

- **HTTP 代理** —— 服务器连不上数据源时填，比如 `http://127.0.0.1:7890`
- **User-Agent** —— 留空用内置的
- **请求间隔** —— 默认 1500 毫秒。别调太低，对站点友好一点
- **请求超时** —— 默认 30 秒
- **缓存时长** —— 默认 1440 分钟，填 0 就不缓存

**番号与版本**

- **通过站内搜索解析番号** —— 默认开。关掉的话，文件名里得写完整内容 ID
- **标题前置番号** —— 默认开，标题会变成 `AB123-4567 タイトル`
- **优先选择通常版** —— 默认开。关掉后手动搜索会列出全部版本，自动刮削仍然用通常版

**元数据**

- **导入类型**、**系列名写入标签** —— 默认都开
- **保存 JAN 条码为外部 ID** —— 默认开
- **把导演加入演职人员** —— 默认关
- **最多提供的预览图** —— 默认 50 张，填 0 表示不限

**演员**

- **查询演员头像** —— 默认开
- **演员索引缓存** —— 默认 7 天，重建一次大约 14 个请求

## 两个提醒

> [!TIP]
> 第一次刮削时，刷新元数据记得勾上**替换所有图像**。自动扫描每种图片只取一张，勾上才会三种都写进去。

> [!NOTE]
> 手动改图片时，插件提供的每一张都会列出来，包括站上的全部预览图——想换哪张都行。

## 它做不到的事

都是数据源的限制，不是插件坏了。

- **预约中的作品没有预览图集**，背景和缩略图会退回用包装图
- **大部分演员没有头像**，站上只给很少一部分演员传了头像
- **演员没有简介和生日**，站上就没有这些
- **元数据保持日文**，不做翻译

## 出问题的时候

打开 **控制台 → 日志**，搜 `GravureX`，能看到每次刮削供出了哪些图。还是看不出所以然，就把日志级别调到 `Debug`，每条图片地址都会打出来。

想了解实现细节（图片地址规律、字段映射），看 [docs/data-source.md](docs/data-source.md)。

## 许可

MIT
