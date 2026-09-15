using System;
using System.Security.Cryptography;
using System.Text;

namespace DataSync.Core.Security
{
    /// <summary>
    /// DES/CBC text encryption, compatible with DDRREP and DDUtility (<c>.eps</c> connection files and <c>PW.txt</c>).
    /// This is obfuscation for compatibility with existing files, not strong protection.
    /// </summary>
    public static class Encryption
    {
        private static readonly byte[] Key = Encoding.UTF8.GetBytes("Drill@#$");
        private static readonly byte[] IV = { 0x01, 0x12, 0x23, 0x34, 0x45, 0x56, 0x67, 0x78 };

        public static string Encrypt(string plainText)
        {
            var input = Encoding.UTF8.GetBytes(plainText ?? string.Empty);
            using (var des = DES.Create())
            using (var encryptor = des.CreateEncryptor(Key, IV))
            {
                return Convert.ToBase64String(encryptor.TransformFinalBlock(input, 0, input.Length));
            }
        }

        public static string Decrypt(string cipherText)
        {
            // Base64 that passed through a query string may have had '+' turned into ' '.
            var input = Convert.FromBase64String((cipherText ?? string.Empty).Trim().Replace(" ", "+"));
            using (var des = DES.Create())
            using (var decryptor = des.CreateDecryptor(Key, IV))
            {
                return Encoding.UTF8.GetString(decryptor.TransformFinalBlock(input, 0, input.Length));
            }
        }
    }
}
