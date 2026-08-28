using GalgameMetadata.Models;
using GalgameMetadata.Services;
using GalgameMetadata.Views;
using Playnite.SDK;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Windows.Controls;

namespace GalgameMetadata
{
    public class GalgameMetadataPlugin : MetadataPlugin
    {
        private static readonly ILogger _logger = LogManager.GetLogger();

        private readonly PluginConfigViewModel _settings;

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
            // LoadPluginSettings/SavePluginSettings は ExtensionsData/<id>/config.json を読み書きするので、
            // 以前に手で書いた config.json もそのまま引き継がれる
            _settings = new PluginConfigViewModel(
                () => LoadPluginSettings<PluginConfig>(),
                config => SavePluginSettings(config));

            Properties = new MetadataPluginProperties
            {
                HasSettings = true
            };
        }

        public override OnDemandMetadataProvider GetMetadataProvider(MetadataRequestOptions options)
        {
            // 取得のたびに現在の設定でクライアントを作る（設定変更後の再起動が不要）
            return new GalgameMetadataProvider(
                options, new GalgameMetadataClient(_logger, _settings.Settings));
        }

        public override ISettings GetSettings(bool firstRunSettings)
        {
            return _settings;
        }

        public override UserControl GetSettingsView(bool firstRunSettings)
        {
            return new SettingsView { DataContext = _settings };
        }
    }
}
