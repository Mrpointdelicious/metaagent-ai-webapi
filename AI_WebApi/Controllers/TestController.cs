using AI_WebApi.Model;
using Microsoft.AspNetCore.Mvc;
using SqlSugar;

namespace AI_WebApi.Controllers
{
    [ApiController]
    [Route("api/[controller]/[action]")]
    public class TestController : ControllerBase
    {
        private readonly ISqlSugarClient _db;

        public TestController(ISqlSugarClient db)
        {
            _db = db;
        }

        /// <summary>
        /// 测试数据库连接 - 执行简单查询
        /// </summary>
        [HttpGet]
        public async Task<ApiResponse<string>> Ping()
        {
            try
            {
                var result = await _db.Ado.SqlQueryAsync<string>("SELECT 'Hello from MySQL!' AS Message");
                return ApiResponse<string>.Success(result.FirstOrDefault() ?? "OK", "数据库连接成功");
            }
            catch (Exception ex)
            {
                return ApiResponse<string>.Error($"数据库连接失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 测试数据库连接 - 查询当前数据库信息
        /// </summary>
        [HttpGet("info")]
        public async Task<ApiResponse<object>> GetDbInfo()
        {
            try
            {
                var dbName = await _db.Ado.SqlQueryAsync<string>("SELECT DATABASE()");
                var version = await _db.Ado.SqlQueryAsync<string>("SELECT VERSION()");
                var tables = await _db.Ado.SqlQueryAsync<string>("SHOW TABLES");

                var info = new
                {
                    Database = dbName.FirstOrDefault(),
                    Version = version.FirstOrDefault(),
                    TableCount = tables.Count,
                    Tables = tables.Take(20).ToList()
                };

                return ApiResponse<object>.Success(info, "数据库信息获取成功");
            }
            catch (Exception ex)
            {
                return ApiResponse<object>.Error($"获取信息失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 测试数据库连接 - 查询表数据
        /// </summary>
        [HttpGet("query")]
        public async Task<ApiResponse<object>> QueryTest([FromQuery] string sql = "SELECT 1 + 1 AS Result")
        {
            try
            {
                var result = await _db.Ado.SqlQueryAsync<object>(sql);
                return ApiResponse<object>.Success(result, "查询成功");
            }
            catch (Exception ex)
            {
                return ApiResponse<object>.Error($"查询失败: {ex.Message}");
            }
        }
    }
}
