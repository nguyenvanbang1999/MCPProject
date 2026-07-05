
namespace YARPGateWay
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // 👉 Gọi cấu hình Aspire mặc định (nếu có ServiceDefaults project)
            builder.AddServiceDefaults();

            string authUrl = Environment.GetEnvironmentVariable("AUTH_URL")??"https://auth";
            var check = Environment.GetEnvironmentVariables();

            // Thêm YARP Reverse Proxy, cấu hình route và cluster của AuthService
            builder.Services.AddReverseProxy()
            .LoadFromMemory(
                new[]
                {
                    new Yarp.ReverseProxy.Configuration.RouteConfig()
                    {
                        RouteId = "auth-route",
                        ClusterId = "auth-cluster",
                        Match = new Yarp.ReverseProxy.Configuration.RouteMatch
                        {
                            Path = "/auth/{**catch-all}"
                        },
                        Transforms = new[]
                        {
                            new Dictionary<string,string> { { "PathRemovePrefix", "/auth" } }
                        }
                    }
                },
                new[]
                {
                    new Yarp.ReverseProxy.Configuration.ClusterConfig()
                    {
                        ClusterId = "auth-cluster",
                        Destinations = new Dictionary<string, Yarp.ReverseProxy.Configuration.DestinationConfig>
                        {
                            { "auth-destination", new() { Address = authUrl } }
                        }
                    }
                }
            );

            var app = builder.Build();

            app.MapDefaultEndpoints();
            // Dùng reverse proxy làm middleware
            app.MapReverseProxy();

            app.Run();
        }
    }
}
