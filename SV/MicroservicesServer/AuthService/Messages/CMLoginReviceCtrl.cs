using AccountService.Contracts;
using AccountService.DB_Service;
using MessagePack;
using ServiceRegistry.SevicesControl;
using SharedContracts.Messages;
using ServiceShare.EventBus;

namespace AccountService.Messages
{


    public class CMLoginReviceCtrl : MessageReviceController<CMLogin>
    {
        private readonly IEventBus _eventBus;
        private readonly IGatewaySender _gatewaySender;

        // Constructor injection thật (resolve qua MessageUtil.ServiceProvider trong MessageUtil.OnReviceMessage).
        // Trước đây field _eventBus không có constructor gán -> luôn null vì handler bị tạo bằng
        // Activator.CreateInstance parameterless.
        public CMLoginReviceCtrl(IEventBus eventBus, IGatewaySender gatewaySender)
        {
            _eventBus = eventBus;
            _gatewaySender = gatewaySender;
        }

        protected override async void OnReveive(CMLogin message)
        {
            SMLogin sMLogin = new SMLogin
            {
                deviceId = message.deviceId
            };
            var user = await UserRepository.Instance.GetByDeviceIdAsync(message.deviceId);
            bool isNewUser = false;
    
            if (user != null)
            {
                sMLogin.userId = user.UserId;
            }
            else
            {
                uint newUserId = await AccountCounterService.Instance.GetNextUserIdAsync();
                await UserRepository.Instance.CreateAsync(new User
                {
                    DeviceId = message.deviceId,
                    UserId = newUserId
                });
                sMLogin.userId = newUserId;
                isNewUser = true;
            }

            if (!_gatewaySender.TrySend(sMLogin))
            {
                Console.WriteLine("AccountService: Gateway connection not established yet. Cannot send login response.");
                return;
            }

            Console.WriteLine("AccountService: Đã gửi phản hồi đăng nhập đến Gateway cho user: " + message.deviceId);

            // Publish events to Kafka
            try
            {
                if (isNewUser)
                {
                    // Publish UserCreatedEvent
                    var createdEvent = new UserCreatedEvent
                    {
                        UserId = sMLogin.userId,
                        Username = message.deviceId,
                        AccountType = "Guest",
                        SourceService = "AccountService"
                    };
                    await _eventBus.PublishAsync("user-events", "creat_new_user", createdEvent);
                    Console.WriteLine($"AccountService: Published UserCreatedEvent for user: {sMLogin.userId}");
                }

                // Publish UserLoggedInEvent
                var loginEvent = new UserLoggedInEvent
                {
                    UserId = sMLogin.userId.ToString(),
                    LoginTime = DateTime.UtcNow

                };
                await _eventBus.PublishAsync("user-events", "user_login", loginEvent);
                Console.WriteLine($"AccountService: Published UserLoggedInEvent for user: {sMLogin.userId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"AccountService: Failed to publish event: {ex.Message}");
                // Don't fail the login process if event publishing fails
            }
        }
    }
}
