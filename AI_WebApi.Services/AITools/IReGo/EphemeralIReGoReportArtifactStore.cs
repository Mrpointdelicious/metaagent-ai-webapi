using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.AITools;
using AI_WebApi.Model.DTOs.AITools;
using Microsoft.Extensions.Options;

namespace AI_WebApi.Services.AITools.IReGo;

/// <summary>
/// Bounded process-memory storage for report PNG bytes. No report image is
/// written to a database, mounted volume, object store, or application file.
/// </summary>
public sealed class EphemeralIReGoReportArtifactStore : IIReGoReportArtifactStore
{
    private const string TokenVersion = IReGoDataVersion.ImageTokenVersion;
    private const int SignatureBytes = 16;

    private readonly object _sync = new();
    private readonly Dictionary<string, CacheEntry> _entries =
        new(StringComparer.Ordinal);
    private readonly byte[] _key;
    private readonly TimeSpan _cacheTtl;
    private readonly int _maxImageBytes;
    private readonly long _maxCacheBytes;
    private long _cacheBytes;

    public EphemeralIReGoReportArtifactStore(IOptions<IReGoToolOptions> options)
    {
        var configured = options.Value.ReportImage;
        _key = Encoding.UTF8.GetBytes(configured.SigningKey?.Trim() ?? string.Empty);
        _cacheTtl = TimeSpan.FromSeconds(
            Math.Clamp(configured.CacheTtlSeconds, 60, 3600));
        _maxImageBytes = Math.Clamp(
            configured.MaxImageBytes,
            256 * 1024,
            20 * 1024 * 1024);
        _maxCacheBytes = Math.Clamp(
            (long)configured.MaxCacheBytes,
            _maxImageBytes,
            1024L * 1024L * 1024L);
    }

    public bool IsConfigured => _key.Length >= 32;
    public string StorageMode => "ephemeral_memory";

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
        if (!IsConfigured || !IsValidArtifactRef(artifactRef))
        {
            bytes = Array.Empty<byte>();
            return false;
        }
        lock (_sync)
        {
            RemoveExpired(DateTimeOffset.UtcNow);
            if (_entries.TryGetValue(artifactRef, out var entry))
            {
                entry.LastAccessedAt = DateTimeOffset.UtcNow;
                bytes = entry.Bytes;
                return true;
            }
        }

        bytes = Array.Empty<byte>();
        return false;
    }

    public void Store(string artifactRef, byte[] bytes)
    {
        EnsureConfigured();
        if (!IsValidArtifactRef(artifactRef))
            throw new InvalidOperationException("Invalid IREGO artifact reference.");
        if (bytes.Length == 0 || bytes.Length > _maxImageBytes)
        {
            throw new InvalidOperationException(
                "Rendered report image exceeds the configured in-memory limit.");
        }

        lock (_sync)
        {
            var now = DateTimeOffset.UtcNow;
            RemoveExpired(now);
            if (_entries.ContainsKey(artifactRef))
            {
                return;
            }

            while (_cacheBytes + bytes.Length > _maxCacheBytes &&
                   _entries.Count > 0)
            {
                var oldest = _entries.MinBy(x => x.Value.LastAccessedAt);
                Remove(oldest.Key);
            }

            _entries[artifactRef] = new CacheEntry
            {
                Bytes = bytes,
                ExpiresAt = now.Add(_cacheTtl),
                LastAccessedAt = now
            };
            _cacheBytes += bytes.Length;
        }
    }

    public string IssueAccessToken(string artifactRef, DateTimeOffset expiresAt)
    {
        EnsureConfigured();
        if (!IsValidArtifactRef(artifactRef))
            throw new InvalidOperationException("Invalid IREGO artifact reference.");
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
        if (!IsConfigured || string.IsNullOrWhiteSpace(token))
        {
            return Result(ReportImageAccessStatus.Invalid);
        }

        var parts = token.Split('.');
        if (parts.Length != 3 || parts[0] != TokenVersion)
        {
            return Result(ReportImageAccessStatus.Invalid);
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
            return Result(ReportImageAccessStatus.Invalid);
        }

        var payload = Encoding.UTF8.GetString(payloadBytes);
        var expectedSignature = Sign(payload);
        if (suppliedSignature.Length != expectedSignature.Length ||
            !CryptographicOperations.FixedTimeEquals(
                suppliedSignature,
                expectedSignature))
        {
            return Result(ReportImageAccessStatus.Invalid);
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
            return Result(ReportImageAccessStatus.Invalid);
        }

        DateTimeOffset expiresAt;
        try { expiresAt = DateTimeOffset.FromUnixTimeSeconds(expirySeconds); }
        catch (ArgumentOutOfRangeException) { return Result(ReportImageAccessStatus.Invalid); }
        if (DateTimeOffset.UtcNow >= expiresAt)
        {
            return Result(ReportImageAccessStatus.Expired, payloadParts[3]);
        }

        return TryGet(payloadParts[3], out var bytes)
            ? new ReportImageAccessResult
            {
                Status = ReportImageAccessStatus.Available,
                ArtifactRef = payloadParts[3],
                Bytes = bytes
            }
            : Result(ReportImageAccessStatus.Missing, payloadParts[3]);
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

    private void RemoveExpired(DateTimeOffset now)
    {
        foreach (var key in _entries
                     .Where(x => x.Value.ExpiresAt <= now)
                     .Select(x => x.Key)
                     .ToArray())
        {
            Remove(key);
        }
    }

    private void Remove(string key)
    {
        if (_entries.Remove(key, out var entry))
        {
            _cacheBytes -= entry.Bytes.Length;
        }
    }

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

    private sealed class CacheEntry
    {
        public byte[] Bytes { get; init; } = Array.Empty<byte>();
        public DateTimeOffset ExpiresAt { get; init; }
        public DateTimeOffset LastAccessedAt { get; set; }
    }
}
