using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace GalgameMetadata.Services
{
    // EGS スコア取得。**EGS 本体には一切アクセスしない**：
    //   1. Wayback Machine 快照（無料・無制限、スコアは快照時点値）
    //   2. Tavily Extract API（サーバ側代理取得、APIキー設定時のみ、live 値）
    // 2026-08 のリニューアル後 EGS は未許可 IP を silent drop するため、
    // 直接アクセスは封鎖リスクしかない。本クラスがその代替。
    public class EgsStatsClient
    {
        private const string GameUrlTemplate =
            "https://erogamescape.dyndns.org/~ap2/ero/toukei_kaiseki/game.php?game={0}";

        private const string TavilyExtractEndpoint = "https://api.tavily.com/extract";

        private readonly HttpClient _http;
        private readonly ILogger _logger;
        private readonly string _tavilyApiKey;
        private readonly bool _preferTavily;

        public EgsStatsClient(HttpClient http, ILogger logger, string tavilyApiKey, bool preferTavily)
        {
            _http = http;
            _logger = logger;
            _tavilyApiKey = tavilyApiKey;
            _preferTavily = preferTavily;
        }

        public static string GetGameUrl(int egsId)
        {
            return string.Format(GameUrlTemplate, egsId);
        }

        public async Task<EgsStats> GetStatsAsync(int egsId, CancellationToken ct)
        {
            var haveTavily = !string.IsNullOrWhiteSpace(_tavilyApiKey);

            if (_preferTavily && haveTavily)
            {
                return await TryTavilyAsync(egsId, ct) ?? await TryWaybackAsync(egsId, ct);
            }
            var stats = await TryWaybackAsync(egsId, ct);
            if (stats != null)
            {
                return stats;
            }
            return haveTavily ? await TryTavilyAsync(egsId, ct) : null;
        }

        private async Task<EgsStats> TryWaybackAsync(int egsId, CancellationToken ct)
        {
            // 2021年以前の古いゲームは Wayback 上に http:// のみ快照が存在する場合が多い。
            // そのため https:// を試して 404 の場合は http:// へフォールバックする。
            var urlTemplates = new[]
            {
                "https://erogamescape.dyndns.org/~ap2/ero/toukei_kaiseki/game.php?game={0}",
                "http://erogamescape.dyndns.org/~ap2/ero/toukei_kaiseki/game.php?game={0}"
            };

            foreach (var template in urlTemplates)
            {
                var targetUrl = string.Format(template, egsId);
                var archiveUrl = $"https://web.archive.org/web/{DateTime.Now.Year}/{targetUrl}";

                try
                {
                    using (var response = await _http.GetAsync(archiveUrl, ct))
                    {
                        if (response.StatusCode == HttpStatusCode.NotFound)
                        {
                            continue;
                        }
                        if (!response.IsSuccessStatusCode)
                        {
                            _logger.Warn($"EGS Wayback 失敗: {(int)response.StatusCode} ({archiveUrl})");
                            continue;
                        }

                        var html = await response.Content.ReadAsStringAsync();
                        var stats = ParseStats(html);
                        if (stats == null)
                        {
                            continue;
                        }

                        stats.Source = "wayback";
                        var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? "";
                        var snap = Regex.Match(finalUrl, @"/web/(\d{8})");
                        stats.SnapshotDate = snap.Success ? snap.Groups[1].Value : null;
                        _logger.Info($"EGS Wayback 取得成功: game={egsId} 中央値={stats.Median} (快照 {stats.SnapshotDate})");
                        return stats;
                    }
                }
                catch (Exception ex) when (!(ex is OperationCanceledException) || !ct.IsCancellationRequested)
                {
                    _logger.Warn($"EGS Wayback エラー ({archiveUrl}): {ex.Message}");
                }
            }

            _logger.Info($"EGS Wayback 快照なし: game={egsId}");
            return null;
        }

        private async Task<EgsStats> TryTavilyAsync(int egsId, CancellationToken ct)
        {
            try
            {
                var gameUrl = GetGameUrl(egsId);
                var body = "{\"urls\":[\"" + gameUrl + "\"]}";

                using (var request = new HttpRequestMessage(HttpMethod.Post, TavilyExtractEndpoint))
                {
                    request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _tavilyApiKey.Trim());
                    request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");

                    using (var response = await _http.SendAsync(request, ct))
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            _logger.Warn($"Tavily extract 失敗: {(int)response.StatusCode} (game={egsId})");
                            return null;
                        }

                        var json = await response.Content.ReadAsStringAsync();
                        var parsed = Serialization.FromJson<TavilyExtractResponse>(json);
                        var content = parsed?.Results != null && parsed.Results.Count > 0
                            ? parsed.Results[0].RawContent
                            : null;
                        if (string.IsNullOrEmpty(content))
                        {
                            return null;
                        }

                        var stats = ParseStats(content);
                        if (stats == null)
                        {
                            return null;
                        }
                        stats.Source = "tavily";
                        _logger.Info($"EGS Tavily 取得成功: game={egsId} 中央値={stats.Median} (live)");
                        return stats;
                    }
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException) || !ct.IsCancellationRequested)
            {
                _logger.Warn($"Tavily extract エラー (game={egsId}): {ex.Message}");
                return null;
            }
        }

        // game.php の Twitter シェア data-text（中央値:NN データ数:NN 標準偏差:NN）を最優先、
        // 無ければ HTML タグを除去した本文テキストから抽出（旧形式快照・テーブル構造対応）
        internal static EgsStats ParseStats(string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                return null;
            }

            // 1. Twitter シェアボタンの data-text を試す
            var dataText = Regex.Match(content, @"data-text=""([^""]+)""");
            if (dataText.Success)
            {
                var decodedDataText = WebUtility.HtmlDecode(dataText.Groups[1].Value);
                var med = Regex.Match(decodedDataText, @"中央値[：:]?\s*(\d+)");
                var cnt = Regex.Match(decodedDataText, @"データ数[：:]?\s*(\d+)");
                var std = Regex.Match(decodedDataText, @"標準偏差[：:]?\s*(\d+)");

                if (med.Success && cnt.Success)
                {
                    return new EgsStats
                    {
                        Median = int.Parse(med.Groups[1].Value),
                        Count = int.Parse(cnt.Groups[1].Value),
                        StdDev = std.Success ? int.Parse(std.Groups[1].Value) : (int?)null,
                    };
                }
            }

            // 2. HTML タグを除去してテーブル要素やテキストから直接抽出
            var cleanText = Regex.Replace(content, @"<[^>]+>", " ");
            cleanText = WebUtility.HtmlDecode(cleanText);

            var fallbackMed = Regex.Match(cleanText, @"中央値[：:]?\s*(\d+)");
            var fallbackCnt = Regex.Match(cleanText, @"データ数[：:]?\s*(\d+)");
            var fallbackStd = Regex.Match(cleanText, @"標準偏差[：:]?\s*(\d+)");

            if (!fallbackMed.Success || !fallbackCnt.Success)
            {
                return null;
            }

            return new EgsStats
            {
                Median = int.Parse(fallbackMed.Groups[1].Value),
                Count = int.Parse(fallbackCnt.Groups[1].Value),
                StdDev = fallbackStd.Success ? int.Parse(fallbackStd.Groups[1].Value) : (int?)null,
            };
        }
    }

    public class EgsStats
    {
        public int Median { get; set; }
        public int Count { get; set; }
        public int? StdDev { get; set; }
        public string Source { get; set; }
        public string SnapshotDate { get; set; }
    }

    public class TavilyExtractResponse
    {
        [SerializationPropertyName("results")]
        public List<TavilyExtractResult> Results { get; set; }
    }

    public class TavilyExtractResult
    {
        [SerializationPropertyName("url")]
        public string Url { get; set; }

        [SerializationPropertyName("raw_content")]
        public string RawContent { get; set; }
    }
}
