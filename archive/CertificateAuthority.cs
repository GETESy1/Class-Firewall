using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace ClassFirewall
{
    /// <summary>
    /// 本地根证书颁发机构（CA）。
    /// 用于 MITM 时动态签发域名证书，让浏览器信任我们返回的 HTTPS 页面。
    /// </summary>
    public static class CertificateAuthority
    {
        private const string CaSubject = "CN=ClassFirewall Local CA, O=ClassFirewall, C=CN";

        private static readonly ConcurrentDictionary<string, X509Certificate2> _domainCertCache
            = new(StringComparer.OrdinalIgnoreCase);

        private static X509Certificate2? _caCache;
        private static readonly object _lock = new();

        // ---------------- 根证书 ----------------

        /// <summary>检查根证书是否已安装到系统信任区</summary>
        public static bool IsInstalled()
        {
            try
            {
                using var store = new X509Store(StoreName.Root, StoreLocation.LocalMachine);
                store.Open(OpenFlags.ReadOnly);
                return store.Certificates
                    .Find(X509FindType.FindBySubjectDistinguishedName, CaSubject, false)
                    .Count > 0;
            }
            catch { return false; }
        }

        /// <summary>获取或创建根证书（自动安装到 LocalMachine\Root）</summary>
        public static X509Certificate2 GetOrCreate()
        {
            lock (_lock)
            {
                if (_caCache != null) return _caCache;

                // 先尝试从证书存储读取
                using (var store = new X509Store(StoreName.Root, StoreLocation.LocalMachine))
                {
                    store.Open(OpenFlags.ReadOnly);
                    var found = store.Certificates
                        .Find(X509FindType.FindBySubjectDistinguishedName, CaSubject, false)
                        .OfType<X509Certificate2>()
                        .FirstOrDefault();
                    if (found != null)
                    {
                        _caCache = found;
                        return _caCache;
                    }
                }

                // 生成新 CA
                var ca = CreateCaCertificate();

                // 安装到信任根
                using (var store = new X509Store(StoreName.Root, StoreLocation.LocalMachine))
                {
                    store.Open(OpenFlags.ReadWrite);
                    store.Add(ca);
                }

                _caCache = ca;
                return _caCache;
            }
        }

        private static X509Certificate2 CreateCaCertificate()
        {
            using var rsa = RSA.Create(2048);
            var req = new CertificateRequest(
                CaSubject,
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            req.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(true, false, 0, true));
            req.CertificateExtensions.Add(
                new X509KeyUsageExtension(
                    X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign,
                    true));
            req.CertificateExtensions.Add(
                new X509SubjectKeyIdentifierExtension(req.PublicKey, false));

            var cert = req.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1),
                DateTimeOffset.UtcNow.AddYears(10));

            // 导出再导入，确保私钥句柄可持久化
            return new X509Certificate2(
                cert.Export(X509ContentType.Pfx),
                (string?)null,
                X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
        }

        /// <summary>从系统信任区移除根证书</summary>
        public static void Uninstall()
        {
            try
            {
                using var store = new X509Store(StoreName.Root, StoreLocation.LocalMachine);
                store.Open(OpenFlags.ReadWrite);
                var found = store.Certificates
                    .Find(X509FindType.FindBySubjectDistinguishedName, CaSubject, false);
                foreach (var c in found) store.Remove(c);
                _caCache = null;
                _domainCertCache.Clear();
            }
            catch { }
        }

        // ---------------- 域名证书 ----------------

        /// <summary>为指定域名签发（或从缓存取）证书</summary>
        public static X509Certificate2 IssueForDomain(string domain)
        {
            if (_domainCertCache.TryGetValue(domain, out var cached))
            {
                if (cached.NotAfter > DateTime.Now.AddDays(1))
                    return cached;
            }

            var ca = GetOrCreate();
            var cert = CreateDomainCertificate(domain, ca);
            _domainCertCache[domain] = cert;
            return cert;
        }

        private static X509Certificate2 CreateDomainCertificate(string domain, X509Certificate2 ca)
        {
            using var rsa = RSA.Create(2048);
            var req = new CertificateRequest(
                $"CN={domain}",
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            var san = new SubjectAlternativeNameBuilder();
            san.AddDnsName(domain);
            // 通配符版：仅当域名由多级组成时加
            var parts = domain.Split('.');
            if (parts.Length >= 3)
                san.AddDnsName("*." + string.Join(".", parts.Skip(1)));
            req.CertificateExtensions.Add(san.Build());

            req.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(false, false, 0, false));
            req.CertificateExtensions.Add(
                new X509KeyUsageExtension(
                    X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                    false));
            req.CertificateExtensions.Add(
                new X509EnhancedKeyUsageExtension(
                    new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, // Server Authentication
                    false));
            req.CertificateExtensions.Add(
                new X509SubjectKeyIdentifierExtension(req.PublicKey, false));

            var serial = new byte[16];
            RandomNumberGenerator.Fill(serial);
            // 保证最高位为 0，避免被当作负数
            serial[0] &= 0x7F;

            var cert = req.Create(
                ca,
                DateTimeOffset.UtcNow.AddDays(-1),
                DateTimeOffset.UtcNow.AddYears(1),
                serial);

            var withKey = cert.CopyWithPrivateKey(rsa);
            return new X509Certificate2(
                withKey.Export(X509ContentType.Pfx),
                (string?)null,
                X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
        }
    }
}