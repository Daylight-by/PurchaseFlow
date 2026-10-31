# PurchaseFlow 快速学习手册 v2

这是一份给 Java 后端学习者的源码导读与现场练习手册。目标是能从浏览器一个按钮追到 Razor PageModel、应用服务、领域方法、仓储和 EF Core 映射，并能说清哪些是当前源码已经证明的行为，哪些仍需业务确认。它不是“已经完全掌握”的证明，也不代表任何公司的正式采购制度。

## 0. 当前基线与学习边界

当前仓库是一个可本地运行的教学业务切片：.NET 10、ABP 10.6.2、ASP.NET Core Razor Pages、EF Core、SQLite。主流程是“采购需求 → 供应商报价 → 选择依据 → 提交审核 → 退回或批准”。三类核心业务记录是 `PurchaseRequest`、`SupplierQuote`、`ReviewEvent`，另有 `Supplier`、`Material` 主数据档案；Identity、权限、租户、审计等由 ABP 模块提供。

源码能确认的示例规则包括：销售金额大于零、每条报价单价不小于零、报价必须逐项覆盖当前需求全部明细（合计由服务端按明细汇总）、交付天数不小于零；提交至少两条报价；选中报价必须属于当前需求并填写理由；草稿和已退回可编辑；待审核和已批准不可编辑；退回需要审核意见；批准后进入冻结状态；预计毛利等于销售金额减选中报价；同一需求内供应商名称不区分大小写地不能重复；供应商与物料档案编码在租户内唯一，档案可停用、停用后不能再被新报价或新需求明细引用，单据上的名称/规格仍是快照。这些都是教学切片规则，不是莫先生所在公司的制度。

新增的分析能力只服务于比较和提示：服务端 `QuoteDecisionAnalyzer.Analyze` 是无 I/O 的静态计算，给出三项提交检查、最低价/最快交期（支持并列）、预计毛利率、Pareto 价期均不占优和负毛利提示。提示不会阻止提交。详情页的“决策实验台”在浏览器端按价格/交期权重计算相对分数，调节权重不会改变已选报价，也不写数据库。独立 A4“决策简报”读取详情 DTO，打印动作是浏览器的 `window.print()`。运行时这些功能不生成 AI 内容；项目的页面、后端和测试曾借助 AI 生成并迭代，学习者正在按代码链路学习和验证，报价、理由和业务规则仍是示例数据与人工输入。

## 1. 先跑起来：本机工具与常规 .NET 命令

仓库已有本机工具目录 `.tools`。下面的命令从仓库根目录执行（把 `$repo` 换成你的本地仓库路径），使用仓库内 SDK；另一台电脑可把 `$dotnet` 换成系统 `dotnet`，把 `$abp` 换成已安装的 ABP CLI。

```powershell
$repo = '<你的仓库根目录>'
$dotnet = Join-Path $repo '.tools\dotnet\dotnet.exe'
$abp = Join-Path $repo '.tools\abp-cli\abp.exe'

Set-Location (Join-Path $repo 'app\PurchaseFlow')
& $dotnet --info
& $dotnet restore PurchaseFlow.slnx

# 需要安装/更新前端库时，在 Web 项目目录使用本机 ABP CLI：
Set-Location (Join-Path $repo 'app\PurchaseFlow\src\PurchaseFlow.Web')
& $abp --version
& $abp install-libs
```

本机已有演示数据库时，直接双击仓库根目录的 `启动项目.cmd` 即可，不需要重复运行迁移器或重建已有数据。脚本只有发现已有 Web 实例时才会自动打开浏览器；首次启动需要等待窗口出现 `Now listening on: https://localhost:44345`，再手动点击或打开该 HTTPS 地址。新电脑或新数据库才需要从各自项目目录运行迁移器和 Web，因为配置中的 `Data Source=../../PurchaseFlow.db;` 是相对当前工作目录的：

