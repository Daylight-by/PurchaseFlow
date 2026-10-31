using System;
using PurchaseFlow.Purchasing;
using Volo.Abp;
using Xunit;

namespace PurchaseFlow.Purchasing;

/// <summary>
/// 第二轮第 1 批：物料档案的领域规则。
/// 重点验证：编码规范化与格式、名称/单位必填、停用后不能再被引用、更新同样要过校验。
/// </summary>
public class MaterialTests
{
    [Fact]
    public void Material_code_is_trimmed_and_normalized_to_upper_case()
    {
        var material = NewMaterial(code: "  mat-001 ");

        Assert.Equal("MAT-001", material.Code);
        Assert.True(material.IsActive);
        Assert.Equal("把", material.Unit);
    }

    [Theory]
    [InlineData("")]
    [InlineData("MAT 001")]
    [InlineData("MAT_001")]
    [InlineData("物料001")]
    public void Material_code_must_use_upper_case_letters_digits_and_dashes(string code)
    {
        Assert.Equal(PurchaseFlowErrors.InvalidMaterialCode,
            Assert.Throws<BusinessException>(() => NewMaterial(code: code)).Code);
    }

    [Fact]
    public void Material_name_and_unit_are_required()
    {
        Assert.Throws<ArgumentException>(() => NewMaterial(name: " "));
        Assert.Throws<ArgumentException>(() => NewMaterial(unit: " "));
    }

    [Fact]
    public void A_deactivated_material_can_not_be_used_for_new_documents()
    {
        var material = NewMaterial();
        material.Deactivate();

        Assert.False(material.IsActive);
        Assert.Equal(PurchaseFlowErrors.MaterialInactive,
            Assert.Throws<BusinessException>(() => material.EnsureActive()).Code);

        material.Activate();
        material.EnsureActive();
        Assert.True(material.IsActive);
    }

    [Fact]
    public void Updating_a_material_revalidates_the_code_and_refreshes_the_snapshot_source()
    {
        var material = NewMaterial();

        material.Update("MAT-009", "办公椅（新版）", "人体工学 2.0", "张", "办公家具", "升级款");

        Assert.Equal("MAT-009", material.Code);
        Assert.Equal("办公椅（新版）", material.Name);
        Assert.Equal("人体工学 2.0", material.Specification);
        Assert.Equal("张", material.Unit);
        Assert.Equal("办公家具", material.Category);
        Assert.Equal("升级款", material.Note);

        Assert.Equal(PurchaseFlowErrors.InvalidMaterialCode,
            Assert.Throws<BusinessException>(() =>
                material.Update("bad code", "x", "", "个", null, null)).Code);
    }

    private static Material NewMaterial(
        string code = "MAT-001",
        string name = "办公椅",
        string unit = "把")
    {
        return new Material(Guid.NewGuid(), null, code, name, "人体工学", unit, "办公家具", null);
    }
}
