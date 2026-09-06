using System.Text;
using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.AITools;
using AI_WebApi.Model.DTOs.AITools;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace AI_WebApi.Services.AITools.HospitalOperations;

public sealed class FileSystemHospitalOperationsArtifactStore :
    IHospitalOperationsArtifactStore
{
    private const string Prefix = "hospital_report_v2_";
    private const string HtmlFile = "report.html";
    private const string PngFile = "report.png";
    private const string ManifestFile = "manifest.json";
    private readonly string _rootPath;
    private readonly int _maxArtifactBytes;

    public FileSystemHospitalOperationsArtifactStore(
        IOptions<HospitalOperationsOptions> options)
    {
        var configured = options.Value;
        _maxArtifactBytes = Math.Clamp(
            configured.MaxArtifactBytes,
            256 * 1024,
            20 * 1024 * 1024);
        var configuredPath = string.IsNullOrWhiteSpace(
            configured.ArtifactStoragePath)
            ? "data/hospital-operation-reports"
            : configured.ArtifactStoragePath.Trim();
        _rootPath = Path.GetFullPath(configuredPath, AppContext.BaseDirectory);
        Directory.CreateDirectory(_rootPath);
    }

    public string StorageMode => "durable_filesystem";

    public bool TryGet(
        string artifactRef,
        out HospitalOperationsArtifactManifestDto manifest)
    {
        manifest = new HospitalOperationsArtifactManifestDto();
        if (!IsValidArtifactRef(artifactRef))
        {
            return false;
        }

        try
        {
            var directory = ArtifactDirectory(artifactRef);
            var htmlPath = Path.Combine(directory, HtmlFile);
            var pngPath = Path.Combine(directory, PngFile);
            var manifestPath = Path.Combine(directory, ManifestFile);
            if (!ValidFile(htmlPath) || !ValidFile(pngPath) ||
                !ValidManifestFile(manifestPath))
            {
                return false;
            }

            var parsed = JsonConvert.DeserializeObject<
                HospitalOperationsArtifactManifestDto>(
                File.ReadAllText(manifestPath, Encoding.UTF8));
            if (parsed == null ||
                !string.Equals(
                    parsed.ArtifactRef,
                    artifactRef,
                    StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(parsed.QueryFingerprint) ||
                string.IsNullOrWhiteSpace(parsed.ContentFingerprint) ||
                !parsed.Formats.Contains("html", StringComparer.Ordinal) ||
                !parsed.Formats.Contains("png", StringComparer.Ordinal))
            {
                return false;
            }

            manifest = parsed;
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public HospitalOperationsArtifactWriteResult Store(
        HospitalOperationsArtifactManifestDto manifest,
        string html,
        byte[] png)
    {
        ValidateInput(manifest, html, png);
        if (TryGet(manifest.ArtifactRef, out var existing))
        {
            EnsureSameIdentity(existing, manifest);
            return new HospitalOperationsArtifactWriteResult
            {
                Manifest = existing,
                Reused = true
            };
        }

        var finalDirectory = ArtifactDirectory(manifest.ArtifactRef);
        if (Directory.Exists(finalDirectory))
        {
            if (TryGet(manifest.ArtifactRef, out existing))
            {
                EnsureSameIdentity(existing, manifest);
                return new HospitalOperationsArtifactWriteResult
                {
                    Manifest = existing,
                    Reused = true
                };
            }
            throw new InvalidOperationException(
                "An incomplete hospital report artifact already exists.");
        }

        var temporaryDirectory = Path.Combine(
            _rootPath,
            $".{manifest.ArtifactRef}.{Guid.NewGuid():N}.tmp");
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            File.WriteAllText(
                Path.Combine(temporaryDirectory, HtmlFile),
                html,
                new UTF8Encoding(false));
            File.WriteAllBytes(
                Path.Combine(temporaryDirectory, PngFile),
                png);
            File.WriteAllText(
                Path.Combine(temporaryDirectory, ManifestFile),
                JsonConvert.SerializeObject(manifest, Formatting.Indented),
                new UTF8Encoding(false));
            try
            {
                Directory.Move(temporaryDirectory, finalDirectory);
            }
            catch (IOException) when (Directory.Exists(finalDirectory))
            {
                if (!TryGet(manifest.ArtifactRef, out existing))
                {
                    throw;
                }
                EnsureSameIdentity(existing, manifest);
                return new HospitalOperationsArtifactWriteResult
                {
                    Manifest = existing,
                    Reused = true
                };
            }

            return new HospitalOperationsArtifactWriteResult
            {
                Manifest = manifest,
                Reused = false
            };
        }
        finally
        {
            CleanupTemporaryDirectory(temporaryDirectory);
        }
    }

    public HospitalReportAccessResult Resolve(
        string artifactRef,
        string format)
    {
        var normalizedFormat = format?.Trim().ToLowerInvariant();
        if (!IsValidArtifactRef(artifactRef) ||
            normalizedFormat is not ("html" or "png"))
        {
            return Result(HospitalReportAccessStatus.Invalid);
        }
        if (!TryGet(artifactRef, out _))
        {
            return Result(HospitalReportAccessStatus.Missing);
        }

        try
        {
            var path = Path.Combine(
                ArtifactDirectory(artifactRef),
                normalizedFormat == "html" ? HtmlFile : PngFile);
            var bytes = File.ReadAllBytes(path);
            return bytes.Length is > 0 && bytes.Length <= _maxArtifactBytes
                ? new HospitalReportAccessResult
                {
                    Status = HospitalReportAccessStatus.Available,
                    Bytes = bytes,
                    MimeType = normalizedFormat == "html"
                        ? "text/html; charset=utf-8"
                        : "image/png"
                }
                : Result(HospitalReportAccessStatus.Missing);
        }
        catch (IOException)
        {
            return Result(HospitalReportAccessStatus.Missing);
        }
        catch (UnauthorizedAccessException)
        {
            return Result(HospitalReportAccessStatus.Missing);
        }
    }

    private void ValidateInput(
        HospitalOperationsArtifactManifestDto manifest,
        string html,
        byte[] png)
    {
        if (!IsValidArtifactRef(manifest.ArtifactRef) ||
            string.IsNullOrWhiteSpace(manifest.QueryFingerprint) ||
            string.IsNullOrWhiteSpace(manifest.ContentFingerprint))
        {
            throw new InvalidOperationException(
                "Invalid hospital report artifact identity.");
        }
        var htmlLength = Encoding.UTF8.GetByteCount(html ?? string.Empty);
        if (htmlLength is <= 0 || htmlLength > _maxArtifactBytes ||
            png.Length is <= 0 || png.Length > _maxArtifactBytes)
        {
            throw new InvalidOperationException(
                "Hospital report artifact exceeds the durable storage limit.");
        }
    }

    private static void EnsureSameIdentity(
        HospitalOperationsArtifactManifestDto existing,
        HospitalOperationsArtifactManifestDto requested)
    {
        if (!string.Equals(
                existing.QueryFingerprint,
                requested.QueryFingerprint,
                StringComparison.Ordinal) ||
            !string.Equals(
                existing.ContentFingerprint,
                requested.ContentFingerprint,
                StringComparison.Ordinal) ||
            !string.Equals(
                existing.TemplateVersion,
                requested.TemplateVersion,
                StringComparison.Ordinal) ||
            !string.Equals(
                existing.AnalysisRuleVersion,
                requested.AnalysisRuleVersion,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Hospital report artifact reference collision detected.");
        }
    }

    private bool ValidFile(string path)
    {
        var file = new FileInfo(path);
        return file.Exists && file.Length is > 0 &&
               file.Length <= _maxArtifactBytes;
    }

    private static bool ValidManifestFile(string path)
    {
        var file = new FileInfo(path);
        return file.Exists && file.Length is > 0 and <= 256 * 1024;
    }

    private string ArtifactDirectory(string artifactRef) =>
        Path.Combine(_rootPath, artifactRef);

    private static bool IsValidArtifactRef(string artifactRef) =>
        !string.IsNullOrWhiteSpace(artifactRef) &&
        artifactRef.StartsWith(Prefix, StringComparison.Ordinal) &&
        artifactRef.Length == Prefix.Length + 32 &&
        artifactRef.AsSpan(Prefix.Length).ToString().All(Uri.IsHexDigit);

    private static HospitalReportAccessResult Result(
        HospitalReportAccessStatus status) => new() { Status = status };

    private static void CleanupTemporaryDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }
        foreach (var name in new[] { HtmlFile, PngFile, ManifestFile })
        {
            var path = Path.Combine(directory, name);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        Directory.Delete(directory, false);
    }
}
