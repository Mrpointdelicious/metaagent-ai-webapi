# AI_WebApi 框架审计报告

审计日期：2026-06-10  
审计对象：`D:\Project\AI_WebApi` 下的 ASP.NET Core Web API 解决方案  
审计方式：静态代码审计、依赖漏洞扫描、构建验证、测试资产检查  
结论级别：当前状态不建议直接部署到生产环境

## 1. 执行摘要

该项目是一个基于 ASP.NET Core、Autofac、SqlSugar 的分层 Web API 框架，包含 API、Common、Extensions、IServices、Model、Repository、Services 七个项目。整体分层清晰，基础仓储和服务抽象已经具备雏形，但当前更接近示例/脚手架状态，安全边界、配置管理、异常处理、测试和生产化能力不足。

最高风险集中在三类问题：

1. `AI_WebApi/appsettings.json` 中提交了真实数据库连接串，且使用 `root` 账号和明文密码。
2. `AI_WebApi/Controllers/TestController.cs` 暴露了可由请求参数传入 SQL 的接口，且项目未配置认证授权。
3. 项目目标框架为 `net6.0`，构建时已提示该目标框架不受支持；依赖扫描发现传递依赖 `System.Text.Json 6.0.0` 存在 High 级漏洞。

构建结果为成功，但存在 49 个编译警告。项目中未发现测试项目。

## 2. 审计范围

已覆盖：

- 解决方案与项目结构：`AI_WebApi.sln`、各 `*.csproj`
- API 入口与中间件：`AI_WebApi/Program.cs`
- 控制器：`SampleController`、`TestController`、`WeatherForecastController`
- 数据库配置：`appsettings.json`、`BaseDBConfig`、`SqlsugarSetup`
- 依赖注入：`AutofacModuleRegister`
- 仓储与服务基类：`BaseRepository`、`BaseServices`
- 实体与响应模型：`SampleEntity`、`DBArticle`、`ApiResponse`、`PageModel`
- 构建与依赖漏洞验证

未覆盖：

- 未连接真实数据库做动态接口测试
- 未做外部渗透测试、压力测试、SAST/DAST 商业工具扫描
- 未审计部署脚本、容器镜像、云资源或数据库权限配置

## 3. 项目结构概览

| 项目 | 作用 | 观察 |
| --- | --- | --- |
| `AI_WebApi` | Web API 入口、控制器、配置文件 | 包含 Swagger、CORS、控制器、数据库连接串 |
| `AI_WebApi.Common` | 公共配置与数据库配置模型 | 使用静态 `AppSettings.Configuration` |
| `AI_WebApi.Extensions` | Autofac 注册、SqlSugar 注册 | 通过 DLL 扫描注册服务与仓储 |
| `AI_WebApi.IServices` | 服务接口 | 继承泛型服务接口 |
| `AI_WebApi.Services` | 服务实现 | 基本透传仓储能力 |
| `AI_WebApi.Repository` | 泛型仓储、UnitOfWork | 暴露原始 SQL/where/order 字符串入口 |
| `AI_WebApi.Model` | 实体与响应模型 | API 返回包装模型与 SqlSugar 实体 |

## 4. 验证结果

### 4.1 构建验证

执行命令：

```powershell
dotnet build .\AI_WebApi.sln
```

结果：

- 构建成功
- 0 个错误
- 49 个警告
- 关键警告：`net6.0` 不受支持，将来不会收到安全更新
- 多处 nullable 警告，主要集中在接口、仓储、服务基类和配置类

### 4.2 依赖漏洞扫描

执行命令：

```powershell
dotnet list .\AI_WebApi.sln package --vulnerable --include-transitive
```

结果：

- 多个项目存在传递依赖 `System.Text.Json 6.0.0`
- 严重性：High
- 顾问：`https://github.com/advisories/GHSA-8g4q-xg66-9fp4`
- 受影响项目包括 `AI_WebApi`、`AI_WebApi.Common`、`AI_WebApi.Repository`、`AI_WebApi.Services`、`AI_WebApi.Extensions`

### 4.3 测试资产检查

