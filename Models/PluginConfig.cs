using System;
using System.Collections.Generic;

namespace GalgameMetadata.Models
{
    /// <summary>
    /// 設定の実体。Playnite の LoadPluginSettings/SavePluginSettings が
    /// ExtensionsData/&lt;plugin id&gt;/config.json として読み書きするので、
    /// 手で書いた config.json もそのまま引き継がれる。
    /// </summary>
    public class PluginConfig : ObservableObject
    {
        public const int DefaultNocoDbMaxTags = 30;

        private string tavilyApiKey = string.Empty;
        private bool preferTavily;
        private string nocoDbBaseUrl = string.Empty;
        private string nocoDbApiToken = string.Empty;
        private string nocoDbGamesTableId = string.Empty;
        private string nocoDbGenreLinkId = string.Empty;
        private string nocoDbAttrLinkId = string.Empty;
        private bool preferNocoDbTags = true;
        private int nocoDbMaxTags = DefaultNocoDbMaxTags;
        private bool nocoDbIgnoreSslErrors;
        private bool fallbackNocoDbScores = true;

        // Tavily Extract の API キー（tvly-...）。空なら Wayback のみ。
        public string TavilyApiKey
        {
            get => tavilyApiKey;
            set => SetValue(ref tavilyApiKey, value);
        }

        // true = Tavily（live 値）を優先し、失敗時に Wayback。false = Wayback 優先（既定、無料）。
        public bool PreferTavily
        {
            get => preferTavily;
            set => SetValue(ref preferTavily, value);
        }

        public string NocoDbBaseUrl
        {
            get => nocoDbBaseUrl;
            set => SetValue(ref nocoDbBaseUrl, value);
        }

        public string NocoDbApiToken
        {
            get => nocoDbApiToken;
            set => SetValue(ref nocoDbApiToken, value);
        }

        public string NocoDbGamesTableId
        {
            get => nocoDbGamesTableId;
            set => SetValue(ref nocoDbGamesTableId, value);
        }

        public string NocoDbGenreLinkId
        {
            get => nocoDbGenreLinkId;
            set => SetValue(ref nocoDbGenreLinkId, value);
        }

        public string NocoDbAttrLinkId
        {
            get => nocoDbAttrLinkId;
            set => SetValue(ref nocoDbAttrLinkId, value);
        }

        // true = NocoDB のタグのみ採用。false = NocoDB を先頭に VNDB タグを継ぎ足す。
        public bool PreferNocoDbTags
        {
            get => preferNocoDbTags;
            set => SetValue(ref preferNocoDbTags, value);
        }

        // NocoDB 由来タグの上限（0 = 無制限）。
        public int NocoDbMaxTags
        {
            get => nocoDbMaxTags;
            set => SetValue(ref nocoDbMaxTags, value);
        }

        // 自己署名証明書の自建 NocoDB 用。**その host のみ**検証を免除する。
        public bool NocoDbIgnoreSslErrors
        {
            get => nocoDbIgnoreSslErrors;
            set => SetValue(ref nocoDbIgnoreSslErrors, value);
        }

        // true（既定）= NocoDB にある EGS/VNDB 評分をそのまま使う。
        // false = NocoDB の評分キャッシュを無視し、常に Wayback/Tavily から取得する。
        public bool FallbackNocoDbScores
        {
            get => fallbackNocoDbScores;
            set => SetValue(ref fallbackNocoDbScores, value);
        }

        public PluginConfig Clone()
        {
            return new PluginConfig
            {
                TavilyApiKey = TavilyApiKey,
                PreferTavily = PreferTavily,
                NocoDbBaseUrl = NocoDbBaseUrl,
                NocoDbApiToken = NocoDbApiToken,
                NocoDbGamesTableId = NocoDbGamesTableId,
                NocoDbGenreLinkId = NocoDbGenreLinkId,
                NocoDbAttrLinkId = NocoDbAttrLinkId,
                PreferNocoDbTags = PreferNocoDbTags,
                NocoDbMaxTags = NocoDbMaxTags,
                NocoDbIgnoreSslErrors = NocoDbIgnoreSslErrors,
                FallbackNocoDbScores = FallbackNocoDbScores
            };
        }

        /// <summary>
        /// 保存前の検証。空の設定（未使用）はエラーにしない。
        /// </summary>
        public List<string> Validate()
        {
            var errors = new List<string>();

            var anyNocoDb = !string.IsNullOrWhiteSpace(NocoDbBaseUrl)
                || !string.IsNullOrWhiteSpace(NocoDbApiToken)
                || !string.IsNullOrWhiteSpace(NocoDbGamesTableId);

            if (anyNocoDb)
            {
                if (string.IsNullOrWhiteSpace(NocoDbBaseUrl))
                {
                    errors.Add("NocoDB: Base URL is required.");
                }
                else if (!IsHttpUrl(NocoDbBaseUrl))
                {
                    errors.Add("NocoDB: Base URL must start with http:// or https://.");
                }

                if (string.IsNullOrWhiteSpace(NocoDbApiToken))
                {
                    errors.Add("NocoDB: API token is required.");
                }

                if (string.IsNullOrWhiteSpace(NocoDbGamesTableId))
                {
                    errors.Add("NocoDB: Games table ID is required.");
                }
            }

            if (NocoDbMaxTags < 0)
            {
                errors.Add("NocoDB: Max tags cannot be negative (use 0 for no limit).");
            }

            if (PreferTavily && string.IsNullOrWhiteSpace(TavilyApiKey))
            {
                errors.Add("Tavily: an API key is required when Tavily is preferred.");
            }

            return errors;
        }

        internal static bool IsHttpUrl(string url)
        {
            Uri uri;
            if (!Uri.TryCreate((url ?? string.Empty).Trim(), UriKind.Absolute, out uri))
            {
                return false;
            }
            return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
        }
    }
}
