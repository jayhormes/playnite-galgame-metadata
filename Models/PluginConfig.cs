namespace ErogameScapeMetadata.Models
{
    // 設定は ExtensionsData/<plugin id>/config.json
    public class PluginConfig
    {
        // Tavily Extract の API キー（tvly-...）。空なら Wayback のみ。
        public string TavilyApiKey { get; set; } = string.Empty;

        // true = Tavily（live 値）を優先し、失敗時に Wayback。false = Wayback 優先（既定、無料）。
        public bool PreferTavily { get; set; } = false;

        // NocoDB 自建資料庫整合設定（標籤與備用評分）
        public string NocoDbBaseUrl { get; set; } = string.Empty;
        public string NocoDbApiToken { get; set; } = string.Empty;
        public string NocoDbGamesTableId { get; set; } = string.Empty;
        public string NocoDbGenreLinkId { get; set; } = string.Empty;
        public string NocoDbAttrLinkId { get; set; } = string.Empty;
        public bool PreferNocoDbTags { get; set; } = true;
        public bool NocoDbIgnoreSslErrors { get; set; } = false;
        public bool FallbackNocoDbScores { get; set; } = true;
    }
}

