using System.Text;
using System.Text.Json;
using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.AITools;
using AI_WebApi.Model.DTOs.AITools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NPinyin;

namespace AI_WebApi.Services.AITools.Rehab;

public sealed class SceneNavigationService : ISceneNavigationService
{
    private static readonly string[] SqlTemplates =
    {
        "SELECT jsonb_agg(jsonb_build_object('id', elem.id, 'name', elem.elem->>'Name', 'intro', elem.elem->>'PointExplain')) AS result FROM meta.meta_space_data, LATERAL (SELECT e.*, row_number() OVER () - 1 AS id FROM jsonb_array_elements(data_content->'TeleportConfig'->'ChannelsConfig') WITH ORDINALITY e(elem)) AS elem WHERE version_id = {0};",
        "SELECT jsonb_agg(jsonb_build_object('id', elem.id, 'name', elem.elem->>'Name', 'intro', elem.elem->>'PointExplain')) AS result FROM meta.meta_space_data, LATERAL (SELECT e.*, row_number() OVER () - 1 AS id FROM jsonb_array_elements(data_content->'ScenePointCofig'->'Config') WITH ORDINALITY e(elem)) AS elem WHERE version_id = {0};",
        "SELECT jsonb_agg(jsonb_build_object('id', elem.id, 'name', elem.elem->>'des')) AS result FROM meta.meta_space_data, LATERAL (SELECT e.*, row_number() OVER () - 1 AS id FROM jsonb_array_elements(data_content->'CommandList') WITH ORDINALITY e(elem)) AS elem WHERE version_id = {0};",
        "SELECT jsonb_agg(jsonb_build_object('id', elem.id, 'name', elem.elem->>'des')) AS result FROM meta.meta_space_data, LATERAL (SELECT e.*, row_number() OVER () - 1 AS id FROM jsonb_array_elements(data_content->'FastTaskConfig'->'GameRoomConfig') WITH ORDINALITY e(elem)) AS elem WHERE version_id = {0};"
    };

    private static readonly string[] ResultPrefixes =
        { "[Telepor:]", "[ScenePoint:]", "[ClickPoint:]", "[GamePoint:]" };

    private static readonly string[] PrefixWords =
        { "打开", "关闭", "去", "前往", "进入", "回到", "退回", "取消", "确认", "带我去", "带我", "帮我" };

    private static readonly (string From, string To)[] Synonyms =
    {
        ("点开", "打开"), ("开启", "打开"),
        ("关掉", "关闭"), ("关上", "关闭"), ("退出", "关闭"),
        ("退出", "结束"), ("终止", "结束"), ("停止", "结束"),
        ("转换", "切换"), ("换", "切换"),
        ("返回", "退回"), ("后退", "退回"), ("进去", "进入")
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly RehabToolsOptions _options;
    private readonly ILogger<SceneNavigationService> _logger;

    public SceneNavigationService(
        IHttpClientFactory httpClientFactory,
        IOptions<RehabToolsOptions> options,
        ILogger<SceneNavigationService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SceneOperationToolResponseDto> NavigateAsync(
        string target,
        string? spaceId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            throw new ArgumentException("Target is required.", nameof(target));
        }
        var scene = _options.Scene;
        var versionUrl = Required(scene.VersionUrl, "RehabTools:Scene:VersionUrl");
        var queryUrl = Required(scene.QueryUrl, "RehabTools:Scene:QueryUrl");
        var apiKey = Required(scene.ApiKey, "RehabTools:Scene:ApiKey");
        var apiToken = Required(scene.ApiToken, "RehabTools:Scene:ApiToken");
        var resolvedSpaceId = string.IsNullOrWhiteSpace(spaceId)
            ? Required(_options.DefaultSpaceId, "RehabTools:DefaultSpaceId")
            : spaceId.Trim();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(
            Math.Clamp(scene.RequestTimeoutSeconds, 3, 60)));

        var version = await ResolveVersionAsync(
            versionUrl,
            resolvedSpaceId,
            apiKey,
            apiToken,
            timeout.Token);
        var points = new List<ScenePoint>();
        for (var index = 0; index < SqlTemplates.Length; index++)
        {
            try
            {
                points.AddRange(await QueryPointsAsync(
                    queryUrl,
                    string.Format(SqlTemplates[index], version),
                    ResultPrefixes[index],
                    apiKey,
                    apiToken,
                    timeout.Token));
            }
            catch (Exception exception)
                when (exception is not OperationCanceledException)
            {
                _logger.LogWarning(
                    exception,
                    "Scene point query failed. Category={Category}",
                    index);
            }
        }

        var best = points
            .Select(point => new
            {
                Point = point,
                Score = ScoreMatchBest(target, point.Name, point.Intro)
            })
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Point.Prefix, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Point.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        if (best == null)
        {
            return Empty();
        }
        return new SceneOperationToolResponseDto
        {
            Result = best.Point.Prefix + best.Point.Id,
            IntentType = best.Point.Prefix switch
            {
                "[Telepor:]" => "telepor",
                "[ScenePoint:]" => "scene_point",
                "[ClickPoint:]" => "click_point",
                "[GamePoint:]" => "game_point",
                _ => "unknown"
            },
            MatchName = best.Point.Name,
            Score = best.Score
        };
    }

