namespace PurchaseFlow.Purchasing;

/// <summary>
/// Lifecycle of a purchase order. A purchase order is created when the
/// purchase request is approved and follows the supplier afterwards.
/// </summary>
public enum PurchaseOrderStatus
{
    Created = 0,
    Issued = 1,
    Received = 2,
    Closed = 3
}
