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

  function loadSession() {
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
    var section = el("section", "panel");
    var head = el("div", "panel-head");
    head.appendChild(el("h2", null, "Query Health Score y desglose"));
    head.appendChild(el("p", "panel-note", score ? "Calculado el " + formatDateTime(score.calculatedAt) : "Pendiente de cálculo"));
    section.appendChild(head);

    if (!score) {
      section.appendChild(el("p", "panel-note", "Esta consulta aún no tiene un Query Health Score calculado en la última ventana."));
      return section;
    }

    var hero = el("div", "score-hero");

    var dial = el("div", "score-dial");
    dial.appendChild(el("span", "score-number", formatNum(score.score, 1)));
    dial.appendChild(el("span", "score-scale-tag", "Escala 0 – 100 · Prioridad P(q): " + formatNum(score.priority, 1)));

    var badges = el("div", "score-badges");
    badges.appendChild(badge("Referencia provisional: " + (score.bandLabel || score.band), BAND_TONE[score.band] || "neutral"));
    dial.appendChild(badges);

    dial.appendChild(el(
      "p",
      "score-rank",
      "Posición relativa: puesto " + score.rankInWindow + " de " + score.totalQueriesInWindow + " consultas en la ventana"
    ));
    hero.appendChild(dial);

    var meta = el("div", "score-meta-col");
    meta.appendChild(el("p", "scale-direction", "Dirección de la escala: " + score.scaleDirection + "."));
    meta.appendChild(el("p", "disclaimer", score.usageDisclaimer));

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
    hero.appendChild(meta);
    section.appendChild(hero);

    // Desglose de hallazgos contribuyentes a S(q)
    section.appendChild(el("h3", "sub-title", "Hallazgos que contribuyen a S(q)"));
    var contribs = score.contributingFindings || [];
    if (!contribs.length) {
      section.appendChild(el("p", "panel-note", "Sin hallazgos activos en S(q) para este cálculo (S(q) = 0)."));
    } else {
      var wrap = el("div", "table-wrap");
      var table = el("table", "table");
      var thead = el("thead");
      var hrow = el("tr");
      ["Código", "Regla", "Severidad", "Evidencia", "Peso sev.", "Peso conf.", "Aporte"].forEach(function (col) {
        var th = el("th", null, col);
        th.setAttribute("scope", "col");
        hrow.appendChild(th);
      });
      thead.appendChild(hrow);
      table.appendChild(thead);

      var tbody = el("tbody");
      contribs.forEach(function (c) {
        var tr = el("tr");
        tr.appendChild(el("td", null, c.finding_code));
        tr.appendChild(el("td", null, c.rule_id));
        tr.appendChild(el("td", null, c.severity));
        tr.appendChild(el("td", null, c.evidence_class));
        tr.appendChild(el("td", null, formatNum(c.severity_weight, 2)));
        tr.appendChild(el("td", null, formatNum(c.confidence_weight, 2)));
        tr.appendChild(el("td", null, formatNum(c.combined_weight, 3)));
        tbody.appendChild(tr);
      });
      table.appendChild(tbody);
      wrap.appendChild(table);
      section.appendChild(wrap);
    }

    // Historial de scores (RF-SCO-05)
    var history = score.history || [];
    if (history.length > 1) {
      var details = el("details", "details-toggle");
      details.appendChild(el("summary", null, "Historial de cálculos (" + history.length + " puntos)"));
      var hWrap = el("div", "table-wrap");
      var hTable = el("table", "table");
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
        row.appendChild(el("td", null, formatNum(h.score, 2)));
        row.appendChild(el("td", null, formatNum(h.impactComponent, 3)));
        row.appendChild(el("td", null, formatNum(h.severityComponent, 3)));
        row.appendChild(el("td", null, formatNum(h.weightedConsumptionPct, 1) + " %"));
        hBody.appendChild(row);
      });
      hTable.appendChild(hBody);
      hWrap.appendChild(hTable);
      details.appendChild(hWrap);
      section.appendChild(details);
    }

    return section;
  }

  // ---------- Render: SQL ----------
  function renderSqlPanel(detail) {
    var section = el("section", "panel");
    var head = el("div", "panel-head");
    head.appendChild(el("h2", null, "Texto SQL de la consulta"));
    head.appendChild(el("p", "panel-note", "Literales enmascarados de forma determinista antes de mostrarse"));
    section.appendChild(head);

    if (detail.sqlMaskingFailure) {
      section.appendChild(el("p", "panel-note", "No se pudo enmascarar el texto SQL de forma segura (" + detail.sqlMaskingFailure + ")."));
      return section;
    }
    if (detail.sqlExpired || !detail.sqlText) {
      section.appendChild(el("p", "panel-note", "Evidencia expirada o texto SQL no disponible."));
      return section;
    }

    var pre = el("pre", "sql-code");
    pre.appendChild(el("code", null, detail.sqlText));
    section.appendChild(pre);
    return section;
  }

  // ---------- Render: Series por plan ----------
  function buildSvgMetricChart(title, points, planClassMap, valueSelector, decimals) {
    var card = el("div", "chart-card");
    card.appendChild(el("h3", null, title));

    var width = 460;
    var height = 210;
    var padLeft = 56;
    var padRight = 16;
    var padTop = 16;
    var padBottom = 34;
    var plotW = width - padLeft - padRight;
    var plotH = height - padTop - padBottom;

    var times = points.map(function (p) { return new Date(p.intervalStart).getTime(); });
    var minT = Math.min.apply(null, times);
    var maxT = Math.max.apply(null, times);
    if (maxT === minT) { maxT = minT + 1; }

    var values = points.map(valueSelector);
    var maxV = Math.max.apply(null, values);
    if (!(maxV > 0)) { maxV = 1; }

    var svg = svgEl("svg", {
      "class": "chart-svg",
      "viewBox": "0 0 " + width + " " + height,
      "role": "img",
      "aria-label": title
    });

    // Rejilla horizontal y etiquetas Y
    [0, 0.5, 1].forEach(function (ratio) {
      var y = padTop + plotH * (1 - ratio);
      svg.appendChild(svgEl("line", {
        "class": "chart-grid-line",
        "x1": padLeft, "y1": y, "x2": width - padRight, "y2": y
      }));
      var lbl = svgEl("text", {
        "class": "chart-axis-label",
        "x": padLeft - 6, "y": y + 3, "text-anchor": "end"
      });
      lbl.textContent = formatNum(maxV * ratio, decimals);
      svg.appendChild(lbl);
    });

    svg.appendChild(svgEl("line", {
      "class": "chart-axis-line",
      "x1": padLeft, "y1": padTop + plotH, "x2": width - padRight, "y2": padTop + plotH
    }));
    svg.appendChild(svgEl("line", {
      "class": "chart-axis-line",
      "x1": padLeft, "y1": padTop, "x2": padLeft, "y2": padTop + plotH
    }));

    var xStartLabel = svgEl("text", { "class": "chart-axis-label", "x": padLeft, "y": height - 10, "text-anchor": "start" });
    xStartLabel.textContent = formatShortTime(points[0].intervalStart);
    svg.appendChild(xStartLabel);

    var xEndLabel = svgEl("text", { "class": "chart-axis-label", "x": width - padRight, "y": height - 10, "text-anchor": "end" });
    xEndLabel.textContent = formatShortTime(points[points.length - 1].intervalStart);
    svg.appendChild(xEndLabel);

    // Agrupar puntos por PlanId para trazar una serie por plan (CA-HU07.2)
    var byPlan = {};
    points.forEach(function (p) {
      if (!byPlan[p.planId]) { byPlan[p.planId] = []; }
      byPlan[p.planId].push(p);
    });

    Object.keys(byPlan).forEach(function (planId) {
      var cls = planClassMap[planId] || "series-0";
      var planPoints = byPlan[planId];
      var coords = planPoints.map(function (p) {
        var t = new Date(p.intervalStart).getTime();
        var x = padLeft + ((t - minT) / (maxT - minT)) * plotW;
        var y = padTop + plotH - (valueSelector(p) / maxV) * plotH;
        return { x: x.toFixed(1), y: y.toFixed(1), point: p };
      });

      if (coords.length > 1) {
        svg.appendChild(svgEl("polyline", {
          "class": "chart-line " + cls,
          "points": coords.map(function (c) { return c.x + "," + c.y; }).join(" ")
        }));
      }

      coords.forEach(function (c) {
        var circle = svgEl("circle", {
          "class": "chart-dot " + cls,
          "cx": c.x,
          "cy": c.y,
          "r": 4
        });
        var tip = svgEl("title");
        tip.textContent = "Plan #" + c.point.queryStorePlanId + " · " + formatDateTime(c.point.intervalStart) +
          ": " + formatNum(valueSelector(c.point), decimals);
        circle.appendChild(tip);
        svg.appendChild(circle);
      });
    });

    card.appendChild(svg);
    return card;
  }

  function renderSeriesPanel(detail) {
    var section = el("section", "panel");
    var head = el("div", "panel-head");
    head.appendChild(el("h2", null, "Series temporales por plan"));
    head.appendChild(el("p", "panel-note",
      "Ventana " + detail.window + " (" + formatDateTime(detail.windowStart) + " – " + formatDateTime(detail.windowEnd) + ")"));
    section.appendChild(head);

    var plans = detail.plans || [];
    var series = detail.series || [];
    var planClassMap = {};
    plans.forEach(function (plan, idx) {
      planClassMap[plan.planId] = "series-" + (idx % 4);
    });

    if (plans.length > 0) {
      var legend = el("div", "plan-legend");
      plans.forEach(function (plan, idx) {
        var cls = "series-" + (idx % 4);
        var item = el("span", "legend-item");
        item.appendChild(el("span", "legend-swatch " + cls));
        var label = "Plan #" + plan.queryStorePlanId;
        if (plan.queryPlanHash) { label += " (" + plan.queryPlanHash.slice(0, 10) + ")"; }
        item.appendChild(el("span", null, label));
        legend.appendChild(item);
      });
      section.appendChild(legend);
    }

    if (!series.length) {
      section.appendChild(el("p", "panel-note", "No hay intervalos de ejecución registrados en la ventana seleccionada."));
      return section;
    }

    var chartsGrid = el("div", "charts-grid");
    chartsGrid.appendChild(buildSvgMetricChart("Duración promedio (ms)", series, planClassMap, function (p) { return p.avgDurationMs; }, 2));
    chartsGrid.appendChild(buildSvgMetricChart("CPU promedio (ms)", series, planClassMap, function (p) { return p.avgCpuTimeMs; }, 2));
    chartsGrid.appendChild(buildSvgMetricChart("Lecturas lógicas promedio", series, planClassMap, function (p) { return p.avgLogicalIoReads; }, 1));
    section.appendChild(chartsGrid);

    var details = el("details", "details-toggle");
    details.setAttribute("open", "");
    details.appendChild(el("summary", null, "Tabla de intervalos por plan (" + series.length + " muestras)"));

    var wrap = el("div", "table-wrap");
    var table = el("table", "table");
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
    series.forEach(function (p) {
      var tr = el("tr");
      tr.appendChild(el("td", null, formatDateTime(p.intervalStart)));
      tr.appendChild(el("td", null, "Plan #" + p.queryStorePlanId));
      tr.appendChild(el("td", null, formatNum(p.executionCount, 0)));
      tr.appendChild(el("td", null, formatNum(p.avgDurationMs, 2)));
      tr.appendChild(el("td", null, formatNum(p.avgCpuTimeMs, 2)));
      tr.appendChild(el("td", null, formatNum(p.avgLogicalIoReads, 1)));
      tbody.appendChild(tr);
    });
    table.appendChild(tbody);
    wrap.appendChild(table);
    details.appendChild(wrap);
    section.appendChild(details);

    return section;
  }

  // ---------- Render: Hallazgos con evidencia ----------
  function addKv(dl, label, value) {
    var wrap = el("div", "kv-item");
    wrap.appendChild(el("dt", null, label));
    wrap.appendChild(el("dd", null, value));
    dl.appendChild(wrap);
  }

  function renderFindingsPanel(findings) {
    var section = el("section", "panel");
    var head = el("div", "panel-head");
    head.appendChild(el("h2", null, "Hallazgos y evidencia determinista"));
    head.appendChild(el("p", "panel-note", "Cada hallazgo es una señal con evidencia a investigar, no un diagnóstico concluyente."));
    section.appendChild(head);

    if (!findings || !findings.length) {
      section.appendChild(el("p", "panel-note", "No se detectaron hallazgos determinísticos para los planes analizados de esta consulta."));
      return section;
    }

    var list = el("div", "finding-list");
    findings.forEach(function (f) {
      var card = el("article", "finding-card");
      card.setAttribute("data-severity", f.severity || "");

      var cardHead = el("div", "finding-card-head");
      var title = el("strong", null, f.ruleId + " · " + f.ruleName + " (" + f.findingCode + ")");
      cardHead.appendChild(title);

      var badges = el("div", "finding-badges");
      var sevEntry = SEVERITY_TONE[f.severity];
      badges.appendChild(badge(sevEntry ? sevEntry[0] : f.severity, sevEntry ? sevEntry[1] : "neutral"));
      badges.appendChild(badge(f.kind, "neutral"));
      badges.appendChild(badge("Evidencia: " + f.evidenceClass, "neutral"));
      badges.appendChild(badge("Plan #" + f.queryStorePlanId, "neutral"));
      cardHead.appendChild(badges);
      card.appendChild(cardHead);

      var dl = el("dl", "kv-grid");
      if (f.nodes && f.nodes.length) {
        addKv(dl, "Nodos del plan", f.nodes.map(function (n) { return "Nodo " + n.node_id + " (" + n.physical_op + ")"; }).join(", "));
      }
      if (f.context) {
        if (f.context.executions !== null && f.context.executions !== undefined) {
          addKv(dl, "Ejecuciones", formatNum(f.context.executions, 0));
        }
        if (f.context.query_consumption_pct !== null && f.context.query_consumption_pct !== undefined) {
          addKv(dl, "Consumo en ventana", formatNum(f.context.query_consumption_pct, 2) + " %");
        }
        if (f.context.estimated_rows !== null && f.context.estimated_rows !== undefined) {
          addKv(dl, "Filas estimadas", formatNum(f.context.estimated_rows, 2));
        }
        if (f.context.estimated_rows_read !== null && f.context.estimated_rows_read !== undefined) {
          addKv(dl, "Filas leídas est.", formatNum(f.context.estimated_rows_read, 2));
        }
      }

      if (f.evidenceMaskingFailure) {
        addKv(dl, "Evidencia", "Omitida por enmascarado seguro (" + f.evidenceMaskingFailure + ")");
      } else if (f.evidence) {
        Object.keys(f.evidence).forEach(function (k) {
          addKv(dl, "Evidencia · " + k, f.evidence[k]);
        });
      }

      if (f.thresholdsUsed && Object.keys(f.thresholdsUsed).length) {
        var thText = Object.keys(f.thresholdsUsed).map(function (k) { return k + "=" + f.thresholdsUsed[k]; }).join(", ");
        addKv(dl, "Umbrales aplicados", thText);
      }
      if (f.engineVersion) {
        addKv(dl, "Versión SQL / Catálogo", f.engineVersion + " · cat. " + f.catalogVersion);
      }

      card.appendChild(dl);
      list.appendChild(card);
    });

    section.appendChild(list);
    return section;
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
    stack.appendChild(renderScorePanel(detail.score));
    stack.appendChild(renderSqlPanel(detail));
    stack.appendChild(renderSeriesPanel(detail));
    stack.appendChild(renderFindingsPanel(detail.findings));
    root.appendChild(stack);
  }

  function getQueryIdFromUrl() {
    var params = new URLSearchParams(window.location.search);
    return params.get("id");
  }

  function loadDetail() {
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
