using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Shouldly;
using Xunit;

namespace PurchaseFlow.Pages.Finance;

/// <summary>
/// 第三轮：三单匹配 + 付款申请闭环的页面测试。
/// 用真实页面流程预置“需求 → 报价 → 审批 → 收货 → 订单”，再验证发票登记/匹配拒绝/
/// 付款申请 → 批准 → 登记付款 → 已付款，以及发票总览页。
/// 不绕过业务规则：单价/数量匹配与状态流转都由应用服务/领域校验，测试只走页面提交。
/// </summary>
public class InvoicePayment_Page_Tests : PurchaseFlowWebTestBase
{
    private readonly HttpClient _http;

    public InvoicePayment_Page_Tests()
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
    public async Task Received_order_shows_invoice_form_and_registers_a_matched_invoice()
    {
        await SignInAsync();
        var (requestId, _, orderLineId) = await CreateOrderWithReceiptAsync();
        var invoiceNumber = "INV-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        var details = await GetHtmlAsync($"/Purchasing/Details/{requestId}");
        var detailsText = GetText(details);
        detailsText.ShouldContain("发票与付款");
        detailsText.ShouldContain("登记发票（三单匹配）");
        GetInputNode(details, "InvoiceNumber").ShouldNotBeNull();
        GetInputNode(details, $"InvoiceLines[0].PurchaseOrderLineId")!.Attributes["value"].Value.ShouldBe(orderLineId);

        await RegisterInvoiceAsync(requestId, details, invoiceNumber, orderLineId, unitPrice: 100m, quantity: 2m);

        var afterRegister = await GetHtmlAsync($"/Purchasing/Details/{requestId}");
        var afterText = GetText(afterRegister);
        afterText.ShouldContain(invoiceNumber);
        afterText.ShouldContain("已匹配");
        afterText.ShouldContain("¥200.00");
        afterText.ShouldContain("未申请付款");
    }

    [Fact]
    public async Task Invoice_with_wrong_price_or_too_much_quantity_is_rejected()
    {
        await SignInAsync();
        var (requestId, _, orderLineId) = await CreateOrderWithReceiptAsync();

        var details = await GetHtmlAsync($"/Purchasing/Details/{requestId}");

        // 1) 单价与订单成交价不一致：必须拒绝，不能落库。
        var wrongPriceNumber = "INV-WRONG-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var wrongPriceResult = await RegisterInvoiceAsync(requestId, details, wrongPriceNumber, orderLineId,
            unitPrice: 99m, quantity: 2m);
        GetAlert(wrongPriceResult).ShouldNotBeNullOrWhiteSpace();

        // 2) 数量超过已收货数量（已收 2）：同样拒绝。
        var tooMuchNumber = "INV-OVER-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var tooMuchPage = await GetHtmlAsync($"/Purchasing/Details/{requestId}");
        var tooMuchResult = await RegisterInvoiceAsync(requestId, tooMuchPage, tooMuchNumber, orderLineId,
            unitPrice: 100m, quantity: 5m);
        GetAlert(tooMuchResult).ShouldNotBeNullOrWhiteSpace();

