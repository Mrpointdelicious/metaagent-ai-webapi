using AI_WebApi.Model.AITools;
using AI_WebApi.Model.Entities;
using AI_WebApi.Repository.AITools;
using AI_WebApi.Services.AITools.PatientContext;
using Microsoft.Extensions.Options;

namespace AI_WebApi.Tests;

public sealed class ConfirmedWalkScopeTests
{
    [Fact]
    public async Task Only_unambiguous_mapped_walks_and_their_reports_are_read()
    {
        using var db = new PatientAccessTestDatabase();
        var config = new PatientAccessOptions { IdentitySigningKey = PatientAccessContextTests.Key };
        var resolver = new PatientAccessContextResolver(new PatientAccessRepository(db), Options.Create(config));
        var repository = new IReTourDataRepository(db);
        async Task<IReTourRawDataSet> Read(string scope)
        {
            var resolved = await resolver.ResolveAsync(PatientAccessContextTests.Input(), scope, "r", default);
            return await repository.LoadPatientDataAsync(resolved.Context!, default);
        }
        static ConfirmedWalkRecordBinding Map(int walk, int owner, int? tenant) =>
            new() { WalkId = walk, ProjectUserId = owner, BusinessId = tenant };
        var walks = Enumerable.Range(1, 7).Select(id => new IReTourWalkEntity
            { Id = id, UserId = id == 1 ? 7 : 99, BusinessId = id == 3 ? 2 : 1, DeviceId = 4 }).ToArray();
        db.Db.Insertable(walks).OffIdentity().ExecuteCommand();
        db.Db.Insertable(walks.Select(walk => new IReTourRawReportEntity
            { Id = walk.Id, PlanId = walk.Id }).ToArray()).OffIdentity().ExecuteCommand();
        Assert.Empty((await Read("all_tenants")).Walks);
        config.ConfirmedWalkRecords.AddRange(new[] {
            Map(2, 7, 1), Map(2, 7, 1), Map(3, 7, 2), Map(4, 7, 1), Map(4, 8, 1),
            Map(5, 7, 2), Map(6, 7, null), Map(7, 7, 1), Map(7, 7, 2)
        });
        var all = await Read("all_tenants");
        Assert.Equal(new[] { 2, 3, 6 }, all.Walks.Select(walk => walk.Id).OrderBy(id => id));
        Assert.Equal(new[] { 2, 3, 6 }, all.RawReports.Select(report => report.PlanId).OrderBy(id => id));
        var current = await Read("current_tenant");
        Assert.Equal(2, Assert.Single(current.Walks).Id);
        Assert.Equal(2, Assert.Single(current.RawReports).PlanId);
    }
}
