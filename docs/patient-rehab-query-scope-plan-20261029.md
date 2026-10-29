# 患者康复记录查询范围方案（现有 project 结构）

日期：2026-10-29。状态：已按当前数据库结构实现，尚未部署；患者 HTTP 契约统一替换为 2.0.0。

本方案依据用户提供的《多租户登录名改造方案(2).md》第一至第四部分，以及现有代码与当前部署连接的 `mysql8/project` 数据库只读核查结果编写。此次仅按现有数据库设计：保留 `dbbuniessuser` 用户—租户中间表，接口和查询服务可以改造。新 project 结构在用户提供并要求适配后另行处理。

上传文档中的 DDL、数据更新、删除、迁移接口和归档操作均作为参考材料，本次未执行。

## 推荐选择与覆盖范围

推荐患者本人默认使用 `all_tenants`，查看本人在全部租户下已确认归属的康复记录；同时保留 `current_tenant` 选项。两种范围通过同一个身份解析和查询服务实现，避免维护两套接口。

本期范围为已接入的康复计划、设备训练记录、评估结果及其分析和康复报告。病历、会诊、处方等其他患者资料的跨租户访问规则另行定义，不因本次康复范围改造自动扩大。IReMo 等未接入、未验证的数据源继续明确显示未覆盖。

“全部”表示全部已确认属于该患者、且当前产品已接入的记录。身份关联或来源外键不明时返回覆盖不足说明，不能宣称结果已覆盖库内所有历史记录。

| 比较项 | 方案 A：全部租户（推荐默认） | 方案 B：当前租户 |
| --- | --- | --- |
| 身份基础 | 已登录患者对应的全部已确认患者账号和来源身份 | 相同身份基础，加当前会话租户校验 |
| 查询条件 | 记录归属于本人；不按租户缩小范围 | 记录归属于本人，且记录所属租户等于当前租户 |
| 切换租户 | 本人记录集合不因入口租户改变 | 记录随当前租户改变 |
| 历史租户关系失效 | 已确认本人所有权的历史记录仍可读取 | 当前租户访问资格须有效；历史记录仍按记录级归属筛选 |
| 租户归属未知 | 所有权明确的记录可展示，机构标为未知 | 不纳入严格租户结果，说明覆盖不足 |
| 对现库的适应程度 | 更适合分散、多租户及历史租户缺失的数据 | 依赖记录级租户归属，部分来源只能返回部分结果 |

该默认值仅用于患者本人康复查询。医生、机构工作人员、代查场景继续使用其授权范围；请求传入 `all_tenants` 不会使其获得患者本人的访问权限。

## 一、身份语义与当前结构

文档第一至第四部分描述的目标语义是：一个 LoginName 对应一个租户账号；拆分后同一患者在不同租户可以具有不同 UserId、不同 LoginName，同一个手机号可以跨租户重复。因此 LoginName 的唯一性解决账号定位，不直接解决跨租户自然人关联。

现有结构还允许另一种情况：同一条 dbuser 通过中间表关联多个租户。当前方案必须同时支持以下两种关系。

```text
已登录账号（可信 UserId，或唯一 LoginName 定位）
                 │
                 ▼
          本人已确认账号集合
            ┌────┴────┐
       UserId=100   UserId=200
            │           │
    dbbuniessuser   dbbuniessuser
      ┌─────┴────┐      │
    租户 A      租户 B  租户 C
```

示例中，UserId=100 可同时拥有 A、B 两个租户的记录；UserId=200 是同一患者的另一条已确认账号。按当前租户 B 查询，不能仅选择 UserId=100，还必须筛记录本身的租户。

