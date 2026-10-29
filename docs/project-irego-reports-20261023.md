# IReGo 接入 project 康复报告

> 历史记录（2026-10-23）。下述旧身份别名、部署及验证描述不代表本版接口；本版以 [患者康复 2.0.0 集成说明](patient-rehab-v2-contract.md) 为准，旧契约已移除。

根据已确认的患者 5776 联调需求，将现用后端的 IReGo 查询源从 robotdb 改为 `project.dbrehaplan → project.dbreport`。本次沿用现用后端的 IReCog、量表、医院运营和导航实现。

- 身份入口以 `project.dbuser.Id` 为准，校验启用、未删除及患者角色；不再按姓名匹配 robotdb。响应继续保留旧字段名 `robot_patient_id` 作为兼容别名，值等于 project 患者 ID，`match_method=project_primary_key`。
- 计划按 `dbrehaplan.UserId` 归属患者；报告按 `dbreport.planId=dbrehaplan.Id` 关联。仅纳入设备 `DeviceType=1`、模板 `ModelId=0` 的 project IReGo 记录，不将未关联设备或其他设备报告算作 IReGo。
- 历史计划所属机构、设备当前机构、患者当前机构可能不同，当前机构不能反推历史归属。患者归属以患者主键和计划外键为准，输出保留 `IREGO_HISTORICAL_INSTITUTION_CONTEXT` 来源说明，不修改这些业务字段。
- session/plan/patient/tenant 的不透明引用升级为 v2，签名材料包含 `project.dbrehaplan:20261023`，旧 robot 会话引用不再匹配；内容指纹与制品身份使用同一新来源命名空间，冷启动缓存。已有历史图片文件不删除、不冒充新源报告。
- 查询设定患者计划 1000 条、报告 5000 行的读取上限，超过上限明确失败。

5776 的 project 康复表共有 188 个计划、26 行非空报告。按实际设备与模板分类，IReGo 为 23 个计划、2 份已有报告。另一个部署包读取的 `project.rehabplan/report` 表对该患者没有记录，因此没有直接使用那个包替换运行后端。

现用源码目录：`D:/Project/AI_WebApi`，分支 `codex/project-report-source`。运行镜像为 `ai_webapi:metaagent-project-dbreport-20261023`；MetaAgent 的设备引用校验同步支持新版本，运行镜像为 `meta_agent:project-reports-20261023`。Dify 的受信任测试患者配置改为 5776，未向用户表单开放患者编号。

验证：后端 357 项测试通过，包括患者与设备隔离、历史机构保留的仓储用例；MetaAgent 297 项通过、15 项按原条件跳过。真实 project 查询、单次分析与报告生成通过；原有 `AMBIGUOUS_TEMPLATE_MODE` 限制保留，未伪造完整模板信息。原始验证记录保存在 MetaAgent 工作区忽略目录 `.local/patient-5776/`。

业务数据库仅执行读取，未补写患者映射、训练或报告数据。报告生成仅写入既有报告文件存储。RAG 源码、配置、语料与发布未改动。
