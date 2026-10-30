using System.ComponentModel.DataAnnotations;

namespace AI_WebApi.Model.DTOs.AITools;

public sealed class SearchDoctorsToolRequestDto
{
    public string SpaceId { get; set; } = string.Empty;
}

public sealed class SearchDoctorsToolResponseDto
{
    public string Result { get; set; } = string.Empty;
    public string DoctorNames { get; set; } = string.Empty;
    public string DoctorIds { get; set; } = "0";
    public string DoctorPhones { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public int Count { get; set; }
}

public sealed class SceneOperationToolRequestDto
{
    public string SpaceId { get; set; } = string.Empty;

    [Required]
    public string Target { get; set; } = string.Empty;

    /// <summary>Optional caller snapshot; mismatches return HTTP 409 without actions.</summary>
    [Range(0, int.MaxValue)]
    public int? ExpectedSceneVersion { get; set; }
}

public sealed class SceneOperationToolResponseDto
{
    public string SpaceId { get; set; } = string.Empty;
    public int Version { get; set; }
    public IReadOnlyList<SceneCommandResultDto> Commands { get; set; } = Array.Empty<SceneCommandResultDto>();

    // Preserve the old single-command wire fields. Never expose a partial multi-command result.
    private SceneCommandResultDto? SingleMatch =>
        Commands.Count == 1 && Commands[0].Status == "matched" ? Commands[0] : null;
    public string Result => SingleMatch?.Result ?? string.Empty;
    public string IntentType => SingleMatch?.IntentType ?? "unknown";
    public string MatchName => SingleMatch?.MatchName ?? string.Empty;
    public decimal Score => SingleMatch?.Score ?? 0;
}

public sealed class SceneCommandResultDto
{
    public int Order { get; set; }
    public string SourceText { get; set; } = string.Empty;
    /// <summary>matched, ambiguous or unmatched. Only matched may contain an executable Result.</summary>
    public string Status { get; set; } = "unmatched";
    public string? Result { get; set; }
    public string? IntentType { get; set; }
    public string? MatchName { get; set; }
    public decimal? Score { get; set; }
    public string? Message { get; set; }
    public IReadOnlyList<SceneCommandCandidateDto> Candidates { get; set; } = Array.Empty<SceneCommandCandidateDto>();
}

public sealed class SceneCommandCandidateDto
{
    public string Name { get; set; } = string.Empty;
    public string IntentType { get; set; } = string.Empty;
    public decimal Score { get; set; }
}
