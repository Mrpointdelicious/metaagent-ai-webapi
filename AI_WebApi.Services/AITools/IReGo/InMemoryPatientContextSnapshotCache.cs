using AI_WebApi.Model.AITools;
using System.Collections.Concurrent;
using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.Domain.IReGo;

namespace AI_WebApi.Services.AITools.IReGo;

/// <summary>
/// Optional process-local performance cache. Replace with a shared cache when
/// cross-instance snapshot reuse is needed; session_ref correctness does not depend on it.
/// </summary>
public sealed class InMemoryPatientContextSnapshotCache : IPatientContextSnapshotCache
{
    private readonly ConcurrentDictionary<string, PatientContextSnapshot> _snapshots = new();
    private readonly ConcurrentDictionary<(string ContextKey, string SessionRef), IReGoSessionBinding> _sessions = new();

    public bool TryGetSnapshot(string contextKey, out PatientContextSnapshot snapshot)
    {
        if (IReGoDataVersion.IsCurrentContext(contextKey) &&
            _snapshots.TryGetValue(contextKey, out var candidate) &&
            candidate.ExpiresAt > DateTimeOffset.UtcNow)
        {
            snapshot = candidate;
            return true;
        }

        _snapshots.TryRemove(contextKey, out _);
        snapshot = null!;
        return false;
    }

    public void Store(PatientContextSnapshot snapshot)
    {
        if (!IReGoDataVersion.IsCurrentContext(snapshot.ContextKey) ||
            !IReGoDataVersion.HasHexBody(snapshot.SnapshotRef, IReGoDataVersion.SnapshotPrefix, 32) ||
            ContainsRetiredReferences(snapshot) ||
            snapshot.SessionBindings.Any(x => x.Key != x.Value.SessionRef ||
                !IReGoDataVersion.IsCurrentSession(x.Key) || x.Value.ContextKey != snapshot.ContextKey))
        {
            return;
        }
        _snapshots[snapshot.ContextKey] = snapshot;
        foreach (var binding in snapshot.SessionBindings.Values)
        {
            StoreSession(binding);
        }
    }

    private static bool ContainsRetiredReferences(PatientContextSnapshot snapshot)
    {
        var data = snapshot.Data;
        var domain = data.DataDomains.Irego;
        var refs = new[] { data.PatientRef, data.IdentityBinding.PatientRef,
            data.IdentityBinding.TenantRef, domain.Latest?.SessionRef }
            .Concat(domain.RecentSessions.Select(x => x.SessionRef))
            .Concat(domain.FuturePlans.Concat(domain.UnfinishedPlans).Concat(domain.VoidedPlans).Select(x => x.PlanRef));
        return refs.Any(x => x?.StartsWith("irego_patient_v1_", StringComparison.Ordinal) == true ||
            x?.StartsWith("irego_tenant_v1_", StringComparison.Ordinal) == true ||
            x?.StartsWith("irego_p_v1_", StringComparison.Ordinal) == true ||
            IReGoDataVersion.IsRetiredSession(x)) ||
            (!string.IsNullOrEmpty(data.Snapshot.SnapshotRef) && data.Snapshot.SnapshotRef != snapshot.SnapshotRef);
    }

    public void StoreSession(IReGoSessionBinding binding)
    {
        if (IReGoDataVersion.IsCurrentContext(binding.ContextKey) &&
            IReGoDataVersion.IsCurrentSession(binding.SessionRef))
        {
            _sessions[(binding.ContextKey, binding.SessionRef)] = binding;
        }
    }

    public bool TryResolveSession(
        string contextKey,
        string sessionRef,
        out IReGoSessionBinding binding)
    {
        binding = null!;
        if (!IReGoDataVersion.IsCurrentContext(contextKey) || !IReGoDataVersion.IsCurrentSession(sessionRef))
            return false;
        var key = (contextKey, sessionRef);
        if (_sessions.TryGetValue(key, out var candidate) &&
            candidate.ExpiresAt > DateTimeOffset.UtcNow &&
            string.Equals(candidate.ContextKey, contextKey, StringComparison.Ordinal))
        {
            binding = candidate;
            return true;
        }

        if (candidate != null && candidate.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            _sessions.TryRemove(key, out _);
        }

        binding = null!;
        return false;
    }
}
