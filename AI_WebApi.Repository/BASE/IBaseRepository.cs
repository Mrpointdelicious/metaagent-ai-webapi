using AI_WebApi.Model;
using SqlSugar;
using System.Linq.Expressions;

namespace AI_WebApi.Repository.BASE
{
    public interface IBaseRepository<TEntity> where TEntity : class
    {
        ISqlSugarClient Db { get; }

        Task<TEntity> QueryById(object objId);
        Task<TEntity> QueryById(object objId, bool blnUseCache = false);
        Task<List<TEntity>> QueryByIDs(object[] lstIds);

        Task<int> Add(TEntity model);
        Task<int> Add(List<TEntity> listEntity);

        Task<bool> DeleteById(object id);
        Task<bool> Delete(TEntity model);
        Task<bool> DeleteByIds(object[] ids);

        Task<bool> Update(TEntity model);
        Task<bool> Update(TEntity entity, string where);
        Task<bool> Update(object operateAnonymousObjects);
        Task<bool> Update(TEntity entity, List<string> lstColumns = null, List<string> lstIgnoreColumns = null, string where = "");

        Task<List<TEntity>> Query();
        Task<List<TEntity>> Query(string where);
        Task<List<TEntity>> Query(Expression<Func<TEntity, bool>> whereExpression);
        Task<List<TEntity>> Query(Expression<Func<TEntity, bool>> whereExpression, string orderByFields);
        Task<List<TEntity>> Query(Expression<Func<TEntity, bool>> whereExpression, Expression<Func<TEntity, object>> orderByExpression, bool isAsc = true);

        Task<List<TEntity>> Query(Expression<Func<TEntity, bool>> whereExpression, int intTop, string orderByFields);
        Task<List<TEntity>> Query(string where, int intTop, string orderByFields);
        Task<List<TEntity>> QuerySql(string sql, SugarParameter[] parameters = null);
        Task<System.Data.DataTable> QueryTable(string sql, SugarParameter[] parameters = null);

        Task<List<TEntity>> Query(Expression<Func<TEntity, bool>> whereExpression, int pageIndex, int pageSize, string orderByFields);
        Task<List<TEntity>> Query(string where, int pageIndex, int pageSize, string orderByFields);

        Task<PageModel<TEntity>> QueryPage(Expression<Func<TEntity, bool>> whereExpression, int pageIndex = 1, int pageSize = 20, string orderByFields = null);

        Task<PageModel<TResult>> QueryPage<TResult>(Expression<Func<TEntity, bool>> whereExpression, Expression<Func<TEntity, TResult>> selectExpression, int pageIndex = 1, int pageSize = 20, string orderByFields = null);

        Task<List<TResult>> QueryMuch<T, T2, T3, TResult>(
            Expression<Func<T, T2, T3, object[]>> joinExpression,
            Expression<Func<T, T2, T3, TResult>> selectExpression,
            Expression<Func<T, T2, T3, bool>> whereLambda = null) where T : class, new();

        Task<List<TResult>> QueryTwo<T, T2, TResult>(
            Expression<Func<T, T2, object[]>> joinExpression,
            Expression<Func<T, T2, TResult>> selectExpression,
            Expression<Func<T, T2, bool>> whereLambda = null) where T : class, new();

        Task<PageModel<TResult>> QueryTabsPage<T, T2, TResult>(
            Expression<Func<T, T2, object[]>> joinExpression,
            Expression<Func<T, T2, TResult>> selectExpression,
            Expression<Func<T, T2, bool>> whereExpression,
            int pageIndex = 1,
            int pageSize = 20,
            string orderByFields = null);

        Task<PageModel<TResult>> QueryThreePages<T, T2, T3, TResult>(
            Expression<Func<T, T2, bool>> join1,
            Expression<Func<T, T2, T3, bool>> join2,
            Expression<Func<T, T2, T3, TResult>> selectExpression,
            Expression<Func<T, T2, T3, bool>> whereExpression,
            int pageIndex = 1,
            int pageSize = 20,
            string orderByFields = null);
    }
}
