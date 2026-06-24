using AI_WebApi.IServices;
using AI_WebApi.Model.Entities;
using AI_WebApi.Repository.BASE;
using AI_WebApi.Services.BASE;

namespace AI_WebApi.Services
{
    public class SampleServices : BaseServices<SampleEntity>, ISampleServices
    {
        public SampleServices(IBaseRepository<SampleEntity> baseDal) : base(baseDal)
        {
        }

        public async Task<List<SampleEntity>> GetByNameAsync(string name)
        {
            return await BaseDal.Query(x => x.Name != null && x.Name.Contains(name));
        }
    }
}
