# playnite-galgame-metadata

A metadata provider for [Playnite](https://playnite.link/) aimed at Japanese visual novels. It pulls from VNDB, ErogameScape, DLsite, Getchu and DMM, and can put your own self-hosted [NocoDB](https://nocodb.com/) collection first.

Forked from [minuspiral/playnite-with-erogamescape](https://github.com/minuspiral/playnite-with-erogamescape).

## It never talks to ErogameScape

ErogameScape silently drops traffic from IPs that are not on its allow list, and automated scraping is a good way to get an allow-listed IP blocked again. This plugin therefore **never sends a request to erogamescape.dyndns.org**. EGS scores come from your NocoDB cache, from Wayback Machine snapshots, or through Tavily's Extract API — all of which fetch on their own infrastructure.

## Where each field comes from

| Field | Source | Fallback |
|---|---|---|
| Search | VNDB kana API | — |
| Name, developer, release date | NocoDB | VNDB |
| Community score | NocoDB (`EGS 評分`) | Wayback snapshot → Tavily → VNDB rating |
| Tags | NocoDB linked tags | VNDB content tags |
| Cover | NocoDB attachment | VNDB (portrait) → DMM package shot → VNDB |
| Background | VNDB screenshots (SFW only) | — |
| Description | DLsite API | Getchu → VNDB |
| Genres | DLsite | — |
| Age rating | VNDB release `minage` | — |
| Links | EGS, VNDB, DLsite, DMM | — |

EGS, DLsite and DMM ids are resolved from VNDB release extlinks, so a title has to exist on VNDB to be found. Doujin releases that VNDB does not carry will not show up in search.

## Install

1. Grab `GalgameMetadata.pext` from [Releases](https://github.com/jayhormes/playnite-galgame-metadata/releases)
2. Double-click it, or drag it onto the Playnite window
3. Restart Playnite

## Use

Right-click a game → **Edit** → **Download metadata**, then pick **Galgame Metadata** as the source. Search by the Japanese title. Automatic downloads work too, and match on an exact (normalised) title.

## Configuration

There is no settings UI. Edit `%AppData%\Playnite\ExtensionsData\e6ab0c61-8c40-4e4b-842b-08cd132c09e4\config.json` and restart Playnite.

```json
{
  "TavilyApiKey": "",
  "PreferTavily": false,
  "NocoDbBaseUrl": "",
  "NocoDbApiToken": "",
  "NocoDbGamesTableId": "",
  "NocoDbGenreLinkId": "",
  "NocoDbAttrLinkId": "",
  "PreferNocoDbTags": true,
  "NocoDbMaxTags": 30,
  "NocoDbIgnoreSslErrors": false,
  "FallbackNocoDbScores": true
}
```

| Key | Default | What it does |
|---|---|---|
| `TavilyApiKey` | empty | Empty means Wayback only. Set it to also fetch live EGS scores when no snapshot exists. |
| `PreferTavily` | `false` | `true` tries Tavily first and falls back to Wayback. |
| `NocoDbBaseUrl`, `NocoDbApiToken`, `NocoDbGamesTableId` | empty | All three are required to enable the NocoDB lookup. |
| `NocoDbGenreLinkId`, `NocoDbAttrLinkId` | empty | Column ids (they start with `c`) of the Links fields holding your tags. |
| `PreferNocoDbTags` | `true` | `true` uses NocoDB tags only; `false` appends VNDB tags after them. |
| `NocoDbMaxTags` | `30` | Caps how many tags get written. `0` means no cap. |
| `NocoDbIgnoreSslErrors` | `false` | For a NocoDB behind a self-signed certificate. Only that one host skips validation; everything else in Playnite still verifies normally. |
| `FallbackNocoDbScores` | `true` | `true` uses the scores already stored in NocoDB; `false` ignores them and always goes to Wayback/Tavily. |

To find the Links column ids, call `GET {NocoDbBaseUrl}/api/v2/meta/tables/{tableId}` and look for the columns with `"uidt": "Links"`.

### Expected NocoDB columns

The games table is read with these column names:

`Id`, `Name`, `原名`, `發售日期`, `EGS 評分`, `EGS 票數`, `vndb 評分`, `vndb 票數`, `封面圖` (attachment), `LTAR_開發品牌` (link)

Records are matched in this order: `vndb URL` exact, then `EGS URL` ending in `game=<id>`, then `Name` exact.

## Screenshots

![Search](docs/screenshots/search.png)
![Metadata](docs/screenshots/metadata.png)
![Cover and background](docs/screenshots/media.png)

## Build

Requires the .NET Framework 4.6.2 targeting pack, so Windows.

```
dotnet build GalgameMetadata.csproj -c Release
dotnet test GalgameMetadata.Tests/GalgameMetadata.Tests.csproj -c Release
```

CI builds and tests on every push and attaches the `.pext` to the run. Pushing a `v*` tag publishes a release.

## Dependencies

- [PlayniteSDK](https://github.com/JosefNemec/Playnite) 6.11.0
- [HtmlAgilityPack](https://html-agility-pack.net/) 1.11.54

## License

[MIT](LICENSE)
