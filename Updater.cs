using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuotaWisp;

public sealed record SemanticVersion(int Major, int Minor, int Patch, string? PreRelease = null) : IComparable<SemanticVersion>
{
    public static bool TryParse(string? value, out SemanticVersion version)
    {
        version = new(0, 0, 0);
        if (string.IsNullOrWhiteSpace(value)) return false;
        value = value.Trim();
        if (value.StartsWith('v')) value = value[1..];
        var buildIndex = value.IndexOf('+');
        if (buildIndex >= 0) value = value[..buildIndex];
        var dashIndex = value.IndexOf('-');
        var core = dashIndex >= 0 ? value[..dashIndex] : value;
        var preRelease = dashIndex >= 0 ? value[(dashIndex + 1)..] : null;
        var parts = core.Split('.');
        if (parts.Length != 3 || parts.Any(x => x.Length == 0 || (x.Length > 1 && x[0] == '0')) ||
            !int.TryParse(parts[0], out var major) || !int.TryParse(parts[1], out var minor) || !int.TryParse(parts[2], out var patch) ||
            major < 0 || minor < 0 || patch < 0 || (preRelease is not null && !ValidPreRelease(preRelease))) return false;
        version = new(major, minor, patch, preRelease);
        return true;
    }

    private static bool ValidPreRelease(string value) => value.Length > 0 && value.Split('.').All(part =>
        part.Length > 0 && part.All(c => char.IsAsciiLetterOrDigit(c) || c == '-') &&
        !(part.Length > 1 && part.All(char.IsDigit) && part[0] == '0'));

    public int CompareTo(SemanticVersion? other)
    {
        if (other is null) return 1;
        var core = Major.CompareTo(other.Major);
        if (core == 0) core = Minor.CompareTo(other.Minor);
        if (core == 0) core = Patch.CompareTo(other.Patch);
        if (core != 0) return core;
        if (PreRelease is null) return other.PreRelease is null ? 0 : 1;
        if (other.PreRelease is null) return -1;
        var left = PreRelease.Split('.');
        var right = other.PreRelease.Split('.');
        for (var index = 0; index < Math.Max(left.Length, right.Length); index++)
        {
            if (index >= left.Length) return -1;
            if (index >= right.Length) return 1;
            if (left[index] == right[index]) continue;
            var leftNumeric = int.TryParse(left[index], out var leftNumber);
            var rightNumeric = int.TryParse(right[index], out var rightNumber);
            if (leftNumeric && rightNumeric) return leftNumber.CompareTo(rightNumber);
            if (leftNumeric != rightNumeric) return leftNumeric ? -1 : 1;
            return string.CompareOrdinal(left[index], right[index]);
        }
        return 0;
    }

    public override string ToString() => $"{Major}.{Minor}.{Patch}{(PreRelease is null ? "" : $"-{PreRelease}")}";
}

public sealed record UpdateRelease(SemanticVersion Version, Uri ArchiveUrl, Uri ChecksumUrl);

public sealed record UpdatePackage(SemanticVersion Version, string RootDirectory, string PayloadDirectory)
{
    public void Delete()
    {
        try { if (Directory.Exists(RootDirectory)) Directory.Delete(RootDirectory, true); }
        catch { }
    }
}

public sealed class UpdateManager : IDisposable
{
    internal const string ArchiveName = "QuotaWisp-win-x64.zip";
    internal const string ChecksumName = ArchiveName + ".sha256";
    private const long MaximumArchiveBytes = 512L * 1024 * 1024;
    private static readonly Uri LatestReleaseApi = new("https://api.github.com/repos/Demian87/codex-quota-pet-win/releases/latest");
    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;

    public static SemanticVersion CurrentVersion
    {
        get
        {
            var text = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";
            return SemanticVersion.TryParse(text, out var parsed) ? parsed : new(0, 0, 0);
        }
    }

    public UpdateManager(HttpClient? httpClient = null)
    {
        _ownsHttpClient = httpClient is null;
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        if (!_http.DefaultRequestHeaders.UserAgent.Any()) _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("QuotaWisp", CurrentVersion.ToString()));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public void Dispose()
    {
        if (_ownsHttpClient) _http.Dispose();
    }

