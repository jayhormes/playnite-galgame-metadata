using ErogameScapeMetadata.Models;
using ErogameScapeMetadata.Services;
using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using Xunit;

namespace ErogameScapeMetadata.Tests
{
    public class ErogameScapeApiClientTests
    {
        [Theory]
        [InlineData("アマカノ２", "アマカノ2")]
        [InlineData("ＡＢＣ　１２３", "abc 123")]
        // U+FF01-U+FF5E の全角記号は半角化されるため ＊ → *（比較用なので両辺が同じ規則なら問題ない）
        [InlineData("千恋＊万花　通常版", "千恋*万花 通常版")]
        [InlineData("WHITE ALBUM2　～Closing Chapter～", "white album2 ~closing chapter~")]
        [InlineData("WHITE ALBUM2 〜Closing Chapter〜", "white album2 ~closing chapter~")]
        [InlineData("  Fate  /  stay   night  ", "fate / stay night")]
        public void NormalizeForComparison_NormalizesCorrectly(string input, string expected)
        {
            var actual = ErogameScapeApiClient.NormalizeForComparison(input);
            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData("アマカノ2 [初回限定版]", "アマカノ2")]
        [InlineData("千恋＊万花 (DL版)", "千恋*万花")]
        [InlineData("グリザイアの果実【パッケージ版】", "グリザイアの果実")]
        [InlineData("CLANNAD - Windows 10対応版", "clannad")]
        [InlineData("サノバウィッチ（通常版）", "サノバウィッチ")]
        public void NormalizeAndStripSuffix_StripsBracketsAndTags(string input, string expected)
        {
            var actual = ErogameScapeApiClient.NormalizeAndStripSuffix(input);
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void ParseDate_WithFullDate_ReturnsCorrectDateTime()
        {
            var date = ErogameScapeApiClient.ParseDate("2024-05-31");
            Assert.NotNull(date);
            Assert.Equal(new DateTime(2024, 5, 31), date.Value);
        }

        [Fact]
        public void ParseDate_WithYearMonth_ReturnsFirstDayOfMonth()
        {
            var date = ErogameScapeApiClient.ParseDate("2024-05");
            Assert.NotNull(date);
            Assert.Equal(new DateTime(2024, 5, 1), date.Value);
        }

        [Fact]
        public void ParseDate_WithYearOnly_ReturnsFirstDayOfYear()
        {
            var date = ErogameScapeApiClient.ParseDate("2024");
            Assert.NotNull(date);
            Assert.Equal(new DateTime(2024, 1, 1), date.Value);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("tba")]
        [InlineData("TBA")]
        [InlineData("2035-01-01")]
        [InlineData("invalid-date")]
        public void ParseDate_WithInvalidOrFutureTba_ReturnsNull(string input)
        {
            var date = ErogameScapeApiClient.ParseDate(input);
            Assert.Null(date);
        }

        [Fact]
        public void ApplyExtLinks_ResolvesEgsId_FromVariousUrlPatterns()
        {
            var client = new ErogameScapeApiClient(null, null);
            var game = new ErogameScapeGameInfo();

            var releases = new List<VndbRelease>
            {
                new VndbRelease
                {
                    ExtLinks = new List<VndbExtLink>
                    {
                        new VndbExtLink { Url = "https://erogamescape.dyndns.org/~ap2/ero/toukei_kaiseki/game.php?game=12345" }
                    }
                }
            };

            client.ApplyExtLinks(game, releases);

            Assert.Equal(12345, game.EgsId);
        }

        [Fact]
        public void ApplyExtLinks_ResolvesDlsiteProAndManiax()
        {
            var client = new ErogameScapeApiClient(null, null);
            var game = new ErogameScapeGameInfo();

            var releases = new List<VndbRelease>
            {
                new VndbRelease
                {
                    ExtLinks = new List<VndbExtLink>
                    {
                        new VndbExtLink { Url = "https://www.dlsite.com/pro/work/=/product_id/VJ012345.html" }
                    }
                }
            };

            client.ApplyExtLinks(game, releases);

            Assert.Equal("pro", game.DlsiteDomain);
            Assert.Equal("VJ012345", game.DlsiteId);
        }

        [Fact]
        public void ApplyExtLinks_ResolvesDmmCidAndStandardUrls()
        {
            var client = new ErogameScapeApiClient(null, null);
            var game = new ErogameScapeGameInfo();

            var releases = new List<VndbRelease>
            {
                new VndbRelease
                {
                    ExtLinks = new List<VndbExtLink>
                    {
                        new VndbExtLink { Url = "https://www.dmm.co.jp/dc/pcgame/-/detail/=/cid=h_1234test001/" }
                    }
                }
            };

            client.ApplyExtLinks(game, releases);

            Assert.Equal("h_1234test001", game.DmmId);
        }

        [Fact]
        public void ApplyExtLinks_ResolvesGetchuIdDirectly()
        {
            var client = new ErogameScapeApiClient(null, null);
            var game = new ErogameScapeGameInfo();

            var releases = new List<VndbRelease>
            {
                new VndbRelease
                {
                    ExtLinks = new List<VndbExtLink>
                    {
                        new VndbExtLink { Url = "https://www.getchu.com/soft.phtml?id=987654" }
                    }
                }
            };

            client.ApplyExtLinks(game, releases);

            Assert.Equal("987654", game.GetchuId);
        }

        [Fact]
        public void ApplyExtLinks_CalculatesMaxMinAgeFromReleases()
        {
            var client = new ErogameScapeApiClient(null, null);
            var game = new ErogameScapeGameInfo();

            var releases = new List<VndbRelease>
            {
                new VndbRelease { MinAge = 15 },
                new VndbRelease { MinAge = 18 },
                new VndbRelease { MinAge = 0 }
            };

            client.ApplyExtLinks(game, releases);

            Assert.Equal(18, game.MinAge);
        }

        [Fact]
        public void ApplyVndbImages_FiltersUnsafeAndAssignsPortrait()
        {
            var game = new ErogameScapeGameInfo();
            var vn = new VndbVn
            {
                Image = new VndbImage
                {
                    Url = "https://t.vndb.org/cv/123.jpg",
                    Dims = new List<int> { 600, 800 }, // height 800 > width 600 -> portrait
                    Sexual = 0.0,
                    Violence = 0.0
                },
                Screenshots = new List<VndbImage>
                {
                    new VndbImage { Url = "https://t.vndb.org/sf/safe1.jpg", Sexual = 0.0, Violence = 0.0 },
                    new VndbImage { Url = "https://t.vndb.org/sf/unsafe.jpg", Sexual = 2.0, Violence = 0.0 }
                }
            };

            ErogameScapeApiClient.ApplyVndbImages(game, vn);

            Assert.Equal("https://t.vndb.org/cv/123.jpg", game.VndbCoverImageUrl);
            Assert.True(game.VndbCoverIsPortrait);
            Assert.Single(game.BackgroundImageUrls);
            Assert.Equal("https://t.vndb.org/sf/safe1.jpg", game.BackgroundImageUrls[0]);
        }

        [Fact]
        public void ApplyVndbTags_FiltersByCategoryAndSpoiler()
        {
            var game = new ErogameScapeGameInfo();
            var vn = new VndbVn
            {
                Tags = new List<VndbTag>
                {
                    new VndbTag { Name = "School Life", Category = "cont", Rating = 2.8, Spoiler = 0.0 },
                    new VndbTag { Name = "Romance", Category = "cont", Rating = 3.0, Spoiler = 0.0 },
                    new VndbTag { Name = "Heavy Spoiler Tag", Category = "cont", Rating = 2.9, Spoiler = 1.0 }, // Spoiler >= 0.5
                    new VndbTag { Name = "Low Rating Tag", Category = "cont", Rating = 1.2, Spoiler = 0.0 }, // Rating < 2.0
                    new VndbTag { Name = "Ero Tag", Category = "ero", Rating = 3.0, Spoiler = 0.0 } // Category != cont
                }
            };

            ErogameScapeApiClient.ApplyVndbTags(game, vn);

            Assert.Equal(2, game.Tags.Count);
            Assert.Equal("Romance", game.Tags[0]);
            Assert.Equal("School Life", game.Tags[1]);
        }

        [Fact]
        public void ExtractJsonString_DecodesJsonEscapesAndUnicode()
        {
            var json = "{\"intro_s\":\"Line 1\\nLine 2\\t\\\"Quoted\\\" \\u3042\\u3044\\u3046\"}";
            var value = ErogameScapeApiClient.ExtractJsonString(json, "intro_s");

            Assert.Equal("Line 1\nLine 2\t\"Quoted\" あいう", value);
        }

        [Fact]
        public void ExtractGetchuSection_ExtractsAndCleansStoryText()
        {
            var html = @"
                <div>
                    <h2 class=""tabletitle"">ストーリー</h2>
                    <div class=""tablebody"">
                        <span class=""bootstrap"">主人公はある日、不思議な少女に出会う。<br><br>それは運命の始まりだった。&amp;copy;</span>
                    </div>
                </div>";

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var story = ErogameScapeApiClient.ExtractGetchuSection(doc, "ストーリー");

            Assert.NotNull(story);
            Assert.Contains("主人公はある日、不思議な少女に出会う。", story);
            Assert.Contains("それは運命の始まりだった。&copy;", story);
        }

        [Fact]
        public void EscapeEucJp_EncodesJapaneseCharacters()
        {
            var encoded = ErogameScapeApiClient.EscapeEucJp("アマカノ");
            // EUC-JP bytes for "アマカノ": %A5%A2%A5%DE%A5%AB%A5%CE
            Assert.Equal("%A5%A2%A5%DE%A5%AB%A5%CE", encoded);
        }
    }
}
