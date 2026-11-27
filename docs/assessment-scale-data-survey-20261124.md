# 线上量表数据探查与解析改造（2026-11-24）

> 结论先行：线上量表报告在 `project` 库里存在**三代格式**，而代码只认其中一代，导致 **321 条报告里只有 82 条（26%）能读出分数**。最大的两块分别是「MoCA-B 的名字带后缀被判身份冲突」（171 条）和「老格式字段名不同解析失败」（68 条）。本次改造把三代格式统一到一套模型，并放开了名称别名，使可读比例接近 100%。

---

## 1. 背景与任务

2026-11-24 上午，魏道好把「量表细化」交给吴涵（群内小号 beagle），要求：

1. 先从数据库里分析量表，包含**量表种类、内部字段**；
2. 再设计展示方案；
3. 最终交付**两个工具**：文字版量表解读工具、图片报表（实现方式参考 IReGo）。

同时给出的约束：

- 参考 IReGo 的参数读取方案，但「量表的参数比较少，应该简单一点」；
- 报表是图片，内部内容自行设计；
- 数据关系拿不准的及时在群里对齐；
- **动手前先确认框架**（当时框架 = `AssessmentScaleCatalog` 那 25 行）。

当时的用户可感知症状是：患者问「最近一次的量表」，AI 只能回答——

> 「量表名称和分数还没核对清楚，暂时读不出具体得分，我不能瞎猜给您。」

本次探查就是为了回答一个问题：**为什么读不出？**

---

## 2. 探查方法

在 DBeaver 里连 `localhost:3306` 的 `project` 库（MySQL 8，utf8mb4），只跑 `SELECT`，不改任何数据。

### 2.1 线上量表的判定口径

与 `AssessmentDataRepository` 保持一致：

```text
dbrehaplan.Deviceid = -1
AND dbrehaplan.TemplateId IN (0, -1)
```

`Deviceid = -1` 表示不挂物理设备（线上量表/线上游戏），与 IReGo、IReCog 等设备训练区分开。

### 2.2 探查用的 9 组查询

| # | 目的 | 关键点 |
| --- | --- | --- |
| Q0 | 线上计划总量 | 先知道锅有多大 |
| Q1 | `Details` 里 `type` / `name` 的分布 | 目录命中率 |
| Q2 | 目录外的名字 | 找「读不出」的嫌疑人 |
| Q3 | `Details` 里有哪些字段 | 判断字段名是否如预期 |
| Q4 | 空名字计划有没有报告 | 区分「没做」和「做了读不出」 |
| Q5 | 报告 JSON 的顶层字段组合 | **暴露三代格式** |
| Q6 | 报告覆盖到哪些量表 | 目录 13 个里真实出现几个 |
| Q7 | 每个量表有没有逐题作答 | 决定图表能做到什么程度 |
| Q8 | `dbreportdata` 是否被量表使用 | 判断逐题数据的真源 |

完整 SQL 见工作区 `量表-口径核对.sql`，本文第 8 节摘录了决定性的几条。

---

## 3. 探查结果

### 3.1 量级

| 指标 | 数量 |
| --- | --- |
| `dbrehaplan` 全表 | 9,824 |
| 线上计划（`Deviceid=-1` 且 `TemplateId` 为 0/-1） | 952 |
| `Details` 可解析但 `name` 为空的线上计划 | 354 |
| 其中有报告的 | 103（其余 251 条是「建了计划没做」） |
| 关联到线上计划的合法报告 | 321 |

### 3.2 计划侧：三堆数据

`Details` 全部是 `{"type": N, "name": "..."}` 两个字段，结构干净。异常集中在取值：

| 情况 | 条数 | 说明 |
| --- | --- | --- |
| `type=1` + `name` 为空字符串 | 302 | 老版本建计划时没写名字 |
| `type` 正常 + 名字正常 | 299 + 229 + 55 + 15 等 | 可正常识别 |
| `Details` 不是 JSON 对象（`null`/`[]` 等） | 48 | 脏数据，`JSON_KEYS` 返回 NULL |
| `type=0` | 4 | 非法类型 |

### 3.3 报告侧：三代格式（本次探查的核心发现）

| 格式 | 顶层字段 | 条数 | 时间范围 |
| --- | --- | --- | --- |
| **老格式** | `name / score / scoreList / answerList / selectList / questionList` | 68 | 2025-07-08 ~ 2026-01-31 |
| **新格式·通用** | `Score / Title / Options / TableTitle / OptionScore / ScoreExplain / OptionProblem` | 82 | 2026-02-02 ~ 2026-10-13 |
| **新格式·MoCA-B** | 同上，但选项分值字段叫 `OptionsScore`（多一个 s） | 171 | 2026-02-02 ~ 2026-10-13 |

