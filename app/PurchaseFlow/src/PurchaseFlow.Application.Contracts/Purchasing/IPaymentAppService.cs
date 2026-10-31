using System;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace PurchaseFlow.Purchasing;

/// <summary>
/// 第三轮：付款申请应用服务。匹配发票 → 申请付款 → 管理员批准 → 登记付款，
/// 每一步的状态流转都由领域层强制，页面只负责提供操作入口。
/// </summary>
public interface IPaymentAppService : IApplicationService
{
    Task<PaymentListDto> GetListAsync(PaymentListInput input);
    Task<PaymentApplicationDto> RequestAsync(Guid invoiceId, CreatePaymentApplicationDto input);
    Task<PaymentApplicationDto> ApproveAsync(Guid paymentId, ReviewPaymentApplicationDto input);
    Task<PaymentApplicationDto> RegisterPaymentAsync(Guid paymentId, RegisterPaymentDto input);
}
