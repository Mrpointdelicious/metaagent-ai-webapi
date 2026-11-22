# 患者康复工具 2.0.0 集成说明

2026-11-03 增量：医生 Sandbox 通过独立的医生凭证与逐次医患关系回查接入 current_tenant，仍不接受用患者本人凭证冒充医生授权。见 [医生代查授权](doctor-delegation-v2-20261103.md)；本说明其余部分继续描述患者本人协议。

2026-11-22 全局账号适配：本人范围固定为 `project.dbuser.Id` 的一条有效全局账号（`BusinessId=0, Status=1, IsDeleted=0`），须有有效患者（`RoleId=0`）或学生（`RoleId=10`）角色。租户关系读 `dbusertenant`。工具地址和响应版本不变，不需要执行数据库迁移；旧范围的会话引用、缓存和图片授权需重新取得。

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

`all_tenants` 为本人默认范围，须凭证允许；不要求每条历史记录的租户仍有成员关系。病历按 `dbmedical.UserId`，处方经本人的有效病历，计划按 `dbrehaplan.UserId`，报告经已授权计划的 `dbreport.planId` 读取。`current_tenant` 必须同时有当前机构的有效 `dbusertenant` 与患者/学生角色（两者均 `Enable=0`），再按病历或计划本身的机构过滤。医生只能使用独立委托、明确指定 `current_tenant`；缺机构、缺关系或委托撤销都不放行。

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
  "issued_at": 1793232000,
  "expires_at": 1793232300
}
```

示例时间戳仅展示格式；签发时使用当前 Unix 秒。signature 是 `HMAC-SHA256(key, UTF8("patient_identity_v2." + body))` 的 Base64URL 编码。完整凭证为 `patient_identity_v2.body.signature`，最长有效期为配置值与 900 秒的较小值，验证允许签发时间最多领先服务端 30 秒。

本人凭证的 `actor_role` 接受 `patient` 或 `student`。沿用 `patient` 本人凭证的纯学生账号也能查询；显式 `student` 须在所选范围有有效学生角色。二者都没有代查权限。登录名只用于定位签名锚点；旧本人凭证的 `phone_binding_hash` 不参与授权，旧 `ConfirmedAccountGroups` 配置也不生效。同手机号、姓名、证件或旧 ID 均不会扩展到另一账号。

## 响应与机构标注

根封套统一 `contract_version="2.0.0"`。`patient_ref` 是当前授权范围的匿名引用，各家族使用同一引用；客户端不拼接或反推 ID。`query_scope` 返回有效范围、已确认账号数、实际记录的机构覆盖、范围摘要与覆盖问题；`reason_codes` 合并身份和来源限制。

记录的 `record_tenant` 包含匿名机构引用、医院/卫生中心名称、`attribution_status`、`attribution_basis` 与 `name_source`。计划链路按 `dbrehaplan.BusinessId → dbbuniess.Id/Name` 读取，不使用入口用户或设备当前机构覆盖历史归属。当前字典名称标为 `current_directory`；已知机构但名称缺失显示“所属机构名称暂缺”，无法确认归属显示“所属机构未确认”。

`get_patient_rehab` 的 `report_count`、`reports`、`report_page` 是独立于计划分页的原始报告目录：共用请求的页码和页大小，以报告创建时间、主键倒序。只列出可关联授权计划的报告，含匿名 `record_ref`、计划 ID、时间、机构；不暴露原始报告 JSON。已有设备/量表工具继续负责可解读报告的详情。一份计划可能产生多份报告，目录计数不等于已完成训练次数。

`tenant_segments` 按选中记录的时间和机构分段，保留 A→B→A 顺序；记录首末时间不代表连续治疗或住院期间。无可用单次查询引用的不完整报告位置仍携带 `record_ref` 和机构标注，该引用不授权获取完整报告。列表、分析、图表提示和报告保留记录来源机构。

`dbwalk.BusinessId` 可以标注机构，但不能证明患者身份。步道仅接受逐条核实的 `PatientAccess:ConfirmedWalkRecords`（WalkId、ProjectUserId、BusinessId）；同一条映射有冲突即排除，当前机构视图还须有该机构的明确映射。旧 `WalkUserIdsUseProjectNamespace` 开关不再生效。微信来源没有已验证的账号映射，暂不读取；不把未覆盖解释为没有记录。

## 分析与报告

所有 `generate_*` 请求必须带 `expected_source_snapshot`，由编排后端绑定同轮、同范围、同选择条件的分析 `source_snapshot`；IReGo 应先获取 `detail_level=standard`、`quality_detail=summary` 的分析。数据或范围改变时重新分析，不重用旧快照或旧 session_ref。

IReGo 纵向选择使用 `selection_mode=latest_count/recent_ordinal_range/date_range`，窗口最多 10 个报告位置；不再通过旧 selector/session_refs 控制公开请求。单次分析使用上一步发现的 session_ref。单次响应只保留 canonical `report_blocks` 和 `overview.report_block_count`，不返回 direct_training/games/assessments 分组别名；能力评价使用 basic_evaluation，不返回 patient_message 别名。

报告图片使用 `/api/ai/patients/report-artifacts/{artifact_ref}/content?access_grant=…`，授权有效期不超过 5 分钟和原身份凭证有效期。访问时重新校验账号/角色/租户绑定及范围摘要；身份授权变化后旧地址失效。下载凭证用途独立，不能用于其他患者工具。保留完整 URL 查询参数；过期后用新身份凭证重新调用报告工具取得新地址。

旧裸 patientId 控制器、旧 robot/project 身份映射返回值和旧公开图片下载动作已移除。MetaAgent 当前 main 尚需对应改造，位置及调整要求见 [MetaAgent 对接评估](metaagent-v2-adaptation-20261029.md)。

## 本次验证

使用隔离 SQLite 数据与控制器测试验证身份、范围、分页、机构标注、引用失效及报告授权：

```powershell
dotnet test AI_WebApi.Tests/AI_WebApi.Tests.csproj --no-restore
```

2026-11-22 另用只读账号连接 Docker 的 `mysql8.project` 验证临时 HTTP 服务。对同一全局账号逐页与独立 SQL 核对：本人跨机构为 85 条病历、46 条病历－处方关联、68 条计划、23 条报告；医生当前机构为 17、7、9、1。医生委托使用本地模拟回调，跨机构及撤销后均返回 403。另验纯学生账号、同号职工隔离与未映射步道排除；未写入数据或部署服务。
