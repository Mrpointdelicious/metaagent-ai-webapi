namespace AI_WebApi.Model.AITools;

public sealed class IReTourToolOptions
{
    public const string SectionName = "IReTourTools";

    /// <summary>HMAC key for stable anonymous IReTour session references.</summary>
    public string SessionRefSigningKey { get; set; } = string.Empty;

    public string SourceCompleteness { get; set; } = "partial";
    public int HistoryCacheSeconds { get; set; } = 300;
    public int MaxRecentSessions { get; set; } = 10;
    public string SingleReportTemplateVersion { get; set; } =
        "iretour-single-v3";
    public string LongitudinalReportTemplateVersion { get; set; } =
        "iretour-longitudinal-v3";
}

/// <summary>
/// IReTour contract and registry versions. The three read tools deliberately do
/// not share the IReGo envelope contract; they answer from the IReTour domain.
/// </summary>
public static class IReTourContract
{
    public const string Version = "iretour-1.0.0";
    public const string DeviceRegistryVersion = "iretour-device-1.0.0";
    public const string SchemaRegistryVersion = "iretour-schema-1.0.0";
    public const string MetricRegistryVersion = "iretour-metric-1.0.0";
    public const string StateRegistryVersion = "iretour-state-1.0.0";
    public const string ItemRegistryVersion = "iretour-item-registry-1.0.0";
    public const string DeviceFamily = "iretour";
    public const string SourceSystem = "project_iretour";
    public const int RequiredDeviceType = 6;
}

public static class IReTourReasonCodes
{
    public const string SourceChanged = "IRETOUR_SOURCE_CHANGED";
    public const string ReadLimitExceeded = "IRETOUR_READ_LIMIT_EXCEEDED";
    public const string InvalidRequest = "INVALID_REQUEST";
    public const string NoIReTourData = "NO_IRETOUR_DATA";
    public const string NoUsableSession = "NO_USABLE_IRETOUR_SESSION";
    public const string PatientBindingNotFound = "IRETOUR_PATIENT_BINDING_NOT_FOUND";
    public const string PatientBindingAmbiguous = "IRETOUR_PATIENT_BINDING_AMBIGUOUS";
    public const string DeviceReferenceInvalid = "IRETOUR_DEVICE_REFERENCE_INVALID";
    public const string ReportLinkNotFound = "IRETOUR_REPORT_LINK_NOT_FOUND";
    public const string ReportLinkAmbiguous = "IRETOUR_REPORT_LINK_AMBIGUOUS";
    public const string ReportLinkConflict = "IRETOUR_REPORT_LINK_CONFLICT";
    public const string ItemMismatch = "IRETOUR_ITEM_MISMATCH";
    public const string ItemUnknown = "IRETOUR_ITEM_UNKNOWN";
    public const string SchemaUnsupported = "IRETOUR_SCHEMA_UNSUPPORTED";
    public const string TimeInvalid = "IRETOUR_TIME_INVALID";
    public const string TimeOrderInvalid = "IRETOUR_TIME_ORDER_INVALID";
    public const string DurationInvalid = "IRETOUR_DURATION_INVALID";
    public const string MetricUnitUnconfirmed = "IRETOUR_METRIC_UNIT_UNCONFIRMED";
    public const string MetricOutOfRange = "IRETOUR_METRIC_OUT_OF_RANGE";
    public const string NonFiniteMetricExcluded = "IRETOUR_NON_FINITE_METRIC_EXCLUDED";
    public const string DuplicateReportIdentical = "IRETOUR_DUPLICATE_REPORT_IDENTICAL";
    public const string DuplicateReportConflict = "IRETOUR_DUPLICATE_REPORT_CONFLICT";
    public const string RawDataNotReturned = "IRETOUR_RAW_DATA_NOT_RETURNED";
    public const string LongitudinalRequiresTwoSessions =
        "IRETOUR_LONGITUDINAL_REQUIRES_TWO_SESSIONS";
    public const string LongitudinalWindowTooLarge =
        "IRETOUR_LONGITUDINAL_WINDOW_TOO_LARGE";
    public const string NoComparableMetric = "IRETOUR_NO_COMPARABLE_METRIC";
    public const string SingleReportRequiresCompletedSession =
        "IRETOUR_SINGLE_REPORT_REQUIRES_COMPLETED_SESSION";
    public const string SingleReportHasNoMetrics =
        "IRETOUR_SINGLE_REPORT_HAS_NO_METRICS";
    public const string SingleReportQualityBlocked =
        "IRETOUR_SINGLE_REPORT_QUALITY_BLOCKED";
    public const string LongitudinalReportInsufficientData =
        "IRETOUR_LONGITUDINAL_REPORT_INSUFFICIENT_DATA";
    public const string ReportImageSigningKeyNotConfigured =
        "IRETOUR_REPORT_IMAGE_SIGNING_KEY_NOT_CONFIGURED";
    public const string ReportRenderingUnavailable =
        "IRETOUR_REPORT_RENDERING_UNAVAILABLE";
    public const string SessionRefNotFound = "SESSION_REF_NOT_FOUND";
    public const string SessionRefContextMismatch = "SESSION_REF_CONTEXT_MISMATCH";
    public const string SessionRefSigningKeyNotConfigured =
        "IRETOUR_SESSION_REF_SIGNING_KEY_NOT_CONFIGURED";
    public const string DependencyFailure = "DEPENDENCY_FAILURE";
}
