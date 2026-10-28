using Microsoft.AspNetCore.Builder;
using PurchaseFlow;
using Volo.Abp.AspNetCore.TestBase;

var builder = WebApplication.CreateBuilder();

builder.Environment.ContentRootPath = GetWebProjectContentRootPathHelper.Get("PurchaseFlow.Web.csproj");
await builder.RunAbpModuleAsync<PurchaseFlowWebTestModule>(applicationName: "PurchaseFlow.Web" );

public partial class Program
{
}

