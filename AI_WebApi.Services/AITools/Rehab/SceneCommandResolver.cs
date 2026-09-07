using System.Text.RegularExpressions;
using AI_WebApi.Model.AITools;
using AI_WebApi.Model.DTOs.AITools;

namespace AI_WebApi.Services.AITools.Rehab;

internal sealed class SceneCommandResolver
{
    private static readonly Regex Separators = new(
        "[,，;；。.!！?？、]+|接下来|然后|接着|并且|同时|还有|以及|之后|最后|再次|再|并|和",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly decimal _minimumScore;
    private readonly decimal _minimumMargin;

    public SceneCommandResolver(SceneCommandResolverOptions options)
    {
        if (options.MinimumScore is < 0 or > 1 || options.MinimumMargin is <= 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(options),
                "Scene resolver requires MinimumScore in [0,1] and MinimumMargin in (0,1].");
        _minimumScore = options.MinimumScore;
        _minimumMargin = options.MinimumMargin;
    }

    public IReadOnlyList<SceneCommandResultDto> Resolve(
        string input, IReadOnlyList<SceneCommandCatalogEntry> catalog, CancellationToken cancellationToken)
    {
        var source = new SceneSourceText(input);
        var targets = Occurrences(source.Text, catalog.Select(item => item.Signature.Target), cancellationToken);
        var names = Occurrences(source.Text, catalog.Select(item => item.Signature.CanonicalName), cancellationToken);
        var protectedRanges = targets.Concat(names).ToArray();
        var output = new List<SceneCommandResultDto>();
        foreach (var clause in Clauses(source.Text, protectedRanges))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = source.Text[clause.Start..clause.End];
            if (SceneCommandNormalizer.Clean(text).Length == 0) continue;
            if (SceneCommandNormalizer.HasUnsupportedContext(text, (start, end) =>
                targets.Any(range => range.Start <= clause.Start + start && range.End >= clause.Start + end)))
            {
                output.Add(new SceneCommandResultDto
                {
                    SourceText = source.Slice(clause.Start, clause.End),
                    Message = "此片段包含否定、条件或询问，请明确要执行的动作"
                });
                continue;
            }

            var whole = Match(text, catalog);
            // A complete catalog command is indivisible, even if its name contains verbs/connectors.
            if (whole.Level <= 2)
            {
                whole.Result.SourceText = source.Slice(clause.Start, clause.End);
                output.Add(whole.Result);
                continue;
            }

            var actions = new List<SceneActionToken>();
            for (var index = clause.Start; index < clause.End; index++)
            {
                var action = SceneCommandNormalizer.ActionAt(source.Text, index);
                if (action == null || action.End > clause.End ||
                    targets.Any(range => range.Start <= action.Start && range.End >= action.End)) continue;
                actions.Add(action);
                index = action.End - 1;
            }

            var catalogPlan = CatalogSpans(source.Text, clause, protectedRanges, catalog, cancellationToken);
            if (catalogPlan?.Ambiguous == true)
            {
                var candidates = catalogPlan.Alternatives.SelectMany(span =>
                {
                    var result = Match(source.Text[span.Start..span.End], catalog).Result;
                    return result.Status == "matched" ? new[] { new SceneCommandCandidateDto
                    {
                        Name = result.MatchName!, IntentType = result.IntentType!, Score = result.Score!.Value
                    }} : result.Candidates;
                }).GroupBy(candidate => (candidate.Name, candidate.IntentType)).Select(group => group.First()).ToArray();
                output.Add(new SceneCommandResultDto
                {
                    Status = "ambiguous", SourceText = source.Slice(clause.Start, clause.End),
                    Message = "指令片段边界不明确，请用连接词明确各个动作", Candidates = candidates
                });
                continue;
            }
            var spans = catalogPlan?.Spans ?? (actions.Count > 1
                    ? Partition(source.Text, clause, actions, targets, catalog, cancellationToken)
                    : new[] { clause });
            foreach (var span in spans)
            {
                var match = Match(source.Text[span.Start..span.End], catalog);
                match.Result.SourceText = source.Slice(span.Start, span.End);
                output.Add(match.Result);
            }
        }
        for (var index = 0; index < output.Count; index++) output[index].Order = index + 1;
        return output;
    }

