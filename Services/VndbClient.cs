using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ErogameScapeMetadata.Services
{
    // VNDB kana API クライアント。検索・詳細・release extlinks（EGS/DLsite/DMM id の解決）を担当。
    // 2.0.0 リファクタ：EGS SQL API の代替として検索層を VNDB に置き換えた。
    public class VndbClient
    {
        private const string ApiBase = "https://api.vndb.org/kana";

        private readonly HttpClient _http;
        private readonly ILogger _logger;

        public VndbClient(HttpClient http, ILogger logger)
        {
            _http = http;
            _logger = logger;
        }

        public async Task<List<VndbVn>> SearchAsync(string keyword, CancellationToken ct)
        {
            var body = "{\"filters\":[\"search\",\"=\"," + EscapeJsonString(keyword) + "]," +
                       "\"fields\":\"title,alttitle,released,developers.name,developers.original\"," +
                       "\"results\":15}";
            var response = await PostAsync<VndbResponse<VndbVn>>("/vn", body, ct);
            return response?.Results ?? new List<VndbVn>();
        }

        public async Task<VndbVn> GetDetailAsync(string vnId, CancellationToken ct)
        {
            var body = "{\"filters\":[\"id\",\"=\",\"" + vnId + "\"]," +
                       "\"fields\":\"title,alttitle,released,rating,description," +
                       "image.url,image.dims,image.sexual,image.violence," +
                       "screenshots.url,screenshots.sexual,screenshots.violence," +
                       "developers.name,developers.original," +
                       "tags.name,tags.category,tags.rating,tags.spoiler\"," +
                       "\"results\":1}";
            var response = await PostAsync<VndbResponse<VndbVn>>("/vn", body, ct);
            return response?.Results != null && response.Results.Count > 0
                ? response.Results[0]
                : null;
        }

        public async Task<List<VndbRelease>> GetReleasesAsync(string vnId, CancellationToken ct)
        {
            var body = "{\"filters\":[\"vn\",\"=\",[\"id\",\"=\",\"" + vnId + "\"]]," +
                       "\"fields\":\"minage,extlinks.url,extlinks.name,extlinks.label\",\"results\":100}";
            var response = await PostAsync<VndbResponse<VndbRelease>>("/release", body, ct);
            return response?.Results ?? new List<VndbRelease>();
        }

        private async Task<T> PostAsync<T>(string path, string body, CancellationToken ct) where T : class
        {
            try
            {
                using (var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"))
                using (var response = await _http.PostAsync(ApiBase + path, content, ct))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.Warn($"VNDB {path} 失敗: {(int)response.StatusCode}");
                        return null;
                    }
                    var json = await response.Content.ReadAsStringAsync();
                    return Serialization.FromJson<T>(json);
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException) || !ct.IsCancellationRequested)
            {
                _logger.Warn($"VNDB {path} 通信エラー: {ex.Message}");
                return null;
            }
        }

        internal static string EscapeJsonString(string value)
        {
            return "\"" + (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t") + "\"";
        }
    }

    public class VndbResponse<T>
    {
        [SerializationPropertyName("results")]
        public List<T> Results { get; set; }
    }

    public class VndbVn
    {
        [SerializationPropertyName("id")]
        public string Id { get; set; }

        [SerializationPropertyName("title")]
        public string Title { get; set; }

        [SerializationPropertyName("alttitle")]
        public string AltTitle { get; set; }

        [SerializationPropertyName("released")]
        public string Released { get; set; }

        [SerializationPropertyName("rating")]
        public double? Rating { get; set; }

        [SerializationPropertyName("description")]
        public string Description { get; set; }

        [SerializationPropertyName("image")]
        public VndbImage Image { get; set; }

        [SerializationPropertyName("screenshots")]
        public List<VndbImage> Screenshots { get; set; }

        [SerializationPropertyName("developers")]
        public List<VndbDeveloper> Developers { get; set; }

        [SerializationPropertyName("tags")]
        public List<VndbTag> Tags { get; set; }

        // 表示名は日本語原題を優先（EGS 版の gamename と同じ感覚）
        public string DisplayName => string.IsNullOrEmpty(AltTitle) ? Title : AltTitle;
    }

    public class VndbImage
    {
        [SerializationPropertyName("url")]
        public string Url { get; set; }

        [SerializationPropertyName("dims")]
        public List<int> Dims { get; set; }

        [SerializationPropertyName("sexual")]
        public double? Sexual { get; set; }

        [SerializationPropertyName("violence")]
        public double? Violence { get; set; }

        public bool IsSafe => (Sexual ?? 2) < 1.0 && (Violence ?? 2) < 1.0;

        public bool IsPortrait => Dims != null && Dims.Count == 2 && Dims[1] > Dims[0];
    }

    public class VndbDeveloper
    {
        [SerializationPropertyName("name")]
        public string Name { get; set; }

        [SerializationPropertyName("original")]
        public string Original { get; set; }

        public string DisplayName => string.IsNullOrEmpty(Original) ? Name : Original;
    }

    public class VndbTag
    {
        [SerializationPropertyName("name")]
        public string Name { get; set; }

        [SerializationPropertyName("category")]
        public string Category { get; set; }

        [SerializationPropertyName("rating")]
        public double? Rating { get; set; }

        [SerializationPropertyName("spoiler")]
        public double? Spoiler { get; set; }
    }

    public class VndbRelease
    {
        [SerializationPropertyName("minage")]
        public int? MinAge { get; set; }

        [SerializationPropertyName("extlinks")]
        public List<VndbExtLink> ExtLinks { get; set; }
    }

    public class VndbExtLink
    {
        [SerializationPropertyName("url")]
        public string Url { get; set; }

        [SerializationPropertyName("name")]
        public string Name { get; set; }

        [SerializationPropertyName("label")]
        public string Label { get; set; }
    }
}
