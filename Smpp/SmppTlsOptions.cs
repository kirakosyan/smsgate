using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace Smpp
{
    /// <summary>Portable TLS settings for .NET Framework and modern .NET. Null callbacks use platform certificate validation.</summary>
    public sealed class SmppTlsClientOptions
    {
        public string TargetHost { get; set; }
        public X509CertificateCollection ClientCertificates { get; set; } = new X509CertificateCollection();
        public SslProtocols EnabledSslProtocols { get; set; } = SslProtocols.None;
        public bool CheckCertificateRevocation { get; set; } = true;
        /// <summary>Optional application validation policy. When supplied, its result replaces platform acceptance.</summary>
        public RemoteCertificateValidationCallback CertificateValidationCallback { get; set; }
#if !NETFRAMEWORK
        public static implicit operator SslClientAuthenticationOptions(SmppTlsClientOptions options) => options == null ? null : new SslClientAuthenticationOptions
        {
            TargetHost = options.TargetHost, ClientCertificates = options.ClientCertificates,
            EnabledSslProtocols = options.EnabledSslProtocols,
            CertificateRevocationCheckMode = options.CheckCertificateRevocation ? X509RevocationMode.Online : X509RevocationMode.NoCheck,
            RemoteCertificateValidationCallback = options.CertificateValidationCallback
        };
#endif
    }
    /// <summary>Portable TLS listener settings. The certificate must have a private key and remain valid for the listener lifetime.</summary>
    public sealed class SmppTlsServerOptions
    {
        public X509Certificate ServerCertificate { get; set; }
        public bool ClientCertificateRequired { get; set; }
        public SslProtocols EnabledSslProtocols { get; set; } = SslProtocols.None;
        public bool CheckCertificateRevocation { get; set; } = true;
        /// <summary>Optional application policy for client certificates; null uses platform validation.</summary>
        public RemoteCertificateValidationCallback CertificateValidationCallback { get; set; }
#if !NETFRAMEWORK
        public static implicit operator SslServerAuthenticationOptions(SmppTlsServerOptions options) => options == null ? null : new SslServerAuthenticationOptions
        {
            ServerCertificate = options.ServerCertificate, ClientCertificateRequired = options.ClientCertificateRequired,
            EnabledSslProtocols = options.EnabledSslProtocols,
            CertificateRevocationCheckMode = options.CheckCertificateRevocation ? X509RevocationMode.Online : X509RevocationMode.NoCheck,
            RemoteCertificateValidationCallback = options.CertificateValidationCallback
        };
#endif
    }
}
