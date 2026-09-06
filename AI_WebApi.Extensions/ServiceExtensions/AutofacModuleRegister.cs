using Autofac;
using AI_WebApi.Repository.BASE;
using AI_WebApi.Repository.UnitOfWorks;
using AI_WebApi.IServices.BASE;
using AI_WebApi.Services.BASE;
using AI_WebApi.IServices.AITools;
using AI_WebApi.Services.AITools.IReGo;
using AI_WebApi.Services.AITools.HospitalOperations;
using System.Reflection;

namespace AI_WebApi.Extensions.ServiceExtensions
{
    public class AutofacModuleRegister : Autofac.Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            var basePath = AppContext.BaseDirectory;

            var servicesDllFile = Path.Combine(basePath, "AI_WebApi.Services.dll");
            var repositoryDllFile = Path.Combine(basePath, "AI_WebApi.Repository.dll");

            if (!(File.Exists(servicesDllFile) && File.Exists(repositoryDllFile)))
            {
                var msg = "Repository.dll和service.dll 丢失，因为项目解耦了，所以需要先F6编译，再F5运行，请检查 bin 文件夹，并拷贝。";
                throw new Exception(msg);
            }

            builder.RegisterGeneric(typeof(BaseRepository<>)).As(typeof(IBaseRepository<>)).InstancePerDependency();
            builder.RegisterGeneric(typeof(BaseServices<>)).As(typeof(IBaseServices<>)).InstancePerDependency();

            var assemblysServices = Assembly.LoadFrom(servicesDllFile);
            builder.RegisterAssemblyTypes(assemblysServices)
                .AsImplementedInterfaces()
                .InstancePerDependency()
                .PropertiesAutowired();

            var assemblysRepository = Assembly.LoadFrom(repositoryDllFile);
            builder.RegisterAssemblyTypes(assemblysRepository)
                .AsImplementedInterfaces()
                .PropertiesAutowired()
                .InstancePerDependency();

            // Snapshot/session bindings are optional process-local performance caches.
            // Stable session references are HMAC-derived and are re-resolved from
            // current patient sessions, so correctness does not depend on this cache.
            builder.RegisterType<InMemoryPatientContextSnapshotCache>()
                .As<IPatientContextSnapshotCache>()
                .SingleInstance();
            builder.RegisterType<FileSystemIReGoReportArtifactStore>()
                .As<IIReGoReportArtifactStore>()
                .SingleInstance();
            builder.RegisterType<PlaywrightIReGoReportImageRenderer>()
                .As<IIReGoReportImageRenderer>()
                .SingleInstance();
            builder.RegisterType<HospitalOperationsArtifactIdentityProvider>()
                .As<IHospitalOperationsArtifactIdentityProvider>()
                .SingleInstance();
            builder.RegisterType<FileSystemHospitalOperationsArtifactStore>()
                .As<IHospitalOperationsArtifactStore>()
                .SingleInstance();
            builder.RegisterType<HospitalOperationsUpstreamClient>()
                .As<IHospitalOperationsUpstreamClient>()
                .SingleInstance();

            builder.RegisterType<UnitOfWorkManage>().As<IUnitOfWorkManage>()
                .AsImplementedInterfaces()
                .InstancePerLifetimeScope()
                .PropertiesAutowired();
        }
    }
}
