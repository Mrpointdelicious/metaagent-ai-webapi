# session_ref 与缓存更新方案：旧版本全部停用

日期：2026-09-15。状态：代码已实施并验证；部署切换、实际密钥更新及调用方缓存清理在服务器发布时执行。详见 ../DEPLOY.md。

## 1. 已确定的迁移规则

按当前 project 单库方案迁移，全部患者入口使用新 `project.dbuser.Id`。旧 ID、旧引用、旧缓存均弃用；不做映射、兼容解析、双版本读取或自动转换。本次不把 rehabplan、模板、报告等迁移数据问题作为实施前置条件，也不开展补数或关联修复。

接口路径、HTTP 方法、请求/响应字段和已有错误码保留。引用字段仍为不透明字符串，内容升级版本；缓存与文件存储仍使用现有实现，不引入 Redis、新数据库表或新的框架。

**生效时点 T0：所有旧实例退出对外流量、新版本与 project 配置完成统一切换的时刻。T0 起，旧 session_ref、旧患者/计划/机构引用、旧快照、旧报告引用及旧图片链接不再可用，包括此前尚未到期的链接。** 实际 T0 写入发布记录，不能用文档日期冒充已生效时间。

已下载到用户设备的文件、已显示的聊天图片无法从服务端撤回；停用指服务端不再解析、读取或重新分发旧资源。

## 2. 当前实现与需要同步修改的位置

| 当前实现 | 迁移要求 |
|---|---|
| IReGoSessionRefProvider 生成 `irego_s_v1_`，签名材料使用 robotdb_main | 改成 v2，并加入新数据库代次 |
| Patient/Plan/Tenant 引用均为 v1 | 同步改为 v2，不保留旧分支 |
| TrustedPatientContext.ContextKey 为 SubjectRef + PatientRef | 增加统一命名空间，避免相同数字 ID 复用旧缓存 |
| 快照/会话绑定使用进程内字典，快照命中发生在查数据库之前 | 新实例冷启动；读写只接受当前命名空间，不返回旧快照 |
| 快照引用为 `ctx_<guid>` | 改为 `ctx_v2_<guid>` |
| IReGoArtifactReference 当前生成 img_v2，同时允许 img_v1/img_v2 | 新制品使用 img_v3，关闭两种旧版本 |
| FileSystem store 根据合法格式的 artifactRef 直接读取文件 | 仅换签名密钥不足以撤销旧文件访问，必须同时限制版本和存储目录 |
| 图片令牌为 `v1.payload.signature` | 新令牌 v2，验证时只接受 v2 |
| 过期图片令牌可以跳转永久制品 URL | 旧版本在重定向之前拒绝，不允许借永久地址继续访问 |

## 3. 一套统一的版本常量

在 Model 内新增小型 `IReGoDataVersion` 常量类，由引用提供器、缓存、内容指纹和制品存储共同引用；不分散硬编码，不做可热切换的双版本开关。

```text
DataNamespace   = project
DataGeneration  = project-20260915
ReferenceVersion = v2
CacheNamespace  = irego:project:project-20260915:v2
ArtifactVersion = v3
ImageTokenVersion = v2
```

DataGeneration 是此次数据库身份空间的固定代次，不随每次启动、每次请求或日期变化。后续只有需要整体作废引用/缓存时才升级代次。同一代次所有实例必须使用相同常量和密钥。

当前工作区公开 DTO 的 SourceSystem 已统一为 project，来源与注册表版本标识同步更新；**内部签名、缓存和制品命名空间使用 project 及独立数据代次。** API contract version 与引用格式版本分别管理，不能仅为 v2 引用而改动公开字段结构。

## 4. 新 session_ref 设计

### 4.1 格式与签名

| 引用 | 新格式 |
|---|---|
| session_ref | `irego_s_v2_<32位小写十六进制签名>` |
| plan_ref | `irego_p_v2_<32位小写十六进制签名>` |
| patient_ref | `irego_patient_v2_<32位小写十六进制签名>` |
| tenant_ref | `irego_tenant_v2_<32位小写十六进制签名>` |

保持 HMAC-SHA256、截取前 16 字节及恒定时间比较方式，保留现有提供器方法签名。新增版本隔离字段，不更换密码算法。

使用固定字段顺序的 UTF-8 JSON 作为签名材料，数值为 JSON 数字，缺失机构为 null，schema signature 按原值写入；不使用受区域格式影响的日期/数字字符串，不手工拼接未转义文本。

```json
{
  "version": "v2",
  "namespace": "project",
  "generation": "project-20260915",
  "purpose": "session",
  "tenant_id": 123,
  "patient_id": 5000,
  "plan_id": 6000,
  "report_id": 6000,
  "schema_signature": "<current-schema-signature>"
}
```

