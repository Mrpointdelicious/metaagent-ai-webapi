using AI_WebApi.Model.AITools;
using AI_WebApi.Model.DTOs.AITools;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace AI_WebApi.Swagger;

public sealed class DifyToolSecurityOperationFilter : IOperationFilter
{
    public const string SecuritySchemeId = "dify_service_bearer";
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var group = context.ApiDescription.GroupName;
        if (group is not ("ai-patient-tools-v2" or "ai-hospital-tools-v1" or "ai-rehab-tools-v1")) return;
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = SecuritySchemeId } }] = Array.Empty<string>()
        });
        foreach (var response in operation.Responses.Values)
            foreach (var key in response.Content.Keys.Where(key => key != "application/json").ToArray()) response.Content.Remove(key);
        if (operation.RequestBody != null)
            foreach (var key in operation.RequestBody.Content.Keys.Where(key => key != "application/json").ToArray()) operation.RequestBody.Content.Remove(key);

        if (group == "ai-patient-tools-v2")
        {
            var family = PatientToolFamilies.Find(operation.OperationId);
            operation.Tags = new List<OpenApiTag> { new() { Name = family?.ObjectType ?? "patient" } };
            operation.Summary = "患者工具：" + operation.OperationId;
            operation.Description = "统一契约 2.0.0。身份类型显式区分 project_user_id/login_name，登录名保留前导零。" +
                "system_context 由业务后端和 Dify 运行时注入：user 为已登录主体，identity_context_token 为绑定该主体及入口账号的短期签名凭证。" +
                "模型不得填写身份凭据、患者编号列表或租户编号。康复记录默认 data_scope=all_tenants；current_tenant 按记录所属机构过滤。" +
                "record_tenant 标注医院/卫生中心名称及归属依据，query_scope 说明实际范围和覆盖不足。" +
                "session_ref/assessment_ref 必须来自当前范围的工具结果；切换范围后重新发现引用。" +
                "只展示已接入且确认属于本人的事实，不将归属不明或未接入视为没有数据。";
            if (operation.OperationId?.StartsWith("generate_", StringComparison.Ordinal) == true)
                operation.Description += " expected_source_snapshot 由后端编排程序绑定同轮分析结果。报告图片使用短期授权 URL，过期后重新调用报告工具取新地址。";
            if (family?.ObjectType is "assessment" or "irecog")
                operation.Description += "只展示来源总分，不返回题目答案；计分规则和版本未核验时不进行临床判断或跨量表算术汇总。";
        }
        else if (group == "ai-hospital-tools-v1")
            operation.Description = "服务端动态目录复核医院 ID；comparison 只接受用户明确选择的两个不同医院。报表根据同一查询数据生成。";
        else if (operation.OperationId == "navigate_scene")
            operation.Description = "服务端解析用户原始指令，返回有序 commands；仅执行 matched 项的真实目录指令，歧义项不含可执行字符串。";
        else if (operation.OperationId == "search_doctors")
            operation.Description = "查询指定空间当前在线医生；space_id 为空时使用服务端默认空间。上游凭据不由模型提供。";
    }
}

public sealed class DifySystemContextSchemaFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type == typeof(DifySystemContextDto))
        {
            schema.Required.UnionWith(new[] { "user", "patient_identity", "identity_context_token" });
            schema.Description = "[SYSTEM ONLY] 已认证业务后端注入的患者登录上下文，模型不得生成。";
            foreach (var property in schema.Properties.Values)
                property.Extensions["x-parameter-source"] = new OpenApiString("business_backend_runtime");
            if (schema.Properties.TryGetValue("user", out var user)) { user.MaxLength = 256; user.Description = "已登录主体，必须与签名凭证 Subject 一致。"; }
            if (schema.Properties.TryGetValue("identity_context_token", out var proof)) { proof.MaxLength = 8192; proof.Description = "patient_identity_v2 签名凭证。"; }
        }
        if (context.Type == typeof(PatientIdentityInputDto))
        {
            schema.Required.UnionWith(new[] { "type", "value" });
            schema.Properties["type"].Enum = new List<IOpenApiAny> { new OpenApiString("project_user_id"), new OpenApiString("login_name") };
            schema.Properties["value"].Description = "入口账号标识的原字符串，保留登录名前导零。";
            schema.Properties["value"].MaxLength = 64;
        }
        if (schema.Properties.ContainsKey("system_context")) schema.Required.Add("system_context");
        if (schema.Properties.ContainsKey("expected_source_snapshot")) schema.Required.Add("expected_source_snapshot");
        if (schema.Properties.TryGetValue("data_scope", out var scope))
        {
            scope.Default = new OpenApiString("all_tenants");
            scope.Enum = new List<IOpenApiAny> { new OpenApiString("all_tenants"), new OpenApiString("current_tenant") };
            scope.Description = "康复记录查询范围，由后端结合登录主体和权限校验。";
        }
        foreach (var forbidden in new[] { "ai_evaluation", "report_type" })
        { schema.Properties.Remove(forbidden); schema.Required.Remove(forbidden); }
        if (context.Type == typeof(GetIReGoLongitudinalAnalysisRequestDto) || context.Type == typeof(GenerateIReGoLongitudinalReportRequestDto))
            foreach (var privateSelector in new[] { "selector", "session_refs", "session_refs_json" })
            { schema.Properties.Remove(privateSelector); schema.Required.Remove(privateSelector); }
    }
}
