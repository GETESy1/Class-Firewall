
using System;
using System.Security.Cryptography;
using System.Text;

namespace ClassFirewall
{
    internal static class PasswordGate
    {
      
        public const string MasterPassword = "Admin1145";

        private const string Prefix = "pbkdf2";
        private const int Iterations = 120_000;
        private const int SaltSize = 16;
        private const int HashSize = 32;

        public static bool IsConfigured(string? storedHash) =>
            !string.IsNullOrWhiteSpace(storedHash);

        public static string CreateHash(string password)
        {
            if (password == null) throw new ArgumentNullException(nameof(password));

            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            var hash = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, HashSize);

            return $"{Prefix}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
        }

        public static bool IsMaster(string password) =>
            string.Equals(password, MasterPassword, StringComparison.Ordinal);

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

                return CryptographicOperations.FixedTimeEquals(actual, expected);
            }
            catch
            {
                return false;
            }
        }
    }
}
