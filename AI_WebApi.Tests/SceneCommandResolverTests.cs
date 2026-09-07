using System.Net;
using System.Text;
using System.Text.Json;
using AI_WebApi.Model;
using AI_WebApi.Model.AITools;
using AI_WebApi.Model.DTOs.AITools;
using AI_WebApi.Services.AITools.Rehab;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace AI_WebApi.Tests;

public sealed class SceneCommandResolverTests
{
    [Theory]
    [InlineData("打开历史对话，然后关闭地图，再去康复大厅", true)]
    [InlineData("打开历史对话关闭地图去康复大厅", true)]
    [InlineData("帮我把历史对话打开地图关掉", false)]
    [InlineData("打开历史对话地图关掉", false)]
    [InlineData("历史对话打开关闭地图", false)]
    [InlineData("历史对话打开地图关闭", false)]
    [InlineData("请把历史对话点开，并且把地图关上", false)]
    [InlineData("打开 历史对话， 接着 关闭 地图", false)]
    public async Task Resolves_catalog_spans_in_original_order(string input, bool navigation)
    {
        using var fixture = Standard();
        var result = await fixture.Service.NavigateAsync(input, "space-7");

        Assert.Equal(navigation ? 3 : 2, result.Commands.Count);
        Assert.Equal("[ClickPoint:]13", result.Commands[0].Result);
        Assert.Equal("[ClickPoint:]22", result.Commands[1].Result);
        if (navigation) Assert.Equal("[Telepor:]5", result.Commands[2].Result);
        Assert.All(result.Commands, command => Assert.Equal("matched", command.Status));
        AssertSourceOrder(input, result.Commands);
        Assert.Equal(4, fixture.Sql.Count);
        Assert.Equal(new[] { "space-7" }, fixture.Spaces);
        Assert.All(fixture.Sql, sql =>
        {
            Assert.Contains("version_id = 321", sql);
            Assert.Contains("elem.ordinality - 1", sql);
            Assert.Contains("WITH ORDINALITY", sql);
        });
    }

    [Theory]
    [InlineData("关闭历史对话", "打开历史对话")]
    [InlineData("打开历史对话", "关闭历史对话")]
    [InlineData("进入训练区", "退出训练区")]
    [InlineData("退出训练区", "进入训练区")]
    [InlineData("确认训练", "取消训练")]
    [InlineData("取消训练", "确认训练")]
    [InlineData("退出训练", "结束训练")]
    [InlineData("退出地图", "关闭地图")]
    [InlineData("返回大厅", "退出大厅")]
    [InlineData("关闭历史对话", "历史对话")]
    public async Task Conflicting_action_never_becomes_a_candidate(string input, string name)
    {
        using var fixture = new CatalogFixture(new Row(2, 99, name));
        var result = await fixture.Service.NavigateAsync(input, null);
        AssertNotExecutable(Assert.Single(result.Commands), "unmatched");
        Assert.Empty(result.Commands[0].Candidates);
    }

    [Theory]
    [InlineData("点开历史对话", "打开历史对话", "关闭历史对话")]
    [InlineData("开启历史对话", "打开历史对话", "关闭历史对话")]
    [InlineData("关掉地图", "关闭地图", "打开地图")]
    [InlineData("关上地图", "关闭地图", "打开地图")]
    [InlineData("进去训练区", "进入训练区", "退出训练区")]
    [InlineData("离开训练区", "退出训练区", "进入训练区")]
    [InlineData("确定训练", "确认训练", "取消训练")]
    [InlineData("后退大厅", "返回大厅", "退出大厅")]
    [InlineData("终止训练", "结束训练", "退出训练")]
    public async Task Synonyms_preserve_direction(string input, string expected, string opposite)
    {
        using var fixture = new CatalogFixture(new Row(2, 17, expected), new Row(2, 1, opposite));
        var command = Assert.Single((await fixture.Service.NavigateAsync(input, null)).Commands);
        Assert.Equal("matched", command.Status);
        Assert.Equal("[ClickPoint:]17", command.Result);
        Assert.Equal(.98m, command.Score);
    }

