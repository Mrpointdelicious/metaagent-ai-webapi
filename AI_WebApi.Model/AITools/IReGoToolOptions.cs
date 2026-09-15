namespace AI_WebApi.Model.AITools;

public sealed class IReGoToolOptions
{
    public const string SectionName = "IReGoTools";

    public string DifyServiceBearerToken { get; set; } = string.Empty;
    public string SessionRefSigningKey { get; set; } = string.Empty;
    public int SnapshotTtlSeconds { get; set; } = 300;
    public string SourceCompleteness { get; set; } = "authoritative_primary";
    public IReGoReportImageOptions ReportImage { get; set; } = new();
}

public sealed class IReGoReportImageOptions
{
    public string Mode { get; set; } = "DurableFileSystem";
    public string SigningKey { get; set; } = string.Empty;
    public string PublicBaseUrl { get; set; } = string.Empty;
    public string StoragePath { get; set; } = "data/patient-reports/" + IReGoDataVersion.StorageDirectory;
    public int UrlTtlSeconds { get; set; } = 300;
    public int CacheTtlSeconds { get; set; } = 600;
    public int MaxImageBytes { get; set; } = 5 * 1024 * 1024;
    public int MaxCacheBytes { get; set; } = 128 * 1024 * 1024;
    public string TemplateVersion { get; set; } = "irego-single-v2";
    public string LongitudinalTemplateVersion { get; set; } =
        "irego-longitudinal-v5";
    public string BrowserExecutablePath { get; set; } = "/usr/bin/chromium";
    public int RenderTimeoutSeconds { get; set; } = 30;
    public int MaxConcurrentRenders { get; set; } = 2;
}

public static class IReGoReasonCodes
{
    public const string DifyServiceAuthenticationRequired =
        "DIFY_SERVICE_AUTHENTICATION_REQUIRED";
    public const string DifyServiceAuthenticationFailed =
        "DIFY_SERVICE_AUTHENTICATION_FAILED";
    public const string DifyServiceAuthenticationNotConfigured =
        "DIFY_SERVICE_AUTHENTICATION_NOT_CONFIGURED";
    public const string DifySystemContextMissing = "DIFY_SYSTEM_CONTEXT_MISSING";
    public const string InvalidDifySystemContext = "INVALID_DIFY_SYSTEM_CONTEXT";
    public const string DifyUserIdInvalid = "DIFY_USER_ID_INVALID";
    public const string PatientNotFound = "PATIENT_NOT_FOUND";
    public const string PatientBindingNotFound =
        "PATIENT_BINDING_NOT_FOUND";
    public const string InvalidRequest = "INVALID_REQUEST";
    public const string SessionRefNotFound = "SESSION_REF_NOT_FOUND";
    public const string SessionRefContextMismatch = "SESSION_REF_CONTEXT_MISMATCH";
    public const string SessionRefSigningKeyNotConfigured =
        "SESSION_REF_SIGNING_KEY_NOT_CONFIGURED";
    public const string NoIReGoData = "NO_IREGO_DATA";
    public const string NoUsableSession = "NO_USABLE_IREGO_SESSION";
    public const string ReportDetailsInvalid = "REPORT_DETAILS_INVALID";
    public const string PlanDetailsInvalid = "PLAN_DETAILS_INVALID";
    public const string ReportListMissing = "REPORT_LIST_MISSING";
    public const string MultipleReportsLatestSelected =
        "MULTIPLE_REPORTS_LATEST_SELECTED";
    public const string HistoryWindowTruncated = "HISTORY_WINDOW_TRUNCATED";
    public const string PlanMarkedVoided = "PLAN_MARKED_VOIDED";
    public const string DeviceReferenceInvalid = "DEVICE_REFERENCE_INVALID";
    public const string TemplateReferenceInvalid = "TEMPLATE_REFERENCE_INVALID";
    public const string DeviceClassificationMismatch = "DEVICE_CLASSIFICATION_MISMATCH";
    public const string ReportProcessMismatch = "REPORT_PROCESS_SEQUENCE_MISMATCH";
    public const string UnknownReportMode = "UNKNOWN_REPORT_MODE";
    public const string AmbiguousTemplateMode = "AMBIGUOUS_TEMPLATE_MODE";
    public const string NonFiniteMetric = "NON_FINITE_METRIC_EXCLUDED";
    public const string CompletionEndTimeMissing = "COMPLETION_END_TIME_MISSING";
    public const string StateEvidenceConflict = "STATE_EVIDENCE_CONFLICT";
    public const string CompletionFlagEvidenceConflict =
        "COMPLETION_FLAG_EVIDENCE_CONFLICT";
    public const string ReportTimeBeforeSourceRecordCreated =
        "REPORT_TIME_BEFORE_SOURCE_RECORD_CREATED";
    public const string SnapshotCacheNotDistributed = "SNAPSHOT_CACHE_NOT_DISTRIBUTED";
    public const string ReportDurationExceedsPlanWindow =
        "REPORT_DURATION_EXCEEDS_PLAN_WINDOW";
    public const string WalkingDistanceWithoutStepsOrSpeed =
        "WALKING_DISTANCE_WITHOUT_STEPS_OR_SPEED";
    public const string DomainNotImplemented = "DOMAIN_NOT_IMPLEMENTED";
    public const string SingleReportRequiresCompletedSession =
        "SINGLE_REPORT_REQUIRES_COMPLETED_SESSION";
    public const string SingleReportHasNoBlocks = "SINGLE_REPORT_HAS_NO_BLOCKS";
    public const string SingleReportAiEvaluationRequired =
        "SINGLE_REPORT_AI_EVALUATION_REQUIRED";
    public const string SingleReportAiEvaluationInvalid =
        "SINGLE_REPORT_AI_EVALUATION_INVALID";
    public const string SingleReportAiEvaluationMismatch =
        "SINGLE_REPORT_AI_EVALUATION_MISMATCH";
    public const string ReportImageSigningKeyNotConfigured =
        "REPORT_IMAGE_SIGNING_KEY_NOT_CONFIGURED";
    public const string ReportRenderingUnavailable = "REPORT_RENDERING_UNAVAILABLE";
    public const string LongitudinalRequiresTwoReports =
        "LONGITUDINAL_REQUIRES_TWO_REPORTS";
    public const string LongitudinalWindowTooLarge =
        "LONGITUDINAL_WINDOW_TOO_LARGE";
    public const string LongitudinalNoComparableProject =
        "LONGITUDINAL_NO_COMPARABLE_PROJECT";
    public const string LongitudinalEvaluationInsufficientData =
        "LONGITUDINAL_EVALUATION_INSUFFICIENT_DATA";
    public const string LongitudinalIncompleteReportsIncludedAsMissing =
        "LONGITUDINAL_INCOMPLETE_REPORTS_INCLUDED_AS_MISSING";
}
