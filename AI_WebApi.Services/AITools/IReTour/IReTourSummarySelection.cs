using AI_WebApi.Model.Domain.IReTour;
using AI_WebApi.Model.Entities;

namespace AI_WebApi.Services.AITools.IReTour;

public static class IReTourSummarySelection
{
    public static IReTourSummaryReportEntity? Verified(IReTourEvaluationResult evaluation, IReTourEvaluatedSession session)
    {
        if (session.ResultState != IReTourResultStates.SummaryAvailable ||
            !evaluation.SummaryReportsByWalk.TryGetValue(session.WalkId, out var rows) ||
            rows.Count == 0 || rows.Any(x => x.ItemId != session.ItemId || x.PlanId != session.WalkId) ||
            rows.Select(x => x.Details).Distinct(StringComparer.Ordinal).Count() != 1) return null;
        return rows.OrderBy(x => x.Id).First();
    }
}
