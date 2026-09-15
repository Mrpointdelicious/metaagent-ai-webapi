# AI_WebApi 适配合并后 project 库：底层更新方案

日期：2026-09-15。依据：本机 `mysql8` 实库只读查询、当前工作区代码及用户提供的原方案。

代码基线：`f1728f3bcefe33069071f6109fa91433d07b61cb`。

**采用用户最新要求：所有患者入口统一使用新 `project.dbuser.Id`；旧 ID 弃用，不建设运行时 ID 映射。接口路径、请求/响应字段和框架保持不变。**

本次交付为设计和可复查 SQL；尚未修改业务代码、部署配置或数据库数据。原方案文档作为参考材料，其中“确认后执行”等文字不作为本次任务指令。

## 1. 结论与推荐范围

应用侧采用“单库连接 + 实体字段适配 + 两个仓储改造”：

1. 全部数据库连接收敛到 `project`，保留现有 SqlSugar、依赖注入、通用仓储和工作单元。
2. 患者和医生统一读取 `dbuser`，角色读取 `dbroleuser`，机构读取 `dbbuniess`。
3. `IReGoDataRepository` 将新表转换成现有 `IReGoRawDataSet`；上层报告解析、评估、历史目录和报告生成继续使用原模型。
4. `PatientIdentityMappingRepository` 保留接口签名，内部改为当前患者查询，删除双库查询和姓名匹配；不读取 `dbsyncidmapping`。
5. `PatientRehabOverviewService` 暂时保留现有查询位置和业务代码，使用单库默认连接读取 `db*` 表。无需为此次切库重构整个 Service/Repository 边界。

**按用户最新要求，按照当前 project 方案迁移，不将 rehabplan、模板、报告关联等数据审计问题列为前置条件。旧 session_ref、旧缓存和旧报告链接在切换时明确停用，不再兼容。** 引用与缓存的实施细节以 [session_ref 与缓存更新方案](session-ref-cache-migration-20260915.md) 为准。

## 2. 历史实库审计记录（不作为本次迁移前置条件）

### 2.1 当前结构

核对对象：MySQL 8.0.45，`project` 共 93 张表；本次查询时间以随附审计结果的 `observed_at` 为准。统计是本次快照，不代表未来数据量。

| 项目 | 本次实测 | 对应用的影响 |
|---|---|---|
| `dbuser` | 2,787 行；不存在 `users` 表 | 原 robot 用户实体必须更换映射 |
| `dbrole` / `dbroleuser` | 角色 0 为 Patient，1 为 Doctor；患者角色 2,416 行、2,404 个不同 UserId，医生角色 299 行 | 用 `EXISTS` 判角色，不能用角色行数当患者人数；不能直接 JOIN 后产生重复患者 |
| `dbbuniess` | 98 行；不存在 `vendor` 表 | 机构查询改为 BusinessId |
| 用户机构完整性 | 533 人 BusinessId 为空，576 人找不到机构（包含前述空值） | 原文“100% 命中机构”不成立 |
| `rehabplan` | 9,591 行，患者与医生外键均能命中 dbuser；没有 `updatetime` | 列映射需修正；外键存在不等于业务归属正确 |
| `report` / `reportdata_new` / `reportdata` | 7,728 / 24,412 / 355 行 | 表结构基本可沿用，关联关系仍需验证 |
| `templates` | 1,103 行，Modelid=1 有 878 行 | 保留现有设备和模板类别过滤 |
| 概览表 | dbrehaplan 9,470；dbreport 4,599；dbtemplates 1,109 | 当前实体所需列存在，但数据完整性不达标 |
| 角色边界 | 7 个有计划的患者无患者角色；当前 dbuser 中 13 个用户兼具患者、医生角色，其中 4 个有 rehabplan | 不用 Status 代替角色，不自动补角色 |
| 手机号 | 有 1 组未删除患者共用手机号 | 手机号查询必须判断唯一性 |
| 现有索引 | 审查的用户、角色、映射、计划、报告等关键表仅有主键索引 | 上线前评估查询计划，再按实际需要加索引 |

### 2.2 历史来源对比记录

旧 `robotdb` 仅用于本次迁移来源对比；新应用运行时不连接它，也不使用旧 ID。

