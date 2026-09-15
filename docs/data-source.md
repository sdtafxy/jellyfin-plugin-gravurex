# Data source reference

Everything the plugin reads comes from public DMM mono (DVD / Blu-ray) pages.
There is no API key, login or cookie involved — this category is not age restricted.

Base URL: `https://www.dmm.com/mono/dvd/`

| Purpose | Path |
|---|---|
| Product detail | `/-/detail/=/cid={contentId}/` |
| Keyword search | `/-/search/=/searchstr={query}/` |
| Performer listing | `/-/list/=/article=actor/id={actorId}/` |

## Product detail page

A detail page carries three independent sources of information. The plugin reads
all three and fills each field from the most reliable one.

### 1. JSON-LD (`<script type="application/ld+json">`)

| Field | Notes |
|---|---|
| `sku`, `productID` | DMM content id, e.g. `n_1234abcd5678` |
| `name` | Full product title |
| `description` | Overview |
| `gtin13` | JAN / EAN-13 barcode |
| `brand.name` | Maker |
| `image[0]` | Large package image |
| `aggregateRating.ratingValue` | Present once reviews exist |

### 2. Information table

Rows are `<td class="nw">LABEL：</td><td>VALUE</td>` pairs.

| Label | Notes |
|---|---|
| 発売日 | `yyyy/MM/dd` |
| 収録時間 | `N分` |
| 出演者 | Links carry the performer id: `/article=actor/id={id}/` |
| 監督 | Frequently empty for gravure releases |
| シリーズ | Frequently empty |
| メーカー | Maker |
| ジャンル | One or more keyword links |
| メディア | `DVD` or `Blu-ray`. Only some pages publish it, so it is read on a best effort basis |
| 品番 | Content id. Used only when the JSON-LD block did not carry one |

Empty fields are rendered as `----` and are skipped.

### 3. Images

| Kind | Location | Size |
|---|---|---|
| Package spread | `<meta property="og:image">`, `.../{cid}pl.jpg` | 800×536 |
| Package thumbnail | `.../{cid}ps.jpg` | 147×200 |
| Preview thumbnail | `data-lazy` of `.layout-sampleImage__item`, plus a text scan of the response | 120×90 |
| Preview full size | the same URL with `jp-` before the number | 800×450 |

`pl.jpg` is not the front cover. It is the whole package laid flat: back cover on the
left, spine in the middle, front cover on the right. Measured on a released title, the
front cover starts at about 53% of the width — cropping at exactly half leaves a strip
of spine along the left edge. No larger front-cover-only image exists, so the poster is
produced by cropping `pl.jpg` at download time, which is why the plugin carries an
image library.

Preview URLs are collected twice over. The container markup has not been stable between
titles and one title yielded none from it at all, so the response text is also scanned for
`/digital/video/{id}/{id}-{n}.jpg`. Both lists are merged and ordered by image number.

Preview URLs point at 120×90 thumbnails. The full size twin is obtained by inserting
`jp-` before the trailing number, so `{id}-3.jpg` becomes `{id}jp-3.jpg`. The full size
variant is not mentioned anywhere on the page and is therefore derived. A request for a
missing image is answered with a redirect to a shared placeholder and an HTTP 200,
which is why the plugin inspects the final URL rather than the status code.

Preview images are also not uniform in shape. Measured on one title, seven of nine were
534×800 portrait and two were 800×534 landscape, so the set cannot be assumed to be 16:9
and the plugin does not report dimensions for them (see the note in `ImageProvider`).

Gallery images live under `https://pics.dmm.com/digital/video/{digitalCid}/`. The
digital content id cannot be derived from the mono content id, so those URLs are always
taken from the page. Pre-order titles generally have no gallery yet.

## Search results page

Each entry is a `<li>` holding the detail link, the title in `span.txt`, the package
thumbnail, and the release date printed as `発売日：yyyy/MM/dd`:

```html
<li>
  <a href=".../detail/=/cid={contentId}/"><span class="img"><img src=".../{cid}ps.jpg"></span>
    <span class="txt">TITLE</span></a>
  <p class="rate">発売日：2026/09/30</p>
</li>
```

The listing only publishes the 147×200 package thumbnail, so the plugin swaps the trailing
`ps.jpg` for `pl.jpg` to get the 800×536 spread for the search result image.

## Content numbers

DMM content ids embed a maker specific prefix, so a content number cannot be
turned into a detail URL directly:

```
AB123-4567   ->   n_1234ab1234567
AB-123456    ->   n_1234ab0123456
```

DMM only recognises the hyphen in its own position, so the plugin searches with
the separators removed and matches the results back against the normalised
number. Because DMM zero-pads the numeric part differently per maker, padded
variants are tried as well. If the normalised query returns nothing, the raw
number is tried once as a fallback.

Multiple editions share one content number and are distinguished by a suffix:

| Suffix | Meaning |
|---|---|
| *(none)* | Standard edition |
| `tk` | Limited / bonus edition |

When **Prefer the standard edition** is enabled, the plain release wins.

## Performers

| Purpose | Path |
|---|---|
| Actor listing | `/-/list/=/article=actor/id={actorId}/` |
| Gravure idol index | `/-/idol/=/keyword={kana}/` |

The actor listing page carries the canonical name and the kana reading in its
title (`名前(よみ)`). It contains no portrait and no biography.

Portraits live only on the gravure idol index, which is split by the first kana of
the reading (`a`, `i`, `u`, `e`, `o`, `ka`, `sa`, `ta`, `na`, `ha`, `ma`, `ya`,
`ra`, `wa`). Most entries point at a shared placeholder named `printing`; DMM
serves that placeholder with HTTP 200, so it has to be filtered out by URL rather
than by status code. At the time of writing the index held about 1300 performers,
of which roughly 40 had a real portrait.

## Field mapping

| Jellyfin field | Source |
|---|---|
| Name | Title with the trailing `/performer` segment removed |
| OriginalTitle | Title as published |
| Overview | `description` |
| PremiereDate, ProductionYear | 発売日 |
| RunTimeTicks | 収録時間 |
| Studios | メーカー |
| Genres | ジャンル |
| Tags | シリーズ, and メディア when the page publishes it |
| CommunityRating | `aggregateRating` |
| People | 出演者, 監督 |
| ProviderIds | `GravureX` (content id), `GravureXJan` (barcode) |
| Primary image | Package image |
| Backdrops | Gallery images |

## Rate limiting

Pages are throttled (1.5 s between requests by default) and cached in memory
for 24 hours by default. Both are configurable. `robots.txt` on dmm.com
disallows the search path, so keep the delay conservative or switch to full
content ids in file names.
