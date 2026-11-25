using AI_WebApi.Model.AITools;
using AI_WebApi.Model.Domain.IReGo;
using AI_WebApi.Model.Entities;
using AI_WebApi.Repository.AITools;
using AI_WebApi.Services.AITools.PatientContext;
using Microsoft.Extensions.Options;

namespace AI_WebApi.Tests;

public sealed class ConfirmedWalkScopeTests
{
    [Fact]
    public async Task Direct_project_user_walks_and_reports_are_scoped_by_owner_device_and_tenant()
    {
        using var db = new PatientAccessTestDatabase();
        var config = new PatientAccessOptions { IdentitySigningKey = PatientAccessContextTests.Key };
        var resolver = new PatientAccessContextResolver(new PatientAccessRepository(db), Options.Create(config));
        var repository = new IReTourDataRepository(db);
        async Task<(IReTourRawDataSet Data, PatientAccessContext Access)> Read(string scope)
        {
            var resolved = await resolver.ResolveAsync(PatientAccessContextTests.Input(), scope, "r", default);
            var data = await repository.LoadPatientDataAsync(resolved.Context!, default);
            return (data, resolved.Context!.Access!);
        }

        var empty = await Read("all_tenants");
        Assert.True(empty.Data.Binding.Verified);
        Assert.Equal("direct_primary_key", empty.Data.Binding.BindingMethod);
        Assert.Empty(empty.Data.Walks);
        Assert.DoesNotContain("SOURCE_PATIENT_BINDING_UNVERIFIED", empty.Access.Describe().Issues);

        var walks = new[]
        {
            new IReTourWalkEntity { Id = 1, UserId = 7, BusinessId = 1, DeviceId = 4 },
            new IReTourWalkEntity { Id = 2, UserId = 7, BusinessId = 2, DeviceId = 4 },
            new IReTourWalkEntity { Id = 3, UserId = 7, BusinessId = 0, DeviceId = 4 },
            new IReTourWalkEntity { Id = 4, UserId = 7, BusinessId = 1, DeviceId = 1 },
            new IReTourWalkEntity { Id = 5, UserId = 9, BusinessId = 1, DeviceId = 4 },
            new IReTourWalkEntity { Id = 6, UserId = 99, BusinessId = 1, DeviceId = 4 },
        };
        db.Db.Insertable(walks).OffIdentity().ExecuteCommand();
        db.Db.Insertable(walks.Select(walk => new IReTourSummaryReportEntity
            { Id = walk.Id, PlanId = walk.Id }).ToArray()).OffIdentity().ExecuteCommand();
        db.Db.Insertable(walks.Select(walk => new IReTourRawReportEntity
            { Id = walk.Id, PlanId = walk.Id }).ToArray()).OffIdentity().ExecuteCommand();

        var all = await Read("all_tenants");
        Assert.True(all.Data.Binding.Verified);
        Assert.True(all.Data.ScopedOwnershipVerified);
        Assert.Equal(new[] { 1, 2, 3 }, all.Data.Walks.Select(walk => walk.Id).OrderBy(id => id));
        Assert.Equal(new[] { 1, 2, 3 }, all.Data.SummaryReports.Select(report => report.PlanId).OrderBy(id => id));
        Assert.Equal(new[] { 1, 2, 3 }, all.Data.RawReports.Select(report => report.PlanId).OrderBy(id => id));
        Assert.DoesNotContain("SOURCE_PATIENT_BINDING_UNVERIFIED", all.Access.Describe().Issues);
        Assert.Contains("RECORD_TENANT_UNVERIFIED", all.Access.Describe().Issues);

        var current = await Read("current_tenant");
        Assert.Equal(1, Assert.Single(current.Data.Walks).Id);
        Assert.Equal(1, Assert.Single(current.Data.SummaryReports).PlanId);
        Assert.Equal(1, Assert.Single(current.Data.RawReports).PlanId);
        Assert.Empty(current.Access.Describe().Issues);
    }
}
