using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace Smpp
{
    /// <summary>Portable TLS settings. Configure before assignment: assignment snapshots scalar settings and the certificate collection on both targets. Certificate objects and callbacks remain shared; null callbacks use platform validation.</summary>
    public sealed class SmppTlsClientOptions
    {
        public string TargetHost { get; set; }
        public X509CertificateCollection ClientCertificates { get; set; } = new X509CertificateCollection();
        public SslProtocols EnabledSslProtocols { get; set; } = SslProtocols.None;
        public bool CheckCertificateRevocation { get; set; } = true;
        /// <summary>Optional application validation policy. When supplied, its result replaces platform acceptance.</summary>
        public RemoteCertificateValidationCallback CertificateValidationCallback { get; set; }
        internal SmppTlsClientOptions Snapshot() => new SmppTlsClientOptions
        {
            TargetHost = TargetHost,
            ClientCertificates = ClientCertificates == null ? null : new X509CertificateCollection(ClientCertificates),
            EnabledSslProtocols = EnabledSslProtocols, CheckCertificateRevocation = CheckCertificateRevocation,
            CertificateValidationCallback = CertificateValidationCallback
        };
#if !NETFRAMEWORK
        /// <summary>Creates an independent settings snapshot, copying the certificate collection but sharing certificate objects and callbacks.</summary>
        public static implicit operator SslClientAuthenticationOptions(SmppTlsClientOptions options) => options == null ? null : new SslClientAuthenticationOptions
        {
            TargetHost = options.TargetHost,
            ClientCertificates = options.ClientCertificates == null ? null : new X509CertificateCollection(options.ClientCertificates),
            EnabledSslProtocols = options.EnabledSslProtocols,
            CertificateRevocationCheckMode = options.CheckCertificateRevocation ? X509RevocationMode.Online : X509RevocationMode.NoCheck,
            RemoteCertificateValidationCallback = options.CertificateValidationCallback
        };
#endif
    }
    /// <summary>Portable listener settings, snapshotted when passed to the listener. The certificate and callback remain shared; keep the certificate and private key valid for the listener lifetime.</summary>
    public sealed class SmppTlsServerOptions
    {
        public X509Certificate ServerCertificate { get; set; }
        public bool ClientCertificateRequired { get; set; }
        public SslProtocols EnabledSslProtocols { get; set; } = SslProtocols.None;
        public bool CheckCertificateRevocation { get; set; } = true;
        /// <summary>Optional application policy for client certificates; null uses platform validation.</summary>
        public RemoteCertificateValidationCallback CertificateValidationCallback { get; set; }
        internal SmppTlsServerOptions Snapshot() => (SmppTlsServerOptions)MemberwiseClone();
#if !NETFRAMEWORK
        /// <summary>Creates an independent settings snapshot, sharing the certificate and validation callback.</summary>
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
