using ErogameScapeMetadata.Services;
using System.Net.Security;
using Xunit;

namespace ErogameScapeMetadata.Tests
{
    // ServicePointManager のコールバックはプロセス共有のため、
    // 「登録 host のみバイパス、他 host は従来どおり検証」が守られていることを検証する。
    [Collection("SslBypass")]
    public class SslCertificateBypassTests
    {
        [Theory]
        [InlineData("https://192.168.1.106:5555", "192.168.1.106")]
        [InlineData("https://nocodb.example.com/", "nocodb.example.com")]
        [InlineData("http://nas.local:8080/api/v2", "nas.local")]
        [InlineData("  https://spaced.example.com  ", "spaced.example.com")]
        public void ExtractHost_ParsesHostFromBaseUrl(string url, string expected)
        {
            Assert.Equal(expected, SslCertificateBypass.ExtractHost(url));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("not-a-url")]
        [InlineData("/relative/path")]
        public void ExtractHost_ReturnsNullForInvalidUrl(string url)
        {
            Assert.Null(SslCertificateBypass.ExtractHost(url));
        }

        [Fact]
        public void ShouldAccept_ValidCertificate_AlwaysAccepted()
        {
            SslCertificateBypass.ResetForTests();

            // 証明書に問題が無ければ、未登録 host でも当然通す
            Assert.True(SslCertificateBypass.ShouldAccept("api.vndb.org", SslPolicyErrors.None));
            Assert.True(SslCertificateBypass.ShouldAccept(null, SslPolicyErrors.None));
        }

        [Fact]
        public void ShouldAccept_OnlyBypassesRegisteredHost()
        {
            SslCertificateBypass.ResetForTests();
            SslCertificateBypass.AllowHost("https://192.168.1.106:5555");

            // 登録した NocoDB host はエラーがあっても通す
            Assert.True(SslCertificateBypass.ShouldAccept(
                "192.168.1.106", SslPolicyErrors.RemoteCertificateNameMismatch));

            // それ以外（Playnite 本体・他プラグインの通信）は通さない
            Assert.False(SslCertificateBypass.ShouldAccept(
                "api.vndb.org", SslPolicyErrors.RemoteCertificateNameMismatch));
            Assert.False(SslCertificateBypass.ShouldAccept(
                "erogamescape.dyndns.org", SslPolicyErrors.RemoteCertificateChainErrors));
            Assert.False(SslCertificateBypass.ShouldAccept(
                "web.archive.org", SslPolicyErrors.RemoteCertificateNotAvailable));
        }

        [Fact]
        public void ShouldAccept_HostMatchIsCaseInsensitive()
        {
            SslCertificateBypass.ResetForTests();
            SslCertificateBypass.AllowHost("https://NocoDB.Example.com");

            Assert.True(SslCertificateBypass.ShouldAccept(
                "nocodb.example.com", SslPolicyErrors.RemoteCertificateChainErrors));
        }

        [Fact]
        public void ShouldAccept_NullOrEmptyHost_NotBypassed()
        {
            SslCertificateBypass.ResetForTests();
            SslCertificateBypass.AllowHost("https://192.168.1.106:5555");

            Assert.False(SslCertificateBypass.ShouldAccept(null, SslPolicyErrors.RemoteCertificateChainErrors));
            Assert.False(SslCertificateBypass.ShouldAccept("", SslPolicyErrors.RemoteCertificateChainErrors));
        }

        [Fact]
        public void AllowHost_InvalidUrl_RegistersNothing()
        {
            SslCertificateBypass.ResetForTests();
            SslCertificateBypass.AllowHost("not-a-url");
            SslCertificateBypass.AllowHost(null);

            Assert.False(SslCertificateBypass.ShouldAccept(
                "not-a-url", SslPolicyErrors.RemoteCertificateChainErrors));
        }

        [Fact]
        public void AllowHost_MultipleHostsAccumulate()
        {
            SslCertificateBypass.ResetForTests();
            SslCertificateBypass.AllowHost("https://192.168.1.106:5555");
            SslCertificateBypass.AllowHost("https://nas.local:8443");

            Assert.True(SslCertificateBypass.ShouldAccept(
                "192.168.1.106", SslPolicyErrors.RemoteCertificateChainErrors));
            Assert.True(SslCertificateBypass.ShouldAccept(
                "nas.local", SslPolicyErrors.RemoteCertificateChainErrors));
            Assert.False(SslCertificateBypass.ShouldAccept(
                "evil.example.com", SslPolicyErrors.RemoteCertificateChainErrors));
        }

        [Fact]
        public void NocoDbClient_WithoutIgnoreSslErrors_DoesNotRegisterHost()
        {
            SslCertificateBypass.ResetForTests();

            new NocoDbClient(new System.Net.Http.HttpClient(), null,
                "https://192.168.1.106:5555", "token", "tbl", ignoreSslErrors: false);

            Assert.False(SslCertificateBypass.ShouldAccept(
                "192.168.1.106", SslPolicyErrors.RemoteCertificateNameMismatch));
        }

        [Fact]
        public void NocoDbClient_WithIgnoreSslErrors_RegistersOnlyItsOwnHost()
        {
            SslCertificateBypass.ResetForTests();

            new NocoDbClient(new System.Net.Http.HttpClient(), null,
                "https://192.168.1.106:5555", "token", "tbl", ignoreSslErrors: true);

            Assert.True(SslCertificateBypass.ShouldAccept(
                "192.168.1.106", SslPolicyErrors.RemoteCertificateNameMismatch));
            Assert.False(SslCertificateBypass.ShouldAccept(
                "api.vndb.org", SslPolicyErrors.RemoteCertificateNameMismatch));
        }
    }
}
