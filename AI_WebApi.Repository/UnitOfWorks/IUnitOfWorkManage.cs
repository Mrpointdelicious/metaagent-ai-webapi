using SqlSugar;

namespace AI_WebApi.Repository.UnitOfWorks
{
    public interface IUnitOfWorkManage
    {
        SqlSugarScope GetDbClient();
        void BeginTran();
        void CommitTran();
        void RollbackTran();
        int TranCount { get; }
    }
}
