using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace PurchaseFlow.Purchasing;

/// <summary>
/// 第二轮第 1 批：物料档案（主数据）。
///
/// 为什么单独建档案：采购明细原来完全是自由文本，同一物料反复录入会出现
/// 名称/规格/单位不一致。档案提供可选的标准化来源；明细上仍保存
/// 名称/规格快照，因此档案改名不会改写历史需求与订单。
///
/// 停用语义：停用（IsActive=false）后不能再被新的需求明细引用
/// （应用层调用 EnsureActive 拦截），历史单据保留原快照。
/// </summary>
public class Material : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    /// <summary>物料编码：trim 后转大写，≤32，格式 ^[A-Z0-9][A-Z0-9-]*$。</summary>
    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;
    public string Specification { get; private set; } = string.Empty;

    /// <summary>计量单位（把/张/台等），必填；长度 ≤20。</summary>
    public string Unit { get; private set; } = string.Empty;

    public string? Category { get; private set; }
    public bool IsActive { get; private set; }
    public string? Note { get; private set; }

    protected Material() { }

    public Material(
        Guid id,
        Guid? tenantId,
        string code,
        string name,
        string specification = "",
        string unit = "",
        string? category = null,
        string? note = null)
        : base(id)
    {
        TenantId = tenantId;
        ApplyDetails(code, name, specification, unit, category, note);
        // 新建档案默认启用：录入后应立即可用，停用是显式动作。
        IsActive = true;
    }

    /// <summary>
    /// 修改档案。Code 变更同样走统一规范化与校验；启停用状态不在这里改。
    /// </summary>
    public void Update(
        string code,
        string name,
        string specification = "",
        string unit = "",
        string? category = null,
        string? note = null)
    {
        ApplyDetails(code, name, specification, unit, category, note);
    }

    public void Activate()
    {
        IsActive = true;
    }

    public void Deactivate()
    {
        IsActive = false;
    }

    /// <summary>
    /// 被新的需求明细引用前必须确认档案仍启用。停用只影响新录入，
    /// 历史明细里的名称/规格快照不受影响。
    /// </summary>
    public void EnsureActive()
    {
        if (!IsActive)
            throw new BusinessException(PurchaseFlowErrors.MaterialInactive);
    }

    private void ApplyDetails(string code, string name, string specification, string unit,
        string? category, string? note)
    {
        // 编码是主数据唯一标识：统一 trim + 大写，避免同一编码因大小写/空格重复。
        Code = NormalizeCode(code);
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), maxLength: 100).Trim();
        Specification = string.IsNullOrWhiteSpace(specification)
            ? string.Empty
            : Check.Length(specification.Trim(), nameof(specification), maxLength: 100) ?? string.Empty;
        Unit = Check.NotNullOrWhiteSpace(unit, nameof(unit), maxLength: 20).Trim();
        Category = string.IsNullOrWhiteSpace(category)
            ? null
            : Check.Length(category.Trim(), nameof(category), maxLength: 50);
        Note = string.IsNullOrWhiteSpace(note)
            ? null
            : Check.Length(note.Trim(), nameof(note), maxLength: 500);
    }

    private static string NormalizeCode(string? code)
    {
        var normalized = code?.Trim().ToUpperInvariant() ?? string.Empty;
        if (normalized.Length == 0 || normalized.Length > 32 || !IsValidCode(normalized))
            throw new BusinessException(PurchaseFlowErrors.InvalidMaterialCode);
        return normalized;
    }

    private static bool IsValidCode(string code)
    {
        if (!IsUpperLetterOrDigit(code[0])) return false;
        for (var i = 1; i < code.Length; i++)
        {
            if (!IsUpperLetterOrDigit(code[i]) && code[i] != '-') return false;
        }
        return true;
    }

    private static bool IsUpperLetterOrDigit(char value) =>
        char.IsAsciiLetterUpper(value) || char.IsAsciiDigit(value);
}
