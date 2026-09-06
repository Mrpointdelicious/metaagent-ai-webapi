namespace AI_WebApi.Repository.AITools;

/// <summary>
/// Temporary read-only bridge between project_cpoy and robotdb.
/// Returns at most two candidate IDs because the caller only needs to
/// distinguish a unique match from zero or multiple matches.
/// </summary>
public interface IPatientIdentityMappingRepository
{
    Task<IReadOnlyList<int>> FindRobotPatientCandidatesAsync(
        int projectPatientId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PatientIdentityMappingCandidate>>
        FindRobotPatientCandidatesByPhoneAsync(
            string phone,
            CancellationToken cancellationToken = default);
}

public sealed record PatientIdentityMappingCandidate(
    int ProjectPatientId,
    int RobotPatientId);
