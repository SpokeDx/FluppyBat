using System.Security.Cryptography;

namespace SQFLUPPY2.Models
{
    /// <summary>
    /// Хеширование паролей лобби через PBKDF2-SHA256.
    /// </summary>
    public static class LobbyPasswordHasher
    {
        private const int SaltSize = 16;
        private const int KeySize = 32;
        private const int Iterations = 100_000;

        public static (string Hash, string Salt) Create(string password)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
            byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
            return (Convert.ToBase64String(key), Convert.ToBase64String(salt));
        }

        public static bool Verify(string password, string? hash, string? salt)
        {
            if (hash is null || salt is null) return false;

            byte[] saltBytes;
            byte[] expected;
            try
            {
                saltBytes = Convert.FromBase64String(salt);
                expected = Convert.FromBase64String(hash);
            }
            catch (FormatException)
            {
                return false;
            }

            byte[] actual = Rfc2898DeriveBytes.Pbkdf2(password, saltBytes, Iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
    }
}
