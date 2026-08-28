using ErogameScapeMetadata.Models;
using ErogameScapeMetadata.Services;
using System;
using System.Collections.Generic;
using Xunit;

namespace ErogameScapeMetadata.Tests
{
    // NocoDB（自建庫）の値を VNDB 由来のデータへ被せるマージ規則。HTTP を伴わない。
    public class NocoDbMergeTests
    {
        private static ErogameScapeApiClient Client(
            bool preferNocoTags = true, bool useNocoScores = true, int maxTags = 30)
        {
            return new ErogameScapeApiClient(null, new PluginConfig
            {
                PreferNocoDbTags = preferNocoTags,
                FallbackNocoDbScores = useNocoScores,
                NocoDbMaxTags = maxTags
            });
        }

        private static ErogameScapeGameInfo VndbGame()
        {
            return new ErogameScapeGameInfo
            {
                VndbId = "v63318",
                GameName = "モザイクの天使",
                BrandName = "Frill (VNDB)",
                SellDay = new DateTime(2026, 6, 1),
                VndbRating = 65.0,
                Tags = new List<string> { "Protagonist", "School Life" },
                VndbCoverImageUrl = "https://s2.vndb.org/cv/18/129006.jpg",
                VndbCoverIsPortrait = true
            };
        }

        private static NocoDbGameData Noco()
        {
            return new NocoDbGameData
            {
                GameId = 2337,
                Name = "モザイクの天使",
                BrandName = "Frill",
                ReleaseDate = new DateTime(2026, 6, 26),
                CoverImageUrl = "https://192.168.1.106:5555/dltemp/sig/1/cover.jpg",
                EgsScore = 80,
                EgsCount = 76,
                VndbScore = 71.9,
                VndbCount = 27,
                Tags = new List<string> { "教師女主角", "巨乳女主角" }
            };
        }

        // --- カバー画像 ---

        [Fact]
        public void ApplyNocoDbData_SetsNocoDbCoverAsTopCandidate()
        {
            var game = VndbGame();
            Client().ApplyNocoDbData(game, Noco());

            Assert.Equal("https://192.168.1.106:5555/dltemp/sig/1/cover.jpg", game.NocoDbCoverImageUrl);
            // VNDB カバーは候補として残る（NocoDB が 401/期限切れでも落ちないように）
            Assert.Equal(game.NocoDbCoverImageUrl, game.GetCoverImageCandidates()[0]);
            Assert.Contains("https://s2.vndb.org/cv/18/129006.jpg", game.GetCoverImageCandidates());
        }

        [Fact]
        public void ApplyNocoDbData_WithoutCover_KeepsVndbCover()
        {
            var game = VndbGame();
            var noco = Noco();
            noco.CoverImageUrl = null;

            Client().ApplyNocoDbData(game, noco);

            Assert.Null(game.NocoDbCoverImageUrl);
            Assert.Equal("https://s2.vndb.org/cv/18/129006.jpg", game.GetCoverImageUrl());
        }

        // --- スコア ---

        [Fact]
        public void ApplyNocoDbData_UsesNocoDbScores()
        {
            var game = VndbGame();
            game.EgsSnapshotDate = "20240101";

            Client().ApplyNocoDbData(game, Noco());

            Assert.Equal(80, game.Median);
            Assert.Equal(76, game.ReviewCount);
            Assert.Equal("nocodb", game.EgsSource);
            // NocoDB は live 値なので、以前の Wayback 快照日付は残さない
            Assert.Null(game.EgsSnapshotDate);
            Assert.Equal(71.9, game.VndbRating);
        }

        [Fact]
        public void ApplyNocoDbData_ScoresDisabled_LeavesScoresForWaybackOrTavily()
        {
            var game = VndbGame();
            Client(useNocoScores: false).ApplyNocoDbData(game, Noco());

            Assert.Null(game.Median);
            Assert.Null(game.ReviewCount);
            Assert.Null(game.EgsSource);
            // VNDB rating も NocoDB 側で上書きしない
            Assert.Equal(65.0, game.VndbRating);
            // タグと封面は FallbackNocoDbScores と無関係に適用される
            Assert.Equal(new[] { "教師女主角", "巨乳女主角" }, game.Tags);
            Assert.NotNull(game.NocoDbCoverImageUrl);
        }

        [Theory]
        [InlineData(null)]
        [InlineData(0)]
        public void ApplyNocoDbData_IgnoresMissingOrZeroEgsScore(int? score)
        {
            var game = VndbGame();
            var noco = Noco();
            noco.EgsScore = score;

            Client().ApplyNocoDbData(game, noco);

            Assert.Null(game.Median);
            Assert.Null(game.EgsSource);
        }

        [Fact]
        public void ApplyNocoDbData_IgnoresZeroVndbScore()
        {
            var game = VndbGame();
            var noco = Noco();
            noco.VndbScore = 0;

            Client().ApplyNocoDbData(game, noco);

            Assert.Equal(65.0, game.VndbRating);
        }

        // --- タグ ---