未发现 `*Test*.csproj` 或 `*Tests*.csproj` 测试项目。当前没有可执行的单元测试、集成测试或接口契约测试。

## 5. 风险总览

| 编号 | 严重性 | 类型 | 位置 | 问题 |
| --- | --- | --- | --- | --- |
| C-01 | Critical | 密钥管理 | `AI_WebApi/appsettings.json` | 明文数据库连接串、root 账号、真实密码入库 |
| C-02 | Critical | SQL 注入/数据泄露 | `TestController.QueryTest` | 请求参数可直接作为 SQL 执行 |
| C-03 | Critical | 访问控制 | `Program.cs`、控制器 | 未配置认证，接口默认公开 |
| H-01 | High | 依赖安全 | 各 `*.csproj` | `net6.0` EOL，`System.Text.Json 6.0.0` 高危漏洞 |
| H-02 | High | CORS | `Program.cs` | `AllowAnyOrigin/Method/Header` 完全开放 |
| H-03 | High | SQL 注入面 | `BaseRepository`、`IBaseRepository`、`BaseServices` | 框架层暴露原始 SQL、where、order 字符串接口 |
| H-04 | High | 信息泄露 | `TestController` | 对外返回异常消息、数据库名称、版本、表名 |
| M-01 | Medium | 传输安全 | `Program.cs` | 未启用 HTTPS 重定向和 HSTS |
| M-02 | Medium | API 语义 | `ApiResponse`、控制器 | 业务状态码与 HTTP 状态码分离，错误也可能返回 HTTP 200 |
| M-03 | Medium | 滥用/DoS | `SampleController.GetPage` | `pageSize` 无上限，缺少输入验证 |
| M-04 | Medium | 事务一致性 | `UnitOfWorkManage` | 事务计数和异常处理较脆弱 |
| M-05 | Medium | 日志安全 | `SqlsugarSetup` | SQL 直接输出到 Console，缺少脱敏与日志等级控制 |
| M-06 | Medium | 可维护性 | 全项目 | 无测试项目，缺少回归保护 |
| L-01 | Low | 示例代码残留 | `WeatherForecastController` | 默认模板接口仍暴露 |
| L-02 | Low | 空值安全 | 多处 | nullable 警告未清理 |
| L-03 | Low | 配置健壮性 | `BaseDBConfig`、`SqlsugarSetup` | 数据库配置缺少启动时校验 |
| L-04 | Low | 命名/编码 | 多处 | 存在 `Muti`、`Fileds` 等拼写问题，中文注释/字符串在终端呈现异常 |

## 6. 详细发现

### C-01 明文数据库凭据入库

位置：`AI_WebApi/appsettings.json`

当前配置包含数据库地址、库名、`root` 用户和明文密码。只要代码仓库、制品包、日志、备份或开发机器被访问，数据库凭据就会泄露。使用 root 账号会进一步放大破坏面。

影响：

- 数据库被直接登录
- 数据被读取、篡改或删除
- 生产、测试、开发环境之间可能横向移动
- 即使后续删除文件，凭据也可能已存在于历史记录或制品中

建议：

- 立即轮换该数据库密码
- 禁用应用使用 root 账号，改为最小权限账号
- 从 `appsettings.json` 移除真实连接串，仅保留 `appsettings.example.json`
- 使用环境变量、User Secrets、CI/CD Secret 或密钥管理服务注入连接串
- 在配置加载阶段校验连接串存在，但不要打印完整连接串

### C-02 测试接口支持任意 SQL 查询

位置：`AI_WebApi/Controllers/TestController.cs`

`QueryTest` 从 `sql` 查询参数读取 SQL，并直接调用：

```csharp
_db.Ado.SqlQueryAsync<object>(sql)
```

该接口当前没有认证、授权、IP 限制、环境限制、SQL 白名单或只读保障。即使数据库驱动最终只允许查询，也足以造成数据泄露、结构探测、慢查询拖垮数据库等问题。

影响：

- 任意读取数据库数据
- 枚举库名、版本、表名、字段结构
- 执行昂贵 SQL 造成数据库资源耗尽
- 如果连接或驱动允许多语句，可能扩大到写入或破坏数据

