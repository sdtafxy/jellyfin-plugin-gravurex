<h1 align="center">GravureX</h1>

<p align="center">
为 Jellyfin 补齐日本 DVD / Blu-ray 影片的元数据与图片，重点覆盖写真偶像（グラビア）类作品。<br>
数据取自 <a href="https://www.dmm.com/mono/dvd/">DMM</a> 公开商品页，无需 API Key 或登录，元数据按原始发布内容保留日文。
</p>

<p align="center"><b>简体中文</b> · <a href="#english">English</a></p>

---

## 特色

- **元数据** — 标题、简介、发售日期、时长、厂商、类型、JAN 条码、评分、演员与导演。
- **标题带番号** — 刮削结果形如 `AB123-4567 タイトル`。番号取自文件名，连字符位置与你命名时一致。
- **三类图片一次到位** — 封面、背景、缩略图都会提供，不再有空缺的位置。
- **演员信息** — 提供规范姓名、假名读音与演员 ID，数据源有头像时一并附上。
- **番号匹配省心** — `AB123-4567`、`ab1234567`、`AB-1234567`、`n_1234abcd5678` 都能识别，连字符位置和补零差异都不影响匹配。
- **网络可控** — 支持 HTTP 代理、请求限速、超时与页面缓存，默认配置对数据源友好。

## 系统要求

| | |
|---|---|
| Jellyfin | 10.11.x |
| 运行环境 | .NET 9.0（Jellyfin 10.11 已自带，无需单独安装） |
| 网络 | 需能访问 `dmm.com` 与 `pics.dmm.com`。服务器不能直连时，在插件设置里填写 HTTP 代理。 |

## 安装

### 通过插件仓库安装（推荐）

1. 打开 **控制台 → 插件 → 仓库**，添加地址：

   ```
   https://raw.githubusercontent.com/sdtafxy/jellyfin-plugin-gravurex/main/manifest.json
   ```

2. 切到 **目录** 标签页，在 *Metadata* 分类下找到 **GravureX**，点击安装。
3. **重启 Jellyfin**。
4. 打开 **控制台 → 插件 → GravureX** 完成配置。
5. 进入你的媒体库，在 **元数据下载器** 与 **图片获取器** 中勾选 **GravureX**。

之后有新版本会直接出现在目录里，可就地升级。

### 手动安装

