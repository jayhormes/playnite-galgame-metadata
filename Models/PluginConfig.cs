namespace GalgameMetadata.Models
{
    // 設定は ExtensionsData/<plugin id>/config.json
    public class PluginConfig
    {
        public const int DefaultNocoDbMaxTags = 30;

        // Tavily Extract の API キー（tvly-...）。空なら Wayback のみ。
        public string TavilyApiKey { get; set; } = string.Empty;

        // true = Tavily（live 値）を優先し、失敗時に Wayback。false = Wayback 優先（既定、無料）。
        public bool PreferTavily { get; set; } = false;

        // NocoDB 自建資料庫整合設定（標籤・封面・評分）
        public string NocoDbBaseUrl { get; set; } = string.Empty;
        public string NocoDbApiToken { get; set; } = string.Empty;
        public string NocoDbGamesTableId { get; set; } = string.Empty;
        public string NocoDbGenreLinkId { get; set; } = string.Empty;
        public string NocoDbAttrLinkId { get; set; } = string.Empty;

        // true = NocoDB のタグのみ採用。false = NocoDB を先頭に VNDB タグを継ぎ足す。
        public bool PreferNocoDbTags { get; set; } = true;

        // NocoDB 由来タグの上限（0 = 無制限）。Playnite のタグ一覧が膨らむのを防ぐ。
        public int NocoDbMaxTags { get; set; } = DefaultNocoDbMaxTags;

        // 自己署名証明書の自建 NocoDB 用。**その host のみ**検証を免除する（他 host は影響なし）。
        public bool NocoDbIgnoreSslErrors { get; set; } = false;

        // true（既定）= NocoDB にある EGS/VNDB 評分をそのまま使う。
        // false = NocoDB の評分キャッシュを無視し、常に Wayback/Tavily から取得する。
        public bool FallbackNocoDbScores { get; set; } = true;
    }
}
