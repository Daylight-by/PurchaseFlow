using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace PurchaseFlow.Purchasing;

/// <summary>
/// 第二轮第 1 批：供应商档案。报价可以关联档案（SupplierId），
/// 但报价/订单上仍保存供应商名称快照 —— 档案改名或停用不能影响历史单据。
/// </summary>
public class SupplierDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ContactName { get; set; }
    public string? ContactPhone { get; set; }
    public int PaymentTermsDays { get; set; }
    public SupplierRating Rating { get; set; }
    public bool IsActive { get; set; }
    public string? Note { get; set; }
    public DateTime CreationTime { get; set; }
}

public class CreateSupplierDto
{
    [Required, StringLength(32)]
    public string Code { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [StringLength(50)]
    public string? ContactName { get; set; }

    [StringLength(30)]
    public string? ContactPhone { get; set; }

    [Range(0, 365)]
    public int PaymentTermsDays { get; set; }

    public SupplierRating Rating { get; set; } = SupplierRating.C;

    [StringLength(500)]
    public string? Note { get; set; }
}

public class UpdateSupplierDto : CreateSupplierDto
{
}

public class SupplierListInput
{
    public string? Keyword { get; set; }
    public bool? IsActive { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public class SupplierListDto
{
    public List<SupplierDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

/// <summary>
/// 第二轮第 1 批：物料档案。采购明细可以关联档案（MaterialId），
/// 明细上的物料名称/规格仍是快照，档案改名不影响历史需求与订单。
/// </summary>
public class MaterialDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Specification { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string? Category { get; set; }
    public bool IsActive { get; set; }
    public string? Note { get; set; }
    public DateTime CreationTime { get; set; }
}

public class CreateMaterialDto
{
    [Required, StringLength(32)]
    public string Code { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [StringLength(100)]
    public string Specification { get; set; } = string.Empty;

    [Required, StringLength(20)]
    public string Unit { get; set; } = string.Empty;

    [StringLength(50)]
    public string? Category { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }
}

public class UpdateMaterialDto : CreateMaterialDto
{
}

public class MaterialListInput
{
    public string? Keyword { get; set; }
    public bool? IsActive { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public class MaterialListDto
{
    public List<MaterialDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}
