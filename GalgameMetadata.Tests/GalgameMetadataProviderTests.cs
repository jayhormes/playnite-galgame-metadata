using GalgameMetadata.Models;
using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace GalgameMetadata.Tests
{
    public class GalgameMetadataProviderTests
    {
        private GalgameMetadataProvider CreateProviderWithGame(GalgameInfo game)
        {
            var options = new MetadataRequestOptions(new Game("Test Game"), false);
            var provider = new GalgameMetadataProvider(options, null);

            // リフレクション経由で内部の _matchedGame と _searchCompleted を設定
            var matchedField = typeof(GalgameMetadataProvider)
                .GetField("_matchedGame", BindingFlags.NonPublic | BindingFlags.Instance);
            var searchCompletedField = typeof(GalgameMetadataProvider)
                .GetField("_searchCompleted", BindingFlags.NonPublic | BindingFlags.Instance);

            matchedField.SetValue(provider, game);
            searchCompletedField.SetValue(provider, true);

            return provider;
        }

        [Fact]
        public void GetCommunityScore_WithValidEgsScore_ReturnsEgsMedian()
        {
            var game = new GalgameInfo
            {
                Median = 85,
                ReviewCount = 100,
                VndbRating = 79.5
            };
            var provider = CreateProviderWithGame(game);

            var score = provider.GetCommunityScore(new GetMetadataFieldArgs());

            Assert.Equal(85, score);
        }

        [Fact]
        public void GetCommunityScore_WithZeroEgsScore_FallsBackToVndbRating()
        {
            var game = new GalgameInfo
            {
                Median = 0,
                ReviewCount = 0,
                VndbRating = 82.4
            };
            var provider = CreateProviderWithGame(game);

            var score = provider.GetCommunityScore(new GetMetadataFieldArgs());

            Assert.Equal(82, score);
        }

        [Fact]
        public void GetCommunityScore_WithoutEgsScore_FallsBackToVndbRating()
        {
            var game = new GalgameInfo
            {
                Median = null,
                ReviewCount = null,
                VndbRating = 75.6
            };
            var provider = CreateProviderWithGame(game);

            var score = provider.GetCommunityScore(new GetMetadataFieldArgs());

            Assert.Equal(76, score);
        }

        [Fact]
        public void GetReleaseDate_ReturnsCorrectPlayniteReleaseDate()
        {
            var game = new GalgameInfo
            {
                SellDay = new DateTime(2024, 7, 26)
            };
            var provider = CreateProviderWithGame(game);

            var releaseDate = provider.GetReleaseDate(new GetMetadataFieldArgs());

            Assert.NotNull(releaseDate);
            Assert.Equal(2024, releaseDate.Value.Year);
            Assert.Equal(7, releaseDate.Value.Month);
            Assert.Equal(26, releaseDate.Value.Day);
        }

        [Theory]
        [InlineData(18, "18+")]
        [InlineData(15, "全年齢")]
        [InlineData(0, "全年齢")]
        public void GetAgeRatings_MapsCorrectly(int minAge, string expectedRating)
        {
            var game = new GalgameInfo { MinAge = minAge };
            var provider = CreateProviderWithGame(game);

            var ratings = provider.GetAgeRatings(new GetMetadataFieldArgs())?.ToList();

            Assert.NotNull(ratings);
            Assert.Single(ratings);
            Assert.Equal(expectedRating, (ratings[0] as MetadataNameProperty)?.Name);
        }

        [Fact]
        public void GetDevelopersAndPublishers_ReturnsBrandName()
        {
            var game = new GalgameInfo { BrandName = "あざらしそふと" };
            var provider = CreateProviderWithGame(game);

            var developers = provider.GetDevelopers(new GetMetadataFieldArgs())?.ToList();
            var publishers = provider.GetPublishers(new GetMetadataFieldArgs())?.ToList();

            Assert.NotNull(developers);
            Assert.Equal("あざらしそふと", (developers[0] as MetadataNameProperty)?.Name);
            Assert.NotNull(publishers);
            Assert.Equal("あざらしそふと", (publishers[0] as MetadataNameProperty)?.Name);
        }

        [Fact]
        public void GetLinks_GeneratesAllAvailableLinks()
        {
            var game = new GalgameInfo
            {
                EgsId = 1234,
                VndbId = "v5678",
                DlsiteId = "VJ012345",
                DlsiteDomain = "pro",
                DmmId = "dmm_001"
            };
            var provider = CreateProviderWithGame(game);

            var links = provider.GetLinks(new GetMetadataFieldArgs())?.ToList();

            Assert.NotNull(links);
            Assert.Equal(4, links.Count);
            Assert.Contains(links, l => l.Name == "ErogameScape" && l.Url.Contains("game=1234"));
            Assert.Contains(links, l => l.Name == "VNDB" && l.Url == "https://vndb.org/v5678");
            Assert.Contains(links, l => l.Name == "DLsite" && l.Url.Contains("VJ012345.html"));
            Assert.Contains(links, l => l.Name == "DMM" && l.Url.Contains("dmm_001"));
        }

        [Fact]
        public void GetTagsAndGenres_ReturnsMappedProperties()
        {
            var game = new GalgameInfo
            {
                Tags = new List<string> { "純愛", "学園" },
                Genres = new List<string> { "ADV", "ビジュアルノベル" }
            };
            var provider = CreateProviderWithGame(game);

            var tags = provider.GetTags(new GetMetadataFieldArgs())?.ToList();
            var genres = provider.GetGenres(new GetMetadataFieldArgs())?.ToList();

            Assert.NotNull(tags);
            Assert.Equal(2, tags.Count);
            Assert.Equal("純愛", (tags[0] as MetadataNameProperty)?.Name);

            Assert.NotNull(genres);
            Assert.Equal(2, genres.Count);
            Assert.Equal("ADV", (genres[0] as MetadataNameProperty)?.Name);
        }
    }
}
