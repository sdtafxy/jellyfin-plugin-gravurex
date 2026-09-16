<div align="center">

<img src="assets/logo.png" width="560" alt="GravureX">

**为 Jellyfin 补齐日本 DVD / Blu-ray 影片的元数据与图片**

写真偶像（グラビア）类作品优先 · 数据取自公开商品页 · 无需 API Key 或登录

[![Jellyfin](https://img.shields.io/badge/Jellyfin-10.11-00A4DC?logo=jellyfin&logoColor=white)](https://jellyfin.org)
[![.NET](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com)
[![Release](https://img.shields.io/github/v/release/sdtafxy/jellyfin-plugin-gravurex?label=%E7%89%88%E6%9C%AC&color=FF6B6B)](https://github.com/sdtafxy/jellyfin-plugin-gravurex/releases)
[![License](https://img.shields.io/badge/%E8%AE%B8%E5%8F%AF%E8%AF%81-MIT-22C55E)](LICENSE)

**简体中文** · [English](README.en.md)

</div>

## ✨ 特色

- 📋 **元数据** — 标题、简介、发售日期、时长、厂商、类型、JAN 条码、评分、演员与导演
- 🔢 **标题带番号** — 刮削结果形如 `AB123-4567 タイトル`，番号取自你的文件名，连字符位置原样保留
- 🖼️ **三类图片齐全** — 封面、背景、缩略图都会提供，不留空位
- 🎭 **演员信息** — 规范姓名、假名读音、演员 ID，数据源有头像时一并附上
- 🔍 **番号匹配宽松** — `AB123-4567`、`ab1234567`、`AB-1234567`、`n_1234abcd5678` 都能识别
- 💿 **多版本能区分** — 同一番号有通常版 / 蓝光 / 限定特典版时，自动刮削取通常版，手动搜索可看到全部
- 🌐 **网络可控** — HTTP 代理、请求限速、超时与页面缓存，默认配置对数据源温和

元数据按数据源原始发布内容保留日文，不做翻译。

## ✅ 系统要求

| 项目 | 要求 |
|---|---|
| **Jellyfin** | 10.11.x |
| **运行环境** | .NET 9.0，Jellyfin 10.11 已自带，无需单独安装 |
| **网络** | 需能访问 `dmm.com` 与 `pics.dmm.com`；服务器不能直连时，在插件设置里填写 HTTP 代理 |

## 🚀 安装

### 通过插件仓库安装（推荐）

1. 打开 **控制台 → 插件 → 仓库**，添加下面的地址

   ```
   https://raw.githubusercontent.com/sdtafxy/jellyfin-plugin-gravurex/main/manifest.json
   ```

2. 切到 **目录** 标签页，在 *Metadata* 分类下找到 **GravureX**，点击安装
3. **重启 Jellyfin**
4. 打开 **控制台 → 插件 → GravureX** 完成配置
5. 进入媒体库，在 **元数据下载器** 与 **图片获取器** 中勾选 **GravureX**

之后有新版本会直接出现在目录里，可就地升级。

### 手动安装

1. 从 [Releases](https://github.com/sdtafxy/jellyfin-plugin-gravurex/releases) 下载 `jellyfin-plugin-gravurex-*.zip`
2. 解压到 Jellyfin 的 `plugins` 目录，形成 `plugins/Jellyfin.Plugin.GravureX/`
3. 重启 Jellyfin，然后配置插件并在媒体库中启用

<details>
<summary><b>从源码构建</b></summary>

```bash
dotnet publish Jellyfin.Plugin.GravureX/Jellyfin.Plugin.GravureX.csproj \
  -c Release -o ./artifacts
```

把 `./artifacts` 的内容复制到 `plugins/Jellyfin.Plugin.GravureX/`。
</details>

## ⚙️ 配置

全部设置都在 **控制台 → 插件 → GravureX**。

| 设置项 | 默认 | 说明 |
|---|---|---|
| HTTP 代理 | 空 | 例如 `http://127.0.0.1:7890`，服务器不能直连数据源时填写 |
| User-Agent 请求头 | 内置 | 留空使用内置默认值 |
| 请求最小间隔 | `1500` 毫秒 | 数值越大越温和，建议不要调低 |
| 请求超时 | `30` 秒 | 单次请求 |
| 页面缓存时长 | `1440` 分钟 | 填 `0` 关闭缓存 |
| 通过站内搜索解析番号 | 开 | 匹配番号必须开启；文件名写完整内容 ID 时不需要 |
| 标题前置番号 | 开 | 标题形如 `AB123-4567 タイトル` |
| 优先选择通常版 | 开 | 开：只使用最普通的通常版。关：手动搜索会列出全部版本（通常版 / 蓝光 / 限定特典版）；自动刮削始终使用通常版 |
| 保存 JAN 条码为外部 ID | 开 | |
| 导入类型（Genre） | 开 | |
| 把系列名写入标签 | 开 | 数据源提供媒体类型时会一并写入 |
| 把导演加入演职人员 | 关 | 关闭时演职人员只保留演员 |
| 最多提供的预览图张数 | `50` | 填 `0` 表示不限；实际保存张数由媒体库的图片上限决定 |
| 查询演员头像 | 开 | 见下方说明 |
| 演员索引缓存 | `7` 天 | 重建一次索引约需 14 次请求 |

## 📝 文件命名

番号或完整内容 ID 从文件名读取，读不到时回退到所在文件夹名。

| 文件名 | 识别为 |
|---|---|
| `AB123-4567.mp4` | 番号 `AB123-4567` |
| `ab1234567.mp4` | 番号 `ab1234567` |
| `AB-1234567.mp4` | 番号 `AB-1234567` |
| `[AB123-4567] タイトル.mp4` | 番号 `AB123-4567` |
| `n_1234abcd5678.mp4` | 完整内容 ID，直接抓取 |

匹配时忽略连字符，写在哪个位置都可以；数据源对不同厂商补零位数不同，位数差异也会自动兼容。同一作品有多个版本时优先选择通常版。

## 💡 使用提醒

> [!TIP]
> **首次刮削请勾选「替换所有图像」。** 自动刮削每种图片类型只取一张（封面取正面裁切图，背景与缩略图取套图展开图），勾选替换能确保三种类型都写入。手动修改图片时，插件提供的所有图片都会列出来供你挑选。

> [!NOTE]
> **未发售（预约）作品通常没有预览图集**，这时背景与缩略图会使用套图展开图。
>
> **多数演员没有头像。** 数据源只为极少数演员提供头像，查不到属于正常现象；数据源也不提供演员简介与生日。

排查问题时打开 **控制台 → 日志** 搜索 `GravureX`，能看到每次刮削供出了哪些图片；把日志级别调为 `Debug` 还能看到每一条图片地址。

实现细节（图片地址规律、字段映射等）见 [docs/data-source.md](docs/data-source.md)。

## 📄 许可

[MIT](LICENSE)
