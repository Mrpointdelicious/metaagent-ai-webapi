# AI_WebApi project 迁移版：服务器部署

## 包内内容

这是当前工作区的源码部署包，包含最新未提交的代码、测试、标准 Dockerfile、compose.yaml 和配置模板。服务器通过 Docker 编译；不依赖开发机 publish/ 或 Dockerfile.local。

包内不含真实 .env、appsettings.json、密码、旧密钥、数据库备份、患者报告、审计结果、Git 历史或开发机编译产物。PACKAGE-MANIFEST.json 列出每个文件的 SHA-256。

## 新版规则

- 应用仅连接 project；所有患者 ID 使用当前 dbuser.Id，不兼容旧 ID。
- 会话/患者/计划/机构引用为 v2，报告制品为 img_v3，图片令牌为 v2。
- 旧引用、旧缓存、旧报告链接全部停用，不能通过重定向或旧文件恢复访问。
- 缓存冷启动。持久报告写入原卷内的 project-20260915-v2 子目录，旧报告目录不会读取或删除。
- 接口路径及请求/响应字段保留。公开纵向接口仍只接受语义窗口，不接受 session_refs 列表；旧列表保持原 400，并补停用提示。正常单次旧引用返回 404 / SESSION_REF_NOT_FOUND 和提示。

对调用方公告：

> 系统已完成数据升级。升级前的患者标识、会话引用、查询缓存和报告链接已停用，不再可用。请重新选择患者并查询训练记录，需要报告时重新生成。

公告在实际切换完成时发布，不能把源码包生成时间当作系统已上线时间。

## 部署步骤（Linux + Docker Compose）

1. 上传 ZIP 和同名 .sha256 文件，在上传目录校验：

   ```sh
   sha256sum -c aiwebapi-project-deploy-20260915.zip.sha256
   unzip aiwebapi-project-deploy-20260915.zip -d aiwebapi-project
   cd aiwebapi-project
   cp .env.ai_webapi.example .env.ai_webapi
   chmod 600 .env.ai_webapi
   ```

2. 编辑 .env.ai_webapi，填入服务器真实配置：

   - MainDB=project_main、MutiDBEnabled=false，只保留 DBS__0，Database=project。
   - MySQL 地址 mysql8 需能在 mysql_default 网络中解析；数据库账号使用当前业务查询所需的 SELECT 权限。
   - IReGoTools__SessionRefSigningKey 与 IReGoTools__ReportImage__SigningKey 使用两把新的独立密钥，分别运行 `openssl rand -hex 32` 生成并持久保存。每次重启不要换密钥。
   - DifyServiceBearerToken 使用实际调用方凭据；无需因迁移而轮换它。
   - 报告存储路径保持 `/app/data/patient-reports/project-20260915-v2`。
   - PublicBaseUrl 填服务器实际对外地址。医院运营、医生查询、场景导航的上游地址和凭据按服务器情况填写，示例值不能当作实际连接配置。
   - 清除所有 `<...>` 占位符，配置时不要把真实密钥提交到 Git。

3. 检查外部网络和卷。compose.yaml 使用已有 mysql_default 网络；首次在该服务器部署时创建需要的卷（同名已存在时不会清空）：

   ```sh
   docker network inspect mysql_default
   docker volume create ai_webapi_report_artifacts
   docker volume create ai_webapi_hospital_report_artifacts
   docker compose config --quiet
   docker compose build
   ```

   MySQL 必须已接入该网络。若服务器实际网络名不同，修改 compose.yaml 的 external network name。标准 Dockerfile 会获取 .NET SDK/运行时、NuGet 包、Chromium 和中文字体，构建需要网络；本包不是离线镜像。

4. 在维护窗口暂停入口流量，停止旧实例，更新 Dify/前端的新患者绑定并清空旧引用，再启动：

   ```sh
   docker compose up -d --force-recreate
   docker compose ps
   docker compose logs --tail=100 ai_webapi
   ```

   若旧实例属于其他 compose 项目，先停止该项目的 ai_webapi 服务，避免容器名/端口冲突。不要同时向旧版、新版分流；不要使用 `down -v` 删除历史卷。

5. 验证后恢复流量并记录切换时间：

   - 新患者 ID 查询与手机号查询的两个返回 ID 相等。
   - 历史目录返回 irego_s_v2_；新引用能进入单次分析/报告生成。
   - 上下文首次生成、第二次命中缓存；ForceRefresh=true 会刷新快照。
   - 旧 session_ref 明确返回“旧会话引用已停用，不再可用”。
   - 旧图片令牌和 img_v1/img_v2 永久地址均返回 404 停用提示。
   - 新报告能生成、显示，重启后新持久制品仍可读取；医院运营和康复上游工具正常。

故障时暂停入口，修复新版或回退到仍执行本次代次和旧引用拒绝规则的构建；不要悄悄重新开放已公告停用的旧实例。

## 开发验证与重新打包

```sh
dotnet build AI_WebApi/AI_WebApi.csproj
dotnet test AI_WebApi.Tests/AI_WebApi.Tests.csproj
python scripts/package-deploy.py --output dist/new-deployment.zip
```

测试项目目标 net8.0，需要相应 .NET/ASP.NET Core 运行时；若机器仅安装较新运行时，可在本地验证命令设置 `DOTNET_ROLL_FORWARD=Major`，这不修改应用 net6.0 目标。

真实 project 仓储检查默认跳过。需要时临时设置 `AIWEBAPI_TEST_PROJECT_CONNECTION` 并运行测试；该用例在 MySQL 只读事务中执行，不写数据库，连接字符串不要写进仓库。常规测试使用隔离的内存数据库。

打包脚本必须在 Git 工作区内运行，按允许列表读取当前文件并排除本地配置、编译输出及临时文件。ZIP 自身放在已忽略的 dist/ 下。生成包不会 commit 或 push，也不会修改服务器。
