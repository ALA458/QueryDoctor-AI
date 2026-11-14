/* Query Doctor AI — shell de la aplicación y pantalla de Conexiones (S1-15).
 * Capa de presentación: no decide permisos ni guarda secretos. Todo lo que muestra viene de la API
 * y se pinta con textContent (nunca innerHTML), igual que la regla e() de FRONT_PHP.md §7. */
(function () {
  "use strict";

  var ENUM_NAMES = {
    platform: ["SqlServer", "AzureSqlDatabase"],
    supportLevel: ["Tested", "CompatibleUntested", "NotSupported"],
    status: ["Active", "Disabled"]
  };

  var PLATFORM = { SqlServer: "SQL Server", AzureSqlDatabase: "Azure SQL Database" };
  var SUPPORT = {
    Tested: ["Probado", "ok"],
    CompatibleUntested: ["Compatible, sin probar", "warn"],
    NotSupported: ["No soportado", "danger"]
  };
  var STATUS = { Active: ["Activa", "ok"], Disabled: ["Deshabilitada", "neutral"] };
  var QS = { READ_WRITE: ["Lectura y escritura", "ok"], READ_ONLY: ["Solo lectura", "warn"], OFF: ["Apagado", "danger"] };

  var $ = function (id) { return document.getElementById(id); };

  function el(tag, className, text) {
    var node = document.createElement(tag);
    if (className) { node.className = className; }
    if (text !== undefined && text !== null) { node.textContent = text; }
    return node;
  }

  function enumName(kind, value) {
    if (typeof value === "number") { return ENUM_NAMES[kind][value]; }
    return value;
  }

  function badge(map, key) {
    var entry = map[key];
    var node = el("span", "badge", entry ? entry[0] : "Sin dato");
    node.setAttribute("data-tone", entry ? entry[1] : "neutral");
    return node;
  }

  function showNotice(node, message, ok) {
    node.textContent = message;
    node.className = "notice is-visible" + (ok ? " is-ok" : "");
  }

  function hideNotice(node) {
    node.textContent = "";
    node.className = "notice";
  }

  function goToLogin() {
    window.location.replace("/login?returnUrl=" + encodeURIComponent(window.location.pathname));
  }

  function formatDate(iso) {
    if (!iso) { return "Aún no hay ingesta"; }
    var date = new Date(iso);
    if (isNaN(date.getTime())) { return "Aún no hay ingesta"; }
    return date.toLocaleString("es-CO", { dateStyle: "medium", timeStyle: "short" });
  }

  // ---------- Sesión ----------
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

  // ---------- Lista de conexiones ----------
  function renderEmpty(title, text) {
    var list = $("list");
    list.replaceChildren();
    var box = el("div", "empty");
    box.appendChild(el("h2", null, title));
    box.appendChild(el("p", null, text));
    list.appendChild(box);
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
    ["Conexión", "Plataforma", "Soporte", "Query Store", "Última ingesta", "Estado"].forEach(function (name) {
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
        td.appendChild(el("span", "primary", item.server));
        td.appendChild(el("span", "secondary", item.databaseName));
        if (item.hasExcessivePermissions) {
          td.appendChild(el("span", "alert-inline", "La cuenta tiene permisos de escritura. Usa una cuenta de solo lectura."));
        }
      });
      cell(row, function (td) { td.textContent = PLATFORM[enumName("platform", item.platform)] || "—"; });
      cell(row, function (td) {
        td.appendChild(badge(SUPPORT, enumName("supportLevel", item.supportLevel)));
        if (item.engineVersion) { td.appendChild(el("span", "secondary", item.engineVersion)); }
      });
      cell(row, function (td) { td.appendChild(badge(QS, item.queryStoreState)); });
      cell(row, function (td) { td.textContent = formatDate(item.lastIngestionAt); });
      cell(row, function (td) { td.appendChild(badge(STATUS, enumName("status", item.status))); });
      body.appendChild(row);
    });
    table.appendChild(body);
    wrap.appendChild(table);
    list.appendChild(wrap);
  }

  function loadConnections() {
    hideNotice($("notice"));
    return fetch("/api/connections", { credentials: "same-origin", headers: { Accept: "application/json" } })
      .then(function (response) {
        if (response.status === 401) { goToLogin(); return null; }
        if (response.status === 403) {
          renderEmpty("Sin acceso", "Tu cuenta no tiene permiso para ver conexiones.");
          return null;
        }
        if (response.status === 404 || response.status === 405) {
          renderEmpty("Registro de conexiones pendiente",
            "La API todavía no expone /connections (tarea S1-09). Esta pantalla ya está lista y mostrará las conexiones en cuanto exista.");
          $("new-connection").disabled = true;
          return null;
        }
        if (!response.ok) { throw new Error("connections " + response.status); }
        return response.json();
      })
      .then(function (items) {
        if (items === null || items === undefined) { return; }
        if (!items.length) {
          renderEmpty("Aún no hay conexiones", "Registra la primera con «Nueva conexión»: la API sondea Query Store y muestra aquí su estado.");
          return;
        }
        renderTable(items);
      })
      .catch(function () {
        renderEmpty("El servicio no está disponible", "No pudimos cargar las conexiones. Inténtalo de nuevo en unos minutos.");
      });
  }

  // ---------- Alta ----------
  function collectErrors(problem) {
    var lines = [];
    if (problem && problem.errors) {
      Object.keys(problem.errors).forEach(function (key) {
        [].concat(problem.errors[key]).forEach(function (message) { lines.push(String(message)); });
      });
    }
    if (!lines.length && problem && problem.detail) { lines.push(String(problem.detail)); }
    return lines;
  }

  function wireDialog() {
    var dialog = $("dialog");
    var form = $("connection-form");
    var notice = $("form-notice");

    $("new-connection").addEventListener("click", function () {
      hideNotice(notice);
      form.reset();
      dialog.showModal();
      $("server").focus();
    });
    $("cancel").addEventListener("click", function () { dialog.close(); });

    form.addEventListener("submit", function (event) {
      event.preventDefault();
      hideNotice(notice);
      var save = $("save");
      save.disabled = true;

      var body = {
        platform: $("platform").value,
        server: $("server").value.trim(),
        databaseName: $("databaseName").value.trim(),
        authenticationMethod: "SqlAuthentication",
        encryption: $("encryption").value,
        username: $("username").value.trim(),
        password: $("password").value
      };

      fetch("/api/connections", {
        method: "POST",
        credentials: "same-origin",
        headers: { "Content-Type": "application/json", Accept: "application/json" },
        body: JSON.stringify(body)
      }).then(function (response) {
        if (response.status === 401) { goToLogin(); return null; }
        if (response.ok) { return { ok: true }; }
        return response.json().catch(function () { return null; }).then(function (problem) {
          return { ok: false, status: response.status, problem: problem };
        });
      }).then(function (result) {
        if (!result) { return; }
        if (result.ok) {
          dialog.close();
          showNotice($("notice"), "Conexión registrada. La API está sondeando sus capacidades.", true);
          return loadConnections().then(function () {
            showNotice($("notice"), "Conexión registrada.", true);
          });
        }
        if (result.status === 400 || result.status === 422) {
          var lines = collectErrors(result.problem);
          showNotice(notice, lines.length ? lines.join(" ") : "Revisa los datos de la conexión.", false);
        } else if (result.status === 403) {
          showNotice(notice, "Tu cuenta no tiene permiso para registrar conexiones.", false);
        } else {
          showNotice(notice, "El servicio no está disponible. Inténtalo de nuevo.", false);
        }
      }).catch(function () {
        showNotice(notice, "El servicio no está disponible. Inténtalo de nuevo.", false);
      }).then(function () {
        // La contraseña no se conserva en la página bajo ningún resultado.
        $("password").value = "";
        save.disabled = false;
      });
    });
  }

  wireDialog();
  loadSession().then(function (me) { if (me) { return loadConnections(); } }).catch(function () {
    $("main").hidden = false;
    renderEmpty("El servicio no está disponible", "No pudimos comprobar tu sesión. Inténtalo de nuevo en unos minutos.");
  });
})();