    public async Task<UpdateRelease?> CheckAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(LatestReleaseApi, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, cancellationToken: cancellationToken)
            ?? throw new InvalidDataException("GitHub returned an empty release response.");
        if (!SemanticVersion.TryParse(release.TagName, out var version)) throw new InvalidDataException("The latest release tag is not a semantic version.");
        if (version.CompareTo(CurrentVersion) <= 0) return null;
        var archive = FindAsset(release, ArchiveName);
        var checksum = FindAsset(release, ChecksumName);
        return new(version, ValidateAssetUri(archive), ValidateAssetUri(checksum));
    }

    public async Task<UpdatePackage> DownloadAndVerifyAsync(UpdateRelease release, CancellationToken cancellationToken)
    {
        var root = Path.Combine(SettingsStore.DataDirectory, "updates", $"{release.Version}-{Guid.NewGuid():N}");
        var payload = Path.Combine(root, "payload");
        Directory.CreateDirectory(root);
        try
        {
            var checksumText = await DownloadTextLimitedAsync(release.ChecksumUrl, 16 * 1024, cancellationToken);
            var expectedHash = ParseChecksum(checksumText, ArchiveName);
            var archivePath = Path.Combine(root, ArchiveName);
            var actualHash = await DownloadFileAndHashAsync(release.ArchiveUrl, archivePath, MaximumArchiveBytes, cancellationToken);
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(expectedHash), Convert.FromHexString(actualHash)))
                throw new InvalidDataException("The downloaded update failed SHA-256 verification.");
            SafeZipExtractor.Extract(archivePath, payload);
            if (!File.Exists(Path.Combine(payload, "QuotaWisp.exe"))) throw new InvalidDataException("The update archive does not contain QuotaWisp.exe.");
            return new(release.Version, root, payload);
        }
        catch
        {
            try { Directory.Delete(root, true); } catch { }
            throw;
        }
    }

    public void StartInstaller(UpdatePackage package)
    {
        var currentExecutable = Environment.ProcessPath ?? throw new InvalidOperationException("The application executable path is unavailable.");
        var installDirectory = Path.GetDirectoryName(currentExecutable) ?? throw new InvalidOperationException("The application directory is unavailable.");
        var helperDirectory = Path.Combine(SettingsStore.DataDirectory, "updates", $"helper-{Guid.NewGuid():N}");
        Directory.CreateDirectory(helperDirectory);
        var helperPath = Path.Combine(helperDirectory, "QuotaWisp.UpdateHelper.exe");
        try
        {
            File.Copy(currentExecutable, helperPath, false);
            var start = new ProcessStartInfo(helperPath) { UseShellExecute = false, WorkingDirectory = helperDirectory };
            start.ArgumentList.Add("--apply-update");
            start.ArgumentList.Add(Environment.ProcessId.ToString());
            start.ArgumentList.Add(package.RootDirectory);
            start.ArgumentList.Add(package.PayloadDirectory);
            start.ArgumentList.Add(installDirectory);
            start.ArgumentList.Add(Path.GetFileName(currentExecutable));
            if (Process.Start(start) is null) throw new InvalidOperationException("The update installer could not be started.");
        }
        catch
        {
            try { Directory.Delete(helperDirectory, true); } catch { }
            throw;
        }
    }

    public static string ParseChecksum(string text, string expectedFileName)
    {
        var lines = text.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length != 1) throw new InvalidDataException("The checksum file must contain exactly one entry.");
        var parts = lines[0].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || parts[0].Length != 64 || !parts[0].All(Uri.IsHexDigit) ||
            (parts[1] != expectedFileName && parts[1] != "*" + expectedFileName))
            throw new InvalidDataException("The checksum file has an invalid format or file name.");
        return parts[0].ToUpperInvariant();
    }

    private static GitHubAsset FindAsset(GitHubRelease release, string name)
    {
        var matches = release.Assets.Where(x => string.Equals(x.Name, name, StringComparison.Ordinal)).ToArray();
        return matches.Length == 1 ? matches[0] : throw new InvalidDataException($"The release must contain exactly one {name} asset.");
    }

    private static Uri ValidateAssetUri(GitHubAsset asset)
    {
        if (!Uri.TryCreate(asset.DownloadUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            !uri.AbsolutePath.StartsWith("/Demian87/codex-quota-pet-win/releases/download/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The release contains an unexpected asset URL.");
        return uri;
    }

    private async Task<string> DownloadTextLimitedAsync(Uri uri, int maximumBytes, CancellationToken token)
    {
        using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > maximumBytes) throw new InvalidDataException("The checksum response is too large.");
        await using var input = await response.Content.ReadAsStreamAsync(token);
        using var memory = new MemoryStream();
        await CopyLimitedAsync(input, memory, maximumBytes, token);
        return System.Text.Encoding.UTF8.GetString(memory.ToArray()).TrimStart('\uFEFF');
    }

    private async Task<string> DownloadFileAndHashAsync(Uri uri, string destination, long maximumBytes, CancellationToken token)
    {
        using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > maximumBytes) throw new InvalidDataException("The update archive is too large.");
        await using var input = await response.Content.ReadAsStreamAsync(token);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        using var sha256 = SHA256.Create();
        using var hashing = new CryptoStream(output, sha256, CryptoStreamMode.Write, true);
        await CopyLimitedAsync(input, hashing, maximumBytes, token);
        await hashing.FlushFinalBlockAsync(token);
        return Convert.ToHexString(sha256.Hash!);
    }

    private static async Task CopyLimitedAsync(Stream input, Stream output, long maximumBytes, CancellationToken token)
    {
        var buffer = new byte[81920];
        long total = 0;
        int count;
        while ((count = await input.ReadAsync(buffer, token)) > 0)
        {
            total += count;
            if (total > maximumBytes) throw new InvalidDataException("The downloaded file exceeds the size limit.");
            await output.WriteAsync(buffer.AsMemory(0, count), token);
        }
    }

    private sealed record GitHubRelease([property: JsonPropertyName("tag_name")] string? TagName,
        [property: JsonPropertyName("assets")] GitHubAsset[] Assets);
    private sealed record GitHubAsset([property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("browser_download_url")] string DownloadUrl);
}

