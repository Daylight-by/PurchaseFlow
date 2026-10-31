using System;
using PurchaseFlow.Purchasing;
using Volo.Abp;
using Xunit;

namespace PurchaseFlow.Purchasing;

/// <summary>
/// 第二轮第 1 批：供应商档案的领域规则。
/// 重点验证：编码规范化与格式、名称必填、账期范围、停用后不能再被引用、更新同样要过校验。
/// </summary>
public class SupplierTests
{
    [Fact]
    public void Supplier_code_is_trimmed_and_normalized_to_upper_case()
    {
        var supplier = NewSupplier(code: "  sup-001  ");

        Assert.Equal("SUP-001", supplier.Code);
        Assert.True(supplier.IsActive);
        Assert.Equal(SupplierRating.C, supplier.Rating);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("SUP 001")]
    [InlineData("SUP_001")]
    [InlineData("-SUP001")]
    [InlineData("供应商001")]
    public void Supplier_code_must_use_upper_case_letters_digits_and_dashes(string code)
    {
        Assert.Equal(PurchaseFlowErrors.InvalidSupplierCode,
            Assert.Throws<BusinessException>(() => NewSupplier(code: code)).Code);
    }

    [Fact]
    public void Supplier_name_is_required()
    {
        Assert.Throws<ArgumentException>(() => NewSupplier(name: "  "));
    }

    [Fact]
    public void Payment_terms_must_not_be_negative()
    {
        Assert.Throws<BusinessException>(() => NewSupplier(paymentTermsDays: -1));
    }

    [Fact]
    public void Payment_terms_above_the_business_limit_are_rejected()
    {
        // 契约 Range(0,365) 与领域边界必须一致：>365 同样是业务错误，
        // 避免 UI/API 只靠 DTO 校验、领域层还能存进 366 天。
        Assert.Throws<BusinessException>(() => NewSupplier(paymentTermsDays: 366));
    }

    [Fact]
    public void A_deactivated_supplier_can_not_be_used_for_new_documents()
    {
        var supplier = NewSupplier();
        supplier.Deactivate();

        Assert.False(supplier.IsActive);
        Assert.Equal(PurchaseFlowErrors.SupplierInactive,
            Assert.Throws<BusinessException>(() => supplier.EnsureActive()).Code);

        supplier.Activate();
        supplier.EnsureActive();
        Assert.True(supplier.IsActive);
    }

    [Fact]
    public void Updating_a_supplier_revalidates_the_code_and_keeps_master_data_consistent()
    {
        var supplier = NewSupplier(code: "SUP-001", name: "华东办公家具");

        supplier.Update("sup-002", "华东办公家具（新）", "张三", "13800000000", 30,
            SupplierRating.A, "重点供应商");

        Assert.Equal("SUP-002", supplier.Code);
        Assert.Equal("华东办公家具（新）", supplier.Name);
        Assert.Equal("张三", supplier.ContactName);
        Assert.Equal(30, supplier.PaymentTermsDays);
        Assert.Equal(SupplierRating.A, supplier.Rating);
        Assert.Equal("重点供应商", supplier.Note);

        Assert.Equal(PurchaseFlowErrors.InvalidSupplierCode,
            Assert.Throws<BusinessException>(() =>
                supplier.Update("bad code", "x", null, null, 0, SupplierRating.C, null)).Code);
    }

    private static Supplier NewSupplier(
        string code = "SUP-001",
        string name = "华东办公家具",
        int paymentTermsDays = 30)
    {
        return new Supplier(Guid.NewGuid(), null, code, name, "李四", "13900000000",
            paymentTermsDays, SupplierRating.C, null);
    }
}
