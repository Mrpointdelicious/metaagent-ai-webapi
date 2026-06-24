using AI_WebApi.Common.Helper;
using Microsoft.Extensions.Configuration;

namespace AI_WebApi.Common.DB
{
    public class BaseDBConfig
    {
        public static (List<MutiDBOperate> allDbs, List<MutiDBOperate> slaveDbs) MutiConnectionString => MutiInitConn();

        public static (List<MutiDBOperate>, List<MutiDBOperate>) MutiInitConn()
        {
            List<MutiDBOperate> listdatabase = new List<MutiDBOperate>();

            var dbSection = AppSettings.Configuration?.GetSection("DBS");
            if (dbSection != null)
            {
                listdatabase = dbSection.Get<List<MutiDBOperate>>() ?? new List<MutiDBOperate>();
            }

            listdatabase = listdatabase.Where(i => i.Enabled).ToList();

            List<MutiDBOperate> listdatabaseSimpleDB = new List<MutiDBOperate>();
            List<MutiDBOperate> listdatabaseSlaveDB = new List<MutiDBOperate>();

            var mutiDbEnabled = AppSettings.app("MutiDBEnabled")?.ToLower() == "true";
            var mainDb = AppSettings.app("MainDB");

            if (!mutiDbEnabled)
            {
                if (listdatabase.Count == 1)
                {
                    return (listdatabase, listdatabaseSlaveDB);
                }
                else
                {
                    var dbFirst = listdatabase.FirstOrDefault(d => d.ConnId == mainDb);
                    if (dbFirst == null)
                    {
                        dbFirst = listdatabase.FirstOrDefault();
                    }
                    if (dbFirst != null)
                    {
                        listdatabaseSimpleDB.Add(dbFirst);
                    }
                    return (listdatabaseSimpleDB, listdatabaseSlaveDB);
                }
            }

            return (listdatabase, listdatabaseSlaveDB);
        }
    }

    public enum DataBaseType
    {
        MySql = 0,
        SqlServer = 1,
        Sqlite = 2,
        Oracle = 3,
        PostgreSQL = 4,
        Dm = 5,
        Kdbndp = 6,
    }

    public class MutiDBOperate
    {
        public bool Enabled { get; set; }
        public string ConnId { get; set; } = string.Empty;
        public int HitRate { get; set; }
        public string Connection { get; set; } = string.Empty;
        public DataBaseType DbType { get; set; }
    }
}