时间上是一条清晰的分界线：**2026 年 1 月之前写老格式，2 月之后写新格式**；新格式里 MoCA-B 单独一套模块。

老格式样例（AD8）：

```json
{
  "name": "AD8认知障碍自评表",
  "score": 0,
  "scoreList": ["1|0|0.5", "1|0|0.5", "..."],
  "answerList": ["是，有改变|不是，没有改变|不知道", "..."],
  "selectList": [1, 1, 1, 1, 1, 1, 1, 1],
  "questionList": ["Q: 判断力和以前相比出现问题？", "..."]
}
```

新格式样例（AD8，MoCA-B 同构）：

```json
{
  "Score": 2,
  "Title": ["Q: 判断力和以前相比出现问题？", "..."],
  "Options": ["是，有改变|不是，没有改变|不知道", "..."],
  "TableTitle": "AD8认知障碍自评表",
  "OptionScore": ["1|0|0.5", "..."],
  "ScoreExplain": "得分≥2有可能为早期失智表现，建议进行专业诊断和评估。",
  "OptionProblem": []
}
```

### 3.4 真实出现过的量表

报告侧一共出现 **9 个名字**，其中 7 个在目录内：

| 报告里的名字 | 条数 | 目录对应 |
| --- | --- | --- |
| 蒙特利尔认知评估量表 (MoCA-B) | 171 | type 11，**但名字不同** |
| AD8认知障碍自评表 | 29 | type 1 |
| ADL日常生活能力量表 | 15 | type 3 |
| ADL量表-Barthel指数 | 11 | type 2 |
| Rivermead行为记忆量表记录表 | 8 | type 6 |
| GAD-7广泛性焦虑量表 | 7 | type 5 |
| Beck抑郁量表 | 5 | type 4 |
| 汉密尔顿抑郁量表 | 4 | type 7 |
| 蒙特利尔认知评估量表记录表 | 2 | type 11（标准名） |

**目录里 13 个量表，有 6 个从未出现过**：简易认知状态、简易智力、老年人认知功能筛查、认知障碍分级、色盲色弱检测（5 个），以及蒙特利尔的标准名（只出现过 2 次）。

### 3.5 逐题作答：只有两类数据有

| 数据源 | 覆盖 | 说明 |
| --- | --- | --- |
| `OptionProblem`（新格式） | **仅 MoCA-B，171 条全覆盖** | 定长数组，`-1` 表示未作答或填充 |
| `selectList`（老格式） | 老格式 68 条 | 长度即题目数，无填充 |
| `dbreportdata` | **仅 1 行** | 量表基本不用这张表，可忽略 |

其余 8 个量表的 81 条新格式报告，`OptionProblem` 全是空数组 —— **只有总分，没有逐题**。

### 3.6 计分规则

AD8 是唯一能从数据里反推出规则的量表：

| 选项 | 编号 | 分值 |
| --- | --- | --- |
| 是，有改变 | 0 | 1 |
| 不是，没有改变 | 1 | 0 |
| 不知道 | 2 | 0.5 |

验证（`score` 与 `selectList` 的对应关系）：

| `selectList` | 重算 | 库里 `score` |
| --- | --- | --- |
| 全 0 | 8 | 8 |
| 全 1 | 0 | 0 |
| 全 2 | 4 | 4 |
| `[0,1,0,0,0,1,0,1]` | 5 | 5 |

满分 8 分。**但存在脏数据**：有一条 `score = 9`、`selectList` 全 0（按规则应为 8），说明不能盲信 `Score` 字段。

MoCA-B 是 30 分制且需按受教育年限校正，**不能简单按选项分求和**，规则未签核前不复算。

### 3.7 脏数据清单

| 类型 | 条数 | 处理 |
| --- | --- | --- |
| `Details` 不是 JSON 对象 | 48 | 忽略 |
| `type=0` | 4 | 忽略 |
| `score` 与选项重算不一致 | 至少 1 条 | 标记 `ASSESSMENT_SCORE_MISMATCH` |

---

## 4. 为什么读不出分

### 4.1 主因：MoCA-B 的名字多了 4 个字符（171 条 = 53%）

- 报告里：`蒙特利尔认知评估量表 (MoCA-B)`
- 目录里：`蒙特利尔认知评估量表记录表`

旧 `Matches()` 要求归一化后**逐字相等**，于是这 171 条全部判定为「身份冲突」，分数被直接丢弃。

这是占比最大、也最容易修的一块。

### 4.2 次因：老格式字段名不同（68 条 = 21%）