    private async Task<int> ResolveVersionAsync(
        string url,
        string spaceId,
        string apiKey,
        string apiToken,
        CancellationToken cancellationToken)
    {
        using var request = JsonRequest(
            url,
            JsonSerializer.Serialize(new { spaceId }),
            apiKey,
            apiToken);
        using var response = await _httpClientFactory.CreateClient().SendAsync(
            request,
            cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Scene version upstream returned HTTP {(int)response.StatusCode}.");
        }
        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("data", out var data) ||
            !int.TryParse(data.ToString(), out var version))
        {
            throw new InvalidOperationException(
                "Scene version upstream returned an invalid version.");
        }
        return version;
    }

    private async Task<IReadOnlyList<ScenePoint>> QueryPointsAsync(
        string url,
        string sql,
        string prefix,
        string apiKey,
        string apiToken,
        CancellationToken cancellationToken)
    {
        using var request = JsonRequest(
            url,
            JsonSerializer.Serialize(new { sql }),
            apiKey,
            apiToken);
        using var response = await _httpClientFactory.CreateClient().SendAsync(
            request,
            cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Scene query upstream returned HTTP {(int)response.StatusCode}.");
        }
        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("result", out var result) ||
            result.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<ScenePoint>();
        }
        return result.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object)
            .Select(item => new ScenePoint(
                prefix,
                item.TryGetProperty("id", out var id) ? id.ToString() : string.Empty,
                Text(item, "name"),
                Text(item, "intro")))
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .ToArray();
    }

    private static HttpRequestMessage JsonRequest(
        string url,
        string json,
        string apiKey,
        string apiToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("apikey", apiKey);
        request.Headers.Add("apitoken", apiToken);
        return request;
    }

    private static decimal ScoreMatchBest(string target, string name, string intro) =>
        ExpandSynonyms(target)
            .Select(value => ScoreMatch(value, name, intro))
            .DefaultIfEmpty(0m)
            .Max();

    private static decimal ScoreMatch(string target, string name, string intro)
    {
        var candidate = target.Trim();
        var point = name.Trim();
        if (candidate.Length == 0 || point.Length == 0) return 0m;
        if (string.Equals(candidate, point, StringComparison.OrdinalIgnoreCase)) return 1m;
        if (point.Contains(candidate, StringComparison.OrdinalIgnoreCase)) return .9m;
        var stripped = StripPrefix(candidate);
        if (stripped != candidate)
        {
            if (string.Equals(stripped, point, StringComparison.OrdinalIgnoreCase)) return .85m;
            if (point.Contains(stripped, StringComparison.OrdinalIgnoreCase)) return .8m;
            if (stripped.Contains(point, StringComparison.OrdinalIgnoreCase)) return .6m;
        }
        if (candidate.Contains(point, StringComparison.OrdinalIgnoreCase)) return .5m;
        if (intro.Contains(candidate, StringComparison.OrdinalIgnoreCase)) return .4m;
        var pinyin = PinyinScore(candidate, point);
        return pinyin > 0 ? pinyin : stripped != candidate
            ? PinyinScore(stripped, point)
            : 0m;
    }

    private static IEnumerable<string> ExpandSynonyms(string target)
    {
        yield return target;
        foreach (var (from, to) in Synonyms)
        {
            if (target.StartsWith(from, StringComparison.Ordinal) &&
                target.Length > from.Length)
            {
                yield return to + target[from.Length..];
            }
        }
    }

    private static string StripPrefix(string input)
    {
        foreach (var word in PrefixWords)
        {
            if (input.StartsWith(word, StringComparison.Ordinal) &&
                input.Length > word.Length)
            {
                return input[word.Length..];
            }
        }
        return input;
    }

    private static decimal PinyinScore(string left, string right)
    {
        try
        {
            var normalizedLeft = NormalizePinyin(Pinyin.GetPinyin(left));
            var normalizedRight = NormalizePinyin(Pinyin.GetPinyin(right));
            if (normalizedLeft == normalizedRight) return .7m;
            return normalizedLeft.Length >= 3 &&
                   normalizedRight.Contains(normalizedLeft, StringComparison.Ordinal)
                ? .65m
                : 0m;
        }
        catch
        {
            return 0m;
        }
    }

    private static string NormalizePinyin(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var output = new StringBuilder();
        foreach (var character in decomposed)
        {
            if (character is >= 'a' and <= 'z') output.Append(character);
        }
        return output.ToString();
    }

    private static string Text(JsonElement item, string property) =>
        item.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? string.Empty
            : string.Empty;

    private static SceneOperationToolResponseDto Empty() => new()
    {
        MatchName = "未匹配到点位"
    };

    private static string Required(string? value, string configurationName) =>
        !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : throw new InvalidOperationException(
                $"Required configuration {configurationName} is missing.");

    private sealed record ScenePoint(
        string Prefix,
        string Id,
        string Name,
        string Intro);
}
