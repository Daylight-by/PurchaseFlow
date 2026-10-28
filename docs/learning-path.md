# PurchaseFlow 五天学习手册

当前界面已增加总览、提交检查、决策实验台与打印简报。请优先使用 [V2 快速学习路线](quick-learning-v2.md) 对照当前代码；本文保留较长的基础学习安排。

这是一条面向有 Java、Spring Boot 或 Yudao 经验的学习路径。目标不是把 C# 语法背完，而是用 5 天真正读懂并能独立讲解这个项目：一次采购需求如何从页面进入应用服务，经过领域规则，写入 EF Core/SQLite，再回到页面；为什么某些操作会被拒绝；ABP 在其中承担了什么工作。

项目是一个可运行的 **.NET 10 + ABP 10.6.2 + ASP.NET Core Razor Pages + EF Core SQLite** 原型。业务代码在 app/PurchaseFlow/src，测试在 app/PurchaseFlow/test。根目录的 README.md 和 docs/business-rules.md 是本手册的配套资料。

## 先建立对应关系：Java/Spring/Yudao 到 C#/ASP.NET/ABP

| Java 经验 | 本项目中的对应概念 | 先看哪里 |
| --- | --- | --- |
| Java 类、包、Maven/Gradle module | C# 类、namespace、.csproj 项目；解决方案文件是 PurchaseFlow.slnx | app/PurchaseFlow/PurchaseFlow.slnx、各项目 .csproj |
| Spring Boot main、自动装配 | ASP.NET Core Program.cs + ABP Module 的 DependsOn | src/PurchaseFlow.Web/Program.cs、PurchaseFlowWebModule.cs |
| Spring DI、Service、Autowired | 内置 IServiceCollection DI；构造函数注入；ABP 自动注册应用服务 | PurchaseRequestAppService 构造函数 |
| Spring MVC Controller | ASP.NET Core Controller；ABP 可按约定从 Application Assembly 自动生成 API Controller | PurchaseFlowWebModule.ConfigureAutoApiControllers；src/PurchaseFlow.HttpApi/Controllers/PurchaseFlowController.cs |
| Spring MVC/Thymeleaf 页面 | Razor Pages：.cshtml 是视图，.cshtml.cs 是 PageModel | src/PurchaseFlow.Web/Pages/Purchasing/Index.*、Details.* |
| JPA Repository | ABP IRepository<TEntity, Guid>，底层由 EF Core 实现 | PurchaseRequestAppService 的 _requests、_quotes、_events |
| JPA/Hibernate Entity | EF Core Entity；ABP 的 AggregateRoot、审计、租户接口 | PurchaseRequest : FullAuditedAggregateRoot<Guid>, IMultiTenant |
| JPA @Transactional / service transaction | ABP Unit of Work；Web 模块调用 app.UseUnitOfWork() | PurchaseFlowWebModule.OnApplicationInitialization |
| Bean Validation / Jakarta Validation | DTO 的 System.ComponentModel.DataAnnotations，如 Required、Range；领域对象还做自己的检查 | PurchaseRequestDtos.cs、PurchaseRequest.cs |
| Spring Security、角色、PreAuthorize | ASP.NET Core Authorization + ABP Permission；Authorize | PurchaseFlowPermissions.cs、PurchaseFlowPermissionDefinitionProvider.cs |
| Yudao 菜单/权限/租户 | ABP 菜单贡献器、Permission Definition、IMultiTenant 和 CurrentTenant | Web/Menus、Application.Contracts/Permissions、领域实体 |
| JPA mapping、Flyway/Liquibase | EF Core OnModelCreating 和 Migrations | PurchaseFlowDbContext.cs、EntityFrameworkCore/Migrations |
| application.yml | appsettings.json、环境变量、User Secrets | src/PurchaseFlow.Web/appsettings.json、DbMigrator/appsettings.json |
| JUnit | xUnit；断言常见为 Assert、Shouldly | test/PurchaseFlow.Domain.Tests、test/PurchaseFlow.Web.Tests |

有两个容易混淆的点：第一，ABP Application Service 不是 Spring Controller，它更接近“面向用例的应用服务”，同时可被 Razor Pages 和 ABP 约定式 API 调用。第二，PurchaseFlow.HttpApi/Controllers/PurchaseFlowController.cs 目前只是本项目的控制器基类；采购用例的具体 API Controller 由 ConfigureAutoApiControllers 按约定生成，因此不要在目录中寻找一个名为 PurchaseRequestController 的手写类。

