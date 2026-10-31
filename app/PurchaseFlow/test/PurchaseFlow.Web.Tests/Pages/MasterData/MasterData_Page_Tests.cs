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

namespace PurchaseFlow.Pages.MasterData;

/// <summary>
/// 第二轮第 1 批：主数据（供应商/物料档案）页面的冒烟与联动测试。
/// 用真实表单 POST 驱动页面，验证列表可访问、创建成功、重复编码显示错误，
/// 以及采购明细/报价能够关联档案并由服务端用档案快照覆盖客户端文本。
/// </summary>
public class MasterData_Page_Tests : PurchaseFlowWebTestBase
{
    private readonly HttpClient _http;

    public MasterData_Page_Tests()
    {
        // 与收货页面测试一致：使用带 CookieContainer 的客户端，
        // 这样登录 Cookie 与防伪 Cookie 会在同一个浏览器会话里往返。
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
                        // 被 CookieContainer 拒绝的 Cookie 不应让测试失败。
                    }
                }
            }

            return response;
        }
    }

    [Fact]
    public async Task Master_data_pages_can_be_opened()
    {
        await SignInAsync();

        var suppliers = await GetHtmlAsync("/MasterData/Suppliers");
        GetText(suppliers).ShouldContain("供应商档案");
        // AlwaysAllow 授权下当前测试用户拥有 ManageMasterData，所以应显示写操作表单。
        GetText(suppliers).ShouldContain("新建供应商档案");

        var materials = await GetHtmlAsync("/MasterData/Materials");
        GetText(materials).ShouldContain("物料档案");
        GetText(materials).ShouldContain("新建物料档案");
    }

    [Fact]
    public async Task A_supplier_can_be_created_and_duplicate_code_is_rejected()
    {
        await SignInAsync();

        var code = NewCode("SUP");
        await CreateSupplierAsync(code, "页面测试供应商");

        var text = GetText(await GetHtmlAsync("/MasterData/Suppliers"));
        text.ShouldContain(code);
        text.ShouldContain("页面测试供应商");
        text.ShouldContain("A（优先）");

        // 同一编码再次创建：应用层抛 DuplicateSupplierCode，页面应展示错误而不是 500。
        var page = await GetHtmlAsync("/MasterData/Suppliers");
        var duplicate = await PostFormAsync("/MasterData/Suppliers?handler=Save", page, new Dictionary<string, string>
        {
            ["Code"] = code,
            ["Name"] = "重复编码供应商",
            ["PaymentTermsDays"] = "10",
            ["Rating"] = "C"
        });
        GetAlert(duplicate).ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_material_can_be_created_and_deactivated()
    {
        await SignInAsync();

        var code = NewCode("MAT");
        await CreateMaterialAsync(code, "页面测试物料");

        var listPage = await GetHtmlAsync("/MasterData/Materials");
        var text = GetText(listPage);
        text.ShouldContain(code);
        text.ShouldContain("页面测试物料");

        // 行内“停用”按钮：先按编码定位到行，再取该行隐藏的档案 Id 提交。
        var materialId = GetRowInputValue(listPage, code, "id");
        materialId.ShouldNotBeNullOrWhiteSpace();
        GetRowStatus(listPage, code).ShouldBe("启用");

        var afterToggle = await PostFormAsync("/MasterData/Materials?handler=Toggle", listPage, new Dictionary<string, string>
        {
            ["id"] = materialId!,
            ["isActive"] = "false"
        });
        GetRowStatus(afterToggle, code).ShouldBe("停用");
    }

    [Fact]
    public async Task Request_lines_and_quotes_can_reference_master_data()
    {
        await SignInAsync();

        var materialCode = NewCode("MAT");
        var materialName = "页面联动物料";
        await CreateMaterialAsync(materialCode, materialName, specification: "规格联动");
        var materialId = GetRowInputValue(await GetHtmlAsync("/MasterData/Materials"), materialCode, "id");
        materialId.ShouldNotBeNullOrWhiteSpace();

        var supplierCode = NewCode("SUP");
        var supplierName = "页面联动供应商";
        await CreateSupplierAsync(supplierCode, supplierName);
        var supplierId = GetRowInputValue(await GetHtmlAsync("/MasterData/Suppliers"), supplierCode, "id");
        supplierId.ShouldNotBeNullOrWhiteSpace();

        // 创建采购需求：明细带 MaterialId，物料名称/规格故意填“占位”，验证服务端用档案快照覆盖。
        var createPage = await GetHtmlAsync("/Purchasing/Create");
        // 物料档案下拉应包含刚创建的启用物料（证明页面模型把 Materials 传给了行编辑器）。
        GetText(createPage).ShouldContain(materialCode);
        var response = await PostFormResponseAsync("/Purchasing/Create", createPage, new Dictionary<string, string>
        {
            ["OrderReference"] = "SO-WEB-MD-" + Guid.NewGuid().ToString("N")[..8],
            ["Description"] = "主数据联动测试",
            ["SaleAmount"] = "1000",
            ["Lines[0].MaterialId"] = materialId!,
            ["Lines[0].ItemName"] = "占位物料名称",
            ["Lines[0].Specification"] = "占位规格",
            ["Lines[0].Quantity"] = "2",
            ["Lines[0].UnitPrice"] = "10"
        });

        var requestId = FollowRedirectAndGetRequestId(response);
        var details = await GetHtmlAsync($"/Purchasing/Details/{requestId}");
        var detailsText = GetText(details);
        // 服务端应使用物料档案的名称/规格作为快照，而不是页面提交的占位文本。
        detailsText.ShouldContain(materialName);
        detailsText.ShouldContain("规格联动");
        detailsText.ShouldNotContain("占位物料名称");

        // 添加报价：带 SupplierId + 占位供应商名称，验证服务端用档案名称覆盖并记录关联。
        var requestLineIds = GetInputValues(details, "QuoteLines[0].RequestLineId");
        requestLineIds.Count.ShouldBe(1);
        // 报价表单的供应商档案下拉应包含刚创建的启用供应商（Code - Name）。
        GetText(details).ShouldContain(supplierCode);
        GetText(details).ShouldContain(supplierName);
        var quoted = await PostFormAsync($"/Purchasing/Details/{requestId}?handler=AddQuote", details, new Dictionary<string, string>
        {
            ["supplierName"] = "占位供应商名称",
            ["supplierId"] = supplierId!,
            ["deliveryDays"] = "5",
            ["QuoteLines[0].RequestLineId"] = requestLineIds[0],
            ["QuoteLines[0].UnitPrice"] = "10"
        });

        var quotedText = GetText(quoted);
        quotedText.ShouldContain(supplierName);
        quotedText.ShouldNotContain("占位供应商名称");
        quotedText.ShouldContain("已关联供应商档案");
        // 报价合计 = 2 × 10 = 20，证明逐项报价仍按明细汇总。
        quotedText.ShouldContain("¥20.00");
    }

    private async Task CreateSupplierAsync(string code, string name)
    {
        var page = await GetHtmlAsync("/MasterData/Suppliers");
        await PostFormAsync("/MasterData/Suppliers?handler=Save", page, new Dictionary<string, string>
        {
            ["Code"] = code,
            ["Name"] = name,
            ["ContactName"] = "测试联系人",
            ["ContactPhone"] = "021-00000000",
            ["PaymentTermsDays"] = "30",
            ["Rating"] = "A",
            ["Note"] = "页面测试"
        });
    }

    private async Task CreateMaterialAsync(string code, string name, string? specification = null)
    {
        var page = await GetHtmlAsync("/MasterData/Materials");
        await PostFormAsync("/MasterData/Materials?handler=Save", page, new Dictionary<string, string>
        {
            ["Code"] = code,
            ["Name"] = name,
            ["Specification"] = specification ?? "标准规格",
            ["Unit"] = "个",
            ["Category"] = "测试分类",
            ["Note"] = "页面测试"
        });
    }

    private static string NewCode(string prefix) => $"{prefix}-WEB-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";

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

    /// <summary>读取“包含指定编码/名称的行”里某个隐藏输入的值（例如停用按钮的档案 Id）。</summary>
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

    /// <summary>取包含指定编码/名称的行里第一个状态标签文本（启用/停用），用于断言启停用。</summary>
    private static string GetRowStatus(string html, string cellText)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var row = document.DocumentNode.SelectSingleNode($"//tr[.//*[normalize-space()='{cellText}']]");
        row.ShouldNotBeNull($"no table row containing '{cellText}'.");
        var status = row!.SelectSingleNode(".//span[contains(@class, 'pf-status')]");
        status.ShouldNotBeNull($"no status badge in the row containing '{cellText}'.");
        return WebUtility.HtmlDecode(status!.InnerText).Trim();
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