    private static IReadOnlyList<TextRange> Occurrences(
        string text, IEnumerable<string> phrases, CancellationToken cancellationToken)
    {
        var ranges = new List<TextRange>();
        foreach (var phrase in phrases.Where(value => value.Length > 0).Distinct(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var start = 0;
            while (start < text.Length)
            {
                start = text.IndexOf(phrase, start, StringComparison.Ordinal);
                if (start < 0) break;
                ranges.Add(new TextRange(start, start + phrase.Length));
                start++;
            }
        }
        return ranges;
    }

    private static IEnumerable<TextRange> Clauses(string text, IReadOnlyList<TextRange> protectedRanges)
    {
        var start = 0;
        foreach (System.Text.RegularExpressions.Match separator in Separators.Matches(text))
        {
            var end = separator.Index + separator.Length;
            if (protectedRanges.Any(range => range.Start <= separator.Index && range.End >= end)) continue;
            if (separator.Index > start) yield return new TextRange(start, separator.Index);
            start = end;
        }
        if (start < text.Length) yield return new TextRange(start, text.Length);
    }

    private IReadOnlyList<TextRange> Partition(
        string text, TextRange clause, IReadOnlyList<SceneActionToken> actions,
        IReadOnlyList<TextRange> targets, IReadOnlyList<SceneCommandCatalogEntry> catalog,
        CancellationToken cancellationToken)
    {
        // Every action owns exactly one span. Cuts come from action edges and catalog targets,
        // so both "打开地图关闭对话" and "地图打开对话关闭" retain all their original text.
        var states = new Dictionary<int, PartitionPath>
        {
            [clause.Start] = new(0, 0, 0, Array.Empty<TextRange>())
        };
        for (var index = 0; index < actions.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var action = actions[index];
            var cuts = index == actions.Count - 1 ? new[] { clause.End }
                : targets.SelectMany(range => new[] { range.Start, range.End })
                    .Append(action.End).Append(actions[index + 1].Start)
                    .Where(cut => cut >= action.End && cut <= actions[index + 1].Start)
                    .Distinct().OrderByDescending(cut => cut).ToArray();
            var next = new Dictionary<int, PartitionPath>();
            foreach (var cut in cuts)
            foreach (var (start, previous) in states)
            {
                var phrase = text[start..cut];
                var signature = SceneCommandNormalizer.Signature(phrase);
                var match = Match(phrase, catalog);
                var formed = signature.Action == action.Action &&
                             (signature.Target.Length > 0 || match.Level <= 2) ? 1 : 0;
                var candidate = new PartitionPath(
                    previous.Formed + formed,
                    previous.Evidence + (match.Level <= 5 ? 6 - match.Level : 0),
                    previous.Score + match.BestScore,
                    previous.Spans.Append(new TextRange(start, cut)).ToArray());
                if (!next.TryGetValue(cut, out var current) || candidate.CompareTo(current) > 0)
                    next[cut] = candidate;
            }
            states = next;
        }
        return states[clause.End].Spans;
    }

    private CatalogPlan? CatalogSpans(
        string text, TextRange clause, IReadOnlyList<TextRange> anchors,
        IReadOnlyList<SceneCommandCatalogEntry> catalog, CancellationToken cancellationToken)
    {
        // Cover the entire clause with full catalog commands, including commands such as
        // 挥手 which have no predefined action alias. Uncovered text goes to the rule path.
        var cuts = anchors.SelectMany(range => new[] { range.Start, range.End })
            .Append(clause.Start).Append(clause.End)
            .Where(cut => cut >= clause.Start && cut <= clause.End).Distinct().OrderBy(cut => cut).ToArray();
        var paths = new Dictionary<int, CatalogPlan>
        {
            [clause.Start] = new(Array.Empty<TextRange>(), 0, 0, false, Array.Empty<TextRange>())
        };
        foreach (var end in cuts.Skip(1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var start in cuts.TakeWhile(cut => cut < end))
            {
                if (!paths.TryGetValue(start, out var previous)) continue;
                var match = Match(text[start..end], catalog);
                if (match.Level > 3) continue;
                var candidate = new CatalogPlan(previous.Spans.Append(new TextRange(start, end)).ToArray(),
                    previous.Evidence + 6 - match.Level, previous.Score + match.BestScore,
                    previous.Ambiguous, previous.Alternatives);
                if (!paths.TryGetValue(end, out var current) || candidate.CompareTo(current) > 0)
                    paths[end] = candidate;
                else if (candidate.CompareTo(current) == 0 && !candidate.Spans.SequenceEqual(current.Spans))
                    paths[end] = current with
                    {
                        Ambiguous = true,
                        Alternatives = current.Alternatives.Concat(candidate.Alternatives)
                            .Concat(current.Spans).Concat(candidate.Spans).Distinct().ToArray()
                    };
            }
        }
        return paths.TryGetValue(clause.End, out var complete) ? complete : null;
    }

    private SpanMatch Match(string text, IReadOnlyList<SceneCommandCatalogEntry> catalog)
    {
        var expression = SceneCommandNormalizer.Clean(text);
        var signature = SceneCommandNormalizer.Signature(text);
        var pinyin = new Lazy<string>(() => SceneCommandNormalizer.TargetPinyin(signature.Target));
        var candidates = catalog.Select(item => Rank(expression, signature, pinyin, item))
            .Where(item => item != null).Cast<RankedCommand>()
            .OrderBy(item => item.Level).ThenByDescending(item => item.Score)
            .ThenBy(item => item.Command.Name, StringComparer.Ordinal)
            .ThenBy(item => item.Command.IntentType, StringComparer.Ordinal).ToArray();
        if (candidates.Length == 0)
        {
            var missingAction = signature.Action == null
                ? catalog.Where(item => item.Signature.Action != null && item.Signature.Target == signature.Target)
                    .Select(item => new SceneCommandCandidateDto
                    {
                        Name = item.Name, IntentType = item.IntentType, Score = .96m
                    }).ToArray()
                : Array.Empty<SceneCommandCandidateDto>();
            return new SpanMatch(new SceneCommandResultDto
            {
                Status = missingAction.Length > 1 ? "ambiguous" : "unmatched",
                Message = missingAction.Length > 0 ? "请明确要执行的动作" : "未匹配到当前空间指令",
                Candidates = missingAction
            }, 6, 0);
        }

        var best = candidates[0];
        if (best.Score < _minimumScore)
            return new SpanMatch(new SceneCommandResultDto { Message = "匹配置信度不足，请明确指令" },
                best.Level, best.Score);

        var contenders = candidates.Where(item => item.Level == best.Level &&
            best.Score - item.Score < _minimumMargin).ToArray();
        if (contenders.Length > 1)
            return new SpanMatch(new SceneCommandResultDto
            {
                Status = "ambiguous",
                Message = "无法唯一匹配当前空间指令",
                Candidates = contenders.Select(item => new SceneCommandCandidateDto
                {
                    Name = item.Command.Name, IntentType = item.Command.IntentType, Score = item.Score
                }).ToArray()
            }, best.Level, best.Score);

        return new SpanMatch(new SceneCommandResultDto
        {
            Status = "matched",
            Result = best.Command.Prefix + best.Command.Id,
            IntentType = best.Command.IntentType,
            MatchName = best.Command.Name,
            Score = best.Score
        }, best.Level, best.Score);
    }

    private static RankedCommand? Rank(
        string expression, CommandSignature user, Lazy<string> pinyin, SceneCommandCatalogEntry command)
    {
        var known = command.Signature;
        // An explicit action is a constraint, not a score adjustment.
        if (user.Action != known.Action && !(known.Action == null && command.IsDestination &&
            user.Action is "navigate" or "enter" or "return")) return null;

        if (user.CanonicalName == known.CanonicalName) return new RankedCommand(command, 1, 1m);
        if (expression == known.CanonicalName) return new RankedCommand(command, 2, .98m);
        if (user.Action == null) return null;
        if (user.Target == known.Target)
            return known.Action != null && user.ActionFirst == known.ActionFirst
                ? new RankedCommand(command, 2, .98m)
                : new RankedCommand(command, 3, .96m);
        if (user.Target.Length == 0 || known.Target.Length == 0) return null;

        // Only omission from a catalog target is allowed. Extra user qualifiers must not
        // collapse onto a shorter known target (e.g. 地图说明书 must not execute 地图).
        var coverage = (decimal)user.Target.Length / known.Target.Length;
        if (user.Target.Length >= 2 && coverage >= .5m && coverage < 1 &&
            (known.Target.Contains(user.Target, StringComparison.Ordinal) ||
             user.Target.Length >= 4 && known.Target.StartsWith(user.Target[..2], StringComparison.Ordinal) &&
             known.Target.EndsWith(user.Target[^2..], StringComparison.Ordinal) && IsSubsequence(user.Target, known.Target)))
            return new RankedCommand(command, 4, .82m + .10m * coverage);

        var left = pinyin.Value;
        var right = command.TargetPinyin;
        if (left.Length >= 3 && left == right) return new RankedCommand(command, 5, .78m);
        if (left.Length >= 6 && right.Length > left.Length &&
            right.Contains(left, StringComparison.Ordinal))
        {
            var pinyinCoverage = (decimal)left.Length / right.Length;
            if (pinyinCoverage >= .75m)
                return new RankedCommand(command, 5, .70m + .08m * pinyinCoverage);
        }
        return null;
    }

    private static bool IsSubsequence(string shorter, string longer)
    {
        var index = 0;
        foreach (var character in longer)
            if (index < shorter.Length && shorter[index] == character) index++;
        return index == shorter.Length;
    }

    private readonly record struct TextRange(int Start, int End);
    private sealed record RankedCommand(SceneCommandCatalogEntry Command, int Level, decimal Score);
    private sealed record SpanMatch(SceneCommandResultDto Result, int Level, decimal BestScore);
    private sealed record CatalogPlan(IReadOnlyList<TextRange> Spans, int Evidence, decimal Score,
        bool Ambiguous, IReadOnlyList<TextRange> Alternatives)
    {
        public int CompareTo(CatalogPlan other)
        {
            var count = other.Spans.Count.CompareTo(Spans.Count);
            if (count != 0) return count;
            var evidence = Evidence.CompareTo(other.Evidence);
            return evidence != 0 ? evidence : Score.CompareTo(other.Score);
        }
    }
    private sealed record PartitionPath(int Formed, int Evidence, decimal Score, IReadOnlyList<TextRange> Spans)
    {
        public int CompareTo(PartitionPath other)
        {
            var formed = Formed.CompareTo(other.Formed);
            if (formed != 0) return formed;
            var evidence = Evidence.CompareTo(other.Evidence);
            return evidence != 0 ? evidence : Score.CompareTo(other.Score);
        }
    }
}
