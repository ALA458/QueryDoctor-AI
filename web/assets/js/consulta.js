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
    var titleWrap = el("div");
    titleWrap.appendChild(el("h2", null, "Texto SQL de la consulta"));
    titleWrap.appendChild(el("p", "panel-note", "Literales enmascarados de forma determinista antes de mostrarse"));
    head.appendChild(titleWrap);

    if (!detail.sqlMaskingFailure && !detail.sqlExpired && detail.sqlText && window.QDCore) {
      var copyBtn = el("button", "btn btn-sm btn-secondary", "Copiar SQL");
      copyBtn.type = "button";
      copyBtn.addEventListener("click", function () {
        window.QDCore.copyToClipboard(detail.sqlText).then(function () {
          window.QDCore.showToast("SQL enmascarado copiado al portapapeles.", "ok");
        });
      });
      head.appendChild(copyBtn);
    }
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

      card.id = "finding-card-" + (f.findingCode || f.id);
      card.tabIndex = -1;
      card.appendChild(dl);

      if (f.nodes && f.nodes.length) {
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
        card.appendChild(jumpRow);
      }

      list.appendChild(card);
    });

    section.appendChild(list);
    return section;
  }

  // ---------- Render: Visor jerárquico del plan (S2-10) ----------
  var planViewerState = {
    queryId: null,
    activePlanId: null,
    selectedKey: null,
    onlyFlagged: false,
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
          var card = document.getElementById("finding-card-" + f.findingCode);
          if (card) { card.scrollIntoView({ behavior: "smooth", block: "center" }); }
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
    var filterBtn = el("button", "plan-btn", planViewerState.onlyFlagged ? "Mostrando hallazgos/advertencias" : "Solo hallazgos y advertencias");
    filterBtn.type = "button";
    filterBtn.setAttribute("aria-pressed", planViewerState.onlyFlagged ? "true" : "false");
    filterBtn.addEventListener("click", function () {
      planViewerState.onlyFlagged = !planViewerState.onlyFlagged;
      renderLoadedPlanTree(planView);
    });
    actions.appendChild(filterBtn);

    if (planView.totalOperators > 1) {
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

    var inspector = el("div", "plan-node-inspector");
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
        body.appendChild(stmtBox);
        return;
      }

      var prefixes = buildTreePrefixes(stmt.operators);
      var visibleByFilter = computeVisibleSet(stmt.operators, planViewerState.onlyFlagged);
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
      body.appendChild(stmtBox);
    });

    if (!selectedOpRef && planView.statements[0] && planView.statements[0].operators[0]) {
      selectedOpRef = planView.statements[0].operators[0];
    }
    renderOperatorInspector(inspector, planView.planId, selectedStmtIdx, selectedOpRef);
    body.appendChild(inspector);
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

  function focusNodeInPlanViewer(planId, nodeId) {
    if (!planId) { return; }
    selectAndLoadPlan(planId, nodeId);
  }

  function renderPlanTreePanel(detail) {
    planViewerState.queryId = detail.id;
    planViewerState.cache = {};
    planViewerState.collapsed = {};
    planViewerState.selectedKey = null;
    planViewerState.onlyFlagged = false;
    planViewerState.tabsByPlanId = {};

    var section = el("section", "panel");
    section.id = "plan-viewer-panel";
    var head = el("div", "panel-head");
    head.appendChild(el("h2", null, "Plan de ejecución (árbol jerárquico)"));
    head.appendChild(el("p", "panel-note", "Operadores en jerarquía con costos estimados, advertencias y nodos asociados a hallazgos."));
    section.appendChild(head);

    var plans = detail.plans || [];
    if (!plans.length) {
      section.appendChild(el("p", "panel-note", "Esta consulta todavía no tiene planes de ejecución ingeridos."));
      return section;
    }

    var tabs = el("div", "plan-tabs");
    tabs.setAttribute("role", "tablist");
    tabs.setAttribute("aria-label", "Planes de ejecución de la consulta");

    plans.forEach(function (plan, index) {
      var label = "Plan #" + plan.queryStorePlanId +
        (plan.queryPlanHash ? " (" + plan.queryPlanHash + ")" : "");
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
    section.appendChild(tabs);

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

    var section = el("section", "panel");
    section.id = "explanation-panel";
    var head = el("div", "panel-head");
    head.appendChild(el("h2", null, "Explicación asistida por IA"));
    head.appendChild(el("p", "panel-note", "Cada afirmación se contrasta contra la evidencia del motor antes de mostrarse."));
    section.appendChild(head);

    var body = el("div", "exp-body");
    explanationState.bodyEl = body;
    section.appendChild(body);

    paintExplanation({ kind: "loading" });
    fetchLatestExplanation(token);
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

    var workbench = el("div", "detail-workbench");
    var primaryCol = el("div", "detail-col");
    primaryCol.appendChild(renderSqlPanel(detail));
    primaryCol.appendChild(renderPlanTreePanel(detail));
    primaryCol.appendChild(renderSeriesPanel(detail));

    var asideCol = el("div", "detail-col detail-col-aside");
    asideCol.appendChild(renderExplanationPanel(detail));
    asideCol.appendChild(renderFindingsPanel(detail.findings));

    workbench.appendChild(primaryCol);
    workbench.appendChild(asideCol);
    stack.appendChild(workbench);
    root.appendChild(stack);
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
