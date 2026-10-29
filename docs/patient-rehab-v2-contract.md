# 患者康复工具 2.0.0 集成说明

本版按现有 `project` 数据库和 `dbbuniessuser` 中间表实现。患者工具原地址下直接替换为新契约，不并行发布旧输入、旧输出或 `/v2/tools` 路由。未执行数据库迁移、服务部署或 smoke test。

## 请求与范围

业务工具地址为 `/api/ai/patients/tools/{tool_name}`，OpenAPI 为 `/api/ai/patients/tools/openapi.json`。GET catalog 和 POST 工具继续要求服务 `Authorization: Bearer …`。除 catalog 外还须传入：

```json
{
  "system_context": {
    "user": "authenticated-subject",
    "conversation_id": "conversation-example",
    "patient_identity": {"type": "project_user_id", "value": "7"},
    "current_business_id": 1,
    "identity_context_token": "patient_identity_v2.<body>.<signature>"
  },
  "data_scope": "all_tenants"
}
```

身份类型只接受 `project_user_id` 或 `login_name`，值始终为字符串。`login_name` 保留前导零，零命中或多命中都拒绝；不允许用手机号或数字 user 作为备用身份。`user` 是已登录主体，必须与凭证的 subject 一致。

`all_tenants` 为默认值，只扩大到已确认属于本人的账号和来源；`current_tenant` 同时验证入口账号在当前机构的有效中间表关系和患者角色，并按每条记录的机构过滤。医生及代查身份不通过患者本人凭证校验。病历、会诊等其他资料仍使用入口账号，不随康复查询扩大范围。

## 业务后端签发凭证

通过环境变量配置 `PatientAccess__IdentitySigningKey`，使用独立、稳定、至少 32 字节的随机密钥；缺少配置时患者查询返回 503。示例配置见 `.env.ai_webapi.example` 和 `AI_WebApi/appsettings.example.json`。密钥仅用于 AI_WebApi 与已认证业务后端，MetaAgent/Dify 只转发短期凭证。

可直接复用 `PatientIdentityProofCodec.Issue`。跨语言签发时，body 为下列 JSON 的 UTF-8 字节经 Base64URL 编码（去除 `=`）：

```json
{
  "purpose": "patient_rehab_v2",
  "subject": "authenticated-subject",
  "anchor_user_id": 7,
  "actor_role": "patient",
  "current_business_id": 1,
  "allow_all_tenants": true,
  "phone_binding_hash": null,
  "issued_at": 1793232000,
  "expires_at": 1793232300
}
```

示例时间戳仅展示格式；签发时使用当前 Unix 秒。signature 是 `HMAC-SHA256(key, UTF8("patient_identity_v2." + body))` 的 Base64URL 编码。完整凭证为 `patient_identity_v2.body.signature`，最长有效期为配置值与 900 秒的较小值，验证允许签发时间最多领先服务端 30 秒。

业务后端确认当前手机号确实绑定该患者后，才填入 `phone_binding_hash`：对 `UTF8("phone-binding-v2|" + phone.Trim())` 计算同一密钥的 HMAC-SHA256，输出小写十六进制。未提供时不自动扩大到同号账号；提供后还会排除资料冲突、疑似测试账号及未经核实的停用账号，并返回覆盖不足原因。

历史同人账号可用 `PatientAccess:ConfirmedAccountGroups` 配置经过业务核实的账号组；不要把待核查的同号列表直接写入。此配置只影响读取范围，不修改 dbuser、外键或原始记录。

## 响应与机构标注

根封套统一 `contract_version="2.0.0"`。`patient_ref` 是当前授权范围的匿名引用，各家族使用同一引用；客户端不拼接或反推 ID。`query_scope` 返回有效范围、已确认账号数、实际记录的机构覆盖、范围摘要与覆盖问题；`reason_codes` 合并身份和来源限制。

记录的 `record_tenant` 包含匿名机构引用、医院/卫生中心名称、`attribution_status`、`attribution_basis` 与 `name_source`。计划链路按 `dbrehaplan.BusinessId → dbbuniess.Id/Name` 读取，不使用入口用户或设备当前机构覆盖历史归属。当前字典名称标为 `current_directory`；已知机构但名称缺失显示“所属机构名称暂缺”，无法确认归属显示“所属机构未确认”。

`tenant_segments` 按选中记录的时间和机构分段，保留 A→B→A 顺序；记录首末时间不代表连续治疗或住院期间。无可用单次查询引用的不完整报告位置仍携带 `record_ref` 和机构标注，该引用不授权获取完整报告。列表、分析、图表提示和报告保留记录来源机构。

`dbwalk` 没有机构字段，且本地历史 userId 的来源尚未全部确认。默认 `WalkUserIdsUseProjectNamespace=false`，不凭相同数字猜测归属。可配置业务核实的 `ConfirmedWalkRecords`（WalkId、ProjectUserId、BusinessId）；仅在已核实整个来源命名空间后才开启全局 project 命名空间开关。当前机构查询只纳入已确认记录级机构的步道记录，缺失部分明确返回覆盖不足。

## 分析与报告

所有 `generate_*` 请求必须带 `expected_source_snapshot`，由编排后端绑定同轮、同范围、同选择条件的分析 `source_snapshot`；IReGo 应先获取 `detail_level=standard`、`quality_detail=summary` 的分析。数据或范围改变时重新分析，不重用旧快照或旧 session_ref。

IReGo 纵向选择使用 `selection_mode=latest_count/recent_ordinal_range/date_range`，窗口最多 10 个报告位置；不再通过旧 selector/session_refs 控制公开请求。单次分析使用上一步发现的 session_ref。单次响应只保留 canonical `report_blocks` 和 `overview.report_block_count`，不返回 direct_training/games/assessments 分组别名；能力评价使用 basic_evaluation，不返回 patient_message 别名。

报告图片使用 `/api/ai/patients/report-artifacts/{artifact_ref}/content?access_grant=…`，授权有效期不超过 5 分钟和原身份凭证有效期。访问时重新校验账号/角色/租户绑定及范围摘要；身份授权变化后旧地址失效。下载凭证用途独立，不能用于其他患者工具。保留完整 URL 查询参数；过期后用新身份凭证重新调用报告工具取得新地址。

旧裸 patientId 控制器、旧 robot/project 身份映射返回值和旧公开图片下载动作已移除。MetaAgent 当前 main 尚需对应改造，位置及调整要求见 [MetaAgent 对接评估](metaagent-v2-adaptation-20261029.md)。

## 本次验证

使用构建、隔离 SQLite 数据和纯 MVC/控制器单元测试验证身份、范围、分页、机构标注、引用失效及报告授权。测试运行命令排除所有名称包含 Smoke 的用例：

```powershell
dotnet test AI_WebApi.Tests/AI_WebApi.Tests.csproj --no-restore --filter 'FullyQualifiedName!~Smoke'
```

本次不启动 Web 服务，不探测真实 HTTP 接口，不写入当前 project 数据库。真实调用方接入及新数据库适配分别进行。