```powershell
Set-Location $repo\app\PurchaseFlow\src\PurchaseFlow.DbMigrator
& $repo\.tools\dotnet\dotnet.exe run

Set-Location $repo\app\PurchaseFlow\src\PurchaseFlow.Web
& $repo\.tools\dotnet\dotnet.exe run --launch-profile PurchaseFlow.Web
```

使用系统 SDK 时等价于：

```powershell
Set-Location $repo\app\PurchaseFlow
dotnet restore PurchaseFlow.slnx
dotnet test PurchaseFlow.slnx
Set-Location src\PurchaseFlow.DbMigrator
dotnet run
Set-Location ..\PurchaseFlow.Web
dotnet run --launch-profile PurchaseFlow.Web
```

本机启动脚本 `启动项目.cmd` 会调用 `start-project.ps1`。新电脑首次初始化时，迁移器会创建 `buyer`、`reviewer`、`receiver` 角色并播种演示主数据；演示用户只有在本机私密配置提供密码时才会创建。若使用本地 HTTPS 证书遇到问题，可按本机环境执行 `dotnet dev-certs https --trust`。不要把密码、证书或私密配置写入仓库。

## 2. 90 分钟上手路线

### 0–10 分钟：确认运行与地图

1. 从根目录双击 `启动项目.cmd`；新电脑首次初始化才先运行迁移器。登录后打开首页 Dashboard。
2. 认出六个主要职责区：`Domain.Shared` 放状态和错误码，`Domain` 放实体与状态不变式，`Application.Contracts` 放 DTO/接口/权限，`Application` 编排用例，`EntityFrameworkCore` 映射 SQLite，`Web` 放 Razor Pages。`DbMigrator` 是建库和种子数据的辅助项目。
3. 先看 `PurchaseFlow.slnx`，注意解决方案文件是 `.slnx`，不要凭习惯寻找不存在的 `.sln`。

### 10–25 分钟：用真实页面建立业务心智模型

1. 在“采购需求”创建销售金额 100000 的需求。
2. 录入需求明细（办公椅 10 把、会议桌 2 张），再按明细逐项录入 A 的报价单价（合计 82000 元、10 天）与 B 的报价单价（合计 86000 元、5 天）；选择 B，填写“交付时间更短”。
3. 观察预计毛利 14000 元、比最低报价高 4000 元、交付提前 5 天；页面明确这是预计毛利，不是净利润。
4. 在首页确认 Dashboard 的需求数、报价数、待审核队列和已批准金额只来自当前租户可见数据。

### 25–45 分钟：走通状态和角色

1. 点击提交审核，状态变为 `PendingReview`，历史中出现 `Submitted`。
2. 用有 `ReviewRequests` 权限的审核角色退回，必须填写理由；状态变为 `Returned`。
3. 用创建者补充或修改后再次提交，再由审核角色批准；状态变为 `Approved`，历史中出现 `Approved`。
4. 尝试修改已批准需求，确认页面按钮隐藏只是体验层，后端仍由领域状态检查拒绝。

### 45–60 分钟：从按钮回溯代码

按“表单 → handler → `IPurchaseRequestAppService` → `PurchaseRequestAppService` → Domain → `IRepository` → `PurchaseFlowDbContext` → SQLite → 重定向/DTO”回溯一次提交。重点打开 `Pages/Purchasing/Details.cshtml.cs`、`PurchaseRequestAppService.cs`、`PurchaseRequest.cs` 和 `PurchaseFlowDbContext.cs`。

### 60–75 分钟：理解分析器与实验台

打开 `QuoteDecisionAnalyzer.cs` 与对应测试。确认它不注入仓储、不改变实体，只创建 `QuoteDecisionAnalysisDto`。再拖动详情页实验台的价格权重，观察排名变化，说明这只是当前报价集合的相对比较。

### 75–90 分钟：边界验证

