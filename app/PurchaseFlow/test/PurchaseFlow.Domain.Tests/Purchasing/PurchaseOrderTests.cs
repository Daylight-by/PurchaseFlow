using System;
using System.Linq;
using PurchaseFlow.Purchasing;
using Volo.Abp;
using Xunit;

namespace PurchaseFlow.Purchasing;

public class PurchaseOrderTests
{
    [Fact]
    public void A_purchase_order_amount_is_the_sum_of_its_lines()
    {
        var order = NewOrder();

        // P0-1: 订单金额必须等于明细合计，AddLine 会按明细重算金额。
        order.AddLine(Guid.NewGuid(), "深沟球轴承", "6205-2RS", 10m, 25.5m);
        order.AddLine(Guid.NewGuid(), "骨架油封", "NBR 20x3", 4.5m, 12m);

        Assert.Equal(2, order.Lines.Count);
        Assert.Equal(309m, order.Lines.Sum(x => x.Subtotal));
        Assert.Equal(order.Lines.Sum(x => x.Subtotal), order.Amount);
    }

    [Fact]
    public void A_purchase_order_keeps_the_ordered_lines()
    {
        var order = NewOrder();

        order.AddLine(Guid.NewGuid(), "深沟球轴承", "6205-2RS", 10m, 25.5m);
        order.AddLine(Guid.NewGuid(), "骨架油封", "NBR 20x3", 4.5m, 12m);

        Assert.Equal(2, order.Lines.Count);
        Assert.Equal(309m, order.Lines.Sum(x => x.Subtotal));
    }

    [Fact]
    public void A_line_of_a_purchase_order_needs_a_positive_quantity()
    {
        var order = NewOrder();

        Assert.Equal(PurchaseFlowErrors.InvalidQuantity,
            Assert.Throws<BusinessException>(() => order.AddLine(Guid.NewGuid(), "深沟球轴承", "6205-2RS", 0m, 25.5m)).Code);
    }

    [Fact]
    public void An_order_can_only_be_issued_once()
    {
        var order = NewOrder();
        order.Issue();

        Assert.Equal(PurchaseOrderStatus.Issued, order.Status);
        Assert.Equal(PurchaseFlowErrors.InvalidStatus,
            Assert.Throws<BusinessException>(() => order.Issue()).Code);
    }

    [Fact]
    public void Order_number_holds_the_longest_allowed_request_reference()
    {
        // P1-1: OrderReference 最长 64 个字符，加 "PO-" 前缀后为 67 个字符，
        // 所以订单号上限扩大到 128，这里验证边界长度可以正常构造。
        var orderReference = new string('R', 64);
        var order = new PurchaseOrder(Guid.NewGuid(), null, $"PO-{orderReference}",
            Guid.NewGuid(), "华东机电", 0m, 10);

        Assert.Equal(67, order.OrderNumber.Length);
        Assert.Equal($"PO-{orderReference}", order.OrderNumber);
    }

    private static PurchaseOrder NewOrder() =>
        new(Guid.NewGuid(), null, "PO-SO-1", Guid.NewGuid(), "华东机电", 0m, 10);
}
