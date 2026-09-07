using System.Globalization;
using System.Text;
using System.Text.Json;
using AI_WebApi.Model.AITools;

namespace AI_WebApi.Services.AITools.Rehab;

internal sealed record SceneCommandCatalogEntry(
    string Prefix, string Id, string Name, string Intro, string IntentType)
{
    public CommandSignature Signature { get; } = SceneCommandNormalizer.Signature(Name);
    private string? _targetPinyin;
    public string TargetPinyin => _targetPinyin ??= SceneCommandNormalizer.TargetPinyin(Signature.Target);
    public bool IsDestination => IntentType is "telepor" or "scene_point" or "game_point";
}

internal sealed record SceneCommandCatalogSnapshot(
    string SpaceId, int Version, IReadOnlyList<SceneCommandCatalogEntry> Commands);

internal sealed class SceneCommandCatalog
{
    // These are the existing front-end array-index protocols, including Telepor's spelling.
    private static readonly (string Path, string Name, string? Intro, string Prefix, string Intent)[] Categories =
    {
        ("data_content->'TeleportConfig'->'ChannelsConfig'", "Name", "PointExplain", "[Telepor:]", "telepor"),
        ("data_content->'ScenePointCofig'->'Config'", "Name", "PointExplain", "[ScenePoint:]", "scene_point"),
        ("data_content->'CommandList'", "des", null, "[ClickPoint:]", "click_point"),
        ("data_content->'FastTaskConfig'->'GameRoomConfig'", "des", null, "[GamePoint:]", "game_point")
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly RehabToolsOptions _options;

    public SceneCommandCatalog(IHttpClientFactory httpClientFactory, RehabToolsOptions options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
    }

    public async Task<SceneCommandCatalogSnapshot> LoadAsync(
        string? spaceId, CancellationToken cancellationToken)
    {
        var scene = _options.Scene;
        var versionUrl = Required(scene.VersionUrl, "RehabTools:Scene:VersionUrl");
        var queryUrl = Required(scene.QueryUrl, "RehabTools:Scene:QueryUrl");
        var apiKey = Required(scene.ApiKey, "RehabTools:Scene:ApiKey");
        var apiToken = Required(scene.ApiToken, "RehabTools:Scene:ApiToken");
        var resolvedSpaceId = string.IsNullOrWhiteSpace(spaceId)
            ? Required(_options.DefaultSpaceId, "RehabTools:DefaultSpaceId")
            : spaceId.Trim();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(scene.RequestTimeoutSeconds, 3, 60)));

        using var versionDocument = await PostAsync(
            versionUrl, new { spaceId = resolvedSpaceId }, apiKey, apiToken, timeout.Token);
        if (!versionDocument.RootElement.TryGetProperty("data", out var data) ||
            !int.TryParse(data.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var version))
            throw new InvalidOperationException("Scene version upstream returned an invalid version.");

        var commands = new List<SceneCommandCatalogEntry>();
        foreach (var category in Categories)
        {
            var intro = category.Intro == null ? "" : $", 'intro', elem.value->>'{category.Intro}'";
            var sql = $"SELECT jsonb_agg(jsonb_build_object('id', elem.ordinality - 1, " +
                      $"'name', elem.value->>'{category.Name}'{intro})) AS result " +
                      $"FROM meta.meta_space_data, LATERAL jsonb_array_elements({category.Path}) " +
                      $"WITH ORDINALITY AS elem(value, ordinality) WHERE version_id = {version};";
            using var document = await PostAsync(queryUrl, new { sql }, apiKey, apiToken, timeout.Token);
            if (!document.RootElement.TryGetProperty("data", out var queryData) ||
                queryData.ValueKind != JsonValueKind.Object ||
                !queryData.TryGetProperty("result", out var result))
                throw new InvalidOperationException("Scene query upstream returned an invalid catalog.");

            // PostgreSQL jsonb_agg over zero rows returns null.
            if (result.ValueKind == JsonValueKind.Null) continue;
            if (result.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("Scene query upstream returned an invalid catalog.");

            foreach (var item in result.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    throw new InvalidOperationException("Scene catalog contains an invalid entry.");
                var name = Text(item, "name");
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (!item.TryGetProperty("id", out var id) ||
                    id.ValueKind is not (JsonValueKind.Number or JsonValueKind.String) ||
                    !long.TryParse(id.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out _))
                    throw new InvalidOperationException("Scene catalog contains an invalid command ID.");

                commands.Add(new SceneCommandCatalogEntry(
                    category.Prefix, id.ToString(), name, Text(item, "intro"), category.Intent));
            }
        }

        var unique = new List<SceneCommandCatalogEntry>();
        foreach (var group in commands.GroupBy(command => (command.Prefix, command.Id)))
        {
            var first = group.First();
            if (group.Any(command => command.Name != first.Name || command.Intro != first.Intro))
                throw new InvalidOperationException("Scene catalog contains conflicting command IDs.");
            unique.Add(first);
        }
        return new SceneCommandCatalogSnapshot(resolvedSpaceId, version, unique);
    }

    private async Task<JsonDocument> PostAsync(
        string url, object payload, string apiKey, string apiToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("apikey", apiKey);
        request.Headers.Add("apitoken", apiToken);
        using var response = await _httpClientFactory.CreateClient().SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Scene upstream returned HTTP {(int)response.StatusCode}.");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    private static string Text(JsonElement item, string property) =>
        item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static string Required(string? value, string name) =>
        !string.IsNullOrWhiteSpace(value) ? value.Trim()
            : throw new InvalidOperationException($"Required configuration {name} is missing.");
}