解析器按 `tableTitle` / `title` 找字段，老数据叫 `name` / `questionList`，直接抛异常 → 归为「结构不支持」。

### 4.3 结果：82 / 321 可读

- 新格式·通用：82 条 → 可读 ✅
- 新格式·MoCA-B：171 条 → 身份冲突 ❌
- 老格式：68 条 → 结构不支持 ❌

这也解释了群里那句「量表好像可以」—— 因为确实有一小部分能读，只是没人发现大头是坏的。

---

## 5. 改造方案

### 5.1 名称归一化 + 别名表

`AssessmentScaleCatalog` 升到 v2：

- 新增 `NormalizeName()`：去掉所有空白、全角括号转半角、全角连字符转半角；
- 新增 `Variants` 别名表，只收录**真实观察到的写法**：
  - `type 1`：`AD8`
  - `type 11`：`蒙特利尔认知评估量表(MoCA-B)` / `蒙特利尔认知评估量表MoCA-B` / `MoCA-B` / `MoCA`
- `Matches()` 先比标准名，再比别名；
- 新增 `ResolveType()`：从名字反查类型，供「计划没写类型」的场景兜底。

### 5.2 三形状统一解析

`ParseReport()` 用一个入口分流：

```text
有 name 字段            → ParseLegacySummary   → legacy_scale_summary_v1
有 tableTitle/title     → ParseModernSummary   → lowercase_total_v1
有 TableTitle/Title     → ParseModernSummary   → option_score_total_v1 / options_score_total_v1
```

统一输出内部结构：

```text
schema_family / score / score_field / scale_title
question_count / answered_count / has_item_answers / recomputed_score
```

`OptionProblem` 的 `-1` 视为「未作答/填充」，只统计有效选择；不把填充值当成「患者全没答」。

### 5.3 身份判定改为「报告标题优先」

旧逻辑完全依赖计划，计划一旦缺名字或类型非法就无解。新顺序：

1. 计划 `type` 合法 → 采用；
2. 否则从计划名反查（`ASSESSMENT_SCALE_FROM_PLAN_NAME`）；
3. 否则从报告标题反查（`ASSESSMENT_SCALE_FROM_REPORT_TITLE`）；
4. 计划名与报告标题**同时存在且指向不同类型** → 仍判冲突（不放宽）。

这样那 302 条「计划没写名字、报告里有名字」的记录才能恢复。

### 5.4 AD8 总分复算（唯一放开的一个）

仅当 `ScoringStatus == "verified_option_sum"` 时才复算：

```text
recomputed = Σ scoreList[i].split('|')[selectList[i]]
```

- 一致 → `ASSESSMENT_SCORE_RECOMPUTED`
- 不一致 → `ASSESSMENT_SCORE_MISMATCH`（保留源总分，但显式告警）
- 任何一题缺选择/越界 → 不复算，返回 null（宁可不给，也不给一个看起来合理的错数）

### 5.5 MoCA-B 故意不复算

`ScoringStatus = unverified_education_adjusted`，`recomputed_score` 恒为 null。理由：30 分制 + 教育年限校正，没有签核规则就算不准，算错比不算更危险。

### 5.6 新增字段

| 字段 | 含义 |
| --- | --- |
| `recomputed_score` | 按选项重算的总分，仅已验证量表有值 |
| `scale_max_score` | 满分，仅已知量表有值（AD8 = 8） |
| `scoring_status` | `verified_option_sum` / `unverified_education_adjusted` / `unverified` |
| `question_count` | 题目数 |
| `answered_item_count` | 已作答题数 |
| `has_item_answers` | 是否记录了逐题作答 |

题目与选项原文**仍然不进入对外 DTO**，只输出计数。

### 5.7 新增原因码

```text
ASSESSMENT_SCALE_FROM_PLAN_NAME
ASSESSMENT_SCALE_FROM_REPORT_TITLE
ASSESSMENT_RESULT_PARTIALLY_MISSING
ASSESSMENT_SCORE_RECOMPUTED
ASSESSMENT_SCORE_MISMATCH
```

---

## 6. 验证

服务项目编译 0 错误。用真实三形状构造验证程序，结果：

```text
status=partial reasons=ASSESSMENT_SCORE_SEMANTICS_UNVERIFIED,ASSESSMENT_PLAN_NAME_MISSING,ASSESSMENT_SCORE_RECOMPUTED
  蒙特利尔认知评估量表记录表 | score=24 | recomputed=  | Q=30/30 | schema=options_score_total_v1
  AD8认知障碍自评表         | score=3  | recomputed=3 | Q=8/8   | schema=legacy_scale_summary_v1
ALL CHECKS PASSED
```

