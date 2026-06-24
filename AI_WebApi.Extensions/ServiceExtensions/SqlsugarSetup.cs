using AI_WebApi.Common.DB;
using AI_WebApi.Common.Helper;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;

namespace AI_WebApi.Extensions.ServiceExtensions
{
    public static class SqlsugarSetup
    {
        public static void AddSqlsugarSetup(this IServiceCollection services)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));

            MainDb.CurrentDbConnId = AppSettings.app("MainDB");

            services.AddSingleton<ISqlSugarClient>(o =>
            {
                var listConfig = new List<ConnectionConfig>();
                var listConfig_Slave = new List<SlaveConnectionConfig>();

                BaseDBConfig.MutiConnectionString.slaveDbs.ForEach(s =>
                {
                    listConfig_Slave.Add(new SlaveConnectionConfig()
                    {
                        HitRate = s.HitRate,
                        ConnectionString = s.Connection
                    });
                });

                BaseDBConfig.MutiConnectionString.allDbs.ForEach(m =>
                {
                    listConfig.Add(new ConnectionConfig()
                    {
                        ConfigId = m.ConnId.ObjToString().ToLower(),
                        ConnectionString = m.Connection,
                        DbType = (DbType)m.DbType,
                        IsAutoCloseConnection = true,
                        AopEvents = new AopEvents
                        {
                            OnLogExecuting = (sql, p) =>
                            {
                                Console.WriteLine($"SQL: {sql}");
                            }
                        },
                        MoreSettings = new ConnMoreSettings()
                        {
                            IsAutoRemoveDataCache = true
                        },
                        SlaveConnectionConfigs = listConfig_Slave,
                        ConfigureExternalServices = new ConfigureExternalServices()
                        {
                            EntityService = (property, column) =>
                            {
                                if (column.IsPrimarykey && property.PropertyType == typeof(int))
                                {
                                    column.IsIdentity = true;
                                }
                            }
                        },
                        InitKeyType = InitKeyType.Attribute
                    });
                });

                return new SqlSugarScope(listConfig);
            });
        }
    }

    public static class ObjExtend
    {
        public static string ObjToString(this object obj)
        {
            return obj?.ToString() ?? string.Empty;
        }

        public static bool ObjToBool(this object obj)
        {
            if (obj == null) return false;
            if (bool.TryParse(obj.ToString(), out bool result))
            {
                return result;
            }
            return false;
        }

        public static int ObjToInt(this object obj)
        {
            if (obj == null) return 0;
            if (int.TryParse(obj.ToString(), out int result))
            {
                return result;
            }
            return 0;
        }
    }
}