public static class SafeZipExtractor
{
    private const int MaximumEntries = 10_000;
    private const long MaximumExpandedBytes = 1024L * 1024 * 1024;

    public static void Extract(string archivePath, string destinationDirectory)
    {
        var root = Path.GetFullPath(destinationDirectory) + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(root);
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count > MaximumEntries) throw new InvalidDataException("The update archive contains too many entries.");
        long expandedBytes = 0;
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            expandedBytes = checked(expandedBytes + entry.Length);
            if (expandedBytes > MaximumExpandedBytes) throw new InvalidDataException("The expanded update is too large.");
            if ((entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0 || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                throw new InvalidDataException("Links are not allowed in update archives.");
            var relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(relative) || Path.IsPathFullyQualified(relative)) throw new InvalidDataException("The update archive contains an invalid path.");
            var destination = Path.GetFullPath(Path.Combine(root, relative));
            if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The update archive attempts to write outside its destination.");
            if (!destinations.Add(destination)) throw new InvalidDataException("The update archive contains duplicate paths.");
            if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(destination); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var input = entry.Open();
            using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            input.CopyTo(output);
        }
    }
}

public static class UpdateBootstrapper
{
    public static async Task<bool> TryRunInstallerAsync(string[] args)
    {
        if (args.Length != 6 || args[0] != "--apply-update") return false;
        try
        {
            if (!int.TryParse(args[1], out var processId) || processId <= 0) throw new InvalidDataException("Invalid parent process id.");
            var updateRoot = Path.GetFullPath(args[2]);
            var payload = Path.GetFullPath(args[3]);
            var installDirectory = Path.GetFullPath(args[4]);
            var executableName = args[5];
            if (executableName != Path.GetFileName(executableName) || !executableName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Invalid executable name.");
            var allowedUpdateRoot = Path.GetFullPath(Path.Combine(SettingsStore.DataDirectory, "updates")) + Path.DirectorySeparatorChar;
            if (!updateRoot.StartsWith(allowedUpdateRoot, StringComparison.OrdinalIgnoreCase) ||
                !payload.StartsWith(updateRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The update package is outside the application update directory.");

            try
            {
                using var parent = Process.GetProcessById(processId);
                await parent.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
            }
            catch (ArgumentException) { }

            InstallWithRollback(payload, installDirectory);
            var target = Path.Combine(installDirectory, executableName);
            var restart = new ProcessStartInfo(target) { UseShellExecute = true, WorkingDirectory = installDirectory };
            restart.ArgumentList.Add("--cleanup-update");
            restart.ArgumentList.Add(updateRoot);
            restart.ArgumentList.Add(Path.GetDirectoryName(Environment.ProcessPath!)!);
            Process.Start(restart);
        }
        catch (Exception error)
        {
            try
            {
                Directory.CreateDirectory(SettingsStore.DataDirectory);
                File.WriteAllText(Path.Combine(SettingsStore.DataDirectory, "update-error.log"), $"{DateTimeOffset.Now:O}\n{error}");
                if (args.Length == 6)
                {
                    var target = Path.Combine(Path.GetFullPath(args[4]), Path.GetFileName(args[5]));
                    if (File.Exists(target))
                    {
                        var restart = new ProcessStartInfo(target) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(target)! };
                        restart.ArgumentList.Add("--update-install-failed");
                        restart.ArgumentList.Add("--cleanup-update");
                        restart.ArgumentList.Add(Path.GetFullPath(args[2]));
                        restart.ArgumentList.Add(Path.GetDirectoryName(Environment.ProcessPath!)!);
                        Process.Start(restart);
                    }
                }
            }
            catch { }
        }
        return true;
    }

    internal static void InstallWithRollback(string payload, string installDirectory)
    {
        if (!Directory.Exists(payload)) throw new DirectoryNotFoundException("The verified update payload is missing.");
        Directory.CreateDirectory(installDirectory);
        var files = Directory.GetFiles(payload, "*", SearchOption.AllDirectories);
        if (files.Length == 0) throw new InvalidDataException("The update payload is empty.");
        var backup = Path.Combine(installDirectory, $".quotawisp-rollback-{Guid.NewGuid():N}");
        var installed = new List<(string Destination, string? Backup)>();
        Directory.CreateDirectory(backup);
        try
        {
            foreach (var source in files)
            {
                var relative = Path.GetRelativePath(payload, source);
                if (relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathFullyQualified(relative))
                    throw new InvalidDataException("Invalid update payload path.");
                var destination = Path.GetFullPath(Path.Combine(installDirectory, relative));
                var installRoot = Path.GetFullPath(installDirectory) + Path.DirectorySeparatorChar;
                if (!destination.StartsWith(installRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Invalid installation path.");
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                string? backupPath = null;
                if (File.Exists(destination))
                {
                    backupPath = Path.Combine(backup, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                    File.Copy(destination, backupPath, true);
                }
                var temporary = destination + $".update-{Guid.NewGuid():N}.tmp";
                File.Copy(source, temporary, false);
                File.Move(temporary, destination, true);
                installed.Add((destination, backupPath));
            }
        }
        catch
        {
            foreach (var item in installed.AsEnumerable().Reverse())
            {
                try
                {
                    if (item.Backup is not null) File.Copy(item.Backup, item.Destination, true);
                    else File.Delete(item.Destination);
                }
                catch { }
            }
            throw;
        }
        finally
        {
            try { Directory.Delete(backup, true); } catch { }
        }
    }

    public static void ScheduleCleanup(string[] args)
    {
        var index = Array.IndexOf(args, "--cleanup-update");
        if (index < 0 || index + 2 >= args.Length) return;
        var paths = new[] { args[index + 1], args[index + 2] };
        _ = Task.Run(async () =>
        {
            await Task.Delay(1500);
            var allowedRoot = Path.GetFullPath(Path.Combine(SettingsStore.DataDirectory, "updates")) + Path.DirectorySeparatorChar;
            foreach (var path in paths)
            {
                try
                {
                    var full = Path.GetFullPath(path);
                    if (full.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase) && Directory.Exists(full)) Directory.Delete(full, true);
                }
                catch { }
            }
        });
    }
}
