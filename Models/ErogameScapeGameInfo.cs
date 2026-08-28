using System;
using System.Collections.Generic;

namespace ErogameScapeMetadata.Models
{
    public class ErogameScapeGameInfo
    {
        private const string ErogameScapeGameUrlTemplate =
            "https://erogamescape.dyndns.org/~ap2/ero/toukei_kaiseki/game.php?game={0}";

        private const string DmmCoverUrlTemplate =
            "https://pics.dmm.co.jp/digital/pcgame/{0}/{0}pl.jpg";

        // 2.0.0: 主キーは VNDB id。EGS id は release extlinks から解決（無い作品もある）
        public string VndbId { get; set; }
        public int? EgsId { get; set; }

        public string GameName { get; set; }
        public string AltName { get; set; }
        public string BrandName { get; set; }
        public DateTime? SellDay { get; set; }

        // EGS スコア（Wayback/Tavily 経由）。EgsSource = "wayback"（快照時点値）/ "tavily"（live）
        public int? Median { get; set; }
        public int? ReviewCount { get; set; }
        public string EgsSource { get; set; }
        public string EgsSnapshotDate { get; set; }

        public double? VndbRating { get; set; }
        public int? MinAge { get; set; }

        public string DmmId { get; set; }
        public string DlsiteId { get; set; }
        public string DlsiteDomain { get; set; }
        public string GetchuId { get; set; }
        public string Description { get; set; }
        public List<string> Tags { get; set; } = new List<string>();
        public List<string> Genres { get; set; } = new List<string>();
        public List<string> BackgroundImageUrls { get; set; } = new List<string>();
        public string NocoDbCoverImageUrl { get; set; }
        public string VndbCoverImageUrl { get; set; }
        public bool VndbCoverIsPortrait { get; set; }

        public List<string> GetCoverImageCandidates()
        {
            var candidates = new List<string>();
            var dmmUrl = GetDmmCoverUrl();

            // 0. NocoDB のカバー画像があれば最優先
            if (!string.IsNullOrEmpty(NocoDbCoverImageUrl))
            {
                candidates.Add(NocoDbCoverImageUrl);
            }

            // 1. VNDBカバーが縦長なら次点優先
            if (!string.IsNullOrEmpty(VndbCoverImageUrl) && VndbCoverIsPortrait && !candidates.Contains(VndbCoverImageUrl))
            {
                candidates.Add(VndbCoverImageUrl);
            }

            // 2. DMMパッケージ画像があれば追加
            if (!string.IsNullOrEmpty(dmmUrl) && !candidates.Contains(dmmUrl))
            {
                candidates.Add(dmmUrl);
            }

            // 3. VNDBカバー（横長、または次点フォールバックとして追加）
            if (!string.IsNullOrEmpty(VndbCoverImageUrl) && !candidates.Contains(VndbCoverImageUrl))
            {
                candidates.Add(VndbCoverImageUrl);
            }

            return candidates;
        }

        public string GetCoverImageUrl()
        {
            var candidates = GetCoverImageCandidates();
            return candidates.Count > 0 ? candidates[0] : null;
        }

        public string GetDmmCoverUrl()
        {
            if (string.IsNullOrEmpty(DmmId))
                return null;
            return string.Format(DmmCoverUrlTemplate, DmmId);
        }

        public string GetErogameScapeUrl()
        {
            return EgsId.HasValue
                ? string.Format(ErogameScapeGameUrlTemplate, EgsId.Value)
                : null;
        }

        public string GetVndbUrl()
        {
            return string.IsNullOrEmpty(VndbId) ? null : $"https://vndb.org/{VndbId}";
        }
    }
}