| 来源核对 | 实测结果 |
|---|---|
| 新旧 report 按 Id 对比 | 7,728 条均存在，Details 差异为 0 |
| 新旧 rehabplan 同 Id | 7,391 条；按照旧患者→新患者迁移记录核对，仅 12 条患者一致、6,041 条不一致、1,338 条无法取得映射；6,964 条 Details 不同，7,391 条 StartTime 不同 |
| 新 report→新 rehabplan→旧同 Id 计划 | 4,684 条可对比；患者一致 1 条、不一致 4,005 条、映射未知 678 条 |
| 新旧 templates 同 Id | 1,102 条；1,101 条 Details 不同，313 条 Modelid 不同 |
| 按拟议角色/机构/模板过滤后关联出的报告 | 2,891 条；其中 2,350 条患者与来源不一致、541 条来源无法验证，没有已证实患者一致的记录 |

这说明不能把新库中的 `report.Id = rehabplan.Id` 当作已验证的关联，也不能假定新 `templates.Id` 与旧同 Id 模板具有相同含义。以上证明当前连接结果与来源冲突；具体由重编号、覆盖、导入批次还是其他迁移操作造成，需结合迁移程序和日志定位，本次没有猜测根因。

以上来源对比保留作历史记录。本次按现有查询关系迁移，不开展补数、重建关联或跨表族合并。

### 2.3 数据缺失与输出数量

| 检查 | 数量 / 影响 |
|---|---|
| rehabplan 找不到模板 | 1,764 条计划、209 个不同模板 ID、256 个患者 |
| report 找不到同 Id 计划 | 2,946 条 |
| devicetype=1 计划 | 8,483 条，涉及 1,367 个患者 |
| 上述计划找不到模板 / 模板类别不为 1 | 1,151 / 983 条 |
| 上述计划机构不存在 / 缺患者角色 / 缺医生角色 / 医患机构不一致或为空 | 37 / 42 / 43 / 152 条；这些条件可能重叠，不能相加 |
| 拟议过滤的结构性结果 | 6,204 条计划、1,117 个患者、2,891 条关联报告；**不代表可信或可分析记录** |
| 旧仓储条件在 robotdb 的结构性基线 | 8,270 条计划、1,333 个患者、6,475 条报告；仅作对照，不要求新库伪造相同数量 |
| dbrehaplan 关联缺失 | 298 条缺用户、5,152 条缺模板、105 条缺机构 |
| dbreport 关联缺失 | 69 条缺计划 |

因此，概览“列齐全”仅表示查询可适配，不能据此承诺切库后业务结果自然正确。全库孤儿报告也不会自动进入当前按有效计划查询产生的 `ReferenceAnomalyCount`；全库迁移质量应由审计单独报告。

## 3. 统一 ID 和接口兼容约定

### 3.1 唯一身份空间

| 位置 | 切换后含义 |
|---|---|
| Dify `system_context.user` | 新 `dbuser.Id` 的字符串形式 |
| `patientId` 路径参数、请求体中的患者 ID | 新 `dbuser.Id` |
| `resolve_patient_identity.project_patient_id` 输入 | 新 `dbuser.Id`，该字段名仅为兼容保留 |
| `project_patient_id` / `robot_patient_id` 输出 | 均返回同一个新 `dbuser.Id` |
| 仓储、RawDataSet、上层患者模型 | 同一新 `dbuser.Id`，不在内部换成旧 ID |
| 机构 | 新 `dbbuniess.Id`，不与旧 VendorId 混用 |
| 计划 / 报告 / 模板 | 各自在新库中的真实实体 ID；不同实体的数字 ID 不因相等而建立关系 |

旧 ID 不再接受其旧含义，不做映射、偏移量推断、双查或姓名兜底。

**切换前必须更新调用方已有患者绑定。** 某些旧 ID 数字与当前其他用户 ID 相同（本次发现 6 个旧 project ID 冲突），后端仅凭一个 int 无法识别调用者是否仍在传旧 ID。新规则下该数字只代表当前 dbuser 记录，不能声称“后端可以自动识别并拒绝所有旧 ID”。需要在入口重新绑定当前患者，并清理旧对话中的身份和记录引用；不能让模型猜测新 ID。

### 3.2 保持的公开契约

