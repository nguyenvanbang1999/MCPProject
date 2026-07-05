using Aspire.Hosting;
namespace AppHost
{
    internal class Program
    {
        public static void Main(string[] args)
        {
            var builder = DistributedApplication.CreateBuilder(args);
            var kafka = builder.AddKafka("kafka");
            var mongoServer = builder.AddMongoDB("mongodb");
            var authDb = mongoServer.AddDatabase("authdb");
            var levelDb = mongoServer.AddDatabase("leveldb");
            var resourceDb = mongoServer.AddDatabase("resourcedb");

            // Registry phải khai báo sau kafka để có thể WithReference(kafka)
            var registry = builder.AddProject<Projects.ServiceRegistry>("serviceregistry")
                .WithReference(kafka)
                .WaitFor(kafka);

            List<IResourceBuilder<ProjectResource>> listServices = new List<IResourceBuilder<ProjectResource>>();

            var auth = builder.AddProject<Projects.AccountService>("accountservice")
                .WithReference(authDb)
                .WaitFor(authDb);
            var level = builder.AddProject<Projects.LevelService>("levelservice")
                .WithReference(levelDb)
                .WaitFor(levelDb);
            var resource = builder.AddProject<Projects.ResourceService>("resourceservice")
                .WithReference(resourceDb)
                .WaitFor(resourceDb);
            listServices.Add(auth);
            listServices.Add(level);
            listServices.Add(resource);
            var gatewayTcp = builder.AddProject<Projects.GateWayTCP>("gatewaytcp")
                .WithReference(registry)
                .WithReference(kafka)
                .WaitFor(kafka)
                .WaitFor(registry);
            for (int i = 0; i < listServices.Count; i++)
            {
                var service = listServices[i];
                service.WithReference(registry);
                service.WithReference(kafka);
                service.WaitFor(kafka);
                // Service phải chờ Registry sẵn sàng trước khi POST /register
                service.WaitFor(registry);
                // Service phải chờ Gateway sẵn sàng để Kafka consumer của Gateway
                // đã chạy khi service POST /register, tránh miss ServiceRegisteredEvent
                service.WaitFor(gatewayTcp);
                gatewayTcp.WithReference(service);
            }

            var app = builder.Build();
            app.Run();
        }
    }
}
