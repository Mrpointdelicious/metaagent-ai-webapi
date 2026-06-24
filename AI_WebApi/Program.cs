/// 魏道好 2026/06/16修改
/// 添加OpenApi

using AI_WebApi.Common.Helper;
using AI_WebApi.Extensions.ServiceExtensions;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Writers;
using Swashbuckle.AspNetCore.Swagger;

var builder = WebApplication.CreateBuilder(args);

// 配置 Autofac
builder.Host
    .UseServiceProviderFactory(new AutofacServiceProviderFactory())
    .ConfigureContainer<ContainerBuilder>(builder =>
    {
        builder.RegisterModule(new AutofacModuleRegister());
    });

// 配置 AppSettings
AppSettings.Configuration = builder.Configuration;
builder.Services.AddSingleton(new AppSettings(builder.Configuration));

// 添加 SqlSugar
builder.Services.AddSqlsugarSetup();

// 添加控制器
builder.Services.AddControllers()
    .AddNewtonsoftJson(options =>
    {
        options.SerializerSettings.ReferenceLoopHandling =
            Newtonsoft.Json.ReferenceLoopHandling.Ignore;

        options.SerializerSettings.DateFormatString =
            "yyyy-MM-dd'T'HH:mm:sszzz";
    });

// 添加 Swagger
builder.Services.AddEndpointsApiExplorer();

// 多文档配置
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
        Version = "0.1.0",
        Description = "Dify / Agent 专用患者端 AI 工具接口。第一阶段只读，不允许传入 SQL、表名或字段名。"
    });

    options.DocInclusionPredicate((docName, apiDesc) =>
    {
        if (docName == "ai-patient-tools-v1")
        {
            return apiDesc.GroupName == "ai-patient-tools-v1";
        }

        return true;
    });

    options.CustomOperationIds(apiDesc =>
        apiDesc.ActionDescriptor.AttributeRouteInfo?.Name);
});

// 添加 CORS
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
    });
}

app.UseCors("CorsPolicy");

app.UseAuthorization();

app.MapGet(
    "/api/ai/patients/tools/openapi.json",
    (ISwaggerProvider swaggerProvider, HttpRequest request) =>
    {
        var document = swaggerProvider.GetSwagger(
            "ai-patient-tools-v1");

        document.Servers = new List<OpenApiServer>
        {
            new()
            {
                Url = $"{request.Scheme}://{request.Host.Value}"
            }
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