## 环境准备与第一次启动

### 需要的软件

- .NET 10 SDK：所有项目的 TargetFramework 都是 net10.0。
- Node.js 与 Yarn：Web 项目的 package.json 依赖 @abp/aspnetcore.mvc.ui.theme.basic。
- ABP CLI 10.6.2：项目 README 指定此版本。
- 可选的 IDE：Visual Studio、Rider 或 VS Code 均可；学习时建议同时保留 PowerShell 窗口观察日志。

在仓库根目录 D:\shuju\AAA\abp-procurement-workflow 打开 PowerShell，执行：

~~~powershell
dotnet --version
node --version
dotnet tool install -g Volo.Abp.Cli --version 10.6.2
npm install -g yarn
yarn --version
~~~

如果已安装工具，dotnet tool install 报已存在时不必重复安装；可用 abp --version 确认 CLI。项目自带 app/PurchaseFlow/dotnet-tools.json，其中 dotnet-ef 是 10.0.13，需要时在解决方案目录执行 dotnet tool restore。

### 恢复依赖和前端库

~~~powershell
Set-Location D:\shuju\AAA\abp-procurement-workflow\app\PurchaseFlow
dotnet restore PurchaseFlow.slnx
Set-Location src\PurchaseFlow.Web
abp install-libs
~~~

abp install-libs 是项目 README 给出的命令；它安装 ABP Razor UI 静态依赖。不要把命令改成从仓库根目录执行，因为后续 SQLite 路径与本次启动目录有关。

### 建库、种子数据和 Web

先开第一个 PowerShell 窗口运行迁移器：

~~~powershell
Set-Location D:\shuju\AAA\abp-procurement-workflow\app\PurchaseFlow\src\PurchaseFlow.DbMigrator
dotnet run
~~~

它使用 DbMigratorHostedService 创建 ABP 应用、执行 PurchaseFlowDbMigrationService.MigrateAsync，应用已有迁移并执行种子数据，然后自行退出。业务迁移有 `20261023165331_InitialCreate` 和 `20261023182758_HostOrderReferenceUnique` 两次；后者为没有 TenantId 的宿主数据补充销售单号唯一索引。

再开第二个窗口启动 Web：

~~~powershell
Set-Location D:\shuju\AAA\abp-procurement-workflow\app\PurchaseFlow\src\PurchaseFlow.Web
dotnet dev-certs https --trust
dotnet run --launch-profile PurchaseFlow.Web
~~~

浏览器打开 https://localhost:44345。端口来自 src/PurchaseFlow.Web/Properties/launchSettings.json 和 appsettings.json 的 App:SelfUrl。Web 与 DbMigrator 的连接字符串都是 Data Source=../../PurchaseFlow.db;，所以必须从各自项目目录启动；按上述目录启动时，数据库文件位于 app/PurchaseFlow/PurchaseFlow.db。不要同时运行两个 DbMigrator。

ABP 模板管理员账号是 admin / <local-template-password>，仅用于本地演示。DemoRoleDataSeedContributor 自动创建 buyer 和 reviewer 角色并授予权限；只有在 `Demo:BuyerPassword`、`Demo:ReviewerPassword` 配置存在时才创建 `demo.buyer`、`demo.reviewer` 用户。可以在迁移器窗口用 `$env:Demo__BuyerPassword`、`$env:Demo__ReviewerPassword` 设置私有密码后重跑迁移器，详见 README。采购员需要 PurchaseFlow.Requests、PurchaseFlow.Requests.Edit；审核员需要 PurchaseFlow.Requests、PurchaseFlow.Requests.Review。审核权限本身不包含编辑权限。

### 最小验收

登录后进入“采购需求”，创建销售金额 100000 的需求；增加 A=82000、10 天，B=86000、5 天的报价；选择 B，理由填写“交付时间更短”。页面应显示预计毛利 14000、成本差额 4000 和交付提前 5 天。提交、退回（原因必填）、重新提交并批准，详情页应出现四条对应的中文操作记录；代码中的动作值依次是 Submitted、Returned、Submitted、Approved。批准后编辑和提交按钮应消失。再建一条只含一条报价的需求，提交应显示“提交前至少录入两条供应商报价”。