    [Theory]
    [InlineData("打开历史对话，关闭训练面板，然后去康复大厅")]
    [InlineData("打开历史对话关闭训练面板去康复大厅")]
    public async Task Ambiguous_middle_command_is_preserved(string input)
    {
        using var fixture = Standard(new Row(2, 31, "关闭训练记录面板"), new Row(2, 32, "关闭训练详情面板"));
        var commands = (await fixture.Service.NavigateAsync(input, null)).Commands;
        Assert.Equal(new[] { "matched", "ambiguous", "matched" }, commands.Select(command => command.Status));
        AssertNotExecutable(commands[1], "ambiguous");
        Assert.Equal("关闭训练面板", commands[1].SourceText);
        Assert.Equal(2, commands[1].Candidates.Count);
        Assert.All(commands[1].Candidates, candidate => Assert.StartsWith("关闭训练", candidate.Name));
        AssertSourceOrder(input, commands);
    }

    [Theory]
    [InlineData("打开历史对话，关闭天气面板，然后去康复大厅")]
    [InlineData("打开历史对话关闭天气面板去康复大厅")]
    [InlineData("打开历史对话，跳舞，然后去康复大厅")]
    public async Task Unknown_middle_command_is_preserved(string input)
    {
        using var fixture = Standard();
        var commands = (await fixture.Service.NavigateAsync(input, null)).Commands;
        Assert.Equal(new[] { "matched", "unmatched", "matched" }, commands.Select(command => command.Status));
        AssertNotExecutable(commands[1], "unmatched");
        AssertSourceOrder(input, commands);
    }

    [Fact]
    public async Task Repeated_commands_are_not_deduplicated()
    {
        using var fixture = Standard();
        const string input = "打开历史对话然后关闭历史对话再打开历史对话";
        var commands = (await fixture.Service.NavigateAsync(input, null)).Commands;
        Assert.Equal(new[] { "[ClickPoint:]13", "[ClickPoint:]14", "[ClickPoint:]13" }, commands.Select(c => c.Result));
        AssertSourceOrder(input, commands);
    }

    [Fact]
    public async Task Canonical_command_outranks_synonym_and_partial_matches()
    {
        using var fixture = new CatalogFixture(
            new Row(2, 3, "点开地图"), new Row(2, 1, "打开地图"), new Row(2, 2, "点开地图设置"));
        var command = Assert.Single((await fixture.Service.NavigateAsync("点开地图", null)).Commands);
        Assert.Equal("[ClickPoint:]3", command.Result);
        Assert.Equal(1m, command.Score);
    }

    [Fact]
    public async Task Raw_canonical_name_outranks_a_name_reached_by_removing_polite_words()
    {
        using var fixture = new CatalogFixture(new Row(2, 3, "请打开地图"), new Row(2, 1, "打开地图"));
        var command = Assert.Single((await fixture.Service.NavigateAsync("请打开地图", null)).Commands);
        Assert.Equal("[ClickPoint:]3", command.Result);
        Assert.Equal(1m, command.Score);
    }

    [Fact]
    public async Task Exact_action_target_outranks_partial_and_pinyin()
    {
        using var fixture = new CatalogFixture(
            new Row(2, 9, "关闭训练报告"), new Row(2, 1, "关闭训练历史报告"), new Row(2, 2, "关闭训炼报告"));
        var command = Assert.Single((await fixture.Service.NavigateAsync("把训练报告关闭", null)).Commands);
        Assert.Equal("[ClickPoint:]9", command.Result);
        Assert.Equal(.96m, command.Score);
    }

    [Fact]
    public async Task Small_top_two_margin_is_ambiguous_and_is_configurable()
    {
        var rows = new[] { new Row(2, 9, "关闭训练历史报告"), new Row(2, 2, "关闭训练详细历史报告") };
        using var standard = new CatalogFixture(rows);
        var ambiguous = Assert.Single((await standard.Service.NavigateAsync("关闭训练报告", null)).Commands);
        AssertNotExecutable(ambiguous, "ambiguous");
        Assert.True(ambiguous.Candidates[0].Score > ambiguous.Candidates[1].Score);
        Assert.True(ambiguous.Candidates[0].Score - ambiguous.Candidates[1].Score < .05m);

        using var narrower = new CatalogFixture(new SceneCommandResolverOptions { MinimumMargin = .01m }, rows);
        var matched = Assert.Single((await narrower.Service.NavigateAsync("关闭训练报告", null)).Commands);
        Assert.Equal("[ClickPoint:]9", matched.Result);
    }

