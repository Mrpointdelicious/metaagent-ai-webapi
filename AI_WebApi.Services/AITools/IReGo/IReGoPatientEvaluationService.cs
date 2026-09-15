using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.AITools;
using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.DTOs.AITools;
using AI_WebApi.Repository.AITools;

namespace AI_WebApi.Services.AITools.IReGo;

public sealed class IReGoPatientEvaluationService : IIReGoPatientEvaluationService
{
    private readonly IIReGoDataRepository _repository;
    private readonly IIReGoReportParser _parser;

    public IReGoPatientEvaluationService(
        IIReGoDataRepository repository,
        IIReGoReportParser parser)
    {
        _repository = repository;
        _parser = parser;
    }

    public async Task<IReGoPatientEvaluation> EvaluateAsync(
        TrustedPatientContext context,
        CancellationToken cancellationToken = default)
    {
        var raw = await _repository.LoadPatientDataAsync(
            context.PatientId,
            cancellationToken);
        var issues = new List<ToolIssueDto>();

        if (raw.Patient == null || raw.Patient.IsDeleted == true)
        {
            return new IReGoPatientEvaluation
            {
                Raw = raw,
                DataState = "none",
                Issues = new[]
                {
                    Issue(IReGoReasonCodes.PatientNotFound, "error", "patient", "deny")
                }
            };
        }

        var reportGroups = raw.Reports
            .Where(x => x.PlanId.HasValue)
            .GroupBy(x => x.PlanId!.Value)
            .ToDictionary(x => x.Key, x => x.ToList());
        var evaluated = new List<IReGoEvaluatedSession>();
        var evaluatedPlans = new List<IReGoEvaluatedPlan>();
        var strictPlanCount = 0;
        var eligiblePlanCount = 0;
        var voidedPlanCount = 0;
        var referenceAnomalyCount = raw.ReferenceAnomalyCount;
        var strictReportCount = 0;
        var timeZone = ChinaTimeZone();
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone);

        foreach (var plan in raw.Plans)
        {
            if (!plan.DeviceId.HasValue ||
                !raw.Devices.TryGetValue(plan.DeviceId.Value, out var device))
            {
                referenceAnomalyCount++;
                issues.Add(Issue(
                    IReGoReasonCodes.DeviceReferenceInvalid,
                    "warning",
                    $"plan:{plan.Id}",
                    "exclude_plan"));
                continue;
            }

            if (device.DeviceType != 1)
            {
                continue;
            }

            if (!plan.TemplateId.HasValue ||
                !raw.Templates.TryGetValue(plan.TemplateId.Value, out var template))
            {
                referenceAnomalyCount++;
                issues.Add(Issue(
                    IReGoReasonCodes.TemplateReferenceInvalid,
                    "warning",
                    $"plan:{plan.Id}",
                    "exclude_plan"));
                continue;
            }

            if (template.ModelId != 1)
            {
                referenceAnomalyCount++;
                issues.Add(Issue(
                    IReGoReasonCodes.DeviceClassificationMismatch,
                    "warning",
                    $"plan:{plan.Id}",
                    "exclude_plan"));
                continue;
            }

            strictPlanCount++;
            reportGroups.TryGetValue(plan.Id, out var planReports);
            planReports ??= new List<Model.Entities.RehabReportEntity>();
            var state = TrainingStateResolver.Resolve(
                plan,
                now,
                timeZone,
                planReports.Count > 0 ||
                raw.ExecutionEvidencePlanIds.Contains(plan.Id));
            var parsedPlan = IReGoPlanParser.Parse(plan.Details, template.Details);
            eligiblePlanCount++;

            if (planReports.Count == 0)
            {
                evaluatedPlans.Add(new IReGoEvaluatedPlan
                {
                    Plan = plan,
                    Template = template,
                    Device = device,
                    State = state,
                    ParsedPlan = parsedPlan,
                    Issues = state.Issues.Concat(parsedPlan.Issues).ToArray()
                });
                continue;
            }

            // robotdb_main report.Id is the plan Id, so one plan has at most
            // one summary report. reportdata/reportdata_new rows are not
            // additional summary reports.
            var report = planReports[0];
            strictReportCount++;
            var parsed = _parser.Parse(report.ReportDetails);
            var sessionIssues = new List<ToolIssueDto>();
            sessionIssues.AddRange(parsed.Issues);

            var usable = IsUsable(parsed, state, out var validationStatus);
            sessionIssues.AddRange(state.Issues);
            sessionIssues.AddRange(parsedPlan.Issues);
            var eligibleForLatest = true;
            if (report.CreateTime.HasValue &&
                plan.CreateTime.HasValue &&
                report.CreateTime.Value < plan.CreateTime.Value)
            {
                eligibleForLatest = false;
                sessionIssues.Add(Issue(
                    IReGoReasonCodes.ReportTimeBeforeSourceRecordCreated,
                    "warning",
                    "report_recorded_at",
                    "exclude_from_latest",
                    "report.reporttime is earlier than rehabplan.createtime."));
            }

            var evaluatedSession = new IReGoEvaluatedSession
            {
                Plan = plan,
                Template = template,
                Device = device,
                Report = report,
                ParsedReport = parsed,
                State = state,
                Usable = usable,
                EligibleForLatest = eligibleForLatest,
                ValidationStatus = validationStatus,
                Issues = sessionIssues
            };
            evaluated.Add(evaluatedSession);
            evaluatedPlans.Add(new IReGoEvaluatedPlan
            {
                Plan = plan,
                Template = template,
                Device = device,
                State = state,
                ParsedPlan = parsedPlan,
                EffectiveSession = evaluatedSession,
                Issues = state.Issues
                    .Concat(parsedPlan.Issues)
                    .Concat(parsed.Issues)
                    .ToArray()
            });
        }

