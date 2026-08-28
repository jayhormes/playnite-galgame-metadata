using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ErogameScapeMetadata.Services
{
    public class NocoDbClient
    {
        // 遊戲收藏庫で取得する列。NocoDB の列名は日本語/繁体字そのままがキーになる。
        internal const string RecordFields =
            "Id,Name,原名,發售日期,EGS 評分,EGS 票數,vndb 評分,vndb 票數,封面圖,LTAR_開發品牌";

        internal const int MaxLinkedTags = 100;

        private readonly HttpClient _http;
        private readonly ILogger _logger;
        private readonly string _baseUrl;
        private readonly string _token;
        private readonly string _gamesTableId;
        private readonly string _genreLinkId;
        private readonly string _attrLinkId;

        public string BaseUrl => _baseUrl;

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(_baseUrl)
            && !string.IsNullOrWhiteSpace(_token)
            && !string.IsNullOrWhiteSpace(_gamesTableId);

        public NocoDbClient(
            HttpClient http,
            ILogger logger,
            string baseUrl,
            string token,
            string gamesTableId = "",
            string genreLinkId = "",
            string attrLinkId = "",
            bool ignoreSslErrors = false)
        {
            _http = http;
            _logger = logger;
            _baseUrl = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
            _token = (token ?? string.Empty).Trim();
            _gamesTableId = (gamesTableId ?? string.Empty).Trim();
            _genreLinkId = (genreLinkId ?? string.Empty).Trim();
            _attrLinkId = (attrLinkId ?? string.Empty).Trim();

            if (ignoreSslErrors)
            {
                // この host だけ証明書検証を免除する（他 host には影響しない）
                SslCertificateBypass.AllowHost(_baseUrl);
            }
        }

        public async Task<NocoDbGameData> GetGameDataAsync(
            string vndbId, int? egsId, string gameName, CancellationToken ct = default)
        {
            if (!IsConfigured)
            {
                if (!string.IsNullOrWhiteSpace(_baseUrl) && string.IsNullOrWhiteSpace(_gamesTableId))
                {
                    _logger?.Warn("NocoDB: NocoDbGamesTableId が未設定のため照会をスキップ");
                }
                return null;
            }

            try
            {
                // 1. vndb URL（完全一致）→ EGS URL（末尾一致）→ Name（完全一致）の順で照合
                var gameRecord = await FindGameRecordAsync(vndbId, egsId, gameName, ct);
                if (gameRecord == null || gameRecord.Id == 0)
                {
                    _logger?.Info($"NocoDB 查無記錄: vndb={vndbId}, egs={egsId}, name={gameName}");
                    return null;
                }

                _logger?.Info($"NocoDB 找到遊戲記錄: Id={gameRecord.Id}, Name={gameRecord.Name}");

                // 2. 類型標籤・遊戲屬性のリンクを取得
                var tags = new List<string>();
                AppendTags(tags, await FetchLinkedTagsAsync(_genreLinkId, gameRecord.Id, ct));
                AppendTags(tags, await FetchLinkedTagsAsync(_attrLinkId, gameRecord.Id, ct));

                return new NocoDbGameData
                {
                    GameId = gameRecord.Id,
                    Name = gameRecord.Name,
                    OriginalName = gameRecord.OriginalName,
                    BrandName = gameRecord.Brands != null && gameRecord.Brands.Count > 0
                        ? gameRecord.Brands[0].Name
                        : null,
                    ReleaseDate = ParseReleaseDate(gameRecord.ReleaseDate),
                    CoverImageUrl = BuildAttachmentUrl(_baseUrl, gameRecord.CoverAttachments),
                    EgsScore = gameRecord.EgsScore,
                    EgsCount = gameRecord.EgsCount,
                    VndbScore = gameRecord.VndbScore,
                    VndbCount = gameRecord.VndbCount,
                    Tags = tags
                };
            }
            catch (Exception ex) when (!(ex is OperationCanceledException) || !ct.IsCancellationRequested)
            {
                _logger?.Warn($"NocoDB 查詢失敗: {ex.Message}");
                return null;
            }
        }

        public bool IsNocoDbUrl(string url)
        {
            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(_baseUrl))
                return false;
            return url.StartsWith(_baseUrl, StringComparison.OrdinalIgnoreCase);
        }

        public async Task<byte[]> DownloadImageAsync(string url, CancellationToken ct = default)
        {
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    // token は NocoDB 宛のみ付与（共有 HttpClient の既定ヘッダには入れない）
                    if (IsNocoDbUrl(url))
                    {
                        request.Headers.TryAddWithoutValidation("xc-token", _token);
                    }

                    using (var response = await _http.SendAsync(request, ct))
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            _logger?.Warn($"NocoDB 圖片下載失敗 ({(int)response.StatusCode}): {url}");
                            return null;
                        }

                        var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
                        if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                        {
                            _logger?.Warn($"NocoDB 圖片下載: 非圖片回應 ({contentType}) {url}");
                            return null;
                        }

                        return await response.Content.ReadAsByteArrayAsync();
                    }
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException) || !ct.IsCancellationRequested)
            {
                _logger?.Warn($"NocoDB 圖片下載失敗 ({url}): {ex.Message}");
                return null;
            }
        }

        private async Task<NocoDbRawGameRecord> FindGameRecordAsync(
            string vndbId, int? egsId, string gameName, CancellationToken ct)
        {
            foreach (var where in BuildWhereConditions(vndbId, egsId, gameName))
            {
                var url = BuildRecordsUrl(_baseUrl, _gamesTableId, where);
                var response = await GetAsync<NocoDbListResponse<NocoDbRawGameRecord>>(url, ct);
                if (response?.List != null && response.List.Count > 0)
                {
                    return response.List[0];
                }
            }

            return null;
        }

        /// <summary>
        /// 照合条件を優先順に返す。実 DB（2,332 件）で検証した形式:
        ///   vndb URL は "https://vndb.org/vN" に統一 → 完全一致が使える
        ///   EGS URL は http/https/旧パスが混在 → 末尾一致 "%game=N"（末尾 % なし）で誤爆を防ぐ
        /// </summary>
        internal static List<string> BuildWhereConditions(string vndbId, int? egsId, string gameName)
        {
            var conditions = new List<string>();

            var vndb = SanitizeFilterValue(vndbId);
            if (!string.IsNullOrEmpty(vndb))
            {
                conditions.Add($"(vndb URL,eq,https://vndb.org/{vndb})");
            }

            if (egsId.HasValue)
            {
                // 末尾に % を付けない = 前方一致による誤爆（game=4043 が game=40432 に当たる等）を防ぐ
                conditions.Add($"(EGS URL,like,%game={egsId.Value})");
            }

            var name = SanitizeFilterValue(gameName);
            if (!string.IsNullOrEmpty(name))
            {
                conditions.Add($"(Name,eq,{name})");
            }

            return conditions;
        }

        internal static string BuildRecordsUrl(string baseUrl, string tableId, string where)
        {
            // sort=Id で limit=1 の結果を決定的にする
            return $"{baseUrl}/api/v2/tables/{tableId}/records"
                 + $"?where={Uri.EscapeDataString(where)}"
                 + $"&fields={Uri.EscapeDataString(RecordFields)}"
                 + "&sort=Id&limit=1";
        }

        internal static string BuildLinksUrl(string baseUrl, string tableId, string linkId, int recordId)
        {
            return $"{baseUrl}/api/v2/tables/{tableId}/links/{linkId}/records/{recordId}"
                 + $"?fields=Id,Name&limit={MaxLinkedTags}";
        }

        /// <summary>
        /// signedPath（署名付き一時 URL）優先、無ければ path（xc-token 必須）。
        /// </summary>
        internal static string BuildAttachmentUrl(string baseUrl, List<NocoDbAttachment> attachments)
        {
            if (attachments == null || attachments.Count == 0 || string.IsNullOrEmpty(baseUrl))
                return null;

            var attachment = attachments[0];
            var path = !string.IsNullOrWhiteSpace(attachment?.SignedPath)
                ? attachment.SignedPath
                : attachment?.Path;

            if (string.IsNullOrWhiteSpace(path))
                return null;

            return $"{baseUrl.TrimEnd('/')}/{path.Trim().TrimStart('/')}";
        }

        internal static DateTime? ParseReleaseDate(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            DateTime parsed;
            return DateTime.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out parsed)
                ? parsed
                : (DateTime?)null;
        }

        private async Task<List<string>> FetchLinkedTagsAsync(
            string linkId, int gameRecordId, CancellationToken ct)
        {
            var tags = new List<string>();
            if (string.IsNullOrWhiteSpace(linkId))
                return tags;

            var url = BuildLinksUrl(_baseUrl, _gamesTableId, linkId, gameRecordId);
            var response = await GetAsync<NocoDbListResponse<NocoDbTagRecord>>(url, ct);
            if (response?.List == null)
                return tags;

            foreach (var item in response.List)
            {
                if (!string.IsNullOrWhiteSpace(item.Name))
                {
                    tags.Add(item.Name.Trim());
                }
            }

            return tags;
        }

        internal static void AppendTags(List<string> target, List<string> source)
        {
            if (target == null || source == null)
                return;

            foreach (var tag in source)
            {
                if (!string.IsNullOrWhiteSpace(tag) && !target.Contains(tag))
                {
                    target.Add(tag);
                }
            }
        }

        private async Task<T> GetAsync<T>(string url, CancellationToken ct) where T : class
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                request.Headers.TryAddWithoutValidation("xc-token", _token);
                using (var response = await _http.SendAsync(request, ct))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        _logger?.Warn($"NocoDB API 請求失敗 ({(int)response.StatusCode}): {url}");
                        return null;
                    }

                    var json = await response.Content.ReadAsStringAsync();
                    return Serialization.FromJson<T>(json);
                }
            }
        }

        /// <summary>
        /// NocoDB の where 値は括弧・カンマを含んでもそのまま通る（実 DB で検証済み。
        /// 例:「彼女は高天(そら)に祈らない -quantum girlfriend-」「… into the firmament‐」）。
        /// 逆に文字を削除・バックスラッシュ escape すると一致しなくなるため、trim のみ行う。
        /// </summary>
        internal static string SanitizeFilterValue(string value)
        {
            return (value ?? string.Empty).Trim();
        }
    }

    public class NocoDbGameData
    {
        public int GameId { get; set; }
        public string Name { get; set; }
        public string OriginalName { get; set; }
        public string BrandName { get; set; }
        public DateTime? ReleaseDate { get; set; }
        public string CoverImageUrl { get; set; }
        public int? EgsScore { get; set; }
        public int? EgsCount { get; set; }
        public double? VndbScore { get; set; }
        public int? VndbCount { get; set; }
        public List<string> Tags { get; set; } = new List<string>();
    }

    public class NocoDbListResponse<T>
    {
        [SerializationPropertyName("list")]
        public List<T> List { get; set; }
    }

    public class NocoDbRawGameRecord
    {
        [SerializationPropertyName("Id")]
        public int Id { get; set; }

        [SerializationPropertyName("Name")]
        public string Name { get; set; }

        [SerializationPropertyName("原名")]
        public string OriginalName { get; set; }

        [SerializationPropertyName("發售日期")]
        public string ReleaseDate { get; set; }

        [SerializationPropertyName("EGS 評分")]
        public int? EgsScore { get; set; }

        [SerializationPropertyName("EGS 票數")]
        public int? EgsCount { get; set; }

        [SerializationPropertyName("vndb 評分")]
        public double? VndbScore { get; set; }

        [SerializationPropertyName("vndb 票數")]
        public int? VndbCount { get; set; }

        [SerializationPropertyName("封面圖")]
        public List<NocoDbAttachment> CoverAttachments { get; set; }

        [SerializationPropertyName("LTAR_開發品牌")]
        public List<NocoDbBrandRecord> Brands { get; set; }
    }

    public class NocoDbAttachment
    {
        [SerializationPropertyName("path")]
        public string Path { get; set; }

        [SerializationPropertyName("signedPath")]
        public string SignedPath { get; set; }

        [SerializationPropertyName("title")]
        public string Title { get; set; }

        [SerializationPropertyName("mimetype")]
        public string MimeType { get; set; }
    }

    public class NocoDbBrandRecord
    {
        [SerializationPropertyName("Id")]
        public int Id { get; set; }

        [SerializationPropertyName("Name")]
        public string Name { get; set; }
    }

    public class NocoDbTagRecord
    {
        [SerializationPropertyName("Id")]
        public int Id { get; set; }

        [SerializationPropertyName("Name")]
        public string Name { get; set; }
    }
}
