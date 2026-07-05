using ServiceShare.EventBus;
using SharedContracts.LogUltil;
using System.Collections.Concurrent;

namespace ServiceRegistry
{
    public class Program
    {
        // ConcurrentDictionary để an toàn khi nhiều service đăng ký đồng thời
        static ConcurrentDictionary<uint, string> dicMessageRouter = new ConcurrentDictionary<uint, string>();

        // Danh sách service đã đăng ký (serviceName -> URL). Tách riêng khỏi route theo hash để Gateway
        // vẫn kết nối được tới các service push-only (0 message type có handler, vd LevelService/ResourceService).
        static ConcurrentDictionary<string, string> dicServices = new ConcurrentDictionary<string, string>();

        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddSingleton<IMyLogger, DefaultLog>();

            // Add Kafka Event Bus (producer only — Registry chỉ publish, không consume)
            builder.Services.AddKafkaEventBusProducerOnly(builder.Configuration);

            var app = builder.Build();
            _ = app.Services.GetRequiredService<IMyLogger>();

            app.MapGet("/mapRouter", () =>
            {
                return dicMessageRouter;
            });

            // Danh sách URL của mọi service đã đăng ký — Gateway dùng để kết nối tới cả service push-only.
            app.MapGet("/services", () =>
            {
                return dicServices.Values.Distinct().ToList();
            });

            app.MapPost("/register", async (RegistryMessageRouterInfo info, IEventBus eventBus) =>
            {
                // Ghi nhận sự hiện diện của service (kể cả khi nó không xử lý message nào — push-only).
                dicServices.AddOrUpdate(info.serviceName, info.urlService, (_, _) => info.urlService);

                // Ghi/cập nhật route cho từng hash mà service này xử lý.
                // AddOrUpdate: nếu service restart với port mới, URL luôn được cập nhật đúng (kể cả hash đã tồn tại).
                foreach (var hash in info.hashMessage)
                {
                    dicMessageRouter.AddOrUpdate(hash, info.urlService, (_, _) => info.urlService);
                    Console.WriteLine($"Registered message hash {hash} to service {info.urlService}");
                }

                // LUÔN publish để Gateway kết nối tới service này — kể cả:
                //  - service push-only (0 message type có handler, vd LevelService/ResourceService), và
                //  - khi các hash đã tồn tại (service restart đổi URL).
                // Trước đây chỉ publish khi có hash MỚI, nên đăng ký của AccountService (các hash đã bị
                // Level/Resource đăng ký nhầm trước đó) không bao giờ được publish -> Gateway không biết AccountService.
                var serviceRegisteredEvent = new ServiceRegisteredEvent
                {
                    ServiceUrl = info.urlService,
                    ServiceName = info.serviceName,
                    MessageHashes = info.hashMessage,
                    RegisteredAt = DateTime.UtcNow
                };

                await eventBus.PublishAsync("service-registry", info.serviceName, serviceRegisteredEvent);
                Console.WriteLine($"[Kafka] Published ServiceRegisteredEvent for {info.serviceName} at {info.urlService}");

                return Results.Ok();
            });

            app.Run();
        }
    }
}
