using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.AITools;
using AI_WebApi.Model.DTOs.AITools;
using Microsoft.Extensions.Options;

namespace AI_WebApi.Services.AITools.IReGo;

/// <summary>
/// Durable report image storage. The opaque artifact reference is the stable
/// identity; short-lived signed tokens are retained only for compatibility.
/// </summary>
public sealed class FileSystemIReGoReportArtifactStore :
    IIReGoReportArtifactStore
{
    private const string TokenVersion = IReGoDataVersion.ImageTokenVersion;
    private const int SignatureBytes = 16;

    private readonly byte[] _key;
    private readonly string _rootPath;
    private readonly int _maxImageBytes;

    public FileSystemIReGoReportArtifactStore(
        IOptions<IReGoToolOptions> options)
    {
        var configured = options.Value.ReportImage;
        _key = Encoding.UTF8.GetBytes(
            configured.SigningKey?.Trim() ?? string.Empty);
        _maxImageBytes = Math.Clamp(
            configured.MaxImageBytes,
            256 * 1024,
            20 * 1024 * 1024);

        var configuredPath = string.IsNullOrWhiteSpace(configured.StoragePath)
            ? "data/patient-reports"
            : configured.StoragePath.Trim();
        _rootPath = Path.GetFullPath(configuredPath, AppContext.BaseDirectory);
        if (!string.Equals(Path.GetFileName(_rootPath), IReGoDataVersion.StorageDirectory, StringComparison.Ordinal))
            _rootPath = Path.Combine(_rootPath, IReGoDataVersion.StorageDirectory);
        Directory.CreateDirectory(_rootPath);
    }

    public bool IsConfigured => _key.Length >= 32;
    public string StorageMode => "durable_filesystem";

    public string CreateArtifactRef(
        string patientRef,
        string sessionRef,
        string sourceSnapshot,
        string templateVersion)
    {
        EnsureConfigured();
        return IReGoArtifactReference.Create(
            _key,
            patientRef,
            sessionRef,
            sourceSnapshot,
            templateVersion);
    }

    public bool TryGet(string artifactRef, out byte[] bytes)
    {
        if (!IsValidArtifactRef(artifactRef))
        {
            bytes = Array.Empty<byte>();
            return false;
        }

        var path = ArtifactPath(artifactRef);
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length <= 0 || file.Length > _maxImageBytes)
            {
                bytes = Array.Empty<byte>();
                return false;
            }

            bytes = File.ReadAllBytes(path);
            return bytes.Length > 0 && bytes.Length <= _maxImageBytes;
        }
        catch (IOException)
        {
            bytes = Array.Empty<byte>();
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            bytes = Array.Empty<byte>();
            return false;
        }
    }

    public void Store(string artifactRef, byte[] bytes)
    {
        EnsureConfigured();
        if (!IsValidArtifactRef(artifactRef))
        {
            throw new InvalidOperationException("Invalid IREGO artifact reference.");
        }

        if (bytes.Length == 0 || bytes.Length > _maxImageBytes)
        {
            throw new InvalidOperationException(
                "Rendered report image exceeds the configured durable storage limit.");
        }

        var path = ArtifactPath(artifactRef);
        if (File.Exists(path))
        {
            return;
        }

        var temporaryPath = Path.Combine(
            _rootPath,
            $".{artifactRef}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporaryPath, bytes);
            try
            {
                File.Move(temporaryPath, path, false);
            }
            catch (IOException) when (File.Exists(path))
            {
                // Another request completed the same content-addressed artifact.
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public string IssueAccessToken(string artifactRef, DateTimeOffset expiresAt)
    {
        EnsureConfigured();
        if (!IsValidArtifactRef(artifactRef))
        {
            throw new InvalidOperationException("Invalid IREGO artifact reference.");
        }

        var payload = string.Join(
            "|",
            TokenVersion,
            IReGoDataVersion.Namespace,
            IReGoDataVersion.Generation,
            artifactRef,
            expiresAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        return string.Join(
            ".",
            TokenVersion,
            Base64Url(Encoding.UTF8.GetBytes(payload)),
            Base64Url(Sign(payload)));
    }

    public ReportImageAccessResult Resolve(string token)
    {
        if (!TryReadToken(token, out var artifactRef, out var expiresAt))
        {
            return Result(ReportImageAccessStatus.Invalid);
        }

        if (DateTimeOffset.UtcNow >= expiresAt)
        {
            return Result(ReportImageAccessStatus.Expired, artifactRef);
        }

        return ResolveArtifact(artifactRef);
    }

    public ReportImageAccessResult ResolveArtifact(string artifactRef)
    {
        if (!IsConfigured || !IsValidArtifactRef(artifactRef))
        {
            return Result(ReportImageAccessStatus.Invalid);
        }

        return TryGet(artifactRef, out var bytes)
            ? new ReportImageAccessResult
            {
                Status = ReportImageAccessStatus.Available,
                ArtifactRef = artifactRef,
                Bytes = bytes
            }
            : Result(ReportImageAccessStatus.Missing, artifactRef);
    }

    private bool TryReadToken(
        string token,
        out string artifactRef,
        out DateTimeOffset expiresAt)
    {
        artifactRef = string.Empty;
        expiresAt = DateTimeOffset.MinValue;
        if (!IsConfigured || string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var parts = token.Split('.');
        if (parts.Length != 3 || parts[0] != TokenVersion)
        {
            return false;
        }

        byte[] payloadBytes;
        byte[] suppliedSignature;
        try
        {
            payloadBytes = FromBase64Url(parts[1]);
            suppliedSignature = FromBase64Url(parts[2]);
        }
        catch (FormatException)
        {
            return false;
        }

        var payload = Encoding.UTF8.GetString(payloadBytes);
        var expectedSignature = Sign(payload);
        if (suppliedSignature.Length != expectedSignature.Length ||
            !CryptographicOperations.FixedTimeEquals(
                suppliedSignature,
                expectedSignature))
        {
            return false;
        }

        var payloadParts = payload.Split('|');
        if (payloadParts.Length != 5 ||
            payloadParts[0] != TokenVersion ||
            payloadParts[1] != IReGoDataVersion.Namespace ||
            payloadParts[2] != IReGoDataVersion.Generation ||
            !IsValidArtifactRef(payloadParts[3]) ||
            !long.TryParse(
                payloadParts[4],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var expirySeconds))
        {
            return false;
        }

        try
        {
            artifactRef = payloadParts[3];
            expiresAt = DateTimeOffset.FromUnixTimeSeconds(expirySeconds);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            artifactRef = string.Empty;
            expiresAt = DateTimeOffset.MinValue;
            return false;
        }
    }

    private string ArtifactPath(string artifactRef) =>
        Path.Combine(_rootPath, artifactRef + ".png");

    private byte[] Sign(string payload)
    {
        using var hmac = new HMACSHA256(_key);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))
            .AsSpan(0, SignatureBytes)
            .ToArray();
    }

    private string HexSignature(string payload) =>
        Convert.ToHexString(Sign(payload)).ToLowerInvariant();

    private void EnsureConfigured()
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException(
                "IREGO report image signing key is not configured.");
        }
    }

    private static bool IsValidArtifactRef(string artifactRef) =>
        IReGoArtifactReference.IsValid(artifactRef);

    private static ReportImageAccessResult Result(
        ReportImageAccessStatus status,
        string artifactRef = "") =>
        new() { Status = status, ArtifactRef = artifactRef };

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return Convert.FromBase64String(padded);
    }
}
