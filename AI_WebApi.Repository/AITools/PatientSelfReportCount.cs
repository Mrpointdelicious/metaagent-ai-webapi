using AI_WebApi.Model.Entities;
using AI_WebApi.Repository.UnitOfWorks;

namespace AI_WebApi.Repository.AITools;

public interface IPatientSelfReportCount
{
    Task<int> CountAsync(int projectUserId, int businessId, CancellationToken ct);
}

public sealed class PatientSelfReportCount : IPatientSelfReportCount
{
    private readonly IUnitOfWorkManage _unit;
    public PatientSelfReportCount(IUnitOfWorkManage unit) => _unit = unit;

    public Task<int> CountAsync(int projectUserId, int businessId, CancellationToken ct) =>
        _unit.GetDbClient().GetConnectionScope("project_main").Queryable<RehabReportEntity>()
            .InnerJoin<RehabPlanEntity>((report, plan) => report.PlanId == plan.Id)
            .Where((report, plan) => plan.UserId == projectUserId && plan.BusinessId == businessId)
            .CountAsync(ct);
}
