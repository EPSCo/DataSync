namespace DataSync.Core.Models
{
    /// <summary>
    /// A row of TUsersInfo (only the columns this application uses; other columns are ignored when reading).
    /// </summary>
    public class User
    {
        public int Id { get; set; }
        public string UserName { get; set; }
        public string Password { get; set; }
        public string FullName { get; set; }
        public string Title { get; set; }
        public bool IsAdmin { get; set; }
    }
}
