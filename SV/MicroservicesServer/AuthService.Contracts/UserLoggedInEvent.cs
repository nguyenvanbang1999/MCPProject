namespace AccountService.Contracts
{
    public class UserLoggedInEvent
    {
        public string UserId { get; set; }
        public DateTime LoginTime { get; set; }
    }
}