以上 ID 仅为格式示例。plan 的 purpose 为 plan，材料只保留机构/患者/计划；patient 只保留机构/患者；tenant 只保留机构。purpose 防止不同类型引用互用。

签名不包含 requestId、conversationId、缓存生成时间或 TTL。同一代次、同一密钥、同一身份与记录生成相同引用；进程重启、缓存过期后仍可通过数据库当前记录重新计算验证。

session_ref 代表记录身份及现有 schema signature，不代表内容永久不变。同一记录内容更新但身份/schema 未变时，引用可以不变；报告制品通过内容指纹生成新的 artifact_ref。

### 4.2 验证顺序

1. 保持现有认证及请求结构校验优先。
2. 进入单次/纵向分析服务后，先检查用户实际提交的 session_ref / session_refs 版本，再查询患者和记录。这样旧引用不会被“暂无报告”等结果掩盖。
3. 已知 v1 引用立即返回停用提示，不查旧库、不检查旧缓存、不尝试旧密钥。
4. 当前 v2 引用按固定前缀、长度、十六进制格式校验；随后使用当前患者上下文和当前数据库记录重新计算签名。
5. 不属于当前患者、被篡改或不存在的引用沿用现有 SESSION_REF_NOT_FOUND / SESSION_REF_CONTEXT_MISMATCH 处理，不提供跨患者候选。
6. 显式引用列表中只要含一个已停用引用，整个请求失败；不得丢弃旧项后缩小比较窗口，不得自动改成“最近一次”。

若请求带有旧引用但 selector 选择其他模式，也应明确拒绝其旧引用，而不是静默忽略。缺失字段、非法类型等仍走现有参数错误路径；未知版本作为无效引用处理，不谎称它曾经有效。

单次/纵向报告生成继续调用分析服务，并完整透传失败码和 patient_message；只补版本入口校验与提示，不改业务分析算法。

### 4.3 明确告知旧引用不可用

合法单次引用入口的旧引用错误保持 HTTP 404 与既有响应结构；公开纵向接口原本就禁止 session_refs，继续返回原 400 / INVALID_REQUEST 并补同一停用提示，不放开该输入。内部纵向服务也会拒绝旧引用。

```text
status = failed
data = null
reason_codes = ["SESSION_REF_NOT_FOUND"]
patient_message = "系统已切换至新版数据。旧会话引用已停用，不再可用，请重新查询训练记录后选择记录。"
```

在 ToolResponseDto 的现有字段内返回，不增加响应字段或新枚举。内部可记录诊断标签 `legacy_reference_retired`；不得记录密钥或完整患者资料。其他“记录不存在”错误维持其原有提示，不一律误报为版本停用。

## 5. 缓存更新规则

### 5.1 快照与会话绑定

```text
ContextKey = CacheNamespace + "|" + SubjectRef + "|" + PatientRef
Snapshot 字典键 = ContextKey
SessionBinding 字典键 = (ContextKey, SessionRef)
SnapshotRef = "ctx_v2_" + Guid.NewGuid().ToString("N")
```

直接修改 `TrustedPatientContext.ContextKey` 的内部计算，并让所有读写使用该值。PatientRef 在此仍指现有内部 `patient:<newId>`；公开签名 patient_ref 由提供器生成，二者不能混淆。

保留原本按主体/患者共享快照的行为，不引入 conversationId 改变跨会话缓存策略。会话绑定键带 ContextKey，避免同一记录在不同上下文的缓存写入相互覆盖。

缓存 Store/TryGetSnapshot/StoreSession/TryResolveSession 均校验当前 ContextKey 前缀；会话绑定还须为 v2 引用。旧 ContextKey、旧快照引用、旧 SessionBinding 拒绝写入或视为未命中，不能给旧对象简单加前缀后复用。内部 SessionBindings 与公开快照中的引用也要属于当前版本。

TTL 保持现有默认：快照 300 秒，沿用现有 30–3600 秒约束；ForceRefresh=true 仍跳过快照缓存，重新查询并替换。新版本启动不导入旧内存快照、不恢复旧绑定。

缓存未命中时按当前患者重新计算并存入；session_ref 本身不因缓存 TTL 到期失效。此次不新增分布式缓存；内存缓存跨实例不共享仍是当前实现边界。

### 5.2 watermark 与内容变化

当前 watermark 由时间及数量组成，不含完整内容哈希。仅增加代次前缀以区分新旧：

```text
project:project-20260915:v2:db:<现有时间与计数部分>
```

它仍是观测标识，不能当作完整内容版本，也不能据此承诺立即感知所有数据库更新。快照新鲜度仍由 TTL / ForceRefresh 控制；需要立即刷新时使用现有 ForceRefresh。

