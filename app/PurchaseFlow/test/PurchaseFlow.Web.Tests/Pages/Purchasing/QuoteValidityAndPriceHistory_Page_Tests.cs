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
/// 第二轮第 3 批：报价有效期 + 历史采购价前端页面测试。
/// 覆盖：报价表单有效期输入与展示、“已过期”标签且不能选择、报价录入行显示“上次采购价”。
/// 过期报价与历史订单按规格要求用领域/仓储预置数据（应用层会拒绝新增过期报价），
/// 页面断言仍走真实 GET/POST 流程，不绕过业务规则。
/// </summary>
public class QuoteValidityAndPriceHistory_Page_Tests : PurchaseFlowWebTestBase
{
    private readonly HttpClient _http;

    public QuoteValidityAndPriceHistory_Page_Tests()
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
    public async Task Quote_form_accepts_validity_and_shows_valid_until_or_long_term()
    {
        await SignInAsync();
        var (requestId, _) = await CreateRequestAsync();

        var details = await GetHtmlAsync($"/Purchasing/Details/{requestId}");
        // 报价表单必须有可选的 date 类型有效期输入（name=validUntil）。
        var validityInput = GetInputNode(details, "validUntil");
        validityInput.ShouldNotBeNull("the add-quote form has no validUntil input.");
        validityInput!.Attributes["type"]?.Value.ShouldBe("date");

        var requestLineId = GetInputValues(details, "QuoteLines[0].RequestLineId").First();
        var validUntil = DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd");

        // 1) 带有效期的报价：页面应显示“有效至 yyyy-MM-dd”。
        await PostFormAsync($"/Purchasing/Details/{requestId}?handler=AddQuote", details, QuoteFields(
            requestLineId, "有效期供应商", 100m, validUntil));
        var afterValid = await GetHtmlAsync($"/Purchasing/Details/{requestId}");
        GetText(afterValid).ShouldContain($"有效至 {validUntil}");

        // 2) 不填有效期的报价：页面应显示“长期有效”。
        details = afterValid;
        await PostFormAsync($"/Purchasing/Details/{requestId}?handler=AddQuote", details, QuoteFields(
            requestLineId, "长期供应商", 110m, validUntil: null));
        var afterLongTerm = await GetHtmlAsync($"/Purchasing/Details/{requestId}");
        var text = GetText(afterLongTerm);
        text.ShouldContain("长期有效");
        text.ShouldContain($"有效至 {validUntil}");
        text.ShouldNotContain("已过期");
    }

    [Fact]
    public async Task Expired_quote_is_marked_and_cannot_be_selected()
    {
        await SignInAsync();
        var (requestId, _) = await CreateRequestAsync();

        var details = await GetHtmlAsync($"/Purchasing/Details/{requestId}");
        var requestLineId = GetInputValues(details, "QuoteLines[0].RequestLineId").First();

        // 应用层会拒绝“新增已过期报价”，因此按规格用领域/仓储预置一条昨天到期的报价（页面只做展示断言）。
        await PreseedExpiredQuoteAsync(requestId, requestLineId);

        var expiredPage = await GetHtmlAsync($"/Purchasing/Details/{requestId}");
        var text = GetText(expiredPage);
        text.ShouldContain("已过期");
        text.ShouldContain("已过期，不能选择");
        // 过期报价不渲染“选择”表单（无 reason 输入），但删除按钮仍保留。
        GetInputNode(expiredPage, "reason").ShouldBeNull();
        GetInputValues(expiredPage, "quoteId").Count.ShouldBe(1);
        GetText(expiredPage).ShouldContain("删除报价");
    }

    [Fact]
    public async Task Quote_entry_shows_last_purchase_price_from_a_previous_order()
    {
        await SignInAsync();

        // 先建物料档案，再从档案生成历史订单，最后在另一张需求上验证“上次采购价”参考。
        var materialCode = "MAT-HIST-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var materialName = "历史价物料";
        await CreateMaterialAsync(materialCode, materialName);
        var materialId = GetRowInputValue(await GetHtmlAsync("/MasterData/Materials"), materialCode, "id");

        // 第一张需求：带物料档案，2 × 100；页面此时应显示无历史价空态。
        var (firstRequestId, firstReference) = await CreateRequestAsync(materialId: materialId, quantity: "2", unitPrice: "100");
        var firstDetails = await GetHtmlAsync($"/Purchasing/Details/{firstRequestId}");
        GetText(firstDetails).ShouldContain("暂无历史采购价");

        var firstLineId = GetInputValues(firstDetails, "QuoteLines[0].RequestLineId").First();
        await AddQuoteAsync(firstRequestId, firstLineId, "历史供应商A", 100m);
        var afterFirstQuote = await GetHtmlAsync($"/Purchasing/Details/{firstRequestId}");
        await AddQuoteAsync(firstRequestId, firstLineId, "历史供应商B", 105m, pageHtml: afterFirstQuote);

        // 选定并走完低金额审批 → 生成采购订单（订单号 = PO- + 销售单号）。
        var selectPage = await GetHtmlAsync($"/Purchasing/Details/{firstRequestId}");
        var quoteId = GetInputValues(selectPage, "quoteId").First();
        await PostFormAsync($"/Purchasing/Details/{firstRequestId}?handler=Select", selectPage, new Dictionary<string, string>
        {
            ["quoteId"] = quoteId,
            ["reason"] = "历史价测试选择"
        });

        var submitPage = await GetHtmlAsync($"/Purchasing/Details/{firstRequestId}");
        await PostFormAsync($"/Purchasing/Details/{firstRequestId}?handler=Submit", submitPage, new Dictionary<string, string>());

        var approvePage = await GetHtmlAsync($"/Purchasing/Details/{firstRequestId}");
        await PostFormAsync($"/Purchasing/Details/{firstRequestId}?handler=Approve", approvePage,
            new Dictionary<string, string> { ["comment"] = "低金额一级批准（历史价测试）" });

        // 第二张需求：同一物料档案，报价录入行应显示上次采购价、订单号与供应商。
        var (secondRequestId, _) = await CreateRequestAsync(materialId: materialId, quantity: "1", unitPrice: "100");
        var secondDetails = await GetHtmlAsync($"/Purchasing/Details/{secondRequestId}");
        var secondText = GetText(secondDetails);
        secondText.ShouldContain("上次采购价");
        secondText.ShouldContain("¥100.00");
        secondText.ShouldContain($"PO-{firstReference}");
        secondText.ShouldContain("历史供应商A");
    }

