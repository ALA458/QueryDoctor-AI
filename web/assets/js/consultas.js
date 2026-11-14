/* Query Doctor AI — listado de consultas por consumo con sus hallazgos (S1-16).
 * Capa de presentación: la API decide qué se ve; aquí solo se pinta, siempre con textContent. */
(function () {
  "use strict";

  var SEVERITY = {
    High: ["Alta", "danger"], Alta: ["Alta", "danger"],
    Medium: ["Media", "warn"], Media: ["Media", "warn"],
    Low: ["Baja", "neutral"], Baja: ["Baja", "neutral"]
  };

  var $ = function (id) { return document.getElementById(id); };

  function el(tag, className, text) {
    var node = document.createElement(tag);
    if (className) { node.className = className; }
    if (text !== undefined && text !== null) { node.textContent = text; }
    return node;
  }

  function goToLogin() {
    window.location.replace("/login?returnUrl=" + encodeURIComponent(window.location.pathname));
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
    var list = $("list");
    list.replaceChildren();
    var box = el("div", "empty");
    box.appendChild(el("h2", null, title));
    box.appendChild(el("p", null, text));
    list.appendChild(box);
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

  // ---------- Filtro de conexión ----------
  function loadConnectionOptions() {
    return fetch("/api/connections", { credentials: "same-origin", headers: { Accept: "application/json" } })
      .then(function (response) { return response.ok ? response.json() : []; })
      .then(function (items) {
        var select = $("f-connection");
        (items || []).forEach(function (item) {
          var option = el("option", null, item.server + " / " + item.databaseName);
          option.value = item.id;
          select.appendChild(option);
        });
      })
      .catch(function () { /* sin conexiones el filtro queda en "Todas" */ });
  }

  // ---------- Tabla ----------
  function severityBadge(value) {
    var entry = SEVERITY[value];
    var node = el("span", "badge", entry ? entry[0] : "Sin dato");
    node.setAttribute("data-tone", entry ? entry[1] : "neutral");
    return node;
  }

  function formatPct(value) {
    if (typeof value !== "number" || isNaN(value)) { return "—"; }
    return value.toLocaleString("es-CO", { minimumFractionDigits: 1, maximumFractionDigits: 1 }) + " %";
  }

  function formatInt(value) {
    return typeof value === "number" ? value.toLocaleString("es-CO") : "—";
  }

  function cell(row, build) {
    var td = el("td");
    build(td);
    row.appendChild(td);
  }

  function renderTable(items) {
    var list = $("list");
    list.replaceChildren();

    var wrap = el("div", "table-wrap");
    var table = el("table", "table");
    var head = el("tr");
    ["Consulta", "Consumo", "Ejecuciones", "Hallazgos"].forEach(function (name) {
      var th = el("th", null, name);
      th.setAttribute("scope", "col");
      head.appendChild(th);
    });
    var thead = el("thead");
    thead.appendChild(head);
    table.appendChild(thead);

    var body = el("tbody");
    items.forEach(function (item) {
      var row = el("tr");

      cell(row, function (td) {
        // El texto SQL viene de la base del cliente: no es confiable. Solo textContent.
        td.appendChild(el("code", "q-sql", item.sqlPreview || ""));
        td.appendChild(el("span", "secondary", item.databaseName || ""));
      });

      cell(row, function (td) {
        var pct = typeof item.consumptionPct === "number" ? Math.max(0, Math.min(100, item.consumptionPct)) : 0;
        var meter = el("div", "meter");
        var track = el("span", "meter-track");
        var bar = el("span", "meter-bar");
        bar.style.width = pct + "%"; // CSSOM: permitido por la CSP (a diferencia del atributo style)
        track.appendChild(bar);
        meter.appendChild(track);
        meter.appendChild(el("span", "meter-value", formatPct(item.consumptionPct)));
        td.appendChild(meter);
      });

      cell(row, function (td) { td.textContent = formatInt(item.executions); });

      cell(row, function (td) {
        var findings = item.findings || [];
        if (!findings.length) {
          td.appendChild(el("span", "no-findings", "Sin hallazgos"));
          return;
        }
        var box = el("div", "findings");
        findings.forEach(function (finding) {
          var line = el("div", "finding");
          var top = el("div");
          top.appendChild(el("strong", null, finding.ruleId + " "));
          top.appendChild(severityBadge(finding.severity));
          line.appendChild(top);
          if (finding.summary) { line.appendChild(el("span", "secondary", finding.summary)); }
          box.appendChild(line);
        });
        td.appendChild(box);
      });

      body.appendChild(row);
    });
    table.appendChild(body);
    wrap.appendChild(table);
    list.appendChild(wrap);
  }

  function loadQueries() {
    hideNotice();
    var params = new URLSearchParams();
    params.set("window", $("f-window").value);
    params.set("sort", "consumption");
    if ($("f-connection").value) { params.set("connectionId", $("f-connection").value); }
    if ($("f-severity").value) { params.set("severity", $("f-severity").value); }

    return fetch("/api/queries?" + params.toString(), { credentials: "same-origin", headers: { Accept: "application/json" } })
      .then(function (response) {
        if (response.status === 401) { goToLogin(); return null; }
        if (response.status === 403) { renderEmpty("Sin acceso", "Tu cuenta no tiene permiso para ver consultas."); return null; }
        if (response.status === 404 || response.status === 405) {
          renderEmpty("Listado de consultas pendiente",
            "La API todavía no expone /queries. Necesita la ingesta (S1-11) y las reglas (S1-14). Esta pantalla ya está lista y mostrará las consultas en cuanto exista.");
          return null;
        }
        if (!response.ok) { throw new Error("queries " + response.status); }
        return response.json();
      })
      .then(function (items) {
        if (items === null || items === undefined) { return; }
        if (!items.length) {
          renderEmpty("No hay consultas para estos filtros", "Prueba con otra ventana de tiempo o quita el filtro de severidad.");
          return;
        }
        renderTable(items);
      })
      .catch(function () {
        renderEmpty("El servicio no está disponible", "No pudimos cargar las consultas. Inténtalo de nuevo en unos minutos.");
      });
  }

  ["f-connection", "f-window", "f-severity"].forEach(function (id) {
    $(id).addEventListener("change", loadQueries);
  });
  $("filters").addEventListener("submit", function (event) { event.preventDefault(); });

  loadSession()
    .then(function (me) { if (me) { return loadConnectionOptions().then(loadQueries); } })
    .catch(function () {
      $("main").hidden = false;
      renderEmpty("El servicio no está disponible", "No pudimos comprobar tu sesión. Inténtalo de nuevo en unos minutos.");
    });
})();
