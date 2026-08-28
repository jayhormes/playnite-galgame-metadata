using ErogameScapeMetadata.Services;
using Playnite.SDK.Data;
using System.Collections.Generic;
using System.Net.Http;
using Xunit;

namespace ErogameScapeMetadata.Tests
{
    public class NocoDbClientTests
    {
        [Theory]
        [InlineData("https://nocodb.example.com", "my-token", true)]
        [InlineData("https://nocodb.example.com", "", false)]
        [InlineData("", "my-token", false)]
        [InlineData(null, null, false)]
        [InlineData("   ", "   ", false)]
        public void IsConfigured_EvaluatesCorrectly(string baseUrl, string token, bool expected)
        {
            var client = new NocoDbClient(new HttpClient(), null, baseUrl, token);
            Assert.Equal(expected, client.IsConfigured);
        }

        [Fact]
        public void NocoDbRawGameRecord_DeserializesCorrectly()
        {
            var json = @"
            {
                ""list"": [
                    {
                        ""Id"": 2337,
                        ""Name"": ""モザイクの天使"",
                        ""原名"": ""Mosaic no Tenshi"",
                        ""發售日期"": ""2026-06-26"",
                        ""EGS 評分"": 80,
                        ""EGS 票數"": 76,
                        ""vndb 評分"": 71.9,
                        ""vndb 票數"": 27,
                        ""封面圖"": [
                            {
                                ""path"": ""download/2026/08/04/cover.jpg"",
                                ""signedPath"": ""dltemp/token/cover.jpg"",
                                ""title"": ""cover.jpg"",
                                ""mimetype"": ""image/jpeg""
                            }
                        ],
                        ""LTAR_開發品牌"": [
                            {
                                ""Id"": 373,
                                ""Name"": ""Frill""
                            }
                        ]
                    }
                ]
            }";

            var response = Serialization.FromJson<NocoDbListResponse<NocoDbRawGameRecord>>(json);

            Assert.NotNull(response);
            Assert.NotNull(response.List);
            Assert.Single(response.List);

            var game = response.List[0];
            Assert.Equal(2337, game.Id);
            Assert.Equal("モザイクの天使", game.Name);
            Assert.Equal("Mosaic no Tenshi", game.OriginalName);
            Assert.Equal("2026-06-26", game.ReleaseDate);
            Assert.Equal(80, game.EgsScore);
            Assert.Equal(76, game.EgsCount);
            Assert.Equal(71.9, game.VndbScore);
            Assert.Equal(27, game.VndbCount);

            Assert.NotNull(game.CoverAttachments);
            Assert.Single(game.CoverAttachments);
            Assert.Equal("dltemp/token/cover.jpg", game.CoverAttachments[0].SignedPath);

            Assert.NotNull(game.Brands);
            Assert.Single(game.Brands);
            Assert.Equal("Frill", game.Brands[0].Name);
        }

        [Fact]
        public void NocoDbTagRecord_DeserializesCorrectly()
        {
            var json = @"
            {
                ""list"": [
                    { ""Id"": 1, ""Name"": ""男性主角"" },
                    { ""Id"": 2, ""Name"": ""魔法學校"" }
                ]
            }";

            var response = Serialization.FromJson<NocoDbListResponse<NocoDbTagRecord>>(json);

            Assert.NotNull(response);
            Assert.NotNull(response.List);
            Assert.Equal(2, response.List.Count);
            Assert.Equal("男性主角", response.List[0].Name);
            Assert.Equal("魔法學校", response.List[1].Name);
        }
    }
}
