/* Query Doctor AI — Centro de control (/): KPIs, alta directa en modal paso a paso y drawer de conexión. */
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
    runs: [],
    wizardController: null
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

  function openWizardModal() {
    var dlg = $("wizard-dialog");
    if (!dlg || !C.isDba(appState.me)) { return; }
    if (!appState.wizardController && window.QDWizardUI) {
      appState.wizardController = window.QDWizardUI.mount($("wizard-modal-host"), {
        onChanged: function () { loadConnections(); },
        onClose: function () { closeWizardModal(); }
      });
    } else if (appState.wizardController) {
      appState.wizardController.reset();
    }
    if (!dlg.open && typeof dlg.showModal === "function") {
      dlg.showModal();
    }
  }

  function closeWizardModal() {
    var dlg = $("wizard-dialog");
    if (appState.wizardController) { appState.wizardController.destroy(); }
    if (dlg && dlg.open) { dlg.close(); }
    loadConnections();
  }

  function renderEmptyState() {
    var list = $("list");
    C.clear(list);
    var card = C.el("section", "surface-card empty");
    if (C.isDba(appState.me)) {
      card.appendChild(C.el("h2", null, "Aún no hay conexiones registradas"));
      card.appendChild(C.el("p", null, "Registra tu primera instancia SQL Server o Azure SQL con una cuenta de solo lectura para comenzar la ingesta y el diagnóstico."));
      var cta = C.el("button", "btn btn-auto", "+ Nueva conexión");
      cta.type = "button";
      cta.addEventListener("click", openWizardModal);
      card.appendChild(cta);
    } else {
      card.appendChild(C.el("h2", null, "Aún no tienes conexiones asignadas"));
      card.appendChild(C.el("p", null, "Solicita al administrador (DBA) que te asigne una instancia monitoreada para consultar su telemetría y diagnósticos."));
    }
    list.appendChild(card);
  }

  function renderTable(items) {
    var list = $("list");
    C.clear(list);
    if (!items.length) {
      renderEmptyState();
      return;
    }

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

  function finishClosingDrawer() {
    stopIngestPoll();
    appState.selectedId = null;
    renderTable(appState.connections);
  }

  function closeDrawer() {
    var drawer = $("drawer");
    if (drawer.open) {
      drawer.close();
    } else {
      finishClosingDrawer();
    }
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
    info.appendChild(C.el("p", "panel-note", "La purga automática elimina textos SQL y planes XML cifrados al cumplirse el plazo de retención (30 días). Desde aquí puedes purgar de inmediato los datos de esta conexión."));
    host.appendChild(info);

    var zone = C.el("div", "danger-zone");
    zone.appendChild(C.el("h3", null, "Purgar evidencia SQL y planes XML"));
    zone.appendChild(C.el("p", null, "Limpia EncryptedSqlText y EncryptedPlanXml de la base '" + d.databaseName + "'. La cadena audit.* permanece intacta."));

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
          C.showToast("Evidencia SQL/XML purgada.", "ok");
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
    var drawer = $("drawer");
    if (!drawer.open) { drawer.showModal(); }
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

  function wireEvents() {
    $("btn-wizard").addEventListener("click", openWizardModal);
    $("wizard-dialog-close").addEventListener("click", closeWizardModal);
    $("wizard-dialog").addEventListener("cancel", function (ev) {
      ev.preventDefault();
      closeWizardModal();
    });

    $("drawer-close").addEventListener("click", closeDrawer);
    $("drawer").addEventListener("close", finishClosingDrawer);
    $("drawer").addEventListener("click", function (ev) {
      if (ev.target === $("drawer")) { closeDrawer(); }
    });
    document.querySelectorAll("#drawer .tab-btn").forEach(function (btn) {
      btn.addEventListener("click", function () {
        setDrawerTab(btn.getAttribute("data-tab"));
      });
    });

    // Evento emitido por qd-core cuando se pulsa "Nueva conexión" estando ya en /
    document.addEventListener("qd:open-wizard", openWizardModal);
  }

  wireEvents();

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
    return loadConnections().then(function () {
      var params = new URLSearchParams(window.location.search);
      if (dba && params.get("new") === "1") {
        openWizardModal();
      }
    });
  }).catch(function () {
    $("main").hidden = false;
    showNotice($("notice"), "El servicio no está disponible. Inténtalo de nuevo en unos minutos.", false);
  });
})();