建议：

- 生产代码删除 `TestController`
- 若必须保留，仅在 Development 环境注册，并加 `[Authorize]`、管理员角色、IP 白名单
- 禁止请求参数直接传入 SQL
- 改为固定的健康检查 SQL，例如 `SELECT 1`
- 数据库账号限制为只读且仅限必要表

### C-03 接口缺少认证授权边界

位置：`AI_WebApi/Program.cs`、所有控制器

当前代码调用了 `app.UseAuthorization()`，但没有发现：

- `builder.Services.AddAuthentication(...)`
- `app.UseAuthentication()`
- 全局授权策略
- 控制器或 Action 上的 `[Authorize]`

这意味着 `SampleController` 的增删改查、`TestController` 的数据库信息与 SQL 查询、默认 `WeatherForecastController` 都是公开接口。

影响：

- 未授权用户可创建、修改、删除数据
- 测试接口可泄露数据库信息
- 无法区分用户身份，审计追踪缺失

建议：

- 引入 JWT Bearer、OAuth2/OIDC 或内部网关认证
- 添加 `builder.Services.AddAuthorization(...)`
- 在中间件顺序中加入 `app.UseAuthentication()`，且位于 `UseAuthorization()` 之前
- 默认要求授权，仅对明确公开的健康检查或登录接口使用 `[AllowAnonymous]`
- 对写接口增加角色或权限策略

### H-01 运行时和依赖存在安全债

位置：各 `*.csproj`

项目目标框架为 `net6.0`。构建输出明确提示 `net6.0` 不受支持。依赖漏洞扫描发现 `System.Text.Json 6.0.0` High 级漏洞。

影响：

- 运行时安全补丁不可持续
- 传递依赖漏洞可能通过 JSON 解析路径触发
- 后续包升级和云平台支持会越来越困难

建议：

- 优先升级到受支持的 LTS 版本，例如 `net8.0` 或部署环境已支持时升级到 `net10.0`
- 升级 ASP.NET Core、Swashbuckle、Autofac、SqlSugarCore 等依赖
- 在 CI 中加入 `dotnet list package --vulnerable --include-transitive`
- 对高危漏洞设置阻断策略

### H-02 CORS 完全开放

位置：`AI_WebApi/Program.cs`

当前 CORS 策略为：

```csharp
policy.AllowAnyOrigin()
      .AllowAnyMethod()
      .AllowAnyHeader();
```

在没有认证授权的前提下，这会让任意站点从浏览器发起跨域请求。即使未来加入 Cookie 或 Bearer Token，也容易形成跨站调用风险。

建议：

- 为不同环境配置明确允许的 Origin
- 生产环境禁止 `AllowAnyOrigin`
- 只开放必要 Method 和 Header
- 若使用凭证，严格使用 `WithOrigins(...)`，不要和任意来源组合

### H-03 泛型仓储暴露高风险字符串查询接口

位置：

- `AI_WebApi.Repository/BASE/BaseRepository.cs`
- `AI_WebApi.Repository/BASE/IBaseRepository.cs`
- `AI_WebApi.Services/BASE/BaseServices.cs`
- `AI_WebApi.IServices/BASE/IBaseServices.cs`

框架层提供了多种字符串入口：

- `Query(string where)`
- `Update(TEntity entity, string where)`
- `QuerySql(string sql, SugarParameter[] parameters = null)`
- `QueryTable(string sql, SugarParameter[] parameters = null)`
- `orderByFields` 字符串排序

这些接口不一定立刻构成漏洞，但会让业务层很容易把用户输入拼进 SQL 片段。框架越底层，越应默认安全。

建议：

- 将原始 SQL 能力改为 `protected` 或内部基础设施能力，不向普通业务服务暴露
- 业务查询优先使用表达式树、规范化 Query Object 或 Repository 专用方法
- `orderByFields` 使用枚举或白名单映射
- 如确需 SQL，强制参数化，并封装审批过的 SQL 模板

### H-04 数据库信息和异常细节直接返回客户端

