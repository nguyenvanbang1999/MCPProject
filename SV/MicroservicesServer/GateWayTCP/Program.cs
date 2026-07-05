using AccountService.Contracts;
using LevelService.Contracts;
using Microsoft.Extensions.Logging.Console;
using ResourceService.Contracts;
using ServiceShare.EventBus;
using SharedContracts.LogUltil;
using SharedContracts.Messages;
using System.Text;

namespace GateWayTCP
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            var builder = WebApplication.CreateBuilder(args);
            builder.AddServiceDefaults();
            builder.Services.AddHttpClient(); // dùng để gọi service nội bộ
            builder.Services.AddSingleton<TcpGatewayService>();
            builder.Services.AddSingleton<MessageRouter>();
            builder.Services.AddSingleton<IMyLogger, DefaultLog>();
            
            // Add Kafka Event Bus + consumer subscribe đúng cách qua KafkaConsumerBuilder
            builder.Services.AddKafkaEventBus(builder.Configuration);
            builder.Services.AddKafkaConsumer(builder.Configuration)
                .Subscribe<ServiceRegisteredEvent, ServiceRegistrationHandler>("service-registry")
                .Build();
            
            // Load existing routes sau khi app fully started (không dùng Task.Delay)
            builder.Services.AddHostedService<GatewayStartupService>();
            
            builder.Logging.AddSimpleConsole(opt =>
            {
                opt.ColorBehavior = LoggerColorBehavior.Enabled;
                opt.SingleLine = true;
                opt.TimestampFormat = "HH:mm:ss ";
            });
            var app = builder.Build();
            app.MapDefaultEndpoints();

            // BUG có sẵn từ trước, phát hiện khi verify runtime: Gateway chỉ force-load AccountContract,
            // thiếu LevelContract/ResourceContract. MessageUtil.AllMessage cache 1 lần duy nhất (lazy) khi
            // message đầu tiên được deserialize; nếu lúc đó LevelService.Contracts/ResourceService.Contracts
            // chưa được CLR load vào AppDomain, Gateway vĩnh viễn không nhận diện được SMGetLevelData/
            // SMGetResourceData (log: "Không tìm thấy kiểu tin nhắn cho MessageTypeId: ..."), dù handler
            // (SMGetLevelDataReviceController/SMGetResourceDataReviceController) đã viết đúng.
            AccountContract.Load();
            LevelContract.Load();
            ResourceContract.Load();

            // Cho phép MessageUtil tạo message handler qua DI container thay vì
            // Activator.CreateInstance parameterless, để constructor injection hoạt động.
            MessageUtil.ServiceProvider = app.Services;

            app.MapGet("/", () => "TCP Gateway Service is running.");

            // TCP Gateway được start bởi GatewayStartupService sau khi load routes xong
            var log = app.Services.GetRequiredService<IMyLogger>();

            app.Run();
        }
    }
}
