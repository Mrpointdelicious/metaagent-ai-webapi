using System.Globalization;
using System.Text;
using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.AITools;
using AI_WebApi.Model.DTOs.AITools;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AI_WebApi.Services.AITools.HospitalOperations;

public sealed class HospitalOperationsUpstreamClient :
    IHospitalOperationsUpstreamClient
{
    private static readonly HttpClient Client = new();
    private readonly HospitalOperationsOptions _options;
    private readonly object _directorySync = new();
    private IReadOnlyList<HospitalDirectoryEntryDto>? _directory;
    private DateTimeOffset _directoryExpiresAt;

    public HospitalOperationsUpstreamClient(
        IOptions<HospitalOperationsOptions> options)
    {
        _options = options.Value;
    }

    public async Task<IReadOnlyList<HospitalDirectoryEntryDto>>
        GetDirectoryAsync(CancellationToken cancellationToken = default)
    {
        lock (_directorySync)
        {
            if (_directory != null && DateTimeOffset.UtcNow < _directoryExpiresAt)
            {
                return _directory;
            }
        }

        var payload = new StringContent(
            "{\"tenantLevel\":0,\"tenantType\":2}",
            Encoding.UTF8,
            "application/json");
        var json = await SendAsync(
            HttpMethod.Post,
            "/api/sysTenant/GetAllTenant",
            payload,
            cancellationToken);
        var items = json["Items"] as JArray ??
                    json["items"] as JArray ??
                    json.SelectToken("data.Items") as JArray ??
                    json.SelectToken("data.items") as JArray ??
                    new JArray();
        var directory = items
            .Select(item => new HospitalDirectoryEntryDto
            {
                HospitalId = Int(item, "Id", "id"),
                HospitalName = Text(item, "Name", "name"),
                TenantLevel = Int(item, "TenantLevel", "tenantLevel")
            })
            .Where(x => x.HospitalId > 0 &&
                        !string.IsNullOrWhiteSpace(x.HospitalName))
            .GroupBy(x => x.HospitalId)
            .Select(x => x.First())
            .OrderBy(x => x.HospitalId)
            .ToArray();
        if (directory.Length == 0)
        {
            throw new InvalidOperationException(
                "Hospital directory response did not contain usable items.");
        }

        lock (_directorySync)
        {
            _directory = directory;
            _directoryExpiresAt = DateTimeOffset.UtcNow.AddSeconds(
                Math.Clamp(_options.DirectoryCacheSeconds, 30, 3600));
        }
        return directory;
    }

    public async Task<HospitalOverviewDto> GetOverviewAsync(
        int hospitalId,
        CancellationToken cancellationToken = default)
    {
        var data = Data(await SendAsync(
            HttpMethod.Get,
            $"/api/sysTenant/GetHospitalAnalysis?id={hospitalId}",
            null,
            cancellationToken));
        return new HospitalOverviewDto
        {
            PatientCount = Int(data, "PatientCount"),
            DoctorCount = Int(data, "DoctorCount"),
            DailyActiveCount = Int(data, "DailyActiveCount"),
            PlatformHospitalCount = Int(data, "HospCount"),
            PlatformSchoolCount = Int(data, "SchoolCount")
        };
    }

    public async Task<HospitalConsultationDto> GetConsultationAsync(
        int hospitalId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        var start = startDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var end = endDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var data = Data(await SendAsync(
            HttpMethod.Get,
            "/api/sysMedical/GetConsultationCount" +
            $"?startTime={start}&endTime={end}&id={hospitalId}",
            null,
            cancellationToken));
        return new HospitalConsultationDto
        {
            ConsultationPeople = Int(data, "ConsultationCount"),
            ConsultationTimes = Int(data, "ConsultationTimes")
        };
    }

    public async Task<HospitalRoomsDto> GetRoomsAsync(
        int hospitalId,
        CancellationToken cancellationToken = default)
    {
        var data = Data(await SendAsync(
            HttpMethod.Get,
            $"/api/sysTenant/GetRoomCount?id={hospitalId}",
            null,
            cancellationToken));
        return new HospitalRoomsDto
        {
            ConsultationRooms = Int(data, "consultationRoomCount"),
            AssessmentRooms = Int(data, "assessmentRoomCount"),
            TrainingRooms = Int(data, "trainingRoomCount")
        };
    }

    public async Task<(HospitalAssessmentTrainingDto AssessmentTraining,
        HospitalRecordsDto Records)> GetDetailsAsync(
        int hospitalId,
        CancellationToken cancellationToken = default)
    {
        var data = Data(await SendAsync(
            HttpMethod.Get,
            $"/api/sysTenant/GetHospitalDetails?id={hospitalId}",
            null,
            cancellationToken));
        var assessment = data["AssesseessData"] as JObject ?? new JObject();
        var training = data["OfflineTrainData"] as JObject ?? new JObject();
        var consultation = data["ConsultationData"] as JObject ?? new JObject();
        return (
            new HospitalAssessmentTrainingDto
            {
                AssessmentCount = Int(
                    assessment,
                    "AssesseesCount",
                    "AssessCount"),
                ScaleAssessmentCount = Int(
                    assessment,
                    "ScaleCount",
                    "ScalesCount"),
                ParadigmAssessmentCount = Int(
                    assessment,
                    "ParadigmCount",
                    "ProCount"),
                OfflineTrainingCount = Int(training, "OfflineTrainCount"),
                DeviceTrainingCount = Int(
                    training,
                    "DeviceTrainCount",
                    "DeviceTrianCount"),
                CorridorTrainingCount = Int(
                    training,
                    "IReTourTrainCount",
                    "CorridorTrainCount")
            },
            new HospitalRecordsDto
            {
                RealConsultationCount = Int(
                    consultation,
                    "RealConsultationCount"),
                AiConsultationCount = Int(
                    consultation,
                    "AIConsultationCount",
                    "AiConsultationCount"),
                RemoteConsultationCount = Int(
                    consultation,
                    "RemoteConsultationCount"),
                MedicalRecordCount = Int(
                    consultation,
                    "MedicalCount",
                    "MedicalRecordCount"),
                PrescriptionTemplateCount = Int(
                    consultation,
                    "PrescriptionTemplateCount"),
                ReportCount = Int(consultation, "ReportCount")
            });
    }

    private async Task<JObject> SendAsync(
        HttpMethod method,
        string relativeUrl,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        var baseUrl = _options.UpstreamBaseUrl?.Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException(
                "HospitalOperations upstream base URL is not configured.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(
            Math.Clamp(_options.RequestTimeoutSeconds, 3, 60)));
        using var request = new HttpRequestMessage(
            method,
            baseUrl + relativeUrl)
        {
            Content = content
        };
        using var response = await Client.SendAsync(request, timeout.Token);
        var body = await response.Content.ReadAsStringAsync(timeout.Token);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Hospital upstream returned HTTP {(int)response.StatusCode}.");
        }

        var json = JsonConvert.DeserializeObject<JObject>(body) ??
                   throw new InvalidOperationException(
                       "Hospital upstream returned invalid JSON.");
        var code = json["code"]?.Value<int?>();
        if (code.HasValue && code.Value != 200)
        {
            throw new InvalidOperationException(
                $"Hospital upstream returned business code {code.Value}.");
        }
        return json;
    }

    private static JObject Data(JObject json) =>
        json["data"] as JObject ?? json["Data"] as JObject ?? json;

    private static int Int(JToken? token, params string[] names)
    {
        foreach (var name in names)
        {
            var value = token?[name];
            if (value != null && int.TryParse(
                    value.ToString(),
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out var parsed))
            {
                return parsed;
            }
        }
        return 0;
    }

    private static string Text(JToken? token, params string[] names)
    {
        foreach (var name in names)
        {
            var value = token?[name]?.ToString()?.Trim();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }
        return string.Empty;
    }
}
