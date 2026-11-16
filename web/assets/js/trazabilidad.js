/* Query Doctor AI — Trazabilidad IA y Auditoría inmutable (/trazabilidad, Solo DBA). */
(function () {
  "use strict";

  var C = window.QDCore;
  var $ = C.$;

  function showNotice(msg, ok) {
    var box = $("trace-notice");
    box.textContent = msg || "";
    box.className = msg ? ("notice is-visible" + (ok ? " is-ok" : "")) : "notice";
  }

  function renderVerifyCard(title, res) {
    var card = C.el("article", "verify-card");
    if (!res) {
      card.appendChild(C.el("p", "eyebrow", title));
      card.appendChild(C.el("strong", null, "No disponible"));
      return card;
    }
    card.setAttribute("data-intact", res.intact ? "true" : "false");
    card.appendChild(C.el("p", "eyebrow", title));
    var top = C.el("div", "timeline-top");
    top.appendChild(C.badge(res.intact ? "Cadena íntegra" : "Ruptura detectada", res.intact ? "ok" : "danger"));
    top.appendChild(C.el("span", "mono tabular", res.entries_checked + " entradas verificadas"));
    card.appendChild(top);
    if (!res.intact && res.reason) {
      card.appendChild(C.el("p", "panel-note", "Seq #" + res.first_broken_seq + ": " + res.reason));
    }
    return card;
  }

  function verifyChains(notifyToast) {
    return Promise.all([
      C.apiRequest("/api/trace/verify"),
      C.apiRequest("/api/audit/verify")
    ]).then(function (results) {
      var banner = $("verify-banner");
      C.clear(banner);
      var t = results[0].ok ? results[0].data : null;
      var a = results[1].ok ? results[1].data : null;
      banner.appendChild(renderVerifyCard("Cadena audit.trace_entry (IA)", t));
      banner.appendChild(renderVerifyCard("Cadena audit.audit_event (Seguridad)", a));
      if (notifyToast) {
        var allIntact = Boolean(t && t.intact && a && a.intact);
        C.showToast(allIntact ? "Cadenas SHA-256 verificadas e íntegras." : "Se detectaron inconsistencias en la verificación.", allIntact ? "ok" : "danger");
      }
    });
  }

  function renderTraceList(entries) {
    var host = $("trace-list");
    C.clear(host);
    if (!entries.length) {
      host.appendChild(C.el("p", "panel-note", "No hay entradas de trazabilidad para este filtro."));
      return;
    }

    var wrap = C.el("div", "table-wrap");
    var table = C.el("table", "table");
    var thead = C.el("thead");
    var hRow = C.el("tr");
    ["Seq", "Fecha", "Estado / Validación", "Perfil y modelo", "Política / Origen", "Hash entrada"].forEach(function (col) {
      var th = C.el("th", null, col);
      th.setAttribute("scope", "col");
      hRow.appendChild(th);
    });
    thead.appendChild(hRow);
    table.appendChild(thead);

    var tbody = C.el("tbody");
    entries.forEach(function (e) {
      var tr = C.el("tr");
      tr.appendChild(C.el("td", "mono tabular", "#" + e.seq));
      tr.appendChild(C.el("td", "tabular", C.formatDateTime(e.recorded_at)));

      var tdState = C.el("td");
      var tone = e.state === "TRAZADA" ? "ok" : (e.state === "NO_VERIFICADA" ? "warn" : "neutral");
      tdState.appendChild(C.badge(e.state || "—", tone));
      if (e.validation_status) {
        tdState.appendChild(C.el("span", "secondary", e.validation_status + (e.cause ? " · " + e.cause : "")));
      }
      tr.appendChild(tdState);

      var tdModel = C.el("td");
      tdModel.appendChild(C.el("span", "primary mono", e.llm_profile || "—"));
      tdModel.appendChild(C.el("span", "secondary mono", (e.model || "sin modelo") + (e.prompt_version ? " · prompt " + e.prompt_version : "")));
      tr.appendChild(tdModel);

      var tdPol = C.el("td");
      tdPol.appendChild(C.el("span", "primary", e.served_by || "—"));
      tdPol.appendChild(C.el("span", "secondary", (e.data_policy || "—") + " · reglas: " + ((e.finding_codes || []).join(", ") || "ninguna")));
      tr.appendChild(tdPol);

      var hashShort = e.entry_hash ? (e.entry_hash.slice(0, 14) + "…") : "—";
      var tdHash = C.el("td", "mono", hashShort);
      if (e.entry_hash) { tdHash.setAttribute("title", e.entry_hash); }
      tr.appendChild(tdHash);

      tbody.appendChild(tr);
    });
    table.appendChild(tbody);
    wrap.appendChild(table);
    host.appendChild(wrap);
  }

  function loadTrace() {
    showNotice("");
    var params = new URLSearchParams();
    var qId = $("t-query-id").value.trim();
    if (qId) { params.set("queryId", qId); }
    params.set("take", $("t-take").value || "100");

    return C.apiRequest("/api/trace?" + params.toString()).then(function (res) {
      if (res.redirected) { return; }
      if (!res.ok) {
        var lines = C.collectProblemMessages(res.problem);
        showNotice(lines.length ? lines.join(" ") : "No pudimos consultar /trace.", false);
        return;
      }
      renderTraceList(res.data || []);
    });
  }

  function renderAuditList(events) {
    var host = $("audit-list");
    C.clear(host);
    if (!events.length) {
      host.appendChild(C.el("p", "panel-note", "No hay eventos de auditoría para este filtro."));
      return;
    }

    var wrap = C.el("div", "table-wrap");
    var table = C.el("table", "table");
    var thead = C.el("thead");
    var hRow = C.el("tr");
    ["Seq", "Fecha", "Evento", "Actor", "Objeto", "Resultado"].forEach(function (col) {
      var th = C.el("th", null, col);
      th.setAttribute("scope", "col");
      hRow.appendChild(th);
    });
    thead.appendChild(hRow);
    table.appendChild(thead);

    var tbody = C.el("tbody");
    events.forEach(function (ev) {
      var tr = C.el("tr");
      tr.appendChild(C.el("td", "mono tabular", "#" + ev.seq));
      tr.appendChild(C.el("td", "tabular", C.formatDateTime(ev.recorded_at)));

      var tdEv = C.el("td");
      tdEv.appendChild(C.el("span", "primary", C.AUDIT_EVENT_MAP[ev.event_type] || ev.event_type));
      tdEv.appendChild(C.el("span", "secondary mono", ev.event_type));
      tr.appendChild(tdEv);

      tr.appendChild(C.el("td", "mono", ev.user_name || "sistema"));
      tr.appendChild(C.el("td", "mono", ev.subject_type ? (ev.subject_type + (ev.subject_id ? " · " + String(ev.subject_id).slice(0, 8) : "")) : "—"));

      var tdOut = C.el("td");
      var outTone = ev.outcome === "OK" ? "ok" : (ev.outcome === "LOCKED" || ev.outcome === "REJECTED" ? "danger" : "warn");
      tdOut.appendChild(C.badge(ev.outcome || "—", outTone));
      tr.appendChild(tdOut);

      tbody.appendChild(tr);
    });
    table.appendChild(tbody);
    wrap.appendChild(table);
    host.appendChild(wrap);
  }

  function loadAudit() {
    showNotice("");
    var params = new URLSearchParams();
    var type = $("a-type").value;
    if (type) { params.set("type", type); }
    params.set("take", $("a-take").value || "100");

    return C.apiRequest("/api/audit?" + params.toString()).then(function (res) {
      if (res.redirected) { return; }
      if (!res.ok) {
        var lines = C.collectProblemMessages(res.problem);
        showNotice(lines.length ? lines.join(" ") : "No pudimos consultar /audit.", false);
        return;
      }
      renderAuditList(res.data || []);
    });
  }

  function populateAuditEventOptions() {
    var select = $("a-type");
    C.AUDIT_EVENT_OPTIONS.forEach(function (pair) {
      var opt = C.el("option", null, pair[1] + " (" + pair[0] + ")");
      opt.value = pair[0];
      select.appendChild(opt);
    });
  }

  function wireTabsAndForms() {
    document.querySelectorAll(".tab-btn").forEach(function (btn) {
      btn.addEventListener("click", function () {
        var target = btn.getAttribute("data-tab");
        document.querySelectorAll(".tab-btn").forEach(function (b) {
          b.setAttribute("aria-selected", b === btn ? "true" : "false");
        });
        $("panel-trace").hidden = target !== "trace";
        $("panel-audit").hidden = target !== "audit";
        if (target === "audit") { loadAudit(); }
      });
    });

    $("trace-filters").addEventListener("submit", function (ev) { ev.preventDefault(); loadTrace(); });
    $("audit-filters").addEventListener("submit", function (ev) { ev.preventDefault(); loadAudit(); });
    $("btn-verify-chains").addEventListener("click", function () { verifyChains(true); });
  }

  C.mountShell({ activeNav: "trace", requireDba: true }).then(function (ctx) {
    if (!ctx || ctx.denied) { return; }
    populateAuditEventOptions();
    wireTabsAndForms();
    return Promise.all([verifyChains(false), loadTrace()]);
  }).catch(function () {
    $("main").hidden = false;
    showNotice("No pudimos cargar las bitácoras de trazabilidad y auditoría.", false);
  });
})();
