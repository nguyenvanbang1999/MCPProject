using AccountService.Contracts;
using ResourceService.Contracts;
using ResourceService.DB_Service;
using ServiceRegistry.SevicesControl;
using SharedContracts.LogUltil;
using ServiceShare.EventBus;
using ServiceShare.MongoDB;

namespace ResourceService
{
    /// <summary>
    /// Entry point for the ResourceService.
    /// Handles player resource data and responds to user creation events.
    /// </summary>
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add Aspire defaults (logging, tracing, health checks)
            builder.AddServiceDefaults();
            builder.Services.AddSingleton<IMyLogger, DefaultLog>();

            // Register MongoDB services via shared ServiceShare infrastructure.
            // Aspire injects ConnectionStrings:resourcedb; falls back to appsettings.json MongoDB section.
            builder.Services.AddMongoDb(builder.Configuration, "resourcedb");
            builder.Services.AddMongoCollection<ResourceDocument>("Resource");
            builder.Services.AddSingleton<IResourceRepository, ResourceRepository>();

            // Add Kafka Event Bus (producer) + đăng ký consumer subscribe UserCreatedEvent.
            // BUG có sẵn từ trước: trước đây chỉ gọi AddKafkaEventBus (producer-only) — KafkaConsumerService
            // (hosted service tiêu thụ Kafka) không bao giờ được đăng ký/khởi động, nên UserCreatedEventHandler
            // không bao giờ nhận được UserCreatedEvent dù class đã viết đúng. Phát hiện khi verify runtime:
            // không có log "Kafka consumer started" / "Received UserCreatedEvent" ở service này.
            builder.Services.AddKafkaEventBus(builder.Configuration);
            builder.Services.AddKafkaConsumer(builder.Configuration)
                .Subscribe<UserCreatedEvent, ResourceService.Events.UserCreatedEventHandler>("user-events")
                .Build();

            // Trừu tượng hóa việc gửi message tới Gateway (thay vì truy cập trực tiếp
            // ServiceExtentions.connectToGateway static ở mọi call site) — xem GatewaySender.cs.
            builder.Services.AddSingleton<IGatewaySender, GatewaySender>();

            var app = builder.Build();
            app.MapDefaultEndpoints();

            string urlRegistry = builder.Configuration["services:serviceregistry:https:0"] ?? "";
            string urlResource = builder.Configuration["services:resourceservice:https:0"] ?? "";

            // Force assembly loading so MessageUtil can discover SMGetResourceData
            ResourceContract.Load();
            AccountContract.Load();

            // Register with Service Registry and open TCP port for Gateway — await để lỗi đăng ký không bị mất hút
            await ServiceExtentions.RegisterServiceAsync(urlRegistry, urlResource, "ResourceService");

            _ = app.Services.GetRequiredService<IMyLogger>();
            await app.RunAsync();
        }
    }
}
