# MetaAgent 对接患者康复契约 2.0.0 的改动评估

核查日期：2026-10-29。核查基线为拉取后的 `origin/main` 提交 `2a08efe7bedc2e8e0999fe90c268b97b1bea180b`（Merge pull request #4）。本次通过 `git show` / `git grep` 读取提交，不读取功能分支作为基线，不修改 MetaAgent 工作区。

结论：当前 main 不能直接调用这一版 AI_WebApi。阻断点包括旧身份请求、旧版本白名单、旧 patient_ref 推导和缺少新登录凭证。患者工具统一使用契约 `2.0.0`，不保留旧输入/输出格式。

## HTTP 地址与统一请求

工具基地址继续为 `/api/ai/patients/tools`，工具名称继续沿用 catalog 中的业务名称；不增加 `_v2` 工具别名，也不同时发布 `/v2/tools` 第二套路由。OpenAPI 地址继续是 `/api/ai/patients/tools/openapi.json`，内容为新契约。

```json
{
  "system_context": {
    "user": "authenticated-subject",
    "conversation_id": "conversation-example",
    "patient_identity": {"type": "project_user_id", "value": "7"},
    "current_business_id": 1,
    "identity_context_token": "business-backend-issued-proof"
  },
  "data_scope": "all_tenants"
}
```

LoginName 使用 `{"type":"login_name","value":"000007"}`，完整保留字符串。手机号不作为公开身份解析请求参数；`robot_patient_id`、`project_patient_id` 旧映射返回值已经移除。`resolve_patient_identity` 返回匿名 `patient_ref`、已确认账号数和绑定状态；封套增加 `query_scope` 及必要的 `tenant_segments`。

凭证由已认证业务登录后端签发，不能让模型根据患者编号、手机号或 role 参数构造凭证。MetaAgent 可以只转发短期凭证，无须持有签名密钥；凭证不得进入模型上下文、普通日志或长期会话存储。

## 必须改动的位置

下面链接均固定到核查提交，避免与本地功能分支混淆。

