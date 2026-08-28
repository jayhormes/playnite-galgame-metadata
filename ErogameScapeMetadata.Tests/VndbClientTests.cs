using ErogameScapeMetadata.Services;
using System.Collections.Generic;
using Xunit;

namespace ErogameScapeMetadata.Tests
{
    public class VndbClientTests
    {
        [Theory]
        [InlineData("Fate/stay night", "\"Fate/stay night\"")]
        [InlineData("Quote \"Test\"", "\"Quote \\\"Test\\\"\"")]
        [InlineData("Path\\To\\File", "\"Path\\\\To\\\\File\"")]
        [InlineData("Line1\nLine2\r\tTab", "\"Line1\\nLine2\\r\\tTab\"")]
        [InlineData(null, "\"\"")]
        [InlineData("", "\"\"")]
        public void EscapeJsonString_EscapesSpecialCharacters(string input, string expected)
        {
            var actual = VndbClient.EscapeJsonString(input);
            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData(0.0, 0.0, true)]
        [InlineData(0.9, 0.9, true)]
        [InlineData(1.0, 0.0, false)] // Sexual >= 1.0 -> not safe
        [InlineData(0.0, 1.0, false)] // Violence >= 1.0 -> not safe
        [InlineData(null, 0.0, false)] // Null default >= 1.0 -> not safe
        public void VndbImage_IsSafe_EvaluatesCorrectly(double? sexual, double? violence, bool expected)
        {
            var image = new VndbImage { Sexual = sexual, Violence = violence };
            Assert.Equal(expected, image.IsSafe);
        }

        [Fact]
        public void VndbImage_IsPortrait_EvaluatesCorrectly()
        {
            var portrait = new VndbImage { Dims = new List<int> { 600, 800 } };
            var landscape = new VndbImage { Dims = new List<int> { 1920, 1080 } };
            var square = new VndbImage { Dims = new List<int> { 500, 500 } };
            var invalid = new VndbImage { Dims = null };

            Assert.True(portrait.IsPortrait);
            Assert.False(landscape.IsPortrait);
            Assert.False(square.IsPortrait);
            Assert.False(invalid.IsPortrait);
        }

        [Fact]
        public void VndbVn_DisplayName_PrefersAltTitle()
        {
            var vnWithAlt = new VndbVn
            {
                Title = "Fate/stay night",
                AltTitle = "フェイト/ステイナイト"
            };

            var vnWithoutAlt = new VndbVn
            {
                Title = "Clannad",
                AltTitle = null
            };

            Assert.Equal("フェイト/ステイナイト", vnWithAlt.DisplayName);
            Assert.Equal("Clannad", vnWithoutAlt.DisplayName);
        }

        [Fact]
        public void VndbDeveloper_DisplayName_PrefersOriginal()
        {
            var devWithOriginal = new VndbDeveloper
            {
                Name = "Key",
                Original = "キー"
            };

            var devWithoutOriginal = new VndbDeveloper
            {
                Name = "Yuzusoft",
                Original = null
            };

            Assert.Equal("キー", devWithOriginal.DisplayName);
            Assert.Equal("Yuzusoft", devWithoutOriginal.DisplayName);
        }
    }
}