报告生成保留当前内容指纹机制，把内部 source_namespace/generation 写入指纹材料；单次及纵向 scope 的版本升级为 v2，内容 profile 升级为 v2，实际 evaluation rule 版本在算法未变时不改。同样内容在同代次可复用制品，内容变化则生成新制品，旧数据库代次不能命中。

## 6. 报告制品和图片缓存同步停用

### 6.1 新制品引用

`IReGoArtifactReference.Create` 改为 `irego_img_v3_<64位十六进制签名>`；派生密钥 purpose 使用 `ai-webapi/irego/artifact-ref/v3`，规范化载荷包含：

```text
artifact_schema=irego-artifact-v3
source_namespace=project
data_generation=project-20260915
patient_ref=当前v2患者引用
training_scope_ref=当前v2单次/纵向scope
source_snapshot=当前内容指纹
template_version=现有实际模板版本
format=png
```

只接受 img_v3；删除原有 img_v1/img_v2 合法分支。Create、TryGet、Store、IssueAccessToken、ResolveArtifact 均执行一致检查，覆盖 DurableFileSystem 和 EphemeralMemory 两种模式；不能仅在一个 HTTP 入口拦截。

模板视觉内容未改变时，不靠虚增模板版本作隔离；隔离由代次、artifact v3 和内容 profile 负责。

### 6.2 存储与 TTL

保持现有 Docker volume，实际存储根目录改为：

```text
/app/data/patient-reports/project-20260915-v2
```

新实例只读写新子目录，不回退读取父目录，也不复制/重命名旧 PNG 到新目录。旧文件可暂留作离线存档；保留文件不代表接口继续可用，本次无需删除卷。

EphemeralMemory 在新实例中冷启动，只接受新制品引用，默认 CacheTtlSeconds=600、容量约束保持。DurableFileSystem 是持久制品存储，**不能误认为等 600 秒就会自动清除旧文件**；它通过目录和引用版本隔离。

### 6.3 图片访问令牌和旧 URL

新短链接格式为 `v2.<payload-base64url>.<signature-base64url>`，签名材料包含版本、namespace、generation、artifact_ref 和 expires_at。保持现有 URL 路径，默认 UrlTtlSeconds=300，两个 store 只验当前版本和当前密钥。

两类旧地址统一明确停用：

- `/api/ai/patients/report-images/{旧v1令牌}`：HTTP 404，文本“旧报告链接已停用，不再可用，请重新生成报告。”
- `/api/ai/patients/report-artifacts/{旧img_v1或img_v2}/content`：HTTP 404，同上。

沿用旧无效资源的 404，不新引入 410 作为版本停用状态。错误正文只补充可读提示；成功响应仍为图片。新 v2 令牌自然到期的处理沿用当前规则，但只允许跳转到当前 img_v3 制品；旧版本在任何过期判断和重定向之前被拒绝。

成功和错误响应统一使用 `Cache-Control: private, no-store, max-age=0`，保留 nosniff，防止新响应被浏览器或代理保存后绕过服务端检查。已存在于客户端的历史副本无法保证远程清除，调用方应停止展示其为“当前报告”。

## 7. 部署配置与切换

```dotenv
# 在现有 project 单库配置之外更新以下部署项。
IReGoTools__SessionRefSigningKey=<此次生成并持久保存的新独立密钥>
IReGoTools__ReportImage__SigningKey=<此次生成并持久保存的另一把独立密钥>
IReGoTools__ReportImage__StoragePath=/app/data/patient-reports/project-20260915-v2
IReGoTools__SnapshotTtlSeconds=300
IReGoTools__ReportImage__UrlTtlSeconds=300
IReGoTools__ReportImage__CacheTtlSeconds=600
```

两把新密钥分别使用至少 32 个密码学随机字节生成、编码后存入部署密钥配置，满足现有字符串长度检查。不要每次启动重新生成，不配置旧密钥候选集；不更换 DifyServiceBearerToken 或医院运营密钥。

新密钥是补充隔离，不能代替版本拒绝和旧永久 URL 阻断。

切换顺序：

1. 完成现有 project 实体/仓储适配和本方案的小范围引用/缓存修改，在隔离实例验证。
2. 准备新密钥、新存储子目录和统一新 Id 的调用方配置，不导入旧缓存。
3. 暂停对外请求接入，等待旧请求结束，停止所有旧实例；禁止旧版与新版同时对外服务。
4. 启动所有新实例并检查代次、连接、密钥配置一致，缓存冷启动。
5. 调用方刷新患者绑定，清空旧引用、旧记录列表、旧快照及报告 URL；发出停用公告。
6. 完成新链路及旧引用拒绝的冒烟检查，恢复流量并记录 T0。