1. 只录入一条报价后提交，确认 `NeedTwoQuotes`。
2. 清空选择理由，确认 `NeedSelectionReason`。
3. 用另一名采购员尝试修改他人需求，确认 `GetOwnedRequestAsync` 拒绝。
4. 打开“查看决策简报”，打印预览，确认它是独立 A4 页面且不新增数据库记录。
5. 运行 `dotnet test PurchaseFlow.slnx`，把测试输出和浏览器验收结果分开记录。

## 3. 两天深入路线

### 第一天：领域、用例和页面链路

上午先读 `PurchaseRequestStatus`、`PurchaseFlowErrors`、`PurchaseRequest`、`SupplierQuote`、`ReviewEvent`。画出 `Draft → PendingReview → Approved` 与 `PendingReview → Returned → PendingReview`，标出可编辑状态只有 `Draft` 和 `Returned`。运行领域测试，理解 `Submit(1)` 为什么抛 `NeedTwoQuotes`，以及 `SelectQuote` 为什么只检查理由和可编辑状态：跨对象的“报价 ID 是否属于这条需求”由 Application 层先查询并检查。

下午按接口到实现阅读 `IPurchaseRequestAppService`、`PurchaseRequestDtos`、`PurchaseRequestAppService`。把接口和按钮用例（包括 Dashboard 入口）与输入 DTO、权限、状态影响写成表。特别确认 `GetAsync` 中的 `ExpectedGrossProfit = SaleAmount - selected.Amount` 是展示用的预计毛利。最后从 `Details.cshtml` 逐个追 POST handler，理解 `RunAsync` 如何捕获 `BusinessException` 并重新加载页面。

### 第二天：持久化、ABP 基础设施和表达

上午读 `PurchaseFlowDbContext.OnModelCreating`、EF Core 模块和 8 个迁移（InitialCreate、HostOrderReferenceUnique、AddPurchaseRequestLines、AddPurchaseOrders、AddGoodsReceipts、AddReceiptConcurrency、AddSupplierQuoteLines、AddMasterData）。确认业务 DbSet（需求/明细、报价/报价明细、订单/明细、收货/明细、审核记录、供应商档案、物料档案）、列长度、金额精度、外键级联、租户索引和宿主 `TenantId IS NULL` 的部分唯一索引。理解 `AddDefaultRepositories(includeAllEntities: true)` 为什么让非聚合根报价和审核记录也有默认仓储。

中午专门核对租户与工作单元边界：实体实现 `IMultiTenant` 并保存 `TenantId`；Application 创建时写入 `CurrentTenant.Id`；ABP 数据过滤和当前租户上下文共同影响查询。Web 管线调用 `UseMultiTenancy`、`UseUnitOfWork`、`UseAuthorization`，但 EF Core 模块把 `TransactionBehavior` 设为 `Disabled`。所以“同一请求通常共享工作单元”不能表述成生产级事务保证，切换正式数据库时必须重新设计和验证事务、并发与重试。

下午完成五个小练习中的至少三个，跑测试或做浏览器验收；再用 30 秒介绍和 5 分钟脚本练习一次，始终把“源码已证明”“本地演示可见”“尚需业务确认”分开说。

## 4. Java 映射：用熟悉的语言理解 ABP