    private static Dictionary<string, string> QuoteFields(string requestLineId, string supplierName, decimal unitPrice,
        string? validUntil)
    {
        var fields = new Dictionary<string, string>
        {
            ["supplierName"] = supplierName,
            ["deliveryDays"] = "5",
            ["note"] = "有效期/历史价测试",
            ["QuoteLines[0].RequestLineId"] = requestLineId,
            ["QuoteLines[0].UnitPrice"] = unitPrice.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        if (!string.IsNullOrEmpty(validUntil))
        {
            fields["validUntil"] = validUntil;
        }

        return fields;
    }

    /// <summary>通过页面创建采购需求；可选关联物料档案。返回 (需求 Id, 销售单号)。</summary>
    private async Task<(string RequestId, string OrderReference)> CreateRequestAsync(
        string? materialId = null, string quantity = "1", string unitPrice = "100")
    {
        var orderReference = "SO-WEB-VAL-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var fields = new Dictionary<string, string>
        {
            ["OrderReference"] = orderReference,
            ["Description"] = "有效期与历史价页面测试",
            ["SaleAmount"] = "1000",
            ["Lines[0].ItemName"] = "占位物料",
            ["Lines[0].Specification"] = "占位规格",
            ["Lines[0].Quantity"] = quantity,
            ["Lines[0].UnitPrice"] = unitPrice
        };
        if (!string.IsNullOrEmpty(materialId))
        {
            fields["Lines[0].MaterialId"] = materialId;
        }

        var createPage = await GetHtmlAsync("/Purchasing/Create");
        var response = await PostFormResponseAsync("/Purchasing/Create", createPage, fields);
        return (FollowRedirectAndGetRequestId(response), orderReference);
    }

    private async Task AddQuoteAsync(string requestId, string requestLineId, string supplierName, decimal unitPrice,
        string? pageHtml = null)
    {
        var page = pageHtml ?? await GetHtmlAsync($"/Purchasing/Details/{requestId}");
        await PostFormAsync($"/Purchasing/Details/{requestId}?handler=AddQuote", page,
            QuoteFields(requestLineId, supplierName, unitPrice, validUntil: null));
    }

    private async Task CreateMaterialAsync(string code, string name)
    {
        var page = await GetHtmlAsync("/MasterData/Materials");
        await PostFormAsync("/MasterData/Materials?handler=Save", page, new Dictionary<string, string>
        {
            ["Code"] = code,
            ["Name"] = name,
            ["Specification"] = "历史价测试规格",
            ["Unit"] = "个",
            ["Category"] = "测试分类"
        });
    }

    /// <summary>
    /// 预置一条昨天到期的报价：用领域仓储加载完整聚合（Amount 由明细汇总）后调用领域 AddQuote。
    /// 领域层允许导入历史有效期，应用层才拦截“新增已过期报价”，因此这里走领域是符合规格的预置方式。
    /// </summary>
    private async Task PreseedExpiredQuoteAsync(string requestId, string requestLineId)
    {
        using var scope = Server.Services.CreateScope();
        var uowManager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        using var uow = uowManager.Begin(requiresNew: true);
        var repository = scope.ServiceProvider.GetRequiredService<IPurchaseRequestRepository>();
        var request = await repository.FindWithDetailsAsync(Guid.Parse(requestId));
        request.AddQuote(
            Guid.NewGuid(),
            "过期供应商",
            5,
            null,
            new[] { (Guid.NewGuid(), Guid.Parse(requestLineId), 100m) },
            supplierId: null,
            validUntil: DateTime.UtcNow.AddDays(-1));
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

    private static HtmlNode? GetInputNode(string html, string name)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        return document.DocumentNode.SelectSingleNode($"//input[@name='{name}']");
    }

    /// <summary>读取“包含指定编码/名称的行”里某个隐藏输入的值（例如档案 Id）。</summary>
    private static string GetRowInputValue(string html, string cellText, string inputName)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var row = document.DocumentNode.SelectSingleNode($"//tr[.//*[normalize-space()='{cellText}']]");
        row.ShouldNotBeNull($"no table row containing '{cellText}'.");
        var input = row!.SelectSingleNode($".//input[@name='{inputName}']");
        input.ShouldNotBeNull($"no input named '{inputName}' in the row containing '{cellText}'.");
        return input!.Attributes["value"].Value;
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

    private static string GetText(string html)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        return WebUtility.HtmlDecode(document.DocumentNode.InnerText);
    }
}
