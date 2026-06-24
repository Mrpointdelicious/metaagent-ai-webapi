using SqlSugar;

namespace AI_WebApi.Model.Entities
{
    /// <summary>
    /// 资讯通知表
    /// </summary>
    [SugarTable("dbarticle")]
    public class DBArticle
    {
        [SugarColumn(IsNullable = false, IsPrimaryKey = true, IsIdentity = true)]
        public int Id { get; set; }

        [SugarColumn(IsNullable = true)]
        public int CreateBy { get; set; }

        [SugarColumn(IsNullable = true)]
        public int UpdateBy { get; set; }

        [SugarColumn(Length = 128, IsNullable = true)]
        public string? Title { get; set; }

        [SugarColumn(IsNullable = true)]
        public int Type { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? ImgUrl { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? Content { get; set; }

        public int Visits { get; set; }

        public int Comments { get; set; }

        public DateTime UpdateTime { get; set; }

        public DateTime CreateTime { get; set; }

        [SugarColumn(Length = 256, IsNullable = true)]
        public string? Remark { get; set; }

        [SugarColumn(IsNullable = true)]
        public int IsDeleted { get; set; }

        [SugarColumn(IsNullable = true)]
        public int Status { get; set; }
    }
}
