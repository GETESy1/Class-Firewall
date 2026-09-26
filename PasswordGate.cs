// ==================== PasswordGate.cs ====================
using System;
using System.Security.Cryptography;
using System.Text;

namespace ClassFirewall
{
    /// <summary>
    /// 程序解锁密码：哈希、校验、万能密码。
    ///
    /// 存储格式（存在 settings.json 里，**从不保存明文**）：
    ///     pbkdf2$&lt;迭代次数&gt;$&lt;盐 base64&gt;$&lt;哈希 base64&gt;
    ///
    /// ⚠️ 关于万能密码：它被硬编码在 exe 里，**反编译就能看到**，因此它不是机密，
    ///    只用于"管理员忘记自己设的密码"这种救急场景，防不住有心人。
    ///    它挡的是"随便点两下就把屏蔽关掉"的普通使用场景。
    ///    真正要防住改程序的人，得靠 ACL / 组策略 / 不把控制权交给对方。
    /// </summary>
    internal static class PasswordGate
    {
        /// <summary>万能密码：任何情况下都能解锁，用于管理员找回</summary>
        public const string MasterPassword = "Admin1145";

        private const string Prefix = "pbkdf2";
        private const int Iterations = 120_000;
        private const int SaltSize = 16;
        private const int HashSize = 32;

        /// <summary>是否已经设置过密码</summary>
        public static bool IsConfigured(string? storedHash) =>
            !string.IsNullOrWhiteSpace(storedHash);

        /// <summary>把明文密码变成可存储的哈希串</summary>
        public static string CreateHash(string password)
        {
            if (password == null) throw new ArgumentNullException(nameof(password));

            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            var hash = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, HashSize);

            return $"{Prefix}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
        }

        /// <summary>是不是万能密码（区分大小写，按用户指定的原样匹配）</summary>
        public static bool IsMaster(string password) =>
            string.Equals(password, MasterPassword, StringComparison.Ordinal);

        /// <summary>
        /// 校验密码。万能密码永远通过；否则与存储的哈希做定长时间比较。
        /// 没有设置过密码时，任何输入都返回 false（调用方应先判断 IsConfigured）。
        /// </summary>
        public static bool Verify(string password, string? storedHash)
        {
            if (IsMaster(password)) return true;
            if (string.IsNullOrWhiteSpace(password)) return false;
            if (!IsConfigured(storedHash)) return false;

            try
            {
                var parts = storedHash!.Split('$');
                if (parts.Length != 4) return false;
                if (!string.Equals(parts[0], Prefix, StringComparison.Ordinal)) return false;
                if (!int.TryParse(parts[1], out int iterations) || iterations <= 0) return false;

                var salt = Convert.FromBase64String(parts[2]);
                var expected = Convert.FromBase64String(parts[3]);
                if (salt.Length == 0 || expected.Length == 0) return false;

                var actual = Rfc2898DeriveBytes.Pbkdf2(
                    Encoding.UTF8.GetBytes(password), salt,
                    iterations, HashAlgorithmName.SHA256, expected.Length);

                // 定长时间比较，避免通过响应时间推测哈希
                return CryptographicOperations.FixedTimeEquals(actual, expected);
            }
            catch
            {
                // 哈希串损坏时一律视为校验失败，而不是放行
                return false;
            }
        }
    }
}