| 数据或字段 | 本期用途 |
| --- | --- |
| `dbuser.Id` | 当前 project 账号主键、原始记录外键的关联目标之一 |
| `dbuser.LoginName` | 唯一定位入口账号；完整字符串处理，保留前导零 |
| `dbuser.Phone` | 按已确认业务规则生成跨账号关联候选，不充当数据库主键 |
| `dbbuniessuser(UserId, BusinessId, Enable)` | 本期租户关系来源；有效关系按 `Enable=0` 读取并去重 |
| `dbroleuser(UserId, RoleId, BusinessId, Enable)` | 判断执行主体的患者角色及其租户范围；不只检查 RoleId |
| `dbuser.BusinessId` | 当前资料字段和一致性检查线索，不替代中间表或历史记录租户 |
| 计划/记录的 `BusinessId` 或 `TenantId` | 在字段含义已确认的来源中，作为记录级租户依据 |
| `OldId` | 普通历史信息；现库存在“步道场景”等非数字值，不用作通用身份或租户映射键 |

### 当前快照的实际约束

2026-10-29 对 `.env.ai_webapi` 指向的本地 `mysql8/project` 进行一致性快照只读核查，得到：

| 项目 | 当前结果 | 对本期方案的影响 |
| --- | --- | --- |
| `dbuser` | 2,943 条 | 按既有账号数据读取 |
| LoginName | 2,937 条为空；6 条非空，共 5 个不同值，存在 1 组重复 | 业务唯一规则尚未在此快照完整落地；先支持可信 UserId，LoginName 仅唯一命中时使用 |
| `dbuser` 索引 | 只有 `Id` 的 PRIMARY 索引 | 不能把声明的唯一规则等同于数据库已有唯一约束 |
| 中间表有效正租户关系 | 17 个用户关联多个租户 | 当前租户查询必须处理一个 UserId 对应多个租户 |
| 中间表重复有效关系 | 1 组 UserId+BusinessId 多出 3 行 | 使用 EXISTS 或去重关系，避免 JOIN 放大记录和计数 |
| `dbrehaplan` | 9,824 条，157 条租户为空或非正值；80 条患者外键为空或非正值（两类可重叠） | 所有权缺失和租户缺失分开处理 |
| 计划租户与用户当前 BusinessId 不同 | 两边均为正值的关联记录中有 4,534 条 | 在当前多租户模型下不必然是错误，不能按用户当前 BusinessId 丢弃历史计划 |
| `dbwalk` | 无 BusinessId 字段；13,259 条中 10,012 条的 userId 不能直接连接现有 dbuser.Id | 所有权关联需核实，不能把 userId 数值直接视作可信 project 主键 |
| DeviceType=6 的步道记录 | 13,068 条，其中 9,852 条 userId 不能直接连接 dbuser.Id | IReTour 历史覆盖存在独立数据关联前提 |
| `rehabplan`、`wx_rehabplan`、`assess_patient` | 当前没有 BusinessId 字段 | 若后续接入这些表，严格租户过滤需要其他已验证来源证据 |

`dbsyncidmapping` 虽存在，但按现有 `user/robotuser` 类型尝试 `SourceId=dbwalk.userId → TargetId=dbuser.Id` 未得到可用关联；本期不能未经验证将其视为现成的步道患者映射。即使某个旧 ID 数字恰好能连接 dbuser，也必须确认该来源声明使用 project 命名空间，不能通过“先查主键、查不到再猜其他来源”判断。

这些结果只描述当前本地部署库，不外推到稍后交付的新库。

## 二、统一身份与范围解析

已新增集中服务 `IPatientAccessContextResolver`，所有康复工具使用解析后的上下文。身份关联规则与租户关系查询由 `IPatientAccessRepository` 封装。

### 解析顺序

