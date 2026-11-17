/* Query Doctor AI — detalle de consulta: SQL, series por plan, hallazgos y score (S2-09).
 * Capa de presentación: solo pinta datos de la API con textContent y createElementNS (CSP estricta). */
(function () {
  "use strict";

  var SVG_NS = "http://www.w3.org/2000/svg";

  var SEVERITY_TONE = {
    ALTA: ["Alta", "danger"], High: ["Alta", "danger"], Alta: ["Alta", "danger"],
    MEDIA: ["Media", "warn"], Medium: ["Media", "warn"], Media: ["Media", "warn"],
    BAJA: ["Baja", "neutral"], Low: ["Baja", "neutral"], Baja: ["Baja", "neutral"],
    INFORMATIVA: ["Informativa", "neutral"], Informativa: ["Informativa", "neutral"]
  };

  var BAND_TONE = {
    Critica: "danger",
    Atencion: "warn",
    Saludable: "ok"
  };

  var $ = function (id) { return document.getElementById(id); };

  function el(tag, className, text) {
    var node = document.createElement(tag);
    if (className) { node.className = className; }
    if (text !== undefined && text !== null) { node.textContent = text; }
    return node;
  }

  function svgEl(tag, attrs) {
    var node = document.createElementNS(SVG_NS, tag);
    Object.keys(attrs || {}).forEach(function (key) {
      node.setAttribute(key, String(attrs[key]));
    });
    return node;
  }

  function badge(text, tone) {
    var node = el("span", "badge", text);
    node.setAttribute("data-tone", tone || "neutral");
    return node;
  }

  function goToLogin() {
    var back = window.location.pathname + window.location.search;
    window.location.replace("/login?returnUrl=" + encodeURIComponent(back));
  }

  function showNotice(message) {
    var node = $("notice");
    node.textContent = message;
    node.className = "notice is-visible";
  }

  function hideNotice() {
    var node = $("notice");
    node.textContent = "";
    node.className = "notice";
  }

  function renderEmpty(title, text) {
    var root = $("detail-root");
    root.replaceChildren();
    var box = el("div", "empty");
    box.appendChild(el("h2", null, title));
    box.appendChild(el("p", null, text));
    root.appendChild(box);
  }

  function formatNum(value, decimals) {
    if (typeof value !== "number" || isNaN(value)) { return "—"; }
    return value.toLocaleString("es-CO", {
      minimumFractionDigits: decimals,
      maximumFractionDigits: decimals
    });
  }

  function formatDateTime(iso) {
    if (!iso) { return "—"; }
    var d = new Date(iso);
    if (isNaN(d.getTime())) { return "—"; }
    return d.toLocaleString("es-CO", { dateStyle: "short", timeStyle: "short" });
  }

  function formatShortTime(iso) {
    if (!iso) { return ""; }
    var d = new Date(iso);
    if (isNaN(d.getTime())) { return ""; }
    return d.toLocaleTimeString("es-CO", { hour: "2-digit", minute: "2-digit" });
  }

  function formatWindowTick(iso, windowKind) {
    if (!iso) { return ""; }
    var d = new Date(iso);
    if (isNaN(d.getTime())) { return ""; }
    if (windowKind === "24h") {
      return d.toLocaleTimeString("es-CO", { hour: "2-digit", minute: "2-digit" });
    }
    var datePart = d.toLocaleDateString("es-CO", { day: "2-digit", month: "2-digit" });
    if (windowKind === "7d") {
      return datePart + " " + d.toLocaleTimeString("es-CO", { hour: "2-digit", minute: "2-digit" });
    }
    return datePart;
  }

  function buildPanelHead(title, subtitle, rightEl) {
    var head = el("div", "panel-head");
    var titleCol = el("div", "panel-head-title");
    titleCol.appendChild(el("h2", null, title));
    if (subtitle) {
      titleCol.appendChild(el("p", "panel-note", subtitle));
    }
    head.appendChild(titleCol);
    if (rightEl) {
      var rightWrap = el("div", "panel-head-actions");
      rightWrap.appendChild(rightEl);
      head.appendChild(rightWrap);
    }
    return head;
  }

  function loadSession() {
    if (window.QDCore) {
      return window.QDCore.mountShell({ activeNav: "queries" }).then(function (ctx) {
        return ctx ? ctx.me : null;
      });
    }
    return fetch("/api/auth/me", { credentials: "same-origin" }).then(function (response) {
      if (response.status === 401) { goToLogin(); return null; }
      if (!response.ok) { throw new Error("me " + response.status); }
      return response.json();
    }).then(function (me) {
      if (!me) { return null; }
      if (me.mustChangePassword) { window.location.replace("/cambiar-contrasena"); return null; }
      $("who-user").textContent = me.userName;
      $("who-role").textContent = me.roles && me.roles.length ? me.roles.join(", ") : "Sin rol";
      $("main").hidden = false;
      return me;
    });
  }

  // ---------- Render: Score ----------
  function renderScorePanel(score) {
    var section = el("section", "panel panel-score");

    var headMeta = null;
    if (score) {
      headMeta = el("div", "score-head-badges");
      headMeta.appendChild(badge(score.bandLabel || score.band, BAND_TONE[score.band] || "neutral"));
      headMeta.appendChild(badge("Puesto #" + score.rankInWindow + " de " + score.totalQueriesInWindow, "neutral"));
    }
    section.appendChild(buildPanelHead(
      "Query Health Score y desglose",
      score ? "Calculado el " + formatDateTime(score.calculatedAt) : "Pendiente de cálculo",
      headMeta
    ));

    if (!score) {
      section.appendChild(el("p", "panel-note", "Esta consulta aún no tiene un Query Health Score calculado en la última ventana."));
      return section;
    }

    var hero = el("div", "score-hero");

    var dial = el("div", "score-dial");
    var gaugeRow = el("div", "score-gauge-row");
    var scoreVal = typeof score.score === "number" ? Math.max(0, Math.min(100, score.score)) : 0;
    var r = 30;
    var circ = 2 * Math.PI * r;
    var gaugeSvg = svgEl("svg", { class: "score-gauge-svg", viewBox: "0 0 76 76", "aria-hidden": "true" });
    gaugeSvg.appendChild(svgEl("circle", { class: "score-gauge-track", cx: "38", cy: "38", r: r }));
    var arc = svgEl("circle", {
      class: "score-gauge-arc",
      cx: "38",
      cy: "38",
      r: r,
      "stroke-dasharray": circ.toFixed(1),
      "stroke-dashoffset": (circ * (1 - scoreVal / 100)).toFixed(1)
    });
    arc.setAttribute("data-tone", BAND_TONE[score.band] || "neutral");
    gaugeSvg.appendChild(arc);
    gaugeRow.appendChild(gaugeSvg);

    var numWrap = el("div");
    numWrap.appendChild(el("span", "score-number", formatNum(score.score, 1)));
    numWrap.appendChild(el("span", "score-scale-tag", "Escala 0–100 · Prioridad P(q): " + formatNum(score.priority, 1)));
    gaugeRow.appendChild(numWrap);
    dial.appendChild(gaugeRow);

    dial.appendChild(el("p", "scale-direction", "Escala: " + score.scaleDirection + "."));
    dial.appendChild(el("p", "score-rank", "Posición relativa #" + score.rankInWindow + " en la ventana"));
    hero.appendChild(dial);

    var meta = el("div", "score-meta-col");
    var kpis = el("div", "kpi-grid");
    [
      ["Impacto en carga I(q)", formatNum(score.impactComponent, 3)],
      ["Severidad hallazgos S(q)", formatNum(score.severityComponent, 3)],
      ["Peso base α", formatNum(score.alphaUsed, 2)],
      ["Consumo ponderado", formatNum(score.weightedConsumptionPct, 1) + " %"],
      ["Participación CPU", formatNum(score.cpuShare * 100, 1) + " %"],
      ["Participación duración", formatNum(score.durationShare * 100, 1) + " %"],
      ["Participación lecturas", formatNum(score.logicalReadsShare * 100, 1) + " %"],
      ["Ejecuciones ventana", formatNum(score.totalExecutions, 0)]
    ].forEach(function (pair) {
      var card = el("div", "kpi-card");
      card.appendChild(el("span", null, pair[0]));
      card.appendChild(el("strong", null, pair[1]));
      kpis.appendChild(card);
    });
    meta.appendChild(kpis);
    meta.appendChild(el("p", "disclaimer", score.usageDisclaimer));
    hero.appendChild(meta);
    section.appendChild(hero);

    var subblocks = el("div", "score-subblocks");

    // Desglose de hallazgos contribuyentes a S(q)
    var contribBox = el("div", "subpanel-card");
    contribBox.appendChild(el("h3", "sub-title", "Hallazgos que contribuyen a S(q)"));
    var contribs = score.contributingFindings || [];
    if (!contribs.length) {
      contribBox.appendChild(el("p", "panel-note", "Sin hallazgos activos en S(q) para este cálculo (S(q) = 0)."));
    } else {
      var wrap = el("div", "table-wrap");
      var table = el("table", "table table-compact");
      var thead = el("thead");
      var hrow = el("tr");
      ["Código", "Regla", "Severidad", "Evidencia", "Sev.", "Conf.", "Aporte"].forEach(function (col) {
        var th = el("th", null, col);
        th.setAttribute("scope", "col");
        hrow.appendChild(th);
      });
      thead.appendChild(hrow);
      table.appendChild(thead);

      var tbody = el("tbody");
      contribs.forEach(function (c) {
        var tr = el("tr");
        tr.appendChild(el("td", "mono", c.finding_code));
        tr.appendChild(el("td", "mono", c.rule_id));
        tr.appendChild(el("td", null, c.severity));
        tr.appendChild(el("td", null, c.evidence_class));
        tr.appendChild(el("td", "mono tabular", formatNum(c.severity_weight, 2)));
        tr.appendChild(el("td", "mono tabular", formatNum(c.confidence_weight, 2)));
        tr.appendChild(el("td", "mono tabular", formatNum(c.combined_weight, 3)));
        tbody.appendChild(tr);
      });
      table.appendChild(tbody);
      wrap.appendChild(table);
      contribBox.appendChild(wrap);
    }
    subblocks.appendChild(contribBox);

    // Historial de scores (RF-SCO-05)
    var history = score.history || [];
    if (history.length > 1) {
      var histBox = el("div", "subpanel-card");
      var details = el("details", "details-toggle");
      details.appendChild(el("summary", null, "Historial de cálculos (" + history.length + " puntos)"));
      var hWrap = el("div", "table-wrap");
      var hTable = el("table", "table table-compact");
      var hHead = el("thead");
      var hTr = el("tr");
      ["Fecha", "Score", "I(q)", "S(q)", "Consumo %"].forEach(function (col) {
        var th = el("th", null, col);
        th.setAttribute("scope", "col");
        hTr.appendChild(th);
      });
      hHead.appendChild(hTr);
      hTable.appendChild(hHead);
      var hBody = el("tbody");
      history.forEach(function (h) {
        var row = el("tr");
        row.appendChild(el("td", null, formatDateTime(h.calculatedAt)));
        row.appendChild(el("td", "mono tabular", formatNum(h.score, 2)));
        row.appendChild(el("td", "mono tabular", formatNum(h.impactComponent, 3)));
        row.appendChild(el("td", "mono tabular", formatNum(h.severityComponent, 3)));
        row.appendChild(el("td", "mono tabular", formatNum(h.weightedConsumptionPct, 1) + " %"));
        hBody.appendChild(row);
      });
      hTable.appendChild(hBody);
      hWrap.appendChild(hTable);
      details.appendChild(hWrap);
      histBox.appendChild(details);
      subblocks.appendChild(histBox);
    }

    section.appendChild(subblocks);
    return section;
  }

  // ---------- Render: SQL ----------
  function renderSqlPanel(detail) {
    var section = el("section", "panel panel-sql");
    var actions = el("div", "sql-head-actions");
    var canShowSql = !detail.sqlMaskingFailure && !detail.sqlExpired && Boolean(detail.sqlText);
    var sqlText = detail.sqlText || "";
    var isLongSql = canShowSql && (sqlText.split("\n").length > 8 || sqlText.length > 480);

    var pre = null;
    if (isLongSql) {
      var expandBtn = el("button", "btn btn-sm btn-secondary", "Expandir SQL");
      expandBtn.type = "button";
      expandBtn.setAttribute("aria-expanded", "false");
      expandBtn.addEventListener("click", function () {
        if (!pre) { return; }
        var expanded = pre.classList.toggle("is-expanded");
        expandBtn.textContent = expanded ? "Contraer SQL" : "Expandir SQL";
        expandBtn.setAttribute("aria-expanded", expanded ? "true" : "false");
      });
      actions.appendChild(expandBtn);
    }

    if (canShowSql && window.QDCore) {
      var copyBtn = el("button", "btn btn-sm btn-secondary", "Copiar SQL");
      copyBtn.type = "button";
      copyBtn.addEventListener("click", function () {
        window.QDCore.copyToClipboard(detail.sqlText).then(function () {
          window.QDCore.showToast("SQL enmascarado copiado al portapapeles.", "ok");
        });
      });
      actions.appendChild(copyBtn);
    }

    section.appendChild(buildPanelHead(
      "Texto SQL enmascarado",
      "Literales reemplazados de forma determinista antes de almacenarse o mostrarse",
      actions.childNodes.length ? actions : null
    ));

    if (detail.sqlMaskingFailure) {
      section.appendChild(el("p", "panel-note", "No se pudo enmascarar el texto SQL de forma segura (" + detail.sqlMaskingFailure + ")."));
    } else if (detail.sqlExpired || !detail.sqlText) {
      section.appendChild(el("p", "panel-note", "Evidencia expirada o texto SQL no disponible."));
    } else {
      pre = el("pre", "sql-code" + (isLongSql ? " is-compact" : ""));
      pre.appendChild(el("code", null, detail.sqlText));
      section.appendChild(pre);
    }

    var findings = detail.findings || [];
    if (findings.length) {
      var strip = el("div", "sql-findings-strip");
      strip.appendChild(el("span", "sql-findings-label", "Señales detectadas (" + findings.length + "):"));
      var chips = el("div", "node-jump-list");
      findings.forEach(function (f) {
        var sev = SEVERITY_TONE[f.severity];
        var btn = el("button", "node-jump-btn", f.ruleId + " · " + (sev ? sev[0] : f.severity));
        btn.type = "button";
        btn.title = f.ruleName || f.ruleId;
        btn.addEventListener("click", function () {
          focusFindingCard(f.findingCode || f.id);
        });
        chips.appendChild(btn);
      });
      strip.appendChild(chips);
      var toScoreBtn = el("button", "btn btn-sm btn-secondary sql-score-link", "Score y hallazgos →");
      toScoreBtn.type = "button";
      toScoreBtn.addEventListener("click", function () { activateDetailTab("findings"); });
      strip.appendChild(toScoreBtn);
      section.appendChild(strip);
    }

    return section;
  }

  // ---------- Render: Series por plan interactivas ----------
  function formatChartValue(val, unitKind, decimals) {
    if (typeof val !== "number" || isNaN(val)) { return "—"; }
    if (unitKind === "ms") {
      if (Math.abs(val) >= 1000) { return formatNum(val / 1000, 2) + " s"; }
      return formatNum(val, decimals) + " ms";
    }
    if (unitKind === "count" && window.QDPlanGraph) {
      return window.QDPlanGraph.formatCompactCount(val);
    }
    return formatNum(val, decimals);
  }

  var SERIES_METRICS = [
    { id: "duration", title: "Duración promedio", selector: function (p) { return p.avgDurationMs; }, decimals: 2, unitKind: "ms" },
    { id: "cpu", title: "CPU promedio", selector: function (p) { return p.avgCpuTimeMs; }, decimals: 2, unitKind: "ms" },
    { id: "reads", title: "Lecturas lógicas promedio", selector: function (p) { return p.avgLogicalIoReads; }, decimals: 1, unitKind: "count" },
    { id: "execs", title: "Ejecuciones por intervalo", selector: function (p) { return p.executionCount; }, decimals: 0, unitKind: "count" }
  ];

  function computeChartSummary(points, valueSelector, activePlanFilter) {
    var scoped = activePlanFilter
      ? points.filter(function (p) { return p.planId === activePlanFilter; })
      : points;
    var vals = scoped.map(valueSelector).filter(function (v) { return typeof v === "number" && !isNaN(v); });
    if (!vals.length) { return { count: 0, maxVal: null, avgVal: null }; }
    var sum = vals.reduce(function (acc, v) { return acc + v; }, 0);
    return {
      count: vals.length,
      maxVal: Math.max.apply(null, vals),
      avgVal: sum / vals.length
    };
  }

  function appendChartAxes(svg, cfg, summary) {
    var yFracs = cfg.ySteps === 5 ? [0, 0.25, 0.5, 0.75, 1] : [0, 0.33, 0.67, 1];
    yFracs.forEach(function (ratio) {
      var y = cfg.padTop + cfg.plotH * (1 - ratio);
      svg.appendChild(svgEl("line", {
        "class": "chart-grid-line",
        "x1": cfg.padLeft, "y1": y, "x2": cfg.width - cfg.padRight, "y2": y
      }));
      var lbl = svgEl("text", {
        "class": "chart-axis-label",
        "x": cfg.padLeft - 8, "y": y + 3, "text-anchor": "end"
      });
      lbl.textContent = formatChartValue(cfg.maxV * ratio, cfg.unitKind, cfg.decimals);
      svg.appendChild(lbl);
    });

    if (summary.avgVal !== null && summary.count > 1) {
      var avgY = cfg.padTop + cfg.plotH - (summary.avgVal / cfg.maxV) * cfg.plotH;
      svg.appendChild(svgEl("line", {
        "class": "chart-avg-line",
        "x1": cfg.padLeft, "y1": avgY.toFixed(1), "x2": cfg.width - cfg.padRight, "y2": avgY.toFixed(1)
      }));
    }

    svg.appendChild(svgEl("line", {
      "class": "chart-axis-line",
      "x1": cfg.padLeft, "y1": cfg.padTop + cfg.plotH, "x2": cfg.width - cfg.padRight, "y2": cfg.padTop + cfg.plotH
    }));
    svg.appendChild(svgEl("line", {
      "class": "chart-axis-line",
      "x1": cfg.padLeft, "y1": cfg.padTop, "x2": cfg.padLeft, "y2": cfg.padTop + cfg.plotH
    }));

    var xFracs = cfg.singleTimestamp
      ? [0.5]
      : (cfg.xSteps === 6 ? [0, 0.2, 0.4, 0.6, 0.8, 1] : [0, 0.33, 0.67, 1]);
    xFracs.forEach(function (frac) {
      var tTick = cfg.singleTimestamp ? cfg.minT : cfg.minT + (cfg.maxT - cfg.minT) * frac;
      var xTick = cfg.padLeft + cfg.plotW * frac;
      var anchor = cfg.singleTimestamp ? "middle" : (frac === 0 ? "start" : (frac === 1 ? "end" : "middle"));
      var xLbl = svgEl("text", { "class": "chart-axis-label", "x": xTick, "y": cfg.height - 10, "text-anchor": anchor });
      xLbl.textContent = formatWindowTick(new Date(tTick).toISOString(), cfg.windowKind);
      svg.appendChild(xLbl);
    });
  }

  function appendPlanSeriesLines(svg, points, cfg, planClassMap, activePlanFilter, guideLine, readout) {
    var byPlan = {};
    points.forEach(function (p) {
      if (!byPlan[p.planId]) { byPlan[p.planId] = []; }
      byPlan[p.planId].push(p);
    });

    Object.keys(byPlan).forEach(function (planId) {
      var cls = planClassMap[planId] || "series-0";
      var dimmed = activePlanFilter && activePlanFilter !== planId;
      var planPoints = byPlan[planId].slice().sort(function (a, b) {
        return new Date(a.intervalStart).getTime() - new Date(b.intervalStart).getTime();
      });

      var coords = planPoints.map(function (p) {
        var t = new Date(p.intervalStart).getTime();
        var x = cfg.singleTimestamp ? (cfg.padLeft + cfg.plotW * 0.5) : (cfg.padLeft + ((t - cfg.minT) / (cfg.maxT - cfg.minT)) * cfg.plotW);
        var y = cfg.padTop + cfg.plotH - (cfg.valueSelector(p) / cfg.maxV) * cfg.plotH;
        return { x: x.toFixed(1), y: y.toFixed(1), point: p };
      });

      if (coords.length > 1) {
        var areaPoints = coords.map(function (c) { return c.x + "," + c.y; }).join(" ") +
          " " + coords[coords.length - 1].x + "," + (cfg.padTop + cfg.plotH) +
          " " + coords[0].x + "," + (cfg.padTop + cfg.plotH);
        svg.appendChild(svgEl("polygon", {
          "class": "chart-area " + cls + (dimmed ? " chart-series-dim" : ""),
          "points": areaPoints
        }));
        svg.appendChild(svgEl("polyline", {
          "class": "chart-line " + cls + (dimmed ? " chart-series-dim" : ""),
          "points": coords.map(function (c) { return c.x + "," + c.y; }).join(" ")
        }));
      }

      coords.forEach(function (c) {
        if (coords.length === 1) {
          svg.appendChild(svgEl("line", {
            "class": "chart-single-stem " + cls + (dimmed ? " chart-series-dim" : ""),
            "x1": c.x, "y1": c.y, "x2": c.x, "y2": cfg.padTop + cfg.plotH
          }));
        }
        var circle = svgEl("circle", {
          "class": "chart-dot " + cls + (dimmed ? " chart-series-dim" : ""),
          "cx": c.x,
          "cy": c.y,
          "r": coords.length === 1 ? 5.5 : (cfg.expanded ? 5 : 4.2),
          "tabindex": "0"
        });
        var summaryText = "Plan #" + c.point.queryStorePlanId + " · " + formatDateTime(c.point.intervalStart) +
          " → " + formatChartValue(cfg.valueSelector(c.point), cfg.unitKind, cfg.decimals);
        var tip = svgEl("title");
        tip.textContent = summaryText;
        circle.appendChild(tip);

        var showHover = function () {
          guideLine.setAttribute("x1", c.x);
          guideLine.setAttribute("x2", c.x);
          guideLine.classList.add("is-visible");
          readout.textContent = summaryText;
        };
        circle.addEventListener("mouseenter", showHover);
        circle.addEventListener("focus", showHover);
        svg.appendChild(circle);
      });
    });
  }

  function buildPlanLegendPills(container, plans, activePlanFilter, onToggle) {
    container.replaceChildren();
    if (!plans.length) { return; }
    plans.forEach(function (plan, idx) {
      var cls = "series-" + (idx % 4);
      var btn = el("button", "legend-pill" + (activePlanFilter && activePlanFilter !== plan.planId ? " is-dimmed" : ""));
      btn.type = "button";
      btn.setAttribute("aria-pressed", activePlanFilter === plan.planId ? "true" : "false");
      btn.appendChild(el("span", "legend-swatch " + cls));
      var label = "Plan #" + plan.queryStorePlanId + (plan.queryPlanHash ? " (" + plan.queryPlanHash.slice(0, 8) + ")" : "");
      btn.appendChild(el("span", null, label));
      btn.addEventListener("click", function () { onToggle(plan.planId); });
      container.appendChild(btn);
    });
  }

  function buildSvgMetricChart(metricSpec, points, planClassMap, activePlanFilter, windowKind, chartOpts) {
    var opts = chartOpts || {};
    var expanded = Boolean(opts.expanded);
    var card = el("div", expanded ? "chart-card chart-card-expanded" : "chart-card");
    var head = el("div", "chart-card-head");
    head.appendChild(el("h3", null, metricSpec.title));

    var summary = computeChartSummary(points, metricSpec.selector, activePlanFilter);
    var headRight = el("div", "chart-card-stats");
    if (summary.avgVal !== null && summary.maxVal !== null) {
      headRight.appendChild(el("span", "chart-stat-pill", "Prom: " + formatChartValue(summary.avgVal, metricSpec.unitKind, metricSpec.decimals)));
      headRight.appendChild(el("span", "chart-stat-pill", "Máx: " + formatChartValue(summary.maxVal, metricSpec.unitKind, metricSpec.decimals)));
    }
    if (!expanded && typeof opts.onExpand === "function") {
      var expandBtn = el("button", "plan-btn chart-expand-btn", "Ampliar ⤢");
      expandBtn.type = "button";
      expandBtn.setAttribute("aria-label", "Ampliar gráfico de " + metricSpec.title);
      expandBtn.addEventListener("click", function () { opts.onExpand(metricSpec.id, expandBtn); });
      headRight.appendChild(expandBtn);
    }
    head.appendChild(headRight);
    card.appendChild(head);

    var readout = el("div", "chart-readout", "Pasa el cursor o enfoca un punto para inspeccionar la muestra");
    card.appendChild(readout);

    var width = opts.width || 600;
    var height = opts.height || 225;
    var padLeft = expanded ? 72 : 64;
    var padRight = 20;
    var padTop = 18;
    var padBottom = 36;
    var times = points.map(function (p) { return new Date(p.intervalStart).getTime(); });
    var minT = Math.min.apply(null, times);
    var maxT = Math.max.apply(null, times);
    var singleTimestamp = maxT === minT;
    if (singleTimestamp) { maxT = minT + 1; }
    var values = points.map(metricSpec.selector);
    var maxV = Math.max.apply(null, values);
    if (!(maxV > 0)) { maxV = 1; }

    var cfg = {
      width: width,
      height: height,
      padLeft: padLeft,
      padRight: padRight,
      padTop: padTop,
      padBottom: padBottom,
      plotW: width - padLeft - padRight,
      plotH: height - padTop - padBottom,
      minT: minT,
      maxT: maxT,
      singleTimestamp: singleTimestamp,
      maxV: maxV,
      decimals: metricSpec.decimals,
      unitKind: metricSpec.unitKind,
      valueSelector: metricSpec.selector,
      windowKind: windowKind,
      ySteps: expanded ? 5 : 4,
      xSteps: expanded ? 6 : 4,
      expanded: expanded
    };

    var svg = svgEl("svg", {
      "class": "chart-svg",
      "viewBox": "0 0 " + width + " " + height,
      "role": "img",
      "aria-label": metricSpec.title
    });

    appendChartAxes(svg, cfg, summary);
    var guideLine = svgEl("line", {
      "class": "chart-hover-guide",
      "x1": padLeft, "y1": padTop, "x2": padLeft, "y2": padTop + cfg.plotH
    });
    svg.appendChild(guideLine);
    appendPlanSeriesLines(svg, points, cfg, planClassMap, activePlanFilter, guideLine, readout);

    svg.addEventListener("mouseleave", function () {
      guideLine.classList.remove("is-visible");
    });

    card.appendChild(svg);
    return card;
  }

  function openSeriesModal(series, plans, planClassMap, windowKind, initialMetricId, initialPlanFilter, triggerEl) {
    var existing = document.getElementById("chart-zoom-dialog");
    if (existing && existing.parentNode) {
      if (existing.open) { existing.close(); }
      existing.parentNode.removeChild(existing);
    }

    var modalState = {
      metricId: initialMetricId || SERIES_METRICS[0].id,
      planFilter: initialPlanFilter || null
    };

    var dialog = el("dialog", "chart-dialog");
    dialog.id = "chart-zoom-dialog";
    dialog.setAttribute("aria-labelledby", "chart-dialog-title");

    var head = el("div", "chart-dialog-head");
    var titleWrap = el("div", "panel-head-title");
    var titleEl = el("h2", null, "Detalle ampliado de serie temporal");
    titleEl.id = "chart-dialog-title";
    titleWrap.appendChild(titleEl);
    titleWrap.appendChild(el("p", "panel-note", "Cambia entre las 4 métricas o filtra por plan sin cerrar la vista."));
    head.appendChild(titleWrap);

    var closeBtn = el("button", "drawer-close", "Cerrar · Esc");
    closeBtn.type = "button";
    closeBtn.addEventListener("click", function () { dialog.close(); });
    head.appendChild(closeBtn);
    dialog.appendChild(head);

    var body = el("div", "chart-dialog-body");
    var controlsBar = el("div", "chart-dialog-controls");
    var metricSwitch = el("div", "series-view-switch");
    metricSwitch.setAttribute("role", "group");
    metricSwitch.setAttribute("aria-label", "Seleccionar métrica a ampliar");

    var metricButtons = {};
    var modalLegend = el("div", "plan-legend");
    var chartHost = el("div", "chart-dialog-stage");

    function renderModalContent() {
      Object.keys(metricButtons).forEach(function (id) {
        metricButtons[id].setAttribute("aria-pressed", id === modalState.metricId ? "true" : "false");
      });
      buildPlanLegendPills(modalLegend, plans, modalState.planFilter, function (planId) {
        modalState.planFilter = modalState.planFilter === planId ? null : planId;
        renderModalContent();
      });
      var spec = SERIES_METRICS.filter(function (m) { return m.id === modalState.metricId; })[0] || SERIES_METRICS[0];
      chartHost.replaceChildren(buildSvgMetricChart(
        spec,
        series,
        planClassMap,
        modalState.planFilter,
        windowKind,
        { width: 920, height: 350, expanded: true }
      ));
    }

    SERIES_METRICS.forEach(function (m) {
      var btn = el("button", "plan-btn", m.title);
      btn.type = "button";
      btn.addEventListener("click", function () {
        modalState.metricId = m.id;
        renderModalContent();
      });
      metricButtons[m.id] = btn;
      metricSwitch.appendChild(btn);
    });

    controlsBar.appendChild(metricSwitch);
    controlsBar.appendChild(modalLegend);
    body.appendChild(controlsBar);
    body.appendChild(chartHost);
    dialog.appendChild(body);

    dialog.addEventListener("click", function (ev) {
      if (ev.target === dialog) { dialog.close(); }
    });
    dialog.addEventListener("close", function () {
      if (dialog.parentNode) { dialog.parentNode.removeChild(dialog); }
      if (triggerEl && typeof triggerEl.focus === "function") { triggerEl.focus(); }
    });

    document.body.appendChild(dialog);
    renderModalContent();
    dialog.showModal();
    closeBtn.focus();
  }

  function renderSeriesPanel(detail) {
    var section = el("section", "panel panel-series");
    var plans = detail.plans || [];
    var series = detail.series || [];
    var planClassMap = {};
    plans.forEach(function (plan, idx) {
      planClassMap[plan.planId] = "series-" + (idx % 4);
    });

    var activePlanFilter = null;
    var activeSeriesView = "charts";

    var headControls = el("div", "series-head-controls");
    var legendHost = el("div", "plan-legend");
    headControls.appendChild(legendHost);

    var modeSwitch = el("div", "series-view-switch");
    modeSwitch.setAttribute("role", "group");
    modeSwitch.setAttribute("aria-label", "Modo de visualización de series");
    var chartsModeBtn = el("button", "plan-btn", "Gráficos (2×2)");
    chartsModeBtn.type = "button";
    chartsModeBtn.setAttribute("aria-pressed", "true");
    var tableModeBtn = el("button", "plan-btn", "Tabla (" + series.length + ")");
    tableModeBtn.type = "button";
    tableModeBtn.setAttribute("aria-pressed", "false");
    modeSwitch.appendChild(chartsModeBtn);
    modeSwitch.appendChild(tableModeBtn);
    if (series.length) {
      headControls.appendChild(modeSwitch);
    }

    section.appendChild(buildPanelHead(
      "Series temporales por plan",
      "Ventana " + detail.window + " (" + formatDateTime(detail.windowStart) + " – " + formatDateTime(detail.windowEnd) + ")",
      (plans.length || series.length) ? headControls : null
    ));

    var chartsHost = el("div", "charts-grid");
    var tableHost = el("div", "subpanel-card series-table-card");
    tableHost.hidden = true;

    function paintCharts() {
      buildPlanLegendPills(legendHost, plans, activePlanFilter, function (planId) {
        activePlanFilter = activePlanFilter === planId ? null : planId;
        paintCharts();
        paintTable();
      });
      chartsHost.replaceChildren();
      SERIES_METRICS.forEach(function (spec) {
        chartsHost.appendChild(buildSvgMetricChart(
          spec,
          series,
          planClassMap,
          activePlanFilter,
          detail.window,
          {
            onExpand: function (metricId, btnEl) {
              openSeriesModal(series, plans, planClassMap, detail.window, metricId, activePlanFilter, btnEl);
            }
          }
        ));
      });
    }

    function paintTable() {
      tableHost.replaceChildren();
      var rows = activePlanFilter
        ? series.filter(function (p) { return p.planId === activePlanFilter; })
        : series;
      var wrap = el("div", "table-wrap");
      var table = el("table", "table table-compact");
      var thead = el("thead");
      var hrow = el("tr");
      ["Inicio intervalo", "Plan", "Ejecuciones", "Duración prom. (ms)", "CPU prom. (ms)", "Lecturas lógicas prom."].forEach(function (col) {
        var th = el("th", null, col);
        th.setAttribute("scope", "col");
        hrow.appendChild(th);
      });
      thead.appendChild(hrow);
      table.appendChild(thead);

      var tbody = el("tbody");
      rows.forEach(function (p) {
        var tr = el("tr");
        tr.appendChild(el("td", null, formatDateTime(p.intervalStart)));
        tr.appendChild(el("td", "mono", "Plan #" + p.queryStorePlanId));
        tr.appendChild(el("td", "mono tabular", formatNum(p.executionCount, 0)));
        tr.appendChild(el("td", "mono tabular", formatNum(p.avgDurationMs, 2)));
        tr.appendChild(el("td", "mono tabular", formatNum(p.avgCpuTimeMs, 2)));
        tr.appendChild(el("td", "mono tabular", formatNum(p.avgLogicalIoReads, 1)));
        tbody.appendChild(tr);
      });
      table.appendChild(tbody);
      wrap.appendChild(table);
      tableHost.appendChild(wrap);
    }

    function syncSeriesView(nextView) {
      activeSeriesView = nextView;
      chartsHost.hidden = activeSeriesView !== "charts";
      tableHost.hidden = activeSeriesView !== "table";
      chartsModeBtn.setAttribute("aria-pressed", activeSeriesView === "charts" ? "true" : "false");
      tableModeBtn.setAttribute("aria-pressed", activeSeriesView === "table" ? "true" : "false");
    }

    if (!series.length) {
      if (plans.length > 0) {
        plans.forEach(function (plan, idx) {
          var cls = "series-" + (idx % 4);
          var item = el("span", "legend-item");
          item.appendChild(el("span", "legend-swatch " + cls));
          item.appendChild(el("span", null, "Plan #" + plan.queryStorePlanId));
          legendHost.appendChild(item);
        });
      }
      section.appendChild(el("p", "panel-note", "No hay intervalos de ejecución registrados en la ventana seleccionada."));
      return section;
    }

    chartsModeBtn.addEventListener("click", function () { syncSeriesView("charts"); });
    tableModeBtn.addEventListener("click", function () { syncSeriesView("table"); });

    paintCharts();
    paintTable();
    section.appendChild(chartsHost);
    section.appendChild(tableHost);

    return section;
  }

  // ---------- Render: Hallazgos con evidencia ----------
  function addKv(dl, label, value, isFullWidth) {
    var wrap = el("div", isFullWidth ? "kv-item is-full" : "kv-item");
    wrap.appendChild(el("dt", null, label));
    wrap.appendChild(el("dd", isFullWidth ? "kv-evidence-val" : null, value));
    dl.appendChild(wrap);
  }

  function renderFindingsPanel(findings) {
    var section = el("section", "panel panel-findings");
    var list = findings || [];
    var activeSeverity = "ALL";

    var sevBuckets = {};
    list.forEach(function (f) {
      var entry = SEVERITY_TONE[f.severity];
      var key = entry ? entry[0].toUpperCase() : (f.severity || "OTRA");
      sevBuckets[key] = true;
    });
    var sevKeys = Object.keys(sevBuckets);

    var headControls = el("div", "findings-head-controls");
    headControls.appendChild(badge(list.length + " hallazgo(s)", list.length ? "info" : "neutral"));

    var listHost = el("div", "finding-list");

    function paintFindings() {
      listHost.replaceChildren();
      var visible = activeSeverity === "ALL"
        ? list
        : list.filter(function (f) {
            var entry = SEVERITY_TONE[f.severity];
            var key = entry ? entry[0].toUpperCase() : (f.severity || "OTRA");
            return key === activeSeverity;
          });

      visible.forEach(function (f) {
        var card = el("article", "finding-card");
        card.setAttribute("data-severity", f.severity || "");

        var cardHead = el("div", "finding-card-head");
        var headTop = el("div", "finding-card-top");
        headTop.appendChild(el("strong", "finding-card-title", f.ruleId + " · " + f.ruleName));
        var sevEntry = SEVERITY_TONE[f.severity];
        headTop.appendChild(badge(sevEntry ? sevEntry[0] : f.severity, sevEntry ? sevEntry[1] : "neutral"));
        cardHead.appendChild(headTop);

        var metaRow = el("div", "finding-badges");
        metaRow.appendChild(badge(f.findingCode, "neutral"));
        metaRow.appendChild(badge(f.kind, "neutral"));
        metaRow.appendChild(badge("Evidencia: " + f.evidenceClass, "neutral"));
        metaRow.appendChild(badge("Plan #" + f.queryStorePlanId, "neutral"));
        cardHead.appendChild(metaRow);
        card.appendChild(cardHead);

        // Franja numérica uniforme (solo métricas cortas)
        var numericMetrics = [];
        if (f.context) {
          if (f.context.executions !== null && f.context.executions !== undefined) {
            numericMetrics.push(["Ejecuciones", formatNum(f.context.executions, 0)]);
          }
          if (f.context.query_consumption_pct !== null && f.context.query_consumption_pct !== undefined) {
            numericMetrics.push(["Consumo", formatNum(f.context.query_consumption_pct, 2) + " %"]);
          }
          if (f.context.estimated_rows !== null && f.context.estimated_rows !== undefined) {
            numericMetrics.push(["Filas est.", formatNum(f.context.estimated_rows, 2)]);
          }
          if (f.context.estimated_rows_read !== null && f.context.estimated_rows_read !== undefined) {
            numericMetrics.push(["Filas leídas", formatNum(f.context.estimated_rows_read, 2)]);
          }
        }
        if (numericMetrics.length) {
          var metricsRow = el("div", "finding-metrics-row");
          numericMetrics.forEach(function (m) {
            var box = el("div", "finding-metric-tile");
            box.appendChild(el("span", null, m[0]));
            box.appendChild(el("strong", null, m[1]));
            metricsRow.appendChild(box);
          });
          card.appendChild(metricsRow);
        }

        // Metadatos en 2 columnas compactas y evidencia técnica a ancho completo acotado
        var dl = el("dl", "kv-details");
        if (f.nodes && f.nodes.length) {
          addKv(dl, "Nodos del plan", f.nodes.map(function (n) { return "Nodo " + n.node_id + " (" + n.physical_op + ")"; }).join(", "), false);
        }
        if (f.thresholdsUsed && Object.keys(f.thresholdsUsed).length) {
          var thText = Object.keys(f.thresholdsUsed).map(function (k) { return k + "=" + f.thresholdsUsed[k]; }).join(", ");
          addKv(dl, "Umbrales aplicados", thText, false);
        }
        if (f.engineVersion) {
          addKv(dl, "Motor / Catálogo", f.engineVersion + " · cat. " + f.catalogVersion, false);
        }
        if (f.evidenceMaskingFailure) {
          addKv(dl, "Evidencia", "Omitida por enmascarado seguro (" + f.evidenceMaskingFailure + ")", true);
        } else if (f.evidence) {
          Object.keys(f.evidence).forEach(function (k) {
            addKv(dl, "Evidencia · " + k, f.evidence[k], true);
          });
        }

        card.id = "finding-card-" + (f.findingCode || f.id);
        card.tabIndex = -1;
        card.appendChild(dl);

        if (f.nodes && f.nodes.length) {
          var cardFoot = el("div", "finding-card-foot");
          var jumpRow = el("div", "node-jump-list");
          f.nodes.forEach(function (n) {
            var jumpBtn = el("button", "node-jump-btn", "Ver nodo #" + n.node_id + " en el plan →");
            jumpBtn.type = "button";
            jumpBtn.setAttribute("data-target-plan-id", f.planId);
            jumpBtn.setAttribute("data-target-node-id", String(n.node_id));
            jumpBtn.addEventListener("click", function () {
              focusNodeInPlanViewer(f.planId, n.node_id);
            });
            jumpRow.appendChild(jumpBtn);
          });
          cardFoot.appendChild(jumpRow);
          card.appendChild(cardFoot);
        }

        listHost.appendChild(card);
      });
    }

    if (sevKeys.length > 1) {
      var filterGroup = el("div", "findings-filter-group");
      filterGroup.setAttribute("role", "group");
      filterGroup.setAttribute("aria-label", "Filtrar hallazgos por severidad");
      var filterBtns = [];
      [["ALL", "Todos"]].concat(sevKeys.map(function (k) { return [k, k.charAt(0) + k.slice(1).toLowerCase()]; })).forEach(function (pair) {
        var btn = el("button", "plan-btn", pair[1]);
        btn.type = "button";
        btn.setAttribute("aria-pressed", pair[0] === "ALL" ? "true" : "false");
        btn.addEventListener("click", function () {
          activeSeverity = pair[0];
          filterBtns.forEach(function (b, idx) {
            b.setAttribute("aria-pressed", (idx === 0 ? "ALL" : sevKeys[idx - 1]) === activeSeverity ? "true" : "false");
          });
          paintFindings();
        });
        filterBtns.push(btn);
        filterGroup.appendChild(btn);
      });
      headControls.appendChild(filterGroup);
    }

    section.appendChild(buildPanelHead(
      "Hallazgos y evidencia determinista",
      "Cada hallazgo es una señal con evidencia a investigar, no un diagnóstico concluyente.",
      headControls
    ));

    if (!list.length) {
      section.appendChild(el("p", "panel-note", "No se detectaron hallazgos determinísticos para los planes analizados de esta consulta."));
      return section;
    }

    paintFindings();
    section.appendChild(listHost);
    return section;
  }

  // ---------- Render: Visor jerárquico del plan (S2-10 + Vista Gráfica) ----------
  var planViewerState = {
    queryId: null,
    activePlanId: null,
    selectedKey: null,
    onlyFlagged: false,
    viewMode: "graph",
    zoom: 1,
    collapsed: {},
    cache: {},
    renderBody: null,
    tabsByPlanId: {}
  };

  function formatWarningBadge(w) {
    if (!w) { return "Advertencia"; }
    if (w.kind === "SpillToTempDb" || w.kind === "SortSpillDetails" || w.kind === "HashSpillDetails") {
      return "Spill TempDB" + (w.spillLevel ? " (niv. " + w.spillLevel + ")" : "");
    }
    if (w.kind === "PlanAffectingConvert") {
      return "Convert" + (w.convertIssue ? ": " + w.convertIssue : "");
    }
    if (w.kind === "NoJoinPredicate") { return "Sin predicado Join"; }
    if (w.kind === "ColumnsWithNoStatistics") { return "Sin estadísticas"; }
    if (w.kind === "MemoryGrantWarning") { return "Memoria" + (w.details ? ": " + w.details : ""); }
    return w.kind || "Advertencia";
  }

  function buildTreePrefixes(operators) {
    var activeBranches = [];
    return operators.map(function (op) {
      var prefix = "";
      if (op.depth > 0) {
        for (var d = 1; d < op.depth; d++) {
          prefix += activeBranches[d] ? "│  " : "   ";
        }
        prefix += op.isLastSibling ? "└─ " : "├─ ";
        activeBranches[op.depth] = !op.isLastSibling;
      }
      return prefix;
    });
  }

  function computeVisibleSet(operators, onlyFlagged) {
    var visible = new Array(operators.length);
    if (!onlyFlagged) {
      for (var i = 0; i < operators.length; i++) { visible[i] = true; }
      return visible;
    }

    // Indexar por NodeId dentro de la sentencia para mantener visibles los ancestros de cualquier nodo relevante.
    var indexByNodeId = {};
    var idx;
    for (idx = 0; idx < operators.length; idx++) {
      visible[idx] = false;
      indexByNodeId[operators[idx].nodeId] = idx;
    }

    for (idx = 0; idx < operators.length; idx++) {
      var op = operators[idx];
      var isFlagged = op.hasFindings || (op.warnings && op.warnings.length > 0);
      if (!isFlagged) { continue; }
      var cursorIdx = idx;
      var guard = 0;
      while (cursorIdx !== undefined && !visible[cursorIdx] && guard < operators.length) {
        visible[cursorIdx] = true;
        var parentId = operators[cursorIdx].parentNodeId;
        cursorIdx = (parentId !== null && parentId !== undefined) ? indexByNodeId[parentId] : undefined;
        guard++;
      }
    }
    return visible;
  }

  function renderOperatorInspector(container, planId, stmtIdx, op) {
    container.replaceChildren();
    if (!op) {
      container.appendChild(el("p", "panel-note", "Selecciona un operador del árbol para inspeccionar su costo, predicado enmascarado, advertencias y hallazgos."));
      return;
    }

    var title = "Nodo #" + op.nodeId + " · " + op.physicalOp +
      (op.logicalOp && op.logicalOp !== op.physicalOp ? " (" + op.logicalOp + ")" : "");
    container.appendChild(el("h4", null, title));

    var dl = el("dl", "kv-grid");
    addKv(dl, "Costo estimado subárbol", formatNum(op.estimatedTotalSubtreeCost, 6) +
      (typeof op.costSharePct === "number" ? " (" + formatNum(op.costSharePct, 1) + " %)" : ""));
    addKv(dl, "Costo CPU / E/S", formatNum(op.estimateCpu, 6) + " / " + formatNum(op.estimateIo, 6));
    addKv(dl, "Filas estimadas", formatNum(op.estimateRows, 2));
    if (op.estimatedRowsRead !== null && op.estimatedRowsRead !== undefined) {
      addKv(dl, "Filas leídas est.", formatNum(op.estimatedRowsRead, 2));
    }
    if (op.tableCardinality !== null && op.tableCardinality !== undefined) {
      addKv(dl, "Cardinalidad de tabla", formatNum(op.tableCardinality, 0));
    }
    if (op.targetObject) { addKv(dl, "Objeto destino", op.targetObject); }
    if (op.estimatedExecutionMode) {
      addKv(dl, "Modo / Paralelismo", op.estimatedExecutionMode + (op.isParallel ? " (Paralelo)" : ""));
    }
    if (op.predicateScalarString) { addKv(dl, "Predicado (enmascarado)", op.predicateScalarString); }
    if (op.seekColumns && op.seekColumns.length) { addKv(dl, "Columnas Seek", op.seekColumns.join(", ")); }
    if (op.outputColumns && op.outputColumns.length) { addKv(dl, "Columnas de salida", op.outputColumns.join(", ")); }
    if (op.implicitConversions && op.implicitConversions.length) {
      addKv(dl, "Conversiones implícitas", op.implicitConversions.map(function (c) {
        return (c.column ? c.column + " → " : "") + c.targetDataType +
          (c.scalarString ? " [" + c.scalarString + "]" : "");
      }).join("; "));
    }
    if (op.warnings && op.warnings.length) {
      addKv(dl, "Advertencias del nodo", op.warnings.map(function (w) {
        return formatWarningBadge(w) + (w.expression ? " — " + w.expression : (w.details ? " — " + w.details : ""));
      }).join(" | "));
    }
    container.appendChild(dl);

    if (op.findings && op.findings.length) {
      var fRow = el("div", "node-jump-list");
      op.findings.forEach(function (f) {
        var btn = el("button", "node-jump-btn", "Ir al hallazgo " + f.ruleId + " (" + f.findingCode + ") ↓");
        btn.type = "button";
        btn.addEventListener("click", function () {
          focusFindingCard(f.findingCode);
        });
        fRow.appendChild(btn);
      });
      container.appendChild(fRow);
    }

    container.setAttribute("data-inspected-key", planId + ":" + stmtIdx + ":" + op.nodeId);
  }

  function renderLoadedPlanTree(planView) {
    var body = planViewerState.renderBody;
    if (!body) { return; }
    body.replaceChildren();

    if (planView.planMaskingFailure) {
      body.appendChild(el("p", "panel-note", "No se pudo enmascarar el Showplan XML de forma segura (" + planView.planMaskingFailure + ")."));
      return;
    }
    if (planView.planExpired) {
      body.appendChild(el("p", "panel-note", "El XML del plan expiró por retención o no se recuperó durante la ingesta."));
      return;
    }
    if (planView.parseFailureReason) {
      body.appendChild(el("p", "panel-note", "Plan no analizable (" + (planView.notAnalyzableCause || planView.parseFailureReason) + ")."));
      return;
    }

    var toolbar = el("div", "plan-toolbar");
    toolbar.appendChild(el("span", "plan-summary-meta",
      "Plan #" + planView.queryStorePlanId +
      " · Operadores: " + formatIntText(planView.totalOperators) +
      " · Nodos con hallazgos: " + formatIntText(planView.highlightedOperatorsCount) +
      (planView.engineVersion ? " · SQL " + planView.engineVersion : "")));

    var actions = el("div", "plan-toolbar-actions");

    var viewToggleBtn = el("button", "plan-btn", planViewerState.viewMode === "graph" ? "Cambiar a vista árbol lista" : "Cambiar a vista gráfica");
    viewToggleBtn.type = "button";
    viewToggleBtn.setAttribute("aria-pressed", planViewerState.viewMode === "graph" ? "true" : "false");
    viewToggleBtn.addEventListener("click", function () {
      planViewerState.viewMode = planViewerState.viewMode === "graph" ? "list" : "graph";
      renderLoadedPlanTree(planView);
    });
    actions.appendChild(viewToggleBtn);

    if (planViewerState.viewMode === "graph" && window.QDPlanGraph) {
      [0.75, 1, 1.25].forEach(function (z) {
        var zBtn = el("button", "plan-btn", Math.round(z * 100) + "%");
        zBtn.type = "button";
        zBtn.setAttribute("aria-pressed", planViewerState.zoom === z ? "true" : "false");
        zBtn.addEventListener("click", function () {
          planViewerState.zoom = z;
          renderLoadedPlanTree(planView);
        });
        actions.appendChild(zBtn);
      });
    }

    var filterBtn = el("button", "plan-btn", planViewerState.onlyFlagged ? "Mostrando hallazgos/advertencias" : "Solo hallazgos y advertencias");
    filterBtn.type = "button";
    filterBtn.setAttribute("aria-pressed", planViewerState.onlyFlagged ? "true" : "false");
    filterBtn.addEventListener("click", function () {
      planViewerState.onlyFlagged = !planViewerState.onlyFlagged;
      renderLoadedPlanTree(planView);
    });
    actions.appendChild(filterBtn);

    if (planViewerState.viewMode === "list" && planView.totalOperators > 1) {
      var expandBtn = el("button", "plan-btn", "Expandir todo");
      expandBtn.type = "button";
      expandBtn.addEventListener("click", function () {
        planViewerState.collapsed = {};
        renderLoadedPlanTree(planView);
      });
      actions.appendChild(expandBtn);

      var collapseBtn = el("button", "plan-btn", "Colapsar ramas");
      collapseBtn.type = "button";
      collapseBtn.addEventListener("click", function () {
        planView.statements.forEach(function (st, sIdx) {
          st.operators.forEach(function (op) {
            if (op.hasChildren && op.depth > 0) {
              planViewerState.collapsed[planView.planId + ":" + sIdx + ":" + op.nodeId] = true;
            }
          });
        });
        renderLoadedPlanTree(planView);
      });
      actions.appendChild(collapseBtn);
    }

    toolbar.appendChild(actions);
    body.appendChild(toolbar);

    if (!planView.statements || !planView.statements.length) {
      body.appendChild(el("p", "panel-note", "Este plan no contiene sentencias con operadores analizables."));
      return;
    }

    var split = el("div", "plan-workbench-split");
    var mainCol = el("div", "plan-workbench-main");
    var inspector = el("aside", "plan-node-inspector");
    var selectedOpRef = null;
    var selectedStmtIdx = 0;

    planView.statements.forEach(function (stmt, stmtIdx) {
      var stmtBox = el("div", "plan-statement");
      var stHead = el("div", "plan-statement-head");
      stHead.appendChild(el("strong", null,
        "Sentencia #" + stmt.statementId + " · " + (stmt.statementType || "SQL") +
        (stmt.isDispatcherPlan ? " (Dispatcher PSP)" : "")));
      stHead.appendChild(el("span", "plan-summary-meta",
        "Costo total: " + formatNum(stmt.statementSubTreeCost, 4) +
        " · Filas est.: " + formatNum(stmt.statementEstRows, 1) +
        (stmt.statementOptmLevel ? " · Opt: " + stmt.statementOptmLevel : "")));
      stmtBox.appendChild(stHead);

      if (stmt.warnings && stmt.warnings.length) {
        var warnBar = el("div", "plan-warnings-bar");
        stmt.warnings.forEach(function (w) {
          warnBar.appendChild(badge(formatWarningBadge(w), "warn"));
        });
        stmtBox.appendChild(warnBar);
      }

      if (!stmt.operators || !stmt.operators.length) {
        stmtBox.appendChild(el("p", "panel-note", stmt.isDispatcherPlan
          ? "Plan Dispatcher sin árbol RelOp directo."
          : "Sin operadores RelOp en esta sentencia."));
        mainCol.appendChild(stmtBox);
        return;
      }

      var prefixes = buildTreePrefixes(stmt.operators);
      var visibleByFilter = computeVisibleSet(stmt.operators, planViewerState.onlyFlagged);

      if (planViewerState.viewMode === "graph" && window.QDPlanGraph) {
        var filteredOps = stmt.operators.filter(function (_, idx) { return visibleByFilter[idx]; });
        filteredOps.forEach(function (op) {
          var key = planView.planId + ":" + stmtIdx + ":" + op.nodeId;
          if (planViewerState.selectedKey === key) {
            selectedOpRef = op;
            selectedStmtIdx = stmtIdx;
          }
        });
        var graphView = window.QDPlanGraph.renderGraphCanvas(document, filteredOps, {
          planId: planView.planId,
          stmtIdx: stmtIdx,
          selectedKey: planViewerState.selectedKey,
          zoom: planViewerState.zoom,
          onSelectNode: function (op, key) {
            planViewerState.selectedKey = key;
            renderLoadedPlanTree(planView);
          }
        });
        stmtBox.appendChild(graphView);
        mainCol.appendChild(stmtBox);
        return;
      }

      var list = el("ul", "plan-tree");
      list.setAttribute("role", "tree");

      var collapsedDepthCutoff = null;

      stmt.operators.forEach(function (op, idx) {
        if (collapsedDepthCutoff !== null && op.depth > collapsedDepthCutoff) {
          return;
        }
        collapsedDepthCutoff = null;

        var key = planView.planId + ":" + stmtIdx + ":" + op.nodeId;
        var isCollapsed = Boolean(planViewerState.collapsed[key]);
        if (isCollapsed && op.hasChildren) {
          collapsedDepthCutoff = op.depth;
        }

        if (!visibleByFilter[idx]) {
          return;
        }

        var li = el("li", "plan-node-item");
        li.setAttribute("role", "treeitem");

        var row = el("div", "plan-node-row");
        row.id = "plan-node-" + planView.planId + "-" + op.nodeId;
        row.setAttribute("data-node-id", String(op.nodeId));
        row.setAttribute("data-has-findings", op.hasFindings ? "true" : "false");
        if (op.hasFindings) {
          row.className += " is-highlighted";
          row.setAttribute("data-severity", op.maxFindingSeverity || "");
        }
        if (planViewerState.selectedKey === key) {
          row.className += " is-selected";
          selectedOpRef = op;
          selectedStmtIdx = stmtIdx;
        }

        var main = el("div", "plan-node-main");
        if (prefixes[idx]) {
          main.appendChild(el("span", "plan-tree-prefix", prefixes[idx]));
        }

        if (op.hasChildren) {
          var toggleBtn = el("button", "plan-toggle-btn", isCollapsed ? "▸" : "▾");
          toggleBtn.type = "button";
          toggleBtn.setAttribute("aria-expanded", isCollapsed ? "false" : "true");
          toggleBtn.setAttribute("aria-label", (isCollapsed ? "Expandir" : "Colapsar") + " nodo " + op.nodeId);
          toggleBtn.addEventListener("click", function () {
            planViewerState.collapsed[key] = !isCollapsed;
            renderLoadedPlanTree(planView);
          });
          main.appendChild(toggleBtn);
        }

        var selectBtn = el("button", "plan-node-select", "#" + op.nodeId + " " + op.physicalOp);
        selectBtn.type = "button";
        selectBtn.addEventListener("click", function () {
          planViewerState.selectedKey = key;
          renderLoadedPlanTree(planView);
        });
        main.appendChild(selectBtn);

        if (op.targetObject) {
          main.appendChild(el("span", "plan-node-obj", "[" + op.targetObject + "]"));
        }

        var metrics = el("div", "plan-node-metrics");
        var costText = "Costo " + formatNum(op.estimatedTotalSubtreeCost, 4) +
          (typeof op.costSharePct === "number" ? " (" + formatNum(op.costSharePct, 1) + "%)" : "");
        metrics.appendChild(badge(costText, "neutral"));
        metrics.appendChild(badge("Filas " + formatNum(op.estimateRows, 0), "neutral"));
        if (op.isParallel) { metrics.appendChild(badge("Paralelo", "neutral")); }

        (op.warnings || []).forEach(function (w) {
          metrics.appendChild(badge(formatWarningBadge(w), "warn"));
        });

        (op.findings || []).forEach(function (f) {
          var sev = SEVERITY_TONE[f.severity];
          metrics.appendChild(badge(f.ruleId + " · " + (sev ? sev[0] : f.severity), sev ? sev[1] : "danger"));
        });

        row.appendChild(main);
        row.appendChild(metrics);
        li.appendChild(row);
        list.appendChild(li);
      });

      stmtBox.appendChild(list);
      mainCol.appendChild(stmtBox);
    });

    if (!selectedOpRef && planView.statements[0] && planView.statements[0].operators[0]) {
      selectedOpRef = planView.statements[0].operators[0];
    }
    renderOperatorInspector(inspector, planView.planId, selectedStmtIdx, selectedOpRef);
    split.appendChild(mainCol);
    split.appendChild(inspector);
    body.appendChild(split);
  }

  function formatIntText(value) {
    return typeof value === "number" ? value.toLocaleString("es-CO") : "0";
  }

  function selectAndLoadPlan(planId, focusNodeId) {
    planViewerState.activePlanId = planId;
    Object.keys(planViewerState.tabsByPlanId).forEach(function (id) {
      planViewerState.tabsByPlanId[id].setAttribute("aria-selected", id === planId ? "true" : "false");
    });

    var cached = planViewerState.cache[planId];
    var applyFocus = function (view) {
      if (focusNodeId !== undefined && focusNodeId !== null && view && view.statements) {
        planViewerState.onlyFlagged = false;
        planViewerState.collapsed = {};
        view.statements.forEach(function (st, sIdx) {
          (st.operators || []).forEach(function (op) {
            if (op.nodeId === focusNodeId) {
              planViewerState.selectedKey = planId + ":" + sIdx + ":" + focusNodeId;
            }
          });
        });
      }
      renderLoadedPlanTree(view);
      if (focusNodeId !== undefined && focusNodeId !== null) {
        var row = document.getElementById("plan-node-" + planId + "-" + focusNodeId);
        if (row) {
          row.scrollIntoView({ behavior: "smooth", block: "center" });
          var btn = row.querySelector(".plan-node-select");
          if (btn) { btn.focus({ preventScroll: true }); }
        } else {
          var graphNode = document.querySelector(".pg-node.is-selected");
          if (graphNode) {
            graphNode.scrollIntoView({ behavior: "smooth", block: "center", inline: "center" });
            graphNode.focus({ preventScroll: true });
          }
        }
      }
    };

    if (cached) {
      applyFocus(cached);
      return Promise.resolve(cached);
    }

    if (planViewerState.renderBody) {
      planViewerState.renderBody.replaceChildren(el("p", "panel-note", "Cargando árbol jerárquico del plan..."));
    }

    return fetch("/api/queries/" + encodeURIComponent(planViewerState.queryId) + "/plans/" + encodeURIComponent(planId), {
      credentials: "same-origin",
      headers: { Accept: "application/json" }
    }).then(function (response) {
      if (response.status === 401) { goToLogin(); return null; }
      if (!response.ok) { throw new Error("plan-tree " + response.status); }
      return response.json();
    }).then(function (view) {
      if (!view) { return null; }
      planViewerState.cache[planId] = view;
      applyFocus(view);
      return view;
    }).catch(function () {
      if (planViewerState.renderBody) {
        planViewerState.renderBody.replaceChildren(el("p", "panel-note", "No pudimos cargar el árbol de este plan."));
      }
      return null;
    });
  }

  var detailTabState = {
    active: "overview",
    order: [],
    buttons: {},
    panels: {}
  };

  function activateDetailTab(tabId, opts) {
    if (!detailTabState.panels[tabId]) { return; }
    detailTabState.active = tabId;
    Object.keys(detailTabState.buttons).forEach(function (id) {
      var isSelected = id === tabId;
      var btn = detailTabState.buttons[id];
      btn.setAttribute("aria-selected", isSelected ? "true" : "false");
      btn.tabIndex = isSelected ? 0 : -1;
    });
    Object.keys(detailTabState.panels).forEach(function (id) {
      detailTabState.panels[id].hidden = id !== tabId;
    });
    if (!(opts && opts.skipHistory)) {
      try {
        if (window.history && window.history.replaceState) {
          window.history.replaceState(null, "", window.location.pathname + window.location.search + "#tab=" + tabId);
        }
      } catch (_) { /* noop */ }
    }
  }

  function focusNodeInPlanViewer(planId, nodeId) {
    if (!planId) { return; }
    activateDetailTab("plan");
    selectAndLoadPlan(planId, nodeId);
  }

  function renderPlanTreePanel(detail) {
    planViewerState.queryId = detail.id;
    planViewerState.cache = {};
    planViewerState.collapsed = {};
    planViewerState.selectedKey = null;
    planViewerState.onlyFlagged = false;
    planViewerState.tabsByPlanId = {};

    var section = el("section", "panel panel-plan");
    section.id = "plan-viewer-panel";

    var plans = detail.plans || [];
    if (!plans.length) {
      section.appendChild(buildPanelHead(
        "Plan de ejecución",
        "Operadores en jerarquía con costos estimados, advertencias y nodos asociados a hallazgos."
      ));
      section.appendChild(el("p", "panel-note", "Esta consulta todavía no tiene planes de ejecución ingeridos."));
      return section;
    }

    var tabs = el("div", "plan-tabs");
    tabs.setAttribute("role", "tablist");
    tabs.setAttribute("aria-label", "Planes de ejecución de la consulta");

    plans.forEach(function (plan, index) {
      var shortHash = plan.queryPlanHash ? " (" + plan.queryPlanHash.slice(0, 8) + ")" : "";
      var label = "Plan #" + plan.queryStorePlanId + shortHash;
      var tab = el("button", "plan-tab", label);
      tab.type = "button";
      tab.setAttribute("role", "tab");
      tab.setAttribute("aria-selected", index === 0 ? "true" : "false");
      if (plan.analysisStatus === "NotAnalyzable") {
        tab.appendChild(badge("No analizable", "warn"));
      }
      tab.addEventListener("click", function () {
        selectAndLoadPlan(plan.planId, null);
      });
      planViewerState.tabsByPlanId[plan.planId] = tab;
      tabs.appendChild(tab);
    });

    section.appendChild(buildPanelHead(
      "Plan de ejecución",
      "Operadores en jerarquía con costos estimados, advertencias y nodos asociados a hallazgos.",
      tabs
    ));

    var body = el("div", "plan-viewer-body");
    planViewerState.renderBody = body;
    section.appendChild(body);

    selectAndLoadPlan(plans[0].planId, null);
    return section;
  }

  // ---------- Render: Explicación con IA y trazabilidad (S2-11) ----------
  var explanationState = {
    token: 0,
    pollTimerId: null,
    requestInFlight: false,
    lastPayload: null,
    bodyEl: null,
    detail: null,
    highlightedFindingCode: null
  };

  function cancelExplanationPolling() {
    if (explanationState.pollTimerId !== null) {
      window.clearTimeout(explanationState.pollTimerId);
      explanationState.pollTimerId = null;
    }
  }

  function focusFindingCard(findingCode) {
    if (!findingCode) { return; }
    activateDetailTab("findings");
    if (explanationState.highlightedFindingCode) {
      var prev = document.getElementById("finding-card-" + explanationState.highlightedFindingCode);
      if (prev) { prev.classList.remove("is-target-highlight"); }
    }
    var card = document.getElementById("finding-card-" + findingCode);
    if (!card) { return; }
    explanationState.highlightedFindingCode = findingCode;
    card.classList.add("is-target-highlight");
    card.scrollIntoView({ behavior: "smooth", block: "center" });
    card.focus({ preventScroll: true });
  }

  function paintExplanation(payload) {
    explanationState.lastPayload = payload;
    var viewModule = window.QueryDoctorExplanationView;
    if (!viewModule || !explanationState.bodyEl) { return; }
    var detail = explanationState.detail || {};
    var plans = detail.plans || [];
    var findings = detail.findings || [];
    viewModule.renderExplanationState(
      explanationState.bodyEl,
      payload,
      {
        formatDateTime: formatDateTime,
        detailFindings: findings,
        findingsCount: findings.length,
        defaultPlanId: plans.length === 1 ? plans[0].planId : null,
        requestInFlight: explanationState.requestInFlight
      },
      {
        onRequest: function () { requestExplanation(explanationState.token); },
        onReload: function () { fetchLatestExplanation(explanationState.token); },
        onFocusFinding: focusFindingCard,
        onFocusNode: focusNodeInPlanViewer
      }
    );
  }

  function fetchLatestExplanation(token) {
    var detail = explanationState.detail;
    if (!detail || token !== explanationState.token) { return Promise.resolve(); }
    cancelExplanationPolling();

    return fetch("/api/queries/" + encodeURIComponent(detail.id) + "/explanations/latest", {
      credentials: "same-origin",
      headers: { Accept: "application/json" }
    }).then(function (response) {
      if (token !== explanationState.token) { return null; }
      if (response.status === 401) { goToLogin(); return null; }
      if (response.status === 403) { paintExplanation({ kind: "forbidden" }); return null; }
      if (response.status === 404) { paintExplanation({ kind: "not_requested" }); return null; }
      if (!response.ok) { throw new Error("explanation-latest " + response.status); }
      return response.json();
    }).then(function (view) {
      if (!view || token !== explanationState.token) { return; }
      paintExplanation({ kind: "view", view: view });
      if (view.status === "PENDIENTE" || view.status === "EN_CURSO") {
        explanationState.pollTimerId = window.setTimeout(function () {
          fetchLatestExplanation(token);
        }, 2500);
      }
    }).catch(function () {
      if (token !== explanationState.token) { return; }
      paintExplanation({
        kind: "error",
        message: "No pudimos consultar el estado de la explicación de IA. Puedes reintentar sin perder los hallazgos del motor."
      });
    });
  }

  function requestExplanation(token) {
    var detail = explanationState.detail;
    if (!detail || token !== explanationState.token || explanationState.requestInFlight) { return Promise.resolve(); }

    explanationState.requestInFlight = true;
    paintExplanation(explanationState.lastPayload || { kind: "not_requested" });

    return fetch("/api/queries/" + encodeURIComponent(detail.id) + "/explanations", {
      method: "POST",
      credentials: "same-origin",
      headers: { Accept: "application/json" }
    }).then(function (response) {
      if (token !== explanationState.token) { return null; }
      explanationState.requestInFlight = false;
      if (response.status === 401) { goToLogin(); return null; }
      if (response.status === 403) { paintExplanation({ kind: "forbidden" }); return null; }
      if (response.status === 200) {
        return response.json().then(function (body) {
          if (token !== explanationState.token) { return; }
          paintExplanation({
            kind: "view",
            view: {
              status: (body && body.status) || "SIN_HALLAZGOS",
              notice: body && body.notice
            }
          });
        });
      }
      if (response.status === 202 || response.status === 409) {
        return fetchLatestExplanation(token);
      }
      throw new Error("explanation-post " + response.status);
    }).catch(function () {
      if (token !== explanationState.token) { return; }
      explanationState.requestInFlight = false;
      paintExplanation({
        kind: "error",
        message: "No pudimos enviar la solicitud de explicación de IA. Inténtalo de nuevo en unos instantes."
      });
    });
  }

  function renderExplanationPanel(detail) {
    cancelExplanationPolling();
    explanationState.token += 1;
    var token = explanationState.token;
    explanationState.requestInFlight = false;
    explanationState.detail = detail;
    explanationState.highlightedFindingCode = null;

    var section = el("section", "panel panel-explanation");
    section.id = "explanation-panel";
    section.appendChild(buildPanelHead(
      "Diagnóstico asistido por IA",
      "Cada afirmación se contrasta contra la evidencia determinista del motor antes de mostrarse."
    ));

    var body = el("div", "exp-body");
    explanationState.bodyEl = body;
    section.appendChild(body);

    paintExplanation({ kind: "loading" });
    fetchLatestExplanation(token);
    return section;
  }

  function renderSummaryStrip(detail) {
    var strip = el("div", "detail-summary-strip");
    var score = detail.score;

    // 1. Score Card con mini indicador radial
    var sCard = el("article", "summary-pill-card");
    if (score) {
      var val = typeof score.score === "number" ? Math.max(0, Math.min(100, score.score)) : 0;
      var r = 22;
      var circ = 2 * Math.PI * r;
      var svg = svgEl("svg", { viewBox: "0 0 56 56", width: "56", height: "56", "aria-hidden": "true" });
      svg.appendChild(svgEl("circle", { cx: "28", cy: "28", r: r, fill: "none", stroke: "var(--color-border)", "stroke-width": "5.5" }));
      var arc = svgEl("circle", {
        cx: "28",
        cy: "28",
        r: r,
        fill: "none",
        stroke: "var(--color-accent)",
        "stroke-width": "5.5",
        "stroke-linecap": "round",
        "stroke-dasharray": circ.toFixed(1),
        "stroke-dashoffset": (circ * (1 - val / 100)).toFixed(1),
        transform: "rotate(-90 28 28)"
      });
      var tone = BAND_TONE[score.band];
      if (tone === "danger") { arc.setAttribute("stroke", "var(--color-danger)"); }
      else if (tone === "warn") { arc.setAttribute("stroke", "var(--color-warning)"); }
      else if (tone === "ok") { arc.setAttribute("stroke", "var(--color-success)"); }
      svg.appendChild(arc);
      sCard.appendChild(svg);

      var sMeta = el("div", "summary-pill-meta");
      sMeta.appendChild(el("span", "summary-pill-label", "Health Score · #" + score.rankInWindow + "/" + score.totalQueriesInWindow));
      var row = el("div", "q-score-cell");
      row.appendChild(el("span", "summary-pill-val", formatNum(score.score, 1)));
      row.appendChild(badge(score.bandLabel || score.band, tone || "neutral"));
      sMeta.appendChild(row);
      sCard.appendChild(sMeta);
    } else {
      var emptyMeta = el("div", "summary-pill-meta");
      emptyMeta.appendChild(el("span", "summary-pill-label", "Health Score"));
      emptyMeta.appendChild(el("span", "summary-pill-val", "Pendiente"));
      sCard.appendChild(emptyMeta);
    }
    strip.appendChild(sCard);

    // 2. Consumo ponderado
    var cCard = el("article", "summary-pill-card");
    var cMeta = el("div", "summary-pill-meta");
    cMeta.appendChild(el("span", "summary-pill-label", "Consumo ponderado"));
    cMeta.appendChild(el("span", "summary-pill-val", score ? (formatNum(score.weightedConsumptionPct, 1) + " %") : "—"));
    cCard.appendChild(cMeta);
    strip.appendChild(cCard);

    // 3. Ejecuciones
    var eCard = el("article", "summary-pill-card");
    var eMeta = el("div", "summary-pill-meta");
    eMeta.appendChild(el("span", "summary-pill-label", "Ejecuciones en ventana"));
    eMeta.appendChild(el("span", "summary-pill-val", score ? formatNum(score.totalExecutions, 0) : "—"));
    eCard.appendChild(eMeta);
    strip.appendChild(eCard);

    // 4. Hallazgos y Planes
    var fCount = (detail.findings || []).length;
    var pCount = (detail.plans || []).length;
    var fCard = el("article", "summary-pill-card");
    var fMeta = el("div", "summary-pill-meta");
    fMeta.appendChild(el("span", "summary-pill-label", "Señales y planes"));
    fMeta.appendChild(el("span", "summary-pill-val", fCount + " hallazgo(s) · " + pCount + " plan(es)"));
    fCard.appendChild(fMeta);
    strip.appendChild(fCard);

    return strip;
  }

  function initialTabFromHash() {
    var m = (window.location.hash || "").match(/tab=(overview|plan|series|findings)/);
    return m ? m[1] : "overview";
  }

  function renderDetail(detail) {
    $("q-context").textContent = (detail.server || "") + " / " + (detail.databaseName || "");
    var metaText = "Query Store ID: " + detail.queryStoreQueryId +
      (detail.queryHash ? " · Hash: " + detail.queryHash : "") +
      (detail.originContext ? " · Objeto: " + detail.originContext : "");
    $("q-meta").textContent = metaText;

    var root = $("detail-root");
    root.replaceChildren();
    var stack = el("div", "detail-stack");
    stack.appendChild(renderSummaryStrip(detail));

    var plansCount = (detail.plans || []).length;
    var seriesCount = (detail.series || []).length;
    var findingsCount = (detail.findings || []).length;

    var tabDefs = [
      { id: "overview", label: "SQL y Diagnóstico IA", count: null },
      { id: "plan", label: "Plan de Ejecución", count: plansCount },
      { id: "series", label: "Series Temporales", count: seriesCount },
      { id: "findings", label: "Hallazgos y Score", count: findingsCount }
    ];

    var tabBar = el("div", "detail-tabs");
    tabBar.setAttribute("role", "tablist");
    tabBar.setAttribute("aria-label", "Secciones del diagnóstico");

    detailTabState.order = tabDefs.map(function (t) { return t.id; });
    detailTabState.buttons = {};
    detailTabState.panels = {};
    var startTab = initialTabFromHash();

    tabDefs.forEach(function (t) {
      var btn = el("button", "detail-tab-btn");
      btn.type = "button";
      btn.id = "detail-tab-" + t.id;
      btn.setAttribute("role", "tab");
      btn.setAttribute("aria-controls", "detail-panel-" + t.id);
      btn.setAttribute("aria-selected", t.id === startTab ? "true" : "false");
      btn.tabIndex = t.id === startTab ? 0 : -1;
      btn.appendChild(el("span", null, t.label));
      if (t.count !== null) {
        btn.appendChild(el("span", "detail-tab-count", String(t.count)));
      }
      btn.addEventListener("click", function () { activateDetailTab(t.id); });
      btn.addEventListener("keydown", function (ev) {
        var order = detailTabState.order;
        var idx = order.indexOf(t.id);
        if (idx === -1) { return; }
        var nextId = null;
        if (ev.key === "ArrowRight") { nextId = order[(idx + 1) % order.length]; }
        else if (ev.key === "ArrowLeft") { nextId = order[(idx - 1 + order.length) % order.length]; }
        else if (ev.key === "Home") { nextId = order[0]; }
        else if (ev.key === "End") { nextId = order[order.length - 1]; }
        if (nextId) {
          ev.preventDefault();
          activateDetailTab(nextId);
          if (detailTabState.buttons[nextId]) { detailTabState.buttons[nextId].focus(); }
        }
      });
      detailTabState.buttons[t.id] = btn;
      tabBar.appendChild(btn);
    });
    stack.appendChild(tabBar);

    // Panel 1: SQL y Diagnóstico IA (Opción A: bandas horizontales sin espacios muertos)
    var pOverview = el("div", "detail-tab-panel");
    pOverview.id = "detail-panel-overview";
    pOverview.setAttribute("role", "tabpanel");
    pOverview.setAttribute("aria-labelledby", "detail-tab-overview");
    pOverview.appendChild(renderSqlPanel(detail));
    pOverview.appendChild(renderExplanationPanel(detail));

    // Panel 2: Plan de Ejecución
    var pPlan = el("div", "detail-tab-panel");
    pPlan.id = "detail-panel-plan";
    pPlan.setAttribute("role", "tabpanel");
    pPlan.setAttribute("aria-labelledby", "detail-tab-plan");
    pPlan.appendChild(renderPlanTreePanel(detail));

    // Panel 3: Series Temporales
    var pSeries = el("div", "detail-tab-panel");
    pSeries.id = "detail-panel-series";
    pSeries.setAttribute("role", "tabpanel");
    pSeries.setAttribute("aria-labelledby", "detail-tab-series");
    pSeries.appendChild(renderSeriesPanel(detail));

    // Panel 4: Hallazgos y Score (Score arriba → Hallazgos abajo)
    var pFindings = el("div", "detail-tab-panel");
    pFindings.id = "detail-panel-findings";
    pFindings.setAttribute("role", "tabpanel");
    pFindings.setAttribute("aria-labelledby", "detail-tab-findings");
    pFindings.appendChild(renderScorePanel(detail.score));
    pFindings.appendChild(renderFindingsPanel(detail.findings));

    detailTabState.panels = {
      overview: pOverview,
      plan: pPlan,
      series: pSeries,
      findings: pFindings
    };

    [pOverview, pPlan, pSeries, pFindings].forEach(function (panel) {
      stack.appendChild(panel);
    });

    root.appendChild(stack);
    activateDetailTab(startTab, { skipHistory: !window.location.hash });
  }

  function getQueryIdFromUrl() {
    var params = new URLSearchParams(window.location.search);
    return params.get("id");
  }

  function loadDetail() {
    cancelExplanationPolling();
    hideNotice();
    var queryId = getQueryIdFromUrl();
    if (!queryId) {
      $("q-meta").textContent = "Falta el identificador de consulta en la URL.";
      renderEmpty("Consulta no especificada", "Abre una consulta desde el listado de Consultas para inspeccionar su detalle.");
      return Promise.resolve();
    }

    var win = $("f-window").value;
    return fetch("/api/queries/" + encodeURIComponent(queryId) + "?window=" + encodeURIComponent(win), {
      credentials: "same-origin",
      headers: { Accept: "application/json" }
    }).then(function (response) {
      if (response.status === 401) { goToLogin(); return null; }
      if (response.status === 403) {
        renderEmpty("Sin acceso", "Tu cuenta no tiene permiso para consultar el detalle de esta consulta.");
        return null;
      }
      if (response.status === 404) {
        renderEmpty("Consulta no encontrada", "No existe la consulta solicitada o fue eliminada.");
        return null;
      }
      if (!response.ok) { throw new Error("query-detail " + response.status); }
      return response.json();
    }).then(function (detail) {
      if (!detail) { return; }
      renderDetail(detail);
    }).catch(function () {
      showNotice("No pudimos cargar el detalle de la consulta. Inténtalo de nuevo en unos instantes.");
    });
  }

  $("f-window").addEventListener("change", loadDetail);
  window.addEventListener("hashchange", function () {
    var target = initialTabFromHash();
    if (detailTabState.panels[target] && detailTabState.active !== target) {
      activateDetailTab(target, { skipHistory: true });
    }
  });

  var initialWindow = new URLSearchParams(window.location.search).get("window");
  if (initialWindow === "24h" || initialWindow === "7d" || initialWindow === "30d") {
    $("f-window").value = initialWindow;
  }

  loadSession()
    .then(function (me) { if (me) { return loadDetail(); } })
    .catch(function () {
      $("main").hidden = false;
      renderEmpty("El servicio no está disponible", "No pudimos comprobar tu sesión. Inténtalo de nuevo en unos minutos.");
    });
})();