        var finalPage = await GetHtmlAsync($"/Purchasing/Details/{requestId}");
        var text = GetText(finalPage);
        text.ShouldContain("还没有登记发票");
        text.ShouldNotContain("已匹配");
    }

    [Fact]
    public async Task Invoice_payment_can_be_requested_approved_and_registered()
    {
        await SignInAsync();
        var (requestId, _, orderLineId) = await CreateOrderWithReceiptAsync();
        var invoiceNumber = "INV-PAY-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        var details = await GetHtmlAsync($"/Purchasing/Details/{requestId}");
        await RegisterInvoiceAsync(requestId, details, invoiceNumber, orderLineId, unitPrice: 100m, quantity: 2m);

        // 申请付款（金额留空 = 发票全额 200）。
        var afterInvoice = await GetHtmlAsync($"/Purchasing/Details/{requestId}");
        var invoiceId = GetInputValues(afterInvoice, "invoiceId").First();
        var afterRequest = await PostFormAsync($"/Purchasing/Details/{requestId}?handler=RequestPayment", afterInvoice,
            new Dictionary<string, string>
            {
                ["invoiceId"] = invoiceId,
                ["amount"] = string.Empty
            });
        GetAlert(afterRequest).ShouldBeNullOrWhiteSpace();
        var requestText = GetText(afterRequest);
        requestText.ShouldContain("已申请付款");
        requestText.ShouldContain("等待批准");

        // 管理员批准付款。
        var paymentId = GetInputValues(afterRequest, "paymentId").First();
        var afterApprove = await PostFormAsync($"/Purchasing/Details/{requestId}?handler=ApprovePayment", afterRequest,
            new Dictionary<string, string>
            {
                ["paymentId"] = paymentId,
                ["comment"] = "页面测试批准付款"
            });
        GetAlert(afterApprove).ShouldBeNullOrWhiteSpace();
        GetText(afterApprove).ShouldContain("已批准待付款");

        // 管理员登记付款（默认今天），发票同步为已付款。
        var afterPay = await PostFormAsync($"/Purchasing/Details/{requestId}?handler=RegisterPayment", afterApprove,
            new Dictionary<string, string>
            {
                ["paymentId"] = paymentId,
                ["paymentDate"] = DateTime.Today.ToString("yyyy-MM-dd")
            });
        GetAlert(afterPay).ShouldBeNullOrWhiteSpace();
        var paidText = GetText(afterPay);
        paidText.ShouldContain("已付款");
        paidText.ShouldContain(invoiceNumber);

        // 总览页同步显示该发票为已付款。
        var overview = await GetHtmlAsync("/Finance/Invoices");
        var overviewText = GetText(overview);
        overviewText.ShouldContain(invoiceNumber);
        overviewText.ShouldContain("已付款");
    }

    [Fact]
    public async Task Invoice_overview_page_lists_invoices_with_status()
    {
        await SignInAsync();
        var (requestId, _, orderLineId) = await CreateOrderWithReceiptAsync();
        var invoiceNumber = "INV-LIST-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        var details = await GetHtmlAsync($"/Purchasing/Details/{requestId}");
        await RegisterInvoiceAsync(requestId, details, invoiceNumber, orderLineId, unitPrice: 100m, quantity: 2m);

        var overview = await GetHtmlAsync("/Finance/Invoices");
        var text = GetText(overview);
        text.ShouldContain("发票总览");
        text.ShouldContain(invoiceNumber);
        text.ShouldContain("已匹配");
        text.ShouldContain("未申请付款");

        // 状态筛选：已匹配能命中，已付款不应命中这张未付款发票。
        var matched = GetText(await GetHtmlAsync("/Finance/Invoices?status=Matched"));
        matched.ShouldContain(invoiceNumber);
        var paidFilter = GetText(await GetHtmlAsync("/Finance/Invoices?status=Paid"));
        paidFilter.ShouldNotContain(invoiceNumber);
    }

    /// <summary>
    /// 用真实页面流程创建一张“已审批 + 已全额收货”的低金额订单，返回 (需求 Id, 销售单号, 订单行 Id)。
    /// 订单/收货都是正常业务动作，发票测试只在此基础上做匹配。
    /// </summary>
    private async Task<(string RequestId, string OrderReference, string OrderLineId)> CreateOrderWithReceiptAsync()
    {
        var orderReference = "SO-WEB-INV-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var createPage = await GetHtmlAsync("/Purchasing/Create");
        var createResponse = await PostFormResponseAsync("/Purchasing/Create", createPage, new Dictionary<string, string>
        {
            ["OrderReference"] = orderReference,
            ["Description"] = "发票付款页面测试",
            ["SaleAmount"] = "1000",
            ["Lines[0].ItemName"] = "开票测试物料",
            ["Lines[0].Specification"] = "标准",
            ["Lines[0].Quantity"] = "2",
            ["Lines[0].UnitPrice"] = "100"
        });
        var requestId = FollowRedirectAndGetRequestId(createResponse);

        // 两家报价（提交审核要求至少两家），单价 100 便于与订单单价核对。
        var details = await GetHtmlAsync($"/Purchasing/Details/{requestId}");
        var requestLineId = GetInputValues(details, "QuoteLines[0].RequestLineId").First();
        await AddQuoteAsync(requestId, details, requestLineId, "发票供应商A", 100m);
        details = await GetHtmlAsync($"/Purchasing/Details/{requestId}");
        await AddQuoteAsync(requestId, details, requestLineId, "发票供应商B", 105m);

        details = await GetHtmlAsync($"/Purchasing/Details/{requestId}");
        var quoteId = GetInputValues(details, "quoteId").First();
        await PostFormAsync($"/Purchasing/Details/{requestId}?handler=Select", details, new Dictionary<string, string>
        {
            ["quoteId"] = quoteId,
            ["reason"] = "发票测试选择供应商A"
        });

        details = await GetHtmlAsync($"/Purchasing/Details/{requestId}");
        await PostFormAsync($"/Purchasing/Details/{requestId}?handler=Submit", details, new Dictionary<string, string>());

        // 低金额（200）一级批准即生成订单。
        details = await GetHtmlAsync($"/Purchasing/Details/{requestId}");
        await PostFormAsync($"/Purchasing/Details/{requestId}?handler=Approve", details,
            new Dictionary<string, string> { ["comment"] = "低金额批准（发票测试）" });

        // 全额收货 2 件，作为三单匹配的收货依据。
        details = await GetHtmlAsync($"/Purchasing/Details/{requestId}");
        var orderLineId = GetInputValues(details, "ReceiptLines[0].PurchaseOrderLineId").First();
        await PostFormAsync($"/Purchasing/Details/{requestId}?handler=Receive", details, new Dictionary<string, string>
        {
            ["ReceiptDate"] = DateTime.Today.ToString("yyyy-MM-dd"),
            ["ReceiptIdempotencyKey"] = Guid.NewGuid().ToString("N"),
            ["ReceiptNote"] = "发票测试收货",
            ["ReceiptLines[0].PurchaseOrderLineId"] = orderLineId,
            ["ReceiptLines[0].Selected"] = "true",
            ["ReceiptLines[0].Quantity"] = "2"
        });

        return (requestId, orderReference, orderLineId);
    }

    private async Task AddQuoteAsync(string requestId, string pageHtml, string requestLineId, string supplierName, decimal unitPrice)
    {
        await PostFormAsync($"/Purchasing/Details/{requestId}?handler=AddQuote", pageHtml, new Dictionary<string, string>
        {
            ["supplierName"] = supplierName,
            ["deliveryDays"] = "5",
            ["note"] = "发票付款测试",
            ["QuoteLines[0].RequestLineId"] = requestLineId,
            ["QuoteLines[0].UnitPrice"] = unitPrice.ToString(System.Globalization.CultureInfo.InvariantCulture)
        });
    }

    private async Task<string> RegisterInvoiceAsync(string requestId, string pageHtml, string invoiceNumber,
        string orderLineId, decimal unitPrice, decimal quantity)
    {
        return await PostFormAsync($"/Purchasing/Details/{requestId}?handler=RegisterInvoice", pageHtml,
            new Dictionary<string, string>
            {
                ["InvoiceNumber"] = invoiceNumber,
                ["InvoiceDate"] = DateTime.Today.ToString("yyyy-MM-dd"),
                ["InvoiceNote"] = "页面测试发票",
                ["InvoiceLines[0].PurchaseOrderLineId"] = orderLineId,
                ["InvoiceLines[0].UnitPrice"] = unitPrice.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["InvoiceLines[0].Quantity"] = quantity.ToString(System.Globalization.CultureInfo.InvariantCulture)
            });
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