1. 验证业务登录主体及服务调用方。当前 Dify Bearer 负责服务认证，具体患者和当前租户须由业务登录后端通过可信运行时上下文注入、校验；普通工具参数或语言模型不能选择执行主体。
2. 用唯一一个显式身份对象定位入口账号：`project_user_id` 或 `login_name`，并校验其账号 ID 与签名凭证绑定一致。LoginName 空值、零命中或多命中时返回明确错误，不按手机号或数字主键静默兜底。
3. 验证执行主体可使用患者本人查询。账号 `Status=1`、未删除，患者角色关联有效；涉及当前租户时，同时检查该租户的有效关系及角色。审核中、拒绝、历史停用等状态按现库实际规则处理，不能套用未上线的新状态语义。
4. 从入口账号读取手机号，按已确认的业务规则发现同一患者的其他账号。已有同一 UserId 的多个租户关系直接由中间表展开，不需要复制用户。
5. 对跨 UserId 候选做身份关联检查。不能依据姓名相同、机构相同、共同医生或数字相同自动归并。同号且证件/已确认档案冲突、测试身份混用、来源不明时，停止扩展该候选并返回 `IDENTITY_LINK_AMBIGUOUS`；仍可返回已确认账号的记录并标记 partial。
6. 形成已确认账号、来源身份及租户关系集合，按请求范围和执行主体权限计算实际范围。客户端提交的 UserId 列表、手机号和租户列表不作为授权结果。

同手机号关联依赖“一名患者跨租户账号沿用该手机号”的业务规则。手机号共享、回收或更换可能使此规则失效；当前库没有可靠的全局自然人主键和号码归属历史时，对这些异常使用业务核实的现有映射或配置，不在本期新增自然人表，也不自动扩大访问。

### 历史账号与登录资格

入口账号必须能够合法登录；历史记录的所有权是另一个判断。已明确绑定本人的旧账号，其记录不应仅因旧账号被禁用、角色变更或租户关系失效就消失。历史账号的扩展须有已确认关联，不能仅通过停用账号当前的相同手机号取得权限；关联不足时明确历史覆盖不完整。

### 建议的后端上下文

```text
ResolvedPatientAccessContext
  AuthenticatedSubjectRef           登录执行主体
  AnchorUserId                      入口 project 账号
  CurrentBusinessId?                当前会话租户
  EffectiveScope                   all_tenants / current_tenant
  ConfirmedPatientUserIds[]         已确认属于本人的 project 账号
  ConfirmedSourceOwners[]           来源命名空间 + 来源用户 ID
  UserTenantBindings[]              去重后的 (UserId, BusinessId, 状态)
  BindingFingerprint                身份与关系版本摘要
  ScopeFingerprint                  主体 + 范围 + 租户 + 授权策略版本
  CoverageIssues[]                  身份/来源/租户覆盖问题
```

`AnchorUserId` 保留入口账号语义，不从关联集合任取一条作为所有历史数据的新主键。每条训练记录保留自己的原始 OwnerId、SourceSystem、PlanId、ReportId 和记录级租户。对外引用使用后端生成的匿名引用，不返回完整手机号来标识患者。

## 三、两种查询范围如何落在现有表上

### A. 本人全部租户康复记录

从已确认账号和来源身份取得本人记录，不增加当前租户筛选，也不要求每条历史记录的租户仍在当前有效中间表关系中。

以 project 命名空间已确认的 `dbrehaplan` 为例，以下为参数化查询的逻辑示意：

```sql
SELECT p.*
FROM dbrehaplan p
WHERE p.UserId IN (@ConfirmedProjectUserIds)
ORDER BY COALESCE(p.StartTime, p.BookingTime, p.CreateTime) DESC, p.Id DESC;
```

集合参数通过 ORM 的数组 Contains 或安全参数展开实现，不拼接用户输入。设备家族、记录状态、结果有效性等原有来源识别和质量规则继续执行。

这里不再添加 `p.BusinessId = 入口用户.BusinessId` 或 `设备当前.BusinessId = 入口用户.BusinessId`。本人所有权已确认且记录机构未知时可以展示，标注 `record_tenant.attribution_status=unknown`；所有权不明的记录不因使用 all_tenants 就进入结果。

报告通过已经选中的 PlanId/记录主键继续关联，不能再次退回单个入口 UserId 查询，或将报告连接到别人的计划。

### B. 本人当前租户康复记录

除本人所有权外，还须同时满足：当前租户由登录会话确定、执行账号在该租户具备有效患者访问资格、记录有可验证的该租户归属。

