# IReTour 报表生成

IReTour 的单次训练报告和多次训练统合汇报沿用 IReGo 的 HTML → Playwright PNG → 持久化图片流程。

## 接口

以下接口均使用 `POST /api/ai/patients/tools/` 前缀，沿用 Dify Bearer 凭据和运行时注入的 `system_context`。

| 接口 | 参数 | 生成条件 |
| --- | --- | --- |
| `generate_iretour_single_session_report` | `session_ref` | 当前患者的完整训练，质量可用，至少一个有效汇总指标 |
| `generate_iretour_longitudinal_report` | `selection_mode`、`activity_scope` 及范围参数 | 同一训练项目至少三条记录，至少一个指标有三个有效数值 |

`session_ref` 必须来自 IReTour context/history 工具。单次接口拒绝外部 `ai_evaluation`；两个生成接口均拒绝 `report_type`。

多次报告支持：

- `latest_count`：`report_count` 为 3–20，默认 4。
- `recent_ordinal_range`：`start_ordinal` 和 `end_ordinal` 从最近一次开始计数，范围包含 3–20 条，支持正反顺序。
- `date_range`：同时提供 `start_date` 和 `end_date`，包含边界，最大 180 天，最多 20 条。

窗口选择后按时间正序绘图；缺失指标保留时间位置，仅连接有效点。数据不足返回 HTTP 200、`status=unavailable`；错误引用返回 404；参数错误返回 400；渲染不可用返回 503。

成功响应返回 `artifact_ref`、`storage_mode`、`mime_type`、`image_url`、`display_markdown`、`expires_at` 和 `template_version`。图片通过现有 `GET /api/ai/patients/report-artifacts/{artifact_ref}/content` 读取。

## 与 IReGo 的排版一致性

- 单次报告直接复用 IReGo 完整 CSS，包括四列页眉补充样式，以及同一个时长环图渲染方法。保留总览双栏、记录设置/结果对照、训练详情卡片、说明及页脚的结构。
- 多次报告直接复用 IReGo 完整 CSS、指标卡片和 SVG 渲染方法。页面宽度、字体、颜色、间距、网格、折线和标记均来自同一实现。
- 较长窗口由共享图表方法控制标签密度，保留所有数据点，避免日期重叠。
- IReTour 仅适配自身字段和数值描述；比率转换为百分数后使用同一数轴。没有确认的单位不补造；不套用 IReGo 专属的疗效评价规则。
- `IReTourReportTemplateParityTests` 比较两者完整 CSS、时长环图及指标卡片 HTML，覆盖普通数值、百分比、缺失点和 20 条记录。

## 配置与缓存

图片签名、公网地址、持久化目录、浏览器路径和并发限制共享 `IReGoTools:ReportImage`。IReTour 会话签名使用独立的 `IReTourTools:SessionRefSigningKey`。

IReTour 默认模板版本为 `iretour-single-v3` / `iretour-longitudinal-v3`。共享图表标签密度调整对应的 IReGo 多次报告版本为 `irego-longitudinal-v6`。若部署配置显式覆盖模板版本，应同步升级这些值以避免命中旧图片；完整配置示例见 `AI_WebApi/appsettings.example.json`。

同一患者、训练范围、报表展示内容和模板版本复用稳定图片。内容指纹包含展示名称、参数、指标值、时间及变化方向，更新后重新生成，不覆盖既有图片。

## 既有验证记录

```powershell
dotnet build AI_WebApi.sln --no-restore
dotnet test AI_WebApi.Tests/AI_WebApi.Tests.csproj --no-restore
```

本次还使用合成记录和真实 Playwright 渲染验证了单次、4 次和 20 次报告，以及控制器鉴权、参数拦截、共享图片读取和持久化缓存；未访问生产患者数据。

## 2026-10-14 开发增量

单次和多次分析增加 source_snapshot、interpretation 及来源/质量说明。报表请求支持 expected_source_snapshot：须复制对应分析的快照；源变化时拒绝生成，制品响应也带 source_snapshot。MetaAgent 由程序绑定证据，模型不填写此字段。

多次窗口先选同项目历史，保留没有汇总或无法解析的记录，不用更早记录补数。指标按源字段、单位和可见布尔设置分组；同组至少三个有效值才能生成图片。单次报告的“计划”改称“记录设置”，因为现有设置来自汇总结果，尚未验证为处方计划。

本轮仅补充并运行 TrainingToolsReleaseTests 的三个关键合成检查，编译通过；没有运行新的全套回归。合成单次、多次及 IReCog 报表已做浏览器视觉检查。本轮没有生产写入或发布。

IReCog 新接口同属 /api/ai/patients/tools，包含 get_irecog_patient_history、get_irecog_session_analysis、get_irecog_longitudinal_analysis、generate_irecog_single_session_report、generate_irecog_longitudinal_report。仅读取 DeviceType=5 的关联测评总分，不展示题目答案，不进行未经核验的临床分数趋势。配置使用独立 IReCogTools:SessionRefSigningKey，并复用 IReGoTools:ReportImage。

IREMO（DeviceType=2）只有未核验为训练结果的量表报告，依用户要求阻塞其开发。完整跨仓库交付说明在 MetaAgent docs/training-tools-delivery.md。
