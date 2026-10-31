using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Microsoft.Extensions.DependencyInjection;
using PurchaseFlow.Purchasing;
using Shouldly;
using Volo.Abp.Uow;
using Xunit;

namespace PurchaseFlow.Pages.Purchasing;

/// <summary>
/// 第二轮第 2 批：按金额分级审批的页面级测试。
/// 用真实表单 POST 走完“创建高金额需求 → 提交 → 一级批准 → 二级待审”，
/// 验证：两级提示、一级批准后不生成订单、二级审批面板/按钮、同人不能二级（领域硬规则）。
/// 说明：本测试宿主对所有 HTTP 请求都固定为同一个管理员身份（与既有页面测试一致），
/// 无法通过登录切换两个账号；因此“另一位审批人”用领域方法预置一级状态（FirstApproverUserId
/// 设为另一个 Guid），二级批准仍然走真实页面 handler + 应用服务 + 领域校验，不绕过业务规则。
/// </summary>
public class TwoLevelApproval_Page_Tests : PurchaseFlowWebTestBase
{
    private readonly HttpClient _http;

    public TwoLevelApproval_Page_Tests()
    {
        // 与既有页面测试一致：CookieContainer 让登录 Cookie 与防伪 Cookie 在会话内往返。
        _http = NewClient();
    }

    private HttpClient NewClient() => new(new CookieHandler(Server.CreateHandler()))
    {
        BaseAddress = Client.BaseAddress
    };

    private sealed class CookieHandler : DelegatingHandler
    {
        private readonly CookieContainer _cookies = new();

        public CookieHandler(HttpMessageHandler innerHandler)
            : base(innerHandler)
        {
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            var cookieHeader = _cookies.GetCookieHeader(uri);
            if (!string.IsNullOrEmpty(cookieHeader))
            {
                request.Headers.Remove("Cookie");
                request.Headers.Add("Cookie", cookieHeader);
            }

            var response = await base.SendAsync(request, cancellationToken);
            if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
            {
                foreach (var setCookie in setCookies)
                {
                    try
                    {
                        _cookies.SetCookies(uri, setCookie);
                    }
                    catch (CookieException)
                    {
                        // 容器拒绝的 Cookie 不应让测试失败。
                    }
                }
            }

            return response;
        }
    }

    [Fact]
    public async Task High_value_request_shows_two_level_hint_before_approval()
    {
        await SignInAsync();

        var requestId = await CreateHighValueRequestAsync();
        var details = await GetHtmlAsync($"/Purchasing/Details/{requestId}");
        var text = GetText(details);

        // 头部/决策概览/流程都要提示“需两级审批”，并显示一级批准按钮。
        text.ShouldContain("需两级审批");
        text.ShouldContain("¥50,000");
        text.ShouldContain("一级审核");
        text.ShouldContain("一级批准");
        text.ShouldContain("待审核");
    }

    [Fact]
    public async Task First_level_approval_moves_high_value_request_to_second_approval_without_order()
    {
        await SignInAsync();
        var requestId = await CreateHighValueRequestAsync();

        var details = await GetHtmlAsync($"/Purchasing/Details/{requestId}");
        var approveResult = await PostFormAsync($"/Purchasing/Details/{requestId}?handler=Approve", details,
            new Dictionary<string, string> { ["comment"] = "一级批准（页面测试）" });
        GetAlert(approveResult).ShouldBeNullOrWhiteSpace();

        var afterFirst = await GetHtmlAsync($"/Purchasing/Details/{requestId}");
        var text = GetText(afterFirst);
        text.ShouldContain("二级待审");
        text.ShouldContain("二级审批");
        text.ShouldContain("一级审批人");
        text.ShouldContain("一级批准");
        // 一级批准后不能直接生成订单：订单区块及其“订单金额”指标不应出现。
        text.ShouldNotContain("订单金额");

        // 列表状态入口与计数：按二级待审筛选应能看到该需求。
        var filtered = await GetHtmlAsync("/Purchasing/Index?status=PendingSecondApproval");
        var filteredText = GetText(filtered);
        filteredText.ShouldContain("二级待审");
        filteredText.ShouldContain("SO-WEB-HV-");

        // 仪表盘展示“等待二级审批”数量与流程分布入口。
        var dashboard = await GetHtmlAsync("/");
        var dashboardText = GetText(dashboard);
        dashboardText.ShouldContain("等待二级审批");
        dashboardText.ShouldContain("二级待审");
    }

