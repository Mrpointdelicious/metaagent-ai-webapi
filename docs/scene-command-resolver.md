# 场景 Command Resolver

## 审计与边界

审计基线：`dev` / `f1728f3`，原有 147 项测试全部通过。

- `SceneNavigationService` 对整句打分后取 `FirstOrDefault()`，确实只能返回一个动作。
- `StripPrefix` 会删除打开、关闭、进入、取消、确认等动作；只有打开指令时，输入关闭同一目标也可能得到打开指令。
- 同义表同时把退出解释成关闭、结束，存在动作方向混淆。
- 任意正分即返回结果，没有最低分或歧义判定；同分时按类别和 ID 排序会掩盖真实歧义。
- 四类查询异常被逐个吞掉，剩余目录不能证明唯一性。缺少 ID 的目录项也可能生成可执行字符串。
- ID 原本来自各配置数组的零起始下标。改为明确的 `WITH ORDINALITY` 下标，保持前端协议，不按筛选后的列表重新编号。

只修改 `POST /api/ai/rehab/tools/navigate_scene` 的场景服务、契约、接口说明与测试。医生、患者和 IReGo 行为不变。

## 设计

1. `SceneCommandCatalog`：每次请求解析 spaceId（空值继续使用默认空间）和实时 version，再读取四类完整目录。保留真实 Prefix、Id、Name、Intro、IntentType；只在内部生成 signature。任一查询失败或响应结构异常，整个请求由原控制器返回 502；合法空数组或聚合结果 null 表示空目录。
2. `SceneCommandNormalizer`：只归一化文字和明确的动作同义词，保留动作方向。open/close、enter/exit、confirm/cancel、return/stop 分别独立。前置、后置动作均可构造 Action + Target + CanonicalName。
3. `SceneCommandResolver`：保护目录名称内部的动作词和连接词；以原文动作位置、目录目标边界和少量连接词共同寻找 command span。优先寻找完整覆盖片段的目录指令序列，因此“挥手”等没有预设动词的真实指令也能参与识别；同样可信的不同目录切分返回边界歧义。其余表达对相邻动作之间的边界进行整体选择，优先有完整目标的解释，再利用目录匹配证据消歧。不会简单切句后重复旧打分，也不会对整句取 Top N。未知片段参与结果，不静默丢弃；重复动作保留。
4. 每个 span 先应用动作硬约束，再依次比较：完整名称（1.00）、同义归一化完整名称（0.98）、Action + Target 精确（0.96）、目标包含/有序省略（0.82–0.92）、目标拼音 fallback（最高 0.78）。只有当前最强层级的候选参与分差判定；ID 排序不决定唯一性。
5. 无动作的传送、场景、游戏点位可由明确的导航/进入/返回表达定位；这是类别的导航能力。具有明确动作的目录项始终要求同一动作，不能把关闭映射到无动作的 UI 名称。仅给出动作型指令的目标时，提示补充动作。

`RehabTools:Scene:Resolver:MinimumScore` 默认 0.75，`MinimumMargin` 默认 0.05，均可配置（分数 0–1，分差大于 0 且不超过 1）。这些数值是规则置信度而非统计概率，通过精确、近音、弱包含及近分候选测试校准；真实业务语料增加后可继续调整。

对应环境变量已加入 `.env.ai_webapi.example`。部分目标要求至少两个字符、至少覆盖目录目标一半；跨字省略还要求至少四字并保留首尾各两字，避免“历话”误命中“历史对话”。拼音仅转换目标，保留数字和未转换字符；不能把“报告2”当成“报告1”。最低分比较包含边界；同层候选分差小于 MinimumMargin 时为歧义（即使第二名略低于最低分，也仍参与分差判断）。

## 响应与调用方迁移

路由、请求 `target`/`spaceId`、认证、400/401/502 行为保持原样。成功仍用 `ApiResponse` 包装，`data` 改为：

```json
{
  "spaceId": "space-7",
  "version": 321,
  "commands": [
    {
      "order": 1,
      "sourceText": "打开历史对话",
      "status": "matched",
      "result": "[ClickPoint:]13",
      "intentType": "click_point",
      "matchName": "打开历史对话",
      "score": 1.0,
      "message": null,
      "candidates": []
    }
  ]
}
```

示例 ID 仅为演示；运行时只能来自本次目录。前端应依次读取 `data.commands`，仅执行 `status=matched` 的非空 `result`。旧的 `data.result` 等单条字段删除，需要同步更新前端/Dify 的字段映射。

`ambiguous` 和 `unmatched` 的 result、intentType、matchName、score 均为 null。歧义候选仅含 name、intentType、score，不公开 ID 或可执行 result。单条失败不影响同一响应中的其他条目。纯标点或礼貌用语可返回空 commands；有内容但未识别的片段返回 unmatched。sourceText 保留原文对应片段（含原始空格/礼貌词），order 从 1 开始。

解析为确定性规则，不调用 LLM，不推断目录外编号。包含条件、否定或询问的片段保留 unmatched；不支持跨分句条件推理、代词指代和省略目标。各分句独立解析，复杂表达应改写成明确动作。版本是解析时快照，前端在空间/版本变化后应重新解析。

## 验证计划

- 三动作顺序、无连接词/前后置混用、重复动作、未知和歧义夹在成功项中。
- 打开/关闭、进入/退出、确认/取消硬约束，包括仅存在相反动作的目录。
- 完整名称优先、同义动作、目录名称内部动词/连接词、相近目标、同名不同 ID。
- 拼音仅作用于目标、弱匹配阈值、同层分差、可配置阈值边界。
- 版本/空间重新查询、四类 ID 保真、空目录、失败/畸形响应、缺失 ID、取消传递。
- JSON 契约、场景回归、全套测试及 Web API 构建。

## 完成后的验证

- 全套 xUnit：236 项通过、0 失败、0 跳过；其中新增场景专项测试 89 项，原 147 项回归通过。
- Web API 构建通过、0 错误。保留项目已有的 .NET 6 目标框架、NPinyin 兼容性及既有空引用告警，没有借此修改其他模块或升级依赖。
- 实际启动 Web API 并通过 HTTP 检查：三动作、无连接词、歧义部分成功、未匹配部分成功，以及 400/401/502 和 OpenAPI 的 commands[] schema。
- HTTP 检查使用本地模拟目录、独立内容目录及测试凭据，不连接真实场景上游。本机没有 ASP.NET Core 6 运行时，HTTP 检查通过 `LatestMajor` 使用本机 .NET 10 运行时；xUnit 使用 .NET 8。生产 .NET 6 上游联调不在上述验证范围内。

复现：

```powershell
dotnet test AI_WebApi.Tests/AI_WebApi.Tests.csproj --no-restore --verbosity quiet
dotnet build AI_WebApi/AI_WebApi.csproj --no-restore --verbosity quiet
python tools/verify_scene_command_resolver.py
```

HTTP 检查输出在忽略提交的 `tmp/scene-http-smoke/`，脚本结束时自动关闭它启动的 Web API 和模拟目录进程。