| Java 常见概念 | 本项目对应物 | 需要注意的差异 |
| --- | --- | --- |
| Spring Boot `main` | `Web/Program.cs` 的 `AddApplicationAsync<PurchaseFlowWebModule>()` | ABP 模块依赖由 `[DependsOn]` 组合，启动时初始化模块。 |
| Spring `@Service` / Service 类 | `PurchaseRequestAppService : ApplicationService` | 应用服务是用例入口，负责权限、当前用户、跨实体查询和 DTO 编排。 |
| REST Controller | Razor `PageModel` handler；同时 ABP 可由 `ConfigureAutoApiControllers` 为应用服务生成约定式 API | 当前现场演示主要走 Razor Pages；没有为每个按钮手写 MVC Controller。 |
| Java Bean/record DTO | `CreatePurchaseRequestDto`、`SupplierQuoteDto` 等普通 C# 类 | `[Required]`、`[Range]`、`[StringLength]` 负责输入约束；领域方法仍要再次保护关键不变式。 |
| JPA `@Entity` | `PurchaseRequest`、`SupplierQuote`、`ReviewEvent` | 私有 setter 和公开领域方法控制状态变化；EF Core 在 `OnModelCreating` 配置映射。 |
| Spring Data `JpaRepository` | ABP `IRepository<TEntity, Guid>` | 由 DI 注入，查询通常通过 `GetQueryableAsync()` 和 `AsyncExecuter`。 |
| Hibernate `@Table/@Column/@Index` | `PurchaseFlowDbContext.OnModelCreating` | 表名是 `AppPurchaseRequests`、`AppSupplierQuotes`、`AppReviewEvents`。 |
| `@Transactional` | ABP `UseUnitOfWork` 与 UOW 配置 | 本项目 `TransactionBehavior.Disabled`，不能把它当作默认数据库事务。 |
| Spring Security authority | `PurchaseFlowPermissions.Requests`、`.Edit`、`.Review` 和 `[Authorize]` | 页面 `CanEdit/CanReview` 控制显示，方法级授权和业务检查才是后端边界。 |
| `SecurityContext` 当前用户 | `CurrentUser`、`CurrentTenant` | `BuyerUserId` 限制编辑者；`TenantId` 与 ABP 数据过滤限制租户范围。 |
| JPA migration/Flyway | EF Core `Migrations` 与 DbMigrator | 迁移记录数据库结构版本，不等于领域业务规则。 |
| `@ControllerAdvice` 异常处理 | Razor `RunAsync` 捕获 `BusinessException`，用本地化错误码回显 | 直接 API 调用仍由应用服务/领域异常拒绝。 |

## 5. 核心按钮与类/方法对应表

| 用户动作 | 页面入口 | 应用服务与领域方法 | 结果/边界 |
| --- | --- | --- | --- |
| 首页 Dashboard | `Pages/Index.cshtml.cs` `IndexModel.OnGetAsync` | `GetDashboardAsync` | 统计当前租户需求、报价、待审核和已批准派生金额。 |
| 打开采购需求 | `Pages/Purchasing/Index.cshtml.cs` `OnGetAsync` | `GetListAsync(PurchaseRequestListInput)` | 关键词、状态、分页；页面权限要求 `Requests`。 |
| 创建需求 | `IndexModel.OnPostCreateAsync` | `CreateAsync` → `new PurchaseRequest` → `InsertAsync` | 初始 `Draft`；单号重复由 Application 查询和数据库索引共同防护。 |
| 修改需求 | `DetailsModel.OnPostUpdateAsync` | `UpdateAsync` → `SetDetails` | 仅创建者且 `Draft/Returned` 可修改。 |
| 添加报价 | `DetailsModel.OnPostAddQuoteAsync` | `AddQuoteAsync` → `SupplierQuote.AddLine` 逐项生成报价明细 → `InsertAsync` | 逐项报价单价不小于零且必须覆盖全部需求明细；合计由服务端按明细汇总；天数不小于零；同一需求供应商名忽略大小写不重复。 |
| 删除报价 | `OnPostDeleteQuoteAsync` | `DeleteQuoteAsync` → `ClearSelectedQuote` → `DeleteAsync` | 仅可编辑状态；删除选中项会先清除选择。 |
| 选择/更新理由 | `OnPostSelectAsync` | `SelectQuoteAsync` → `PurchaseRequest.SelectQuote` | Application 先确认报价属于需求，Domain 检查理由和可编辑状态。 |
| 提交审核 | `OnPostSubmitAsync` | `SubmitAsync` → `request.Submit` → `AddEventAsync("Submitted")` | 至少两条报价、有效选择、非空理由；进入 `PendingReview`。 |
| 批准需求 | `OnPostApproveAsync` | `ApproveAsync` → `request.Approve` → `AddEventAsync("Approved")` | 需要 `ReviewRequests`，仅 `PendingReview` 可批准。 |
| 退回修改 | `OnPostReturnAsync` | `ReturnAsync` → `request.Return` → `AddEventAsync("Returned")` | Application 和页面都要求意见；进入 `Returned`。 |
| 供应商档案 | `Pages/MasterData/Suppliers.cshtml.cs` | `ISupplierAppService.GetListAsync` / `CreateAsync` / `UpdateAsync` / `SetActiveAsync` | 查看需 `MasterData`；新建/编辑/启停用需 `ManageMasterData`；编码租户内唯一，停用后不能再被新报价引用。 |
| 物料档案 | `Pages/MasterData/Materials.cshtml.cs` | `IMaterialAppService` 同名方法 | 查看需 `MasterData`；维护需 `ManageMasterData`；编码租户内唯一、单位必填，停用后不能再被新需求明细引用。 |
| 查看决策简报 | `BriefModel.OnGetAsync` | `GetAsync` | 读取详情 DTO；页面的“打印简报”只调用 `window.print()`。 |
| 调整实验台权重 | `Details.cshtml` + `wwwroot/js/purchaseflow.js` | 浏览器端 `render()` | 只按当前 DOM 报价计算相对分数；不调用应用服务、不写库、不改变已选报价。 |

