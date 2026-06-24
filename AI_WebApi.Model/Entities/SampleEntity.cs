using SqlSugar;

namespace AI_WebApi.Model.Entities
{
    [SugarTable("sample")]
    public class SampleEntity
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public int Id { get; set; }

        [SugarColumn(Length = 100)]
        public string? Name { get; set; }

        [SugarColumn(Length = 500)]
        public string? Description { get; set; }

        public DateTime CreateTime { get; set; } = DateTime.Now;

        public DateTime? UpdateTime { get; set; }

        public bool IsDeleted { get; set; } = false;
    }
}