```sql
SELECT p.*
FROM dbrehaplan p
WHERE p.UserId IN (@ConfirmedProjectUserIds)
  AND p.BusinessId = @AuthenticatedCurrentBusinessId
ORDER BY COALESCE(p.StartTime, p.BookingTime, p.CreateTime) DESC, p.Id DESC;
```

在进入查询前，通过中间表的 `UserId + BusinessId + Enable=0` 与租户对应的有效角色验证当前会话资格。该校验不能由请求中的任意 BusinessId 自行满足。

不能只把候选用户限定为“属于当前租户的 UserId”再读这些 ID 的所有记录；一个 UserId 可以属于多个租户，这样仍会混入其他租户记录。

### 各数据源的处理

| 来源 | 全部租户 | 当前租户 |
| --- | --- | --- |
| IReGo / `dbrehaplan → dbreport` | 对全部已确认 owner 查询，按原计划关联报告 | 追加计划记录的 BusinessId 过滤 |
| IReCog / `dbrehaplan → dbreport` | 与 IReGo 使用相同所有权范围，保留 DeviceType=5 的识别 | 追加计划 BusinessId 过滤；设备当前机构不替代历史计划机构 |
| 已接入在线评估 / `dbrehaplan → dbreport` | 使用相同 owner 集合，保留在线评估的来源识别条件 | 按该计划 BusinessId 过滤 |
| IReTour / `dbwalk → walkreport` 等 | 先验证 userId 的来源命名空间及本人所有权，再纳入 | 在此基础上还需可靠的历史租户证据；无证据则排除并标记 partial |
| 其他尚未接入的历史计划表 | 单独建立并验证来源适配器后才计入覆盖 | 同时验证记录级租户依据 |

`dbwalk` 当前没有 BusinessId。设备当前归属可能与训练发生时不同，中间表也只能说明账号—租户关系，不能证明某次训练属于哪一租户。因此本期严格 current_tenant 不使用设备当前 BusinessId 或 firstTenant 猜测归属。若现有数据中有可验证的历史绑定/事件快照，再通过来源适配器使用；否则明确覆盖不足。本方案不要求现在增加字段或执行文档中的租户回填迁移。

### 聚合、计数和分析规则

1. 先应用所有权和范围条件，再计算总数、排序和分页。不能对每个 UserId 分别取一页后拼接，否则跨租户的最新记录和分页会失真。
2. 记录键包含 `SourceSystem + SourceTable + SourcePrimaryKey`；使用已有且已验证的来源映射识别同一条复制记录。仅手机号、同一天、同项目名称或相同报告内容不足以去重。
3. 中间表和角色表使用 EXISTS 或先去重，避免一条康复记录因多条租户/角色关系被重复统计。
4. 全部租户下的“最近一次”取整个已授权集合的最新有效记录；当前租户下先筛租户，再选最近一次。
5. 全部租户只是扩大本人记录集合。纵向分析继续遵守量表版本、设备协议、训练条件和可比性规则；跨租户不自动等于可比较。
6. 单次报告沿用该次记录的来源机构。多次报告展示各记录来源及覆盖租户，不把全部历史记录标成当前入口机构。

## 四、接口改造建议

### 版本与默认行为

患者工具沿用 `/api/ai/patients/tools/` 和原业务工具名，全部替换为契约 `2.0.0`。OpenAPI 沿用 `/api/ai/patients/tools/openapi.json`，分组改为 `ai-patient-tools-v2`。请求显式声明身份类型和 `data_scope`；默认 `all_tenants`，`current_tenant` 必须有合法当前租户。

不保留旧契约或第二套路由；旧身份映射入口、裸 patientId 查询入口、旧公开报告下载和数字 user 身份推断已移除。调用方需同步改造请求与响应处理；MetaAgent 最新 main 的影响见 [对接评估](metaagent-v2-adaptation-20261029.md)。

### 请求示例

