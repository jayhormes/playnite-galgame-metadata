using ErogameScapeMetadata.Services;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.Net.Http;
using Xunit;

namespace ErogameScapeMetadata.Tests
{
    public class NocoDbClientTests : SerializationTestBase
    {
        private const string Base = "https://192.168.1.106:5555";
        private const string Table = "mk760gpjbh7yrqh";

        [Theory]
        [InlineData("https://nocodb.example.com", "my-token", "tbl", true)]
        [InlineData("https://nocodb.example.com", "", "tbl", false)]
        [InlineData("", "my-token", "tbl", false)]
        [InlineData(null, null, null, false)]
        [InlineData("   ", "   ", "   ", false)]
        // テーブル ID が無いと照会不能。IsConfigured は false であるべき
        [InlineData("https://nocodb.example.com", "my-token", "", false)]
        public void IsConfigured_EvaluatesCorrectly(
            string baseUrl, string token, string tableId, bool expected)
        {
            var client = new NocoDbClient(new HttpClient(), null, baseUrl, token, tableId);
            Assert.Equal(expected, client.IsConfigured);
        }

        [Theory]
        [InlineData("https://192.168.1.106:5555/download/a.jpg", true)]
        [InlineData("HTTPS://192.168.1.106:5555/download/a.jpg", true)]
        [InlineData("https://api.vndb.org/kana/vn", false)]
        [InlineData("https://s2.vndb.org/cv/18/12345.jpg", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsNocoDbUrl_MatchesOnlyOwnBaseUrl(string url, bool expected)
        {
            var client = new NocoDbClient(new HttpClient(), null, Base, "token", Table);
            Assert.Equal(expected, client.IsNocoDbUrl(url));
        }

        // --- where 条件（実 DB 2,332 件の URL 形式調査に基づく） ---

        [Fact]
        public void BuildWhereConditions_OrdersVndbThenEgsThenName()
        {
            var conditions = NocoDbClient.BuildWhereConditions("v63318", 40432, "モザイクの天使");

            Assert.Equal(3, conditions.Count);
            // vndb URL は "https://vndb.org/vN" に統一されているので完全一致で引く
            Assert.Equal("(vndb URL,eq,https://vndb.org/v63318)", conditions[0]);
            // EGS URL は http/https/旧パス混在のため末尾一致
            Assert.Equal("(EGS URL,like,%game=40432)", conditions[1]);
            Assert.Equal("(Name,eq,モザイクの天使)", conditions[2]);
        }

        [Fact]
        public void BuildWhereConditions_EgsPatternIsSuffixAnchored()
        {
            var conditions = NocoDbClient.BuildWhereConditions(null, 4043, null);

            // 末尾に % を付けない = game=40432 のような前方一致の誤爆を防ぐ
            var condition = Assert.Single(conditions);
            Assert.Equal("(EGS URL,like,%game=4043)", condition);
            Assert.DoesNotContain("game=4043%", condition);
        }

        [Theory]
        [InlineData(null, null, null, 0)]
        [InlineData("v63318", null, null, 1)]
        [InlineData(null, 40432, null, 1)]
        [InlineData(null, null, "名前", 1)]
        [InlineData("", 0, "", 1)]
        public void BuildWhereConditions_SkipsMissingIdentifiers(
            string vndbId, int? egsId, string gameName, int expectedCount)
        {
            var conditions = NocoDbClient.BuildWhereConditions(vndbId, egsId, gameName);
            Assert.Equal(expectedCount, conditions.Count);
        }

        // NocoDB は括弧・カンマを含む値をそのまま受け付ける（実 DB で検証済み）。
        // 削除やバックスラッシュ escape をすると一致しなくなる。
        [Theory]
        [InlineData("彼女は高天(そら)に祈らない -quantum girlfriend-")]
        [InlineData("蒼の彼方のフォーリズム ‐Beyond the sky, into the firmament‐")]
        [InlineData("Chu(治癒)してあげちゃう ～押しかけお姉さんの性交恥療～")]
        public void BuildWhereConditions_KeepsParenthesesAndCommasInName(string name)
        {
            var conditions = NocoDbClient.BuildWhereConditions(null, null, name);

            Assert.Equal($"(Name,eq,{name})", Assert.Single(conditions));
        }

        [Theory]
        [InlineData("  モザイクの天使  ", "モザイクの天使")]
        [InlineData("plain", "plain")]
        [InlineData(null, "")]
        [InlineData("   ", "")]
        public void SanitizeFilterValue_TrimsOnly(string input, string expected)
        {
            Assert.Equal(expected, NocoDbClient.SanitizeFilterValue(input));
        }

        // --- URL 構築 ---

        [Fact]
        public void BuildRecordsUrl_EncodesWhereAndFieldsAndSorts()
        {
            var url = NocoDbClient.BuildRecordsUrl(Base, Table, "(Name,eq,テスト)");

            Assert.StartsWith($"{Base}/api/v2/tables/{Table}/records?", url);
            Assert.Contains("where=%28Name%2Ceq%2C", url);
            // limit=1 の結果を決定的にする
            Assert.Contains("&sort=Id&limit=1", url);
            // 日本語/繁体字の列名がエンコードされている
            Assert.Contains(Uri.EscapeDataString("EGS 評分"), url);
            Assert.Contains(Uri.EscapeDataString("封面圖"), url);
            Assert.Contains(Uri.EscapeDataString("LTAR_開發品牌"), url);
            Assert.DoesNotContain(" ", url);
        }

        [Fact]
        public void BuildLinksUrl_UsesV2LinksEndpoint()
        {
            var url = NocoDbClient.BuildLinksUrl(Base, Table, "cjna85stylyh7g8", 2337);

            Assert.Equal(
                $"{Base}/api/v2/tables/{Table}/links/cjna85stylyh7g8/records/2337?fields=Id,Name&limit=100",
                url);
        }

        // --- 添付ファイル URL ---

        [Fact]
        public void BuildAttachmentUrl_PrefersSignedPath()
        {
            var attachments = new List<NocoDbAttachment>
            {
                new NocoDbAttachment
                {
                    Path = "download/2026/08/04/hash/cover.jpg",
                    SignedPath = "dltemp/sig/1787911800000/cover.jpg"
                }
            };

            Assert.Equal($"{Base}/dltemp/sig/1787911800000/cover.jpg",
                NocoDbClient.BuildAttachmentUrl(Base, attachments));
        }

        [Fact]
        public void BuildAttachmentUrl_FallsBackToPath()
        {
            var attachments = new List<NocoDbAttachment>
            {
                new NocoDbAttachment { Path = "download/2026/08/04/hash/cover.jpg" }
            };

            Assert.Equal($"{Base}/download/2026/08/04/hash/cover.jpg",
                NocoDbClient.BuildAttachmentUrl(Base, attachments));
        }

        [Fact]
        public void BuildAttachmentUrl_AvoidsDoubleSlash()
        {
            var attachments = new List<NocoDbAttachment>
            {
                new NocoDbAttachment { Path = "/download/cover.jpg" }
            };

            Assert.Equal($"{Base}/download/cover.jpg",
                NocoDbClient.BuildAttachmentUrl(Base + "/", attachments));
        }

        [Fact]
        public void BuildAttachmentUrl_ReturnsNullWhenNoUsablePath()
        {
            Assert.Null(NocoDbClient.BuildAttachmentUrl(Base, null));
            Assert.Null(NocoDbClient.BuildAttachmentUrl(Base, new List<NocoDbAttachment>()));
            Assert.Null(NocoDbClient.BuildAttachmentUrl(Base,
                new List<NocoDbAttachment> { new NocoDbAttachment() }));
            Assert.Null(NocoDbClient.BuildAttachmentUrl("",
                new List<NocoDbAttachment> { new NocoDbAttachment { Path = "download/a.jpg" } }));
        }

        // --- タグ結合 ---

        [Fact]
        public void AppendTags_DeduplicatesAndSkipsBlank()
        {
            var target = new List<string> { "教師女主角" };
            NocoDbClient.AppendTags(target, new List<string> { "巨乳女主角", "教師女主角", "", "  ", null });

            Assert.Equal(new[] { "教師女主角", "巨乳女主角" }, target);
        }

        [Fact]
        public void AppendTags_NullArgumentsAreSafe()
        {
            var target = new List<string> { "a" };
            NocoDbClient.AppendTags(target, null);
            NocoDbClient.AppendTags(null, new List<string> { "b" });

            Assert.Single(target);
        }

        // --- 発売日 ---

        [Theory]
        [InlineData("2026-06-26", 2026, 6, 26)]
        [InlineData("2015-03-14", 2015, 3, 14)]
        public void ParseReleaseDate_ParsesIsoDate(string value, int y, int m, int d)
        {
            var parsed = NocoDbClient.ParseReleaseDate(value);

            Assert.NotNull(parsed);
            Assert.Equal(new DateTime(y, m, d), parsed.Value.Date);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("未定")]
        public void ParseReleaseDate_ReturnsNullForUnparsable(string value)
        {
            Assert.Null(NocoDbClient.ParseReleaseDate(value));
        }

        // --- デシリアライズ（実 DB のレスポンス形状） ---

        [Fact]
        public void NocoDbRawGameRecord_DeserializesCorrectly()
        {
            var json = @"
            {
                ""list"": [
                    {
                        ""Id"": 2337,
                        ""Name"": ""モザイクの天使"",
                        ""原名"": null,
                        ""發售日期"": ""2026-06-26"",
                        ""EGS 評分"": 80,
                        ""EGS 票數"": 76,
                        ""vndb 評分"": 71.9,
                        ""vndb 票數"": 27,
                        ""封面圖"": [
                            {
                                ""path"": ""download/2026/08/04/hash/gb_cover_v63318.jpg"",
                                ""title"": ""gb_cover_v63318.jpg"",
                                ""mimetype"": ""image/jpeg"",
                                ""size"": 162824,
                                ""width"": 611,
                                ""height"": 800,
                                ""id"": ""at189vhag8kl6nkg"",
                                ""thumbnails"": { ""tiny"": { ""signedPath"": ""dltemp/x/1/tiny.jpg"" } },
                                ""signedPath"": ""dltemp/sig/1787911800000/gb_cover_v63318.jpg""
                            }
                        ],
                        ""LTAR_開發品牌"": [ { ""Id"": 373, ""Name"": ""Frill"" } ]
                    }
                ]
            }";

            var response = Serialization.FromJson<NocoDbListResponse<NocoDbRawGameRecord>>(json);

            Assert.NotNull(response?.List);
            var game = Assert.Single(response.List);
            Assert.Equal(2337, game.Id);
            Assert.Equal("モザイクの天使", game.Name);
            Assert.Null(game.OriginalName);
            Assert.Equal("2026-06-26", game.ReleaseDate);
            Assert.Equal(80, game.EgsScore);
            Assert.Equal(76, game.EgsCount);
            Assert.Equal(71.9, game.VndbScore);
            Assert.Equal(27, game.VndbCount);

            var attachment = Assert.Single(game.CoverAttachments);
            Assert.Equal("dltemp/sig/1787911800000/gb_cover_v63318.jpg", attachment.SignedPath);
            Assert.Equal("Frill", Assert.Single(game.Brands).Name);

            // 実データ形状からそのままカバー URL が組めること
            Assert.Equal($"{Base}/dltemp/sig/1787911800000/gb_cover_v63318.jpg",
                NocoDbClient.BuildAttachmentUrl(Base, game.CoverAttachments));
        }

        [Fact]
        public void NocoDbRawGameRecord_HandlesNullScoresAndMissingLinks()
        {
            var json = @"{ ""list"": [ { ""Id"": 5, ""Name"": ""未評価ゲーム"" } ] }";

            var response = Serialization.FromJson<NocoDbListResponse<NocoDbRawGameRecord>>(json);
            var game = Assert.Single(response.List);

            Assert.Null(game.EgsScore);
            Assert.Null(game.VndbScore);
            Assert.Null(game.CoverAttachments);
            Assert.Null(game.Brands);
            Assert.Null(NocoDbClient.BuildAttachmentUrl(Base, game.CoverAttachments));
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

            Assert.Equal(2, response.List.Count);
            Assert.Equal("男性主角", response.List[0].Name);
            Assert.Equal("魔法學校", response.List[1].Name);
        }
    }
}