        [Fact]
        public void ApplyNocoDbData_PreferNocoTags_ReplacesVndbTags()
        {
            var game = VndbGame();
            Client(preferNocoTags: true).ApplyNocoDbData(game, Noco());

            Assert.Equal(new[] { "教師女主角", "巨乳女主角" }, game.Tags);
        }

        [Fact]
        public void ApplyNocoDbData_MergeTags_PutsNocoFirstThenVndb()
        {
            var game = VndbGame();
            Client(preferNocoTags: false).ApplyNocoDbData(game, Noco());

            Assert.Equal(
                new[] { "教師女主角", "巨乳女主角", "Protagonist", "School Life" },
                game.Tags);
        }

        [Fact]
        public void ApplyNocoDbData_EmptyNocoTags_KeepsVndbTags()
        {
            var game = VndbGame();
            var noco = Noco();
            noco.Tags = new List<string>();

            Client().ApplyNocoDbData(game, noco);

            Assert.Equal(new[] { "Protagonist", "School Life" }, game.Tags);
        }

        [Fact]
        public void MergeTags_DeduplicatesAcrossSources()
        {
            var merged = ErogameScapeApiClient.MergeTags(
                new List<string> { "共通", "NocoOnly" },
                new List<string> { "共通", "VndbOnly" },
                preferNoco: false,
                maxTags: 30);

            Assert.Equal(new[] { "共通", "NocoOnly", "VndbOnly" }, merged);
        }

        [Fact]
        public void MergeTags_CapsAtMaxTags()
        {
            var noco = new List<string>();
            for (var i = 0; i < 40; i++)
            {
                noco.Add($"tag{i}");
            }

            var merged = ErogameScapeApiClient.MergeTags(noco, new List<string>(), true, 30);

            Assert.Equal(30, merged.Count);
            Assert.Equal("tag0", merged[0]);
            Assert.Equal("tag29", merged[29]);
        }

        [Fact]
        public void MergeTags_ZeroMeansUnlimited()
        {
            var noco = new List<string>();
            for (var i = 0; i < 40; i++)
            {
                noco.Add($"tag{i}");
            }

            var merged = ErogameScapeApiClient.MergeTags(noco, new List<string>(), true, 0);

            Assert.Equal(40, merged.Count);
        }

        [Fact]
        public void MergeTags_NullInputsAreSafe()
        {
            Assert.Empty(ErogameScapeApiClient.MergeTags(null, null, true, 30));
            Assert.Equal(new[] { "a" }, ErogameScapeApiClient.MergeTags(null, new List<string> { "a" }, true, 30));
            Assert.Equal(new[] { "b" }, ErogameScapeApiClient.MergeTags(new List<string> { "b" }, null, true, 30));
        }

        // --- その他フィールド ---

        [Fact]
        public void ApplyNocoDbData_OverridesBrandAndReleaseDate()
        {
            var game = VndbGame();
            Client().ApplyNocoDbData(game, Noco());

            Assert.Equal("Frill", game.BrandName);
            Assert.Equal(new DateTime(2026, 6, 26), game.SellDay);
        }

        [Fact]
        public void ApplyNocoDbData_KeepsVndbValuesWhenNocoFieldsEmpty()
        {
            var game = VndbGame();
            var noco = Noco();
            noco.BrandName = null;
            noco.ReleaseDate = null;

            Client().ApplyNocoDbData(game, noco);

            Assert.Equal("Frill (VNDB)", game.BrandName);
            Assert.Equal(new DateTime(2026, 6, 1), game.SellDay);
        }

        [Fact]
        public void ApplyNocoDbData_UsesOriginalNameOnlyWhenAltNameMissing()
        {
            var game = VndbGame();
            var noco = Noco();
            noco.OriginalName = "Mosaic no Tenshi";

            Client().ApplyNocoDbData(game, noco);
            Assert.Equal("Mosaic no Tenshi", game.AltName);

            var withAlt = VndbGame();
            withAlt.AltName = "Mosaic Angel";
            Client().ApplyNocoDbData(withAlt, noco);
            Assert.Equal("Mosaic Angel", withAlt.AltName);
        }

        [Fact]
        public void ApplyNocoDbData_NullArgumentsAreSafe()
        {
            var game = VndbGame();
            Client().ApplyNocoDbData(game, null);
            Client().ApplyNocoDbData(null, Noco());

            Assert.Equal(new[] { "Protagonist", "School Life" }, game.Tags);
            Assert.Null(game.NocoDbCoverImageUrl);
        }

        // --- 設定の既定値 ---

        [Fact]
        public void PluginConfig_SslBypassDefaultsOff()
        {
            var config = new PluginConfig();

            Assert.False(config.NocoDbIgnoreSslErrors);
            Assert.True(config.PreferNocoDbTags);
            Assert.True(config.FallbackNocoDbScores);
            Assert.Equal(30, config.NocoDbMaxTags);
        }

        [Fact]
        public void NullConfig_DoesNotEnableSslBypass()
        {
            // config 読み込み失敗時に既定で検証を無効化しないこと
            var client = new ErogameScapeApiClient(null, null);

            Assert.NotNull(client);
            Assert.False(SslCertificateBypass.IsHostAllowed(""));
        }
    }
}
