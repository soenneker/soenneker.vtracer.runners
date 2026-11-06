using System;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Soenneker.Vtracer.Runners.Utils;
using Soenneker.Vtracer.Runners.Utils.Abstract;
using Soenneker.Managers.Runners.Registrars;

namespace Soenneker.Vtracer.Runners;

public static class Startup
{
    public static void ConfigureServices(IServiceCollection services) => services.SetupIoC();

    public static IServiceCollection SetupIoC(this IServiceCollection services)
    {
        services.AddHostedService<ConsoleHostedService>()
            .AddSingleton(_ =>
            {
                var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Soenneker.Vtracer.Runners/1.0");
                return client;
            })
            .AddSingleton<IFileOperationsUtil, FileOperationsUtil>()
            .AddRunnersManagerAsSingleton();
        return services;
    }
}