    [Theory]
    [InlineData("打开li shi dui hua")]
    [InlineData("打开LI SHI DUI HUA")]
    [InlineData("打开力史对话")]
    public async Task Pinyin_is_a_target_only_fallback(string input)
    {
        using var fixture = Standard();
        var command = Assert.Single((await fixture.Service.NavigateAsync(input, null)).Commands);
        Assert.Equal("[ClickPoint:]13", command.Result);
        Assert.Equal(.78m, command.Score);
    }

    [Theory]
    [InlineData("dakai历史对话")]
    [InlineData("关闭lishiduihua说明书")]
    [InlineData("打开地图说明书")]
    [InlineData("打开地图123")]
    [InlineData("打开史")]
    [InlineData("打开历话")]
    [InlineData("去康厅")]
    [InlineData("搜索地图")]
    public async Task Weak_or_unknown_expressions_do_not_execute(string input)
    {
        using var fixture = Standard();
        AssertNotExecutable(Assert.Single((await fixture.Service.NavigateAsync(input, null)).Commands), "unmatched");
    }

    [Theory]
    [InlineData(.78, "matched")]
    [InlineData(.79, "unmatched")]
    public async Task Minimum_score_boundary_is_configurable(double minimum, string status)
    {
        using var fixture = new CatalogFixture(
            new SceneCommandResolverOptions { MinimumScore = (decimal)minimum }, new Row(2, 13, "打开历史对话"));
        var command = Assert.Single((await fixture.Service.NavigateAsync("打开lishiduihua", null)).Commands);
        Assert.Equal(status, command.Status);
        if (status == "unmatched") AssertNotExecutable(command, status);
    }

    [Theory]
    [InlineData("打开确认取消面板然后关闭地图", "打开确认取消面板")]
    [InlineData("打开再次确认面板然后关闭地图", "打开再次确认面板")]
    [InlineData("打开康复和训练面板然后关闭地图", "打开康复和训练面板")]
    public async Task Catalog_target_verbs_and_connectors_do_not_split_commands(string input, string name)
    {
        using var fixture = Standard(new Row(2, 33, name));
        var commands = (await fixture.Service.NavigateAsync(input, null)).Commands;
        Assert.Equal(new[] { "[ClickPoint:]33", "[ClickPoint:]22" }, commands.Select(c => c.Result));
    }

    [Theory]
    [InlineData("打开地图挥手")]
    [InlineData("点开地图挥手")]
    public async Task Complete_catalog_commands_without_known_verbs_can_join_a_sequence(string input)
    {
        using var fixture = Standard(new Row(2, 50, "挥手"));
        var commands = (await fixture.Service.NavigateAsync(input, null)).Commands;
        Assert.Equal(new[] { "[ClickPoint:]21", "[ClickPoint:]50" }, commands.Select(c => c.Result));
    }

    [Theory]
    [InlineData("打开性别面板", "打开性别面板", "打开面板")]
    [InlineData("打开酒吧", "打开酒吧", "打开酒")]
    [InlineData("点开先导面板", "打开先导面板", "打开导面板")]
    public async Task Target_characters_are_not_removed_as_grammar(string input, string expected, string other)
    {
        using var fixture = new CatalogFixture(new Row(2, 99, expected), new Row(2, 1, other));
        var command = Assert.Single((await fixture.Service.NavigateAsync(input, null)).Commands);
        Assert.Equal("[ClickPoint:]99", command.Result);
    }

    [Fact]
    public async Task Pinyin_fallback_preserves_target_numbers()
    {
        using var fixture = new CatalogFixture(new Row(2, 8, "打开报告1"));
        var command = Assert.Single((await fixture.Service.NavigateAsync("打开报告2", null)).Commands);
        AssertNotExecutable(command, "unmatched");
    }

