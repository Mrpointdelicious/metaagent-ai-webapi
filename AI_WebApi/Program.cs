using AI_WebApi.Common.Helper;
using AI_WebApi.Extensions.ServiceExtensions;
using AI_WebApi.Model.AITools;
using AI_WebApi.Model.DTOs.AITools;
using AI_WebApi.Security;
using AI_WebApi.Swagger;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Writers;
using Swashbuckle.AspNetCore.Swagger;

var builder = WebApplication.CreateBuilder(args);

builder.Host
    .UseServiceProviderFactory(new AutofacServiceProviderFactory())
    .ConfigureContainer<ContainerBuilder>(container =>
    {
        container.RegisterModule(new AutofacModuleRegister());
    });

AppSettings.Configuration = builder.Configuration;
builder.Services.AddSingleton(new AppSettings(builder.Configuration));
builder.Services.AddSqlsugarSetup();
builder.Services.Configure<IReGoToolOptions>(
    builder.Configuration.GetSection(IReGoToolOptions.SectionName));
builder.Services.Configure<IReTourToolOptions>(
    builder.Configuration.GetSection(IReTourToolOptions.SectionName));
builder.Services.Configure<IReCogToolOptions>(
    builder.Configuration.GetSection(IReCogToolOptions.SectionName));
builder.Services.Configure<AssessmentToolOptions>(
    builder.Configuration.GetSection(AssessmentToolOptions.SectionName));
builder.Services.Configure<HospitalOperationsOptions>(
    builder.Configuration.GetSection(HospitalOperationsOptions.SectionName));
builder.Services.Configure<RehabToolsOptions>(
    builder.Configuration.GetSection(RehabToolsOptions.SectionName));
builder.Services.AddScoped<IDifySystemContextResolver, DifySystemContextResolver>();
builder.Services.AddScoped<IDifyServiceRequestAuthenticator,
    DifyServiceRequestAuthenticator>();
builder.Services.AddHttpClient();
builder.Services.AddHttpClient("patient-self-session", client => client.Timeout = TimeSpan.FromSeconds(4))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
builder.Services.AddScoped<AI_WebApi.Repository.AITools.IPatientSelfReportCount, AI_WebApi.Repository.AITools.PatientSelfReportCount>();
builder.Services.AddHttpClient("doctor-delegation").ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddMemoryCache();
builder.Services.Configure<PatientAccessOptions>(builder.Configuration.GetSection(PatientAccessOptions.SectionName));
builder.Services.AddScoped<PatientV2ScopeFilter>();

builder.Services.AddControllers(options => options.Conventions.Add(new PatientV2RoutingConvention()))
    .AddNewtonsoftJson(options =>
    {
        options.SerializerSettings.ReferenceLoopHandling =
            Newtonsoft.Json.ReferenceLoopHandling.Ignore;
        options.SerializerSettings.DateFormatString =
            "yyyy-MM-dd'T'HH:mm:sszzz";
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc(PatientV2RoutingConvention.GroupName, new OpenApiInfo
    {
        Title = "AI Patient Rehabilitation Tools v2", Version = "2.0.0",
        Description = "患者本人康复查询；默认 all_tenants，可选 current_tenant。身份由业务后端签发的短期凭证绑定，记录按实际所属机构标注。"
    });
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "AI_WebApi",
        Version = "1.0"
    });

    options.SwaggerDoc("ai-hospital-tools-v1", new OpenApiInfo
    {
        Title = "AI Hospital Operations Tools API",
        Version = HospitalOperationsContract.Version,
        Description =
            "面向 Dify Agent 的医院运营查询与报表接口。可查询医院范围以 directory 接口的当前返回为准，" +
            "服务端使用接口11动态目录解析并复核 hospital_id；接口12—15用于运营数据聚合。" +
            "医院对比只接受用户明确选择的两个不同目录ID，差值固定为右侧减左侧。" +
            "整体或局部报表使用稳定 artifact_ref 持久保存，相同条件与数据复用同一报表。"
    });

    options.SwaggerDoc("ai-rehab-tools-v1", new OpenApiInfo
    {
        Title = "AI Rehab Tools API",
        Version = "1.0.0",
        Description =
            "面向 Dify 的在线医生查询和元宇宙场景导航接口。" +
            "上游地址、账号和服务密钥全部由环境变量注入。"
    });

    options.AddSecurityDefinition(
        DifyToolSecurityOperationFilter.SecuritySchemeId,
        new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "opaque service token",
            Description =
                "Dify 工具服务凭据。只保存在 Dify 凭据配置和服务端密钥配置中，不进入提示词。"
    });
    options.OperationFilter<DifyToolSecurityOperationFilter>();
    options.OperationFilter<RehabToolsOperationFilter>();
    options.SchemaFilter<DifySystemContextSchemaFilter>();
    options.DocInclusionPredicate((docName, apiDesc) => docName switch
    {
        "ai-patient-tools-v2" => apiDesc.GroupName == "ai-patient-tools-v2",
        "ai-hospital-tools-v1" => apiDesc.GroupName == "ai-hospital-tools-v1",
        "ai-rehab-tools-v1" => apiDesc.GroupName == "ai-rehab-tools-v1",
        _ => apiDesc.GroupName is not (
            "ai-patient-tools-v2" or "ai-hospital-tools-v1" or
            "ai-rehab-tools-v1")
    });
    options.CustomOperationIds(apiDesc =>
        apiDesc.ActionDescriptor.AttributeRouteInfo?.Name);
});
builder.Services.AddSwaggerGenNewtonsoftSupport();

