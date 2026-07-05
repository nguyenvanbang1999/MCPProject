namespace AccountService.Contracts
{
    public class UserCreatedEvent
    {
        public uint UserId { get; set; }
        public string Username { get; set; }
        public string AccountType { get; set; } = "Guest";
        public string SourceService { get; set; } = "AccountService";
    }
}
