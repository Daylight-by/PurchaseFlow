using System;
using PurchaseFlow.Purchasing;
using Volo.Abp;
using Xunit;

namespace PurchaseFlow.Purchasing;

public class PurchaseRequestLineTests
{
    [Fact]
    public void Subtotal_is_quantity_times_unit_price()
    {
        var line = NewLine(4.5m, 12m);

        Assert.Equal(54m, line.Subtotal);
    }

    [Fact]
    public void Subtotal_is_rounded_to_two_decimals()
    {
        var line = NewLine(2.5m, 1.111m);

        Assert.Equal(2.78m, line.Subtotal);
    }

    [Fact]
    public void Quantity_must_be_greater_than_zero()
    {
        Assert.Equal(PurchaseFlowErrors.InvalidQuantity,
            Assert.Throws<BusinessException>(() => NewLine(0m, 10m)).Code);
    }

    [Fact]
    public void Unit_price_can_not_be_negative()
    {
        Assert.Equal(PurchaseFlowErrors.InvalidUnitPrice,
            Assert.Throws<BusinessException>(() => NewLine(1m, -0.01m)).Code);
    }

    [Fact]
    public void Item_name_is_required()
    {
        Assert.Throws<ArgumentException>(() =>
            new PurchaseRequestLine(Guid.NewGuid(), null, Guid.NewGuid(), "  ", "6205", 1m, 10m));
    }

    private static PurchaseRequestLine NewLine(decimal quantity, decimal unitPrice) =>
        new(Guid.NewGuid(), null, Guid.NewGuid(), "深沟球轴承", "6205-2RS", quantity, unitPrice);
}