builder.Services.AddCors(options =>
{
    options.AddPolicy("CorsPolicy", policy =>
    {
        policy.AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint(
            "/swagger/v1/swagger.json",
            "AI_WebApi v1");
        options.SwaggerEndpoint(
            "/swagger/ai-patient-tools-v2/swagger.json",
            "AI Patient Tools v2");
        options.SwaggerEndpoint(
            "/swagger/ai-hospital-tools-v1/swagger.json",
            "AI Hospital Operations Tools v1");
        options.SwaggerEndpoint(
            "/swagger/ai-rehab-tools-v1/swagger.json",
            "AI Rehab Tools v1");
    });
}

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseCors("CorsPolicy");
app.UseAuthorization();

app.MapGet("/irego-tools", () => Results.Redirect("/irego-tools/index.html"))
    .ExcludeFromDescription();

app.MapGet(
        "/api/ai/patients/tools/openapi.json",
        (ISwaggerProvider swaggerProvider, HttpRequest request) =>
        {
            var document = swaggerProvider.GetSwagger("ai-patient-tools-v2");
            document.Servers = new List<OpenApiServer>
            {
                new() { Url = $"{request.Scheme}://{request.Host.Value}" }
            };

            using var stringWriter = new StringWriter();
            var writer = new OpenApiJsonWriter(stringWriter);
            document.SerializeAsV3(writer);
            return Results.Text(
                stringWriter.ToString(),
                "application/json; charset=utf-8");
        })
    .ExcludeFromDescription();

app.MapGet(
        "/api/ai/rehab/tools/openapi.json",
        (ISwaggerProvider swaggerProvider, HttpRequest request) =>
        {
            var document = swaggerProvider.GetSwagger("ai-rehab-tools-v1");
            document.Servers = new List<OpenApiServer>
            {
                new() { Url = $"{request.Scheme}://{request.Host.Value}" }
            };
            using var stringWriter = new StringWriter();
            var writer = new OpenApiJsonWriter(stringWriter);
            document.SerializeAsV3(writer);
            return Results.Text(
                stringWriter.ToString(),
                "application/json; charset=utf-8");
        })
    .ExcludeFromDescription();

app.MapGet(
        "/api/ai/hospital-operations/tools/openapi.json",
        (ISwaggerProvider swaggerProvider, HttpRequest request) =>
        {
            var document = swaggerProvider.GetSwagger("ai-hospital-tools-v1");
            document.Servers = new List<OpenApiServer>
            {
                new() { Url = $"{request.Scheme}://{request.Host.Value}" }
            };

            using var stringWriter = new StringWriter();
            var writer = new OpenApiJsonWriter(stringWriter);
            document.SerializeAsV3(writer);
            return Results.Text(
                stringWriter.ToString(),
                "application/json; charset=utf-8");
        })
    .ExcludeFromDescription();

app.MapControllers();
app.Run();

public partial class Program
{
}
