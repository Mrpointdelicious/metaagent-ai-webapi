using AI_WebApi.Model.DTOs.AITools;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace AI_WebApi.Swagger;

public sealed class DifyToolSecurityOperationFilter : IOperationFilter
{
    public const string SecuritySchemeId = "dify_service_bearer";

    public void Apply(
        OpenApiOperation operation,
        OperationFilterContext context)
    {
        if (context.ApiDescription.GroupName is not (
                "ai-patient-tools-v1" or "ai-hospital-tools-v1" or
                "ai-rehab-tools-v1"))
        {
            return;
        }

        if (context.ApiDescription.GroupName == "ai-hospital-tools-v1")
        {
            operation.Summary = "查询医院运营数据并按需生成整体或局部报表";
            operation.Description =
                "scope 支持 platform、institution 和 comparison；hospital_id 必须来自医院目录，" +
                "后端会使用接口11重新校验；comparison 必须且只能提交两个不同的目录ID。" +
                "section 支持 all、overview、consultation、rooms、" +
                "assessment_training、records 和 analysis。output_mode=analysis_and_report 时，" +
                "返回稳定 artifact_ref 对应的持久 HTML 与 PNG 地址；相同条件和数据复用同一报表。";
        }

        if (operation.OperationId == "search_doctors")
        {
            operation.Summary = "查询指定空间当前在线医生";
            operation.Description =
                "space_id 为空时使用服务端配置的默认空间。" +
                "上游登录凭据和查询地址只从环境变量读取，不由 Agent 提供。";
        }
        else if (operation.OperationId == "navigate_scene")
        {
            operation.Summary = "将自然语言场景指令解析为元宇宙点位指令";
            operation.Description =
                "target 使用用户原始指令；space_id 为空时使用服务端默认空间。" +
                "服务端根据当前空间版本读取点位并匹配，不允许 Agent 编造点位编号。";
        }

        if (operation.OperationId == "resolve_patient_identity")
        {
            operation.Summary = "通过手机号或当前患者ID查询统一患者身份";
            operation.Description =
                "phone 必须来自 Dify 首轮输入变量；project_patient_id 必须为当前 project.dbuser.Id。" +
                "两者只能传一个且都不得由模型猜测。按当前手机号和患者角色查询，" +
                "仅唯一命中时返回相等的 project_patient_id 和 robot_patient_id，" +
                "零命中和多命中统一返回 PATIENT_BINDING_NOT_FOUND。";
        }
        else if (operation.OperationId == "get_multisource_patient_context")
        {
            operation.Summary = "获取当前患者的 IREGO 上下文和稳定会话引用";
            operation.Description =
                "返回的 session_ref 是后端签名生成的匿名稳定引用。调用方可以选择工具返回过的引用，" +
                "但不得自行编造；患者身份必须由 Dify 运行时绑定。" +
                "患者统一使用当前 project.dbuser.Id；旧ID、旧引用和旧缓存已停用，不再可用。" +
                "recent_sessions 只返回完成状态和时间语义满足条件的会话。";
        }
        else if (operation.OperationId == "get_irego_session_analysis")
        {
            operation.Summary = "分析一份 IREGO 报告会话";
            operation.Description =
                "ReportDetails.ReportList 的每个元素是一段报告记录，不是独立康复项目。" +
                "report_item_count 兼容保留，但必须表述为“报告记录分段数”。" +
                "新增 plan、report_blocks 和 block_comparison；原 direct_training、games、" +
                "assessments、plan_report_comparison 继续保留。" +
                "已确认单位直接写入既有 metric.unit，不新增 display_value、display_unit 或 display_text。" +
                "面向患者时使用 patient_message，不直接展示内部质量原因码。";
        }
        else if (operation.OperationId == "get_irego_patient_history")
        {
            operation.Summary = "分页查询 IREGO 既往训练与计划目录";
            operation.Description =
                "只负责发现当前患者可查询的报告、未完成计划和未来计划，返回稳定 session_ref。" +
                "不在此接口中生成改善、持平或下降结论。patient_id 仍由 system_context.user 可信注入，" +
                "建议由Dify Workflow固定分页与筛选参数后再暴露给Agent。";
        }
        else if (operation.OperationId == "generate_irego_single_session_report")
        {
            operation.Summary = "生成单次 IREGO 训练报告图片";
            operation.Description =
                "只接受工具已经返回的 session_ref。完成情况与报告评价由后端版本化规则生成，" +
                "调用方提交 ai_evaluation 或 report_type 会被拒绝。" +
                "后端重新校验患者归属和完整完成状态，" +
                "按固定模板生成 PNG 并持久化到 artifact_ref；展示地址不应写入聊天记录。";
        }
        else if (operation.OperationId == "get_irego_longitudinal_analysis")
        {
            operation.Summary = "查询连续 IREGO 报告并按同项目、同分段逐指标比较";
            operation.Description =
                "只比较同一训练项目。重复的同项目分段按配置优先、原始顺序兜底建立独立轨道，" +
                "不取最佳值、平均值或加权值。中间不完整报告和数量不足的分段保留为灰色缺失位置；" +
                "可见数据点统一以实线连接，不插值；是否同条件以comparison_scope为准。" +
                "同一 GameMode 的游戏允许跨难度展示：同难度按单位时间得分趋势评价，跨难度另按难度方向、完整完成状态和得分效率评价。" +
                "至少3条同条件有效记录才生成评价；中间缺少该子项的日期只保留为空位，不切断同条件序列。后端按版本化规则选择MDC：内部探索值与外部参考差异过大时，" +
                "或内部值不可用时，采用外部MDC下限，并保留阈值来源。" +
                "评价只输出患者可懂的具体运动能力，不生成脑卒中整体病情结论。" +
                "system_context.user 必须由 Dify 运行时注入；公开 Agent 工具只支持后端解析最近连续窗口，" +
                "不接受 session_refs 或 session_refs_json。";
        }
        else if (operation.OperationId == "generate_irego_longitudinal_report")
        {
            operation.Summary = "生成连续 IREGO 报告对比图片";
            operation.Description =
                "后端按 report_count 解析最近连续报告窗口，不接受模型提交的 session_refs、" +
                "session_refs_json、report_type、指标数组、HTML 或任意趋势结论。" +
                "后端按固定模板生成 PNG 并持久化到 artifact_ref。";
        }

        KeepOnlyApplicationJson(operation);

        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = SecuritySchemeId
                    }
                }
            ] = Array.Empty<string>()
        });
    }

    private static void KeepOnlyApplicationJson(OpenApiOperation operation)
    {
        if (operation.RequestBody?.Content == null ||
            !operation.RequestBody.Content.TryGetValue(
                "application/json",
                out var applicationJson))
        {
            return;
        }

        operation.RequestBody.Content =
            new Dictionary<string, OpenApiMediaType>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["application/json"] = applicationJson
            };
    }
}

