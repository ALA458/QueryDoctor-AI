/* Query Doctor AI — Explorador de consultas (/consultas) con tarjetas legibles sin recortes. */
(function () {
  "use strict";

  var C = window.QDCore;
  var $ = C.$;

  var SEVERITY = {
    High: ["Alta", "danger"], Alta: ["Alta", "danger"],
    Medium: ["Media", "warn"], Media: ["Media", "warn"],
    Low: ["Baja", "neutral"], Baja: ["Baja", "neutral"],
    Info: ["Informativo", "neutral"]
  };

  var BANDS = {
    Critica: ["Crítica", "danger"], "Crítica": ["Crítica", "danger"],
    Atencion: ["Atención", "warn"], "Atención": ["Atención", "warn"],
    Saludable: ["Saludable", "ok"]
  };

  var SQL_PREVIEW_LIMIT = 280;
  var connectionNamesById = {};

  function showNotice(message) {
    var node = $("notice");
    node.textContent = message || "";
    node.className = message ? "notice is-visible" : "notice";
  }

  function renderEmpty(title, text) {
    var list = $("list");
    C.clear(list);
    var box = C.el("div", "empty surface-card");
    box.appendChild(C.el("h2", null, title));
    box.appendChild(C.el("p", null, text));
    list.appendChild(box);
  }

  function formatPct(value) {
    if (typeof value !== "number" || isNaN(value)) { return "—"; }
    return value.toLocaleString("es-CO", { minimumFractionDigits: 1, maximumFractionDigits: 1 }) + " %";
  }

  function formatInt(value) {
    return typeof value === "number" ? value.toLocaleString("es-CO") : "—";
  }

  function applyInitialParamsFromUrl() {
    var params = new URLSearchParams(window.location.search);
    var win = params.get("window");
    if (win === "24h" || win === "7d" || win === "30d") { $("f-window").value = win; }
    var sev = params.get("severity");
    if (sev === "High" || sev === "Medium" || sev === "Low") { $("f-severity").value = sev; }
    var sort = params.get("sort");
    if (sort === "consumption" || sort === "score") { $("f-sort").value = sort; }
    return params.get("connectionId") || "";
  }

  function loadConnectionOptions(preselectId) {
    return C.apiRequest("/api/connections").then(function (res) {
      if (res.redirected || !res.ok || !Array.isArray(res.data)) { return; }
      var select = $("f-connection");
      res.data.forEach(function (item) {
        var label = item.server + " / " + item.databaseName;
        connectionNamesById[item.id] = label;
        var option = C.el("option", null, label);
        option.value = item.id;
        select.appendChild(option);
      });
      if (preselectId && connectionNamesById[preselectId]) {
        select.value = preselectId;
      }
    }).catch(function () { /* noop */ });
  }

  function renderSummaryStrip(items) {
    var host = $("q-summary-strip");
    C.clear(host);

    var list = items || [];
    var total = list.length;
    var critical = list.filter(function (q) { return q.band === "Critica" || q.band === "Crítica"; }).length;
    var withFindings = list.filter(function (q) { return Array.isArray(q.findings) && q.findings.length > 0; }).length;
    var scored = list.filter(function (q) { return typeof q.score === "number" && !isNaN(q.score); });
    var lowestScore = scored.length ? Math.min.apply(null, scored.map(function (q) { return q.score; })) : null;

    var cards = [
      ["Consultas visibles", total, "Ventana " + $("f-window").value, "info"],
      ["Con hallazgos", withFindings, total > 0 ? Math.round((withFindings / total) * 100) + " % del listado" : "Sin registros", withFindings > 0 ? "warn" : "ok"],
      ["En banda Crítica", critical, "Atención prioritaria", critical > 0 ? "danger" : "ok"],
      ["Menor Health Score", lowestScore !== null ? C.formatNumber(lowestScore, 1) : "—", "Escala 0–100 (menor = más severa)", lowestScore !== null && lowestScore < 40 ? "danger" : "neutral"]
    ];

    cards.forEach(function (c) {
      var card = C.el("article", "kpi-card");
      card.setAttribute("data-tone", c[3]);
      card.appendChild(C.el("div", "kpi-label", c[0]));
      card.appendChild(C.el("div", "kpi-value", String(c[1])));
      card.appendChild(C.el("div", "kpi-meta", c[2]));
      host.appendChild(card);
    });
  }

  function renderFilterChips() {
    var chips = $("q-active-chips");
    var resetBtn = $("f-reset");
    C.clear(chips);

    var conn = $("f-connection").value;
    var win = $("f-window").value;
    var sev = $("f-severity").value;
    var sort = $("f-sort").value;

    var active = [];
    if (conn) { active.push("Conexión: " + (connectionNamesById[conn] || conn)); }
    if (win !== "24h") { active.push("Ventana: " + win); }
    if (sev) { active.push("Severidad: " + ((SEVERITY[sev] && SEVERITY[sev][0]) || sev)); }
    if (sort !== "consumption") { active.push("Orden: Query Health Score"); }

    chips.hidden = active.length === 0;
    resetBtn.hidden = active.length === 0;
    active.forEach(function (text) {
      chips.appendChild(C.el("span", "chip", text));
    });
  }

  function renderQueryCards(items) {
    var list = $("list");
    C.clear(list);

    var stack = C.el("div", "query-list");
    var currentWindow = $("f-window").value || "24h";

    items.forEach(function (item) {
      var card = C.el("article", "query-card");
      if (item.band) { card.setAttribute("data-band", item.band); }

      var head = C.el("div", "qc-head");
      var metrics = C.el("div", "qc-metrics");

      metrics.appendChild(C.el("span", "qc-source", (item.server ? item.server + " / " : "") + (item.databaseName || "")));

      if (typeof item.score === "number" && !isNaN(item.score)) {
        var scoreBox = C.el("div", "qc-score");
        scoreBox.appendChild(C.el("span", "qc-score-val", C.formatNumber(item.score, 1)));
        if (item.band) {
          var b = BANDS[item.band];
          scoreBox.appendChild(C.badge(b ? b[0] : item.band, b ? b[1] : "neutral"));
        }
        metrics.appendChild(scoreBox);
      } else {
        metrics.appendChild(C.badge("Sin score", "neutral"));
      }

      var pct = typeof item.consumptionPct === "number" ? Math.max(0, Math.min(100, item.consumptionPct)) : 0;
      var meter = C.el("div", "meter");
      var track = C.el("span", "meter-track");
      var bar = C.el("span", "meter-bar");
      bar.style.width = pct + "%";
      track.appendChild(bar);
      meter.appendChild(track);
      meter.appendChild(C.el("span", "meter-value", formatPct(item.consumptionPct)));
      metrics.appendChild(meter);

      metrics.appendChild(C.el("span", "qc-execs", formatInt(item.executions) + " ejec."));
      head.appendChild(metrics);

      var openLink = C.el("a", "btn btn-sm", "Diagnosticar →");
      openLink.href = "/consulta?id=" + encodeURIComponent(item.id) + "&window=" + encodeURIComponent(currentWindow);
      head.appendChild(openLink);
      card.appendChild(head);

      // Bloque SQL completo/expandible (nunca cortado al seleccionar texto)
      var rawSql = item.sqlPreview || (item.sqlExpired ? "[SQL expirado por retención]" : "[Sin texto SQL]");
      var sqlBox = C.el("div", "qc-sql-box");
      var expanded = false;
      var needsCollapse = rawSql.length > SQL_PREVIEW_LIMIT;
      var codeEl = C.el("code", "qc-sql-code", needsCollapse ? (rawSql.slice(0, SQL_PREVIEW_LIMIT) + "…") : rawSql);
      sqlBox.appendChild(codeEl);

      if (needsCollapse) {
        var toggleBtn = C.el("button", "qc-sql-toggle", "Ver sentencia completa (" + rawSql.length + " car.)");
        toggleBtn.type = "button";
        toggleBtn.addEventListener("click", function () {
          expanded = !expanded;
          codeEl.textContent = expanded ? rawSql : (rawSql.slice(0, SQL_PREVIEW_LIMIT) + "…");
          toggleBtn.textContent = expanded ? "Contraer sentencia" : ("Ver sentencia completa (" + rawSql.length + " car.)");
        });
        sqlBox.appendChild(toggleBtn);
      }
      card.appendChild(sqlBox);

      var findRow = C.el("div", "qc-findings");
      var findings = item.findings || [];
      if (!findings.length) {
        findRow.appendChild(C.el("span", "no-findings", "Sin hallazgos determinísticos en el último análisis"));
      } else {
        findings.forEach(function (f) {
          var pill = C.el("div", "qc-finding-pill");
          pill.appendChild(C.el("strong", "mono", f.ruleId));
          var s = SEVERITY[f.severity];
          pill.appendChild(C.badge(s ? s[0] : "Sin dato", s ? s[1] : "neutral"));
          if (f.summary) { pill.appendChild(C.el("span", null, f.summary)); }
          findRow.appendChild(pill);
        });
      }
      card.appendChild(findRow);

      stack.appendChild(card);
    });

    list.appendChild(stack);
  }

  function loadQueries() {
    showNotice("");
    renderFilterChips();

    var params = new URLSearchParams();
    params.set("window", $("f-window").value);
    params.set("sort", $("f-sort").value || "consumption");
    if ($("f-connection").value) { params.set("connectionId", $("f-connection").value); }
    if ($("f-severity").value) { params.set("severity", $("f-severity").value); }

    return C.apiRequest("/api/queries?" + params.toString()).then(function (res) {
      if (res.redirected) { return; }
      if (res.status === 403) {
        renderSummaryStrip([]);
        renderEmpty("Sin acceso", "Tu cuenta no tiene permiso para ver consultas.");
        return;
      }
      if (!res.ok) { throw new Error("queries " + res.status); }
      var items = res.data || [];
      renderSummaryStrip(items);
      if (!items.length) {
        renderEmpty("No hay consultas para estos filtros", "Prueba ampliar la ventana de tiempo, limpiar la severidad o ejecutar una ingesta desde el Centro de control.");
        return;
      }
      renderQueryCards(items);
    }).catch(function () {
      renderEmpty("El servicio no está disponible", "No pudimos cargar las consultas. Inténtalo de nuevo en unos minutos.");
    });
  }

  ["f-connection", "f-window", "f-severity", "f-sort"].forEach(function (id) {
    $(id).addEventListener("change", loadQueries);
  });
  $("filters").addEventListener("submit", function (event) { event.preventDefault(); });
  $("f-reset").addEventListener("click", function () {
    $("f-connection").value = "";
    $("f-window").value = "24h";
    $("f-severity").value = "";
    $("f-sort").value = "consumption";
    loadQueries();
  });

  var initialConn = applyInitialParamsFromUrl();
  C.mountShell({ activeNav: "queries" }).then(function (ctx) {
    if (!ctx) { return; }
    return loadConnectionOptions(initialConn).then(loadQueries);
  }).catch(function () {
    $("main").hidden = false;
    renderEmpty("El servicio no está disponible", "No pudimos comprobar tu sesión. Inténtalo de nuevo en unos minutos.");
  });
})();