    [Fact]
    public async Task Equally_supported_catalog_boundaries_are_ambiguous()
    {
        using var fixture = new CatalogFixture(new Row(2, 1, "打开甲"), new Row(2, 2, "乙丙"),
            new Row(2, 3, "打开甲乙"), new Row(2, 4, "丙"));
        var command = Assert.Single((await fixture.Service.NavigateAsync("打开甲乙丙", null)).Commands);
        AssertNotExecutable(command, "ambiguous");
        Assert.Contains("边界", command.Message);
    }

    [Fact]
    public async Task Identical_names_with_distinct_ids_are_ambiguous()
    {
        using var fixture = new CatalogFixture(new Row(2, 8, "打开地图"), new Row(2, 2, "打开地图"));
        var command = Assert.Single((await fixture.Service.NavigateAsync("打开地图", null)).Commands);
        AssertNotExecutable(command, "ambiguous");
        Assert.Equal(2, command.Candidates.Count);
    }

    [Fact]
    public async Task Duplicate_rows_of_the_same_real_command_do_not_create_false_ambiguity()
    {
        using var fixture = new CatalogFixture(new Row(2, 8, "打开地图"), new Row(2, 8, "打开地图"));
        Assert.Equal("[ClickPoint:]8", Assert.Single((await fixture.Service.NavigateAsync("打开地图", null)).Commands).Result);
    }

    [Fact]
    public async Task Same_name_across_categories_is_ambiguous()
    {
        using var fixture = new CatalogFixture(new Row(0, 8, "康复大厅"), new Row(1, 8, "康复大厅"));
        var command = Assert.Single((await fixture.Service.NavigateAsync("去康复大厅", null)).Commands);
        AssertNotExecutable(command, "ambiguous");
        Assert.Equal(2, command.Candidates.Count);
    }

    [Fact]
    public async Task Target_without_an_action_does_not_choose_open_or_close()
    {
        using var fixture = Standard();
        var command = Assert.Single((await fixture.Service.NavigateAsync("历史对话", null)).Commands);
        AssertNotExecutable(command, "ambiguous");
        Assert.Equal(2, command.Candidates.Count);
    }

    [Theory]
    [InlineData("不要打开地图，然后关闭历史对话")]
    [InlineData("如果打开地图，然后关闭历史对话")]
    [InlineData("怎么打开地图，然后关闭历史对话")]
    public async Task Unsupported_context_does_not_execute_a_positive_substring(string input)
    {
        using var fixture = Standard();
        var commands = (await fixture.Service.NavigateAsync(input, null)).Commands;
        Assert.Equal(2, commands.Count);
        AssertNotExecutable(commands[0], "unmatched");
        Assert.Equal("[ClickPoint:]14", commands[1].Result);
    }

    [Fact]
    public async Task Bare_catalog_sequences_and_standalone_actions_are_supported()
    {
        using var fixture = Standard(new Row(1, 7, "训练广场"), new Row(2, 41, "确认"), new Row(2, 42, "取消"));
        var commands = (await fixture.Service.NavigateAsync("康复大厅训练广场，确认，然后取消", null)).Commands;
        Assert.Equal(new[] { "[Telepor:]5", "[ScenePoint:]7", "[ClickPoint:]41", "[ClickPoint:]42" },
            commands.Select(c => c.Result));
    }

    [Theory]
    [InlineData("，。；")]
    [InlineData("请帮我")]
    public async Task No_command_content_can_return_an_empty_list(string input)
    {
        using var fixture = Standard();
        Assert.Empty((await fixture.Service.NavigateAsync(input, null)).Commands);
    }

