/*
 * pf-money.js —— 前后端统一的精确十进制金额工具（P1-2）。
 *
 * 为什么新增这个文件：页面原来用 `Math.round(quantity * price * 100) / 100` 计算小计，
 * 而 JS 的 IEEE-754 双精度会让 2.01 * 0.50 = 1.004999…，再 Math.round 得到 1.00；
 * C# 侧用 decimal + Math.Round(..., 2, MidpointRounding.AwayFromZero) 得到 1.01。
 * 两端相差 1 分，页面展示/合计与后端落库金额对不上，所以这里把十进制文本拆成
 * BigInt 的“有效数字 + 小数位数”，用整数乘法求精确积，再按“四舍五入、0.5 远离 0”
 * 取舍到“分”，与后端 decimal 口径逐位一致。
 *
 * 同时把逻辑收敛到一个 UMD 文件：浏览器挂 window.pfMoney 给 purchaseflow.js 用，
 * Node 下可 require 出来做 JS 与 C# 的边界值交叉验证（验收要求）。
 */
(function (root, factory) {
  if (typeof module === "object" && module.exports) {
    // Node 环境：供交叉验证脚本 require。
    module.exports = factory();
  } else {
    // 浏览器环境：purchaseflow.js 通过 window.pfMoney 访问。
    root.pfMoney = factory();
  }
})(typeof globalThis !== "undefined" ? globalThis : this, function () {
  "use strict";

  // 允许的十进制文本：可选正负号、可选整数部分、可选小数部分、可选科学计数法指数。
  // type=number 的输入框在部分浏览器会给出 "1e3" 这类文本，因此这里一并支持。
  const DECIMAL_PATTERN = /^([+-])?(\d*)(?:\.(\d*))?(?:[eE]([+-]?\d+))?$/;

  // 归零对象：非法/空输入统一按 0 处理，与页面原来 Number(...) || 0 的口径一致，
  // 避免用户误输入让脚本抛异常、导致整页合计不再刷新。
  const ZERO = { sign: 1, digits: 0n, scale: 0 };

  /**
   * 把十进制文本解析成 { sign: 1|-1, digits: BigInt（绝对值的有效数字）, scale: 小数位数 }。
   * 例如 "2.01" -> { sign: 1, digits: 201n, scale: 2 }；"-0.50" -> { sign: -1, digits: 50n, scale: 2 }。
   * 空串、空白、非数字文本、超大指数一律返回 0。
   */
  function parseDecimal(text) {
    if (text === null || text === undefined) return ZERO;
    const raw = String(text).trim();
    if (raw === "") return ZERO;
    const match = DECIMAL_PATTERN.exec(raw);
    if (!match) return ZERO;

    const integerPart = match[2] || "";
    const fractionPart = match[3] || "";
    if (integerPart === "" && fractionPart === "") return ZERO;

    const exponent = match[4] ? Number(match[4]) : 0;
    // 指数没有业务意义且过大会构造出巨型 BigInt，直接按非法输入处理。
    if (!Number.isSafeInteger(exponent) || Math.abs(exponent) > 1000) return ZERO;

    let digits = BigInt(integerPart + fractionPart);
    if (digits === 0n) return ZERO;

    // 科学计数法：指数为正时有效数字放大、小数位减少；指数为负时小数位增加。
    let scale = fractionPart.length - exponent;
    if (scale < 0) {
      digits *= 10n ** BigInt(-scale);
      scale = 0;
    }
    return { sign: match[1] === "-" ? -1 : 1, digits, scale };
  }

  /**
   * 按“四舍五入、0.5 远离 0”把 numerator / denominator 取整（denominator 必须为正）。
   * 这是 C# MidpointRounding.AwayFromZero 的等价实现：余数达到除数一半就向远离 0 的方向进一位。
   */
  function divideHalfAwayFromZero(numerator, denominator) {
    const negative = numerator < 0n;
    const absolute = negative ? -numerator : numerator;
    let quotient = absolute / denominator;
    const remainder = absolute % denominator;
    if (remainder * 2n >= denominator) quotient += 1n;
    return negative ? -quotient : quotient;
  }

  /** 把“有效数字 × 10^-scale”的精确积换算成整数“分”，第 3 位小数按 half-away-from-zero 处理。 */
  function toCents(productDigits, productScale) {
    const shift = 2 - productScale;
    if (shift >= 0) return productDigits * 10n ** BigInt(shift);
    return divideHalfAwayFromZero(productDigits, 10n ** BigInt(-shift));
  }

  /**
   * 宽松数量文本 + 单价文本 -> 整数“分”（BigInt）。
   * 页面逐行小计与总计都用它累加，避免浮点误差；非法/空输入按 0 处理。
   */
  function subtotalCents(quantityText, priceText) {
    const quantity = parseDecimal(quantityText);
    const price = parseDecimal(priceText);
    let digits = quantity.digits * price.digits;
    if (quantity.sign * price.sign < 0) digits = -digits;
    return toCents(digits, quantity.scale + price.scale);
  }

  /**
   * 宽松数量文本 + 单价文本 -> 数值（元），精确相乘并舍入到 2 位后再转 number。
   * 提供给只需要展示/简单比较的调用方；参与汇总时请优先用 subtotalCents 以保持精确。
   */
  function subtotal(quantityText, priceText) {
    return Number(subtotalCents(quantityText, priceText)) / 100;
  }

  /**
   * 整数“分” -> 两位小数字符串，例如 101n -> "1.01"、-5n -> "-0.05"、0n -> "0.00"。
   * 只做字符串拼接，不经过浮点，保证展示值就是精确分换算出来的值。
   */
  function formatCents(cents) {
    let value;
    if (typeof cents === "bigint") {
      value = cents;
    } else {
      const numeric = Number(cents);
      value = BigInt(Number.isFinite(numeric) ? Math.trunc(numeric) : 0);
    }
    const negative = value < 0n;
    const absolute = negative ? -value : value;
    const integerPart = absolute / 100n;
    const fractionPart = absolute % 100n;
    return (negative ? "-" : "") + integerPart + "." + String(fractionPart).padStart(2, "0");
  }

  // 显式导出四个约定方法，另外把取整函数暴露为内部属性便于交叉验证时构造用例。
  return {
    parseDecimal,
    subtotal,
    subtotalCents,
    formatCents
  };
});
