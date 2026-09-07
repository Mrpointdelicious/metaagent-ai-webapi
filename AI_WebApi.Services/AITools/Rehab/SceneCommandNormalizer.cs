using System.Text;
using System.Text.RegularExpressions;
using NPinyin;

namespace AI_WebApi.Services.AITools.Rehab;

internal sealed record CommandSignature(
    string? Action, string Target, string CanonicalName, bool ActionFirst = true);

internal sealed record SceneActionToken(int Start, int End, string Action);

internal static class SceneCommandNormalizer
{
    // Directional actions deliberately remain separate. Pinyin never changes this table.
    private static readonly (string Word, string Action)[] Actions = new[]
    {
        ("打开", "open"), ("点开", "open"), ("开启", "open"),
        ("关闭", "close"), ("关掉", "close"), ("关上", "close"),
        ("前往", "navigate"), ("去", "navigate"),
        ("进入", "enter"), ("进去", "enter"),
        ("退出", "exit"), ("离开", "exit"), ("出去", "exit"),
        ("返回", "return"), ("后退", "return"), ("退回", "return"), ("回到", "return"),
        ("确认", "confirm"), ("确定", "confirm"), ("取消", "cancel"),
        ("结束", "stop"), ("终止", "stop"), ("停止", "stop"),
        ("开始", "start"), ("启动", "start"),
        ("切换", "switch"), ("转换", "switch"),
        ("显示", "show"), ("隐藏", "hide"),
        ("播放", "play"), ("暂停", "pause"), ("继续", "resume"),
        ("选择", "select"), ("选中", "select"), ("重置", "reset"),
        ("放大", "zoom_in"), ("缩小", "zoom_out")
    }.OrderByDescending(item => item.Item1.Length).ToArray();

    private static readonly string[] LeadingFillers =
        { "请帮我", "请带我", "麻烦你", "麻烦", "帮我", "带我", "请", "先", "把", "将" };

    private static readonly Regex UnsupportedContext = new(
        "不要|别|不用|无需|不许|禁止|不能|如果|假如|是否|怎么|如何|能否",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Compact(string value) =>
        string.Concat(value.Where(character => !char.IsWhiteSpace(character))).ToLowerInvariant();

    public static string Clean(string value)
    {
        var text = Compact(value);
        bool changed;
        do
        {
            changed = false;
            foreach (var filler in LeadingFillers)
            {
                if (!text.StartsWith(filler, StringComparison.Ordinal)) continue;
                var remainder = text[filler.Length..];
                if (filler is "请" or "先" &&
                    ActionAt(remainder, 0) == null &&
                    !remainder.StartsWith("把", StringComparison.Ordinal) &&
                    !remainder.StartsWith("将", StringComparison.Ordinal)) continue;
                if (filler is "把" or "将" &&
                    !Actions.Any(item => remainder.EndsWith(item.Word, StringComparison.Ordinal) ||
                        remainder.EndsWith(item.Word + "一下", StringComparison.Ordinal) ||
                        remainder.EndsWith(item.Word + "吧", StringComparison.Ordinal))) continue;
                text = text[filler.Length..];
                changed = true;
                break;
            }
        } while (changed);
        // Suffix particles are grammatical only immediately after an action. A target
        // such as 酒吧 or 先导面板 must retain all its characters.
        foreach (var filler in new[] { "一下", "吧" })
            if (Actions.Any(item => text.EndsWith(item.Word + filler, StringComparison.Ordinal)))
                text = text[..^filler.Length];
        return text;
    }

    public static CommandSignature Signature(string text)
    {
        var canonical = Compact(text);
        var expression = Clean(text);
        foreach (var (word, action) in Actions)
        {
            if (expression.StartsWith(word, StringComparison.Ordinal))
                return new CommandSignature(action, CleanTarget(expression[word.Length..]), canonical);
            if (expression.EndsWith(word, StringComparison.Ordinal))
                return new CommandSignature(action, CleanTarget(expression[..^word.Length]), canonical, false);
        }
        return new CommandSignature(null, expression, canonical);
    }

    private static string CleanTarget(string target) =>
        target.StartsWith("一下", StringComparison.Ordinal) ? target[2..] : target;

    public static SceneActionToken? ActionAt(string text, int start)
    {
        foreach (var (word, action) in Actions)
            if (text.AsSpan(start).StartsWith(word, StringComparison.Ordinal))
                return new SceneActionToken(start, start + word.Length, action);
        return null;
    }

    public static bool HasUnsupportedContext(string text, Func<int, int, bool> isCatalogTarget) =>
        UnsupportedContext.Matches(text).Cast<Match>()
            .Any(match => !isCatalogTarget(match.Index, match.Index + match.Length));

    public static string TargetPinyin(string target)
    {
        try
        {
            var output = new StringBuilder();
            var pinyin = Pinyin.GetPinyin(target).ToLowerInvariant().Normalize(NormalizationForm.FormD);
            foreach (var character in pinyin)
                if (!char.IsWhiteSpace(character) &&
                    char.GetUnicodeCategory(character) != System.Globalization.UnicodeCategory.NonSpacingMark)
                    output.Append(character);
            return output.ToString();
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return string.Empty;
        }
    }
}

/// <summary>Offsets stay tied to the original input even when whitespace is normalized.</summary>
internal sealed class SceneSourceText
{
    private readonly string _source;
    private readonly int[] _offsets;
    public string Text { get; }

    public SceneSourceText(string source)
    {
        _source = source;
        _offsets = Enumerable.Range(0, source.Length)
            .Where(index => !char.IsWhiteSpace(source[index])).ToArray();
        Text = new string(_offsets.Select(index => char.ToLowerInvariant(source[index])).ToArray());
    }

    public string Slice(int start, int end) =>
        _source[_offsets[start]..(_offsets[end - 1] + 1)];
}