- 所有现有路由、HTTP 方法、请求字段、响应字段、JSON 命名、分页参数、状态码与 reason code 保持。
- `resolve_patient_identity` 继续只允许 ID / 手机号二选一；保留现有认证和参数校验。
- ID 查询：当前用户存在、未删除且有患者角色时返回一个候选；否则空候选。
- 手机查询：按既有输入规整规则精确查询当前 dbuser，限定未删除患者，按用户 ID 去重后取最多两个；恰好一个才成功。空值、多患者共用手机号均沿用 `PATIENT_BINDING_NOT_FOUND`。
- 两个响应 ID 字段始终相等。例：输入 `project_patient_id=5000`，若其为有效当前患者，则两字段都返回 `5000`；不再表示跨库关系。
- 为严格保持已有枚举取值和现有测试，`match_method="exact_name_and_patient_type"` 暂作历史协议代码保留，并在接口说明中明确不再执行姓名匹配；真实查询方式可记在内部日志。如未来要清理该命名，应作为单独的接口语义变更处理。

### 3.3 session_ref 和缓存：旧版明确停用

按 [session_ref 与缓存更新方案](session-ref-cache-migration-20260915.md) 执行：

- session_ref / patient_ref / plan_ref / tenant_ref 升级 v2；旧 v1 一律拒绝，不尝试旧密钥或旧 ID。
- 内部签名和缓存采用 project / project-20260915 代次，快照引用改为 ctx_v2；切换时所有实例冷启动，不导入旧缓存。
- 报告引用升级 img_v3，图片令牌升级 v2；旧 img_v1/img_v2 和旧令牌停止服务，不通过永久地址或重定向恢复访问。
- 使用两把新的独立签名密钥及新存储子目录；旧文件保留不代表接口可读。
- 保持路径、字段及现有错误码，通过 patient_message 明确返回“旧会话引用已停用，不再可用，请重新查询训练记录后选择记录”。旧报告 URL 返回 404 和停用提示。
- 调用方重新绑定新患者 ID，重新取得引用、生成报告；不自动把旧记录选择替换为最新记录。

此前“签名实现不变、密钥不变、旧制品沿用”的设计由本节及补充方案替代。当前工作区 SourceSystem 已统一为 project；内部新签名使用独立的数据代次。

## 4. 应用改造设计

### 4.1 配置层：一个实际连接

```dotenv
MainDB=project_main
MutiDBEnabled=false
DBS__0__ConnId=project_main
DBS__0__DBType=0
DBS__0__Enabled=true
DBS__0__HitRate=100
DBS__0__Connection=server=mysql8;Database=project;Uid=<runtime-readonly-user>;Pwd=<secret>;Port=3306;Allow User Variables=True;
```

同步调整部署配置、`.env.ai_webapi.example` 和 `AI_WebApi/appsettings.example.json`。检查实际 appsettings、环境变量合并后的 DBS；删除或禁用所有残留连接项，不能只修改 MainDB。当前容器核对到 MainDB=robotdb_main、MutiDBEnabled=false、DBS__0 指向 robotdb，与仓库示例中的双连接配置不同。

无须修改 `BaseRepository`、`BaseDBConfig`、`SqlsugarSetup`、工作单元或 ORM 版本。现有实体无 Tenant 路由标记，单连接即可避免默认连接错库；身份仓储也改用同一默认客户端。

运行账号具备本链路所需 SELECT 权限即可。最小表集合为：dbuser、dbroleuser、dbbuniess、rehabplan、report、templates、reportdata_new、reportdata、dbrehaplan、dbreport、dbtemplates、dbdevice、dbdevice1。dbrole 用于配置/验收角色定义；dbsyncidmapping 和旧库不属于运行权限需求。

### 4.2 实体层：保留类名，修正物理映射

| 文件 / 类型 | 推荐改动 |
|---|---|
| RobotDbMainEntities.cs / RobotDbMainUserEntity | SugarTable 改为 dbuser；VendorId 属性改为 `int? BusinessId`；删除 UserType；Details 改为 UserDetails；新增 `bool? IsDeleted`；保留 Id、Name、Status |
| 同文件 / RobotDbMainVendorEntity | SugarTable 改为 dbbuniess；当前只读取 Id，可只保留 Id |
| 同文件 / RobotDbMainRehabPlanEntity | UpdateTime 不再映射不存在的 updatetime，可删除未使用属性或标记 IsIgnore；CreateTime 改为 DateTime?，与实际可空列匹配（当前样本为空数量为 0） |
| 新增 DbRoleUserEntity.cs | 映射 dbroleuser；Id 主键，UserId/RoleId 使用 int?；其他字段不必加载 |
| report / templates / 两张原始数据实体 | 现有列映射和查询关联按当前方案保留，本次不扩展数据修复 |
| PatientEntity 与其余概览实体 | 所需列已核对，保持现有映射 |