位置：`AI_WebApi/Controllers/TestController.cs`

`GetDbInfo` 返回数据库名、版本、表数量、表名列表。多个 catch 分支直接返回 `ex.Message`。

影响：

- 帮助攻击者快速识别数据库类型、版本和结构
- 暴露连接失败、权限、SQL 语法等内部细节
- 与任意 SQL 接口组合时风险更高

建议：

- 对外返回统一错误码和简短错误消息
- 详细异常只写入服务端日志
- 数据库信息接口仅限内部管理员或彻底移除
- 引入全局异常处理中间件或 `ProblemDetails`

### M-01 未启用 HTTPS 重定向和 HSTS

位置：`AI_WebApi/Program.cs`

未发现 `app.UseHttpsRedirection()` 和 `app.UseHsts()`。虽然 `launchSettings.json` 配置了 HTTPS 本地地址，但应用本身没有强制传输安全。

建议：

- 非开发环境启用 HSTS
- 启用 HTTPS 重定向
- 在反向代理部署时配置 `ForwardedHeaders`

### M-02 HTTP 状态码语义不准确

位置：`ApiResponse`、各控制器

控制器直接返回 `ApiResponse<T>`，例如未找到时返回 `ApiResponse.Error("...", 404)`。这种写法通常仍会产生 HTTP 200，只是在响应体中写入 `Status = 404`。

影响：

- 客户端、网关、监控、重试策略无法正确识别失败
- 缓存和告警容易失效
- REST 语义不一致

建议：

- 控制器返回 `ActionResult<ApiResponse<T>>`
- 未找到返回 `NotFound(...)`
- 参数错误返回 `BadRequest(...)`
- 未授权返回标准 401/403
- 服务端异常由统一异常处理中间件转换为 500

### M-03 分页和输入缺少约束

位置：`AI_WebApi/Controllers/SampleController.cs`

`GetPage` 接收 `pageIndex`、`pageSize`，但没有最小值、最大值、排序字段白名单或 DTO 校验。攻击者可以传入很大的 `pageSize` 触发大查询。

建议：

- 限制 `pageIndex >= 1`
- 限制 `pageSize` 最大值，例如 100 或 200
- 对搜索关键字长度做限制
- 引入 DTO 和模型校验

### M-04 UnitOfWork 实现较脆弱

位置：`AI_WebApi.Repository/UnitOfWorks/UnitOfWorkManage.cs`

风险点：

- `GetDbClient()` 使用 `as SqlSugarScope`，理论上可能返回 null
- `CommitTran()` 中捕获异常后只打印 `Console.WriteLine`，没有向调用方明确失败
- `_tranCount--` 可能在不平衡调用下变成负数
- `lock (this)` 不推荐作为同步锁对象

建议：

- 使用私有 readonly lock 对象
- 显式校验 `SqlSugarScope` 类型，不满足时快速失败
- 事务失败应记录结构化日志并向上抛出或返回明确结果
- 对嵌套事务计数做边界保护
- 补充事务成功、失败、嵌套、异常路径测试

### M-05 SQL 日志直接输出到 Console

位置：`AI_WebApi.Extensions/ServiceExtensions/SqlsugarSetup.cs`

当前 `OnLogExecuting` 使用：

```csharp
Console.WriteLine($"SQL: {sql}");
```

影响：

- 日志不受等级控制
- 生产环境可能泄露 SQL、表结构或敏感查询
- 不利于集中日志采集和追踪

建议：

- 使用 `ILogger`
- 按环境控制 SQL 日志
- 对参数和敏感字段脱敏
- 慢查询单独记录

### M-06 缺少自动化测试

当前未发现测试项目。框架包含通用仓储、服务基类、数据库配置和事务管理，这些都是高复用代码，应至少有基础测试覆盖。

建议补充：

- `BaseDBConfig` 配置解析测试
- `ApiResponse` 状态和序列化测试
- `SampleServices.GetByNameAsync` 行为测试
- `UnitOfWorkManage` 事务边界测试
- 控制器集成测试
- 鉴权和越权测试

