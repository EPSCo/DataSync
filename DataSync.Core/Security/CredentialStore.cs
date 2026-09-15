using System;
using System.IO;
using DataSync.Core.Logging;

namespace DataSync.Core.Security
{
    public class SavedCredentials
    {
        public string UserName { get; set; }

        /// <summary>Decrypted password, or null when none is saved.</summary>
        public string Password { get; set; }

        public bool Remember { get; set; }
    }

    /// <summary>
    /// The "remember me" file <c>PW.txt</c> next to the executable, in the DDRREP format:
    /// <c>UNAME=</c>, <c>PWORD=</c> (encrypted) and <c>CSAVE=</c> lines.
    /// </summary>
    public static class CredentialStore
    {
        public const string FileName = "PW.txt";

        public static string FilePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FileName);

        /// <summary>
        /// Reads the saved credentials; returns empty credentials if the file is missing or unreadable.
        /// </summary>
        public static SavedCredentials Load()
        {
            var credentials = new SavedCredentials();
            try
            {
                if (!File.Exists(FilePath))
                {
                    return credentials;
                }

                foreach (var line in File.ReadAllLines(FilePath))
                {
                    var separator = line.IndexOf('=');
                    if (separator < 0)
                    {
                        continue;
                    }

                    var key = line.Substring(0, separator).Trim().ToUpperInvariant();
                    var value = line.Substring(separator + 1).Trim();
                    switch (key)
                    {
                        case "UNAME":
                            credentials.UserName = value;
                            break;
                        case "PWORD":
                            credentials.Password = Encryption.Decrypt(value);
                            break;
                        case "CSAVE":
                            credentials.Remember = bool.TryParse(value, out var remember) && remember;
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Cannot read " + FileName);
                return new SavedCredentials();
            }

            return credentials;
        }

        public static void Save(string userName, string password)
        {
            File.WriteAllLines(FilePath, new[]
            {
                "UNAME=" + userName,
                "PWORD=" + Encryption.Encrypt(password),
                "CSAVE=True"
            });
        }

        public static void Forget()
        {
            File.WriteAllLines(FilePath, new[] { "CSAVE=False" });
        }
    }
}
