using ErogameScapeMetadata.Models;
using ErogameScapeMetadata.Services;
using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Controls;

namespace ErogameScapeMetadata
{
    public class ErogameScapeMetadataPlugin : MetadataPlugin
    {
        private static readonly ILogger _logger = LogManager.GetLogger();
        private readonly ErogameScapeApiClient _apiClient;

        public override Guid Id { get; } = Guid.Parse("b8e3f2a1-5c4d-4e6f-9a1b-2d3e4f5a6b7c");

        public override string Name => "ErogameScape";

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

        public ErogameScapeMetadataPlugin(IPlayniteAPI api) : base(api)
        {
            _apiClient = new ErogameScapeApiClient(_logger, LoadConfig());
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
            return new ErogameScapeMetadataProvider(options, _apiClient);
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