### L-01 默认模板接口残留

位置：`AI_WebApi/Controllers/WeatherForecastController.cs`

该控制器是 ASP.NET Core 默认模板接口。生产框架中应删除，避免暴露无意义接口和混淆 API 文档。

### L-02 nullable 警告未清理

构建输出中存在多处 CS8618、CS8625、CS8603、CS8602、CS8604。虽然当前可构建，但这些警告提示潜在 NullReferenceException。

建议：

- 将 nullable 警告作为 CI 门禁逐步收紧
- 对可空参数显式标注 `?`
- 对依赖注入必需项移除默认 null
- 对返回值可能为空的方法改为可空返回或显式异常

### L-03 数据库配置缺少启动时校验

位置：`BaseDBConfig`、`SqlsugarSetup`

如果 `DBS` 为空、`MainDB` 不匹配或连接串为空，当前逻辑可能在运行期才失败。

建议：

- 启动阶段校验 `MainDB`、`DBS`、`Connection`、`DbType`
- 对缺失配置抛出明确异常
- 不在异常中输出完整连接串

### L-04 命名和编码问题

观察到 `MutiDBEnabled`、`MutiConnectionString`、`OrderByFileds` 等拼写问题。部分中文注释和字符串在终端输出中呈现异常，可能是历史编码或控制台编码不一致导致。

建议：

- 新增配置键时使用正确拼写，旧键可保留兼容映射
- 统一源码编码为 UTF-8
- 用户可见文案集中管理，避免乱码和难以维护

## 7. 建议修复路线

### 立即处理，0-1 天

1. 轮换 `appsettings.json` 中暴露的数据库密码。
2. 创建最小权限数据库账号，禁止应用使用 root。
3. 从仓库和制品中移除真实连接串，改为环境变量或密钥服务注入。
4. 删除或禁用 `TestController`，至少移除 `QueryTest` 任意 SQL 接口。
5. 为所有非公开接口添加认证授权。
6. 将生产 CORS 改为明确 Origin 白名单。

### 短期处理，1-2 周

1. 升级目标框架到受支持 LTS，例如 `net8.0` 或 `net10.0`。
2. 升级存在漏洞的传递依赖和相关 NuGet 包。
3. 引入全局异常处理，禁止向客户端返回 `ex.Message`。
4. 控制器改为返回标准 HTTP 状态码。
5. 增加 DTO 校验、分页上限、排序白名单。
6. 移除或限制泛型仓储中的原始 SQL/where 字符串接口。
7. 启用 HTTPS 重定向、HSTS、结构化日志。

### 中期处理，2-4 周

1. 建立单元测试和集成测试项目。
2. 在 CI 中加入构建、测试、依赖漏洞扫描、格式检查。
3. 增加健康检查、限流、审计日志、请求追踪。
4. 重构 UnitOfWork 事务边界和错误处理。
5. 编写部署说明、配置说明和环境变量清单。

## 8. 推荐上线门禁

生产上线前建议满足以下条件：

- 无明文密钥入库
- 无任意 SQL 调试接口
- 所有业务接口默认需要认证授权
- CORS 使用生产域名白名单
- 目标框架处于官方支持周期内
- `dotnet list package --vulnerable --include-transitive` 无 High/Critical 漏洞
- `dotnet build` 无错误，关键 nullable 警告已处理或登记
- 有基础单元测试和关键接口集成测试
- API 错误返回标准 HTTP 状态码
- 开启 HTTPS/HSTS、结构化日志、健康检查和限流

## 9. 总体评价

该框架的分层方向是可继续演进的：接口、服务、仓储、模型和扩展注册已经拆分，适合后续扩展业务模块。但当前安全默认值偏弱，且包含高风险测试接口和明文数据库凭据。建议先完成安全基线修复，再继续扩展业务功能。

当前优先级不是做大规模重构，而是先关闭可直接被利用的入口：密钥、任意 SQL、无认证接口、开放 CORS、EOL 运行时和漏洞依赖。完成这些后，再处理仓储 API 安全边界、事务可靠性、测试体系和运维能力。