```json
{
  "system_context": {
    "user": "运行时用户标识",
    "conversation_id": "conversation-example",
    "patient_identity": {
      "type": "project_user_id",
      "value": "7"
    },
    "current_business_id": 1,
    "identity_context_token": "business-backend-issued-proof"
  },
  "data_scope": "all_tenants",
  "page_number": 1,
  "page_size": 20
}
```

`patient_identity` 另支持 `{"type":"login_name","value":"000001"}`。LoginName 即使全部为数字也按字符串处理，禁止把 `000001` 自动解释为 UserId=1。当前库 LoginName 缺失的账号使用可信 project_user_id 入口；LoginName 重复时返回 `LOGIN_NAME_NOT_UNIQUE`，不挑第一条。

`system_context.user` 表示登录主体，不直接强转为患者主键。`patient_identity`、当前租户和本人查询资格通过业务后端签发的短期 `identity_context_token` 绑定，Dify 只转发。服务 Bearer 和身份签名凭证均须有效；共享 Bearer 不能替代患者授权。

本期使用 `PatientIdentityProofCodec` 的 HMAC-SHA256 凭证，最长 900 秒。缺少签名配置时返回 503，缺少或无效凭证时返回 401。同号候选扩展还须凭证中的 `phone_binding_hash` 与入口账号手机号一致，并通过资料一致性检查；业务核实的历史关联可配置在 `ConfirmedAccountGroups`。签发规范见 [集成说明](patient-rehab-v2-contract.md)。

`data_scope` 是所请求范围。实际范围由主体权限及服务端策略计算，不接受模型自行注入 `ConfirmedPatientUserIds` 或来源 ID 列表。

### 响应增加的共同元数据

```json
{
  "query_scope": {
    "requested": "all_tenants",
    "effective": "all_tenants",
    "current_tenant_ref": "tenant_xxx",
    "included_account_count": 2,
    "included_tenant_count": 2,
    "coverage": "partial",
    "unattributed_tenant_record_count": 3,
    "scope_fingerprint": "scope_xxx"
  },
  "reason_codes": ["SOURCE_PATIENT_BINDING_UNVERIFIED"]
}
```

示例数字仅说明契约。覆盖租户数按实际已选中且租户明确的记录统计，不取登录账号拥有的租户关系数。未知患者归属不能计入“本人记录”数量；未知租户计数也不能凭空代替未知患者记录数量。

记录增加匿名的 `owner_user_ref`、`source_system` 和结构化 `record_tenant`。`current_tenant_ref` 表示当前登录机构，`record_tenant` 表示该条或该段康复记录所属的医院、卫生中心等单位，二者独立返回。

### 康复记录所属机构的明确标注

患者端使用“所属机构”这一标签，展示医院、卫生服务中心等单位名称。机构属于记录本身，切换登录租户或查询范围不改变这条历史记录的标注。

#### 机构归属与机构名称分别解析

1. 先确定记录所属租户 ID，再按现有 `dbbuniess.Id` 关联机构名称。`dbrehaplan.BusinessId → dbbuniess.Id/Name` 是当前计划链路的读取方式；其 `dbreport` 经 PlanId 继承该计划的机构。
2. 记录或已验证的原始事件包含历史机构快照时，优先显示快照名称，并保留原机构 ID 关联；只有当前机构字典时，显示当前登记名称，并标注名称来自当前字典，不能声称是训练发生时的历史名称。
3. 不将 `dbuser.BusinessId`、`dbbuniessuser` 第一条租户、当前登录租户或设备当前所属机构填作每条历史记录的所属机构。
4. 用户已退出机构、设备已转移、机构已停用或删除，不改变有明确依据的历史记录归属。机构字典查不到名称时保留已确认机构引用，显示“所属机构名称暂缺”，不能把已知归属降成未知或改挂当前机构。
5. 记录所有权明确但机构依据缺失时，显示“所属机构未确认”；多个同等可信的历史归属依据冲突时，显示“所属机构待核实”。设备当前机构与记录机构不同，本身不构成历史归属冲突。

建议的记录级返回对象如下，名称为示例：

