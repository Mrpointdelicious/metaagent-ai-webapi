using AI_WebApi.Model.DTOs.AITools;
using AI_WebApi.Model.Entities;
using AI_WebApi.Repository.UnitOfWorks;
using SqlSugar;

namespace AI_WebApi.Repository.AITools;

/// <summary>Parameterised read queries against the explicit project connection.</summary>
public sealed class PatientLayerRepository : IPatientLayerRepository
{
    private readonly SqlSugarScope _db;
    public PatientLayerRepository(IUnitOfWorkManage unitOfWork) => _db = unitOfWork.GetDbClient();
    private SqlSugarScopeProvider Project => _db.GetConnectionScope("project_main");

    public async Task<PatientEntity?> PatientAsync(int patientId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Project.Queryable<PatientEntity>().Where(x => x.Id == patientId).FirstAsync();
    }

    private async Task<int> CountAsync(string sql, int patientId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return await Project.Ado.GetIntAsync(sql, new SugarParameter("@patient", patientId));
    }

    private async Task<List<T>> PageAsync<T>(string sql, int patientId, int page, int size, CancellationToken ct) where T : class, new()
    {
        ct.ThrowIfCancellationRequested();
        return await Project.Ado.SqlQueryAsync<T>(sql,
            new SugarParameter("@patient", patientId),
            new SugarParameter("@offset", (page - 1) * size), new SugarParameter("@size", size));
    }

    public async Task<(int Count, IReadOnlyList<PatientMedicalRow> Rows)> MedicalAsync(int patientId, int page, int size, CancellationToken ct)
    {
        var count = await CountAsync("SELECT COUNT(*) FROM dbmedical WHERE UserId=@patient", patientId, ct);
        var rows = await PageAsync<PatientMedicalRow>(@"SELECT Id RecordId,CreateTime RecordedAt,
            MedicalRecord,MedicalContent,Summary,ArrTime FollowupTime,ArrHospital FollowupHospital
            FROM dbmedical WHERE UserId=@patient ORDER BY CreateTime DESC,Id DESC LIMIT @offset,@size", patientId, page, size, ct);
        return (count, rows);
    }

    public async Task<(int Count, IReadOnlyList<PatientConsultationRecordDto> Rows)> ConsultationsAsync(int patientId, int page, int size, CancellationToken ct)
    {
        var count = await CountAsync("SELECT COUNT(*) FROM dbconsultationinfo WHERE PatientId=@patient", patientId, ct);
        var rows = await PageAsync<PatientConsultationRecordDto>(@"SELECT Id RecordId,MedicalId,
            CreateTime CreatedAt,StartTime StartedAt,EndTime EndedAt FROM dbconsultationinfo
            WHERE PatientId=@patient ORDER BY CreateTime DESC,Id DESC LIMIT @offset,@size", patientId, page, size, ct);
        return (count, rows);
    }

    public async Task<IReadOnlyList<PatientPrescriptionDto>> PrescriptionsAsync(int patientId, int medicalId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return await Project.Ado.SqlQueryAsync<PatientPrescriptionDto>(@"SELECT DISTINCT p.Id PrescriptionId,
            p.Name,p.Indication,p.Prescription Content,p.OfflineTrain OfflineTraining,p.OnlineEvaluation
            FROM dbmedical m JOIN dbprescription p ON p.Id=m.PrescriptionId
            OR EXISTS(SELECT 1 FROM dbmedicalprescription mp WHERE mp.MedicalId=m.Id
                AND mp.PrescriptionId=p.Id AND COALESCE(mp.IsDeleted,0)=0)
            WHERE m.UserId=@patient AND m.Id=@medical ORDER BY p.Id LIMIT 20",
            new SugarParameter("@patient", patientId), new SugarParameter("@medical", medicalId));
    }

    public async Task<(int Count, IReadOnlyList<PatientRehabPlanDto> Rows)> PlansAsync(int patientId, int page, int size, CancellationToken ct)
    {
        var count = await CountAsync("SELECT COUNT(*) FROM dbrehaplan WHERE UserId=@patient", patientId, ct);
        var rows = await PageAsync<PatientRehabPlanDto>(@"SELECT Id PlanId,CreateTime CreatedAt,
            BookingTime ScheduledAt,IsComplete CompletionFlag,Status,Details FROM dbrehaplan
            WHERE UserId=@patient ORDER BY COALESCE(BookingTime,CreateTime) DESC,Id DESC LIMIT @offset,@size", patientId, page, size, ct);
        return (count, rows);
    }

    public async Task<(int Count, IReadOnlyList<PatientRehabPlanDto> Rows)> PlansAsync(
        AI_WebApi.Model.Domain.IReGo.TrustedPatientContext context, int page, int size, CancellationToken ct)
    {
        if (context.Access == null) return await PlansAsync(context.PatientId, page, size, ct);
        ct.ThrowIfCancellationRequested();
        var access = context.Access;
        var query = Project.Queryable<RehabPlanEntity>().Where(p => access.UserIds.Contains(p.UserId ?? 0))
            .WhereIF(access.DataScope == "current_tenant", p => p.BusinessId == access.CurrentBusinessId);
        var count = await query.Clone().CountAsync();
        var rows = await query.OrderBy(p => SqlFunc.IsNull(p.BookingTime, p.CreateTime), OrderByType.Desc)
            .OrderBy(p => p.Id, OrderByType.Desc).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return (count, rows.Select(p => new PatientRehabPlanDto
        {
            PlanId = p.Id, CreatedAt = p.CreateTime, ScheduledAt = p.BookingTime, CompletionFlag = p.IsComplete,
            Status = p.Status, Details = p.Details,
            RecordTenant = access.Observe($"project.dbrehaplan:{p.Id}", p.BusinessId, "project.dbrehaplan.BusinessId", p.UserId),
            OwnerUserRef = access.Reference("owner", "project.dbuser|" + p.UserId)
        }).ToArray());
    }
}
