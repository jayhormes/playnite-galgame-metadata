using ErogameScapeMetadata.Models;
using HtmlAgilityPack;
using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ErogameScapeMetadata.Services
{
    // 2.0.0 リファクタ：EGS 本体（SQL API 含む）へは一切アクセスしない。
    //   検索         → VNDB kana API
    //   EGS id 解決  → VNDB release extlinks（DLsite/DMM id も同時に取得）
    //   EGS スコア   → Wayback 快照 → Tavily Extract（EgsStatsClient）
    //   補完         → DLsite API / Getchu / VNDB（従来どおり、これらは EGS ではない）
    public class ErogameScapeApiClient
    {
        private static readonly HttpClient HttpClient;

        private const int TbaYearThreshold = 2030;
        private const int MaxVndbTags = 15;

        private readonly ILogger _logger;
        private readonly VndbClient _vndb;
        private readonly EgsStatsClient _egsStats;
        private readonly NocoDbClient _nocoDb;
        private readonly bool _preferNocoDbTags;
        private readonly bool _useNocoDbScores;
        private readonly int _nocoDbMaxTags;

        static ErogameScapeApiClient()
        {
            HttpClient = new HttpClient();
            HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            HttpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        public ErogameScapeApiClient(ILogger logger, PluginConfig config)
        {
            _logger = logger;
            _vndb = new VndbClient(HttpClient, logger);
            _egsStats = new EgsStatsClient(HttpClient, logger,
                config?.TavilyApiKey, config?.PreferTavily ?? false);
            _nocoDb = new NocoDbClient(HttpClient, logger,
                config?.NocoDbBaseUrl, config?.NocoDbApiToken,
                config?.NocoDbGamesTableId, config?.NocoDbGenreLinkId, config?.NocoDbAttrLinkId,
                config?.NocoDbIgnoreSslErrors ?? false);
            _preferNocoDbTags = config?.PreferNocoDbTags ?? true;
            _useNocoDbScores = config?.FallbackNocoDbScores ?? true;
            _nocoDbMaxTags = config?.NocoDbMaxTags ?? PluginConfig.DefaultNocoDbMaxTags;
        }

        public async Task<List<ErogameScapeGameInfo>> SearchGamesAsync(
            string keyword, CancellationToken ct = default)
        {
            var vns = await _vndb.SearchAsync(keyword, ct);
            return vns.Select(v => new ErogameScapeGameInfo
            {
                VndbId = v.Id,
                GameName = v.DisplayName,
                AltName = string.IsNullOrEmpty(v.AltTitle) ? null : v.Title,
                BrandName = v.Developers?.FirstOrDefault()?.DisplayName,
                SellDay = ParseDate(v.Released),
            }).ToList();
        }

        public async Task<ErogameScapeGameInfo> GetGameDetailsAsync(
            ErogameScapeGameInfo stub, CancellationToken ct = default)
        {
            if (stub?.VndbId == null)
                return null;

            // VNDB 詳細と release extlinks を並列取得
            var detailTask = _vndb.GetDetailAsync(stub.VndbId, ct);
            var releasesTask = _vndb.GetReleasesAsync(stub.VndbId, ct);
            await Task.WhenAll(detailTask, releasesTask);

            var vn = detailTask.Result;
            if (vn == null)
                return null;

            var game = new ErogameScapeGameInfo
            {
                VndbId = vn.Id,
                GameName = vn.DisplayName,
                AltName = string.IsNullOrEmpty(vn.AltTitle) ? null : vn.Title,
                BrandName = vn.Developers?.FirstOrDefault()?.DisplayName,
                SellDay = ParseDate(vn.Released),
                VndbRating = vn.Rating,
            };

            ApplyExtLinks(game, releasesTask.Result);
            ApplyVndbImages(game, vn);
            ApplyVndbTags(game, vn);

            // NocoDB 整合（最優先：若存在 NocoDB 記錄，以 NocoDB 的封面圖、標籤、評分與品牌為主）
            if (_nocoDb.IsConfigured)
            {
                var nocoData = await _nocoDb.GetGameDataAsync(game.VndbId, game.EgsId, game.GameName, ct);
                ApplyNocoDbData(game, nocoData);
            }

            // EGS スコア（NocoDB にスコアが無い場合のみ Wayback → Tavily を実行）
            if (!game.Median.HasValue)
            {
                if (game.EgsId.HasValue)
                {
                    var stats = await _egsStats.GetStatsAsync(game.EgsId.Value, ct);
                    if (stats != null)
                    {
                        game.Median = stats.Median;
                        game.ReviewCount = stats.Count;
                        game.EgsSource = stats.Source;
                        game.EgsSnapshotDate = stats.SnapshotDate;
                    }
                }
                else
                {
                    _logger.Info($"VNDB {vn.Id} に EGS リンクなし（スコアは VNDB rating フォールバック）");
                }
            }

            // あらすじのフォールバック: DLsite → Getchu(日本語) → VNDB(英語)
            await EnrichFromDlsiteAsync(game, ct);
            if (string.IsNullOrEmpty(game.Description))
            {
                await EnrichDescriptionFromGetchuAsync(game, ct);
            }
            if (string.IsNullOrEmpty(game.Description) && !string.IsNullOrEmpty(vn.Description))
            {
                // VNDB の BBCode タグを除去
                game.Description = Regex.Replace(vn.Description, @"\[/?[a-z]+(?:=[^\]]+)?\]", "").Trim();
            }

            return game;
        }

        /// <summary>
        /// NocoDB（自建庫）の値を優先適用する。HTTP を伴わない純粋なマージ処理。
        /// </summary>
        internal void ApplyNocoDbData(ErogameScapeGameInfo game, NocoDbGameData noco)
        {
            if (game == null || noco == null)
                return;

            // 1. 封面圖：NocoDB を最優先候補にする
            if (!string.IsNullOrEmpty(noco.CoverImageUrl))
            {
                game.NocoDbCoverImageUrl = noco.CoverImageUrl;
            }

            // 2. 標籤：PreferNocoDbTags = true なら NocoDB のみ、false なら NocoDB を先頭に VNDB を継ぎ足す
            game.Tags = MergeTags(noco.Tags, game.Tags, _preferNocoDbTags, _nocoDbMaxTags);

            // 3. EGS 分數：FallbackNocoDbScores = false なら NocoDB のキャッシュを使わず Wayback/Tavily を引く
            if (_useNocoDbScores && noco.EgsScore.HasValue && noco.EgsScore.Value > 0)
            {
                game.Median = noco.EgsScore.Value;
                game.ReviewCount = noco.EgsCount;
                game.EgsSource = "nocodb";
                game.EgsSnapshotDate = null;
            }

            // 4. VNDB 分數も NocoDB 側を優先（同じ 10-100 スケール）
            if (_useNocoDbScores && noco.VndbScore.HasValue && noco.VndbScore.Value > 0)
            {
                game.VndbRating = noco.VndbScore;
            }

            if (!string.IsNullOrEmpty(noco.BrandName))
            {
                game.BrandName = noco.BrandName;
            }

            if (noco.ReleaseDate.HasValue)
            {
                game.SellDay = noco.ReleaseDate.Value;
            }

            // 原名は検索候補の副題として使う（VNDB 側に別題が無い場合のみ）
            if (string.IsNullOrEmpty(game.AltName) && !string.IsNullOrWhiteSpace(noco.OriginalName))
            {
                game.AltName = noco.OriginalName.Trim();
            }
        }

        /// <summary>
        /// NocoDB タグと既存（VNDB）タグを結合する。重複除去のうえ maxTags 件で打ち切る。
        /// </summary>
        internal static List<string> MergeTags(
            List<string> nocoTags, List<string> existingTags, bool preferNoco, int maxTags)
        {
            var noco = nocoTags ?? new List<string>();
            var existing = existingTags ?? new List<string>();

            if (noco.Count == 0)
            {
                return existing;
            }

            var merged = new List<string>();
            NocoDbClient.AppendTags(merged, noco);

            if (!preferNoco)
            {
                NocoDbClient.AppendTags(merged, existing);
            }

            if (maxTags > 0 && merged.Count > maxTags)
            {
                merged = merged.GetRange(0, maxTags);
            }

            return merged;
        }

        internal void ApplyExtLinks(ErogameScapeGameInfo game, List<VndbRelease> releases)
        {
            if (releases == null)
                return;

            foreach (var release in releases)
            {
                if (release.MinAge.HasValue)
                {
                    game.MinAge = Math.Max(game.MinAge ?? 0, release.MinAge.Value);
                }

                if (release.ExtLinks == null)
                    continue;

                foreach (var link in release.ExtLinks)
                {
                    var url = link.Url ?? "";

                    if (game.EgsId == null)
                    {
                        var egs = Regex.Match(url, @"game\.php\?.*[?&]game=(\d+)|game\.php\?game=(\d+)|\bgame=(\d+)");
                        var egsIdStr = egs.Groups[1].Success ? egs.Groups[1].Value
                            : egs.Groups[2].Success ? egs.Groups[2].Value
                            : egs.Groups[3].Value;
                        if (!string.IsNullOrEmpty(egsIdStr) && int.TryParse(egsIdStr, out var egsId))
                        {
                            game.EgsId = egsId;
                        }
                    }

                    if (game.DlsiteId == null)
                    {
                        var dlsite = Regex.Match(url, @"dlsite\.com/([a-zA-Z-]+)/.*?product_id/([a-zA-Z0-9_]+)", RegexOptions.IgnoreCase);
                        if (dlsite.Success)
                        {
                            game.DlsiteDomain = dlsite.Groups[1].Value.ToLowerInvariant();
                            game.DlsiteId = dlsite.Groups[2].Value.ToUpperInvariant();
                        }
                    }

                    if (game.DmmId == null)
                    {
                        var dmm = Regex.Match(url, @"dlsoft\.dmm\.co\.jp/detail/([a-zA-Z0-9_]+)|(?:dmm\.co\.jp|dmm\.com)/.*?cid=([a-zA-Z0-9_]+)|games\.dmm\.co\.jp/detail/([a-zA-Z0-9_]+)", RegexOptions.IgnoreCase);
                        if (dmm.Success)
                        {
                            var dmmIdStr = dmm.Groups[1].Success ? dmm.Groups[1].Value
                                : dmm.Groups[2].Success ? dmm.Groups[2].Value
                                : dmm.Groups[3].Value;
                            if (!string.IsNullOrEmpty(dmmIdStr))
                            {
                                game.DmmId = dmmIdStr;
                            }
                        }
                    }

                    if (game.GetchuId == null)
                    {
                        var getchu = Regex.Match(url, @"getchu\.com/soft\.phtml\?id=(\d+)", RegexOptions.IgnoreCase);
                        if (getchu.Success)
                        {
                            game.GetchuId = getchu.Groups[1].Value;
                        }
                    }
                }
            }
        }

        internal static void ApplyVndbImages(ErogameScapeGameInfo game, VndbVn vn)
        {
            if (vn.Image != null && vn.Image.IsSafe && !string.IsNullOrEmpty(vn.Image.Url))
            {
                game.VndbCoverImageUrl = vn.Image.Url;
                game.VndbCoverIsPortrait = vn.Image.IsPortrait;
            }

            if (vn.Screenshots == null)
                return;

            foreach (var shot in vn.Screenshots)
            {
                if (shot.IsSafe && !string.IsNullOrEmpty(shot.Url) && shot.Url != game.VndbCoverImageUrl)
                {
                    game.BackgroundImageUrls.Add(shot.Url);
                }
            }
        }

        internal static void ApplyVndbTags(ErogameScapeGameInfo game, VndbVn vn)
        {
            if (vn.Tags == null)
                return;

            var tags = vn.Tags
                .Where(t => t.Category == "cont"
                            && (t.Rating ?? 0) >= 2.0
                            && (t.Spoiler ?? 0) < 0.5
                            && !string.IsNullOrEmpty(t.Name))
                .OrderByDescending(t => t.Rating ?? 0)
                .Take(MaxVndbTags);

            foreach (var tag in tags)
            {
                game.Tags.Add(tag.Name);
            }
        }

        public async Task EnrichFromDlsiteAsync(
            ErogameScapeGameInfo game, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(game.DlsiteId))
                return;

            try
            {
                var apiUrl = $"https://www.dlsite.com/{game.DlsiteDomain ?? "pro"}/api/=/product.json?workno={game.DlsiteId}";
                _logger.Info($"DLsite API: {apiUrl}");

                using (var response = await HttpClient.GetAsync(apiUrl, ct))
                {
                    if (!response.IsSuccessStatusCode)
                        return;

                    var json = await response.Content.ReadAsStringAsync();
                    // JSON内の \/ エスケープを解除（URLやテキスト抽出を容易にする）
                    json = json.Replace("\\/", "/");

                    // intro_s（あらすじ短縮版）を取得し、HTML タグと実体参照を整形
                    var intro = ExtractJsonString(json, "intro_s");
                    if (!string.IsNullOrWhiteSpace(intro))
                    {
                        intro = Regex.Replace(intro, @"<br\s*/?>", "\n");
                        intro = Regex.Replace(intro, @"<[^>]+>", "");
                        intro = System.Net.WebUtility.HtmlDecode(intro).Trim();
                        game.Description = intro;
                    }

                    // ジャンルを取得: "genres":[{"name":"お姉さん",...},...]
                    var genreSection = Regex.Match(json, @"""genres""\s*:\s*\[(.*?)\]");
                    if (genreSection.Success)
                    {
                        var genreNameMatches = Regex.Matches(genreSection.Groups[1].Value,
                            @"""name""\s*:\s*""((?:[^""\\]|\\.)*)""");
                        foreach (Match gm in genreNameMatches)
                        {
                            var name = gm.Groups[1].Value;
                            name = Regex.Replace(name, @"\\u([0-9a-fA-F]{4})", m2 =>
                                ((char)int.Parse(m2.Groups[1].Value, NumberStyles.HexNumber)).ToString());
                            if (!string.IsNullOrEmpty(name))
                                game.Genres.Add(name);
                        }
                    }
                }
            }
            // HttpClientのタイムアウトもTaskCanceledExceptionを投げるため、
            // 実際にキャンセル要求があった場合のみ伝播させる
            catch (Exception ex) when (!(ex is OperationCanceledException) || !ct.IsCancellationRequested)
            {
                _logger.Warn($"DLsite API エラー ({game.DlsiteId}): {ex.Message}");
            }
        }

        public async Task EnrichDescriptionFromGetchuAsync(
            ErogameScapeGameInfo game, CancellationToken ct = default)
        {
            // 1. VNDB extlinks から既に Getchu ID が解決されている場合は直接あらすじを取得（検索不要）
            if (!string.IsNullOrEmpty(game.GetchuId))
            {
                try
                {
                    var desc = await FetchGetchuDescriptionAsync(game.GetchuId, ct);
                    if (!string.IsNullOrEmpty(desc))
                    {
                        game.Description = desc;
                        return;
                    }
                }
                catch (Exception ex) when (!(ex is OperationCanceledException) || !ct.IsCancellationRequested)
                {
                    _logger.Warn($"Getchu直接取得エラー ({game.GetchuId}): {ex.Message}");
                }
            }

            if (string.IsNullOrEmpty(game.GameName))
                return;

            // 2. Getchu ID が未解決の場合はゲーム名で検索（キーワードはEUC-JPエンコード）
            try
            {
                var searchUrl = "https://www.getchu.com/php/nsearch.phtml"
                    + "?genre=pc_soft&search_type=match&search_keyword="
                    + EscapeEucJp(game.GameName);

                _logger.Info($"Getchu検索: {game.GameName}");

                using (var request = new HttpRequestMessage(HttpMethod.Get, searchUrl))
                {
                    request.Headers.Add("Cookie", "getchu_adalt_flag=getchu.com");
                    using (var response = await HttpClient.SendAsync(request, ct))
                    {
                        if (!response.IsSuccessStatusCode)
                            return;

                        var bytes = await response.Content.ReadAsByteArrayAsync();
                        var html = System.Text.Encoding.GetEncoding("EUC-JP").GetString(bytes);

                        // 検索結果からタイトル一致するGetchu IDを探す（複数候補）
                        var getchuIds = FindGetchuIdsByTitle(html, game.GameName);

                        // あらすじが見つかるまで候補を順に試す
                        foreach (var id in getchuIds)
                        {
                            var desc = await FetchGetchuDescriptionAsync(id, ct);
                            if (!string.IsNullOrEmpty(desc))
                            {
                                game.Description = desc;
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException) || !ct.IsCancellationRequested)
            {
                _logger.Warn($"Getchu検索エラー ({game.GameName}): {ex.Message}");
            }
        }

        internal List<string> FindGetchuIdsByTitle(string html, string gameName)
        {
            var ids = new List<string>();
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var normalized = NormalizeForComparison(gameName);

            // <a href="soft.phtml?id=XXXXX">タイトル</a> を探す（class 名の変更に耐えられるよう柔軟に一致）
            var links = doc.DocumentNode.SelectNodes("//a[contains(@href,'soft.phtml?id=')]");
            if (links == null)
                return ids;

            foreach (var link in links)
            {
                var title = HtmlEntity.DeEntitize(link.InnerText).Trim();
                var normalizedTitle = NormalizeForComparison(title);
                // 完全一致 or ゲーム名で始まるタイトル（エディション違い対応）
                if (normalizedTitle == normalized
                    || normalizedTitle.StartsWith(normalized + " "))
                {
                    var href = link.GetAttributeValue("href", "");
                    var idMatch = Regex.Match(href, @"id=(\d+)");
                    if (idMatch.Success && !ids.Contains(idMatch.Groups[1].Value))
                    {
                        _logger.Info($"Getchuタイトル候補: {title} (ID:{idMatch.Groups[1].Value})");
                        ids.Add(idMatch.Groups[1].Value);
                    }
                }
            }

            return ids;
        }

        private async Task<string> FetchGetchuDescriptionAsync(
            string getchuId, CancellationToken ct)
        {
            var url = $"https://www.getchu.com/soft.phtml?id={getchuId}";
            _logger.Info($"Getchuあらすじ取得: {url}");

            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                request.Headers.Add("Cookie", "getchu_adalt_flag=getchu.com");
                using (var response = await HttpClient.SendAsync(request, ct))
                {
                    if (!response.IsSuccessStatusCode)
                        return null;

                    var bytes = await response.Content.ReadAsByteArrayAsync();
                    var html = System.Text.Encoding.GetEncoding("EUC-JP").GetString(bytes);

                    var doc = new HtmlDocument();
                    doc.LoadHtml(html);

                    // 「ストーリー」セクションを探す（新しいゲーム）
                    var storyText = ExtractGetchuSection(doc, "ストーリー");
                    if (!string.IsNullOrEmpty(storyText))
                        return storyText;

                    // 「商品紹介」セクションを探す（古いゲーム）
                    return ExtractGetchuSection(doc, "商品紹介");
                }
            }
        }

        internal static string ExtractGetchuSection(HtmlDocument doc, string sectionTitle)
        {
            // <h2 class="tabletitle ...">セクション名</h2> の次の <div class="tablebody"> 内テキスト
            var headers = doc.DocumentNode.SelectNodes("//h2[contains(@class,'tabletitle')]");
            if (headers == null)
                return null;

            foreach (var header in headers)
            {
                if (!HtmlEntity.DeEntitize(header.InnerText).Trim().Contains(sectionTitle))
                    continue;

                // 次の兄弟要素で tablebody を持つ div を探す
                var sibling = header.NextSibling;
                while (sibling != null)
                {
                    if (sibling.Name == "div"
                        && sibling.GetAttributeValue("class", "").Contains("tablebody"))
                    {
                        // span.bootstrap 内のテキストを取得
                        var span = sibling.SelectSingleNode(".//span[@class='bootstrap']");
                        var node = span ?? sibling;

                        var text = HtmlEntity.DeEntitize(node.InnerHtml);
                        // HTMLタグを改行とテキストに変換
                        text = Regex.Replace(text, @"<br\s*/?>", "\n");
                        text = Regex.Replace(text, @"<[^>]+>", "");
                        text = HtmlEntity.DeEntitize(text).Trim();

                        if (!string.IsNullOrEmpty(text))
                            return text;
                    }
                    sibling = sibling.NextSibling;
                }
            }

            return null;
        }

        internal static string ExtractJsonString(string json, string key)
        {
            // "key":"value" or "key":null のパターンを抽出
            var pattern = $@"""{Regex.Escape(key)}""\s*:\s*""((?:[^""\\]|\\.)*)""";
            var match = Regex.Match(json, pattern);
            if (!match.Success)
                return null;

            var value = match.Groups[1].Value;
            // JSONのエスケープをデコード
            value = value.Replace("\\\"", "\"")
                         .Replace("\\\\", "\\")
                         .Replace("\\/", "/")
                         .Replace("\\n", "\n")
                         .Replace("\\r", "")
                         .Replace("\\t", "\t");
            // Unicodeエスケープをデコード
            value = Regex.Replace(value, @"\\u([0-9a-fA-F]{4})", m =>
                ((char)int.Parse(m.Groups[1].Value, NumberStyles.HexNumber)).ToString());

            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        /// <summary>
        /// タイトル比較用に全角英数字・記号・全角空白・波線記号を半角に正規化し、小文字化する。
        /// 批評空間「アマカノ2」とVNDB「アマカノ２」、全角スペース「　」の差異を吸収する。
        /// </summary>
        public static string NormalizeForComparison(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            var chars = value.Trim().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                var c = chars[i];
                // 全角英数字・記号（！-～, U+FF01-U+FF5E）を半角（!-~, U+0021-U+007E）に変換
                if (c >= '\uFF01' && c <= '\uFF5E')
                {
                    chars[i] = (char)(c - 0xFEE0);
                }
                // 全角スペース（U+3000）を半角スペースに変換
                else if (c == '\u3000')
                {
                    chars[i] = ' ';
                }
                // 波線（WAVE DASH U+301C）を半角チルダに統一
                else if (c == '\u301C')
                {
                    chars[i] = '~';
                }
            }

            var normalized = new string(chars).ToLowerInvariant();
            // 連続する空白を1つの半角スペースに圧縮
            return Regex.Replace(normalized, @"\s+", " ").Trim();
        }

        /// <summary>
        /// 自動マッチングの第2パス用：[初回限定版]、(DL版)、【通常版】などの版数タグを除去して正規化する。
        /// </summary>
        public static string NormalizeAndStripSuffix(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            // 括弧内の版数・タグ（[...], (...), 【...】, （...）, ［...］）を除去
            var stripped = Regex.Replace(value, @"\s*[\(\[\{【（［].*?[\)\]\}】）］]\s*", " ");
            // 「 - Windows 10...」「 HD Remaster」等のよくある接尾辞を除去
            stripped = Regex.Replace(stripped, @"\s*-\s*windows.*$", "", RegexOptions.IgnoreCase);
            return NormalizeForComparison(stripped);
        }

        public static DateTime? ParseDate(string value)
        {
            if (string.IsNullOrEmpty(value))
                return null;

            // VNDB は "yyyy-MM-dd"、"yyyy-MM"、"yyyy" の3形式がありうる
            var formats = new[] { "yyyy-MM-dd", "yyyy-MM", "yyyy" };
            if (DateTime.TryParseExact(value, formats,
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                if (date.Year >= TbaYearThreshold)
                    return null;
                return date;
            }

            return null;
        }

        public async Task<byte[]> DownloadImageAsync(string url, CancellationToken ct = default)
        {
            if (_nocoDb != null && _nocoDb.IsNocoDbUrl(url))
            {
                return await _nocoDb.DownloadImageAsync(url, ct);
            }

            try
            {
                using (var response = await HttpClient.GetAsync(url, ct))
                {
                    if (!response.IsSuccessStatusCode)
                        return null;

                    var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
                    if (!contentType.StartsWith("image/"))
                        return null;

                    return await response.Content.ReadAsByteArrayAsync();
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException) || !ct.IsCancellationRequested)
            {
                _logger.Warn($"画像ダウンロードエラー ({url}): {ex.Message}");
                return null;
            }
        }

        internal static string EscapeEucJp(string value)
        {
            var bytes = System.Text.Encoding.GetEncoding("EUC-JP").GetBytes(value);
            var sb = new System.Text.StringBuilder();
            foreach (var b in bytes)
            {
                if ((b >= 'A' && b <= 'Z') || (b >= 'a' && b <= 'z') || (b >= '0' && b <= '9')
                    || b == '-' || b == '_' || b == '.' || b == '~')
                {
                    sb.Append((char)b);
                }
                else
                {
                    sb.AppendFormat("%{0:X2}", b);
                }
            }
            return sb.ToString();
        }
    }
}