```json
{
  "record_ref": "rehab_record_example",
  "recorded_at": "2026-10-01T09:00:00+08:00",
  "record_tenant": {
    "tenant_ref": "tenant_a",
    "display_name": "A医院",
    "attribution_status": "verified",
    "attribution_basis": "project.dbrehaplan.BusinessId",
    "name_source": "current_directory"
  }
}
```

后端保留原 BusinessId 供关联和核查，对患者接口使用 `tenant_ref` 标识机构。`display_name` 是患者需要看到的单位名称，不能只返回租户 ID 后让语言模型猜名称。`attribution_basis`、`name_source` 用于后端追溯与必要的详情说明，不要求在记录卡片上暴露技术字段。

| 归属状态 | 机构引用 | 患者端显示 | 全部租户视图 | 严格当前租户视图 |
| --- | --- | --- | --- | --- |
| verified，名称可解析 | 已确认 | 所属机构：A医院 | 按本人所有权展示 | 所属机构等于当前租户时展示 |
| verified，机构字典/名称缺失 | 已确认 | 所属机构名称暂缺 | 展示并保留机构引用 | 仍可按已确认租户 ID 判断，不依赖名称 |
| unknown | null | 所属机构未确认 | 本人所有权明确时可展示 | 排除并说明覆盖不足 |
| conflict | 不自动选取一个 | 所属机构待核实 | 本人所有权明确时可展示并提示 | 排除并说明覆盖不足 |

`verified` 表示记录级归属依据已验证，不表示医疗结果已被认证，也不表示显示名称是历史快照。未知或冲突租户的记录不计入已确认机构数。

#### 列表、分段、图表及报告

| 展示位置 | 标注规则 |
| --- | --- |
| 康复记录列表 | 每条卡片固定展示“所属机构：单位名称”，与训练时间、项目一同出现；即使只查当前机构也保留该标签 |
| 记录详情、单次分析与报告 | 页面/报告摘要展示该次记录所属机构，所有分段沿用该次来源；单次结果实际含多个来源时按明细分别标注 |
| 某段时间的记录 | 段内全部记录属于同一已确认机构时展示该机构；跨机构时显示“多机构康复记录”，逐条或分段标明来源 |
| 多次分析、纵向报告 | 摘要列已确认机构及相应记录数，明细增加所属机构列；未知/待核实记录单列，不能统称为当前医院记录 |
| 趋势图 | 每个数据点附所属机构，图例/提示和导出明细保持一致；有机构切换时标注记录来源切换 |
| 导出和历史报告 | 机构标签与本次选中记录集合一致；不能仅修改在线列表而使导出、图表或报告遗漏机构 |

例如，同一患者依次有 A医院、B卫生服务中心、A医院的记录，时间轴保留三个来源段。不能按机构分组后把首尾 A医院记录合成一个连续住院/治疗时段。

如返回 `tenant_segments`，每段包含 `record_tenant`、`record_refs`、`first_recorded_at`、`last_recorded_at` 和 `record_count`，按确定的记录排序对连续同来源记录分段。这两个时间表示观察到的记录范围，不代表入院、出院或完整治疗起止时间。归属未知的记录不自动并入相邻医院段。

机构标注由同一个记录归属解析服务生成，列表、分析、报告共用。机构名称需要批量查询，避免对每条记录单独查一次字典；名称更新通过字典缓存失效和报告来源快照策略处理，已生成的历史报告保留当次显示内容。

### 涉及接口

| 位置 | 建议调整 |
| --- | --- |
| `resolve_patient_identity` | v2 返回已确认的本人账号组及范围信息；多条记录属于同一已确认患者时不再统一报绑定失败 |
| `get_patient_rehab` | 计划、IReGo、IReTour 等子结果共用同一解析上下文，明确各自覆盖情况 |
| 各家族 context / history | 统一应用账号集合和实际范围，再生成摘要、计数和最近记录 |
| 各家族 single / multiple analysis | 从当前允许集合选择引用，逐条校验所有权和范围 |
| 各家族 single / multiple report | 继承分析的范围和来源快照；报表生成及访问使用同一范围约束 |
| 旧的 patientId 康复概览入口 | 已移除，不保留旧身份规则 |

