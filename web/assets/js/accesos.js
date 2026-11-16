/* Query Doctor AI — Accesos y usuarios (/accesos): alta de Desarrolladores y asignaciones (Solo DBA). */
(function () {
  "use strict";

  var C = window.QDCore;
  var $ = C.$;

  var accessState = {
    connections: [],
    developers: [],
    assignments: [],
    selectedConnectionId: ""
  };

  function showNotice(msg, ok) {
    var box = $("access-notice");
    box.textContent = msg || "";
    box.className = msg ? ("notice is-visible" + (ok ? " is-ok" : "")) : "notice";
  }

  function loadInitialData(preselectConnId) {
    return Promise.all([
      C.apiRequest("/api/connections"),
      C.apiRequest("/api/users?role=" + encodeURIComponent(C.ROLE_DEVELOPER))
    ]).then(function (results) {
      var connRes = results[0];
      var devRes = results[1];
      if (connRes.redirected || devRes.redirected) { return; }

      accessState.connections = connRes.ok && Array.isArray(connRes.data) ? connRes.data : [];
      accessState.developers = devRes.ok && Array.isArray(devRes.data) ? devRes.data : [];

      var select = $("a-connection");
      C.clear(select);
      if (!accessState.connections.length) {
        select.appendChild(C.el("option", null, "No hay conexiones registradas"));
        select.disabled = true;
        renderAssignments();
        return;
      }

      select.disabled = false;
      accessState.connections.forEach(function (c) {
        var opt = C.el("option", null, c.server + " / " + c.databaseName + " (" + c.status + ")");
        opt.value = c.id;
        select.appendChild(opt);
      });

      var match = accessState.connections.some(function (c) { return c.id === preselectConnId; });
      accessState.selectedConnectionId = match ? preselectConnId : accessState.connections[0].id;
      select.value = accessState.selectedConnectionId;
      return loadAssignments(accessState.selectedConnectionId);
    });
  }

  function loadAssignments(connectionId) {
    if (!connectionId) {
      accessState.assignments = [];
      renderAssignments();
      return Promise.resolve();
    }
    return C.apiRequest("/api/connections/" + encodeURIComponent(connectionId) + "/assignments").then(function (res) {
      if (res.redirected) { return; }
      accessState.assignments = res.ok && Array.isArray(res.data) ? res.data : [];
      renderAssignments();
    });
  }

  function renderAssignments() {
    var host = $("assignments-host");
    C.clear(host);

    if (!accessState.connections.length) {
      host.appendChild(C.el("p", "panel-note", "Registra primero una conexión desde el asistente para poder asignar desarrolladores."));
      return;
    }

    if (!accessState.developers.length) {
      host.appendChild(C.el("p", "panel-note", "Aún no existen usuarios con rol Desarrollador. Crea el primero usando el formulario de la izquierda."));
      return;
    }

    var assignedById = {};
    accessState.assignments.forEach(function (a) { assignedById[a.userId] = a; });

    var wrap = C.el("div", "table-wrap");
    var table = C.el("table", "table");
    var thead = C.el("thead");
    var headRow = C.el("tr");
    ["Desarrollador", "Estado en esta conexión", "Asignado desde", "Acción"].forEach(function (name) {
      var th = C.el("th", null, name);
      th.setAttribute("scope", "col");
      headRow.appendChild(th);
    });
    thead.appendChild(headRow);
    table.appendChild(thead);

    var tbody = C.el("tbody");
    accessState.developers.forEach(function (dev) {
      var assigned = assignedById[dev.id];
      var tr = C.el("tr");
      tr.appendChild(C.el("td", null, dev.userName));

      var tdState = C.el("td");
      tdState.appendChild(assigned ? C.badge("Asignado", "ok") : C.badge("Sin acceso", "neutral"));
      tr.appendChild(tdState);

      tr.appendChild(C.el("td", "tabular", assigned ? C.formatDateTime(assigned.assignedAt) : "—"));

      var tdAct = C.el("td");
      var btn = C.el("button", assigned ? "btn btn-sm btn-secondary" : "btn btn-sm", assigned ? "Revocar acceso" : "Asignar");
      btn.type = "button";
      btn.addEventListener("click", function () {
        toggleAssignment(accessState.selectedConnectionId, dev.id, Boolean(assigned), btn);
      });
      tdAct.appendChild(btn);
      tr.appendChild(tdAct);
      tbody.appendChild(tr);
    });

    table.appendChild(tbody);
    wrap.appendChild(table);
    host.appendChild(wrap);
  }

  function toggleAssignment(connectionId, userId, isAssigned, btn) {
    if (!connectionId) { return; }
    btn.disabled = true;
    showNotice("");
    var method = isAssigned ? "DELETE" : "PUT";
    var url = "/api/connections/" + encodeURIComponent(connectionId) + "/assignments/" + encodeURIComponent(userId);

    C.apiRequest(url, { method: method }).then(function (res) {
      if (res.redirected) { return; }
      if (res.ok) {
        C.showToast(isAssigned ? "Acceso revocado y auditado." : "Conexión asignada y auditada.", "ok");
        return loadAssignments(connectionId);
      }
      var lines = C.collectProblemMessages(res.problem);
      showNotice(lines.length ? lines.join(" ") : "No pudimos actualizar la asignación.", false);
      btn.disabled = false;
    }).catch(function () {
      showNotice("Error de comunicación con la API.", false);
      btn.disabled = false;
    });
  }

  function wireCreateUserForm() {
    $("u-toggle-pass").addEventListener("click", function () {
      var input = $("u-password");
      var btn = $("u-toggle-pass");
      var show = input.type === "password";
      input.type = show ? "text" : "password";
      btn.textContent = show ? "Ocultar" : "Mostrar";
      btn.setAttribute("aria-pressed", show ? "true" : "false");
    });

    $("create-user-form").addEventListener("submit", function (event) {
      event.preventDefault();
      showNotice("");
      var submit = $("u-submit");
      submit.disabled = true;

      var body = {
        userName: $("u-username").value.trim(),
        email: $("u-email").value.trim(),
        password: $("u-password").value
      };

      C.apiRequest("/api/users", { method: "POST", json: body }).then(function (res) {
        if (res.redirected) { return; }
        if (res.ok && res.data) {
          $("create-user-form").reset();
          C.showToast("Usuario Desarrollador '" + res.data.userName + "' creado.", "ok");
          showNotice("Usuario '" + res.data.userName + "' creado con cambio obligatorio de contraseña.", true);
          return C.apiRequest("/api/users?role=" + encodeURIComponent(C.ROLE_DEVELOPER)).then(function (devRes) {
            if (devRes.ok && Array.isArray(devRes.data)) {
              accessState.developers = devRes.data;
              renderAssignments();
            }
          });
        }
        var lines = C.collectProblemMessages(res.problem);
        showNotice(lines.length ? lines.join(" ") : "Revisa los datos del usuario.", false);
      }).catch(function () {
        showNotice("No pudimos crear el usuario en este momento.", false);
      }).then(function () {
        $("u-password").value = "";
        submit.disabled = false;
      });
    });

    $("a-connection").addEventListener("change", function () {
      accessState.selectedConnectionId = $("a-connection").value;
      loadAssignments(accessState.selectedConnectionId);
    });
  }

  C.mountShell({ activeNav: "access", requireDba: true }).then(function (ctx) {
    if (!ctx || ctx.denied) { return; }
    wireCreateUserForm();
    var preselect = new URLSearchParams(window.location.search).get("connectionId") || "";
    return loadInitialData(preselect);
  }).catch(function () {
    $("main").hidden = false;
    showNotice("No pudimos cargar la vista de accesos.", false);
  });
})();
