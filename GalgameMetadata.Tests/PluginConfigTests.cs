using GalgameMetadata.Models;
using System.Collections.Generic;
using Xunit;

namespace GalgameMetadata.Tests
{
    public class PluginConfigTests
    {
        private static PluginConfig Filled()
        {
            return new PluginConfig
            {
                TavilyApiKey = "tvly-key",
                PreferTavily = true,
                NocoDbBaseUrl = "https://192.168.1.106:5555",
                NocoDbApiToken = "token",
                NocoDbGamesTableId = "mk760gpjbh7yrqh",
                NocoDbGenreLinkId = "cjna85stylyh7g8",
                NocoDbAttrLinkId = "c9j62bvclguglj3",
                PreferNocoDbTags = false,
                NocoDbMaxTags = 12,
                NocoDbIgnoreSslErrors = true,
                FallbackNocoDbScores = false
            };
        }

        [Fact]
        public void Defaults_AreSafe()
        {
            var config = new PluginConfig();

            Assert.Equal(string.Empty, config.TavilyApiKey);
            Assert.False(config.PreferTavily);
            Assert.Equal(string.Empty, config.NocoDbBaseUrl);
            Assert.True(config.PreferNocoDbTags);
            Assert.Equal(30, config.NocoDbMaxTags);
            Assert.False(config.NocoDbIgnoreSslErrors);
            Assert.True(config.FallbackNocoDbScores);
        }

        [Fact]
        public void Clone_CopiesEveryFieldAndIsIndependent()
        {
            var original = Filled();
            var clone = original.Clone();

            Assert.Equal(original.TavilyApiKey, clone.TavilyApiKey);
            Assert.Equal(original.PreferTavily, clone.PreferTavily);
            Assert.Equal(original.NocoDbBaseUrl, clone.NocoDbBaseUrl);
            Assert.Equal(original.NocoDbApiToken, clone.NocoDbApiToken);
            Assert.Equal(original.NocoDbGamesTableId, clone.NocoDbGamesTableId);
            Assert.Equal(original.NocoDbGenreLinkId, clone.NocoDbGenreLinkId);
            Assert.Equal(original.NocoDbAttrLinkId, clone.NocoDbAttrLinkId);
            Assert.Equal(original.PreferNocoDbTags, clone.PreferNocoDbTags);
            Assert.Equal(original.NocoDbMaxTags, clone.NocoDbMaxTags);
            Assert.Equal(original.NocoDbIgnoreSslErrors, clone.NocoDbIgnoreSslErrors);
            Assert.Equal(original.FallbackNocoDbScores, clone.FallbackNocoDbScores);

            clone.NocoDbBaseUrl = "https://changed";
            Assert.Equal("https://192.168.1.106:5555", original.NocoDbBaseUrl);
        }

        [Fact]
        public void SettingProperty_RaisesPropertyChanged()
        {
            var config = new PluginConfig();
            var raised = new List<string>();
            config.PropertyChanged += (s, e) => raised.Add(e.PropertyName);

            config.NocoDbBaseUrl = "https://example.com";
            config.PreferTavily = true;

            Assert.Contains("NocoDbBaseUrl", raised);
            Assert.Contains("PreferTavily", raised);
        }

        // --- 検証 ---

        [Fact]
        public void Validate_EmptyConfigIsValid()
        {
            Assert.Empty(new PluginConfig().Validate());
        }

        [Fact]
        public void Validate_FullyConfiguredIsValid()
        {
            Assert.Empty(Filled().Validate());
        }

        [Fact]
        public void Validate_PartialNocoDbReportsMissingFields()
        {
            var errors = new PluginConfig { NocoDbBaseUrl = "https://nocodb.example.com" }.Validate();

            Assert.Equal(2, errors.Count);
            Assert.Contains(errors, e => e.Contains("API token"));
            Assert.Contains(errors, e => e.Contains("Games table ID"));
        }

        [Fact]
        public void Validate_TokenWithoutUrlReportsUrl()
        {
            var errors = new PluginConfig { NocoDbApiToken = "token" }.Validate();

            Assert.Contains(errors, e => e.Contains("Base URL is required"));
        }

        [Theory]
        [InlineData("nocodb.example.com")]
        [InlineData("ftp://nocodb.example.com")]
        [InlineData("not a url")]
        public void Validate_RejectsNonHttpBaseUrl(string url)
        {
            var errors = new PluginConfig
            {
                NocoDbBaseUrl = url,
                NocoDbApiToken = "token",
                NocoDbGamesTableId = "tbl"
            }.Validate();

            Assert.Contains(errors, e => e.Contains("http://"));
        }