1. 从 [Releases](https://github.com/sdtafxy/jellyfin-plugin-gravurex/releases) 下载 `jellyfin-plugin-gravurex-*.zip`。
2. 解压到 Jellyfin 的 `plugins` 目录，形成 `plugins/Jellyfin.Plugin.GravureX/`。
3. 重启 Jellyfin，然后配置插件并在媒体库中启用。

<details>
<summary>从源码构建</summary>

```bash
dotnet publish Jellyfin.Plugin.GravureX/Jellyfin.Plugin.GravureX.csproj \
  -c Release -o ./artifacts
```

把 `./artifacts` 的内容复制到 `plugins/Jellyfin.Plugin.GravureX/`。
</details>

## 文件命名

番号或完整内容 ID 从文件名读取，读不到时回退到所在文件夹名。

| 文件名 | 识别为 |
|---|---|
| `AB123-4567.mp4` | 番号 `AB123-4567` |
| `ab1234567.mp4` | 番号 `ab1234567` |
| `AB-1234567.mp4` | 番号 `AB-1234567` |
| `n_1234abcd5678.mp4` | 完整内容 ID，直接抓取 |
| `[AB123-4567] タイトル.mp4` | 番号 `AB123-4567` |

匹配时忽略连字符，所以连字符写在哪里都可以；数据源对不同厂商补零位数不同，位数差异也会自动兼容。同一作品有多个版本时，优先选择通常版。

## 配置项

| 设置项 | 默认 | 说明 |
|---|---|---|
| HTTP 代理 | 空 | 例如 `http://127.0.0.1:7890`。服务器不能直连数据源时填写。 |
| User-Agent | 内置 | 留空使用内置默认值。 |
| 请求最小间隔 | `1500` 毫秒 | 数值越大越温和，建议不要调低。 |
| 请求超时 | `30` 秒 | 单次请求。 |
| 页面缓存时长 | `1440` 分钟 | 填 `0` 关闭缓存。 |
| 通过站内搜索解析番号 | 开 | 匹配番号必须开启；文件名写完整内容 ID 时不需要。 |
| 标题前置番号 | 开 | 标题形如 `AB123-4567 タイトル`。 |
| 优先选择通常版 | 开 | |
| 保存 JAN 条码为外部 ID | 开 | |
| 导入类型（Genre） | 开 | |
| 把媒体类型与系列写入标签 | 开 | |
| 把导演加入演职人员 | 关 | 关闭时演职人员只保留演员。 |
| 最多提供的预览图张数 | `50` | 提供的每一张都会出现在手动选图界面，实际保存几张由媒体库的图片数量上限决定。填 `0` 表示不限。 |
| 查询演员头像 | 开 | 数据源只为少数演员提供头像，见下方说明。 |
| 演员索引缓存 | `7` 天 | 重建一次索引约需 14 次请求。 |

## 使用提醒

- **建议开启「替换所有图像」刷新** — 首次刮削某个条目时，在刷新元数据时勾选替换图片，确保三种图片类型都写入。
- **手动选图能看到全部图片** — 自动刮削每种类型只取一张（封面取正面裁切图，背景与缩略图取套图展开图），但手动修改图片时，插件提供的所有图片都会列出来供你挑选。
- **部分作品没有预览图** — 未发售（预约）商品通常只有套图，没有预览图集，这时背景与缩略图会使用套图。
- **多数演员没有头像** — 数据源只为极少数演员提供头像，查不到属于正常现象，不是插件故障。
- **元数据是日文** — 保持数据源原始发布内容，不做翻译。

排查问题时可打开 **控制台 → 日志** 搜索 `GravureX`；若需更详细的图片记录，把 `Jellyfin.Plugin.GravureX` 的日志级别调为 `Debug`。

实现细节（图片地址规律、字段映射等）见 [docs/data-source.md](docs/data-source.md)。

## 许可

[MIT](LICENSE)

---

<h2 id="english">English</h2>

A Jellyfin metadata and artwork provider for Japanese DVD and Blu-ray releases, with a
focus on the gravure idol category. Data is read from the public
[DMM](https://www.dmm.com/mono/dvd/) storefront — no API key or login — and kept in
Japanese as published.

### Features

- **Metadata** — title, overview, release date, runtime, maker, genres, JAN barcode,
  rating, performers and director.
- **Content number in the title** — results read as `AB123-4567 タイトル`. The number
  comes from the file name, so it keeps whichever hyphen position you used.
- **All three image types** — poster, backdrop and thumbnail are always supplied.
- **Performers** — canonical name, kana reading and actor id, plus a portrait where the
  source publishes one.
- **Lenient matching** — `AB123-4567`, `ab1234567`, `AB-1234567` and `n_1234abcd5678`
  all resolve; hyphen position and zero padding do not matter.
- **Network control** — HTTP proxy, request throttling, timeout and page caching, with
  defaults that are gentle on the source.

### Requirements

| | |
|---|---|
| Jellyfin | 10.11.x |
| Runtime | .NET 9.0 (bundled with Jellyfin 10.11) |
| Network | Access to `dmm.com` and `pics.dmm.com`. Set the HTTP proxy in the plugin settings if your server cannot reach them directly. |

### Installation

**From the plugin repository (recommended)**

1. Open **Dashboard → Plugins → Repositories** and add:

   ```
   https://raw.githubusercontent.com/sdtafxy/jellyfin-plugin-gravurex/main/manifest.json
   ```

2. Open the **Catalog** tab, find **GravureX** under *Metadata* and install it.
3. Restart Jellyfin.
4. Open **Dashboard → Plugins → GravureX** and configure it.
5. In your movie library, enable **GravureX** under metadata downloaders and image
   fetchers.

**Manual install** — download `jellyfin-plugin-gravurex-*.zip` from
[Releases](https://github.com/sdtafxy/jellyfin-plugin-gravurex/releases), extract it into
the Jellyfin `plugins` directory as `plugins/Jellyfin.Plugin.GravureX/`, then restart.

### File naming

| File name | Matched as |
|---|---|
| `AB123-4567.mp4` | content number `AB123-4567` |
| `ab1234567.mp4` | content number `ab1234567` |
| `AB-1234567.mp4` | content number `AB-1234567` |
| `n_1234abcd5678.mp4` | content id, fetched directly |
| `[AB123-4567] タイトル.mp4` | content number `AB123-4567` |

The content number or the full content id is read from the file name, falling back to the
containing folder name. Hyphens and zero padding are ignored while matching. When several
editions exist, the standard edition is preferred.

### Settings

| Setting | Default | Description |
|---|---|---|
| HTTP proxy | *(empty)* | e.g. `http://127.0.0.1:7890`. |
| User agent | *(bundled)* | Empty uses the built-in default. |
| Minimum delay between requests | `1500` ms | Higher values are gentler on the source. |
| Request timeout | `30` s | |
| Cache duration | `1440` min | `0` disables the cache. |
| Resolve content numbers via search | on | Required to match content numbers. |
| Put the content number in front of the title | on | |
| Prefer the standard edition | on | |
| Store the JAN barcode | on | |
| Import genres | on | |
| Add media type and series as tags | on | |
| Add the director to the people | *off* | Off keeps the people list to the performers only. |
| Maximum preview images to offer | `50` | `0` means no limit. |
| Look up performer portraits | on | Only a small subset of performers has a portrait. |
| Performer index cache | `7` days | Rebuilding costs about fourteen requests. |

### Notes

- Refresh with **Replace all images** on the first scrape so all three image types are
  written. An automatic scan takes one image per type; the manual picker lists every
  image the plugin offers.
- Pre-order titles often have no preview images, so the backdrop and thumbnail fall back
  to the package spread.
- Most performers have no portrait — that is a limitation of the source.
- Metadata is kept in Japanese as published.

Implementation details are in [docs/data-source.md](docs/data-source.md).

### Licence

[MIT](LICENSE)
