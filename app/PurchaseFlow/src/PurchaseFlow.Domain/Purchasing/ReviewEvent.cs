using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;

namespace PurchaseFlow.Purchasing;

public class ReviewEvent : Entity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid PurchaseRequestId { get; private set; }
    public Guid ActorUserId { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string? Comment { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }

    protected ReviewEvent() { }

    public ReviewEvent(Guid id, Guid? tenantId, Guid purchaseRequestId, Guid actorUserId, string action, string? comment)
        : base(id)
    {
        TenantId = tenantId;
        PurchaseRequestId = purchaseRequestId;
        ActorUserId = actorUserId;
        Action = Check.NotNullOrWhiteSpace(action, nameof(action), maxLength: 30);
        Comment = string.IsNullOrWhiteSpace(comment) ? null : Check.Length(comment.Trim(), nameof(comment), maxLength: 500);
        OccurredAtUtc = DateTime.UtcNow;
    }
}

