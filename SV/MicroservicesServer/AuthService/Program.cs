using AccountService.Contracts;
using AccountService.DB_Service;
using ServiceRegistry.SevicesControl;
using SharedContracts.LogUltil;
using SharedContracts.Messages;
using ServiceShare.EventBus;
using ServiceShare.MongoDB;
using ServiceShare.Database;

namespace AccountService
{

    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            
            // Thêm Aspire defaults (logging, tracing, health checks,...)
            builder.AddServiceDefaults();
            builder.Services.AddSingleton<IMyLogger, DefaultLog>();
            
            // Register MongoDB services via shared ServiceShare infrastructure.
            // Aspire injects ConnectionStrings:authdb; falls back to appsettings.json MongoDB section.
            builder.Services.AddMongoDb(builder.Configuration, "authdb");
            // Register IDbCollection<User> — used by UserRepository (db-agnostic)
            builder.Services.AddMongoCollection<User>("Account");

            // Add Kafka Event Bus
            builder.Services.AddKafkaEventBus(builder.Configuration);

            // Trừu tượng hóa việc gửi message tới Gateway (thay vì truy cập trực tiếp
            // ServiceExtentions.connectToGateway static ở mọi call site) — xem GatewaySender.cs.
            builder.Services.AddSingleton<IGatewaySender, GatewaySender>();


            var app = builder.Build();
            app.MapDefaultEndpoints();

            // Cho phép MessageUtil tạo message handler (vd CMLoginReviceCtrl) qua DI container
            // thay vì Activator.CreateInstance parameterless, để constructor injection hoạt động.
            MessageUtil.ServiceProvider = app.Services;

            // Initialize static repositories.
            // Static vì UserRepository/AccountCounterService được dùng từ nhiều nơi ngoài request scope.
            UserRepository.Initialize(app.Services.GetRequiredService<IDbCollection<User>>());
            AccountCounterService.Initialize(app.Services.GetRequiredService<IMongoService>().Database);
            //Configure(app);
            //foreach (var x in builder.Configuration.AsEnumerable())
            //{
            //    Console.WriteLine(x.Key + " - " + x.Value);
            //}
            string urlRegistry = builder.Configuration["services:serviceregistry:https:0"] ?? "";
            string urlAuth = builder.Configuration["services:accountservice:https:0"] ?? "";
            AccountContract.Load();
            
            // Đăng ký với service name — await để lỗi đăng ký không bị mất hút (trước đây async void)
            await ServiceExtentions.RegisterServiceAsync(urlRegistry, urlAuth, "AccountService");

            _ = app.Services.GetRequiredService<IMyLogger>();
            await app.RunAsync();
        }
    }
}
