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
})();
