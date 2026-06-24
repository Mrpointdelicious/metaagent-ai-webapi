namespace AI_WebApi.Model
{
    public class PageModel<T>
    {
        public int page { get; set; }
        public int dataCount { get; set; }
        public int pageSize { get; set; }
        public List<T> data { get; set; }

        public PageModel()
        {
        }

        public PageModel(int pageIndex, int totalCount, int pageSize, List<T> data)
        {
            this.page = pageIndex;
            this.dataCount = totalCount;
            this.pageSize = pageSize;
            this.data = data;
        }
    }

    public class PaginationModel
    {
        public int PageIndex { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public string? OrderByFileds { get; set; }
        public string? Conditions { get; set; }
    }
}
