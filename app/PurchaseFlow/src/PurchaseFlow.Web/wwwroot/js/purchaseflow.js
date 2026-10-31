(() => {
  "use strict";
  const toggle = document.querySelector("[data-nav-toggle]");
  const nav = document.querySelector("#workspace-nav");
  const setNav = (open) => { document.body.classList.toggle("pf-nav-open", open); toggle?.setAttribute("aria-expanded", String(open)); toggle?.setAttribute("aria-label", open ? "收起主导航" : "展开主导航"); };
  toggle?.addEventListener("click", () => { const open = !document.body.classList.contains("pf-nav-open"); setNav(open); if(open) nav?.querySelector("a")?.focus(); });
  document.querySelector("[data-nav-overlay]")?.addEventListener("click", () => { setNav(false); toggle?.focus(); });
  document.addEventListener("keydown", event => {
    if(!document.body.classList.contains("pf-nav-open")) return;
    if(event.key === "Escape") { setNav(false); toggle?.focus(); }
    if(event.key === "Tab") { const links = Array.from(nav?.querySelectorAll("a") ?? []); const first = links[0], last = links.at(-1); if(event.shiftKey && document.activeElement === first) { event.preventDefault(); last?.focus(); } else if(!event.shiftKey && document.activeElement === last) { event.preventDefault(); first?.focus(); } }
  });
  window.matchMedia("(min-width:1024px)").addEventListener("change", event => { if(event.matches) setNav(false); });
  const currency = new Intl.NumberFormat("zh-CN", { style:"currency", currency:"CNY", minimumFractionDigits:2 });
  // P1-2：所有页面金额计算统一走 pf-money.js 的 BigInt 精确十进制运算，保证与后端
  // C# decimal + MidpointRounding.AwayFromZero 的“分”口径一致；只有该文件意外未加载时
  // 才退回旧的 Number 计算（并告警），避免整页合计静默变成 0。
  const pfMoney = window.pfMoney;
  if(!pfMoney) console.warn("pf-money.js 未加载，金额改用浮点兜底计算，可能与后端舍入口径不一致。");
  const toCents = (quantity, price) => pfMoney
    ? pfMoney.subtotalCents(quantity, price)
    : BigInt(Math.round((Number(quantity) || 0) * (Number(price) || 0) * 100));
  document.querySelectorAll("[data-decision-lab]").forEach(lab => {
    const input = lab.querySelector("[data-lab-price-weight]");
    const output = lab.querySelector("[data-lab-results]");
    if(!input || !output) return;
    const quotes = Array.from(lab.querySelectorAll("[data-lab-quote]")).map(el => ({id:el.dataset.quoteId, name:el.dataset.supplier ?? "供应商", amount:Number(el.dataset.amount), days:Number(el.dataset.days)})).filter(q => Number.isFinite(q.amount) && Number.isFinite(q.days));
    output.setAttribute("aria-live","polite");
    const render = () => {
      const weight = Math.max(0,Math.min(100, Number(input.value) || 0));
      lab.querySelector("[data-lab-price-weight-value]").textContent = String(weight);
      lab.querySelector("[data-lab-delivery-weight-value]").textContent = String(100-weight);
      output.replaceChildren();
      if(quotes.length < 2) { const empty=document.createElement("p"); empty.className="pf-lab-empty"; empty.textContent="录入至少两家报价后，即可比较不同权重下的方案。"; output.append(empty); return; }
      const minAmount=Math.min(...quotes.map(q=>q.amount)), maxAmount=Math.max(...quotes.map(q=>q.amount)), minDays=Math.min(...quotes.map(q=>q.days)), maxDays=Math.max(...quotes.map(q=>q.days));
      const normalize=(value,min,max)=>max===min?100:(max-value)/(max-min)*100;
      const ranked=quotes.map(q=>({...q, score:normalize(q.amount,minAmount,maxAmount)*weight/100+normalize(q.days,minDays,maxDays)*(100-weight)/100})).sort((a,b)=>b.score-a.score||a.amount-b.amount||a.days-b.days||a.id.localeCompare(b.id));
      let previousScore, previousRank;
      ranked.forEach((quote,index) => {
        const tie=previousScore !== undefined && Math.abs(previousScore-quote.score)<0.000001;
        const rank=tie?previousRank:index+1; previousRank=rank; previousScore=quote.score;
        const row=document.createElement("div");row.className="pf-lab-ranking-row";row.dataset.rankedQuote=quote.id;row.dataset.score=quote.score.toFixed(4);
        const badge=document.createElement("span");badge.className="pf-lab-rank";badge.textContent=String(rank);row.append(badge);
        const detail=document.createElement("div"), name=document.createElement("span"),facts=document.createElement("span"),track=document.createElement("div"),bar=document.createElement("span");
        name.className="pf-lab-name";name.textContent=quote.name;facts.className="pf-lab-facts";facts.textContent=`${currency.format(quote.amount)} · ${quote.days} 天交付`;
        track.className="pf-lab-track";track.setAttribute("role","meter");track.setAttribute("aria-label",`${quote.name} 参考评分`);track.setAttribute("aria-valuemin","0");track.setAttribute("aria-valuemax","100");track.setAttribute("aria-valuenow",quote.score.toFixed(1));bar.style.width=`${quote.score}%`;track.append(bar);detail.append(name,facts,track);row.append(detail);
        const score=document.createElement("span"), unit=document.createElement("small");score.className="pf-lab-score";score.textContent=quote.score.toFixed(1);unit.textContent="参考评分";score.append(unit);row.append(score);output.append(row);
      });
    };
    input.addEventListener("input",render);
    lab.querySelector("[data-lab-reset]")?.addEventListener("click",()=>{input.value="70";render();});
    render();
  });

  // Purchase line editor: adds and removes detail rows, keeps the field names
  // sequential so the model binder can map them, and shows the running total.
  document.querySelectorAll("[data-line-editor]").forEach(editor => {
    const rows = editor.querySelector("[data-line-rows]");
    const template = editor.querySelector("[data-line-template]");
    const addButton = editor.querySelector("[data-line-add]");
    const totalOutput = editor.querySelector("[data-line-total]");
    const prefix = editor.dataset.linePrefix || "Lines";
    if(!rows || !template) return;

    const renumber = () => {
      const all = Array.from(rows.querySelectorAll("[data-line-row]"));
      all.forEach((row,index) => {
        row.querySelectorAll("[data-line-field]").forEach(input => {
          input.name = `${prefix}[${index}].${input.dataset.lineField}`;
          input.id = `${prefix}-${index}-${input.dataset.lineField}`;
        });
        row.querySelectorAll("[data-line-remove]").forEach(button => { button.disabled = all.length === 1; });
      });
      return all;
    };

    // P1-2：逐行小计和总计都改在“分”上用 BigInt 累加，再用 Intl 货币格式展示。
    // 这样 2.01×0.50 这类边界会得到 1.01（旧 Math.round 写法只得 1.00），与后端落库金额一致。
    const recalc = () => {
      let totalCents = 0n;
      rows.querySelectorAll("[data-line-row]").forEach(row => {
        const quantity = row.querySelector("[data-line-quantity]")?.value ?? "";
        const price = row.querySelector("[data-line-price]")?.value ?? "";
        const cents = toCents(quantity, price);
        totalCents += cents;
        const output = row.querySelector("[data-line-subtotal]");
        if(output) output.textContent = currency.format(Number(cents) / 100);
      });
      if(totalOutput) totalOutput.textContent = currency.format(Number(totalCents) / 100);
    };

    rows.addEventListener("input", recalc);
    rows.addEventListener("click", event => {
      const button = event.target.closest("[data-line-remove]");
      if(!button) return;
      event.preventDefault();
      const row = button.closest("[data-line-row]");
      if(!row) return;
      const all = Array.from(rows.querySelectorAll("[data-line-row]"));
      if(all.length === 1) { row.querySelectorAll("input").forEach(input => { input.value = ""; }); }
      else { row.remove(); }
      renumber();
      recalc();
    });
    addButton?.addEventListener("click", () => {
      rows.append(template.content.cloneNode(true));
      renumber();
      recalc();
      rows.querySelector("[data-line-row]:last-child [data-line-item]")?.focus();
    });
    renumber();
    recalc();
  });

  // P0-1：报价逐项录入——每个需求明细行输入该供应商的报价单价，页面实时用 pf-money
  // 以“分”BigInt 汇总出“报价合计”。合计口径与后端“报价总额 = 各报价明细小计之和”一致，
  // 用户不需要也不允许手填报价总额，从入口避免“总额一套数、明细另一套数”。
  document.querySelectorAll("[data-quote-editor]").forEach(editor => {
    const rows = Array.from(editor.querySelectorAll("[data-quote-line]"));
    const totalOutput = editor.querySelector("[data-quote-total]");
    if(rows.length === 0) return;
    const recalc = () => {
      let totalCents = 0n;
      rows.forEach(row => {
        // 数量是服务端渲染的只读快照，放在 data 属性里，避免用户改动数量；
        // 单价来自本行输入框，两者都交给 pf-money 做精确十进制乘法。
        const quantity = row.dataset.quoteQuantity ?? "";
        const price = row.querySelector("[data-quote-price]")?.value ?? "";
        const cents = toCents(quantity, price);
        totalCents += cents;
        const output = row.querySelector("[data-quote-subtotal]");
        if(output) output.textContent = currency.format(Number(cents) / 100);
      });
      if(totalOutput) totalOutput.textContent = currency.format(Number(totalCents) / 100);
    };
    editor.addEventListener("input", recalc);
    recalc();
  });
})();
