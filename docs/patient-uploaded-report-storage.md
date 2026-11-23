# 患者上传报告的数据表与文档存储方案

**评审稿 v0.1 · 2026-11-22**

**归档对象是一份经过脱敏复检的 PDF。** 患者从聊天附件或“我的报告”提交文档，登录业务后端将文件流交给 AI_WebApi。AI_WebApi 将脱敏 PDF 的完整字节存入 MySQL，同时保存从该 PDF 提取的文本、表格数据及其页码。上传原件只在此次处理期间使用。患者退出聊天后，仍可从“我的报告”打开同一份归档。

## 一、两张表

建议为上传报告配置独立的 MySQL 业务库，使用 InnoDB。应用连接池中的每个数据库会话使用 UTC。每份报告有一个稳定的 `report_ref`，每次归档有一个 `revision`。每个版本对应一条首次解读记录，两行在同一事务提交。建表脚本可在空 schema 中执行。[完整建表 SQL](patient-uploaded-report-schema.sql)

| 表 | 一行代表什么 | 主键与关联 |
|---|---|---|
| `patient_report` | 一个归档版本，包含患者归属、实际 PDF 文件、可检索的元数据和可按页读取的证据 | 主键 `id`。对外用 `report_ref + revision` 定位，两者组合唯一 |
| `patient_report_interpretation` | 该版本的首次解读任务及其结果 | 主键 `id`。`report_id` 是指向 `patient_report.id` 的唯一外键 |

### 报告归档表的字段

| 字段 | 类型 | 写入与使用规则 |
|---|---|---|
| `id`、`report_ref`、`revision` | BIGINT UNSIGNED、CHAR(36)、INT UNSIGNED | 内部主键、服务端生成的随机 UUID、从 1 开始的版本号 |
| `owner_user_id`、`upload_business_id` | BIGINT UNSIGNED、可空 BIGINT UNSIGNED | 从已验证的本人身份和机构上下文取得。个人入口的机构为 NULL。纸面出具机构不能作为访问授权 |
| `report_type`、`report_date`、`archived_at` | VARCHAR(32)、可空 DATE、DATETIME(6) | 分类如检验、影像文字报告、评估。无法确定纸面日期时留空。归档时间写 UTC，支持分别按报告日期和上传时间查找 |
| `input_media_type`、`page_count` | VARCHAR(64)、SMALLINT UNSIGNED | 记录实际识别的输入类型和最终 PDF 页数。归档格式统一为 PDF |
| `archived_pdf`、`archived_bytes`、`archived_sha256` | LONGBLOB、INT UNSIGNED、BINARY(32) | 分别保存 PDF 字节、字节数和 SHA-256。表约束检查实际 BLOB 长度与申报长度相等 |
| `evidence_json` | JSON | 保存脱敏后的文字段落和表格行。每块至少含 `block_id`、页码和读取质量。指标另存原文值、单位、参考区间和原文标记，便于回到原页核对 |
| `sanitizer_version`、`extractor_version`、`source_snapshot` | VARCHAR(48)、VARCHAR(48)、BINARY(32) | 记录脱敏和提取规则版本。快照绑定本版本的文件摘要、证据和报告元数据，后续解读据此校验来源 |
| `upload_request_key`、`request_mac`、`mac_key_version` | CHAR(36)、BINARY(32)、SMALLINT UNSIGNED | 同一次提交重传时沿用请求键。服务端用带密钥的 HMAC 核对上传范围、输入类型和原始字节，保存摘要及密钥版本 |
| `archive_state`、`active_ref` | ENUM、可空生成列 CHAR(36) | 状态为 `active`、`superseded` 或 `withdrawn`。仅活动行生成 `active_ref`，唯一索引保证同一报告最多一个活动版本 |

**索引按读写路径设置。** `(owner_user_id, upload_request_key)` 唯一，防止超时重传产生两份报告。本人列表用 `(owner_user_id, archive_state, report_date, id)`，机构内列表在患者字段后增加 `upload_business_id`。列表显式选择引用、类型、日期、状态等小字段，预览时才读取 PDF。

### 首次解读表的字段

| 字段 | 类型 | 用途 |
|---|---|---|
| `id`、`job_ref`、`report_id`、`source_snapshot` | BIGINT UNSIGNED、CHAR(36)、BIGINT UNSIGNED、BINARY(32) | 唯一任务引用、所绑定的归档版本及其内容快照 |
| `state`、`attempt_count`、`available_at` | ENUM、SMALLINT UNSIGNED、DATETIME(6) | 上传时写 `queued`。后续状态为 `running`、`succeeded`、`failed`、`cancelled` |
| `lease_epoch`、`lease_digest`、`lease_until` | BIGINT UNSIGNED、可空 BINARY(32)、可空 DATETIME(6) | 记录当前处理轮次、租约摘要和到期时间，防止旧执行者覆盖结果 |
| `result_json`、`result_sha256`、`receipt_digest`、`interpreter_version` | 可空 JSON、两个可空 BINARY(32)、可空 VARCHAR(64) | 成功时保存带页码引用的解读、结果摘要、提交回执摘要和解释版本 |
| `error_code`、`retryable` | 可空 VARCHAR(64)、BOOLEAN | 记录失败原因码与可否重试。错误字段只保存受控代码 |
| `created_at`、`updated_at`、`finished_at` | DATETIME(6)，完成时间可空 | 记录创建、状态变化及终态时间，统一用 UTC |