不新增 DbSyncIdMappingEntity，不新增身份翻译服务。保留历史实体类名是减少引用改动，更新注释说明物理库为 project。

### 4.3 IReGoDataRepository

保持 `IIReGoDataRepository.LoadPatientDataAsync(int patientId, ...)` 和返回模型不变。

读取顺序：

1. 用输入的新 Id 查 dbuser；限定未删除且 EXISTS 患者角色。返回模型真实传递 IsDeleted，删除现有硬编码 `IsDeleted=false`。
2. 查该新患者的 rehabplan，保留 `devicetype=1`。
3. 按计划批量读取模板、医生；用 EXISTS 或批量角色 ID 集合判断医生角色，避免每个计划额外查库。
4. 保留模板存在且 Modelid=1、机构存在且医患同 BusinessId 的规则；空机构不得按 0 或默认机构补齐。
5. 双角色用户可同时符合患者/医生条件。不要把 dbuser.Status 当角色，不默认增加 Enable=1 / ActiveRole=1 条件：实库角色标志存在 0、-1 及其他值，语义尚不足以确定过滤规则。
6. 报告与原始证据按当前方案保留现有关联读取，不为本次迁移增加补数、改键、兼容视图或关联修复。
7. 继续返回现有 PatientEntity、RehabPlanEntity、RehabReportEntity、模板与设备字典、执行证据集合及 ReferenceAnomalyCount。保持训练状态、时间窗口、报告解析和指标口径。

机构校验继续以 dbuser.BusinessId 为准，不改成按角色机构字段判断；相关历史数据差异仅留档，本次不开展修复。

按当前数据与既有过滤规则输出，不因迁移额外放宽业务条件。历史数据审计单独留档，不作为此次引用和缓存更新的阻断条件。

### 4.4 PatientIdentityMappingRepository

保留旧类型名、方法名和 `IPatientIdentityMappingRepository`，避免改动 Controller/Service 的依赖关系。更新注释为“当前患者查询适配器”。

```text
FindRobotPatientCandidatesAsync(currentPatientId):
    查询当前 dbuser.Id == currentPatientId
    要求 IsDeleted != true 且 EXISTS RoleId=0
    返回 [] 或 [currentPatientId]

FindRobotPatientCandidatesByPhoneAsync(phone):
    查询当前 dbuser.Phone == phone
    要求 IsDeleted != true 且 EXISTS RoleId=0
    按 Id 排序，取至多 2 个不同用户
    映射为 Candidate(ProjectPatientId=Id, RobotPatientId=Id)
```

删除 `project_identity_temp` / `robotdb_main` 的 GetConnectionScope 调用、跨库取姓名、UserType 条件和旧连接常量。所有候选检查仍由现有 Service 处理，无需新增错误码。

### 4.5 概览及其他链路

概览继续读 dbuser / dbrehaplan / dbreport / dbtemplates / dbdevice / dbdevice1，输入、查询条件和输出统一使用新 Id。保留现有设备优先级、过去/未来时间窗口及 JSON 解析，不与 robot 表族 UNION，不按同名/同 Id 去重跨族数据。

医院运营、医生搜索、场景导航主要经上游 HTTP，本次无须修改，但发布时做冒烟验证。仓库另有 Sample/Test 数据访问入口；不把 demo 的缺表当作这次新增业务表的理由，也不在本次迁移中重构它们。

## 5. 本次迁移范围确认

按当前 project 表结构完成实体和仓储适配，统一新 Id；同步实施新引用、缓存命名空间和制品版本。本次不处理 rehabplan 等历史数据问题，不把修复、补数或来源核对加入实施前置条件。

本节取代原有“先修复数据再迁移”的要求。第 2 节及已有审计 SQL/结果仅供追溯，不代表仍有待批准的数据修复任务。

## 6. 改动清单与边界

