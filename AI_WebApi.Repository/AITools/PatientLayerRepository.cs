using AI_WebApi.Model.DTOs.AITools;
using AI_WebApi.Model.Entities;
using AI_WebApi.Repository.UnitOfWorks;
using SqlSugar;
using AI_WebApi.Model.Domain.IReGo;
using System.Globalization;

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

    private static SugarParameter[] Parameters(TrustedPatientContext context, int page = 1, int size = 5) => new[]
    {
        new SugarParameter("@patient", context.PatientId),
        new SugarParameter("@tenant", context.Access?.DataScope == "current_tenant" ? context.Access.CurrentBusinessId : null),
        new SugarParameter("@tenant_text", context.Access?.CurrentBusinessId?.ToString(CultureInfo.InvariantCulture)),
        new SugarParameter("@offset", (page - 1) * size), new SugarParameter("@size", size)
    };

    public async Task<(int Count, IReadOnlyList<PatientMedicalRow> Rows)> MedicalAsync(int patientId, int page, int size, CancellationToken ct)
        => await MedicalAsync(new TrustedPatientContext { PatientId = patientId }, page, size, ct);

    public async Task<(int Count, IReadOnlyList<PatientMedicalRow> Rows)> MedicalAsync(TrustedPatientContext context, int page, int size, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        const string scope = " FROM dbmedical WHERE UserId=@patient AND Enable=1 AND (@tenant IS NULL OR TenantId=@tenant)";
        var count = await Project.Ado.GetIntAsync("SELECT COUNT(*)" + scope, Parameters(context));
        var rows = await Project.Ado.SqlQueryAsync<PatientMedicalRow>(@"SELECT Id RecordId,TenantId InstitutionId,CreateTime RecordedAt,
            MedicalRecord,MedicalContent,Summary,ArrTime FollowupTime,ArrHospital FollowupHospital
            " + scope + " ORDER BY CreateTime DESC,Id DESC LIMIT @offset,@size", Parameters(context, page, size));
        foreach (var row in rows)
        {
            row.RecordedAt = RealDate(row.RecordedAt);
            row.RecordTenant = context.Access?.Observe($"project.dbmedical:{row.RecordId}", row.InstitutionId, "project.dbmedical.TenantId", context.PatientId);
        }
        return (count, rows);
    }

    public async Task<(int Count, IReadOnlyList<PatientConsultationRecordDto> Rows)> ConsultationsAsync(int patientId, int page, int size, CancellationToken ct)
        => await ConsultationsAsync(new TrustedPatientContext { PatientId = patientId }, page, size, ct);

    public async Task<(int Count, IReadOnlyList<PatientConsultationRecordDto> Rows)> ConsultationsAsync(TrustedPatientContext context, int page, int size, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        const string scope = @" FROM dbconsultationinfo c LEFT JOIN dbmedical m
            ON m.Id=c.MedicalId AND m.UserId=c.PatientId AND m.Enable=1
            WHERE c.PatientId=@patient AND (COALESCE(c.MedicalId,0)=0 OR m.Id IS NOT NULL)
            AND (@tenant IS NULL OR (c.BusinessId=@tenant_text AND (m.Id IS NULL OR m.TenantId=@tenant)))";
        var count = await Project.Ado.GetIntAsync("SELECT COUNT(*)" + scope, Parameters(context));
        var rows = await Project.Ado.SqlQueryAsync<ConsultationRow>(@"SELECT c.Id RecordId,c.MedicalId,
            c.CreateTime CreatedAt,c.StartTime StartedAt,c.EndTime EndedAt,c.BusinessId BusinessId" + scope +
            " ORDER BY c.CreateTime DESC,c.Id DESC LIMIT @offset,@size", Parameters(context, page, size));
        return (count, rows.Select(row => new PatientConsultationRecordDto
        {
            RecordId = row.RecordId, MedicalId = row.MedicalId, CreatedAt = RealDate(row.CreatedAt),
            StartedAt = RealDate(row.StartedAt), EndedAt = RealDate(row.EndedAt),
            RecordTenant = context.Access?.Observe($"project.dbconsultationinfo:{row.RecordId}",
                int.TryParse(row.BusinessId, NumberStyles.None, CultureInfo.InvariantCulture, out var tenant) && tenant > 0 ? tenant : null,
                "project.dbconsultationinfo.BusinessId", context.PatientId)
        }).ToArray());
    }

    public async Task<IReadOnlyList<PatientPrescriptionDto>> PrescriptionsAsync(int patientId, int medicalId, CancellationToken ct)
        => await PrescriptionsAsync(new TrustedPatientContext { PatientId = patientId }, medicalId, ct);

    public async Task<IReadOnlyList<PatientPrescriptionDto>> PrescriptionsAsync(TrustedPatientContext context, int medicalId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return await Project.Ado.SqlQueryAsync<PatientPrescriptionDto>(@"SELECT DISTINCT p.Id PrescriptionId,
            p.Name,p.Indication,p.Prescription Content,p.OfflineTrain OfflineTraining,p.OnlineEvaluation
            FROM dbmedical m JOIN dbprescription p ON (p.Id=m.PrescriptionId
            OR EXISTS(SELECT 1 FROM dbmedicalprescription mp WHERE mp.MedicalId=m.Id
                AND mp.PrescriptionId=p.Id AND COALESCE(mp.IsDeleted,0)=0))
            WHERE m.UserId=@patient AND m.Id=@medical AND m.Enable=1 AND p.Enable=1
                AND (@tenant IS NULL OR m.TenantId=@tenant) ORDER BY p.Id LIMIT 20",
            Parameters(context).Append(new SugarParameter("@medical", medicalId)).ToArray());
    }

    public async Task<(int Count, IReadOnlyList<PatientRehabPlanDto> Rows)> PlansAsync(int patientId, int page, int size, CancellationToken ct)
        => await PlansAsync(new TrustedPatientContext { PatientId = patientId }, page, size, ct);

    public async Task<(int Count, IReadOnlyList<PatientRehabPlanDto> Rows)> PlansAsync(
        AI_WebApi.Model.Domain.IReGo.TrustedPatientContext context, int page, int size, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var access = context.Access;
        var owners = access?.UserIds ?? new[] { context.PatientId };
        var query = Project.Queryable<RehabPlanEntity>().Where(p => owners.Contains(p.UserId ?? 0))
            .WhereIF(access?.DataScope == "current_tenant", p => p.BusinessId == access!.CurrentBusinessId);
        var count = await query.Clone().CountAsync();
        var rows = await query.OrderBy(p => SqlFunc.IsNull(p.BookingTime, p.CreateTime), OrderByType.Desc)
            .OrderBy(p => p.Id, OrderByType.Desc).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return (count, rows.Select(p => new PatientRehabPlanDto
        {
            PlanId = p.Id, CreatedAt = RealDate(p.CreateTime), ScheduledAt = RealDate(p.BookingTime), CompletionFlag = p.IsComplete,
            Status = p.Status, Details = p.Details,
            RecordTenant = access?.Observe($"project.dbrehaplan:{p.Id}", p.BusinessId, "project.dbrehaplan.BusinessId", p.UserId),
            OwnerUserRef = access?.Reference("owner", "project.dbuser|" + p.UserId)
        }).ToArray());
    }

    private static DateTime? RealDate(DateTime? value) => value?.Year > 1900 ? value : null;

    public sealed class ConsultationRow
    {
        public int RecordId { get; set; }
        public int? MedicalId { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? EndedAt { get; set; }
        public string? BusinessId { get; set; }
    }
}
