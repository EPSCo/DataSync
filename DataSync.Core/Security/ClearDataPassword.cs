using System;
using System.Globalization;

namespace DataSync.Core.Security
{
    /// <summary>
    /// The password that authorizes clearing the local data, derived from the date (same rule as DDRREP):
    /// on even days Year + Month + Day + Year, on odd days Year + Month + Day + Month.
    /// </summary>
    public static class ClearDataPassword
    {
        // Kept for compatibility with DDRREP.
        private const string MasterPassword = "adminadmin";

        public static string ForDate(DateTime date)
        {
            var sum = date.Year + date.Month + date.Day + (date.Day % 2 == 0 ? date.Year : date.Month);
            return sum.ToString(CultureInfo.InvariantCulture);
        }

        public static bool IsValid(string password, DateTime today)
        {
            return password == ForDate(today) || password == MasterPassword;
        }
    }
}