按钮是否显示由 `CanEdit`、`CanReview` 和当前状态决定，但隐藏按钮不是安全措施。后端 `[Authorize]`、`GetOwnedRequestAsync`、领域 `EnsureEditable/EnsurePendingReview` 仍会执行。

## 6. 分析器与决策实验台的准确读法

`QuoteDecisionAnalyzer.Analyze(saleAmount, selectedId, reason, quotes)` 只接收值并返回新的 DTO。它不读取数据库、不注入服务、不修改 `PurchaseRequest`，因此可单元测试。

它做的事情如下：

1. 统计报价数；最低采购金额和最快交期支持多个并列 ID。
2. 对当前选中报价计算 `预计毛利率 = (销售金额 - 选中报价) / 销售金额 × 100`，销售金额不大于零时不除零。
3. 生成三项检查：`NeedTwoQuotes`、`NeedSelectedQuote`、`NeedSelectionReason`。三项都完成才把 `IsReadyToSubmit` 设为 `true`。
4. 若存在另一报价金额不更高、交期不更慢且至少一项更优，则候选报价属于 `DominatedQuoteIds`。并列完全相同不互相标记为被支配。
5. 选中报价预计毛利为负，或选中报价价期均不占优，只加入警告；它们不会阻断提交，也不会替用户改选报价。
6. 需求明细汇总（预算基准）不大于零时追加 `BudgetNotConfigured` 警告，明确无法进行超预算判定，而不是静默跳过。

实验台的价格/交期相对评分在 `purchaseflow.js` 中计算。对报价集合分别取成本和交期的最小、最大值：

```text
成本分 = (最高采购成本 − 当前采购成本) / (最高采购成本 − 最低采购成本) × 100
交期分 = (最高交付天数 − 当前交付天数) / (最高交付天数 − 最低交付天数) × 100
```

当这一维所有值都相等时，该维得分为 100。若价格权重为 `P`、交期权重为 `D`，则 `P + D = 100`，总分为：

```text
总分 = 成本分 × P / 100 + 交期分 × D / 100
```

权重滑块默认 70/30，拖动时重新渲染排名并支持并列。它是“当前报价集合中的相对位置”，不是绝对质量评分；新增报价会改变 `min/max`，因此会改变其他报价的相对分数。实验台不写 `SelectedQuoteId`，不保存权重，也不生成 AI 建议。

## 7. 当前本机演示数据

本机数据库已准备好几条用于缩短现场录入时间的 SHOWCASE 数据，并保留原有用户需求。演示时直接打开这些新单号即可；若要新建数据，请使用新的单号，避免重复。

