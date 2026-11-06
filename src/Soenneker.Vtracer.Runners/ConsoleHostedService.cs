using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Soenneker.Managers.Runners.Abstract;
using Soenneker.Vtracer.Runners.Utils.Abstract;

namespace Soenneker.Vtracer.Runners;

public sealed class ConsoleHostedService(IFileOperationsUtil files, IRunnersManager runners, IConfiguration configuration,
    IHostApplicationLifetime lifetime, ILogger<ConsoleHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string stage = Directory.CreateTempSubdirectory("vtracer-").FullName;
        try
        {
            await files.Process(stage, stoppingToken);
            string? output = configuration["Runner:OutputDirectory"];
            if (!string.IsNullOrWhiteSpace(output))
            {
                foreach (string file in Directory.EnumerateFiles(stage, "*", SearchOption.AllDirectories))
                {
                    stoppingToken.ThrowIfCancellationRequested();
                    string destination = Path.Combine(Path.GetFullPath(output), Path.GetRelativePath(stage, file));
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(file, destination, overwrite: true);
                }
                logger.LogInformation("Saved both VTracer distributions to {OutputDirectory}", Path.GetFullPath(output));
            }
            else
            {
                foreach ((string rid, string library) in new[] { ("linux-x64", "Soenneker.Vtracer.Linux"), ("win-x64", "Soenneker.Vtracer.Windows") })
                {
                    string relativePath = Path.Combine(rid, "vtracer");
                    await runners.PushIfChangesNeededForDirectory(relativePath, Path.Combine(stage, relativePath), library,
                        $"https://github.com/soenneker/{library.ToLowerInvariant()}", cancellationToken: stoppingToken);
                }
            }
            Environment.ExitCode = 0;
        }
        catch (Exception exception)
        {
            Environment.ExitCode = 1;
            logger.LogError(exception, "Could not update the VTracer distributions");
        }
        finally
        {
            try { Directory.Delete(stage, recursive: true); }
            finally { lifetime.StopApplication(); }
        }
    }
}