        var dataState = ResolveDataState(
            eligiblePlanCount,
            strictReportCount,
            evaluated.Count(x => x.Usable),
            referenceAnomalyCount);
        var watermarkTime = raw.Reports
            .Select(x => x.CreateTime)
            .Concat(raw.Plans.Select(x => x.CreateTime))
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .DefaultIfEmpty(DateTime.MinValue)
            .Max();

        return new IReGoPatientEvaluation
        {
            Raw = raw,
            Sessions = evaluated,
            Plans = evaluatedPlans,
            StrictPlanCount = strictPlanCount,
            EligiblePlanCount = eligiblePlanCount,
            VoidedPlanCount = voidedPlanCount,
            ReferenceAnomalyCount = referenceAnomalyCount,
            StrictReportCount = strictReportCount,
            DataState = dataState,
            Issues = issues,
            Watermark =
                $"{IReGoDataVersion.Namespace}:{IReGoDataVersion.Generation}:v2:db:{watermarkTime:yyyyMMddHHmmss}:{strictPlanCount}:{strictReportCount}"
        };
    }

    private static bool IsUsable(
        ParsedIReGoReport parsed,
        TrainingStateResult state,
        out string validationStatus)
    {
        if (!parsed.ParseSucceeded || !parsed.HasReportList)
        {
            validationStatus = "invalid";
            return false;
        }

        if (state.State != "completed")
        {
            validationStatus = "not_completed";
            return false;
        }

        validationStatus = "structurally_valid";
        return true;
    }

    private static string ResolveDataState(
        int strictPlanCount,
        int strictReportCount,
        int usableSessionCount,
        int referenceAnomalyCount)
    {
        var hasAnomalies = referenceAnomalyCount > 0;
        if (usableSessionCount > 0)
        {
            return hasAnomalies ? "mixed" : "supported_session_available";
        }

        if (strictReportCount > 0)
        {
            return hasAnomalies ? "mixed" : "report_invalid";
        }

        if (strictPlanCount > 0)
        {
            return hasAnomalies ? "mixed" : "plan_only";
        }

        return hasAnomalies ? "anomalous_data_only" : "none";
    }

    private static ToolIssueDto Issue(
        string code,
        string severity,
        string scope,
        string action,
        string? detail = null) => new()
        {
            Code = code,
            Severity = severity,
            Scope = scope,
            Action = action,
            Detail = detail
        };

    private static DateTime ValidSortTime(DateTime? value) =>
        value.HasValue && value.Value.Year > 1900
            ? value.Value
            : DateTime.MinValue;

    public static TimeZoneInfo ChinaTimeZone()
    {
        foreach (var id in new[] { "Asia/Shanghai", "China Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
                // Try the platform-specific alternative.
            }
        }

        return TimeZoneInfo.CreateCustomTimeZone(
            "Asia/Shanghai",
            TimeSpan.FromHours(8),
            "Asia/Shanghai",
            "Asia/Shanghai");
    }
}
