using System;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;

namespace PurchaseFlow.Purchasing;

/// <summary>
/// Repository of the purchase request aggregate. The implementation loads the
/// lines and the quotes together with the aggregate root, so the aggregate
/// always works on a complete object graph and never on a half loaded one.
/// </summary>
public interface IPurchaseRequestRepository : IRepository<PurchaseRequest, Guid>
{
    /// <summary>
    /// Loads the request with its lines and its quotes.
    /// </summary>
    Task<PurchaseRequest> FindWithDetailsAsync(Guid id, CancellationToken cancellationToken = default);
}