## 二、上传过程中各类数据存在哪里

| 数据 | 位置与格式 | 生命周期 |
|---|---|---|
| 原始 PDF、照片、粘贴文本 | BFF 流式转发。AI_WebApi 在每次请求独立的受限临时目录处理，文件用随机名 | 成功、拒绝、超时或取消均在请求结束时清理。建议处理硬时限 10 分钟，每 5 分钟清理已失去活动租约的超时目录 |
| 原始 OCR、渲染页图、遮盖坐标、中间 PDF | 与原件相同的临时区，仅解析与脱敏进程可读写 | 随本次请求清理。临时区排除在静态文件服务、备份和日志采集之外。网关或框架启用磁盘缓冲时也要使用同等受控目录 |
| 通过复检的脱敏 PDF | `patient_report.archived_pdf`，二进制参数直接写入 | 作为患者可预览的归档正本，随报告版本保存 |
| 脱敏文本和结构化字段 | `patient_report.evidence_json`，从最终 PDF 重新提取 | 与 PDF 同版本、同权限、同保留期，引用页码与患者所见 PDF 一致 |
| 首次解读 | `patient_report_interpretation.result_json` | 绑定具体归档版本，归档时为空。报告撤回后同步停止对外提供 |

长期归档和备份的具体年限留给业务方确定。存储侧应支持撤回后立即停止读取，保留期届满后在事务内先清理解读行，再清理报告行。备份按其保留周期到期清除。恢复备份时，回放恢复点之后的撤回和清理变更，核对可见状态后再开放读取。

## 三、一次文件怎样完成归档

1. **确认归属并接收。** 上传走业务接口，身份从登录会话取得。客户端为一次提交生成请求键。首版拟接受 PDF、JPEG、PNG 和 UTF-8 纯文本。检查实际文件类型、字节数、页数及解码资源用量，加密或无法安全解析的文件返回重传提示。
2. **脱敏并重建 PDF。** 将输入渲染为可视页，在页图上清除姓名、证件号、联系方式、病历号、签名及条码等可识别内容，再用干净页面生成 PDF。重建时丢弃输入文件的隐藏文字层、附件、表单、批注和元数据。
3. **复检最终文件。** 重新打开待归档 PDF，核对页数，再做 OCR、标识检测及结构检查。发现残留标识或无法确认处理完整时拒绝整份归档。个别项目读不清、但脱敏复检已通过时，证据块标记为 `uncertain` 或 `unreadable`。文件名由系统生成，原始文件名不入库。
4. **写入短事务。** 用同一份最终字节计算文件大小、SHA-256 和版本快照。插入 `patient_report`，再用新行的 `id` 与快照插入 `queued` 解读记录。提交后才返回 `report_ref` 和 `revision`，页面可显示“已归档”。文件解析和后续解读均在事务外运行。
5. **失败与重传。** 脱敏失败时只留下请求编号和原因码。任一业务行写入失败时整笔回滚。若事务已提交但客户端超时，用原请求键和同一文件重传，服务端在同一患者范围内比对 HMAC 后返回原记录。相同键对应不同内容时返回 409。两个并发事务争用同一键时，由唯一索引裁决，失败方回滚后读取并核验已提交行。

## 四、读取、换版与容量

- **列表与预览。** 列表、证据和文件预览分别读取所需字段。预览先校验 `owner_user_id`、当前机构可见范围及活动版本，再返回 `application/pdf`，并设置 `Cache-Control: private, no-store`。个人入口上传件仅在本人全部范围可见。文件摘要不匹配时停止返回并记录完整性错误。
- **换版与撤回。** 纠错时锁定当前版本，在同一事务内关闭旧版本、取消其未完成任务、归档新版本并建立新的首次任务。若纠错需要恢复已遮盖的文字或重新处理原件，患者须重新提供文件。撤回仅变更可见性，旧引用即时失效。归档阶段计算的请求 HMAC 无法用于取回原件。
- **容量与备份。** 初始建议输入和最终 PDF 分别最多 20 MiB，最多 30 页，证据 JSON 最多 1 MiB。服务在写入前检查大小，数据库连接和服务端的数据包限制应覆盖整条写入及协议开销。按每月 1 万个归档版本、平均每份 PDF 2 MiB 举例，文件正文约增长 **19.5 GiB/月**。证据、索引、副本、日志和备份另计。报告库使用独立服务账号、加密连接及加密备份。[MySQL 数据包限制](https://dev.mysql.com/doc/refman/8.0/en/packet-too-large.html)

**评审时需要确定的业务参数**为长期归档与备份的保留期，以及首版文件大小、页数和临时处理时限。可用实际样本测试脱敏后体积与清晰度，再确定上线限额。

**数据库验证。** 已在隔离的 MySQL 8.0.45 中执行配套脚本。合成单页 PDF 入库后逐字节读回一致，SHA-256 一致。数据库拒绝长度不符及重复请求键，解读表写入失败时同一事务的归档行也已回滚。
