using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace PurchaseFlow.Purchasing;

/// <summary>
/// 第二轮第 1 批：供应商档案（主数据）。
///
/// 为什么单独建档案：报价/订单原来只存供应商名称文本，名称拼写不一致、无法复用、
/// 也无法统一维护账期与评级。档案让“选择供应商”成为可管理的动作；
/// 但单据上仍然保存名称快照（SupplierQuote.SupplierName / PurchaseOrder.SupplierName），
/// 因此档案改名不会改写历史单据。
///
/// 停用语义：档案停用（IsActive=false）后不能再被新的报价/订单引用
/// （应用层调用 EnsureActive 拦截），但历史单据及其名称快照完全保留。
/// </summary>
public class Supplier : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    /// <summary>供应商编码：trim 后转大写，≤32，格式 ^[A-Z0-9][A-Z0-9-]*$。</summary>
    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;
    public string? ContactName { get; private set; }
    public string? ContactPhone { get; private set; }

    /// <summary>账期（天）。规格明确范围 0–365，超出范围属于金额/商务参数错误。</summary>
    public int PaymentTermsDays { get; private set; }

    /// <summary>供应商评级：仅展示用的简单枚举（C 默认、A 最优），不做自动评级算法。</summary>
    public SupplierRating Rating { get; private set; }

    public bool IsActive { get; private set; }
    public string? Note { get; private set; }

    protected Supplier() { }

    public Supplier(
        Guid id,
        Guid? tenantId,
        string code,
        string name,
        string? contactName = null,
        string? contactPhone = null,
        int paymentTermsDays = 0,
        SupplierRating rating = SupplierRating.C,
        string? note = null)
        : base(id)
    {
        TenantId = tenantId;
        ApplyDetails(code, name, contactName, contactPhone, paymentTermsDays, rating, note);
        // 新建档案默认启用：主数据录入后应立即可被报价/需求引用，停用是显式动作。
        IsActive = true;
    }

    /// <summary>
    /// 修改档案。Code 变更同样走统一的规范化与校验（不能绕过格式/长度规则），
    /// 名称、联系人、账期、评级与备注同步刷新；启停用状态不在这里改（用 Activate/Deactivate）。
    /// </summary>
    public void Update(
        string code,
        string name,
        string? contactName = null,
        string? contactPhone = null,
        int paymentTermsDays = 0,
        SupplierRating rating = SupplierRating.C,
        string? note = null)
    {
        ApplyDetails(code, name, contactName, contactPhone, paymentTermsDays, rating, note);
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
    /// 被新报价/订单引用前必须确认档案仍启用。停用只影响新的业务决策，
    /// 历史单据里的名称快照不受影响。
    /// </summary>
    public void EnsureActive()
    {
        if (!IsActive)
            throw new BusinessException(PurchaseFlowErrors.SupplierInactive);
    }

    private void ApplyDetails(
        string code,
        string name,
        string? contactName,
        string? contactPhone,
        int paymentTermsDays,
        SupplierRating rating,
        string? note)
    {
        // 编码是主数据唯一标识：统一 trim + 大写，避免同一编码因大小写/空格重复。
        Code = NormalizeCode(code);
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), maxLength: 100).Trim();
        ContactName = string.IsNullOrWhiteSpace(contactName)
            ? null
            : Check.Length(contactName.Trim(), nameof(contactName), maxLength: 50);
        ContactPhone = string.IsNullOrWhiteSpace(contactPhone)
            ? null
            : Check.Length(contactPhone.Trim(), nameof(contactPhone), maxLength: 30);
        // 账期超出 0–365 是商务参数错误，复用 InvalidAmount（不新增错误码）。
        if (paymentTermsDays < 0 || paymentTermsDays > 365)
            throw new BusinessException(PurchaseFlowErrors.InvalidAmount);
        PaymentTermsDays = paymentTermsDays;
        Rating = rating;
        Note = string.IsNullOrWhiteSpace(note)
            ? null
            : Check.Length(note.Trim(), nameof(note), maxLength: 500);
    }

    /// <summary>
    /// 编码规范化与格式校验：^[A-Z0-9][A-Z0-9-]*$，长度 1–32。
    /// 单独抽出来是为了让构造与 Update 共用同一规则，避免两条路径校验口径不一致。
    /// </summary>
    private static string NormalizeCode(string? code)
    {
        var normalized = code?.Trim().ToUpperInvariant() ?? string.Empty;
        if (normalized.Length == 0 || normalized.Length > 32 || !IsValidCode(normalized))
            throw new BusinessException(PurchaseFlowErrors.InvalidSupplierCode);
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