    [Fact]
    public async Task Current_space_and_version_are_queried_on_every_request_and_ids_are_preserved()
    {
        using var fixture = new CatalogFixture(new Row(0, 103, "大厅"), new Row(1, 207, "广场"),
            new Row(2, 0, "打开地图"), new Row(3, 401, "游戏室")) { IncrementVersion = true };
        fixture.QueryResponse = category => category == 0 && fixture.Spaces.Count > 1
            ? Json("{\"data\":{\"result\":[{\"id\":503,\"name\":\"大厅\"}]}}") : null;
        var first = await fixture.Service.NavigateAsync("去大厅，去广场，打开地图，进入游戏室", " ");
        var second = await fixture.Service.NavigateAsync("去大厅", " space-new ");
        Assert.Equal("default-space", first.SpaceId);
        Assert.Equal("space-new", second.SpaceId);
        Assert.Equal(321, first.Version);
        Assert.Equal(322, second.Version);
        Assert.Equal("[Telepor:]503", Assert.Single(second.Commands).Result);
        Assert.Equal(new[] { "default-space", "space-new" }, fixture.Spaces);
        Assert.All(fixture.Sql.Take(4), sql => Assert.Contains("version_id = 321", sql));
        Assert.All(fixture.Sql.Skip(4), sql => Assert.Contains("version_id = 322", sql));
        Assert.Equal(new[] { "[Telepor:]103", "[ScenePoint:]207", "[ClickPoint:]0", "[GamePoint:]401" },
            first.Commands.Select(command => command.Result));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    public async Task Empty_catalog_keeps_all_requested_actions_unmatched(string resultJson)
    {
        using var fixture = new CatalogFixture { QueryResponse = _ => Json("{\"data\":{\"result\":" + resultJson + "}}") };
        var commands = (await fixture.Service.NavigateAsync("打开地图，关闭历史对话", null)).Commands;
        Assert.Equal(2, commands.Count);
        Assert.All(commands, command => AssertNotExecutable(command, "unmatched"));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"data\":{}}")]
    [InlineData("{\"data\":{\"result\":\"invalid\"}}")]
    [InlineData("{\"data\":{\"result\":[{\"name\":\"打开地图\"}]}}")]
    [InlineData("{\"data\":{\"result\":[{\"id\":-1,\"name\":\"打开地图\"}]}}")]
    [InlineData("{\"data\":{\"result\":[{\"id\":\"[ClickPoint:]9\",\"name\":\"打开地图\"}]}}")]
    [InlineData("{\"data\":{\"result\":[false]}}")]
    public async Task Malformed_catalog_cannot_establish_uniqueness(string body)
    {
        using var fixture = Standard();
        fixture.QueryResponse = category => category == 3 ? Json(body) : null;
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.NavigateAsync("打开历史对话", null));
    }

    [Fact]
    public async Task Failed_category_does_not_return_matches_from_a_partial_catalog()
    {
        using var fixture = Standard();
        fixture.QueryResponse = category => category == 3 ? new HttpResponseMessage(HttpStatusCode.BadGateway) : null;
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Service.NavigateAsync("打开历史对话", null));
        Assert.Contains("HTTP 502", exception.Message);
    }

    [Theory]
    [InlineData("{\"data\":\"321 OR 1=1\"}")]
    [InlineData("{\"data\":null}")]
    public async Task Invalid_version_is_rejected_before_querying_the_catalog(string body)
    {
        using var fixture = new CatalogFixture { VersionResponse = body };
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.NavigateAsync("打开地图", null));
        Assert.Empty(fixture.Sql);
    }

    [Fact]
    public async Task Cancellation_is_propagated()
    {
        using var fixture = Standard();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Service.NavigateAsync("打开地图", null, cancellation.Token));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-.1, .05)]
    [InlineData(1.1, .05)]
    [InlineData(.75, 1.1)]
    public void Invalid_thresholds_are_rejected(double score, double margin)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CatalogFixture(
            new SceneCommandResolverOptions { MinimumScore = (decimal)score, MinimumMargin = (decimal)margin }));
    }

    [Fact]
    public async Task Json_contract_contains_ordered_results_and_no_executable_ambiguous_candidates()
    {
        using var fixture = Standard(new Row(2, 31, "关闭训练记录面板"), new Row(2, 32, "关闭训练详情面板"));
        var response = await fixture.Service.NavigateAsync("打开历史对话，关闭训练面板，去火星", "space-7");
        // ASP.NET's existing Newtonsoft camel-case contract; no serializer changes are needed.
        var json = JObject.Parse(JsonConvert.SerializeObject(ApiResponse<SceneOperationToolResponseDto>.Success(response),
            new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() }));
        Assert.Null(json["data"]!["result"]);
        Assert.Equal("space-7", json["data"]!["spaceId"]!.Value<string>());
        var commands = (JArray)json["data"]!["commands"]!;
        Assert.Equal(3, commands.Count);
        Assert.Equal("matched", commands[0]["status"]!.Value<string>());
        Assert.Equal(JTokenType.Null, commands[1]["result"]!.Type);
        Assert.Equal(JTokenType.Null, commands[1]["score"]!.Type);
        Assert.Equal(JTokenType.Null, commands[2]["result"]!.Type);
        Assert.All((JArray)commands[1]["candidates"]!, candidate =>
        {
            Assert.Null(candidate["id"]);
            Assert.Null(candidate["result"]);
            Assert.NotNull(candidate["name"]);
        });
    }

    private static void AssertNotExecutable(SceneCommandResultDto command, string status)
    {
        Assert.Equal(status, command.Status);
        Assert.Null(command.Result);
        Assert.Null(command.IntentType);
        Assert.Null(command.MatchName);
        Assert.Null(command.Score);
        Assert.False(string.IsNullOrWhiteSpace(command.Message));
    }

    private static void AssertSourceOrder(string input, IReadOnlyList<SceneCommandResultDto> commands)
    {
        var cursor = 0;
        for (var index = 0; index < commands.Count; index++)
        {
            Assert.Equal(index + 1, commands[index].Order);
            var start = input.IndexOf(commands[index].SourceText, cursor, StringComparison.Ordinal);
            Assert.True(start >= cursor, commands[index].SourceText);
            cursor = start + commands[index].SourceText.Length;
        }
    }

    private static CatalogFixture Standard(params Row[] extra) => new(new[]
    {
        new Row(0, 5, "康复大厅"), new Row(2, 13, "打开历史对话"), new Row(2, 14, "关闭历史对话"),
        new Row(2, 21, "打开地图"), new Row(2, 22, "关闭地图")
    }.Concat(extra).ToArray());

    private sealed record Row(int Category, int Id, string Name);

    private sealed class CatalogFixture : HttpMessageHandler, IHttpClientFactory
    {
        private readonly Row[] _rows;
        private readonly HttpClient _client;
        public SceneNavigationService Service { get; }
        public List<string> Sql { get; } = new();
        public List<string> Spaces { get; } = new();
        public bool IncrementVersion { get; init; }
        public string? VersionResponse { get; init; }
        public Func<int, HttpResponseMessage?>? QueryResponse { get; set; }

        public CatalogFixture(params Row[] rows) : this(new SceneCommandResolverOptions(), rows) { }

        public CatalogFixture(SceneCommandResolverOptions resolver, params Row[] rows)
        {
            _rows = rows;
            _client = new HttpClient(this, disposeHandler: false);
            Service = new SceneNavigationService(this, Options.Create(new RehabToolsOptions
            {
                DefaultSpaceId = "default-space",
                Scene = new SceneNavigationOptions
                {
                    VersionUrl = "http://catalog.test/version", QueryUrl = "http://catalog.test/query",
                    ApiKey = "test-key", ApiToken = "test-token", Resolver = resolver
                }
            }), NullLogger<SceneNavigationService>.Instance);
        }

        public HttpClient CreateClient(string name) => _client;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("test-key", request.Headers.GetValues("apikey").Single());
            Assert.Equal("test-token", request.Headers.GetValues("apitoken").Single());
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            if (request.RequestUri!.AbsolutePath == "/version")
            {
                Spaces.Add(document.RootElement.GetProperty("spaceId").GetString()!);
                return Json(VersionResponse ?? JsonSerializer.Serialize(new { data = 321 + (IncrementVersion ? Spaces.Count - 1 : 0) }));
            }
            Sql.Add(document.RootElement.GetProperty("sql").GetString()!);
            var category = (Sql.Count - 1) % 4;
            return QueryResponse?.Invoke(category) ?? Json(JsonSerializer.Serialize(new
            {
                data = new { result = _rows.Where(row => row.Category == category).Select(row => new { id = row.Id, name = row.Name }) }
            }));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _client.Dispose();
            base.Dispose(disposing);
        }
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };
}