| 单号 | 当前状态 | 演示重点 |
| --- | --- | --- |
| `SHOWCASE-REVIEW-01` | 待审核 | 已有 3 条报价，已选择桂林供应商；演示审核视图和待审核队列，结束时仍保持待审核。 |
| `SHOWCASE-RISK-01` | 草稿 | 负预计毛利且价期均不占优，用于演示两类非阻断警示。 |
| `SHOWCASE-RETURN-01` | 已退回 | 用于演示退回状态、历史意见和可编辑边界。 |
| `DEMO-2026-001` | 已批准 | 用于演示批准后的冻结状态和历史记录。 |

这些记录是本机演示数据，不代表真实订单或公司制度。

主数据演示种子（`DemoMasterDataSeedContributor`，host-only、按编码幂等）：供应商 SUP-001 华东办公家具（A/30 天）、SUP-002 南方办公（B/15 天）、SUP-003 北方机电（B/45 天）；物料 MAT-001 办公椅（人体工学/把）、MAT-002 会议桌（2.4m/张）、MAT-003 深沟球轴承（6205-2RS/个）、MAT-004 骨架油封（NBR 20x3/个）。

## 8. 核心业务表

| 逻辑表/实体 | 关键字段 | 关系与用途 |
| --- | --- | --- |
| `AppPurchaseRequests` / `PurchaseRequest` | `TenantId`、`BuyerUserId`、`OrderReference`、`Description`、`SaleAmount`、`Status`、`SelectedQuoteId`、`SelectionReason` | 采购需求主记录。`TenantId + OrderReference` 唯一；宿主 `TenantId IS NULL` 有单独部分唯一索引。 |
| `AppSupplierQuotes` / `SupplierQuote` | `TenantId`、`PurchaseRequestId`、`SupplierName`、`Amount`、`DeliveryDays`、`Note` | 报价记录；`Amount` 由报价明细汇总并随明细变化重算（存储列，便于 Dashboard 只查报价表时取到金额）。通过 `PurchaseRequestId` 关联需求，外键级联删除。报价不是当前代码中的聚合内集合，而是独立 `Entity<Guid>`。 |
| `AppSupplierQuoteLines` / `SupplierQuoteLine` | `TenantId`、`SupplierQuoteId`、`PurchaseRequestLineId`、`ItemName`、`Specification`、`Quantity`、`UnitPrice` | 报价明细：按需求明细逐项记录成交单价，小计 = 数量 × 单价（2 位四舍五入）；`PurchaseRequestLineId` 只作需求行快照来源，不建外键。报价合计必须等于明细合计。 |
| `AppReviewEvents` / `ReviewEvent` | `TenantId`、`PurchaseRequestId`、`ActorUserId`、`Action`、`Comment`、`OccurredAtUtc` | 提交、退回、批准的可追溯事件；同样关联需求并级联删除，按发生时间索引。 |
| `AppSuppliers` / `Supplier` | `TenantId`、`Code`、`Name`、`ContactName`、`ContactPhone`、`PaymentTermsDays`、`Rating`、`IsActive`、`Note` | 供应商主数据；`(TenantId, Code)` 唯一，宿主另有过滤唯一索引；被报价/订单以可空 `SupplierId` 引用（不建外键），单据仍存名称快照。 |
| `AppMaterials` / `Material` | `TenantId`、`Code`、`Name`、`Specification`、`Unit`、`Category`、`IsActive`、`Note` | 物料主数据；`(TenantId, Code)` 唯一；被需求明细以可空 `MaterialId` 引用（不建外键），明细仍存名称/规格快照。 |

另有三列可空引用：`AppPurchaseRequestLines.MaterialId`、`AppSupplierQuotes.SupplierId`、`AppPurchaseOrders.SupplierId`。
核心业务表之外还有报价明细、采购订单、收货等表，以及 ABP Identity、权限、审计、租户、OpenIddict 等基础设施表；不要把教学切片的表说成完整采购系统的全部数据模型。

