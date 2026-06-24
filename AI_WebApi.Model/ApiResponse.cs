namespace AI_WebApi.Model
{
    public class ApiResponse
    {
        public int Status { get; set; }
        public string? Message { get; set; }
        public object? Data { get; set; }

        public static ApiResponse Success(string message = "操作成功", object? data = null)
        {
            return new ApiResponse
            {
                Status = 200,
                Message = message,
                Data = data
            };
        }

        public static ApiResponse Error(string message = "操作失败", int status = 500)
        {
            return new ApiResponse
            {
                Status = status,
                Message = message
            };
        }
    }

    public class ApiResponse<T>
    {
        public int Status { get; set; }
        public string? Message { get; set; }
        public T? Data { get; set; }

        public static ApiResponse<T> Success(T data, string message = "操作成功")
        {
            return new ApiResponse<T>
            {
                Status = 200,
                Message = message,
                Data = data
            };
        }

        public static ApiResponse<T> Error(string message = "操作失败", int status = 500)
        {
            return new ApiResponse<T>
            {
                Status = status,
                Message = message
            };
        }
    }
}
