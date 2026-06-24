using AI_WebApi.IServices.BASE;
using AI_WebApi.Model;
using AI_WebApi.Repository.BASE;
using SqlSugar;
using System.Data;
using System.Linq.Expressions;

namespace AI_WebApi.Services.BASE
{
    public class BaseServices<TEntity> : IBaseServices<TEntity> where TEntity : class, new()
    {
        public BaseServices(IBaseRepository<TEntity> baseDal = null)
        {
            this.BaseDal = baseDal;
        }

        public IBaseRepository<TEntity> BaseDal { get; set; }

        public async Task<TEntity> QueryById(object objId)
        {
            return await BaseDal.QueryById(objId);
        }

        public async Task<TEntity> QueryById(object objId, bool blnUseCache = false)
        {
            return await BaseDal.QueryById(objId, blnUseCache);
        }

        public async Task<List<TEntity>> QueryByIDs(object[] lstIds)
        {
            return await BaseDal.QueryByIDs(lstIds);
        }

        public async Task<int> Add(TEntity entity)
        {
            return await BaseDal.Add(entity);
        }

        public async Task<int> Add(List<TEntity> listEntity)
        {
            return await BaseDal.Add(listEntity);
        }

        public async Task<bool> Update(TEntity entity)
        {
            return await BaseDal.Update(entity);
        }

        public async Task<bool> Update(TEntity entity, string where)
        {
            return await BaseDal.Update(entity, where);
        }

        public async Task<bool> Update(object operateAnonymousObjects)
        {
            return await BaseDal.Update(operateAnonymousObjects);
        }

        public async Task<bool> Update(TEntity entity, List<string> lstColumns = null, List<string> lstIgnoreColumns = null, string where = "")
        {
            return await BaseDal.Update(entity, lstColumns, lstIgnoreColumns, where);
        }

        public async Task<bool> Delete(TEntity entity)
        {
            return await BaseDal.Delete(entity);
        }

        public async Task<bool> DeleteById(object id)
        {
            return await BaseDal.DeleteById(id);
        }

        public async Task<bool> DeleteByIds(object[] ids)
        {
            return await BaseDal.DeleteByIds(ids);
        }

        public async Task<List<TEntity>> Query()
        {
            return await BaseDal.Query();
        }

        public async Task<List<TEntity>> Query(string where)
        {
            return await BaseDal.Query(where);
        }

        public async Task<List<TEntity>> Query(Expression<Func<TEntity, bool>> whereExpression)
        {
            return await BaseDal.Query(whereExpression);
        }

        public async Task<List<TEntity>> Query(Expression<Func<TEntity, bool>> whereExpression, string orderByFields)
        {
            return await BaseDal.Query(whereExpression, orderByFields);
        }

        public async Task<List<TEntity>> Query(Expression<Func<TEntity, bool>> whereExpression, Expression<Func<TEntity, object>> orderByExpression, bool isAsc = true)
        {
            return await BaseDal.Query(whereExpression, orderByExpression, isAsc);
        }

        public async Task<List<TEntity>> Query(Expression<Func<TEntity, bool>> whereExpression, int top, string orderByFields)
        {
            return await BaseDal.Query(whereExpression, top, orderByFields);
        }

        public async Task<List<TEntity>> Query(string where, int top, string orderByFields)
        {
            return await BaseDal.Query(where, top, orderByFields);
        }

        public async Task<List<TEntity>> QuerySql(string sql, SugarParameter[] parameters = null)
        {
            return await BaseDal.QuerySql(sql, parameters);
        }

        public async Task<DataTable> QueryTable(string sql, SugarParameter[] parameters = null)
        {
            return await BaseDal.QueryTable(sql, parameters);
        }

        public async Task<List<TEntity>> Query(Expression<Func<TEntity, bool>> whereExpression, int pageIndex, int pageSize, string orderByFields)
        {
            return await BaseDal.Query(whereExpression, pageIndex, pageSize, orderByFields);
        }

        public async Task<List<TEntity>> Query(string where, int pageIndex, int pageSize, string orderByFields)
        {
            return await BaseDal.Query(where, pageIndex, pageSize, orderByFields);
        }

        public async Task<PageModel<TEntity>> QueryPage(Expression<Func<TEntity, bool>> whereExpression, int pageIndex = 1, int pageSize = 20, string orderByFields = null)
        {
            return await BaseDal.QueryPage(whereExpression, pageIndex, pageSize, orderByFields);
        }

        public async Task<PageModel<TResult>> QueryPage<TResult>(Expression<Func<TEntity, bool>> whereExpression, Expression<Func<TEntity, TResult>> selectExpression, int pageIndex = 1, int pageSize = 20, string orderByFields = null)
        {
            return await BaseDal.QueryPage<TResult>(whereExpression, selectExpression, pageIndex, pageSize, orderByFields);
        }

        public async Task<List<TResult>> QueryMuch<T, T2, T3, TResult>(Expression<Func<T, T2, T3, object[]>> joinExpression, Expression<Func<T, T2, T3, TResult>> selectExpression, Expression<Func<T, T2, T3, bool>> whereLambda = null) where T : class, new()
        {
            return await BaseDal.QueryMuch(joinExpression, selectExpression, whereLambda);
        }

        public async Task<List<TResult>> QueryTwo<T, T2, TResult>(Expression<Func<T, T2, object[]>> joinExpression, Expression<Func<T, T2, TResult>> selectExpression, Expression<Func<T, T2, bool>> whereLambda = null) where T : class, new()
        {
            return await BaseDal.QueryTwo(joinExpression, selectExpression, whereLambda);
        }

        public async Task<PageModel<TEntity>> QueryPage(PaginationModel pagination)
        {
            return await BaseDal.QueryPage(null, pagination.PageIndex, pagination.PageSize, pagination.OrderByFileds);
        }

        public async Task<PageModel<TResult>> QueryTabsPage<T, T2, TResult>(Expression<Func<T, T2, object[]>> joinExpression, Expression<Func<T, T2, TResult>> selectExpression, Expression<Func<T, T2, bool>> whereExpression, int pageIndex = 1, int pageSize = 20, string orderByFields = null) where T : class, new()
        {
            return await BaseDal.QueryTabsPage(joinExpression, selectExpression, whereExpression, pageIndex, pageSize, orderByFields);
        }

        public async Task<PageModel<TResult>> QueryThreePages<T, T2, T3, TResult>(
            Expression<Func<T, T2, bool>> join1,
            Expression<Func<T, T2, T3, bool>> join2,
            Expression<Func<T, T2, T3, TResult>> selectExpression,
            Expression<Func<T, T2, T3, bool>> whereExpression,
            int pageIndex = 1,
            int pageSize = 20,
            string orderByFields = null) where T : class, new()
        {
            return await BaseDal.QueryThreePages(join1, join2, selectExpression, whereExpression, pageIndex, pageSize, orderByFields);
        }
    }
}
