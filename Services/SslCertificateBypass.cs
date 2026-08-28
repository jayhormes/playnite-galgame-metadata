using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace ErogameScapeMetadata.Services
{
    /// <summary>
    /// 自己署名証明書の自建 NocoDB（例: https://192.168.1.106:5555）向けの検証バイパス。
    ///
    /// ServicePointManager.ServerCertificateValidationCallback はプロセス全体で共有される。
    /// 無条件に true を返すと Playnite 本体と他プラグインの HTTPS まで検証されなくなるため、
    /// ここでは「登録された host のみ」バイパスし、それ以外は元のコールバック
    /// （無ければ既定動作）へ委譲する。
    /// </summary>
    internal static class SslCertificateBypass
    {
        private static readonly object Sync = new object();

        private static readonly HashSet<string> AllowedHosts =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static RemoteCertificateValidationCallback _previous;
        private static bool _installed;

        public static void AllowHost(string baseUrl)
        {
            var host = ExtractHost(baseUrl);
            if (host == null)
            {
                return;
            }

            lock (Sync)
            {
                AllowedHosts.Add(host);
                if (_installed)
                {
                    return;
                }

                // 既存のコールバックを保持し、対象外 host はそちらへ委譲する
                _previous = ServicePointManager.ServerCertificateValidationCallback;
                ServicePointManager.ServerCertificateValidationCallback = Validate;
                _installed = true;
            }
        }

        internal static string ExtractHost(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return null;
            }

            Uri uri;
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out uri))
            {
                return null;
            }

            return string.IsNullOrEmpty(uri.Host) ? null : uri.Host;
        }

        internal static bool IsHostAllowed(string host)
        {
            if (string.IsNullOrEmpty(host))
            {
                return false;
            }

            lock (Sync)
            {
                return AllowedHosts.Contains(host);
            }
        }

        /// <summary>証明書エラーを許容するかの純粋な判定（テスト対象）。</summary>
        internal static bool ShouldAccept(string requestHost, SslPolicyErrors errors)
        {
            if (errors == SslPolicyErrors.None)
            {
                return true;
            }

            return IsHostAllowed(requestHost);
        }

        private static bool Validate(
            object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors errors)
        {
            // .NET Framework では sender は HttpWebRequest、経路によっては host 文字列
            var host = (sender as HttpWebRequest)?.RequestUri?.Host ?? sender as string;

            if (ShouldAccept(host, errors))
            {
                return true;
            }

            return _previous != null
                ? _previous(sender, certificate, chain, errors)
                : errors == SslPolicyErrors.None;
        }

        internal static void ResetForTests()
        {
            lock (Sync)
            {
                AllowedHosts.Clear();
            }
        }
    }
}