## 五天计划：每天有可验证的产物

每天先读代码，再运行或测试，最后用自己的话录一段 2～5 分钟解释。下面的“产物”不是笔记标题，而是可以实际检查的结果。

### 第 1 天：从业务流程和状态开始

1. 读 README.md、docs/business-rules.md，画出 Draft → PendingReview → Approved 和 PendingReview → Returned → 可编辑状态的状态图。准确表述：代码中的退回状态是 Returned，PurchaseRequest.IsEditable 只允许 Draft 或 Returned 编辑。
2. 读 PurchaseRequestStatus.cs、PurchaseFlowErrors.cs、PurchaseRequest.cs、SupplierQuote.cs、ReviewEvent.cs。重点找私有 setter、构造函数、SetDetails、Submit、Approve、Return。
3. 运行领域测试：

~~~powershell
Set-Location D:\shuju\AAA\abp-procurement-workflow\app\PurchaseFlow
dotnet test test\PurchaseFlow.Domain.Tests\PurchaseFlow.Domain.Tests.csproj
~~~

产物：一张状态图、一个实体字段表、领域测试通过记录（两条报价/选中报价、提交后冻结、批准后不能修改）。能解释为什么 PurchaseRequest.Submit(1) 抛出 PurchaseFlowErrors.NeedTwoQuotes，以及为什么 SelectQuote 只检查理由和可编辑状态，报价归属检查在 Application 层完成。

### 第 2 天：理解 Application 用例、权限和 DTO

1. 按接口到实现阅读 Application.Contracts/Purchasing/IPurchaseRequestAppService.cs、PurchaseRequestDtos.cs、Application/Purchasing/PurchaseRequestAppService.cs。
2. 把 10 个用例写成表：列表、详情、创建、修改、增加报价、删除报价、选择报价、提交、批准、退回。注意 GetOwnedRequestAsync 用 BuyerUserId 强制“只能修改自己的需求”；审核读写则由 ReviewRequests 权限保护。
3. 查 PurchaseFlowPermissions.cs 和 PurchaseFlowPermissionDefinitionProvider.cs，区分查看、编辑、审核三个权限。
4. 运行完整测试：

~~~powershell
dotnet test PurchaseFlow.slnx
~~~

产物：一张“方法 → 输入 DTO → 状态变化/错误码 → 权限”的表；能现场指出 ExpectedGrossProfit = SaleAmount - selected.Amount 在 GetAsync 中计算，且它是预计毛利，不是净利润。

### 第 3 天：理解 EF Core、迁移和 ABP 基础设施

1. 读 EntityFrameworkCore/PurchaseFlowDbContext.cs：三个业务 DbSet 是 PurchaseRequests、SupplierQuotes、ReviewEvents；OnModelCreating 配置表名、长度、金额精度、索引和级联删除。
2. 读 PurchaseFlowEntityFrameworkCoreModule.cs：AddAbpDbContext 注册仓储，UseSqlite 选择 SQLite，includeAllEntities: true 让非聚合根也拥有默认仓储。
3. 对照 Migrations/20261023165331_InitialCreate.cs、20261023182758_HostOrderReferenceUnique.cs 和 PurchaseFlowDbContextModelSnapshot.cs，确认迁移不是业务规则本身，而是数据库结构版本。
4. 读 IMultiTenant、TenantId 和 CurrentTenant.Id 的使用；ABP 的数据过滤会配合租户上下文，Application 创建实体时也显式写入当前租户。
5. 读 AbpUnitOfWorkDefaultOptions：本 SQLite 模板将 TransactionBehavior 设为 Disabled。这意味着文档中的“同一保存批次”不能讲成生产级事务保证；切换正式数据库必须重新验证事务与并发。

产物：能回答“一个报价表为什么知道属于哪个需求”（SupplierQuote.PurchaseRequestId 外键）和“为什么同一租户内销售单号不能重复”（TenantId + OrderReference 唯一索引；宿主 TenantId 为 NULL 时另有部分唯一索引）。

### 第 4 天：从浏览器回溯完整 Web 请求链路