若发布失败，保持入口暂停，修复新版本或回退至仍执行新版本拒绝规则的构建；不要重新开放旧引用服务。普通代码回退保持此次 generation 和密钥；若要另行恢复旧系统，那是新的迁移决策，不能悄悄恢复已公告停用的引用。

## 8. 调用方通知与恢复流程

### 统一通知文案

> 系统已完成数据升级。升级前的患者标识、会话引用、查询缓存和报告链接已停用，不再可用。请重新选择患者并查询训练记录，需要报告时重新生成。

该文案用于 T0 后的发布说明、调用方升级提示和旧引用错误提示。此次仅交付通知文案，不实际向第三方发送消息。

### Dify / 前端处理

1. 绑定新 dbuser.Id，旧患者 ID 不再放入 system_context.user。
2. 清空应用保存的 session_ref / session_refs / plan_ref / patient_ref / tenant_ref / snapshot_ref / artifact_ref / 图片 URL。历史聊天文本可保留，但其中工具结果标为历史失效，不参与新请求。
3. 使用现有患者上下文接口（需要时 ForceRefresh=true）及历史目录接口重新读取数据。
4. 用户原先选择的旧记录失效后，提示重新选择，不把“旧第 3 条”自动解释成“新第 3 条”，不自动换成最新报告。
5. 明确收到旧引用停用提示后不原样重试；取得新引用后再调用分析或生成报告。

只持有旧 ID 的客户端需要重新通过现有可信患者选择或手机号查询绑定；不让模型按姓名猜测身份。

## 9. 预计文件改动

| 文件 / 位置 | 改动 |
|---|---|
| Model/AITools/IReGoDataVersion.cs（新增） | 统一 namespace/generation/版本常量 |
| IReGoSessionRefProvider.cs | v2 生成、purpose 分隔签名与校验，只接受当前版本 |
| IReGoDomainModels.cs | ContextKey 加命名空间 |
| InMemoryPatientContextSnapshotCache.cs | 当前版本读写检查、上下文+引用组合键 |
| MultisourcePatientContextService.cs | ctx_v2 快照引用及当前版本绑定 |
| IReGoPatientEvaluationService.cs | 只调整 watermark 代次前缀 |
| IReGoSessionAnalysisService.cs、IReGoLongitudinalAnalysisService.cs | 查库前旧引用拒绝及现有错误消息 |
| IReGoArtifactReference.cs、IReGoReportContentFingerprint.cs | img_v3、内部 project 代次、scope/profile v2 |
| FileSystem/EphemeralIReGoReportArtifactStore.cs | v2 令牌、所有制品入口限制为 v3 |
| IReGoToolsController.cs | 旧图片/制品明确提示，旧地址不可重定向；缓存响应头 |
| 配置示例与部署配置 | 新密钥、目录、既有 TTL |
| 相关现有测试 | 新版确定性/隔离、旧版拒绝、缓存重建、永久制品入口测试 |

以上是在数据库交互改造之外为“旧引用与缓存全部停用”所必需的局部改动；公共服务接口签名、业务算法、工具路由和 DTO 结构保持。

## 10. 验收标准

- 同一材料生成稳定 v2 引用；换患者/机构/计划/报告/schema/代次产生不同签名；不同 purpose 不能互换。
- v1 session_ref 在新密钥、旧密钥相同与否、旧缓存是否存在、患者是否有报告等情况下都被拒绝，并出现明确停用文案。
- 单次与纵向分析、两类报告生成均拒绝旧引用；混合新旧列表整体失败，不降级查询。
- 新引用在清空缓存、快照到期、实例重启后仍可通过当前记录解析。
- 预置旧 ContextKey/快照/绑定不被读取；新快照首次 miss、再次 hit、ForceRefresh 后重建；保持患者上下文隔离。
- 旧 img_v1/img_v2 文件即使留在卷内、甚至误放到新目录，访问也返回停用提示；旧 v1 token 即使未过期也失败；过期旧 token 不得重定向成功。
- 当前 img_v3 制品相同内容复用，内容变化重新生成；新实例只使用新目录，临时内存模式执行同样版本限制。
- 所有公开路径/字段/既有 reason code 保持，正常业务结果无额外改动；HTTP 图片响应头和旧资源错误提示符合本方案。
- 新部署只使用 project、新 Id、新密钥与当前代次；不存在继续接流量的旧实例。

实施后执行相关现有单元测试、实际缓存/制品入口集成测试及接口冒烟。设计阶段不声称这些测试已经通过，也不将旧数据审计结果加入本次验收门槛。
