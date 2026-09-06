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
    private readonly ConcurrentDictionary<string, IReGoSessionBinding> _sessions = new();

    public bool TryGetSnapshot(string contextKey, out PatientContextSnapshot snapshot)
    {
        if (_snapshots.TryGetValue(contextKey, out var candidate) &&
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
        _snapshots[snapshot.ContextKey] = snapshot;
        foreach (var binding in snapshot.SessionBindings.Values)
        {
            StoreSession(binding);
        }
    }

    public void StoreSession(IReGoSessionBinding binding)
    {
        _sessions[binding.SessionRef] = binding;
    }

    public bool TryResolveSession(
        string contextKey,
        string sessionRef,
        out IReGoSessionBinding binding)
    {
        if (_sessions.TryGetValue(sessionRef, out var candidate) &&
            candidate.ExpiresAt > DateTimeOffset.UtcNow &&
            string.Equals(candidate.ContextKey, contextKey, StringComparison.Ordinal))
        {
            binding = candidate;
            return true;
        }

        if (candidate != null && candidate.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            _sessions.TryRemove(sessionRef, out _);
        }

        binding = null!;
        return false;
    }
}