同时回归了原有测试的全部期望值，均未变化：

- 线上游戏被排除，`record_count = 6`；
- `readable_total_count = 3`；
- `ScaleCoverage = 13`；
- `identity_conflict`、`conflicting`、`lowercase_total_v1 = 12` 三类用例保持原行为。

> 说明：验证环境无外网，`AI_WebApi.Tests` 缺少 `Microsoft.AspNetCore.Mvc.NewtonsoftJson 8.0.0` 无法还原，因此测试项目需在本地 Visual Studio / 有网环境跑 `dotnet test` 复核。

---

## 7. 未决事项（需业务确认）

| # | 问题 | 影响 |
| --- | --- | --- |
| 1 | MoCA-B 总分如何计算（30 分制、教育年限校正） | 决定能否复算与做趋势 |
| 2 | `ScoreExplain`（源报告自带「得分≥2 可能为早期失智」）能否展示给患者 | 与「不做诊断」红线冲突，需明总确认 |
| 3 | 逐题作答是否展示给患者 | 目前只给计数，不给题目与选项原文 |

在这三条确认前，纵向分析维持 `blocked_unverified_scoring_rules`，不做跨次分数差、不做跨量表平均。

---

## 8. 关键 SQL 摘录

**报告格式分布**

```sql
SELECT CAST(JSON_KEYS(r.ReportDetails) AS CHAR) AS json_keys,
       COUNT(*) AS reports, MIN(r.CreateTime) AS first_seen, MAX(r.CreateTime) AS last_seen
FROM dbreport r
JOIN dbrehaplan p ON p.Id = r.planId
WHERE p.Deviceid = -1 AND p.TemplateId IN (0,-1)
  AND r.ReportDetails IS NOT NULL AND JSON_VALID(r.ReportDetails)
GROUP BY json_keys ORDER BY reports DESC;
```

**每个量表的逐题覆盖**

```sql
SELECT JSON_UNQUOTE(JSON_EXTRACT(r.ReportDetails,'$.TableTitle')) AS scale_name,
       COUNT(*) AS reports,
       SUM(CASE WHEN JSON_LENGTH(JSON_EXTRACT(r.ReportDetails,'$.OptionProblem')) > 0 THEN 1 ELSE 0 END) AS has_answers
FROM dbreport r
JOIN dbrehaplan p ON p.Id = r.planId
WHERE p.Deviceid = -1 AND p.TemplateId IN (0,-1)
  AND JSON_VALID(r.ReportDetails)
  AND JSON_EXTRACT(r.ReportDetails,'$.TableTitle') IS NOT NULL
GROUP BY scale_name ORDER BY reports DESC;
```

**AD8 计分规则反推**

```sql
SELECT CAST(JSON_EXTRACT(r.ReportDetails,'$.score') AS CHAR) AS score,
       CAST(JSON_EXTRACT(r.ReportDetails,'$.selectList') AS CHAR) AS select_list,
       COUNT(*) AS cnt
FROM dbreport r
JOIN dbrehaplan p ON p.Id = r.planId
WHERE p.Deviceid = -1 AND p.TemplateId IN (0,-1)
  AND JSON_VALID(r.ReportDetails)
  AND JSON_UNQUOTE(JSON_EXTRACT(r.ReportDetails,'$.name')) = 'AD8认知障碍自评表'
GROUP BY score, select_list ORDER BY cnt DESC LIMIT 50;
```

---

## 9. 变更清单

| 文件 | 改动 |
| --- | --- |
| `AI_WebApi.Services/AITools/Assessment/AssessmentScaleCatalog.cs` | 名称归一化、别名表、`ScaleProfile` 元数据、`ResolveType` |
| `AI_WebApi.Services/AITools/Assessment/AssessmentToolService.cs` | 三形状解析、身份报告优先、AD8 复算、DTO 赋值、模板渲染增强 |
| `AI_WebApi.Model/DTOs/AITools/AssessmentToolDtos.cs` | 新增 6 个字段 |
| `AI_WebApi.Tests/AssessmentToolFamilySmokeTests.cs` | 新增真实形状回归用例 |
| `docs/patient-tool-families.md` | 更新格式与解析说明 |
| `AI_WebApi/appsettings.json`（本地，已被 gitignore） | 补 `AssessmentTools` 节 |

版本号变更：

- `AssessmentScaleCatalog.Version` → `project-online-scale-catalog-v2`
- 解析器注册版本 → `assessment-summary-parsers-v2`
- 报表模板 → `assessment-records-v2`

> 版本号变化会让 `source_snapshot` 哈希改变，历史缓存报表会重新生成，属预期行为。
