using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace PurchaseFlow.Purchasing;

/// <summary>
/// 第三轮（三单匹配）：登记供应商发票的输入。
/// 单价必须由调用方提供，服务端会与采购订单成交单价逐行比对；
/// 数量不能超过该订单行“已收货 - 已开票”的剩余可开票量。
/// </summary>
public class CreateSupplierInvoiceDto
{
    [Required]
    public Guid PurchaseOrderId { get; set; }

    [Required, StringLength(64)]
    public string InvoiceNumber { get; set; } = string.Empty;

    [DataType(DataType.Date)]
    public DateTime InvoiceDate { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }

    [Required, MinLength(1)]
    public List<CreateSupplierInvoiceLineDto> Lines { get; set; } = new();
}

public class CreateSupplierInvoiceLineDto
{
    [Required]
    public Guid PurchaseOrderLineId { get; set; }

    [Range(typeof(decimal), "0.001", "9999999999999999.999")]
    public decimal Quantity { get; set; }

    [Range(typeof(decimal), "0", "9999999999999999.99")]
    public decimal UnitPrice { get; set; }
}

public class SupplierInvoiceLineDto
{
    public Guid Id { get; set; }
    public Guid PurchaseOrderLineId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string Specification { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Subtotal { get; set; }
}

public class SupplierInvoiceDto
{
    public Guid Id { get; set; }
    public Guid PurchaseOrderId { get; set; }
    public string PurchaseOrderNumber { get; set; } = string.Empty;
    public Guid? SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime InvoiceDate { get; set; }
    public decimal Amount { get; set; }
    public SupplierInvoiceStatus Status { get; set; }
    public string? Note { get; set; }
    public DateTime CreationTime { get; set; }
    public List<SupplierInvoiceLineDto> Lines { get; set; } = new();

    /// <summary>第三轮：该发票的付款申请（没有则为 null）。</summary>
    public Guid? PaymentId { get; set; }
    public PaymentApplicationStatus? PaymentStatus { get; set; }
    public decimal? PaymentAmount { get; set; }
}

public class InvoiceListInput
{
    public string? Keyword { get; set; }
    public SupplierInvoiceStatus? Status { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public class InvoiceListDto
{
    public List<SupplierInvoiceDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

/// <summary>
/// 第三轮：付款申请输入。Amount 为空表示按发票全额申请；
/// 服务端仍会校验不超过发票金额、同一发票只能有一笔付款申请。
/// </summary>
public class CreatePaymentApplicationDto
{
    [Range(typeof(decimal), "0.01", "9999999999999999.99")]
    public decimal? Amount { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }
}

public class PaymentApplicationDto
{
    public Guid Id { get; set; }
    public Guid SupplierInvoiceId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public Guid PurchaseOrderId { get; set; }
    public string PurchaseOrderNumber { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public PaymentApplicationStatus Status { get; set; }
    public string? Note { get; set; }
    public DateTime CreationTime { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime? PaymentDate { get; set; }
}

public class ReviewPaymentApplicationDto
{
    [StringLength(500)]
    public string? Comment { get; set; }
}

public class RegisterPaymentDto
{
    [DataType(DataType.Date)]
    public DateTime PaymentDate { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }
}

public class PaymentListInput
{
    public PaymentApplicationStatus? Status { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public class PaymentListDto
{
    public List<PaymentApplicationDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}
