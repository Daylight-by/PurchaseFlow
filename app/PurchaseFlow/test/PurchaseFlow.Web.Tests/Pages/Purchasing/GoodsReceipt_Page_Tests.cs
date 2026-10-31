using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Shouldly;
using Xunit;

namespace PurchaseFlow.Pages.Purchasing;

/// <summary>
/// Drives the purchase detail page with real form posts: the request is
/// created, quoted, selected, submitted and approved, and the goods receipt
/// is registered through the page form. This proves that the page, the form
/// field names, the antiforgery token and the model binding of the receipt
/// lines work together.
/// </summary>
public class GoodsReceipt_Page_Tests : PurchaseFlowWebTestBase
{
    private readonly HttpClient _http;

    public GoodsReceipt_Page_Tests()
    {
        // The test client is bound to the in memory test server. The cookies
        // are handled here, so the sign in cookie and the antiforgery cookie
        // of the same browser session are sent back.
        _http = new HttpClient(new CookieHandler(Server.CreateHandler()))
        {
            BaseAddress = Client.BaseAddress
        };
    }

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
                        // A cookie that the container rejects must not fail the test.
                    }
                }
            }

            return response;
        }
    }
    [Fact]
    public async Task A_purchase_order_can_be_received_in_batches_through_the_page()
    {
        await SignInAsync();

        var requestId = await CreateRequestWithTwoLinesAsync();
        await AddTwoQuotesAsync(requestId);
        await SelectAndApproveAsync(requestId);

        var details = await GetHtmlAsync($"/Purchasing/Details/{requestId}");
        GetText(details).ShouldContain("分批收货");
        GetText(details).ShouldContain("累计已收");

        var lineIds = GetReceiptLineIds(details);
        lineIds.Count.ShouldBe(2);

        // First batch: A = 4, B = 2.
        var firstBatch = await PostReceiptAsync(requestId, new Dictionary<string, string>
        {
            ["ReceiptLines[0].PurchaseOrderLineId"] = lineIds[0],
            ["ReceiptLines[0].Selected"] = "true",
            ["ReceiptLines[0].Quantity"] = "4",
            ["ReceiptLines[1].PurchaseOrderLineId"] = lineIds[1],
            ["ReceiptLines[1].Selected"] = "true",
            ["ReceiptLines[1].Quantity"] = "2"
        });

        var alert = GetAlert(firstBatch);
        if (!GetText(firstBatch).Contains("部分收货"))
        {
            throw new InvalidOperationException(
                $"The first batch was not registered. alert={alert}, received={GetMetric(firstBatch, "累计已收")}, remaining={GetMetric(firstBatch, "剩余待收")}, orderStatus={GetMetric(firstBatch, "订单状态")}");
        }
        GetMetric(firstBatch, "累计已收").ShouldBe("6");
        GetMetric(firstBatch, "剩余待收").ShouldBe("9");
        GetMetric(firstBatch, "订购总量").ShouldBe("15");

        // More than the remaining quantity is rejected and nothing changes.
        var overReceipt = await PostReceiptAsync(requestId, new Dictionary<string, string>
        {
            ["ReceiptLines[0].PurchaseOrderLineId"] = lineIds[0],
            ["ReceiptLines[0].Selected"] = "true",
            ["ReceiptLines[0].Quantity"] = "7"
        });

        GetAlert(overReceipt).ShouldNotBeNullOrWhiteSpace();
        GetMetric(overReceipt, "累计已收").ShouldBe("6");
        GetMetric(overReceipt, "剩余待收").ShouldBe("9");

        // A receipt without a selected line is rejected as well.
        var emptyReceipt = await PostReceiptAsync(requestId, new Dictionary<string, string>
        {
            ["ReceiptLines[0].PurchaseOrderLineId"] = lineIds[0],
            ["ReceiptLines[0].Quantity"] = "4"
        });

        GetAlert(emptyReceipt).ShouldNotBeNullOrWhiteSpace();
        GetMetric(emptyReceipt, "累计已收").ShouldBe("6");

        // Second batch: A = 6, B = 3, the order is fully received. The token of
        // this page is kept for the direct call of the handler afterwards,
        // because the page hides the receipt form when everything is received.
        var tokenBeforeComplete = GetAntiforgeryToken(await GetHtmlAsync($"/Purchasing/Details/{requestId}"));
        var secondBatch = await PostReceiptAsync(requestId, new Dictionary<string, string>
        {
            ["ReceiptLines[0].PurchaseOrderLineId"] = lineIds[0],
            ["ReceiptLines[0].Selected"] = "true",
            ["ReceiptLines[0].Quantity"] = "6",
            ["ReceiptLines[1].PurchaseOrderLineId"] = lineIds[1],
            ["ReceiptLines[1].Selected"] = "true",
            ["ReceiptLines[1].Quantity"] = "3"
        });

        GetText(secondBatch).ShouldContain("已收货");
        GetMetric(secondBatch, "累计已收").ShouldBe("15");
        GetMetric(secondBatch, "剩余待收").ShouldBe("0");
        GetText(secondBatch).ShouldContain("已提交收货记录不可编辑或删除");

        // A fully received order rejects any further receipt. The page hides
        // the receipt form when everything is received, so the token of the
        // previous page is used for the direct call of the handler.
        var afterComplete = await PostReceiptAsync(requestId, new Dictionary<string, string>
        {
            ["ReceiptLines[0].PurchaseOrderLineId"] = lineIds[0],
            ["ReceiptLines[0].Selected"] = "true",
            ["ReceiptLines[0].Quantity"] = "1"
        }, tokenBeforeComplete);

        GetAlert(afterComplete).ShouldNotBeNullOrWhiteSpace();
        GetMetric(afterComplete, "累计已收").ShouldBe("15");
    }

    [Fact]
    public async Task A_receipt_keeps_the_order_amounts_unchanged()
    {
        await SignInAsync();

        var requestId = await CreateRequestWithTwoLinesAsync();
        await AddTwoQuotesAsync(requestId);
        await SelectAndApproveAsync(requestId);

        var details = await GetHtmlAsync($"/Purchasing/Details/{requestId}");
        var orderAmount = GetMetric(details, "订单金额");
        var saleAmount = GetMetric(details, "预计销售金额");
        var grossProfit = GetMetric(details, "预计毛利");

        var lineIds = GetReceiptLineIds(details);
        await PostReceiptAsync(requestId, new Dictionary<string, string>
        {
            ["ReceiptLines[0].PurchaseOrderLineId"] = lineIds[0],
            ["ReceiptLines[0].Selected"] = "true",
            ["ReceiptLines[0].Quantity"] = "4"
        });

        var afterReceipt = await GetHtmlAsync($"/Purchasing/Details/{requestId}");
        GetMetric(afterReceipt, "订单金额").ShouldBe(orderAmount);
        GetMetric(afterReceipt, "预计销售金额").ShouldBe(saleAmount);
        GetMetric(afterReceipt, "预计毛利").ShouldBe(grossProfit);
    }

    private async Task SignInAsync()
    {
        var loginPage = await GetHtmlAsync("/Account/Login");
        var result = await PostFormAsync("/Account/Login", loginPage, new Dictionary<string, string>
        {
            ["LoginInput.UserNameOrEmailAddress"] = PurchaseFlowWebTestModule.TestUserName,
            ["LoginInput.Password"] = PurchaseFlowWebTestModule.TestUserPassword
        });

        // The signed in layout offers the logout link, so the page after the
        // login proves that the session cookie was accepted.
        if (!result.Contains("Account/Logout"))
        {
            throw new InvalidOperationException(
                $"The sign in did not succeed, the page has no logout link. length={result.Length}.");
        }
    }

    private async Task<string> CreateRequestWithTwoLinesAsync()
    {
        var createPage = await GetHtmlAsync("/Purchasing/Create");
        var response = await PostFormResponseAsync("/Purchasing/Create", createPage, new Dictionary<string, string>
        {
            ["OrderReference"] = "SO-WEB-" + Guid.NewGuid().ToString("N")[..8],
            ["Description"] = "页面收货测试",
            ["SaleAmount"] = "1000",
            ["Lines[0].ItemName"] = "A",
            ["Lines[0].Specification"] = "规格 A",
            ["Lines[0].Quantity"] = "10",
            ["Lines[0].UnitPrice"] = "12.50",
            ["Lines[1].ItemName"] = "B",
            ["Lines[1].Specification"] = "规格 B",
            ["Lines[1].Quantity"] = "5",
            ["Lines[1].UnitPrice"] = "8.00"
        });

        var location = response.Headers.Location;
        location.ShouldNotBeNull();
        var requestUrl = location!.IsAbsoluteUri
            ? location.ToString()
            : new Uri(_http.BaseAddress!, location).ToString();
        requestUrl.ShouldContain("/Purchasing/Details/");
        return requestUrl.Split('/').Last();
    }

    private async Task AddTwoQuotesAsync(string requestId)
    {
        var details = await GetHtmlAsync($"/Purchasing/Details/{requestId}");

        // P0-1：报价改为按需求明细逐项录入。先从详情页 hidden 字段读出两条需求明细 Id，
        // 再按“需求明细 → 报价单价”提交；报价总额由服务端按逐项小计汇总，不再手填金额。
        var requestLineIds = GetQuoteRequestLineIds(details);
        requestLineIds.Count.ShouldBe(2);

        // 报价一：A 10 × 12.50 = 125，B 5 × 8.00 = 40，合计 165（保持原测试的经济口径）。
        await PostFormAsync($"/Purchasing/Details/{requestId}?handler=AddQuote", details, new Dictionary<string, string>
        {
            ["supplierName"] = "供应商一",
            ["deliveryDays"] = "5",
            ["note"] = "报价一",
            ["QuoteLines[0].RequestLineId"] = requestLineIds[0],
            ["QuoteLines[0].UnitPrice"] = "12.50",
            ["QuoteLines[1].RequestLineId"] = requestLineIds[1],
            ["QuoteLines[1].UnitPrice"] = "8.00"
        });

        details = await GetHtmlAsync($"/Purchasing/Details/{requestId}");
        // 报价总额必须等于逐项小计之和：A 10×12.50 + B 5×8.00 = 165，证明逐项提交被正确汇总。
        var detailsText = GetText(details);
        detailsText.ShouldContain("¥165.00");
        // 页面同时展示该报价的逐项明细（A 小计 125、B 小计 40），并给出“报价合计 = 明细合计”一致提示，
        // 这正是 P0-1 要修复的“总额与明细两套数”问题。
        detailsText.ShouldContain("逐项报价明细");
        detailsText.ShouldContain("¥125.00");
        detailsText.ShouldContain("¥40.00");
        detailsText.ShouldContain("✓ 一致");

        // 报价二：A 10 × 13.00 = 130，B 5 × 8.00 = 40，合计 170。
        await PostFormAsync($"/Purchasing/Details/{requestId}?handler=AddQuote", details, new Dictionary<string, string>
        {
            ["supplierName"] = "供应商二",
            ["deliveryDays"] = "6",
            ["note"] = "报价二",
            ["QuoteLines[0].RequestLineId"] = requestLineIds[0],
            ["QuoteLines[0].UnitPrice"] = "13.00",
            ["QuoteLines[1].RequestLineId"] = requestLineIds[1],
            ["QuoteLines[1].UnitPrice"] = "8.00"
        });

        details = await GetHtmlAsync($"/Purchasing/Details/{requestId}");
        GetText(details).ShouldContain("¥170.00");
    }

    private async Task SelectAndApproveAsync(string requestId)
    {
        var details = await GetHtmlAsync($"/Purchasing/Details/{requestId}");
        var quoteIds = GetInputValues(details, "quoteId");
        quoteIds.Count.ShouldBeGreaterThanOrEqualTo(2);

        await PostFormAsync($"/Purchasing/Details/{requestId}?handler=Select", details, new Dictionary<string, string>
        {
            ["quoteId"] = quoteIds[0],
            ["reason"] = "价格与交期综合最优"
        });

        details = await GetHtmlAsync($"/Purchasing/Details/{requestId}");
        await PostFormAsync($"/Purchasing/Details/{requestId}?handler=Submit", details, new Dictionary<string, string>());

        details = await GetHtmlAsync($"/Purchasing/Details/{requestId}");
        await PostFormAsync($"/Purchasing/Details/{requestId}?handler=Approve", details, new Dictionary<string, string>
        {
            ["comment"] = "页面流程测试批准"
        });
    }

    private async Task<string> PostReceiptAsync(
        string requestId,
        IDictionary<string, string> lines,
        string? antiforgeryToken = null)
    {
        var token = antiforgeryToken ?? GetAntiforgeryToken(await GetHtmlAsync($"/Purchasing/Details/{requestId}"));
        var fields = new Dictionary<string, string>
        {
            ["ReceiptDate"] = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["ReceiptIdempotencyKey"] = Guid.NewGuid().ToString("N"),
            ["ReceiptNote"] = "页面收货测试"
        };
        foreach (var line in lines)
        {
            fields[line.Key] = line.Value;
        }

        return await PostFormWithTokenAsync($"/Purchasing/Details/{requestId}?handler=Receive", token, fields);
    }

    [Fact]
    public async Task Editing_request_lines_keeps_the_existing_line_ids()
    {
        await SignInAsync();

        var requestId = await CreateRequestWithTwoLinesAsync();
        var details = await GetHtmlAsync($"/Purchasing/Details/{requestId}");
        var originalIds = GetLineIds(details);
        originalIds.Count.ShouldBe(2);
        originalIds.ShouldAllBe(id => !string.IsNullOrWhiteSpace(id));

        // P1-3：编辑保存时页面必须把已存在的明细行 Id 回传，应用层据此“就地更新”，
        // 行 Id 保持不变；如果 Id 丢失，服务端会把更新当成新增，行 Id 会变。
        await PostFormAsync($"/Purchasing/Details/{requestId}?handler=Update", details, new Dictionary<string, string>
        {
            ["OrderReference"] = "SO-WEB-UPDATED",
            ["Description"] = "页面更新测试",
            ["SaleAmount"] = "1000",
            ["Lines[0].Id"] = originalIds[0],
            ["Lines[0].ItemName"] = "A",
            ["Lines[0].Specification"] = "规格 A",
            ["Lines[0].Quantity"] = "10",
            ["Lines[0].UnitPrice"] = "12.50",
            ["Lines[1].Id"] = originalIds[1],
            ["Lines[1].ItemName"] = "B",
            ["Lines[1].Specification"] = "规格 B",
            ["Lines[1].Quantity"] = "5",
            ["Lines[1].UnitPrice"] = "8.00"
        });

        var updated = await GetHtmlAsync($"/Purchasing/Details/{requestId}");
        GetLineIds(updated).ShouldBe(originalIds);
        GetText(updated).ShouldContain("SO-WEB-UPDATED");
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

        // The test client does not follow the redirect, so the page after the
        // action is requested explicitly.
        if (IsRedirect(response))
        {
            var location = response.Headers.Location;
            location.ShouldNotBeNull();
            var target = location!.IsAbsoluteUri
                ? location.ToString()
                : new Uri(_http.BaseAddress!, location).ToString();
            var followed = await _http.GetAsync(target);
            followed.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
            return await followed.Content.ReadAsStringAsync();
        }

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync();
    }

    private static bool IsRedirect(HttpResponseMessage response) =>
        response.StatusCode is System.Net.HttpStatusCode.Found
            or System.Net.HttpStatusCode.MovedPermanently
            or System.Net.HttpStatusCode.SeeOther
            or System.Net.HttpStatusCode.TemporaryRedirect;

    private async Task<string> PostFormWithTokenAsync(string url, string antiforgeryToken, IDictionary<string, string> fields)
    {
        var payload = new Dictionary<string, string>(fields)
        {
            ["__RequestVerificationToken"] = antiforgeryToken
        };

        var response = await _http.PostAsync(url, new FormUrlEncodedContent(payload));
        if (IsRedirect(response))
        {
            var location = response.Headers.Location;
            location.ShouldNotBeNull();
            var target = location!.IsAbsoluteUri
                ? location.ToString()
                : new Uri(_http.BaseAddress!, location).ToString();
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

    private static string GetAntiforgeryToken(string html)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var node = document.DocumentNode.SelectSingleNode("//input[@name='__RequestVerificationToken']");
        if (node == null)
        {
            var names = document.DocumentNode
                .SelectNodes("//input")
                ?.Select(input => input.Attributes["name"]?.Value ?? "(no name)")
                .Distinct()
                .Take(30)
                .ToList() ?? new List<string>();
            var text = WebUtility.HtmlDecode(document.DocumentNode.InnerText);
            throw new InvalidOperationException(
                $"The page has no antiforgery token. length={html.Length}, " +
                $"viewOnly={text.Contains("只有收货查看权限")}, fullyReceived={text.Contains("已提交收货记录不可编辑或删除")}, " +
                $"hasReceiptForm={text.Contains("本次收货")}, inputs={string.Join(", ", names)}");
        }

        return node.Attributes["value"].Value;
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

    /// <summary>
    /// P1-3：读取详情页编辑表单里回传的明细行 Id。
    /// 只取 Lines[i].Id，避免把 QuoteLines[i].RequestLineId 误当成明细行 Id。
    /// </summary>
    private static List<string> GetLineIds(string html)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var nodes = document.DocumentNode.SelectNodes("//input[starts-with(@name, 'Lines[')]");
        return nodes?
            // 行编辑器的 <template> 里也有一个空值的 Lines[0].Id，用于 JS 新增行；这里只取有值的真实行。
            .Where(node => (node.Attributes["name"]?.Value ?? string.Empty).EndsWith(".Id", StringComparison.Ordinal)
                           && !string.IsNullOrEmpty(node.Attributes["value"]?.Value))
            .Select(node => node.Attributes["value"].Value)
            .ToList() ?? new List<string>();
    }

    /// <summary>
    /// P0-1：读取“添加报价”表单按当前需求明细逐项渲染的隐藏行 Id。
    /// XPath 选择的是文档顺序，即页面明细顺序（Lines[0]、Lines[1]…）。
    /// </summary>
    private static List<string> GetQuoteRequestLineIds(string html)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        return document.DocumentNode
            .SelectNodes("//input[contains(@name, 'QuoteLines') and contains(@name, 'RequestLineId')]")
            ?.Select(node => node.Attributes["value"].Value)
            .ToList() ?? new List<string>();
    }

    private static List<string> GetReceiptLineIds(string html)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        return document.DocumentNode
            .SelectNodes("//input[contains(@name, 'PurchaseOrderLineId')]")
            ?.Select(node => node.Attributes["value"].Value)
            .ToList() ?? new List<string>();
    }

    private static string GetText(string html)
    {
        // Razor encodes the Chinese text as html entities, so the assertions
        // work on the decoded document text.
        var document = new HtmlDocument();
        document.LoadHtml(html);
        return WebUtility.HtmlDecode(document.DocumentNode.InnerText);
    }
    private static string GetAlert(string html)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var node = document.DocumentNode.SelectSingleNode("//div[contains(@class, 'alert-danger')]");
        return node == null ? string.Empty : WebUtility.HtmlDecode(node.InnerText).Trim();
    }
    private static string GetMetric(string html, string label)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var node = document.DocumentNode.SelectSingleNode(
            $"//div[contains(@class, 'pf-metric')][span[normalize-space()='{label}']]/strong");
        node.ShouldNotBeNull();
        return WebUtility.HtmlDecode(node!.InnerText).Trim();
    }
}
