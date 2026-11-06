using System;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Soenneker.Vtracer.Runners.Utils.Abstract;

namespace Soenneker.Vtracer.Runners.Utils;

public sealed class FileOperationsUtil(HttpClient httpClient, IConfiguration configuration, ILogger<FileOperationsUtil> logger) : IFileOperationsUtil
{
    private const string Repository = "visioncortex/vtracer";

    public async ValueTask Process(string outputDirectory, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repository}/releases/latest");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        string? token = configuration["GH:Token"];
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using JsonDocument release = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        string tag = release.RootElement.GetProperty("tag_name").GetString()!;
        string releaseUrl = release.RootElement.GetProperty("html_url").GetString()!;
        string license = await httpClient.GetStringAsync(
            $"https://raw.githubusercontent.com/{Repository}/{Uri.EscapeDataString(tag)}/LICENSE", cancellationToken);
        if (string.IsNullOrWhiteSpace(license))
            throw new InvalidDataException("The upstream VTracer license is empty.");

        // Resolve one release so an upstream update during this run cannot mix platform versions.
        await Prepare(release.RootElement, tag, releaseUrl, license, "win-x64", "vtracer-x86_64-pc-windows-msvc.zip", "vtracer.exe", outputDirectory, cancellationToken);
        await Prepare(release.RootElement, tag, releaseUrl, license, "linux-x64", "vtracer-x86_64-unknown-linux-musl.tar.gz", "vtracer", outputDirectory, cancellationToken);
    }

    private async Task Prepare(JsonElement release, string tag, string releaseUrl, string license, string rid, string assetName,
        string executableName, string outputDirectory, CancellationToken cancellationToken)
    {
        JsonElement[] matches = release.GetProperty("assets").EnumerateArray()
            .Where(asset => asset.GetProperty("name").GetString() == assetName).ToArray();
        if (matches.Length != 1)
            throw new FileNotFoundException($"Release {tag} must contain exactly one '{assetName}' asset.");

        JsonElement asset = matches[0];
        string assetUrl = asset.GetProperty("browser_download_url").GetString()!;
        byte[] archive = await httpClient.GetByteArrayAsync(assetUrl, cancellationToken);
        string sha256 = Convert.ToHexStringLower(SHA256.HashData(archive));
        if (asset.TryGetProperty("digest", out JsonElement digest) && digest.ValueKind == JsonValueKind.String &&
            !string.Equals(digest.GetString(), $"sha256:{sha256}", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Checksum mismatch for {assetName}.");

        string destination = Path.Combine(outputDirectory, rid, "vtracer");
        Directory.CreateDirectory(destination);
        string executablePath = Path.Combine(destination, executableName);
        using var archiveStream = new MemoryStream(archive, writable: false);
        if (assetName.EndsWith(".zip", StringComparison.Ordinal))
        {
            using var zip = new ZipArchive(archiveStream, ZipArchiveMode.Read);
            ZipArchiveEntry[] entries = zip.Entries.Where(entry => entry.Name == executableName).ToArray();
            if (entries.Length != 1)
                throw new InvalidDataException($"Expected one {executableName} in {assetName}.");
            await using Stream source = entries[0].Open();
            await using FileStream target = File.Create(executablePath);
            await source.CopyToAsync(target, cancellationToken);
        }
        else
        {
            using var gzip = new GZipStream(archiveStream, CompressionMode.Decompress);
            using var tar = new TarReader(gzip);
            var found = false;
            while (await tar.GetNextEntryAsync(cancellationToken: cancellationToken) is { } entry)
            {
                if (Path.GetFileName(entry.Name) != executableName)
                    continue;
                if (found || entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile) || entry.DataStream is null)
                    throw new InvalidDataException($"Expected one regular {executableName} file in {assetName}.");
                await using FileStream target = File.Create(executablePath);
                await entry.DataStream.CopyToAsync(target, cancellationToken);
                found = true;
            }
            if (!found)
                throw new FileNotFoundException($"{assetName} does not contain {executableName}.");
        }

        if (new FileInfo(executablePath).Length == 0)
            throw new InvalidDataException($"{executableName} is empty.");
        if (OperatingSystem.IsLinux() && rid == "linux-x64")
            File.SetUnixFileMode(executablePath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        await File.WriteAllTextAsync(Path.Combine(destination, "LICENSE.vtracer"), license, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(destination, "SOURCE.txt"),
            $"Release: {releaseUrl}\nVersion: {tag}\nAsset: {assetUrl}\nSHA256: {sha256}\n", cancellationToken);
        logger.LogInformation("Prepared VTracer {Tag} for {RuntimeIdentifier} at {Destination}", tag, rid, destination);
    }
}
