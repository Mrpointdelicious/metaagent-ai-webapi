using AI_WebApi.Model.DTOs.AITools;

namespace AI_WebApi.IServices.AITools;

public interface IPatientRehabOverviewService
{
    Task<PatientRehabOverviewDto?> GetAsync(
        int patientId,
        int pastDays,
        int futureDays,
        CancellationToken cancellationToken = default);
}