1. 读 Web/Program.cs 和 PurchaseFlowWebModule.cs，定位模块依赖、认证、UseMultiTenancy、UseUnitOfWork、UseAuthorization、Swagger，以及 ConfigureAutoApiControllers。
2. 读 Pages/Purchasing/Index.cshtml.cs 和 Index.cshtml：GET 查询列表；POST OnPostCreateAsync 调用 CreateAsync，成功后重定向详情页。
3. 读 Details.cshtml.cs 和 Details.cshtml：OnPostAddQuoteAsync、OnPostSelectAsync、OnPostSubmitAsync、OnPostApproveAsync、OnPostReturnAsync 分别把表单字段组装成 DTO 调应用服务；RunAsync 捕获 BusinessException 并用本地化错误码重新加载页面。
4. 在浏览器开发者工具观察一次 POST，再在 Logs/logs.txt 或控制台查看 Web 日志；不要只看最终页面，要能从 handler 追到 Application 方法和 Repository。

产物：一张请求链路图，至少包含“表单 → Razor PageModel handler → IPurchaseRequestAppService → PurchaseRequestAppService → Domain method → IRepository → PurchaseFlowDbContext → SQLite → 重定向/详情 DTO”。

### 第 5 天：独立演示、改动和面试表达

1. 不看手册完成一次完整验收流程，并用两个账户说明采购员与审核员的权限差异。
2. 完成下面至少两项亲手改动，跑对应测试或浏览器验收；建议四项全部做完。
3. 使用本文最后的 5 分钟脚本录制一次讲解，随后用面试问题自问自答。

产物：一个能运行的数据库、一次带退回记录的采购流程、至少两个亲手改动的 diff/截图或测试输出，以及一段可以解释“为什么把规则放在这一层”的演示。

## Domain → Application → EF Core → Web：一次提交请求到底发生什么

以详情页点击“提交审核”为例：

1. Details.cshtml 的表单以 POST 发送到 asp-page-handler="Submit"，路由中的 id 是采购需求 GUID。
2. DetailsModel.OnPostSubmitAsync 调用 RunAsync，再调用注入的 IPurchaseRequestAppService.SubmitAsync(id)。
3. PurchaseRequestAppService 先通过 GetOwnedRequestAsync 取出需求并确认当前用户是 BuyerUserId，再从 _quotes 读取该需求报价。如果选中的报价 ID 不属于该需求，抛出 NeedSelectedQuote。
4. Application 调用领域对象 request.Submit(quotes.Count)。领域对象检查当前状态可编辑、报价数至少为 2、已选报价存在、选择理由非空，成功后把状态改为 PendingReview。状态转换和业务错误集中在 Domain，而不是依赖页面按钮是否显示。
5. Application 调用 _requests.UpdateAsync(request)，并通过 AddEventAsync 插入 ReviewEvent，动作字符串为 Submitted，理由作为评论保存。Repository 由 ABP/EF Core 注入，背后使用 PurchaseFlowDbContext。
6. Web 请求管线中的 UseUnitOfWork 管理本次应用服务调用的工作单元；但本项目的 EF Core 模块显式禁用了环境事务，这是 SQLite 模板的边界。
7. 成功后 RunAsync 重定向回 /Purchasing/Details?id=...。GET 重新调用 GetAsync，Application 计算选中报价、最低报价、预计毛利和历史记录，页面据 CanEdit/CanReview 决定显示哪些操作。

创建、加报价、选报价、批准和退回也是同一条路径，只是 Domain 方法与权限不同。批准要求状态为 PendingReview，将状态改为 Approved 并记录 Approved；退回要求评论非空，将状态改为 Returned 并记录 Returned。

## 亲手改动练习（至少完成四项）

每项都应先提交一个可回退的本地 commit 或保存原文件，完成后再恢复到练习前状态，以免影响后续验收。

### 练习 1：增加“至少三家报价”规则

在 src/PurchaseFlow.Domain/Purchasing/PurchaseRequest.cs 的 Submit 中把阈值从 2 改为 3，并在 PurchaseFlowErrors 增加新的错误码与 zh-Hans.json 文案；更新或新增 test/PurchaseFlow.Domain.Tests/Purchasing/PurchaseRequestTests.cs。

预期结果：两条报价提交抛出新的业务错误，三条报价并选中一条后才进入 PendingReview；测试能证明规则不依赖 Web 页面。完成后恢复，因为当前项目 README 和验收流程的正式演示规则是至少两条报价。

### 练习 2：禁止选择比最低报价高超过 10% 的方案

