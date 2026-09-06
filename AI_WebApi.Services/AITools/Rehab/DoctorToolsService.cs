using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.AITools;
using AI_WebApi.Model.DTOs.AITools;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AI_WebApi.Services.AITools.Rehab;

public sealed class DoctorToolsService : IDoctorToolsService
{
    private const string TokenCacheKey = "rehab_tools_doctor_token";
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly RehabToolsOptions _options;
    private readonly ILogger<DoctorToolsService> _logger;

    static DoctorToolsService()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public DoctorToolsService(
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        IOptions<RehabToolsOptions> options,
        ILogger<DoctorToolsService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SearchDoctorsToolResponseDto> SearchDoctorsAsync(
        string? spaceId,
        CancellationToken cancellationToken = default)
    {
        var doctor = _options.Doctor;
        var queryUrl = Required(doctor.QueryUrl, "RehabTools:Doctor:QueryUrl");
        var resolvedSpaceId = ResolveSpaceId(spaceId);
        var token = await GetTokenAsync(cancellationToken);
        var separator = queryUrl.Contains('?') ? '&' : '?';
        var url = queryUrl + separator + "spaceId=" +
                  Uri.EscapeDataString(resolvedSpaceId);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(
            Math.Clamp(doctor.RequestTimeoutSeconds, 3, 60)));
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            token);
        using var response = await _httpClientFactory.CreateClient().SendAsync(
            request,
            HttpCompletionOption.ResponseContentRead,
            timeout.Token);
        var bytes = await response.Content.ReadAsByteArrayAsync(timeout.Token);
        var body = DecodeResponse(
            bytes,
            response.Content.Headers.ContentType?.CharSet);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Doctor upstream returned HTTP {(int)response.StatusCode}.");
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var message = GetString(root, "message").Trim();
        var data = root.TryGetProperty("data", out var dataElement) &&
                   dataElement.ValueKind == JsonValueKind.Array
            ? dataElement
            : default;
        if (message == "暂无在线医生" || data.ValueKind != JsonValueKind.Array)
        {
            return Empty();
        }

        var names = new List<string>();
        var ids = new List<string>();
        var phones = new List<string>();
        foreach (var item in data.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }
            AddString(item, "name", names);
            if (item.TryGetProperty("id", out var id) &&
                id.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
            {
                ids.Add(id.ToString());
            }
            AddString(item, "phone", phones);
        }

        var count = names.Count;
        var phonesText = string.Join(",", phones);
        return new SearchDoctorsToolResponseDto
        {
            Result = count switch
            {
                1 => "一个医生",
                >= 2 => "多个医生",
                _ => "无医生"
            },
            DoctorNames = string.Join("、", names),
            DoctorIds = ids.Count == 0 ? "0" : string.Join(",", ids),
            DoctorPhones = phonesText,
            Command = count == 1 && !string.IsNullOrWhiteSpace(phonesText)
                ? "[DoctorRecommend:]" + phonesText
                : string.Empty,
            Count = count
        };
    }

    private async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue<string>(TokenCacheKey, out var cached) &&
            !string.IsNullOrWhiteSpace(cached))
        {
            return cached;
        }

        var doctor = _options.Doctor;
        var loginUrl = Required(doctor.LoginUrl, "RehabTools:Doctor:LoginUrl");
        var phone = Required(doctor.LoginPhone, "RehabTools:Doctor:LoginPhone");
        var password = Required(
            doctor.LoginPassword,
            "RehabTools:Doctor:LoginPassword");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(
            Math.Clamp(doctor.RequestTimeoutSeconds, 3, 60)));
        using var content = JsonContent.Create(new
        {
            Phone = phone,
            Password = password
        });
        using var response = await _httpClientFactory.CreateClient().PostAsync(
            loginUrl,
            content,
            timeout.Token);
        var raw = await response.Content.ReadAsStringAsync(timeout.Token);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Doctor login upstream returned HTTP {(int)response.StatusCode}.");
        }
        var token = ExtractToken(raw);
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException(
                "Doctor login succeeded without a usable token.");
        }
        _cache.Set(
            TokenCacheKey,
            token,
            TimeSpan.FromMinutes(Math.Clamp(doctor.TokenTtlMinutes, 1, 1440)));
        _logger.LogInformation("Online doctor upstream token refreshed.");
        return token;
    }

    private string ResolveSpaceId(string? value)
    {
        var resolved = string.IsNullOrWhiteSpace(value)
            ? _options.DefaultSpaceId?.Trim()
            : value.Trim();
        return Required(resolved, "RehabTools:DefaultSpaceId");
    }

    private static SearchDoctorsToolResponseDto Empty() => new()
    {
        Result = "无医生",
        DoctorIds = "0"
    };

    private static void AddString(
        JsonElement item,
        string property,
        ICollection<string> output)
    {
        if (item.TryGetProperty(property, out var value) &&
            value.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(value.GetString()))
        {
            output.Add(value.GetString()!.Trim());
        }
    }

    private static string ExtractToken(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }
        try
        {
            using var document = JsonDocument.Parse(raw);
            foreach (var path in new[]
                     {
                         new[] { "token" },
                         new[] { "data", "token" },
                         new[] { "data", "data", "token" },
                         new[] { "body", "token" },
                         new[] { "body", "data", "token" },
                         new[] { "body", "data", "data", "token" }
                     })
            {
                var current = document.RootElement;
                var found = true;
                foreach (var segment in path)
                {
                    if (!current.TryGetProperty(segment, out current))
                    {
                        found = false;
                        break;
                    }
                }
                if (found && current.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(current.GetString()))
                {
                    return current.GetString()!.Trim();
                }
            }
        }
        catch (JsonException)
        {
            return string.Empty;
        }
        return string.Empty;
    }

    private static string DecodeResponse(byte[] bytes, string? charset)
    {
        if (!string.IsNullOrWhiteSpace(charset))
        {
            try
            {
                return Encoding.GetEncoding(charset.Trim('"')).GetString(bytes);
            }
            catch (ArgumentException)
            {
                // Continue with deterministic fallbacks.
            }
        }
        var utf8 = Encoding.UTF8.GetString(bytes);
        if (!utf8.Contains('�'))
        {
            return utf8;
        }
        try
        {
            return Encoding.GetEncoding("GBK").GetString(bytes);
        }
        catch (ArgumentException)
        {
            return utf8;
        }
    }

    private static string GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static string Required(string? value, string configurationName) =>
        !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : throw new InvalidOperationException(
                $"Required configuration {configurationName} is missing.");
}