| 优先级 | main 中的位置 | 当前行为 | 所需调整 |
| --- | --- | --- | --- |
| P0 | [api/routes.py:29](https://github.com/Mrpointdelicious/metaAgent/blob/2a08efe7bedc2e8e0999fe90c268b97b1bea180b/src/meta_agent/api/routes.py#L29) | 从多个手机号/项目 ID 别名构造旧身份映射，并写回三个患者 ID | 读取唯一新身份对象、当前租户及业务签名凭证；删除旧映射输入和 robot ID 回填 |
| P0 | [tools/ai_webapi.py:109](https://github.com/Mrpointdelicious/metaAgent/blob/2a08efe7bedc2e8e0999fe90c268b97b1bea180b/src/meta_agent/tools/ai_webapi.py#L109) | `resolve_patient_identity` 发送 phone 或 project_patient_id，严格校验 1.6.0 和两套整数 ID | 发送统一上下文及范围；校验 2.0.0、匿名 patient_ref 和 query_scope；已确认入口且部分关联不明时接受 partial |
| P0 | [orchestration/identity.py:27](https://github.com/Mrpointdelicious/metaAgent/blob/2a08efe7bedc2e8e0999fe90c268b97b1bea180b/src/meta_agent/orchestration/identity.py#L27) | TrustedScope 区分 patient_id、iretour_patient_id、project_patient_id，scope_hash 不含康复查询范围 | 改为单一入口身份，保留身份类型；增加康复 data_scope、后端匿名 patient_ref、scope_fingerprint；短期凭证只放执行时上下文 |
| P0 | [agent/tools.py:251](https://github.com/Mrpointdelicious/metaAgent/blob/2a08efe7bedc2e8e0999fe90c268b97b1bea180b/src/meta_agent/agent/tools.py#L251) | `_robot_id` 和 agent_robot_id 缓存旧映射；不同工具家族选择不同患者 ID | 删除 robot/project 身份桥；所有患者工具共用一个可信上下文工厂 |
| P0 | [agent/tools.py:317](https://github.com/Mrpointdelicious/metaAgent/blob/2a08efe7bedc2e8e0999fe90c268b97b1bea180b/src/meta_agent/agent/tools.py#L317) | 只注入数字 user 和 conversation_id；349—375 行按旧版本及 `project:{user}` 判断 | 注入完整新上下文和 data_scope；统一校验 2.0.0；patient_ref 从本轮已确认绑定取得，不再自行拼接 |
| P0 | [domains/rehab.py:58](https://github.com/Mrpointdelicious/metaAgent/blob/2a08efe7bedc2e8e0999fe90c268b97b1bea180b/src/meta_agent/domains/rehab.py#L58) | 另一条领域工作流仍使用旧请求；74—89 行接受旧版及 legacy_tour | 与 agent 路径共用新工厂及封套校验，删除旧版兼容分支 |
| P0 | [workflows/irego.py:23](https://github.com/Mrpointdelicious/metaAgent/blob/2a08efe7bedc2e8e0999fe90c268b97b1bea180b/src/meta_agent/workflows/irego.py#L23) | 固定构造 `system_context={user:patient_id}` | 全部子调用使用同一可信身份、范围和凭证；不要在工作流里重新选择患者编号 |
| P0 | [planning/compiler.py:187](https://github.com/Mrpointdelicious/metaAgent/blob/2a08efe7bedc2e8e0999fe90c268b97b1bea180b/src/meta_agent/planning/compiler.py#L187) | 按设备家族要求不同 ID 字段 | 改为要求单一已认证患者身份，具体来源映射由后端负责 |
| P1 | [contracts.py:Fact / RecordAnchor / ConversationState](https://github.com/Mrpointdelicious/metaAgent/blob/2a08efe7bedc2e8e0999fe90c268b97b1bea180b/src/meta_agent/contracts.py) | 事实与当前记录没有所属机构及查询范围信息 | 事实投影携带 record_tenant；记录锚点、分页状态和证据绑定 scope_fingerprint，切换范围时重置锚点/分页 |
| P1 | [domains/irego.py](https://github.com/Mrpointdelicious/metaAgent/blob/2a08efe7bedc2e8e0999fe90c268b97b1bea180b/src/meta_agent/domains/irego.py)、[iretour.py](https://github.com/Mrpointdelicious/metaAgent/blob/2a08efe7bedc2e8e0999fe90c268b97b1bea180b/src/meta_agent/domains/iretour.py)、[irecog.py](https://github.com/Mrpointdelicious/metaAgent/blob/2a08efe7bedc2e8e0999fe90c268b97b1bea180b/src/meta_agent/domains/irecog.py)、[assessment.py](https://github.com/Mrpointdelicious/metaAgent/blob/2a08efe7bedc2e8e0999fe90c268b97b1bea180b/src/meta_agent/domains/assessment.py) | 按旧数据形状投影列表、分析和事实 | 只读取规范字段 report_blocks / report_block_count / display_name / basic_evaluation，保留所属机构和覆盖问题 |
| P1 | [application/context.py:47](https://github.com/Mrpointdelicious/metaAgent/blob/2a08efe7bedc2e8e0999fe90c268b97b1bea180b/src/meta_agent/application/context.py#L47)、[agent/context.py](https://github.com/Mrpointdelicious/metaAgent/blob/2a08efe7bedc2e8e0999fe90c268b97b1bea180b/src/meta_agent/agent/context.py)、[application/composer.py](https://github.com/Mrpointdelicious/metaAgent/blob/2a08efe7bedc2e8e0999fe90c268b97b1bea180b/src/meta_agent/application/composer.py) | 缓存和事实/制品复用依赖旧 scope_hash，存在 agent_robot_id 状态 | 移除旧身份缓存；事实、报告和记录锚点隔离键包含实际范围与绑定版本；同一 artifact_ref 的新授权 URL 可刷新，不能只按 ref 去重丢弃新地址 |
| P1 | [application/service.py:278](https://github.com/Mrpointdelicious/metaAgent/blob/2a08efe7bedc2e8e0999fe90c268b97b1bea180b/src/meta_agent/application/service.py#L278)、[domains/patient_identity.py](https://github.com/Mrpointdelicious/metaAgent/blob/2a08efe7bedc2e8e0999fe90c268b97b1bea180b/src/meta_agent/domains/patient_identity.py) | 档案上下文仍包含旧版本描述和固定版本信息 | 跟随实际 2.0.0 证据；患者基础档案不因康复 all_tenants 就自动合并其他机构病历资料 |
| P1 | [tools/ai_webapi.py:188](https://github.com/Mrpointdelicious/metaAgent/blob/2a08efe7bedc2e8e0999fe90c268b97b1bea180b/src/meta_agent/tools/ai_webapi.py#L188) | 非 2xx 只变成 http_xxx，丢掉业务原因码 | 有界读取错误封套，区分身份过期/权限拒绝/登录名冲突/来源归属不足，不能统一解释成“没有康复记录” |

## 报告与证据链

- 每个 generate_* 请求必须由程序绑定同轮分析的 `data.source_snapshot` 到 `expected_source_snapshot`。IReGo 分析现在也返回此字段。既有 `agent/tools.py:430` 的补链方式可以复用，但要覆盖全部报告家族与旧领域工作流路径。
- 每条记录、趋势点和返回的连续来源段保留医院/卫生中心名称。A医院→B中心→A医院是三个来源段，不推断连续治疗或住院时间。
- `query_scope.coverage=partial`、unknown 机构和身份关联冲突都应进入回答限制，不能把未知归属当作当前登录医院。
- `domains/irego.py:241` 已保留 artifact_ref、image_url、expires_at；这一部分可以沿用。`AIWebApiClient.artifact_available` 目前保留 URL 查询串并有 HEAD/GET 回退，不能新增“移除 query”逻辑，否则会删掉 access_grant。
- 患者报告改用 `irego_scope_v2_` 引用及短期授权 URL；服务端下载会重查当前身份与范围。长期会话只持久化制品引用、分析选择条件和必要证据，展示时重新获取授权地址，避免复用过期链接。

## 配置与测试改动

`config.py:33` 的工具基地址可以保持，health/OpenAPI 地址也不需要改路径。增加可信业务登录上下文的接入配置。若登录网关负责签发凭证，MetaAgent 无须复制 AI_WebApi 的签名密钥。

需要更新 `tests/test_single_agent.py`、`test_new_webapi_tools.py`、`test_irego_workflow.py`、`test_iretour_agent.py`、`test_irecog_assessment_tools.py`、`tests/orchestration/test_patient_brief.py` 及相应合成响应 fixture，覆盖：

1. 2.0.0 唯一契约、旧身份字段拒绝、LoginName 前导零。
2. 本人跨租户记录、当前租户过滤、切换范围后旧锚点失效。
3. partial 绑定可继续查询已确认记录，unknown 机构不冒充当前医院。
4. 所属机构出现在事实、上下文和回答中；凭证和完整手机号不进入模型上下文。
5. 全部报告绑定同轮快照，数据变更重读分析后再生成。
6. 制品 URL 过期刷新、保留授权查询串、同引用的新地址不被去重丢弃。

本次仅完成代码考察和改动清单，尚未对 MetaAgent main 实施这些修改。按用户要求，本次移除旧契约后不进行 smoke test，也不启动 MetaAgent/AI_WebApi HTTP 联调探测。