在 PurchaseRequestAppService.SelectQuoteAsync 读取报价后加入应用层校验，并使用新的 PurchaseFlowErrors 错误码和本地化文案。选择属于别的需求的报价仍必须先抛出 EntityNotFoundException。

预期结果：销售金额 100000、最低报价 82000 时，90000 的报价不能选择；86000 可以选择。这个练习讨论的是“跨对象比较”应该放在哪里：Domain 当前只知道所选 ID 和理由，Application 才同时拿到了报价集合。

### 练习 3：增加详情页的“报价数量”提示

在 PurchaseRequestDetailDto 增加只读字段（例如 QuoteCount），在 PurchaseRequestAppService.GetAsync 赋值，在 Details.cshtml 的“供应商报价”标题旁显示它。不要从页面直接查询 DbContext。

预期结果：详情页显示与列表相同的实际报价数量；删除或添加报价后刷新页面数字变化。测试重点是 DTO 映射和页面显示，不要改变领域状态规则。

### 练习 4：为列表增加 Returned 状态统计卡片

PurchaseRequestListDto 已有 ReturnedCount，PurchaseRequestAppService.GetListAsync 也已有统计；在 Pages/Purchasing/Index.cshtml 增加一个与现有草稿/待审核/已批准卡片一致的退回卡片，并让它链接 status=Returned。

预期结果：退回一条需求后，列表卡片显示数量，点击卡片只看退回记录。该练习让学生看到“后端已经提供数据，页面只负责呈现和路由”，也能避免不必要地重新写查询。

### 练习 5：补一条“重复供应商忽略大小写”的边界测试

当前重复检查在 PurchaseRequestAppService.AddQuoteAsync，使用 StringComparison.OrdinalIgnoreCase，错误码是 PurchaseFlowErrors.DuplicateSupplier。补充一个 Application 测试或把现有测试基础设施读懂后添加测试，验证 Acme 后再加 acme 被拒绝。

预期结果：第二次增加报价抛出 PurchaseFlow:DuplicateSupplier；Acme East 仍可作为不同名称。这个练习诚实地展示：它目前是 Application 查询规则，不是 SupplierQuote 构造函数中的 Domain 不变式。

## 常见错误排查

| 现象 | 先检查 | 处理 |
| --- | --- | --- |
| dotnet restore 找不到项目 | 当前是否在 app/PurchaseFlow，文件名是否为 PurchaseFlow.slnx | 执行 dotnet restore PurchaseFlow.slnx；不要猜一个不存在的 .sln |
| 启动后找不到 PurchaseFlow.db 或迁移结果为空 | 启动目录和连接字符串 | DbMigrator 必须从 src/PurchaseFlow.DbMigrator，Web 必须从 src/PurchaseFlow.Web；两处配置均是 Data Source=../../PurchaseFlow.db; |
| https://localhost:44345 证书错误 | .NET 开发证书是否可信 | 执行 dotnet dev-certs https --trust，再重启 Web |
| 44345 已占用 | 是否有旧 Web 进程 | 停掉旧 dotnet 进程，或修改 launchSettings.json、appsettings.json、DbMigrator 的 OpenIddict:Applications:PurchaseFlow_Web:RootUrl 后保持三处一致 |
| 登录失败 | DbMigrator 是否完整退出、数据库是否来自同一个目录 | 先重新运行 DbMigrator；确认使用本地演示账号，公开部署不能使用模板密码 |
| 页面没有创建/编辑按钮 | 当前用户是否有 PurchaseFlow.Requests.Edit | 管理员到 Identity management 给用户/角色授予查看和编辑权限；审核权限不等于编辑权限 |
| 可以看见需求但修改时报授权错误 | 需求的 BuyerUserId 是否是当前用户 | Application 的 GetOwnedRequestAsync 限制只有创建者修改；这不是通过隐藏按钮解决的 |
| 提交提示至少两家报价 | 报价数量、选中报价、选择理由 | 当前 PurchaseRequest.Submit 要求至少两条、选择 ID 存在、理由非空 |
| 退回提交失败 | 评论是否为空 | ReturnAsync 在 Application 先检查 NeedReviewComment；页面也有 required，但后端检查才是可靠边界 |
| 加报价失败且供应商名字看似不同 | 是否只是大小写不同 | Application 使用 OrdinalIgnoreCase 检查同一需求内重复供应商 |
| 修改已批准/待审核需求失败 | 状态是否为 Draft 或 Returned | PurchaseRequest.IsEditable 和 EnsureEditable 会拒绝；这是预期行为 |
| 修改 appsettings.json 后没有效果 | 是否改错项目或被环境配置覆盖 | Web 和 DbMigrator 各自有 appsettings.json；从正确项目目录启动并查看日志 |
| abp install-libs 找不到 | ABP CLI、Node.js、Yarn 是否安装，当前目录是否 Web 项目 | 在 src/PurchaseFlow.Web 执行 abp --version、yarn --version 后再运行 |
| 删除迁移后 DbMigrator 不能自动创建 | 仓库的迁移器只在能定位解决方案和迁移目录时尝试 ABP CLI；当前解决方案文件是 .slnx | 学习时不要删除 Migrations；若要重新建库，先恢复已有迁移并按团队约定使用 dotnet ef/ABP CLI，确认项目路径后再操作 |

