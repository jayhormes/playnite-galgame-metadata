using GalgameMetadata.Services;
using Xunit;

namespace GalgameMetadata.Tests
{
    public class EgsStatsClientTests
    {
        [Fact]
        public void GetGameUrl_ReturnsStandardEgsUrl()
        {
            var url = EgsStatsClient.GetGameUrl(12345);
            Assert.Equal("https://erogamescape.dyndns.org/~ap2/ero/toukei_kaiseki/game.php?game=12345", url);
        }

        [Fact]
        public void ParseStats_WithModernTwitterDataText_ReturnsCorrectStats()
        {
            var html = @"
                <!DOCTYPE html>
                <html>
                <head><title>アマカノ2</title></head>
                <body>
                    <div class=""twitter-share"">
                        <a href=""https://twitter.com/share"" class=""twitter-share-button""
                           data-text=""アマカノ2 (あざらしそふと) 中央値:85 データ数:120 標準偏差:12 #erogamescape"">Tweet</a>
                    </div>
                </body>
                </html>";

            var stats = EgsStatsClient.ParseStats(html);

            Assert.NotNull(stats);
            Assert.Equal(85, stats.Median);
            Assert.Equal(120, stats.Count);
            Assert.Equal(12, stats.StdDev);
        }

        [Fact]
        public void ParseStats_WithHtmlEncodedTwitterDataText_DecodesAndParsesCorrectly()
        {
            var html = @"<a class=""twitter-share-button"" data-text=""&quot;タイトル&quot; 中央値&#58;88 データ数&#58;45 標準偏差&#58;10 #erogamescape""></a>";

            var stats = EgsStatsClient.ParseStats(html);

            Assert.NotNull(stats);
            Assert.Equal(88, stats.Median);
            Assert.Equal(45, stats.Count);
            Assert.Equal(10, stats.StdDev);
        }

        [Fact]
        public void ParseStats_WithoutTwitterDataText_ParsesHtmlTableElements()
        {
            // 2015年以前の Twitter ボタンが無い Wayback 快照の HTML テーブル構造
            var html = @"
                <!DOCTYPE html>
                <html>
                <body>
                    <table id=""game_table"">
                        <tr>
                            <th>中央値</th>
                            <td>90</td>
                            <th>データ数</th>
                            <td>250</td>
                            <th>標準偏差</th>
                            <td>8</td>
                        </tr>
                    </table>
                </body>
                </html>";

            var stats = EgsStatsClient.ParseStats(html);

            Assert.NotNull(stats);
            Assert.Equal(90, stats.Median);
            Assert.Equal(250, stats.Count);
            Assert.Equal(8, stats.StdDev);
        }

        [Fact]
        public void ParseStats_WithLegacyTableLayoutAndAttributes_ParsesCorrectly()
        {
            var html = @"
                <table class=""toukei"">
                    <tr><td align=""center""><b>中央値</b></td><td align=""right"">78</td></tr>
                    <tr><td align=""center""><b>データ数</b></td><td align=""right"">50</td></tr>
                    <tr><td align=""center""><b>標準偏差</b></td><td align=""right"">15</td></tr>
                </table>";

            var stats = EgsStatsClient.ParseStats(html);

            Assert.NotNull(stats);
            Assert.Equal(78, stats.Median);
            Assert.Equal(50, stats.Count);
            Assert.Equal(15, stats.StdDev);
        }

        [Fact]
        public void ParseStats_WithTavilyExtractedPlainText_ParsesCorrectly()
        {
            var plainText = @"
                ゲーム名: 千恋＊万花
                ブランド: ゆずソフト
                中央値：84
                データ数：850
                標準偏差：11
                発売日: 2016-07-29
            ";

            var stats = EgsStatsClient.ParseStats(plainText);

            Assert.NotNull(stats);
            Assert.Equal(84, stats.Median);
            Assert.Equal(850, stats.Count);
            Assert.Equal(11, stats.StdDev);
        }

        [Fact]
        public void ParseStats_WithoutStdDev_ReturnsNullStdDev()
        {
            var plainText = "中央値: 75 データ数: 30";

            var stats = EgsStatsClient.ParseStats(plainText);

            Assert.NotNull(stats);
            Assert.Equal(75, stats.Median);
            Assert.Equal(30, stats.Count);
            Assert.Null(stats.StdDev);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("<html><body><h1>404 Not Found</h1></body></html>")]
        [InlineData("<html><body><p>このゲームの情報はありません。</p></body></html>")]
        [InlineData("中央値: - データ数: -")]
        public void ParseStats_WithInvalidOrEmptyContent_ReturnsNull(string content)
        {
            var stats = EgsStatsClient.ParseStats(content);
            Assert.Null(stats);
        }
    }
}