## 9. 权限、状态、租户和 UOW 边界

### 权限

- `PurchaseFlowPermissions.Requests`：查看工作台与需求。
- `PurchaseFlowPermissions.EditRequests`：创建、修改、报价增删、选择、提交。
- `PurchaseFlowPermissions.ReviewRequests`：批准和退回。
- `PurchaseFlowPermissions.MasterData`：查看供应商档案与物料档案（用于选择）；`MasterData.Manage`：新建、编辑、启停用档案。
- `buyer`、`reviewer`、`receiver` 角色由迁移器创建：业务角色都获得查看权限（含主数据查看），`admin` 额外拥有主数据维护权限。演示角色种子只在宿主上下文执行，演示用户是否创建取决于本机私密密码配置；管理员的实际权限以当前环境为准。

### 状态

```text
Draft ──提交（≥2报价+已选择+有理由）──> PendingReview ──批准──> Approved
                                             │
                                             └──退回（有意见）──> Returned ──补充后可再次提交──> PendingReview
```

`Draft`、`Returned` 可编辑；`PendingReview`、`Approved` 不可编辑。当前没有撤销批准用例。审核动作由权限和状态保护，不能仅通过改 URL 或隐藏页面按钮绕过。

### 租户

三个业务实体都实现 `IMultiTenant` 并保存 `TenantId`。创建需求、报价和审核事件时使用 `CurrentTenant.Id`；ABP 多租户数据过滤结合当前租户上下文限制常规查询。Dashboard 还在 Application 查询中显式按 `CurrentTenant.Id` 过滤并校验选中报价属于对应需求。正式系统仍需要多租户集成测试和安全评审，不能仅凭接口声明保证所有边界。

### UOW 与数据库

Web 管线使用 `UseUnitOfWork`，应用服务一次调用通常在同一个工作单元内完成查询与写入；但 `PurchaseFlowEntityFrameworkCoreModule` 明确把 `AbpUnitOfWorkDefaultOptions.TransactionBehavior` 设为 `Disabled`。因此“提交后写需求并写一条历史事件”不能宣称为生产级原子事务。SQLite 是本地教学选择，正式数据库、事务、并发编辑、重试和锁策略都需要重新验证。

## 10. 五个带验收结果的小练习

### 练习 1：从零跑通并核对 Dashboard

创建一条需求，录入两条报价并提交，再用审核角色批准；随后回首页查看统计。

验收结果：需求状态为 `Approved`；详情有 `Submitted` 和 `Approved` 历史；Dashboard 的待审核数量减少、已批准采购金额等于选中报价金额，预计毛利等于销售金额减选中报价。记录页面截图或测试输出，不把它写成生产报表。

### 练习 2：验证三项提交检查和状态冻结

分别测试“少于两条报价”“没有选择”“理由为空”，再测试批准后修改。

验收结果：三个缺口分别对应 `NeedTwoQuotes`、`NeedSelectedQuote`、`NeedSelectionReason`；批准后 `SetDetails`/`EnsureEditable` 抛 `NotEditable`。页面提示不是唯一证据，必须能指出领域方法。

### 练习 3：验证分析器是纯函数且警告不阻断

在 `QuoteDecisionAnalyzerTests` 的风格下准备两条报价，测试并列最低价/最快交期、负预计毛利和价期均不占优。

验收结果：分析结果列出并列 ID；负毛利与 Pareto 警告存在，但只要报价数、选择和理由齐全，`IsReadyToSubmit` 仍为 `true`；测试不需要数据库或登录。

### 练习 4：用实验台说明归一化和相对性

准备两条价期不同的报价，拖动 70/30 到 100/0、0/100，再添加一条极低价或极快交期的报价。

验收结果：排名随权重改变；同一维全相等时该维为 100；新增报价可能改变原有报价分数；已选标签、选择理由和数据库状态不变。浏览器刷新后权重回到默认值，说明它没有保存权重。