## 五分钟演示脚本

**0:00–0:40，定位项目。**“这是 PurchaseFlow，技术栈是 .NET 10、ABP 10.6.2、Razor Pages、EF Core SQLite。核心代码分成 Domain、Application.Contracts、Application、EntityFrameworkCore 和 Web。它是教学原型，数据与规则都是模拟的。”展示 src 目录和 docs/business-rules.md。

**0:40–1:30，创建需求。**登录本地管理员，打开采购询价，创建 SO-2026-001，销售金额 100000。说明页面的 Index.cshtml 由 IndexModel.OnPostCreateAsync 调用 CreateAsync，创建出的 PurchaseRequest 初始状态是 Draft，并记录 BuyerUserId。

**1:30–2:30，比较报价。**在详情页增加 A 82000/10 天和 B 86000/5 天，选择 B 并填写“交付时间更短”。指出页面展示的预计毛利是 100000 - 86000 = 14000；选 B 比最低价贵 4000，所以展示成本溢价。它是预计毛利，不含税费、运输和履约成本。

**2:30–3:25，提交和审核。**点击提交，状态变为 PendingReview，应用服务调用 PurchaseRequest.Submit，成功后插入 ReviewEvent(Submitted)。用审核账户打开详情，填写退回原因，状态变为 Returned；采购员改正后再提交，审核员批准，状态变为 Approved。指出操作记录来自 ReviewEvent，不是单纯的日志字符串。

**3:25–4:20，演示后端约束。**尝试只放一条报价提交，页面显示本地化错误；尝试改已批准需求，按钮消失且后端仍会抛 NotEditable；用另一个采购员尝试修改，Application 的 GetOwnedRequestAsync 拒绝。强调页面权限是体验层，领域和应用服务才是安全边界。

**4:20–5:00，讲架构。**“Razor Page handler 调 Application Contract；Application 负责当前用户、权限、跨实体查询和 DTO；Domain 负责状态不变式；Repository 由 EF Core DbContext 持久化到 SQLite；GET 再把 DTO 映射回页面。ABP 提供模块、DI、权限、租户、审计、Unit of Work 和约定式 API。”最后展示 PurchaseFlowDbContext.OnModelCreating 的索引和两次迁移，说明宿主数据需要额外唯一索引。

## 面试官可能追问：诚实回答模板

