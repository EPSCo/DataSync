using System;
using System.Collections.Generic;
using System.Linq;
using DataSync.Core.Models;

namespace DataSync.Core.Data
{
    /// <summary>
    /// Users from TUsersInfo in the local database.
    /// </summary>
    public class UserRepository
    {
        private readonly ConnectionKind _kind;

        public UserRepository(ConnectionKind kind)
        {
            _kind = kind;
        }

        public List<User> GetAll()
        {
            return SqlDb.QueryProcedure<User>(DatabaseConfig.Instance.GetConnectionString(_kind), SqlDb.DefaultCommandTimeout, "[dbo].[TUsersInfo_GetAll]");
        }

        /// <summary>
        /// The user with this name (case-insensitive) and password, or null.
        /// </summary>
        public static User FindByCredentials(IEnumerable<User> users, string userName, string password)
        {
            if (users == null || string.IsNullOrEmpty(userName) || password == null)
            {
                return null;
            }

            userName = userName.Trim();
            return users.FirstOrDefault(u => string.Equals(u.UserName, userName, StringComparison.CurrentCultureIgnoreCase) &&
                                             u.Password == password);
        }
    }
}
