using GalgameMetadata.Models;
using GalgameMetadata.Services;
using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Controls;

namespace GalgameMetadata
{
    public class GalgameMetadataPlugin : MetadataPlugin
    {
        private static readonly ILogger _logger = LogManager.GetLogger();
        private readonly GalgameMetadataClient _apiClient;

        public override Guid Id { get; } = Guid.Parse("e6ab0c61-8c40-4e4b-842b-08cd132c09e4");

        // NocoDB / VNDB / EGS / DLsite / DMM を束ねるため、UI 上の表示名は総称にする
        public override string Name => "Galgame Metadata";

        public override List<MetadataField> SupportedFields { get; } = new List<MetadataField>
        {
            MetadataField.Name,
            MetadataField.Developers,
            MetadataField.Publishers,
            MetadataField.ReleaseDate,
            MetadataField.CommunityScore,
            MetadataField.CoverImage,
            MetadataField.BackgroundImage,
            MetadataField.Links,
            MetadataField.Description,
            MetadataField.Tags,
            MetadataField.Genres,
            MetadataField.Platform,
            MetadataField.AgeRating,
            MetadataField.Region,
        };

        public GalgameMetadataPlugin(IPlayniteAPI api) : base(api)
        {
            _apiClient = new GalgameMetadataClient(_logger, LoadConfig());
            Properties = new MetadataPluginProperties
            {
                HasSettings = false
            };
        }

        // 設定は ExtensionsData/<plugin id>/config.json（UI なし、手動編集して Playnite 再起動）
        private PluginConfig LoadConfig()
        {
            var path = Path.Combine(GetPluginUserDataPath(), "config.json");
            try
            {
                if (File.Exists(path))
                {
                    return Serialization.FromJson<PluginConfig>(File.ReadAllText(path)) ?? new PluginConfig();
                }
                var config = new PluginConfig();
                Directory.CreateDirectory(GetPluginUserDataPath());
                File.WriteAllText(path, Serialization.ToJson(config, true));
                return config;
            }
            catch (Exception ex)
            {
                _logger.Warn($"config.json 読み込み失敗、既定値を使用: {ex.Message}");
                return new PluginConfig();
            }
        }

        public override OnDemandMetadataProvider GetMetadataProvider(MetadataRequestOptions options)
        {
            return new GalgameMetadataProvider(options, _apiClient);
        }

        public override ISettings GetSettings(bool firstRunSettings)
        {
            return null;
        }

        public override UserControl GetSettingsView(bool firstRunSettings)
        {
            return null;
        }
    }
}