1. **为什么不把所有校验写在 Razor Page？** 页面校验只改善输入体验，不能防止直接调用 API。状态、金额和报价数量在 PurchaseRequest 中，归属、报价集合和权限在 PurchaseRequestAppService 中，因此非浏览器调用也受约束。
2. **PurchaseRequest 是不是把报价作为真正的 Aggregate 子实体？** 目前代码把 PurchaseRequest 定义为 FullAuditedAggregateRoot，报价是独立 Entity<Guid>，用 PurchaseRequestId 关联，Application 通过多个 Repository 组合用例。它可以作为教学原型工作，但是否改成严格聚合内集合要结合并发、生命周期和查询需求决定。
3. **为什么 Application Service 既查数据又算预计毛利？** 毛利需要销售金额和选中报价，属于详情用例的派生展示数据；当前实现直接在 GetAsync 映射 DTO。若规则变成可复用的正式财务计算，应进一步抽成领域服务或明确的计算组件，而不能把它当作真实利润。
4. **ABP 的权限和 ASP.NET Authorize 是什么关系？** 类上的 Authorize(PurchaseFlowPermissions.Requests) 要求查看权限，方法上的属性再要求编辑或审核权限；ABP Permission Definition 将这些权限注册到角色和用户。页面的 CanEdit/CanReview 只决定界面是否显示操作，后端属性和业务检查仍会执行。
5. **怎么保证跨租户数据不泄露？** 业务实体实现 IMultiTenant 并保存 TenantId，创建时使用 CurrentTenant.Id；ABP 的数据过滤结合当前租户上下文限制查询。GetOwnedRequestAsync 还做用户归属检查。正式系统仍应补充多租户集成测试和安全评审，不能仅凭接口声明保证所有边界。
6. **这个项目是否有事务保证？** Web 使用 UseUnitOfWork，提交/审核会更新需求并插入历史事件；但 PurchaseFlowEntityFrameworkCoreModule 明确将 TransactionBehavior 设为 Disabled，这是 ABP SQLite 模板为身份安全日志等场景保留的配置。因此不能把它宣称为生产级跨写入事务，切换正式数据库需重新设计和验证。
7. **为什么没有手写采购 Controller？** PurchaseFlowController 是 AbpControllerBase 基类；PurchaseFlowWebModule.ConfigureAutoApiControllers 从 PurchaseFlowApplicationModule Assembly 创建约定式 API Controller。Razor Pages 直接注入接口，所以页面请求不需要手写 MVC Controller。
8. **EF Core 迁移和领域规则有什么区别？** PurchaseFlowDbContext.OnModelCreating 负责表、列、索引、外键和精度，Migrations 记录结构版本；PurchaseRequest.Submit 负责“至少两条报价”等业务不变式。数据库约束不能替代领域状态机，领域规则也不能替代唯一索引。
9. **如何测试只允许创建者修改？** 构造两个用户身份，调用 UpdateAsync/AddQuoteAsync 等方法，确认非 BuyerUserId 得到 AbpAuthorizationException；再用领域测试覆盖状态冻结。当前仓库的现成领域测试主要覆盖状态规则，跨用户 Application 测试是可以继续补强的部分。
10. **如果需求被退回，为什么还能修改？** IsEditable 明确允许 Draft 或 Returned，退回原因写进 ReviewEvent。重新提交会再次要求至少两条报价、选中报价和理由；批准后状态不可回退，当前项目没有“撤销批准”用例。
11. **项目的数据是真实业务吗？** 不是。供应商、报价、销售单、审批流程、权限名称和预计毛利都是从一个教学切片抽出的模拟规则；仓库没有接入真实 ERP、供应商、订单或公司审批制度。
12. **哪些业务问题目前未知？** 真实系统是否要求三家询价、是否允许同一供应商多版本报价、税费/运费/汇率如何计算、谁有最终审批权、是否需要金额分级、并发编辑和审计留存多久，源码都没有给出答案。面试时应明确说“需要业务确认”，不要把演示规则包装成公司制度。

## 明确边界：哪些是模拟规则，哪些仍未知

当前可从源码确认的模拟规则包括：销售金额和报价金额必须大于零，交付天数不能小于零；提交至少两条报价；选中报价必须属于该需求并填写理由；待审核和已批准不可编辑；退回需要原因；批准后冻结；预计毛利等于销售金额减所选报价；同一需求不允许同名（忽略大小写）供应商报价。这些规则服务于教学演示，不能推断目标公司的正式政策。

目前未知或未实现的内容包括：真实供应商主数据与资质、采购合同和付款、税费/运费/汇率、库存和交付验收、金额分级审批、通知、并发版本控制、正式数据库事务、生产密钥、数据保留与合规要求。源码中的 SQLite、模板管理员账号、开发证书、示例密码和 DemoRoleDataSeedContributor 都只适合本地演示；公开部署前必须替换凭据、证书、加密密钥，并重新评估事务、并发和权限。

完成五天后，学习者应该能够打开任意一个按钮对应的 handler，说出它调用的接口和 DTO，继续追到 Application 的权限/归属检查、Domain 的状态规则、EF Core 的表映射，再解释页面如何根据返回 DTO 渲染；也应该能明确区分“源码已经证明的行为”和“需要向业务方确认的真实制度”。