    [Fact]
    public async Task Same_user_cannot_perform_second_level_approval()
    {
        await SignInAsync();
        var requestId = await CreateHighValueRequestAsync();
        await ApproveLevelOneAsync(requestId);

        // 一级审批人（当前登录用户）再次点击二级批准：页面允许提交，
        // 但领域层必须拒绝“同人两级”，并显示错误、状态保持二级待审。
        var details = await GetHtmlAsync($"/Purchasing/Details/{requestId}");
        var result = await PostFormAsync($"/Purchasing/Details/{requestId}?handler=ApproveSecondLevel", details,
            new Dictionary<string, string> { ["comment"] = "同人二级审批（应被拒绝）" });

        GetAlert(result).ShouldContain("different user");
        var text = GetText(result);
        text.ShouldContain("二级待审");
        text.ShouldNotContain("订单金额");
    }

    [Fact]
    public async Task Second_approval_page_completes_and_creates_the_order()
    {
        await SignInAsync();
        var requestId = await CreateHighValueRequestAsync();

        // 预置一级审批状态：把一级审批人设为一个不同于当前 HTTP 用户的 Guid，
        // 模拟“另一位审核员已完成一级批准”。二级批准仍走真实页面 handler 与领域校验。
        await PreseedFirstLevelApprovalAsync(requestId);

        var details = await GetHtmlAsync($"/Purchasing/Details/{requestId}");
        var detailsText = GetText(details);
        detailsText.ShouldContain("二级审批");
        detailsText.ShouldContain("二级批准");
        detailsText.ShouldContain("必须由另一位");

        var approved = await PostFormAsync($"/Purchasing/Details/{requestId}?handler=ApproveSecondLevel", details,
            new Dictionary<string, string> { ["comment"] = "二级批准（页面测试）" });

        var finalText = GetText(approved);
        finalText.ShouldContain("已批准");
        finalText.ShouldContain("二级批准");
        // 二级批准后才生成采购订单：订单区块与订单金额应出现（2 × 30,000 = 60,000）。
        finalText.ShouldContain("采购订单");
        finalText.ShouldContain("订单金额");
        finalText.ShouldContain("¥60,000.00");
    }

    /// <summary>创建一张高金额需求（2 × 30,000 = 60,000 &gt; 50,000），录入两家报价、选定并提交。</summary>
    private async Task<string> CreateHighValueRequestAsync()
    {
        var createPage = await GetHtmlAsync("/Purchasing/Create");
        var response = await PostFormResponseAsync("/Purchasing/Create", createPage, new Dictionary<string, string>
        {
            ["OrderReference"] = "SO-WEB-HV-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
            ["Description"] = "高金额两级审批页面测试",
            ["SaleAmount"] = "120000",
            ["Lines[0].ItemName"] = "整批设备",
            ["Lines[0].Specification"] = "高金额测试",
            ["Lines[0].Quantity"] = "2",
            ["Lines[0].UnitPrice"] = "30000"
        });

        var requestId = FollowRedirectAndGetRequestId(response);

        // 提交审核要求至少两家报价（既有业务规则），所以这里录入两家的逐项报价单价。
        var details = await GetHtmlAsync($"/Purchasing/Details/{requestId}");
        var requestLineId = GetInputValues(details, "QuoteLines[0].RequestLineId").First();
        await PostFormAsync($"/Purchasing/Details/{requestId}?handler=AddQuote", details, new Dictionary<string, string>
        {
            ["supplierName"] = "高金额供应商A",
            ["deliveryDays"] = "10",
            ["note"] = "两级审批测试 A",
            ["QuoteLines[0].RequestLineId"] = requestLineId,
            ["QuoteLines[0].UnitPrice"] = "30000"
        });

        details = await GetHtmlAsync($"/Purchasing/Details/{requestId}");
        await PostFormAsync($"/Purchasing/Details/{requestId}?handler=AddQuote", details, new Dictionary<string, string>
        {
            ["supplierName"] = "高金额供应商B",
            ["deliveryDays"] = "12",
            ["note"] = "两级审批测试 B",
            ["QuoteLines[0].RequestLineId"] = requestLineId,
            ["QuoteLines[0].UnitPrice"] = "31000"
        });

        details = await GetHtmlAsync($"/Purchasing/Details/{requestId}");
        var quoteId = GetInputValues(details, "quoteId").First();
        await PostFormAsync($"/Purchasing/Details/{requestId}?handler=Select", details, new Dictionary<string, string>
        {
            ["quoteId"] = quoteId,
            ["reason"] = "高金额需要两级审批"
        });

        details = await GetHtmlAsync($"/Purchasing/Details/{requestId}");
        await PostFormAsync($"/Purchasing/Details/{requestId}?handler=Submit", details, new Dictionary<string, string>());
        return requestId;
    }

