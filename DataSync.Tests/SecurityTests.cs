using System;
using DataSync.Core.Security;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DataSync.Tests
{
    [TestClass]
    public class SecurityTests
    {
        [TestMethod]
        public void Encryption_RoundTrips()
        {
            const string text = "CPU-ID@@@@Data Source=.;Initial Catalog=OfficeDDR;Integrated Security=True";

            Assert.AreEqual(text, Encryption.Decrypt(Encryption.Encrypt(text)));
        }

        [TestMethod]
        public void Encryption_DecryptToleratesSpacesAndTrailingNewline()
        {
            var cipher = Encryption.Encrypt("secret with + signs");

            Assert.AreEqual("secret with + signs", Encryption.Decrypt(cipher.Replace("+", " ") + "\r\n"));
        }

        [TestMethod]
        public void ClearDataPassword_FollowsDateRule()
        {
            // Even day: Y + M + D + Y; odd day: Y + M + D + M.
            Assert.AreEqual((2026 + 9 + 14 + 2026).ToString(), ClearDataPassword.ForDate(new DateTime(2026, 9, 14)));
            Assert.AreEqual((2026 + 9 + 15 + 9).ToString(), ClearDataPassword.ForDate(new DateTime(2026, 9, 15)));
            Assert.IsFalse(ClearDataPassword.IsValid("wrong", new DateTime(2026, 9, 15)));
        }
    }
}
