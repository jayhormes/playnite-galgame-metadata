using ErogameScapeMetadata.Models;
using Xunit;

namespace ErogameScapeMetadata.Tests
{
    public class ErogameScapeGameInfoTests
    {
        [Fact]
        public void GetCoverImageCandidates_NocoDbCover_HasTopPriority()
        {
            var game = new ErogameScapeGameInfo
            {
                NocoDbCoverImageUrl = "https://nocodb.example.com/download/cover.jpg",
                VndbCoverImageUrl = "https://t.vndb.org/cv/portrait.jpg",
                VndbCoverIsPortrait = true,
                DmmId = "test_game_001"
            };

            var candidates = game.GetCoverImageCandidates();

            Assert.Equal(3, candidates.Count);
            Assert.Equal("https://nocodb.example.com/download/cover.jpg", candidates[0]);
            Assert.Equal("https://t.vndb.org/cv/portrait.jpg", candidates[1]);
            Assert.Equal("https://pics.dmm.co.jp/digital/pcgame/test_game_001/test_game_001pl.jpg", candidates[2]);
        }

        [Fact]
        public void GetCoverImageCandidates_PortraitVndb_PrioritizesVndbOverDmm()
        {
            var game = new ErogameScapeGameInfo
            {
                VndbCoverImageUrl = "https://t.vndb.org/cv/portrait.jpg",
                VndbCoverIsPortrait = true,
                DmmId = "test_game_001"
            };

            var candidates = game.GetCoverImageCandidates();

            Assert.Equal(2, candidates.Count);
            Assert.Equal("https://t.vndb.org/cv/portrait.jpg", candidates[0]);
            Assert.Equal("https://pics.dmm.co.jp/digital/pcgame/test_game_001/test_game_001pl.jpg", candidates[1]);
        }

        [Fact]
        public void GetCoverImageCandidates_LandscapeVndb_PrioritizesDmmThenLandscapeVndb()
        {
            var game = new ErogameScapeGameInfo
            {
                VndbCoverImageUrl = "https://t.vndb.org/cv/landscape.jpg",
                VndbCoverIsPortrait = false,
                DmmId = "test_game_002"
            };

            var candidates = game.GetCoverImageCandidates();

            Assert.Equal(2, candidates.Count);
            Assert.Equal("https://pics.dmm.co.jp/digital/pcgame/test_game_002/test_game_002pl.jpg", candidates[0]);
            Assert.Equal("https://t.vndb.org/cv/landscape.jpg", candidates[1]);
        }

        [Fact]
        public void GetCoverImageCandidates_LandscapeVndbOnly_ReturnsVndbAsFallback()
        {
            var game = new ErogameScapeGameInfo
            {
                VndbCoverImageUrl = "https://t.vndb.org/cv/landscape.jpg",
                VndbCoverIsPortrait = false,
                DmmId = null
            };

            var candidates = game.GetCoverImageCandidates();

            Assert.Single(candidates);
            Assert.Equal("https://t.vndb.org/cv/landscape.jpg", candidates[0]);
        }

        [Fact]
        public void GetCoverImageCandidates_EmptyCovers_ReturnsEmptyList()
        {
            var game = new ErogameScapeGameInfo();

            var candidates = game.GetCoverImageCandidates();

            Assert.Empty(candidates);
            Assert.Null(game.GetCoverImageUrl());
        }

        [Fact]
        public void GetErogameScapeUrl_WhenIdPresent_ReturnsUrl()
        {
            var game = new ErogameScapeGameInfo { EgsId = 12345 };
            Assert.Equal("https://erogamescape.dyndns.org/~ap2/ero/toukei_kaiseki/game.php?game=12345", game.GetErogameScapeUrl());
        }

        [Fact]
        public void GetErogameScapeUrl_WhenIdNull_ReturnsNull()
        {
            var game = new ErogameScapeGameInfo { EgsId = null };
            Assert.Null(game.GetErogameScapeUrl());
        }

        [Fact]
        public void GetVndbUrl_WhenIdPresent_ReturnsUrl()
        {
            var game = new ErogameScapeGameInfo { VndbId = "v1234" };
            Assert.Equal("https://vndb.org/v1234", game.GetVndbUrl());
        }

        [Fact]
        public void GetVndbUrl_WhenIdNull_ReturnsNull()
        {
            var game = new ErogameScapeGameInfo { VndbId = null };
            Assert.Null(game.GetVndbUrl());
        }
    }
}
