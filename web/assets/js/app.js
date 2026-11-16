/* Query Doctor AI — Centro de control (/): KPIs de flota, onboarding y drawer de conexión. */
(function () {
  "use strict";

  var C = window.QDCore;
  var $ = C.$;

  var appState = {
    me: null,
    connections: [],
    selectedId: null,
    activeTab: "caps",
    ingestPollTimer: null,
    ingestPollCount: 0,
    detail: null,
    runs: []
  };

  function showNotice(node, message, ok) {
    node.textContent = message || "";
    node.className = message ? ("notice is-visible" + (ok ? " is-ok" : "")) : "notice";
  }

  function buildKpiCard(label, value, meta, tone) {
    var card = C.el("article", "kpi-card");
    if (tone) { card.setAttribute("data-tone", tone); }
    card.appendChild(C.el("div", "kpi-label", label));
    card.appendChild(C.el("div", "kpi-value", String(value)));
    card.appendChild(C.el("div", "kpi-meta", meta));
    return card;
  }

  function renderKpis(items) {
    var strip = $("kpi-strip");
    C.clear(strip);

    var total = items.length;
    var active = items.filter(function (c) { return c.status === "Active"; }).length;
    var issues = items.filter(function (c) { return c.status === "Failed" || c.status === "Rejected" || c.hasExcessivePermissions; }).length;
    var qsReady = items.filter(function (c) { return c.queryStoreState === "READ_WRITE"; }).length;
    var latestIngest = items
      .map(function (c) { return c.lastIngestionAt; })
      .filter(Boolean)
      .sort()
      .pop();

    strip.appendChild(buildKpiCard("Instancias", total, C.isDba(appState.me) ? "Registradas en plataforma" : "Asignadas a tu usuario", "info"));
    strip.appendChild(buildKpiCard("Activas", active + " / " + total, active === total && total > 0 ? "Listas para recolección" : "Sondeo verificado", active > 0 ? "ok" : "neutral"));
    strip.appendChild(buildKpiCard("Query Store activo", qsReady, "En modo READ_WRITE", qsReady === total && total > 0 ? "ok" : "warn"));
    strip.appendChild(buildKpiCard("Última ingesta", latestIngest ? C.formatRelativeTime(latestIngest) : "Sin ingestas", issues > 0 ? (issues + " con alertas de configuración") : "Sin incidencias críticas", issues > 0 ? "danger" : "ok"));
  }

  function renderSpotlight(items) {
    var host = $("spotlight-host");
    C.clear(host);
    if (items.length > 0) { return; }

    var box = C.el("section", "spotlight");
    var left = C.el("div");

    if (C.isDba(appState.me)) {
      left.appendChild(C.el("p", "eyebrow", "Primeros pasos"));
      left.appendChild(C.el("h2", null, "Conecta tu primera base SQL Server en menos de 3 minutos"));
      left.appendChild(C.el("p", null, "El asistente guiado prepara la cuenta de solo lectura, cifra credenciales en envío, verifica Query Store en vivo y dispara la primera ingesta para diagnosticar consultas."));
      var cta = C.el("a", "btn btn-auto spotlight-cta", "Iniciar asistente de conexión →");
      cta.href = "/conectar";
      left.appendChild(cta);
      box.appendChild(left);

      var steps = C.el("ol", "spotlight-steps");
      [
        ["1", "Copia el script T-SQL de privilegio mínimo (VIEW DATABASE STATE)."],
        ["2", "Ingresa servidor, base y credenciales cifradas por TLS."],
        ["3", "Verifica el sondeo en vivo de capacidades y Query Store."],
        ["4", "Lanza la primera ingesta e inspecciona hallazgos y Health Score."]
      ].forEach(function (pair) {
        var li = C.el("li");
        li.appendChild(C.el("span", "step-num", pair[0]));
        li.appendChild(C.el("span", null, pair[1]));
        steps.appendChild(li);
      });
      box.appendChild(steps);
    } else {
      left.appendChild(C.el("p", "eyebrow", "Acceso de Desarrollador"));
      left.appendChild(C.el("h2", null, "Aún no tienes conexiones asignadas"));
      left.appendChild(C.el("p", null, "Solicita al administrador (DBA) que te asigne una instancia monitoreada para consultar su telemetría, planes de ejecución y explicaciones IA."));
      box.appendChild(left);
    }

    host.appendChild(box);
  }

  function renderTable(items) {
    var list = $("list");
    C.clear(list);
    if (!items.length) { return; }

    var card = C.el("div", "table-card");
    var wrap = C.el("div", "table-wrap");
    var table = C.el("table", "table");

    var thead = C.el("thead");
    var headRow = C.el("tr");
    ["Instancia y base", "Plataforma", "Soporte y motor", "Query Store", "Última ingesta", "Estado", "Acciones"].forEach(function (col) {
      var th = C.el("th", null, col);
      th.setAttribute("scope", "col");
      headRow.appendChild(th);
    });
    thead.appendChild(headRow);
    table.appendChild(thead);

    var tbody = C.el("tbody");
    items.forEach(function (item) {
      var tr = C.el("tr", "is-clickable" + (appState.selectedId === item.id ? " is-selected" : ""));
      tr.addEventListener("click", function (ev) {
        if (ev.target && ev.target.closest && ev.target.closest("a, button")) { return; }
        openDrawer(item.id, "caps");
      });

      var tdConn = C.el("td");
      tdConn.appendChild(C.el("span", "primary", item.server));
      tdConn.appendChild(C.el("span", "secondary", item.databaseName));
      if (item.hasExcessivePermissions) {
        tdConn.appendChild(C.el("span", "alert-inline", "Permisos excesivos: restringir a solo lectura."));
      }
      if (item.failureMessage) {
        tdConn.appendChild(C.el("span", "alert-inline", item.failureMessage));
      }
      tr.appendChild(tdConn);

      tr.appendChild(C.el("td", null, C.PLATFORM_LABELS[item.platform] || item.platform || "—"));

      var tdSup = C.el("td");
      tdSup.appendChild(C.badgeFromMap(C.SUPPORT_META, item.supportLevel));
      if (item.engineVersion) { tdSup.appendChild(C.el("span", "secondary", "v" + item.engineVersion)); }
      tr.appendChild(tdSup);

      var tdQs = C.el("td");
      tdQs.appendChild(C.badgeFromMap(C.QUERY_STORE_META, item.queryStoreState));
      tr.appendChild(tdQs);

      var tdIng = C.el("td", "tabular", item.lastIngestionAt ? C.formatRelativeTime(item.lastIngestionAt) : "Aún sin ingesta");
      tr.appendChild(tdIng);

      var tdStatus = C.el("td");
      var statusMeta = C.CONNECTION_STATUS_META[item.status] || { label: item.status || "—", tone: "neutral" };
      var statusWrap = C.el("span");
      var dot = C.el("span", "dot");
      dot.setAttribute("data-tone", statusMeta.tone);
      statusWrap.appendChild(dot);
      statusWrap.appendChild(C.badge(statusMeta.label, statusMeta.tone));
      tdStatus.appendChild(statusWrap);
      tr.appendChild(tdStatus);

      var tdAct = C.el("td");
      var actWrap = C.el("div", "cell-actions");
      var inspectBtn = C.el("button", "btn btn-sm btn-secondary", "Inspeccionar");
      inspectBtn.type = "button";
      inspectBtn.addEventListener("click", function () { openDrawer(item.id, "caps"); });
      actWrap.appendChild(inspectBtn);

      var queriesLink = C.el("a", "btn btn-sm", "Consultas →");
      queriesLink.href = "/consultas?connectionId=" + encodeURIComponent(item.id);
      actWrap.appendChild(queriesLink);
      tdAct.appendChild(actWrap);
      tr.appendChild(tdAct);

      tbody.appendChild(tr);
    });

    table.appendChild(tbody);
    wrap.appendChild(table);
    card.appendChild(wrap);
    list.appendChild(card);
  }

  function loadConnections() {
    showNotice($("notice"), "");
    return C.apiRequest("/api/connections").then(function (res) {
      if (res.redirected) { return; }
      if (!res.ok) { throw new Error("connections " + res.status); }
      appState.connections = res.data || [];
      renderKpis(appState.connections);
      renderSpotlight(appState.connections);
      renderTable(appState.connections);
    }).catch(function () {
      showNotice($("notice"), "No pudimos cargar las conexiones en este momento.", false);
    });
  }

  // ---------- Drawer ----------
  function stopIngestPoll() {
    if (appState.ingestPollTimer) {
      clearTimeout(appState.ingestPollTimer);
      appState.ingestPollTimer = null;
    }
  }

  function closeDrawer() {
    stopIngestPoll();
    appState.selectedId = null;
    $("drawer").hidden = true;
    $("drawer-backdrop").hidden = true;
    renderTable(appState.connections);
  }

  function setDrawerTab(tab) {
    appState.activeTab = tab;
    ["caps", "ingest", "retention"].forEach(function (name) {
      var btn = $("tab-btn-" + name);
      var sec = $("tab-" + name);
      if (btn) { btn.setAttribute("aria-selected", name === tab ? "true" : "false"); }
      if (sec) { sec.hidden = name !== tab; }
    });
  }

  function addFact(grid, label, valueOrNode) {
    var item = C.el("div", "fact-item");
    item.appendChild(C.el("dt", null, label));
    var dd = C.el("dd");
    if (typeof valueOrNode === "string") { dd.textContent = valueOrNode; }
    else { dd.appendChild(valueOrNode); }
    item.appendChild(dd);
    grid.appendChild(item);
  }

  function renderCapsTab() {
    var host = $("tab-caps");
    C.clear(host);
    var d = appState.detail;
    if (!d) { host.appendChild(C.el("p", "panel-note", "Cargando capacidades...")); return; }

    var grid = C.el("dl", "fact-grid");
    addFact(grid, "Plataforma", C.PLATFORM_LABELS[d.platform] || d.platform);
    addFact(grid, "Estado", C.badgeFromMap(C.CONNECTION_STATUS_META, d.status, d.status));
    addFact(grid, "Último sondeo", C.formatDateTime(d.lastProbedAt, "Pendiente"));
    addFact(grid, "Última ingesta", C.formatDateTime(d.lastIngestionAt, "Sin ingestas"));

    if (d.capabilities) {
      var caps = d.capabilities;
      var qs = caps.queryStore || {};
      addFact(grid, "Versión SQL Server", (caps.engineVersion || "—") + (caps.edition ? " · " + caps.edition : ""));
      addFact(grid, "Nivel de soporte", C.badgeFromMap(C.SUPPORT_META, caps.supportLevel));
      addFact(grid, "Query Store", C.badgeFromMap(C.QUERY_STORE_META, qs.actualState));
      addFact(grid, "Modo captura / intervalo", (qs.captureMode || "—") + " · " + (qs.intervalLengthMinutes ? qs.intervalLengthMinutes + " min" : "—"));
      addFact(grid, "Retención en SQL Server", qs.retentionDays ? (qs.retentionDays + " días") : "—");
      addFact(grid, "Privilegios mínimos", caps.hasExcessivePermissions ? C.badge("Permisos de escritura detectados", "danger") : C.badge("Solo lectura verificado", "ok"));
    }
    host.appendChild(grid);

    if (d.failureMessage) {
      host.appendChild(C.el("div", "notice is-visible", "Fallo reportado (" + (d.failureCause || "Error") + "): " + d.failureMessage));
    }

    if (d.capabilities && Array.isArray(d.capabilities.omittedFeatures) && d.capabilities.omittedFeatures.length > 0) {
      var omCard = C.el("div", "surface-card");
      omCard.appendChild(C.el("p", "eyebrow", "Funciones omitidas por versión"));
      d.capabilities.omittedFeatures.forEach(function (f) {
        omCard.appendChild(C.el("p", "panel-note", f.code + ": " + f.message));
      });
      host.appendChild(omCard);
    }

    var ctaRow = C.el("div", "btn-row");
    var qLink = C.el("a", "btn btn-sm", "Ver consultas de esta conexión →");
    qLink.href = "/consultas?connectionId=" + encodeURIComponent(d.id);
    ctaRow.appendChild(qLink);

    if (C.isDba(appState.me)) {
      var assignLink = C.el("a", "btn btn-sm btn-secondary", "Gestionar asignaciones");
      assignLink.href = "/accesos?connectionId=" + encodeURIComponent(d.id);
      ctaRow.appendChild(assignLink);
    }
    host.appendChild(ctaRow);
  }

  function renderIngestTab() {
    var host = $("tab-ingest");
    C.clear(host);
    var d = appState.detail;

    if (C.isDba(appState.me)) {
      var topBar = C.el("div", "btn-row");
      var runBtn = C.el("button", "btn btn-sm", "Ingerir ahora");
      runBtn.type = "button";
      runBtn.id = "drawer-run-ingest";
      runBtn.disabled = !d || d.status !== "Active";
      runBtn.addEventListener("click", function () { triggerManualIngestion(d.id); });
      topBar.appendChild(runBtn);

      if (d && d.status !== "Active") {
        topBar.appendChild(C.el("span", "panel-note", "Solo se pueden ingerir conexiones en estado Activa."));
      }
      host.appendChild(topBar);
    }

    var runs = appState.runs || [];
    if (!runs.length) {
      host.appendChild(C.el("p", "panel-note", "Todavía no se han ejecutado ingestas en esta conexión."));
      return;
    }

    var timeline = C.el("div", "timeline");
    runs.forEach(function (run) {
      var card = C.el("div", "timeline-item");
      var top = C.el("div", "timeline-top");
      top.appendChild(C.el("strong", null, (C.INGESTION_TRIGGER_LABELS[run.trigger] || run.trigger) + " · " + C.formatDateTime(run.startedAt)));
      top.appendChild(C.badgeFromMap(C.INGESTION_STATUS_META, run.status, run.status));
      card.appendChild(top);

      var rows = typeof run.rowsUpserted === "number" ? (run.rowsUpserted + " filas") : "En proceso";
      card.appendChild(C.el("div", "timeline-meta", rows + (run.finishedAt ? (" · Fin: " + C.formatDateTime(run.finishedAt)) : "")));
      if (run.causeMessage) {
        card.appendChild(C.el("p", "panel-note", run.causeMessage));
      }
      timeline.appendChild(card);
    });
    host.appendChild(timeline);
  }

  function renderRetentionTab() {
    var host = $("tab-retention");
    C.clear(host);
    var d = appState.detail;
    if (!d || !C.isDba(appState.me)) { return; }

    var info = C.el("div", "surface-card");
    info.appendChild(C.el("p", "eyebrow", "Política de retención (RNF-SEG-10)"));
    info.appendChild(C.el("p", "panel-note", "La purga automática elimina textos SQL y planes XML cifrados al cumplirse el plazo de retención (por defecto 30 días). Desde aquí puedes ejecutar un borrado inmediato de texto SQL y planes XML de esta conexión."));
    host.appendChild(info);

    var zone = C.el("div", "danger-zone");
    zone.appendChild(C.el("h3", null, "Purgar evidencia SQL y planes XML"));
    zone.appendChild(C.el("p", null, "Esta acción limpia EncryptedSqlText y EncryptedPlanXml de la base '" + d.databaseName + "'. La cadena de auditoría audit.* y los metadatos numéricos permanecen intactos y verificables."));

    var confirmField = C.el("div", "field");
    var lbl = C.el("label", null, "Escribe PURGAR para habilitar el borrado:");
    lbl.setAttribute("for", "confirm-purge-input");
    confirmField.appendChild(lbl);
    var ctrl = C.el("div", "control");
    var input = C.el("input");
    input.id = "confirm-purge-input";
    input.type = "text";
    input.placeholder = "PURGAR";
    input.autocomplete = "off";
    ctrl.appendChild(input);
    confirmField.appendChild(ctrl);
    zone.appendChild(confirmField);

    var purgeBtn = C.el("button", "btn btn-sm btn-danger", "Purgar datos SQL y XML");
    purgeBtn.type = "button";
    purgeBtn.disabled = true;
    input.addEventListener("input", function () {
      purgeBtn.disabled = input.value.trim() !== "PURGAR";
    });
    purgeBtn.addEventListener("click", function () {
      purgeBtn.disabled = true;
      C.apiRequest("/api/connections/" + encodeURIComponent(d.id) + "/data", { method: "DELETE" }).then(function (res) {
        if (res.redirected) { return; }
        if (res.ok && res.data) {
          input.value = "";
          showNotice($("drawer-notice"), "Purga completada: " + res.data.textsCleared + " textos SQL y " + res.data.plansCleared + " planes XML eliminados.", true);
          C.showToast("Evidencia SQL/XML purgada y registrada en auditoría.", "ok");
          loadConnections();
          return;
        }
        var lines = C.collectProblemMessages(res.problem);
        showNotice($("drawer-notice"), lines.length ? lines.join(" ") : "No pudimos completar la purga.", false);
        purgeBtn.disabled = false;
      }).catch(function () {
        showNotice($("drawer-notice"), "Error de comunicación con la API.", false);
        purgeBtn.disabled = false;
      });
    });
    zone.appendChild(purgeBtn);
    host.appendChild(zone);
  }

  function refreshIngestRuns(connectionId, keepPolling) {
    return C.apiRequest("/api/connections/" + encodeURIComponent(connectionId) + "/ingestions").then(function (res) {
      if (res.redirected || !res.ok) { return; }
      appState.runs = res.data || [];
      renderIngestTab();
      var hasRunning = appState.runs.some(function (r) { return r.status === "Running"; });
      if (keepPolling && hasRunning && appState.selectedId === connectionId && appState.ingestPollCount < 25) {
        appState.ingestPollCount += 1;
        appState.ingestPollTimer = setTimeout(function () { refreshIngestRuns(connectionId, true); }, 2000);
      } else if (!hasRunning && keepPolling) {
        loadConnections();
      }
    });
  }

  function triggerManualIngestion(connectionId) {
    showNotice($("drawer-notice"), "");
    var btn = $("drawer-run-ingest");
    if (btn) { btn.disabled = true; }
    C.apiRequest("/api/connections/" + encodeURIComponent(connectionId) + "/ingestions", { method: "POST" }).then(function (res) {
      if (res.redirected) { return; }
      if (res.ok || res.status === 409) {
        C.showToast(res.status === 409 ? "Ya existe una ingesta en ejecución." : "Ingesta encolada en el Worker.", res.status === 409 ? "info" : "ok");
        appState.ingestPollCount = 0;
        refreshIngestRuns(connectionId, true);
        return;
      }
      var lines = C.collectProblemMessages(res.problem);
      showNotice($("drawer-notice"), lines.length ? lines.join(" ") : "No pudimos iniciar la ingesta.", false);
      if (btn) { btn.disabled = false; }
    });
  }

  function openDrawer(connectionId, initialTab) {
    stopIngestPoll();
    appState.selectedId = connectionId;
    appState.detail = null;
    appState.runs = [];
    showNotice($("drawer-notice"), "");
    $("drawer").hidden = false;
    $("drawer-backdrop").hidden = false;
    setDrawerTab(initialTab || "caps");
    renderTable(appState.connections);

    Promise.all([
      C.apiRequest("/api/connections/" + encodeURIComponent(connectionId)),
      C.apiRequest("/api/connections/" + encodeURIComponent(connectionId) + "/ingestions")
    ]).then(function (results) {
      var detailRes = results[0];
      var runsRes = results[1];
      if (detailRes.ok && detailRes.data) {
        appState.detail = detailRes.data;
        $("drawer-title").textContent = detailRes.data.server;
        $("drawer-sub").textContent = detailRes.data.databaseName + " · " + (C.PLATFORM_LABELS[detailRes.data.platform] || detailRes.data.platform);
      }
      appState.runs = runsRes.ok && runsRes.data ? runsRes.data : [];
      renderCapsTab();
      renderIngestTab();
      renderRetentionTab();
    });
  }

  function wireDrawerEvents() {
    $("drawer-close").addEventListener("click", closeDrawer);
    $("drawer-backdrop").addEventListener("click", closeDrawer);
    document.addEventListener("keydown", function (ev) {
      if (ev.key === "Escape" && !$("drawer").hidden) { closeDrawer(); }
    });
    document.querySelectorAll(".tab-btn").forEach(function (btn) {
      btn.addEventListener("click", function () {
        setDrawerTab(btn.getAttribute("data-tab"));
      });
    });
  }

  wireDrawerEvents();

  C.mountShell({ activeNav: "connections" }).then(function (ctx) {
    if (!ctx) { return; }
    appState.me = ctx.me;
    var dba = C.isDba(ctx.me);
    $("btn-wizard").hidden = !dba;
    $("tab-btn-retention").hidden = !dba;
    if (!dba) {
      $("page-eyebrow").textContent = "Mis instancias asignadas";
      $("page-sub").textContent = "Conexiones SQL Server autorizadas para tu usuario. Inspecciona su estado o salta directo al explorador de consultas.";
    }
    return loadConnections();
  }).catch(function () {
    $("main").hidden = false;
    showNotice($("notice"), "El servicio no está disponible. Inténtalo de nuevo en unos minutos.", false);
  });
})();