    private async Task ApproveLevelOneAsync(string requestId)
    {
        var details = await GetHtmlAsync($"/Purchasing/Details/{requestId}");
        await PostFormAsync($"/Purchasing/Details/{requestId}?handler=Approve", details,
            new Dictionary<string, string> { ["comment"] = "一级批准（页面测试）" });
    }

    /// <summary>
    /// 预置“另一位审核员已一级批准”的状态：用领域仓储加载带明细的完整聚合后调用领域方法并保存。
    /// 必须用 FindWithDetailsAsync（Amount 由明细实时汇总），否则聚合金额为 0 会被误判为低金额直接批准。
    /// 这符合规格允许的“用领域/应用服务预置状态再做页面断言”，第二级仍由真实页面流程校验换人规则。
    /// </summary>
    private async Task PreseedFirstLevelApprovalAsync(string requestId)
    {
        using var scope = Server.Services.CreateScope();
        var uowManager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        using var uow = uowManager.Begin(requiresNew: true);
        var repository = scope.ServiceProvider.GetRequiredService<IPurchaseRequestRepository>();
        var request = await repository.FindWithDetailsAsync(Guid.Parse(requestId));
        request.ApproveFirstLevel(Guid.NewGuid());
        await repository.UpdateAsync(request);
        await uow.CompleteAsync();
    }

    private async Task SignInAsync()
    {
        var loginPage = await GetHtmlAsync("/Account/Login");
        var result = await PostFormAsync("/Account/Login", loginPage, new Dictionary<string, string>
        {
            ["LoginInput.UserNameOrEmailAddress"] = PurchaseFlowWebTestModule.TestUserName,
            ["LoginInput.Password"] = PurchaseFlowWebTestModule.TestUserPassword
        });

        if (!result.Contains("Account/Logout"))
        {
            throw new InvalidOperationException($"The sign in did not succeed. length={result.Length}.");
        }
    }

    private async Task<string> GetHtmlAsync(string url)
    {
        var response = await _http.GetAsync(url);
        response.StatusCode.ShouldBe(HttpStatusCode.OK,
            $"GET {url} answered {(int)response.StatusCode} {response.StatusCode}.");
        return await response.Content.ReadAsStringAsync();
    }

    private async Task<string> PostFormAsync(string url, string pageHtml, IDictionary<string, string> fields)
    {
        var response = await PostFormResponseAsync(url, pageHtml, fields);
        if (IsRedirect(response))
        {
            var target = ResolveLocation(response);
            var followed = await _http.GetAsync(target);
            followed.StatusCode.ShouldBe(HttpStatusCode.OK);
            return await followed.Content.ReadAsStringAsync();
        }

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync();
    }

    private async Task<HttpResponseMessage> PostFormResponseAsync(string url, string pageHtml, IDictionary<string, string> fields)
    {
        var payload = new Dictionary<string, string>(fields)
        {
            ["__RequestVerificationToken"] = GetAntiforgeryToken(pageHtml)
        };
        return await _http.PostAsync(url, new FormUrlEncodedContent(payload));
    }

    private string FollowRedirectAndGetRequestId(HttpResponseMessage response)
    {
        IsRedirect(response).ShouldBeTrue(
            $"expected a redirect to the created request, got {(int)response.StatusCode} {response.StatusCode}.");
        var target = ResolveLocation(response);
        target.ShouldContain("/Purchasing/Details/");
        return target.Split('/').Last();
    }

    private string ResolveLocation(HttpResponseMessage response)
    {
        var location = response.Headers.Location;
        location.ShouldNotBeNull();
        return location!.IsAbsoluteUri
            ? location.ToString()
            : new Uri(_http.BaseAddress!, location).ToString();
    }

    private static bool IsRedirect(HttpResponseMessage response) =>
        response.StatusCode is HttpStatusCode.Found
            or HttpStatusCode.MovedPermanently
            or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect;

    private static string GetAntiforgeryToken(string html)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var node = document.DocumentNode.SelectSingleNode("//input[@name='__RequestVerificationToken']");
        node.ShouldNotBeNull("the page has no antiforgery token.");
        return node!.Attributes["value"].Value;
    }

    private static List<string> GetInputValues(string html, string name)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        return document.DocumentNode
            .SelectNodes($"//input[@name='{name}']")
            ?.Select(node => node.Attributes["value"].Value)
            .ToList() ?? new List<string>();
    }

    private static string GetAlert(string html)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var node = document.DocumentNode.SelectSingleNode("//div[contains(@class, 'alert-danger')]");
        return node == null ? string.Empty : WebUtility.HtmlDecode(node.InnerText).Trim();
    }

    private static string GetText(string html)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        return WebUtility.HtmlDecode(document.DocumentNode.InnerText);
    }
}
