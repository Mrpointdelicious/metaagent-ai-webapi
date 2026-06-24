using AI_WebApi.Common.DB;
using AI_WebApi.Model;
using AI_WebApi.Repository.UnitOfWorks;
using SqlSugar;
using System.Data;
using System.Linq.Expressions;
using System.Reflection;

namespace AI_WebApi.Repository.BASE
{
    public class BaseRepository<TEntity> : IBaseRepository<TEntity> where TEntity : class, new()
    {
        private readonly IUnitOfWorkManage _unitOfWorkManage;
        private readonly SqlSugarScope _dbBase;

        private ISqlSugarClient _db
        {
            get
            {
                ISqlSugarClient db = _dbBase;
                var mutiDbEnabled = Common.Helper.AppSettings.app("MutiDBEnabled")?.ToLower() == "true";
                if (mutiDbEnabled)
                {
                    var tenantAttr = typeof(TEntity).GetCustomAttribute<TenantAttribute>();
                    if (tenantAttr != null)
                    {
                        db = _dbBase.GetConnectionScope(tenantAttr.configId.ToString().ToLower());
                    }
                }
                return db;
            }
        }

        public ISqlSugarClient Db => _db;

        public BaseRepository(IUnitOfWorkManage unitOfWorkManage)
        {
            _unitOfWorkManage = unitOfWorkManage;
            _dbBase = unitOfWorkManage.GetDbClient();
        }

        public async Task<TEntity> QueryById(object objId)
        {
            return await _db.Queryable<TEntity>().In(objId).SingleAsync();
        }

        public async Task<TEntity> QueryById(object objId, bool blnUseCache = false)
        {
            return await _db.Queryable<TEntity>().WithCacheIF(blnUseCache, 10).In(objId).SingleAsync();
        }

        public async Task<List<TEntity>> QueryByIDs(object[] lstIds)
        {
            return await _db.Queryable<TEntity>().In(lstIds).ToListAsync();
        }

        public async Task<int> Add(TEntity entity)
        {
            return await _db.Insertable(entity).ExecuteReturnIdentityAsync();
        }

        public async Task<int> Add(List<TEntity> listEntity)
        {
            return await _db.Insertable(listEntity.ToArray()).ExecuteCommandAsync();
        }

        public async Task<bool> Update(TEntity entity)
        {
            return await _db.Updateable(entity).ExecuteCommandHasChangeAsync();
        }

        public async Task<bool> Update(TEntity entity, string where)
        {
            return await _db.Updateable(entity).Where(where).ExecuteCommandHasChangeAsync();
        }

        public async Task<bool> Update(object operateAnonymousObjects)
        {
            return await _db.Updateable<TEntity>(operateAnonymousObjects).ExecuteCommandAsync() > 0;
        }

        public async Task<bool> Update(TEntity entity, List<string> lstColumns = null, List<string> lstIgnoreColumns = null, string where = "")
        {
            IUpdateable<TEntity> up = _db.Updateable(entity);
            if (lstIgnoreColumns != null && lstIgnoreColumns.Count > 0)
            {
                up = up.IgnoreColumns(lstIgnoreColumns.ToArray());
            }
            if (lstColumns != null && lstColumns.Count > 0)
            {
                up = up.UpdateColumns(lstColumns.ToArray());
            }
            if (!string.IsNullOrEmpty(where))
            {
                up = up.Where(where);
            }
            return await up.ExecuteCommandHasChangeAsync();
        }

        public async Task<bool> Delete(TEntity entity)
        {
            return await _db.Deleteable(entity).ExecuteCommandHasChangeAsync();
        }

        public async Task<bool> DeleteById(object id)
        {
            return await _db.Deleteable<TEntity>().In(id).ExecuteCommandHasChangeAsync();
        }

        public async Task<bool> DeleteByIds(object[] ids)
        {
            return await _db.Deleteable<TEntity>().In(ids).ExecuteCommandHasChangeAsync();
        }

        public async Task<List<TEntity>> Query()
        {
            return await _db.Queryable<TEntity>().ToListAsync();
        }

        public async Task<List<TEntity>> Query(string where)
        {
            return await _db.Queryable<TEntity>().WhereIF(!string.IsNullOrEmpty(where), where).ToListAsync();
        }

        public async Task<List<TEntity>> Query(Expression<Func<TEntity, bool>> whereExpression)
        {
            return await _db.Queryable<TEntity>().WhereIF(whereExpression != null, whereExpression).ToListAsync();
        }

        public async Task<List<TEntity>> Query(Expression<Func<TEntity, bool>> whereExpression, string orderByFields)
        {
            return await _db.Queryable<TEntity>().WhereIF(whereExpression != null, whereExpression).OrderByIF(!string.IsNullOrEmpty(orderByFields), orderByFields).ToListAsync();
        }

        public async Task<List<TEntity>> Query(Expression<Func<TEntity, bool>> whereExpression, Expression<Func<TEntity, object>> orderByExpression, bool isAsc = true)
        {
            return await _db.Queryable<TEntity>().OrderByIF(orderByExpression != null, orderByExpression, isAsc ? OrderByType.Asc : OrderByType.Desc).WhereIF(whereExpression != null, whereExpression).ToListAsync();
        }

        public async Task<List<TEntity>> Query(Expression<Func<TEntity, bool>> whereExpression, int intTop, string orderByFields)
        {
            return await _db.Queryable<TEntity>().OrderByIF(!string.IsNullOrEmpty(orderByFields), orderByFields).WhereIF(whereExpression != null, whereExpression).Take(intTop).ToListAsync();
        }

