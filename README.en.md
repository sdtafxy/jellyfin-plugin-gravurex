<div align="center">

<img src="assets/logo.png" width="560" alt="GravureX">

**Teach Jellyfin your content numbers**

Titles, performers, posters and preview images, in one scrape. No API key, no login, nothing else to set up.

[![Jellyfin](https://img.shields.io/badge/Jellyfin-10.11.x-00A4DC?style=for-the-badge&logo=jellyfin&logoColor=white)](https://jellyfin.org)
[![.NET](https://img.shields.io/badge/.NET-9.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com)
[![C#](https://img.shields.io/badge/C%23-239120?style=for-the-badge&logo=csharp&logoColor=white)](https://learn.microsoft.com/dotnet/csharp/)
[![License](https://img.shields.io/badge/License-MIT-22C55E?style=for-the-badge&logo=opensourceinitiative&logoColor=white)](LICENSE)
[![Release](https://img.shields.io/github/v/release/sdtafxy/jellyfin-plugin-gravurex?style=for-the-badge&label=Release&color=FF6B6B)](https://github.com/sdtafxy/jellyfin-plugin-gravurex/releases)
[![Build](https://img.shields.io/github/actions/workflow/status/sdtafxy/jellyfin-plugin-gravurex/build.yml?style=for-the-badge&label=Build&logo=githubactions&logoColor=white)](https://github.com/sdtafxy/jellyfin-plugin-gravurex/actions)
[![Downloads](https://img.shields.io/github/downloads/sdtafxy/jellyfin-plugin-gravurex/latest/total?style=for-the-badge&label=Downloads&logo=github&logoColor=white&color=0EA5E9)](https://github.com/sdtafxy/jellyfin-plugin-gravurex/releases)
[![Last commit](https://img.shields.io/github/last-commit/sdtafxy/jellyfin-plugin-gravurex?style=for-the-badge&label=Last%20commit&logo=git&logoColor=white&color=8B5CF6)](https://github.com/sdtafxy/jellyfin-plugin-gravurex/commits/main)
[![Metadata](https://img.shields.io/badge/Metadata-Japanese-EF4444?style=for-the-badge)](https://github.com/sdtafxy/jellyfin-plugin-gravurex)
[![API key](https://img.shields.io/badge/API%20key-not%20required-16A34A?style=for-the-badge)](https://github.com/sdtafxy/jellyfin-plugin-gravurex)

[简体中文](README.md) · **English**

</div>

## How it works

A file named `AB123-4567.mp4` is all it needs. The plugin looks that number up, finds the
matching product, and writes the title as `AB123-4567 タイトル` — then fills in the
performers, release date, runtime, studio and artwork.

One number often maps to several editions: plain, Blu-ray, and limited or bonus ones. An
automatic scan takes the plainest; a manual search lets you pick whichever you want.

## What it does

- **Metadata**: title, overview, release date, runtime, studio, genres, JAN barcode, rating, performers
- **Content number in the title**: `AB123-4567 タイトル`, taken from the file name with your hyphen position kept
- **All three image types**: poster, backdrop and thumbnail, with no empty slots
- **Performers**: canonical name, kana reading and actor id, plus a portrait when the source has one
- **Lenient matching**: `AB123-4567`, `ab1234567`, `AB-1234567` and `n_1234abcd5678` all work
- **Network control**: proxy, throttling, timeout and caching, with defaults that are gentle on the source

## Before you start

- Jellyfin 10.11.x
- .NET 9.0 — bundled with Jellyfin 10.11, nothing to install
- The server needs to reach `dmm.com` and `pics.dmm.com`; if it can't, set a proxy in the plugin settings

## Install

1. Open **Dashboard → Plugins → Repositories** and add:

   ```
   https://raw.githubusercontent.com/sdtafxy/jellyfin-plugin-gravurex/main/manifest.json
   ```

2. Go to the **Catalog** tab, find **GravureX** under Metadata and install it
3. Restart Jellyfin
4. Open **Dashboard → Plugins → GravureX** and configure it
5. In your movie library, tick **GravureX** under metadata downloaders and image fetchers

New versions show up in the catalog, so updating is a click.

<details>
<summary>Install without the plugin repository</summary>

Grab the zip from [Releases](https://github.com/sdtafxy/jellyfin-plugin-gravurex/releases),
extract it into `plugins/Jellyfin.Plugin.GravureX/`, and restart Jellyfin.
</details>

<details>
<summary>Build it yourself</summary>

```bash
dotnet publish Jellyfin.Plugin.GravureX/Jellyfin.Plugin.GravureX.csproj \
  -c Release -o ./artifacts
```

Then drop the contents into `plugins/Jellyfin.Plugin.GravureX/`.
</details>

## File names

The content number is read from the file name, falling back to the folder above it.

```
AB123-4567.mp4          →  AB123-4567
ab1234567.mp4           →  ab1234567
AB-1234567.mp4          →  AB-1234567
[AB123-4567] title.mp4  →  AB123-4567
n_1234abcd5678.mp4      →  full content id, fetched directly
```

Hyphens can sit anywhere. Zero padding differs per studio, and that is handled too.

## Settings

All under **Dashboard → Plugins → GravureX**.

**Network**

- **HTTP proxy** — set it when the server can't reach the source, e.g. `http://127.0.0.1:7890`
- **User agent** — leave empty for the built-in one
- **Request interval** — 1500 ms by default. Don't go too low; be nice to the source
- **Request timeout** — 30 s by default
- **Cache duration** — 1440 minutes by default, `0` turns caching off

**Numbers and editions**

- **Resolve content numbers via search** — on by default. Turn it off and file names have to carry the full content id
- **Content number in front of the title** — on by default
- **Prefer the standard edition** — on by default. Turn it off and a manual search lists every edition; an automatic scan still takes the plain one

**Metadata**

- **Import genres**, **series as a tag** — both on
- **Store the JAN barcode** — on
- **Add the director to the people** — off
- **Maximum preview images** — 50 by default, `0` for no limit

**Performers**

- **Look up performer portraits** — on
- **Performer index cache** — 7 days; a rebuild costs about fourteen requests

## Two things worth knowing

> [!TIP]
> On the first scrape, refresh metadata with **Replace all images**. An automatic scan takes one image per type, so this is what writes all three.

> [!NOTE]
> When you change images by hand, every image the plugin offers is listed, including the full preview set.

## What it can't do

All of these are limits of the source, not bugs.

- **Pre-orders have no preview gallery**, so the backdrop and thumbnail fall back to the package image
- **Most performers have no portrait** — the source publishes them for a small subset only
- **No biography or birthday** — the source doesn't have them
- **Metadata stays in Japanese**, untranslated

## If something looks wrong

Open **Dashboard → Logs** and search for `GravureX` to see what each scrape offered. If that
doesn't explain it, set the log level to `Debug` and every image URL is printed.

Implementation details (image URL patterns, field mapping) are in [docs/data-source.md](docs/data-source.md).

## Licence

MIT
