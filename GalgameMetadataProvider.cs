using GalgameMetadata.Models;
using GalgameMetadata.Services;
using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GalgameMetadata
{
    public class GalgameMetadataProvider : OnDemandMetadataProvider
    {
        private readonly MetadataRequestOptions _requestOptions;
        private readonly GalgameMetadataClient _apiClient;
        private static readonly ILogger _logger = LogManager.GetLogger();

        private bool _searchCompleted;
        private GalgameInfo _matchedGame;

        public override List<MetadataField> AvailableFields { get; } = new List<MetadataField>
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

        public GalgameMetadataProvider(
            MetadataRequestOptions options, GalgameMetadataClient apiClient)
        {
            _requestOptions = options;
            _apiClient = apiClient;
        }

        private void EnsureData(CancellationToken ct)
        {
            if (_searchCompleted)
                return;

            try
            {
                if (_requestOptions.IsBackgroundDownload)
                {
                    SearchAutomatic(ct);
                }
                else
                {
                    SearchInteractive(ct);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "EnsureData 実行中にエラーが発生");
            }
            finally
            {
                _searchCompleted = true;
            }
        }

        private void SearchAutomatic(CancellationToken ct)
        {
            var gameName = _requestOptions.GameData?.Name;
            if (string.IsNullOrWhiteSpace(gameName))
                return;

            try
            {
                var results = Task.Run(() => _apiClient.SearchGamesAsync(gameName, ct))
                    .GetAwaiter().GetResult();

                if (results == null || results.Count == 0)
                {
                    // ゲーム名に版数タグが含まれていた場合、接尾辞を除去して再検索
                    var strippedQuery = GalgameMetadataClient.NormalizeAndStripSuffix(gameName);
                    if (!string.IsNullOrEmpty(strippedQuery) && strippedQuery != GalgameMetadataClient.NormalizeForComparison(gameName))
                    {
                        results = Task.Run(() => _apiClient.SearchGamesAsync(strippedQuery, ct))
                            .GetAwaiter().GetResult();
                    }
                }

                if (results == null || results.Count == 0)
                    return;

                var normalized = GalgameMetadataClient.NormalizeForComparison(gameName);
                var strippedNormalized = GalgameMetadataClient.NormalizeAndStripSuffix(gameName);

                // 1. 完全一致（原題または英題）
                var match = results.FirstOrDefault(r =>
                    GalgameMetadataClient.NormalizeForComparison(r.GameName) == normalized
                    || (!string.IsNullOrEmpty(r.AltName)
                        && GalgameMetadataClient.NormalizeForComparison(r.AltName) == normalized));

                // 2. 版数・タグ除去後の一致
                if (match == null && !string.IsNullOrEmpty(strippedNormalized))
                {
                    match = results.FirstOrDefault(r =>
                        GalgameMetadataClient.NormalizeAndStripSuffix(r.GameName) == strippedNormalized
                        || (!string.IsNullOrEmpty(r.AltName)
                            && GalgameMetadataClient.NormalizeAndStripSuffix(r.AltName) == strippedNormalized));
                }

                if (match != null)
                {
                    _matchedGame = Task.Run(() => _apiClient.GetGameDetailsAsync(match, ct))
                        .GetAwaiter().GetResult();
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException) || !ct.IsCancellationRequested)
            {
                _logger.Warn($"自動検索エラー ({gameName}): {ex.Message}");
            }
        }

        private void SearchInteractive(CancellationToken ct)
        {
            List<GalgameInfo> searchResults = null;
            List<GenericItemOption> itemOptions = null;

            var selectedItem = API.Instance.Dialogs.ChooseItemWithSearch(
                null,
                (searchTerm) =>
                {
                    if (string.IsNullOrWhiteSpace(searchTerm))
                        return new List<GenericItemOption>();

                    try
                    {
                        API.Instance.Dialogs.ActivateGlobalProgress((args) =>
                        {
                            searchResults = Task.Run(
                                () => _apiClient.SearchGamesAsync(searchTerm, args.CancelToken))
                                .GetAwaiter().GetResult();
                        }, new GlobalProgressOptions("VNDBを検索中...", true)
                        {
                            IsIndeterminate = true
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "検索中にエラーが発生");
                        return new List<GenericItemOption>();
                    }

                    if (searchResults == null)
                        return new List<GenericItemOption>();

                    itemOptions = searchResults
                        .Select(r => CreateItemOption(r))
                        .ToList();
                    return itemOptions;
                },
                _requestOptions.GameData?.Name ?? string.Empty);

            if (selectedItem != null && itemOptions != null)
            {
                var selectedIndex = itemOptions.IndexOf(selectedItem);
                if (selectedIndex >= 0 && selectedIndex < searchResults.Count)
                {
                    var selected = searchResults[selectedIndex];
                    _matchedGame = Task.Run(
                        () => _apiClient.GetGameDetailsAsync(selected, ct))
                        .GetAwaiter().GetResult();
                }
            }
        }

        private GenericItemOption CreateItemOption(GalgameInfo game)
        {
            var title = game.GameName;
            if (!string.IsNullOrEmpty(game.BrandName))
                title += $" ({game.BrandName})";

            var details = new List<string>();
            if (game.SellDay.HasValue)
                details.Add($"発売日: {game.SellDay.Value:yyyy-MM-dd}");
            if (!string.IsNullOrEmpty(game.AltName))
                details.Add(game.AltName);
            if (!string.IsNullOrEmpty(game.VndbId))
                details.Add(game.VndbId);

            var description = string.Join(" | ", details);
            return new GenericItemOption(title, description);
        }

        public override string GetName(GetMetadataFieldArgs args)
        {
            EnsureData(args.CancelToken);
            if (_matchedGame == null)
                return base.GetName(args);
            return _matchedGame.GameName;
        }

        public override IEnumerable<MetadataProperty> GetDevelopers(GetMetadataFieldArgs args)
        {
            EnsureData(args.CancelToken);
            if (_matchedGame == null || string.IsNullOrEmpty(_matchedGame.BrandName))
                return base.GetDevelopers(args);
            return new[] { new MetadataNameProperty(_matchedGame.BrandName) };
        }

        public override IEnumerable<MetadataProperty> GetPublishers(GetMetadataFieldArgs args)
        {
            EnsureData(args.CancelToken);
            if (_matchedGame == null || string.IsNullOrEmpty(_matchedGame.BrandName))
                return base.GetPublishers(args);
            return new[] { new MetadataNameProperty(_matchedGame.BrandName) };
        }

        public override ReleaseDate? GetReleaseDate(GetMetadataFieldArgs args)
        {
            EnsureData(args.CancelToken);
            if (_matchedGame?.SellDay == null)
                return base.GetReleaseDate(args);
            var d = _matchedGame.SellDay.Value;
            return new ReleaseDate(d.Year, d.Month, d.Day);
        }

        public override int? GetCommunityScore(GetMetadataFieldArgs args)
        {
            EnsureData(args.CancelToken);
            if (_matchedGame == null)
                return base.GetCommunityScore(args);

            // EGS 中央値（有効なスコア・投票数 > 0）を最優先
            if (_matchedGame.Median.HasValue && _matchedGame.Median.Value > 0 && (_matchedGame.ReviewCount ?? 1) > 0)
                return _matchedGame.Median.Value;

            // EGS スコアが無い、または0件の場合は VNDB rating（10-100）で代替
            if (_matchedGame.VndbRating.HasValue)
                return (int)Math.Round(_matchedGame.VndbRating.Value);

            return base.GetCommunityScore(args);
        }

        public override MetadataFile GetBackgroundImage(GetMetadataFieldArgs args)
        {
            EnsureData(args.CancelToken);
            if (_matchedGame == null || _matchedGame.BackgroundImageUrls == null
                || _matchedGame.BackgroundImageUrls.Count == 0)
                return base.GetBackgroundImage(args);

            string selectedUrl;
            if (_requestOptions.IsBackgroundDownload)
            {
                selectedUrl = _matchedGame.BackgroundImageUrls[0];
            }
            else
            {
                var imageOptions = _matchedGame.BackgroundImageUrls
                    .Select(url => new ImageFileOption(url))
                    .ToList();

                var selected = API.Instance.Dialogs.ChooseImageFile(
                    imageOptions, "背景画像を選択");

                if (selected == null)
                    return base.GetBackgroundImage(args);

                selectedUrl = selected.Path;
            }

            return DownloadAsMetadataFile(selectedUrl, 1920, args.CancelToken)
                ?? base.GetBackgroundImage(args);
        }

        public override MetadataFile GetCoverImage(GetMetadataFieldArgs args)
        {
            EnsureData(args.CancelToken);
            if (_matchedGame == null)
                return base.GetCoverImage(args);

            var candidates = _matchedGame.GetCoverImageCandidates();
            foreach (var url in candidates)
            {
                var file = DownloadAsMetadataFile(url, 600, args.CancelToken);
                if (file != null)
                    return file;
            }

            return base.GetCoverImage(args);
        }

        private MetadataFile DownloadAsMetadataFile(
            string url, int maxWidth, CancellationToken ct)
        {
            try
            {
                var data = Task.Run(() => _apiClient.DownloadImageAsync(url, ct))
                    .GetAwaiter().GetResult();

                if (data != null && data.Length > 0)
                {
                    data = ResizeImageIfNeeded(data, maxWidth);
                    var uri = new Uri(url);
                    var fileName = Path.GetFileName(uri.LocalPath);
                    if (string.IsNullOrEmpty(fileName))
                        fileName = "cover.jpg";
                    return new MetadataFile(fileName, data);
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"画像ダウンロード失敗: {url} - {ex.Message}");
            }

            return null;
        }

        private static byte[] ResizeImageIfNeeded(byte[] imageData, int maxWidth)
        {
            try
            {
                using (var ms = new MemoryStream(imageData))
                using (var original = Image.FromStream(ms))
                {
                    if (original.Width <= maxWidth)
                        return imageData;

                    var ratio = (double)maxWidth / original.Width;
                    var newWidth = maxWidth;
                    var newHeight = (int)(original.Height * ratio);

                    using (var resized = new Bitmap(newWidth, newHeight))
                    using (var g = Graphics.FromImage(resized))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(original, 0, 0, newWidth, newHeight);

                        using (var output = new MemoryStream())
                        {
                            resized.Save(output, ImageFormat.Jpeg);
                            return output.ToArray();
                        }
                    }
                }
            }
            catch
            {
                // WebP や非標準フォーマットで GDI+ が失敗した場合は、変換せず元のバイト列を返す
                return imageData;
            }
        }

        public override IEnumerable<Link> GetLinks(GetMetadataFieldArgs args)
        {
            EnsureData(args.CancelToken);
            if (_matchedGame == null)
                return base.GetLinks(args);

            var links = new List<Link>();

            var egsUrl = _matchedGame.GetErogameScapeUrl();
            if (egsUrl != null)
                links.Add(new Link("ErogameScape", egsUrl));

            var vndbUrl = _matchedGame.GetVndbUrl();
            if (vndbUrl != null)
                links.Add(new Link("VNDB", vndbUrl));

            if (!string.IsNullOrEmpty(_matchedGame.DlsiteId))
            {
                var domain = _matchedGame.DlsiteDomain ?? "pro";
                links.Add(new Link("DLsite",
                    $"https://www.dlsite.com/{domain}/work/=/product_id/{_matchedGame.DlsiteId}.html"));
            }

            if (!string.IsNullOrEmpty(_matchedGame.DmmId))
            {
                links.Add(new Link("DMM",
                    $"https://dlsoft.dmm.co.jp/detail/{_matchedGame.DmmId}/"));
            }

            return links;
        }

        public override string GetDescription(GetMetadataFieldArgs args)
        {
            EnsureData(args.CancelToken);
            if (_matchedGame == null || string.IsNullOrEmpty(_matchedGame.Description))
                return base.GetDescription(args);

            return _matchedGame.Description;
        }

        public override IEnumerable<MetadataProperty> GetTags(GetMetadataFieldArgs args)
        {
            EnsureData(args.CancelToken);
            if (_matchedGame == null || _matchedGame.Tags == null || _matchedGame.Tags.Count == 0)
                return base.GetTags(args);

            return _matchedGame.Tags.Select(t => new MetadataNameProperty(t));
        }

        public override IEnumerable<MetadataProperty> GetGenres(GetMetadataFieldArgs args)
        {
            EnsureData(args.CancelToken);
            if (_matchedGame == null || _matchedGame.Genres == null || _matchedGame.Genres.Count == 0)
                return base.GetGenres(args);

            return _matchedGame.Genres.Select(g => new MetadataNameProperty(g));
        }

        public override IEnumerable<MetadataProperty> GetPlatforms(GetMetadataFieldArgs args)
        {
            EnsureData(args.CancelToken);
            if (_matchedGame == null)
                return base.GetPlatforms(args);

            return new[] { new MetadataSpecProperty("pc_windows") };
        }

        public override IEnumerable<MetadataProperty> GetAgeRatings(GetMetadataFieldArgs args)
        {
            EnsureData(args.CancelToken);
            if (_matchedGame?.MinAge == null)
                return base.GetAgeRatings(args);

            var rating = _matchedGame.MinAge.Value >= 18 ? "18+" : "全年齢";
            return new[] { new MetadataNameProperty(rating) };
        }

        public override IEnumerable<MetadataProperty> GetRegions(GetMetadataFieldArgs args)
        {
            EnsureData(args.CancelToken);
            if (_matchedGame == null)
                return base.GetRegions(args);

            return new[] { new MetadataSpecProperty("japan") };
        }
    }
}