        public async Task<List<TEntity>> Query(string where, int intTop, string orderByFields)
        {
            return await _db.Queryable<TEntity>().OrderByIF(!string.IsNullOrEmpty(orderByFields), orderByFields).WhereIF(!string.IsNullOrEmpty(where), where).Take(intTop).ToListAsync();
        }

        public async Task<List<TEntity>> QuerySql(string sql, SugarParameter[] parameters = null)
        {
            return await _db.Ado.SqlQueryAsync<TEntity>(sql, parameters);
        }

        public async Task<DataTable> QueryTable(string sql, SugarParameter[] parameters = null)
        {
            return await _db.Ado.GetDataTableAsync(sql, parameters);
        }

        public async Task<List<TEntity>> Query(Expression<Func<TEntity, bool>> whereExpression, int pageIndex, int pageSize, string orderByFields)
        {
            return await _db.Queryable<TEntity>().OrderByIF(!string.IsNullOrEmpty(orderByFields), orderByFields)
                .WhereIF(whereExpression != null, whereExpression).ToPageListAsync(pageIndex, pageSize);
        }

        public async Task<List<TEntity>> Query(string where, int pageIndex, int pageSize, string orderByFields)
        {
            return await _db.Queryable<TEntity>().OrderByIF(!string.IsNullOrEmpty(orderByFields), orderByFields)
                .WhereIF(!string.IsNullOrEmpty(where), where).ToPageListAsync(pageIndex, pageSize);
        }

        public async Task<PageModel<TEntity>> QueryPage(Expression<Func<TEntity, bool>> whereExpression, int pageIndex = 1, int pageSize = 20, string orderByFields = null)
        {
            RefAsync<int> totalCount = new();
            var list = await _db.Queryable<TEntity>()
                .WhereIF(whereExpression != null, whereExpression)
                .OrderByIF(!string.IsNullOrEmpty(orderByFields), orderByFields)
                .ToPageListAsync(pageIndex, pageSize, totalCount);

            return new PageModel<TEntity>(pageIndex, totalCount, pageSize, list);
        }

        public async Task<PageModel<TResult>> QueryPage<TResult>(Expression<Func<TEntity, bool>> whereExpression, Expression<Func<TEntity, TResult>> selectExpression, int pageIndex = 1, int pageSize = 20, string orderByFields = null)
        {
            RefAsync<int> totalCount = 0;
            var list = await _db.Queryable<TEntity>()
                .WhereIF(whereExpression != null, whereExpression)
                .OrderByIF(!string.IsNullOrEmpty(orderByFields), orderByFields)
                .Select(selectExpression)
                .ToPageListAsync(pageIndex, pageSize, totalCount);

            return new PageModel<TResult>(pageIndex, totalCount, pageSize, list);
        }

        public async Task<List<TResult>> QueryMuch<T, T2, T3, TResult>(
            Expression<Func<T, T2, T3, object[]>> joinExpression,
            Expression<Func<T, T2, T3, TResult>> selectExpression,
            Expression<Func<T, T2, T3, bool>> whereLambda = null) where T : class, new()
        {
            if (whereLambda == null)
            {
                return await _db.Queryable(joinExpression).Select(selectExpression).ToListAsync();
            }
            return await _db.Queryable(joinExpression).Where(whereLambda).Select(selectExpression).ToListAsync();
        }

        public async Task<List<TResult>> QueryTwo<T, T2, TResult>(
            Expression<Func<T, T2, object[]>> joinExpression,
            Expression<Func<T, T2, TResult>> selectExpression,
            Expression<Func<T, T2, bool>> whereLambda = null) where T : class, new()
        {
            if (whereLambda == null)
            {
                return await _db.Queryable(joinExpression).Select(selectExpression).ToListAsync();
            }
            return await _db.Queryable(joinExpression).Where(whereLambda).Select(selectExpression).ToListAsync();
        }

        public async Task<PageModel<TResult>> QueryTabsPage<T, T2, TResult>(
            Expression<Func<T, T2, object[]>> joinExpression,
            Expression<Func<T, T2, TResult>> selectExpression,
            Expression<Func<T, T2, bool>> whereExpression,
            int pageIndex = 1,
            int pageSize = 20,
            string orderByFields = null)
        {
            RefAsync<int> totalCount = 0;
            var list = await _db.Queryable<T, T2>(joinExpression)
                .Where(whereExpression)
                .Select(selectExpression)
                .OrderByIF(!string.IsNullOrEmpty(orderByFields), orderByFields)
                .ToPageListAsync(pageIndex, pageSize, totalCount);
            return new PageModel<TResult>(pageIndex, totalCount, pageSize, list);
        }

        public async Task<PageModel<TResult>> QueryThreePages<T, T2, T3, TResult>(
            Expression<Func<T, T2, bool>> join1,
            Expression<Func<T, T2, T3, bool>> join2,
            Expression<Func<T, T2, T3, TResult>> selectExpression,
            Expression<Func<T, T2, T3, bool>> whereExpression,
            int pageIndex = 1,
            int pageSize = 20,
            string orderByFields = null)
        {
            RefAsync<int> totalCount = 0;
            var list = await _db.Queryable<T>().LeftJoin(join1).LeftJoin(join2)
                .Where(whereExpression)
                .Select(selectExpression)
                .OrderByIF(!string.IsNullOrEmpty(orderByFields), orderByFields)
                .ToPageListAsync(pageIndex, pageSize, totalCount);
            return new PageModel<TResult>(pageIndex, totalCount, pageSize, list);
        }
    }
}
