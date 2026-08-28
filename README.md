# playnite-galgame-metadata

A unified Galgame & Visual Novel metadata provider for [Playnite](https://playnite.link/), seamlessly fusing self-hosted **NocoDB**, **VNDB**, **ErogameScape** (via Wayback Machine / Tavily), **DLsite**, and **DMM/FANZA**.

## ⚠️ 2.0.0：多來源架構（EGS 本體に一切アクセスしません）

批評空間（ErogameScape）は未許可 IP からのアクセスを silent drop でブロックするため、本フォークでは **EGS サーバへ直接リクエストを送らず**、VNDB / NocoDB / Wayback Machine / DLsite を融合したハイブリッド構造に全面刷新しています：

| 層 | 1.x（オリジナル） | 2.0.0（このフォーク） |
|---|---|---|
| 私有庫整合 | なし | **自建 NocoDB（繁中標籤／封面圖／即時評分最優先）** |
| 検索 | EGS SQL API（POST） | **VNDB kana API v2** |
| EGS id 解決 | SQL 結果 | **VNDB release extlinks** |
| EGS スコア | SQL 結果（live） | **NocoDB 快取** → **Wayback 快照** → **Tavily Extract**（任意） |
| スコアのフォールバック | なし | VNDB rating（EGS データ皆無の場合） |
| タグ | EGS POV | **NocoDB 精準繁中標籤**（優先） / VNDB content tags |
| カバー画像 | VNDB / DMM | **NocoDB 封面** → VNDB 縦長 → DMM パッケージ → VNDB 橫長 |
| あらすじ補完 | DLsite / Getchu | **DLsite API** → **Getchu** → **VNDB（英語 BBCode 清理）** |

### 設定（`config.json`）

`%AppData%\Playnite\ExtensionsData\b8e3f2a1-5c4d-4e6f-9a1b-2d3e4f5a6b7c\config.json`：

```json
{
  "TavilyApiKey": "",
  "PreferTavily": false,
  "NocoDbBaseUrl": "https://nocodb.your-domain.com",
  "NocoDbApiToken": "your_nocodb_token",
  "NocoDbGamesTableId": "your_games_table_id",
  "NocoDbGenreLinkId": "your_genre_link_id",
  "NocoDbAttrLinkId": "your_attr_link_id",
  "PreferNocoDbTags": true,
  "NocoDbIgnoreSslErrors": false,
  "FallbackNocoDbScores": true
}
```

- **NocoDB**: 有設定時，優先以此庫的封面圖、繁體中文標籤與社群評分入庫（跳過外網延遲）。
- **TavilyApiKey**: 空白 = 使用 Wayback Machine（免費）。設定後可在 Wayback 無快照時即時爬取 EGS 分數。
- **PreferTavily**: `true` = 優先使用 Tavily 即時分數。

### 制限

- Wayback 経由のスコアは快照時点の値（ログに快照日付を出力）
- VNDB に登録が無い作品は検索でヒットしない（同人作品の一部）

---

以下はオリジナル（1.x）の README。データソース表の EGS SQL 関連の記述は 2.0.0 では上記のとおり置き換え済み。

## 機能

批評空間のデータベースと外部APIから以下のメタデータを取得できます：

- **ゲーム名**
- **開発元** / **発売元**（ブランド名）
- **発売日**
- **評価スコア**（中央値をCommunity Scoreとして使用）
- **カバー画像**（VNDB / DMMフォールバック）
- **背景画像**（VNDBスクリーンショット、SFWフィルタリング済み）
- **説明文**（DLsite API / Getchu / VNDBフォールバック）
- **ジャンル**（DLsiteジャンルタグ / 公式ジャンル）
- **タグ**（POVデータ：ジャンル・背景・傾向カテゴリ）
- **シリーズ**（ゲームグループ）
- **特徴**（属性データ）
- **リンク**（批評空間・公式サイト・DLsite・DMM）
- **年齢レーティング** / **プラットフォーム** / **リージョン**

## データソース

| データ | 主要ソース | フォールバック |
|---|---|---|
| ゲーム情報・タグ・シリーズ・特徴 | 批評空間 SQL API | — |
| カバー画像 | VNDB | DMM |
| 背景画像 | VNDB（SFWスクリーンショット） | — |
| 説明文 | DLsite API | Getchu / VNDB |
| ジャンル | DLsite API | 批評空間公式ジャンル |

## インストール

1. [Releases](https://github.com/jayhormes/playnite-galgame-metadata/releases) ページから最新の `.pext` ファイルをダウンロード
2. ダウンロードした `.pext` ファイルをダブルクリック、またはPlayniteにドラッグ＆ドロップ
3. Playniteを再起動

## 使い方

1. ライブラリ内のゲームを右クリック →「メタデータを編集」→「ダウンロード」
2. メタデータソースとして「ErogameScape」を選択
3. ゲーム名で検索し、該当するタイトルを選択

自動メタデータダウンロード（ライブラリ → メタデータをダウンロード）にも対応しています。ゲーム名が完全一致する場合に自動的にメタデータを取得します。

## スクリーンショット

### ゲーム検索
![ゲーム検索](docs/screenshots/search.png)

### メタデータ取得結果
![メタデータ取得結果](docs/screenshots/metadata.png)

### カバー画像・背景画像
![カバー画像・背景画像](docs/screenshots/media.png)

## ビルド方法

### 前提条件

- .NET SDK（net462 をターゲット）
- Visual Studio 2022 または `dotnet` CLI

### ビルド

```bash
dotnet build -c Release
```

### .pext パッケージ作成

ビルド出力ディレクトリ（`bin/Release/`）内のファイルを ZIP 圧縮し、拡張子を `.pext` に変更してください。パッケージには以下のファイルを含めます：

- `ErogameScapeMetadata.dll`
- `HtmlAgilityPack.dll`
- `extension.yaml`

## 依存ライブラリ

- [PlayniteSDK](https://github.com/JosefNemworthy/Playnite) 6.11.0
- [HtmlAgilityPack](https://html-agility-pack.net/) 1.11.54

## ライセンス

[MIT License](LICENSE)
