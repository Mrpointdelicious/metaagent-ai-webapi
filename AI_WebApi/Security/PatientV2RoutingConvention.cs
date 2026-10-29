using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace AI_WebApi.Security;

/// <summary>Replace patient HTTP contracts in place. There is no v1 or dual-route fallback.</summary>
public sealed class PatientV2RoutingConvention : IApplicationModelConvention
{
    public const string GroupName = "ai-patient-tools-v2";
    private static readonly HashSet<string> Controllers = new(StringComparer.Ordinal)
    { "IReGoTools", "IReTourTools", "IReCogTools", "AssessmentTools", "PatientToolFamilies", "PatientLayerTools" };

    public void Apply(ApplicationModel application)
    {
        foreach (var original in application.Controllers.Where(c => Controllers.Contains(c.ControllerName)).ToArray())
        {
            original.ApiExplorer.GroupName = GroupName;
            original.Filters.Add(new ServiceFilterAttribute(typeof(PatientV2ScopeFilter)) { Order = -1500 });
            foreach (var action in original.Actions.ToArray())
            {
                var isCatalog = original.ControllerName == "PatientToolFamilies" && action.ActionName == "Catalog";
                if (!isCatalog && !action.Attributes.OfType<HttpPostAttribute>().Any()) { original.Actions.Remove(action); continue; }
                action.ApiExplorer.GroupName = GroupName;
            }
        }
    }
}
