using SqlSugar;
using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.Entities;
using AI_WebApi.Repository.BASE;

namespace AI_WebApi.Repository.AITools;

/// <summary>
/// Reads the IREGO table family in project using current dbuser identities.
/// </summary>
public sealed class IReGoDataRepository : IIReGoDataRepository
{
    private readonly IBaseRepository<RobotDbMainUserEntity> _users;
    private readonly IBaseRepository<RobotDbMainRehabPlanEntity> _plans;
    private readonly IBaseRepository<RobotDbMainReportEntity> _reports;
    private readonly IBaseRepository<RobotDbMainTemplateEntity> _templates;
    private readonly IBaseRepository<RobotDbMainVendorEntity> _vendors;
    private readonly IBaseRepository<RobotDbMainReportDataNewEntity> _newRawData;
    private readonly IBaseRepository<RobotDbMainLegacyReportDataEntity> _legacyRawData;

    public IReGoDataRepository(
        IBaseRepository<RobotDbMainUserEntity> users,
        IBaseRepository<RobotDbMainRehabPlanEntity> plans,
        IBaseRepository<RobotDbMainReportEntity> reports,
        IBaseRepository<RobotDbMainTemplateEntity> templates,
        IBaseRepository<RobotDbMainVendorEntity> vendors,
        IBaseRepository<RobotDbMainReportDataNewEntity> newRawData,
        IBaseRepository<RobotDbMainLegacyReportDataEntity> legacyRawData)
    {
        _users = users;
        _plans = plans;
        _reports = reports;
        _templates = templates;
        _vendors = vendors;
        _newRawData = newRawData;
        _legacyRawData = legacyRawData;
    }

