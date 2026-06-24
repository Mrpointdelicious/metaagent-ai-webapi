using AI_WebApi.IServices.BASE;
using AI_WebApi.Model.Entities;

namespace AI_WebApi.IServices
{
    public interface ISampleServices : IBaseServices<SampleEntity>
    {
        Task<List<SampleEntity>> GetByNameAsync(string name);
    }
}