单次引用在全部租户视图生成后，用户切换到当前租户视图时，若该记录不属于当前租户，应拒绝本次读取；不能仅验证引用签名存在。

## 五、代码改造范围

| 层次 | 当前位置 | 拟改造内容 |
| --- | --- | --- |
| 实体 | `PatientEntity`、`DbRoleUserEntity`，新增现有中间表实体 | 映射现库 LoginName、角色 Enable/BusinessId、租户中间表；不改数据库结构 |
| 请求与上下文 | `PatientContextDtos`、`TrustedPatientContext` | 增加显式身份类型、范围、当前租户、账号集合、来源集合及版本摘要 |
| 身份入口 | `DifySystemContextValidator/Resolver`、身份映射 Controller/Service/Repository | 分开服务认证、登录主体、账号定位、本人关联和范围授权；数据库读取放到异步解析服务 |
| 患者康复汇总 | `PatientLayerService.RehabAsync`、`PatientLayerRepository.PlansAsync` | 从单个 patientId 转为已解析查询范围；汇总各来源的覆盖问题 |
| 来源仓储 | `IReGoDataRepository`、`IReTourDataRepository`、`IReCogDataRepository`、`AssessmentDataRepository` | 所有权范围与记录租户过滤统一下推；建立来源身份和历史租户解析边界 |
| 评估和分析 | 对应家族 Evaluation / History / Session / Longitudinal 服务 | 移除“所有返回记录都必须等于入口患者 ID”的假设，改为逐记录验证原始 owner 在允许集合中 |
| 引用、缓存、报告 | SessionRefProvider、SnapshotCache、ReportContentFingerprint、ArtifactReference、报告服务 | 区分执行主体、本人账号集合、查询范围和原始记录归属 |
| 记录机构标注 | `PatientAccessContext`、`RecordTenantPresentation`；各家族 DTO、列表/分析/报告构建器 | 读取记录级机构依据，关联 dbbuniess，输出机构名称、归属状态与依据；时间段和图表保持同一标注 |
| 契约说明 | Swagger、工具 catalog、Dify 运行时参数绑定 | 明确患者本人默认范围、身份字符串类型及覆盖限制 |

身份/范围计算集中完成，各家族消费统一上下文。租户关系读取放在 `IPatientAccessRepository` 后，本期实现读取 `dbbuniessuser`；新库结构以后适配。

### 缓存、引用与历史数据

- 缓存键至少包含：登录主体引用、已确认身份集合摘要、effective scope、当前租户（仅 current_tenant）、授权关系/策略版本、影响结果的筛选参数。
- 不用 Phone、入口 UserId 或 CurrentBusinessId 单独作为全部租户结果的缓存键。
- 来源记录的引用保留原始来源、原始 owner、计划/报告主键及记录级机构；读取引用时结合当前解析上下文重新授权。
- 报告来源快照、内容指纹和制品缓存包含范围及成员版本，避免 all_tenants 的报告在 current_tenant 视图被当作同一结果复用。
- 账号绑定、手机号或租户权限变化后重算关系摘要；历史引用不能因为仍有合法签名而绕过当前范围检查。
- 数据库中的原 UserId、计划外键、报告外键和历史来源归属保持原值。聚合发生在查询层，本期不重绑或批量归并数据。

## 六、实施顺序与验收

以下是现有结构内的实施步骤，不对应上传文档的新库迁移迭代。