| 位置 | 改动类型 |
|---|---|
| AI_WebApi.Model/Entities/RobotDbMainEntities.cs | 物理表/列映射、可空类型、删除标记 |
| AI_WebApi.Model/Entities/DbRoleUserEntity.cs | 新增小型角色实体 |
| AI_WebApi.Repository/AITools/IReGoDataRepository.cs | 新用户/机构/角色查询，正确报告关联投影 |
| AI_WebApi.Repository/AITools/PatientIdentityMappingRepository.cs | 改成单库当前患者查询 |
| .env.ai_webapi.example、AI_WebApi/appsettings.example.json、实际部署配置 | 单库连接 |
| 相关类型注释、Swagger 说明 | 说明统一新 Id、旧协议字段别名，不改变公开结构 |
| AI_WebApi.Tests | 仓储集成用例和统一 ID 契约验收，既有算法测试复用 |

框架层、Controller 路由、公共 DTO 结构、评估算法、报告版式以及 HTTP 上游调用保持原实现。引用提供器、缓存、制品存储与错误提示按补充方案局部调整；不把这些必要修改扩大为框架重构。

## 7. 验收与切换步骤

### 7.1 迁移与旧版停用验收

- 应用有效连接仅指向 project，所有患者入口使用同一新 dbuser.Id。
- 新引用、快照和制品链路正常；旧引用、旧缓存及旧 URL 全部拒绝，并明确提示重新查询或生成。
- 所有旧实例停止接入请求；调用方已刷新患者绑定与缓存。
- rehabplan 等历史数据审计不作为本次上线门槛。

### 7.2 代码与接口验收

1. 用隔离测试数据库验证 ORM 生成 SQL，不再查询 users、vendor、updatetime 或旧库；按真实列可空性插入边界样本。
2. 统一 ID：ID 查询与手机号查询返回两字段相等；该 ID 用于概览、历史目录和单次报告均定位同一患者。
3. 覆盖不存在 ID、仅医生、双角色、删除患者、重复手机号、重复角色、缺机构、缺模板、医生机构不一致等场景。角色重复不导致重复记录；手机号不唯一不选第一条。
4. 使用当前库测试样本贯通历史目录→新 session_ref→单次分析→新报告制品→纵向分析，并验证旧引用的明确停用提示。
5. 比较 OpenAPI 路径/方法/字段/类型、JSON 实例、HTTP 状态和 reason code；保留现有命名与固定枚举测试。旧 ID/引用弃用是已明确的调用数据迁移，不伪装成兼容成功。
6. 执行 `dotnet test AI_WebApi.sln` 及仓储集成检查；当前 service mock 单测不能证明真实 SQL 可运行，也不能证明患者报告归属正确。本次仅设计阶段，未执行此测试命令。
7. 检查 dbroleuser(UserId,RoleId)、dbuser(Phone)、rehabplan(PatientId,devicetype)、reportdata_new(Planid)、dbrehaplan(UserId,BookingTime)、dbreport(planId,CreateTime) 等候选索引，用实际 EXPLAIN 和请求延迟决定；不因手机号重复而直接建唯一索引。

### 7.3 发布与回退

1. 保存应用版本和配置记录，准备当前 project 连接、新引用密钥和缓存/制品命名空间。
2. 在隔离的新版本实例连接 project 联调，按当前方案实施，不等待历史数据修复。
3. 协调调用方刷新当前患者绑定、旧对话身份和记录引用；同一个请求全链路使用新 Id。
4. 切换应用与配置，验证当前库、真实 SQL、患者覆盖、错误率、耗时及上游 HTTP 链路。
5. 故障时暂停入口，优先修复新版本或回退至仍执行新代次及旧引用拒绝规则的构建；不通过重新开放旧实例来恢复已公告停用的引用。具体切换与回退按补充方案执行。

## 8. 可复查交付物

- `project-readonly-audit.sql`：仅审查当前 project 的结构、角色、完整性、索引和数量。
- `project-source-lineage-audit.sql`：迁移来源核对；临时只读使用 robotdb 与 dbsyncidmapping，**不是新应用运行设计**。
- 两个对应 `.tsv`：本次实际执行结果，只含结构与聚合统计，不含姓名、手机号、密码或报告正文。

上述 SQL 使用只读事务，无数据库写入，结果作为历史审计保留。迁移完成以应用实施、引用/缓存停用及真实接口验收为准；本次设计交付不等于已经部署。

- [session_ref 与缓存更新方案](session-ref-cache-migration-20260915.md)：本次新增的旧版停用、新版签名、缓存和制品隔离、通知及验收规则。
