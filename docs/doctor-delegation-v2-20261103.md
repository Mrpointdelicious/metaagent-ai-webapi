# 医生 Sandbox 的 v2 代查授权

2026-11-03，基于患者康复 v2 提交 `4c23a1b` 的增量。工具路径和响应契约仍为 `2.0.0`；患者本人凭证保持原协议。仅给医生 Sandbox 新增独立的服务端授权途径，不把医生转换成患者本人。

配置 `PatientAccess__DoctorDelegationSigningKey`（独立、至少 32 字节）、`PatientAccess__DoctorDelegationValidationUrl`（固定的可信 Sandbox 回调）及 `PatientAccess__DoctorDelegationServiceToken`。默认空配置，不启用医生代查。`PatientAccess__IdentitySigningKey` 仍负责 v2 匿名范围及图片授权。

医生 .NET 工作区在登录和医患授权检查后签发 `doctor_identity_v2.body.signature`，HMAC-SHA256 签名采用独立医生密钥。body 使用 `purpose=doctor_rehab_v2`、`actor_role=doctor`、当前机构、锚定患者账号、医生账号/项目身份、工作区、关系编号与版本，最多有效 300 秒。患者本人验证器不接受医生前缀、用途或角色。

所有医生查询必须显式使用 `current_tenant`。解析器逐次向固定回调发送完整授权字段，只接受 HTTP 204；拒绝重定向，5 秒截止，配置缺失、授权变化或依赖失败均不放行。回调通过 `X-Sandbox-Service` 认证并实时复核医生资格、机构中间表、患者资格和关系有效状态/版本。

医生账号范围固定为所授权的入口患者账号，不因相同手机号或已确认账号组扩大；`all_tenants` 被拒绝。匿名范围指纹包含医生授权字段，关系版本变化使旧记录引用和缓存失效。图片下载通过同一解析器再次回查当前授权，撤销后的旧下载授权不能使用。

医生 Sandbox 的部署脚本创建独立的 v2 容器和报表卷，不覆盖共享 AI_WebApi 实例或其 `.env.ai_webapi`。集成实现、部署和结构核查见 [医生仓库更新说明](https://github.com/Mrpointdelicious/metaAgent-doctor/blob/main/docs/doctor-v2-update-and-dev-comparison-20261103.md)。

病历、会诊和关联处方查询按当前表结构及可信机构范围读取，数量与分页使用一致条件，排除无效病历、已删除处方关联和停用处方。会诊机构的字符串字段与关联病历的患者和机构同时核对。档案投影扩展已有病史、家族史、出生/手术日期、受伤及康复信息，机构照护字段仅在来源机构匹配时输出。患者本人继续保留原 v2 范围指纹；医生授权字段只影响医生范围。

验证：全部非 Smoke 测试共 372 项通过，包括医生授权和当前结构取数回归；配套医生工作区 31 项 Sandbox 测试通过，8 位授权演示患者的三层接口共 24 次真实查询与源库数量一致。project 源连接只读，不迁移或改写源数据。演示账号及关系由医生工作区的独立存储管理。