### 练习 5：打印 A4 简报并追代码

从详情打开“查看决策简报”，点击“打印简报”，再回到详情页，沿 `BriefModel.OnGetAsync → GetAsync → PurchaseRequestDetailDto` 追一次。

验收结果：打印预览只包含当前需求、报价比较、理由和审核历史；返回详情后数据不新增一条业务记录，已选报价不变。后续界面更新时，以实际页面为准复核按钮与布局。

## 11. 十个可能追问与诚实回答

1. **为什么用 ABP，而不是只写 ASP.NET Core？** ABP 提供模块依赖、应用服务、权限、租户、审计、仓储、UOW 和约定式 API，让学习者能看到分层骨架；这份原型并不证明它已经满足生产系统的全部非功能要求。
2. **为什么提交规则在 Domain，报价归属检查在 Application？** `PurchaseRequest.Submit` 只需要报价数量、选中 ID 和理由等状态不变式；“这个 ID 是否属于这条需求”需要先查询报价集合，当前代码由 Application 完成，再调用 Domain。
3. **分析器为什么不直接自动选最低报价？** 价格和交期只是演示中的两个维度，质量、服务、合同和风险没有建模。分析器提供可解释比较与警告，保留人工选择和理由。
4. **价期均不占优为什么只是警告？** 这只是当前价格/交期二维集合中的 Pareto 事实，不包含质量服务；即使被提示，业务也可能有合法理由选择它，所以当前不阻断提交。
5. **实验台为什么不会改变已选方案？** 它只读取详情页渲染出的报价数据，在浏览器中计算临时排名，没有 POST handler、应用服务调用或数据库写入；已选方案仍由 `SelectedQuoteId` 控制。
6. **预计毛利是不是利润？** 不是。当前定义仅为销售金额减选中采购报价，没有税费、运费、人工、售后、汇率或其他成本，所以只能称预计毛利/毛利率展示。
7. **如何保证一个采购员不能改别人的需求？** 编辑用例先调用 `GetOwnedRequestAsync`，比较实体 `BuyerUserId` 与当前 `UserId()`；页面 `CanEdit` 只负责显示体验，后端检查仍会执行。
8. **租户隔离和权限是不是一回事？** 不是。权限回答“这个用户能做什么”，`TenantId`/当前租户和 ABP 数据过滤回答“这个用户在哪个租户数据范围内”。正式系统还要补充集成测试和安全评审。
9. **这里有事务吗？** 有 Web UOW 管线，但当前 EF Core 模块把默认事务行为设为 Disabled，因此不能宣称跨需求更新和历史事件插入具备生产级原子性。正式数据库上线前必须重新验证。
10. **这是你们公司的采购方案吗？AI 做了什么？** 不是，这是一个模拟采购业务切片，真实公司流程、金额审批、供应商制度和数据库要求都未知。项目的页面、后端及测试借助 AI 生成和迭代；我正在按代码链路学习和验证，完成亲手练习后再说明哪些部分已经能够独立解释和修改，不把工具辅助说成全部由我独立完成，也不把 AI 生成的内容包装成真实业务制度或录用保证。

## 12. 复现与交付检查

- 当前记录的浏览器验收覆盖 1440、1280、768、390、360 宽度；匿名首页/简报权限、实验台 0/100/reset 无网络写入、警示和 A4 简报均已通过。这是已记录场景的结果，不等于所有 UI 或业务场景全覆盖。
- 当前完整 `dotnet test PurchaseFlow.slnx` 记录为 112 项通过：Domain 43、Application 11、EF Core 50、Web 8。复现时仍应按本机环境重新执行并保留输出。
- 最终演示应使用现场真实可登录的角色权限；不要在文档、截图或消息中暴露私密凭据。
- GitHub 仓库链接仍待补充；正式数据库、事务/并发方案和真实业务确认仍待交付。

