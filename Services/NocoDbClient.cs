using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ErogameScapeMetadata.Services
{
    public class NocoDbClient
    {
        private readonly HttpClient _http;
        private readonly ILogger _logger;
        private readonly string _baseUrl;
        private readonly string _token;
        private readonly string _gamesTableId;
        private readonly string _genreLinkId;
        private readonly string _attrLinkId;

        public string BaseUrl => _baseUrl;

        public bool IsConfigured => !string.IsNullOrWhiteSpace(_baseUrl) && !string.IsNullOrWhiteSpace(_token);

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
            _baseUrl = (baseUrl ?? string.Empty).TrimEnd('/');
            _token = (token ?? string.Empty).Trim();
            _gamesTableId = (gamesTableId ?? string.Empty).Trim();
            _genreLinkId = (genreLinkId ?? string.Empty).Trim();
            _attrLinkId = (attrLinkId ?? string.Empty).Trim();

            if (ignoreSslErrors)
            {
                ServicePointManager.ServerCertificateValidationCallback =
                    (sender, certificate, chain, sslPolicyErrors) => true;
            }
        }

        public async Task<NocoDbGameData> GetGameDataAsync(
            string vndbId, int? egsId, string gameName, CancellationToken ct = default)
        {
            if (!IsConfigured)
                return null;

            try
            {
                // 1. 依序使用 VNDB ID、EGS ID 或遊戲名稱尋找遊戲記錄
                var gameRecord = await FindGameRecordAsync(vndbId, egsId, gameName, ct);
                if (gameRecord == null || gameRecord.Id == 0)
                {
                    _logger.Info($"NocoDB 查無記錄: vndb={vndbId}, egs={egsId}, name={gameName}");
                    return null;
                }

                _logger.Info($"NocoDB 找到遊戲記錄: Id={gameRecord.Id}, Name={gameRecord.Name}");

                // 2. 獲取類型標籤與遊戲屬性標籤
                var tags = new List<string>();

                var genreTags = await FetchLinkedTagsAsync(_genreLinkId, gameRecord.Id, ct);
                foreach (var tag in genreTags)
                {
                    if (!string.IsNullOrWhiteSpace(tag) && !tags.Contains(tag))
                        tags.Add(tag);
                }

                var attrTags = await FetchLinkedTagsAsync(_attrLinkId, gameRecord.Id, ct);
                foreach (var tag in attrTags)
                {
                    if (!string.IsNullOrWhiteSpace(tag) && !tags.Contains(tag))
                        tags.Add(tag);
                }

                // 3. 獲取封面圖片網址
                string coverUrl = null;
                if (gameRecord.CoverAttachments != null && gameRecord.CoverAttachments.Count > 0)
                {
                    var att = gameRecord.CoverAttachments[0];
                    var path = !string.IsNullOrEmpty(att.SignedPath) ? att.SignedPath : att.Path;
                    if (!string.IsNullOrEmpty(path))
                    {
                        coverUrl = $"{_baseUrl}/{path.TrimStart('/')}";
                    }
                }

                // 4. 品牌與發售日
                string brandName = null;
                if (gameRecord.Brands != null && gameRecord.Brands.Count > 0)
                {
                    brandName = gameRecord.Brands[0].Name;
                }

                DateTime? releaseDate = null;
                if (!string.IsNullOrEmpty(gameRecord.ReleaseDate) &&
                    DateTime.TryParse(gameRecord.ReleaseDate, out var parsedDate))
                {
                    releaseDate = parsedDate;
                }

                return new NocoDbGameData
                {
                    GameId = gameRecord.Id,
                    Name = gameRecord.Name,
                    OriginalName = gameRecord.OriginalName,
                    BrandName = brandName,
                    ReleaseDate = releaseDate,
                    CoverImageUrl = coverUrl,
                    EgsScore = gameRecord.EgsScore,
                    EgsCount = gameRecord.EgsCount,
                    VndbScore = gameRecord.VndbScore,
                    VndbCount = gameRecord.VndbCount,
                    Tags = tags
                };
            }
            catch (Exception ex) when (!(ex is OperationCanceledException) || !ct.IsCancellationRequested)
            {
                _logger.Warn($"NocoDB 查詢失敗: {ex.Message}");
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
                    if (IsNocoDbUrl(url))
                    {
                        request.Headers.TryAddWithoutValidation("xc-token", _token);
                    }

                    using (var response = await _http.SendAsync(request, ct))
                    {
                        if (!response.IsSuccessStatusCode)
                            return null;

                        return await response.Content.ReadAsByteArrayAsync();
                    }
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException) || !ct.IsCancellationRequested)
            {
                _logger.Warn($"NocoDB 圖片下載失敗 ({url}): {ex.Message}");
                return null;
            }
        }

        private async Task<NocoDbRawGameRecord> FindGameRecordAsync(
            string vndbId, int? egsId, string gameName, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(_gamesTableId))
                return null;

            var whereConditions = new List<string>();

            if (!string.IsNullOrEmpty(vndbId))
            {
                whereConditions.Add($"(vndb URL,like,%{vndbId}%)");
            }
            if (egsId.HasValue)
            {
                whereConditions.Add($"(EGS URL,like,%game={egsId.Value}%)");
            }
            if (!string.IsNullOrEmpty(gameName))
            {
                whereConditions.Add($"(Name,eq,{EscapeFilterValue(gameName)})");
            }

            foreach (var where in whereConditions)
            {
                var queryParams = $"where={Uri.EscapeDataString(where)}&fields=Id,Name,%E5%8E%9F%E5%90%8D,%E7%99%BC%E5%94%AE%E6%97%A5%E6%9C%9F,EGS%20%E8%A9%95%E5%88%86,EGS%20%E7%A5%A8%E6%95%B8,vndb%20%E8%A9%95%E5%88%86,vndb%20%E7%A5%A8%E6%95%B8,%E5%B0%81%E9%9D%A2%E5%9C%96,LTAR_%E9%96%8B%E7%99%BC%E5%93%81%E7%89%8C&limit=1";
                var url = $"{_baseUrl}/api/v2/tables/{_gamesTableId}/records?{queryParams}";

                var response = await GetAsync<NocoDbListResponse<NocoDbRawGameRecord>>(url, ct);
                if (response?.List != null && response.List.Count > 0)
                {
                    return response.List[0];
                }
            }

            return null;
        }

        private async Task<List<string>> FetchLinkedTagsAsync(string linkId, int gameRecordId, CancellationToken ct)
        {
            var tags = new List<string>();
            if (string.IsNullOrWhiteSpace(linkId) || string.IsNullOrWhiteSpace(_gamesTableId))
                return tags;

            var queryParams = "fields=Id,Name&limit=100";
            var url = $"{_baseUrl}/api/v2/tables/{_gamesTableId}/links/{linkId}/records/{gameRecordId}?{queryParams}";

            var response = await GetAsync<NocoDbListResponse<NocoDbTagRecord>>(url, ct);
            if (response?.List != null)
            {
                foreach (var item in response.List)
                {
                    if (!string.IsNullOrWhiteSpace(item.Name))
                    {
                        tags.Add(item.Name.Trim());
                    }
                }
            }

            return tags;
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
                        _logger.Warn($"NocoDB API 請求失敗 ({(int)response.StatusCode}): {url}");
                        return null;
                    }

                    var json = await response.Content.ReadAsStringAsync();
                    return Serialization.FromJson<T>(json);
                }
            }
        }

        private static string EscapeFilterValue(string value)
        {
            return (value ?? "").Replace("(", "").Replace(")", "").Replace(",", "");
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