1. 完成账号定位、租户关系和来源所有权对账。输出 LoginName 不可用、同号身份冲突、来源外键未知、记录租户未知的清单，区分可查询数据和不能自动归属的数据。
2. 实现统一解析上下文与范围策略，增加现有中间表映射和 v2 契约。保留有效 UserId 入口，LoginName 严格唯一命中才接受。
3. 先改造记录级租户明确的 project 康复计划链路，再按同一范围规则接入 IReGo、IReCog、已接入评估及报告；IReTour 以已确认身份和租户覆盖为准，明确未覆盖部分。
4. 完成全局分页、计数、最近记录、单次/多次分析、缓存和报告一致性校验。
5. 调用方改造并配置签名凭证后，患者本人入口默认 all_tenants，并允许切换 current_tenant。本次只完成代码和隔离验证，未启动服务、部署或执行 smoke test。

建议优先验收以下场景：

| 场景 | 预期 |
| --- | --- |
| 同一 UserId 关联 A、B，分别有康复记录 | 全部视图含 A+B；当前 A 视图仅 A |
| 同一已确认患者有两个 UserId，各自有报告 | 全部视图包含两者；入口账号不同不丢历史记录 |
| 同号但身份资料冲突或测试身份混用 | 不自动扩展不明确账号，返回覆盖问题 |
| LoginName 为 `000001`、为空或重复 | 保留前导零；空或多命中不能误当 UserId=1 |
| 纯医生账号提交 all_tenants | 不获得患者本人权限 |
| 中间表、角色表有重复行 | 训练记录、总数、分页无重复膨胀 |
| 同一账号多租户，记录与用户当前 BusinessId 不同 | 全部视图保留，当前视图按记录级租户筛选 |
| 设备曾转租户，训练记录无可靠历史租户 | 不按设备当前租户声称历史归属 |
| dbwalk userId 不明，或与 project ID 发生数值碰撞 | 不猜测关联；返回来源覆盖不足 |
| 同一个数字主键出现在不同来源表 | 引用和去重保持来源命名空间 |
| 多账号记录交错分布在多页 | 全局排序及总数正确，最近一次来自全局授权范围 |
| 从全部视图切到当前视图，再用另一租户 session_ref | 拒绝范围外记录，不能命中全部视图缓存绕过限制 |
| 旧账号停用或退出历史租户，但本人关联已明确 | 全部视图按历史所有权策略保留相关记录 |
| 不同协议/量表版本记录跨租户汇总 | 列表可汇总，分析继续遵守可比性规则 |
| 登录机构 B，查看属于 A医院的历史记录 | 列表、详情、图表和报告均显示 A医院，不覆盖为 B |
| 同一时段依次出现 A医院、B卫生中心、A医院记录 | 显示多个来源段；不合并成 A医院连续治疗期 |
| 已知机构 ID，但机构已删除或名称字典缺失 | 保留机构引用，显示名称暂缺；当前租户筛选仍按已知 ID 判断 |
| 记录机构未知或可信历史依据冲突 | 显示未确认/待核实，全部视图不猜归属，严格当前视图排除 |

上线前的性能对账包括集合大小、分页执行计划和现有读数上限。必要的非唯一查询索引可另行提出，但不作为本期计划中已经批准执行的 DDL；现有仓储的 1,000/5,000 条限制不能因扩大集合就静默截断并声称完整结果。

## 七、建议采用的产品行为

患者进入康复记录页默认看到“全部机构”，记录卡片展示实际来源机构；可切换“当前机构”。退出某个历史租户本身不使已确认本人的康复历史消失，身份关联和数据来源异常以覆盖说明呈现。

后端以可信登录账号定位入口，以已确认患者账号/来源集合决定本人所有权；是否按租户筛选作为独立查询范围处理。这样可以按用户偏好先实现跨租户康复历史，同时准确保留当前租户视图。

## 参考材料与核查产物

- 用户提供文档：《多租户登录名改造方案(2).md》，使用第一至第四部分。
- 当前结构只读核查：`tmp/patient-tenant-plan-20261029/current-schema.json`。
- 前次重复手机号明细：`tmp/project-phone-audit-20261027/report.md`；其身份分类为当时的线索，不作为本次自动归并授权依据。

本次已实施接口、查询、报告及机构标注改造；保留现有数据库结构，未运行上传文档中的迁移操作。核查产物位于被 Git 忽略的本地目录，不随代码上传。