public sealed class DifySystemContextSchemaFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type == typeof(ResolvePatientIdentityRequestDto))
        {
            Annotate(
                schema,
                "project_patient_id",
                "[WORKFLOW OUTPUT ONLY] 兼容字段；必须来自旧项目身份查询结果，且不能与 phone 同时传入。",
                "workflow_output");
            Annotate(
                schema,
                "phone",
                "[DIFY INPUT ONLY] 必须来自 Dify 首轮手机号输入变量；禁止由模型推断或改写，且不能与 project_patient_id 同时传入。",
                "workflow_input");
        }

        if (context.Type == typeof(GetIReGoLongitudinalAnalysisRequestDto))
        {
            Remove(schema, "selector", "session_refs", "session_refs_json");
        }

        if (context.Type == typeof(GenerateIReGoLongitudinalReportRequestDto))
        {
            Remove(
                schema,
                "selector",
                "session_refs",
                "session_refs_json",
                "report_type");
        }

        if (context.Type == typeof(GenerateIReGoSingleSessionReportRequestDto))
        {
            Remove(schema, "ai_evaluation", "report_type");
        }

        if (context.Type == typeof(GetMultisourcePatientContextRequestDto) ||
            context.Type == typeof(GetIReGoSessionAnalysisRequestDto) ||
            context.Type == typeof(GenerateIReGoSingleSessionReportRequestDto) ||
            context.Type == typeof(GetIReGoPatientHistoryRequestDto) ||
            context.Type == typeof(GetIReGoLongitudinalAnalysisRequestDto) ||
            context.Type == typeof(GenerateIReGoLongitudinalReportRequestDto))
        {
            schema.Properties["system_context"] = BuildSystemContextSchema();
            schema.Required.Add("system_context");
            Annotate(
                schema,
                "system_context",
                "[SYSTEM ONLY] Dify 系统/运行时注入的身份绑定。禁止由 LLM 推断或覆盖。",
                "dify_runtime");
        }

        if (context.Type == typeof(GetIReGoSessionAnalysisRequestDto))
        {
            Annotate(
                schema,
                "session_ref",
                "[TOOL OUTPUT ONLY] 可由 LLM 从工具已返回的历史会话引用中选择，禁止自行编造。",
                "tool_output_or_empty");
            Describe(
                schema,
                "detail_level",
                "固定建议值 standard；返回每段报告记录的详细指标。");
            Describe(
                schema,
                "quality_detail",
                "固定建议值 summary；保留可解释的质量限制信息。");
        }

        if (context.Type == typeof(GetIReGoPatientHistoryRequestDto))
        {
            Describe(schema, "page_number", "从1开始的页码。");
            Describe(schema, "page_size", "每页1—50条，建议10条。");
            Describe(
                schema,
                "record_scope",
                "all、reports或plans；all按每个计划的当前有效记录返回一条目录项。");
            Describe(
                schema,
                "project_scope",
                "all、walking、sit_stand、games或balance。");
            Describe(
                schema,
                "training_state",
                "all、completed、not_started、not_completed、execution_unknown或voided。");
        }

        if (context.Type == typeof(IReGoHistoryItemDto))
        {
            Annotate(
                schema,
                "session_ref",
                "[TOOL OUTPUT ONLY] 报告可用时返回的稳定匿名引用；可传给单次或纵向分析，禁止编造。",
                "tool_output");
            Describe(
                schema,
                "patient_message",
                "供患者端直接说明该记录能否继续查询，内部状态代码不应直接展示。 ");
        }

        if (context.Type == typeof(GenerateIReGoSingleSessionReportRequestDto))
        {
            schema.Required.Add("session_ref");
            Annotate(
                schema,
                "session_ref",
                "[TOOL OUTPUT ONLY] 必须选择患者上下文工具已经返回的会话引用，禁止自行编造。",
                "tool_output");
        }

        if (context.Type == typeof(GetIReGoLongitudinalAnalysisRequestDto))
        {
            Describe(
                schema,
                "selection_mode",
                "受控语义选择：latest_count、recent_ordinal_range 或 date_range；不得输出会话引用数组。");
            Describe(
                schema,
                "report_count",
                "取值 2—10；后端选择最近连续报告位置，不是只取完整报告数。");
            Describe(
                schema,
                "start_ordinal",
                "仅 recent_ordinal_range 使用；最近一份报告序号为1。必须与 end_ordinal 同时提供。");
            Describe(
                schema,
                "end_ordinal",
                "仅 recent_ordinal_range 使用；与 start_ordinal 形成2—10份连续报告范围。");
            Describe(
                schema,
                "start_date",
                "仅 date_range 使用；来自患者明确日期语义，必须与 end_date 同时提供。时区建议 +08:00。");
            Describe(
                schema,
                "end_date",
                "仅 date_range 使用；后端按可信报告时间筛选，超过10份时拒绝并要求缩小范围。");
            Describe(
                schema,
                "project_scope",
                "all、walking、sit_stand、games 或 balance；只过滤返回模块，不改变同项目比较限制。");
            Describe(
                schema,
                "detail_level",
                "summary 返回图表序列；standard 还在每个已存在分段中返回数据库指标事实。");
            Describe(
                schema,
                "quality_detail",
                "none、summary 或 full；只控制质量问题细节，不改变连续窗口、缺失点或比较结果。");
        }

        if (context.Type == typeof(GenerateIReGoLongitudinalReportRequestDto))
        {
            Describe(
                schema,
                "selection_mode",
                "受控语义选择：latest_count、recent_ordinal_range 或 date_range；报告类型由当前端点固定，禁止另传 report_type。");
            Describe(
                schema,
                "report_count",
                "取值 2—10；后端选择最近连续报告位置。");
            Describe(
                schema,
                "start_ordinal",
                "仅 recent_ordinal_range 使用；最近一份报告序号为1。");
            Describe(
                schema,
                "end_ordinal",
                "仅 recent_ordinal_range 使用；连续范围最多10份。");
            Describe(
                schema,
                "start_date",
                "仅 date_range 使用；必须来自患者明确日期语义。");
            Describe(
                schema,
                "end_date",
                "仅 date_range 使用；与 start_date 同时提供。");
            Describe(
                schema,
                "project_scope",
                "all、walking、sit_stand、games 或 balance；决定图片包含的同项目模块。");
        }

        if (context.Type == typeof(IReGoLongitudinalMetricPointDto))
        {
            Describe(
                schema,
                "line_from_previous",
                "none 或 solid。缺失日期仍保留时间位置；相邻可见点统一以实线连接。是否同条件必须读取 comparison_scope，不能由线型推断。");
            Describe(
                schema,
                "comparison_scope",
                "none、same_condition、cross_difficulty、cross_configuration 或 multiple_limitations；缺失日期不会单独改变比较条件，只有训练配置或游戏难度变化才会阻断同条件评价。");
            Describe(
                schema,
                "game_difficulty",
                "数据库报告中的游戏难度；同一 GameMode 可跨难度展示，跨难度评价必须同时读取后端能力评价，不能只比较原始总分。");
        }

        if (context.Type == typeof(IReGoLongitudinalMetricSeriesDto))
        {
            Describe(
                schema,
                "schema_family",
                "报告 JSON 的结构族，不是数据库名；跨结构族时为 mixed，序列不连线比较。");
            Describe(
                schema,
                "comparison_eligible",
                "只有 metric_code、报告结构族和确认单位满足一致性门禁时为 true；否则仍返回原值，但不连线、不计算方向。");
            Describe(
                schema,
                "chart_eligible",
                "表示该序列可生成折线图；false 时由报表按各报告原值并列展示。");
            Describe(
                schema,
                "longest_same_condition_run",
                "同项目、同分段、同结构、同单位的数据点最大数量；缺失日期空位不切断序列，达到 3 才具备评价资格。");
            Describe(
                schema,
                "comparison_limitations",
                "不能连线的结构化原因，例如 schema_family_mismatch 或 unit_not_confirmed_or_mixed。");
        }

        if (context.Type == typeof(IReGoLongitudinalInterpretationDto))
        {
            Describe(
                schema,
                "verdict",
                "improved、stable、worsened、mixed 或 insufficient_data。患者文案固定为对应运动能力改善、无明显变化、表现下降或表现有好有坏。");
            Describe(
                schema,
                "capability_name",
                "患者可懂的运动能力名称，例如步行能力、步行协调能力、坐站能力或平衡能力。");
            Describe(
                schema,
                "threshold",
                "后端选择的判定阈值及来源；运动指标可使用MDC，游戏单位时间得分使用明确标记的产品规则阈值，二者不会混用。");
            Describe(
                schema,
                "basic_evaluation",
                "第一层患者文案，适合默认语音回答；与patient_message保持一致。");
            Describe(
                schema,
                "extended_explanation",
                "第二层患者文案，只在追问依据时使用；包含同条件范围、数值变化、阈值和判定理由。");
            Describe(
                schema,
                "life_hint",
                "第三层患者文案，只在追问生活影响时使用；表达可能相关性，不代表日常独立能力或脑卒中整体恢复。");
            Describe(
                schema,
                "patient_message",
                "向后兼容字段，内容等于basic_evaluation。Dify不得改写结论方向、数值或阈值。");
        }

        if (context.Type == typeof(IReGoCapabilityEvaluationDto))
        {
            Describe(
                schema,
                "basic_evaluation",
                "默认朗读的能力级基础评价；patient_message为其兼容别名。");
            Describe(
                schema,
                "extended_explanation",
                "能力级证据解释；用户追问为什么时再输出。");
            Describe(
                schema,
                "life_hint",
                "脑卒中康复场景的保守生活相关提示；不得扩展为疾病诊断、治疗效果或生活独立结论。");
            Describe(
                schema,
                "evaluation_axis",
                "评价轴；游戏可为same-difficulty结果表现或cross_difficulty_score_rate，训练模式可为设备支持或负荷变化。");
        }

        if (context.Type == typeof(IReGoMetricThresholdDto))
        {
            Describe(
                schema,
                "selection_policy",
                "默认规则：内部探索MDC与最近外部参考相差超过50%时，或无内部可用值时，选外部MDC下限；最后应用设备可观察下限。");
            Describe(
                schema,
                "is_irego_mdc",
                "当前所选阈值是否为IREGO固定协议正式MDC；外部或数据库探索值均为false。");
        }

        if (context.Type == typeof(IReGoSingleSessionAiEvaluationDto))
        {
            Describe(
                schema,
                "completion_level",
                "旧版兼容字段；后端不采用调用方提交值。完成情况由内部规则服务重新计算。");
            Describe(
                schema,
                "game_evaluations",
                "旧版兼容字段；后端不采用调用方提交值。游戏仅展示后端可核验事实，当前不输出改善/恶化结论。");
        }

        if (context.Type == typeof(IReGoGamePerformanceEvaluationDto))
        {
            Describe(
                schema,
                "report_sequence",
                "旧版兼容字段；新Workflow不提交。 ");
            Describe(
                schema,
                "performance_level",
                "旧版兼容字段；新Workflow不提交，后端忽略调用方评价。");
        }

        if (context.Type == typeof(IReGoSingleSessionEvaluationFactsDto))
        {
            Describe(
                schema,
                "completion_rate",
                "后端按可映射计划分段与报告分段的一对一匹配计算；用于单次报告展示，不是设备直接存储的完成率字段。");
            Describe(
                schema,
                "completion_level",
                "后端根据完成率生成 low、boundary 或 high；仅作为结构化展示事实，Dify不得重新计算。");
            Describe(
                schema,
                "games",
                "游戏名称、分段顺序和得分等后端事实；无有效游戏得分时为空数组。当前游戏规则不生成改善/恶化评价。");
        }

        if (context.Type == typeof(IReGoContentBlockDto))
        {
            Describe(
                schema,
                "training_mode_name",
                "面向患者的训练模式名称。步行：跟随、助力、阻力；坐站：主动、被动。优先展示本字段，不直接翻译模式代码。");
        }

        if (context.Type == typeof(IReGoCurrentPerformanceFacetDto))
        {
            Describe(
                schema,
                "patient_message",
                "当前单次训练的患者可读事实说明，不代表纵向改善、持平或恶化结论。");
        }

        if (context.Type == typeof(IReGoGameEvaluationFactDto))
        {
            Describe(
                schema,
                "report_sequence",
                "ReportList 内的原始分段顺序，用于回传绑定游戏评价；不是数据库主键。");
            Describe(schema, "display_name", "后端解析得到的中文游戏名称。");
            Describe(schema, "score", "数据库报告中的原始游戏得分数值。");
            Describe(
                schema,
                "allowed_performance_levels",
                "Dify AI 允许选择的受控表现等级枚举。");
        }

        if (context.Type == typeof(IReGoSessionOverviewDto))
        {
            Describe(
                schema,
                "report_item_count",
                "兼容字段；含义是 ReportList 中的报告记录分段数，不是训练项目数。");
            Describe(
                schema,
                "report_block_count",
                "ReportList 中的报告记录分段数。");
            Describe(
                schema,
                "report_block_label",
                "用于调用方展示计数含义，固定为“报告记录分段数”。");
            Describe(
                schema,
                "total_training_duration",
                "已确认单位为秒且语义为训练时长的指标合计；不包含 min 或未确认单位字段。");
            Describe(schema, "duration_unit", "与 total_training_duration 一致；当前为 s。");
            Describe(
                schema,
                "total_training_duration_display",
                "基于秒数生成的可读展示值，不替代原始数值。");
        }

        if (context.Type == typeof(IReGoContentBlockDto))
        {
            Describe(schema, "sequence", "ReportList 中的原始顺序，从 1 开始。");
            Describe(
                schema,
                "name",
                "兼容名称字段；填充真实游戏名或后端生成的可读报告分段名称。");
            Describe(
                schema,
                "display_name",
                "可读报告分段名称，例如“步行训练第1段”；不得据此编造项目名。");
        }

        if (context.Type == typeof(MetricFactDto))
        {
            Describe(schema, "raw_value", "源字段原始值；不会为显示而除以 60。");
            Describe(schema, "unit", "已确认单位；证据不足时为空。");
            Describe(
                schema,
                "unit_status",
                "confirmed 表示单位已确认；provisional_usable 仅限本次记录描述；" +
                "unverified/unknown 不得向患者猜测单位。");
            Describe(
                schema,
                "source_kind",
                "raw为数据库原始事实；derived为后端按已确认公式派生，二者不得混淆。");
            Describe(
                schema,
                "repair",
                "仅派生指标返回，包含公式、规则版本、输入字段和置信度；不会回写数据库。");
            Describe(
                schema,
                "evaluation_status",
                "paused开头表示该指标仍可展示，但不得用于改善、持平或下降评价。坐站平均速度固定暂停评价。");
        }

        if (context.Type == typeof(IReGoContextDomainDto))
        {
            Describe(
                schema,
                "availability_flags",
                "兼容扩展：直接回答是否有可用计划、报告、会话、未来计划、未完成计划和作废计划。");
            Describe(
                schema,
                "recent_sessions",
                "最近可用训练记录目录；ordinal=1 表示最近一次，session_ref 可传给会话分析工具。");
            Describe(
                schema,
                "session_history",
                "说明本次返回的历史窗口是否覆盖全部可用记录。");
        }

        if (context.Type == typeof(IReGoPlanListItemDto))
        {
            Describe(
                schema,
                "plan_ref",
                "后端签名生成的匿名稳定计划引用，不包含数据库主键明文。");
            Describe(
                schema,
                "completion_flag",
                "complete/not_complete/unknown；not_complete 表示 IsComplete=0，不能说成完整完成。");
            Describe(
                schema,
                "patient_message",
                "计划状态的患者可读说明；不得改写为诊断或治疗结论。");
        }

        if (context.Type == typeof(IReGoPlanDetailDto))
        {
            Describe(
                schema,
                "blocks",
                "按计划 JSON 解析的计划分段，指标继续使用既有 MetricFactDto。");
            Describe(
                schema,
                "completion_flag",
                "完整完成标志；与训练执行状态分开表达。");
        }

        if (context.Type == typeof(IReGoBlockComparisonDto))
        {
            Describe(
                schema,
                "status",
                "逐段匹配状态：matched_exact、matched_with_warning、planned_without_result、" +
                "unplanned_report_block、ambiguous_match 或 conflicting_match。");
            Describe(
                schema,
                "metric_comparisons",
                "只有语义映射、数值和 confirmed 单位均满足时 comparable 才为 true。");
        }

        if (context.Type == typeof(ToolIssueDto))
        {
            Describe(
                schema,
                "patient_message",
                "面向患者的通俗限制说明；内部 code 不应直接展示给患者。");
        }

        if (context.Type != typeof(DifySystemContextDto))
        {
            return;
        }

        schema.Required.Add("user");

        Annotate(
            schema,
            "user",
            "[SYSTEM ONLY] Dify 自动填写的数据库用户 ID；服务端据此查询患者及机构信息。",
            "dify_runtime");
        Annotate(
            schema,
            "conversation_id",
            "[SYSTEM ONLY] Dify 会话追踪标识，不作为患者身份证明。",
            "dify_runtime");
    }

    private static void Remove(OpenApiSchema schema, params string[] names)
    {
        foreach (var name in names)
        {
            schema.Properties.Remove(name);
            schema.Required.Remove(name);
        }
    }

    private static OpenApiSchema BuildSystemContextSchema() =>
        new()
        {
            Type = "object",
            AdditionalPropertiesAllowed = false,
            Required = new HashSet<string>(StringComparer.Ordinal)
            {
                "user"
            },
            Properties = new Dictionary<string, OpenApiSchema>(
                StringComparer.Ordinal)
            {
                ["user"] = new OpenApiSchema
                {
                    Type = "string",
                    Description =
                        "[SYSTEM ONLY] Dify 运行时注入的数据库患者用户 ID；" +
                        "禁止由 LLM 推断或改写。",
                    Extensions =
                    {
                        ["x-parameter-source"] =
                            new OpenApiString("dify_runtime")
                    }
                },
                ["conversation_id"] = new OpenApiSchema
                {
                    Type = "string",
                    Nullable = true,
                    Description =
                        "[SYSTEM ONLY] Dify 会话追踪标识；" +
                        "不作为患者身份凭据。",
                    Extensions =
                    {
                        ["x-parameter-source"] =
                            new OpenApiString("dify_runtime")
                    }
                }
            }
        };

    private static void Annotate(
        OpenApiSchema schema,
        string propertyName,
        string description,
        string parameterSource)
    {
        if (!schema.Properties.TryGetValue(propertyName, out var property))
        {
            return;
        }

        property.Description = description;
        property.Extensions["x-parameter-source"] =
            new OpenApiString(parameterSource);
    }

    private static void Describe(
        OpenApiSchema schema,
        string propertyName,
        string description)
    {
        if (schema.Properties.TryGetValue(propertyName, out var property))
        {
            property.Description = description;
        }
    }
}