        [Fact]
        public void Validate_RejectsNegativeMaxTags()
        {
            var errors = new PluginConfig { NocoDbMaxTags = -1 }.Validate();
            Assert.Contains(errors, e => e.Contains("negative"));
        }

        [Fact]
        public void Validate_ZeroMaxTagsIsAllowed()
        {
            Assert.Empty(new PluginConfig { NocoDbMaxTags = 0 }.Validate());
        }

        [Fact]
        public void Validate_PreferTavilyWithoutKeyIsReported()
        {
            var errors = new PluginConfig { PreferTavily = true }.Validate();
            Assert.Contains(errors, e => e.Contains("Tavily"));
        }

        [Theory]
        [InlineData("https://nocodb.example.com", true)]
        [InlineData("http://192.168.1.106:5555", true)]
        [InlineData("https://192.168.1.106:5555/", true)]
        [InlineData("  https://spaced.example.com  ", true)]
        [InlineData("ftp://example.com", false)]
        [InlineData("example.com", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsHttpUrl_ChecksScheme(string url, bool expected)
        {
            Assert.Equal(expected, PluginConfig.IsHttpUrl(url));
        }
    }

    public class PluginConfigViewModelTests
    {
        private static PluginConfigViewModel Create(
            PluginConfig loaded, List<PluginConfig> saved = null)
        {
            var sink = saved ?? new List<PluginConfig>();
            return new PluginConfigViewModel(() => loaded, config => sink.Add(config));
        }

        [Fact]
        public void Constructor_UsesDefaultsWhenNothingSaved()
        {
            var vm = new PluginConfigViewModel(() => null, config => { });

            Assert.NotNull(vm.Settings);
            Assert.Equal(30, vm.Settings.NocoDbMaxTags);
        }

        [Fact]
        public void Constructor_UsesSavedSettings()
        {
            var vm = Create(new PluginConfig { NocoDbBaseUrl = "https://saved.example.com" });

            Assert.Equal("https://saved.example.com", vm.Settings.NocoDbBaseUrl);
        }

        [Fact]
        public void CancelEdit_RestoresValuesFromBeforeEditing()
        {
            var vm = Create(new PluginConfig { NocoDbBaseUrl = "https://before.example.com" });

            vm.BeginEdit();
            vm.Settings.NocoDbBaseUrl = "https://typed-by-mistake.example.com";
            vm.Settings.NocoDbMaxTags = 5;
            vm.CancelEdit();

            Assert.Equal("https://before.example.com", vm.Settings.NocoDbBaseUrl);
            Assert.Equal(30, vm.Settings.NocoDbMaxTags);
        }

        [Fact]
        public void CancelEdit_WithoutBeginEdit_DoesNothing()
        {
            var vm = Create(new PluginConfig { NocoDbBaseUrl = "https://a.example.com" });

            vm.Settings.NocoDbBaseUrl = "https://b.example.com";
            vm.CancelEdit();

            Assert.Equal("https://b.example.com", vm.Settings.NocoDbBaseUrl);
        }

        [Fact]
        public void EndEdit_SavesCurrentSettings()
        {
            var saved = new List<PluginConfig>();
            var vm = Create(new PluginConfig(), saved);

            vm.BeginEdit();
            vm.Settings.NocoDbBaseUrl = "https://kept.example.com";
            vm.EndEdit();

            Assert.Single(saved);
            Assert.Equal("https://kept.example.com", saved[0].NocoDbBaseUrl);
        }

        [Fact]
        public void CancelEdit_AfterEndEdit_DoesNotRollBack()
        {
            var vm = Create(new PluginConfig());

            vm.BeginEdit();
            vm.Settings.NocoDbBaseUrl = "https://kept.example.com";
            vm.EndEdit();
            vm.CancelEdit();

            Assert.Equal("https://kept.example.com", vm.Settings.NocoDbBaseUrl);
        }

        [Fact]
        public void VerifySettings_PassesForValidConfig()
        {
            var vm = Create(new PluginConfig());

            List<string> errors;
            Assert.True(vm.VerifySettings(out errors));
            Assert.Empty(errors);
        }

        [Fact]
        public void VerifySettings_FailsAndListsProblems()
        {
            var vm = Create(new PluginConfig { NocoDbBaseUrl = "https://nocodb.example.com" });

            List<string> errors;
            Assert.False(vm.VerifySettings(out errors));
            Assert.Equal(2, errors.Count);
        }
    }
}
