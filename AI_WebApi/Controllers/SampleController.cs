using AI_WebApi.IServices;
using AI_WebApi.Model;
using AI_WebApi.Model.Entities;
using Microsoft.AspNetCore.Mvc;

namespace AI_WebApi.Controllers
{
    [ApiController]
    [Route("api/[controller]/[action]")]
    public class SampleController : ControllerBase
    {
        private readonly ISampleServices _sampleServices;

        public SampleController(ISampleServices sampleServices)
        {
            _sampleServices = sampleServices;
        }

        [HttpGet]
        public async Task<ApiResponse<List<SampleEntity>>> GetAll()
        {
            var list = await _sampleServices.Query();
            return ApiResponse<List<SampleEntity>>.Success(list);
        }

        [HttpGet("{id}")]
        public async Task<ApiResponse<SampleEntity>> GetById(int id)
        {
            var entity = await _sampleServices.QueryById(id);
            if (entity == null)
            {
                return ApiResponse<SampleEntity>.Error("未找到数据", 404);
            }
            return ApiResponse<SampleEntity>.Success(entity);
        }

        [HttpGet("search")]
        public async Task<ApiResponse<List<SampleEntity>>> Search([FromQuery] string name)
        {
            var list = await _sampleServices.GetByNameAsync(name);
            return ApiResponse<List<SampleEntity>>.Success(list);
        }

        [HttpGet("page")]
        public async Task<ApiResponse<PageModel<SampleEntity>>> GetPage([FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 20)
        {
            var page = await _sampleServices.QueryPage(x => !x.IsDeleted, pageIndex, pageSize, "Id desc");
            return ApiResponse<PageModel<SampleEntity>>.Success(page);
        }

        [HttpPost]
        public async Task<ApiResponse<SampleEntity>> Create([FromBody] SampleEntity entity)
        {
            var id = await _sampleServices.Add(entity);
            entity.Id = id;
            return ApiResponse<SampleEntity>.Success(entity, "创建成功");
        }

        [HttpPut]
        public async Task<ApiResponse<bool>> Update([FromBody] SampleEntity entity)
        {
            entity.UpdateTime = DateTime.Now;
            var result = await _sampleServices.Update(entity);
            return ApiResponse<bool>.Success(result, "更新成功");
        }

        [HttpDelete("{id}")]
        public async Task<ApiResponse<bool>> Delete(int id)
        {
            var result = await _sampleServices.DeleteById(id);
            return ApiResponse<bool>.Success(result, "删除成功");
        }
    }
}