    public async Task<IReGoRawDataSet> LoadPatientDataAsync(
        int patientId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var patientSource = await _users.Db.Queryable<RobotDbMainUserEntity>()
            .Where(x => x.Id == patientId && (x.IsDeleted == null || x.IsDeleted == false) &&
                SqlFunc.Subqueryable<DbRoleUserEntity>().Where(r =>
                    r.UserId == x.Id && r.RoleId == DbRoleUserEntity.PatientRole).Any())
            .FirstAsync();

        if (patientSource == null)
        {
            return new IReGoRawDataSet();
        }

        var sourcePlans = await _plans.Db.Queryable<RobotDbMainRehabPlanEntity>()
            .Where(x => x.PatientId == patientId && x.DeviceType == 1)
            .ToListAsync();

        cancellationToken.ThrowIfCancellationRequested();

        var templateIds = sourcePlans
            .Select(x => x.TemplateId)
            .Distinct()
            .ToList();
        var doctorIds = sourcePlans
            .Select(x => x.DoctorId)
            .Distinct()
            .ToList();

        var templates = templateIds.Count == 0
            ? new List<RobotDbMainTemplateEntity>()
            : await _templates.Db.Queryable<RobotDbMainTemplateEntity>()
                .Where(x => templateIds.Contains(x.Id))
                .ToListAsync();
        var doctors = doctorIds.Count == 0
            ? new List<RobotDbMainUserEntity>()
            : await _users.Db.Queryable<RobotDbMainUserEntity>()
                .Where(x => doctorIds.Contains(x.Id) && (x.IsDeleted == null || x.IsDeleted == false) &&
                    SqlFunc.Subqueryable<DbRoleUserEntity>().Where(r =>
                        r.UserId == x.Id && r.RoleId == DbRoleUserEntity.DoctorRole).Any())
                .ToListAsync();
        var vendorExists = patientSource.BusinessId > 0 &&
            await _vendors.Db.Queryable<RobotDbMainVendorEntity>()
                .AnyAsync(x => x.Id == patientSource.BusinessId);

        var templateMap = templates.ToDictionary(x => x.Id);
        var doctorMap = doctors.ToDictionary(x => x.Id);
        var validSourcePlans = sourcePlans
            .Where(plan =>
                vendorExists &&
                templateMap.TryGetValue(plan.TemplateId, out var template) &&
                template.ModelId == 1 &&
                doctorMap.TryGetValue(plan.DoctorId, out var doctor) &&
                doctor.BusinessId == patientSource.BusinessId)
            .ToArray();
        var referenceAnomalyCount = sourcePlans.Count - validSourcePlans.Length;
        var planIds = validSourcePlans.Select(x => x.Id).Distinct().ToList();

        var sourceReports = planIds.Count == 0
            ? new List<RobotDbMainReportEntity>()
            : await _reports.Db.Queryable<RobotDbMainReportEntity>()
                .Where(x => planIds.Contains(x.Id))
                .ToListAsync();

        var newEvidenceIds = planIds.Count == 0
            ? new List<int>()
            : await _newRawData.Db.Queryable<RobotDbMainReportDataNewEntity>()
                .Where(x =>
                    planIds.Contains(x.PlanId) &&
                    (x.Col1 != null || x.Col2 != null || x.Col3 != null ||
                     x.Col4 != null || x.Col5 != null || x.Col6 != null ||
                     x.Col7 != null || x.Col8 != null || x.Col9 != null ||
                     x.Col10 != null))
                .Select(x => x.PlanId)
                .Distinct()
                .ToListAsync();
        var legacyEvidenceIds = planIds.Count == 0
            ? new List<int>()
            : await _legacyRawData.Db.Queryable<RobotDbMainLegacyReportDataEntity>()
                .Where(x =>
                    planIds.Contains(x.Id) &&
                    (x.WImponderability != null || x.WLeftSideway != null ||
                     x.WRightSideway != null || x.WLeftRoll != null ||
                     x.WRightRoll != null || x.WLeftStepSize != null ||
                     x.WRightStepSize != null || x.WLeftSpeed != null ||
                     x.WRightSpeed != null || x.WTimeStamp != null ||
                     x.SImponderability != null || x.SLeftSideway != null ||
                     x.SRightSideway != null || x.SSitTime != null ||
                     x.SStandTime != null || x.SSitSpeed != null ||
                     x.SStandSpeed != null || x.STimeStamp != null ||
                     x.GLeftRightMove != null || x.GFrontBackMove != null ||
                     x.GTimeStamp != null || x.BImponderability != null ||
                     x.BLeftRightMove != null || x.BFrontBackMove != null ||
                     x.BXPositions != null || x.BYPositions != null ||
                     x.BTimeStamp != null))
                .Select(x => x.Id)
                .Distinct()
                .ToListAsync();

        var patient = new PatientEntity
        {
            Id = patientSource.Id,
            Name = patientSource.Name,
            UserDetails = patientSource.UserDetails,
            Status = patientSource.Status,
            IsDeleted = patientSource.IsDeleted,
            BusinessId = patientSource.BusinessId
        };
        var plans = validSourcePlans.Select(x => new RehabPlanEntity
        {
            Id = x.Id,
            UserId = x.PatientId,
            TemplateId = x.TemplateId,
            BusinessId = patientSource.BusinessId,
            DoctorId = x.DoctorId,
            DeviceId = x.Id,
            CreateTime = x.CreateTime,
            BookingTime = x.StartTime,
            StartTime = null,
            EndTime = null,
            IsComplete = x.CompleteStatus,
            Status = null,
            Details = x.Details
        }).ToArray();
        var reports = sourceReports.Select(x => new RehabReportEntity
        {
            Id = x.Id,
            PlanId = x.Id,
            ReportDetails = x.Details,
            CreateTime = x.ReportTime,
            UpdateTime = null
        }).ToArray();
        var devices = validSourcePlans.ToDictionary(
            x => x.Id,
            x => new DeviceEntity { Id = x.Id, DeviceType = x.DeviceType });
        var normalizedTemplates = templates
            .Where(x => planIds.Any(planId =>
                validSourcePlans.Any(plan =>
                    plan.Id == planId && plan.TemplateId == x.Id)))
            .Select(x => new RehabTemplateEntity
            {
                Id = x.Id,
                Title = x.Title,
                Details = x.Details,
                ModelId = x.ModelId
            })
            .ToDictionary(x => x.Id);

        return new IReGoRawDataSet
        {
            Patient = patient,
            Plans = plans,
            Reports = reports,
            Devices = devices,
            Templates = normalizedTemplates,
            ExecutionEvidencePlanIds = newEvidenceIds
                .Concat(legacyEvidenceIds)
                .ToHashSet(),
            ReferenceAnomalyCount = referenceAnomalyCount,
            SourceSystem = "project",
            SourceCompleteness = "authoritative_primary"
        };
    }
}
