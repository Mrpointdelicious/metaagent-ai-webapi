using Microsoft.Extensions.Configuration;

namespace AI_WebApi.Common.Helper
{
    public class AppSettings
    {
        private readonly IConfiguration _configuration;

        public AppSettings(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public static IConfiguration Configuration { get; set; }

        public static string app(params string[] sections)
        {
            try
            {
                var val = string.Empty;
                for (int i = 0; i < sections.Length; i++)
                {
                    val += sections[i];
                    if (i < sections.Length - 1)
                    {
                        val += ":";
                    }
                }
                return Configuration[val];
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
    }
}
