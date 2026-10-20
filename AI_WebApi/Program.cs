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
builder.Services.AddMemoryCache();

builder.Services.AddControllers()
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
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "AI_WebApi",
        Version = "1.0"
    });

    options.SwaggerDoc("ai-patient-tools-v1", new OpenApiInfo
    {
        Title = "AI Patient Tools API",
        Version = IReGoContract.Version,
        Description =
            "面向 Dify 的患者端只读工具。Authorization 由 Dify 工具凭据提供；" +
            "system_context 必须由 Dify 系统/运行时变量注入，不得由 LLM 推断。" +
            "session_ref 只能选择工具已经返回的稳定匿名引用，不得自行编造。" +
            "按 IReGo、IReTour、IReCog 和线上量表对象组织概览、历史、单次、多次及报表工具。" +
            "IReGo 使用独立 robot 身份；IReTour/IReCog/线上量表使用 project 身份，网关负责授权。" +
            "ReportList 元素为报告记录分段，不得表述为多个独立训练项目。报告图片以稳定 artifact_ref 持久化；" +
            "展示地址按需生成，不应写入聊天记录。"
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
        "ai-patient-tools-v1" => apiDesc.GroupName == "ai-patient-tools-v1",
        "ai-hospital-tools-v1" => apiDesc.GroupName == "ai-hospital-tools-v1",
        "ai-rehab-tools-v1" => apiDesc.GroupName == "ai-rehab-tools-v1",
        _ => apiDesc.GroupName is not (
            "ai-patient-tools-v1" or "ai-hospital-tools-v1" or
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
            "/swagger/ai-patient-tools-v1/swagger.json",
            "AI Patient Tools v1");
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
            var document = swaggerProvider.GetSwagger("ai-patient-tools-v1");
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
