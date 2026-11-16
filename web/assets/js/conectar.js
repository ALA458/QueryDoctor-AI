/* Query Doctor AI — controlador del Asistente de Conexión (/conectar). */
(function () {
  "use strict";

  var C = window.QDCore;
  var W = window.QDWizardState;
  var $ = C.$;

  var state = {
    step: 1,
    platform: "SqlServer",
    encryption: "Mandatory",
    connectionId: null,
    pollTimer: null,
    ingestTimer: null,
    pollAttempts: 0,
    ingestAttempts: 0
  };

  function clearTimers() {
    if (state.pollTimer) { clearTimeout(state.pollTimer); state.pollTimer = null; }
    if (state.ingestTimer) { clearTimeout(state.ingestTimer); state.ingestTimer = null; }
  }

  function showNotice(msg, ok) {
    var box = $("wizard-notice");
    box.textContent = msg || "";
    box.className = msg ? ("notice is-visible" + (ok ? " is-ok" : "")) : "notice";
  }

  function goToStep(stepNumber) {
    state.step = stepNumber;
    showNotice("");
    for (var s = 1; s <= 4; s += 1) {
      var panel = $("step-" + s);
      if (panel) { panel.hidden = s !== stepNumber; }
    }
    var items = document.querySelectorAll("#wizard-stepper .wizard-step");
    items.forEach(function (li) {
      var num = Number(li.getAttribute("data-step"));
      li.classList.remove("is-active", "is-done", "is-error");
      if (num < stepNumber) { li.classList.add("is-done"); }
      if (num === stepNumber) { li.classList.add("is-active"); }
    });
  }

  function markStepState(stepNumber, cls) {
    var item = document.querySelector("#wizard-stepper .wizard-step[data-step='" + stepNumber + "']");
    if (item) {
      item.classList.remove("is-active", "is-done", "is-error");
      item.classList.add(cls);
    }
  }

  function refreshSqlPreview() {
    state.platform = $("w-platform").value;
    state.encryption = $("w-encryption").value;
    $("w-sql-preview").textContent = W.buildLeastPrivilegeScript({
      platform: state.platform,
      dbName: $("w-preview-db").value,
      loginName: $("w-preview-login").value
    });
    $("w-sum-platform").textContent = C.PLATFORM_LABELS[state.platform] || state.platform;
    $("w-sum-tls").textContent = state.encryption;
  }

  function wireStep1() {
    ["w-platform", "w-encryption", "w-preview-db", "w-preview-login"].forEach(function (id) {
      $(id).addEventListener("input", refreshSqlPreview);
      $(id).addEventListener("change", refreshSqlPreview);
    });

    $("w-copy-sql").addEventListener("click", function () {
      C.copyToClipboard($("w-sql-preview").textContent).then(function () {
        C.showToast("Script T-SQL copiado al portapapeles.", "ok");
      }).catch(function () {
        C.showToast("No pudimos copiar al portapapeles.", "warn");
      });
    });

    $("w-to-step-2").addEventListener("click", function () {
      var previewDb = $("w-preview-db").value.trim();
      var previewUser = $("w-preview-login").value.trim();
      if (previewDb && !$("w-db").value.trim()) { $("w-db").value = previewDb; }
      if (previewUser && !$("w-user").value.trim()) { $("w-user").value = previewUser; }
      refreshSqlPreview();
      goToStep(2);
      $("w-server").focus();
    });

    refreshSqlPreview();
  }

  function wireStep2() {
    $("w-back-step-1").addEventListener("click", function () { goToStep(1); });

    $("w-toggle-pass").addEventListener("click", function () {
      var input = $("w-pass");
      var btn = $("w-toggle-pass");
      var show = input.type === "password";
      input.type = show ? "text" : "password";
      btn.textContent = show ? "Ocultar" : "Mostrar";
      btn.setAttribute("aria-pressed", show ? "true" : "false");
    });

    $("w-form").addEventListener("submit", function (event) {
      event.preventDefault();
      showNotice("");

      var payload = {
        platform: state.platform,
        server: $("w-server").value.trim(),
        databaseName: $("w-db").value.trim(),
        authenticationMethod: "SqlAuthentication",
        encryption: state.encryption,
        username: $("w-user").value.trim(),
        password: $("w-pass").value
      };

      var check = W.validateConnectionInput(payload);
      if (!check.valid) {
        var firstMsg = Object.keys(check.errors).map(function (k) { return check.errors[k]; }).join(" ");
        showNotice(firstMsg, false);
        return;
      }

      var submitBtn = $("w-submit");
      submitBtn.disabled = true;

      C.apiRequest("/api/connections", { method: "POST", json: payload }).then(function (res) {
        if (res.redirected) { return; }
        if (res.ok && res.data && res.data.id) {
          state.connectionId = res.data.id;
          state.pollAttempts = 0;
          goToStep(3);
          pollProbe();
          return;
        }
        if (res.status === 409 && res.problem && res.problem.existingConnectionId) {
          state.connectionId = res.problem.existingConnectionId;
          showNotice("Esta base ya estaba registrada en ese servidor. Cargando su estado actual...", true);
          state.pollAttempts = 0;
          goToStep(3);
          pollProbe();
          return;
        }
        var lines = C.collectProblemMessages(res.problem);
        showNotice(lines.length ? lines.join(" ") : "No se pudo registrar la conexión.", false);
      }).catch(function () {
        showNotice("El servicio no está disponible en este momento.", false);
      }).then(function () {
        $("w-pass").value = "";
        submitBtn.disabled = false;
      });
    });
  }

  function addFact(container, label, valueNodeOrText) {
    var wrap = C.el("div", "fact-item");
    wrap.appendChild(C.el("dt", null, label));
    var dd = C.el("dd");
    if (typeof valueNodeOrText === "string") {
      dd.textContent = valueNodeOrText;
    } else {
      dd.appendChild(valueNodeOrText);
    }
    wrap.appendChild(dd);
    container.appendChild(wrap);
  }

  function renderProbeDetails(detail) {
    var verdict = W.interpretProbeResult(detail);
    $("w-probe-title").textContent = verdict.title;
    $("w-probe-summary").textContent = verdict.summary;
    $("w-probe-pill").replaceChildren(C.badgeFromMap(C.CONNECTION_STATUS_META, detail.status, detail.status));

    var facts = $("w-probe-facts");
    C.clear(facts);
    addFact(facts, "Instancia / Base", detail.server + " / " + detail.databaseName);
    addFact(facts, "Último sondeo", C.formatDateTime(detail.lastProbedAt, "Pendiente"));

    var caps = detail.capabilities;
    if (caps) {
      addFact(facts, "Motor SQL", (caps.engineVersion || "—") + (caps.edition ? " (" + caps.edition + ")" : ""));
      addFact(facts, "Nivel de soporte", C.badgeFromMap(C.SUPPORT_META, caps.supportLevel));
      var qs = caps.queryStore || {};
      addFact(facts, "Query Store", C.badgeFromMap(C.QUERY_STORE_META, qs.actualState, "Sin lectura"));
      var intervalText = qs.intervalLengthMinutes ? ("Cada " + qs.intervalLengthMinutes + " min (ingesta automática)") : "Según perfil";
      addFact(facts, "Recolección", intervalText);
    }

    var warnHost = $("w-probe-warnings");
    C.clear(warnHost);
    if (verdict.remediation) {
      var errBox = C.el("div", "notice is-visible", "Sugerencia: " + verdict.remediation);
      warnHost.appendChild(errBox);
    }
    (verdict.warnings || []).forEach(function (w) {
      warnHost.appendChild(C.el("div", "notice is-visible", w));
    });

    $("w-probe-edit").hidden = verdict.phase !== "failed";
    $("w-to-step-4").hidden = !verdict.canProceed;
    if (verdict.phase === "failed") {
      markStepState(3, "is-error");
    } else if (verdict.canProceed) {
      markStepState(3, "is-done");
    }

    return verdict;
  }

  function pollProbe() {
    if (!state.connectionId) { return; }
    if (state.pollTimer) { clearTimeout(state.pollTimer); state.pollTimer = null; }

    C.apiRequest("/api/connections/" + encodeURIComponent(state.connectionId)).then(function (res) {
      if (res.redirected || !res.ok || !res.data) {
        showNotice("No pudimos consultar el estado de la conexión.", false);
        return;
      }
      var verdict = renderProbeDetails(res.data);
      if (W.shouldContinuePolling(res.data.status) && state.pollAttempts < 25) {
        state.pollAttempts += 1;
        state.pollTimer = setTimeout(pollProbe, 1800);
      } else if (verdict.phase === "pending") {
        $("w-probe-summary").textContent = "El sondeo sigue en cola en el Worker. Puedes actualizar manualmente en unos segundos.";
      }
    }).catch(function () {
      showNotice("Error de red al consultar el sondeo.", false);
    });
  }

  function wireStep3() {
    $("w-probe-retry-poll").addEventListener("click", function () {
      state.pollAttempts = 0;
      pollProbe();
    });
    $("w-probe-edit").addEventListener("click", function () {
      clearTimers();
      goToStep(2);
      $("w-pass").focus();
    });
    $("w-to-step-4").addEventListener("click", function () {
      clearTimers();
      $("w-skip-queries").href = "/consultas?connectionId=" + encodeURIComponent(state.connectionId);
      $("w-go-queries").href = "/consultas?connectionId=" + encodeURIComponent(state.connectionId);
      $("w-go-access").href = "/accesos?connectionId=" + encodeURIComponent(state.connectionId);
      goToStep(4);
    });
  }

  function renderIngestionTimeline(runs) {
    var host = $("w-ingest-status");
    C.clear(host);
    if (!runs || !runs.length) {
      host.appendChild(C.el("p", "panel-note", "Aún no hay ejecuciones registradas."));
      return false;
    }

    var list = C.el("div", "timeline");
    var latest = runs[0];
    runs.slice(0, 5).forEach(function (run) {
      var card = C.el("div", "timeline-item");
      var top = C.el("div", "timeline-top");
      top.appendChild(C.el("strong", null, (C.INGESTION_TRIGGER_LABELS[run.trigger] || run.trigger) + " · " + C.formatDateTime(run.startedAt)));
      top.appendChild(C.badgeFromMap(C.INGESTION_STATUS_META, run.status, run.status));
      card.appendChild(top);

      var rows = typeof run.rowsUpserted === "number" ? (run.rowsUpserted + " filas procesadas") : "Procesando telemetría...";
      card.appendChild(C.el("div", "timeline-meta", rows + (run.finishedAt ? (" · Finalizó: " + C.formatDateTime(run.finishedAt)) : "")));
      if (run.causeMessage) {
        card.appendChild(C.el("p", "panel-note", run.causeMessage));
      }
      list.appendChild(card);
    });
    host.appendChild(list);

    var done = latest.status === "Succeeded";
    $("w-finish-actions").hidden = !done;
    if (done) { markStepState(4, "is-done"); }
    return W.shouldContinuePolling(latest.status);
  }

  function pollIngestionRuns() {
    if (!state.connectionId) { return; }
    if (state.ingestTimer) { clearTimeout(state.ingestTimer); state.ingestTimer = null; }

    C.apiRequest("/api/connections/" + encodeURIComponent(state.connectionId) + "/ingestions").then(function (res) {
      if (res.redirected || !res.ok) { return; }
      var running = renderIngestionTimeline(res.data || []);
      if (running && state.ingestAttempts < 30) {
        state.ingestAttempts += 1;
        state.ingestTimer = setTimeout(pollIngestionRuns, 1800);
      } else if (!running) {
        $("w-run-ingest").disabled = false;
      }
    });
  }

  function wireStep4() {
    $("w-run-ingest").addEventListener("click", function () {
      if (!state.connectionId) { return; }
      var btn = $("w-run-ingest");
      btn.disabled = true;
      showNotice("");

      C.apiRequest("/api/connections/" + encodeURIComponent(state.connectionId) + "/ingestions", { method: "POST" }).then(function (res) {
        if (res.redirected) { return; }
        if (res.ok || res.status === 409) {
          if (res.status === 409) {
            C.showToast("Ya hay una ingesta en curso; actualizando estado.", "info");
          } else {
            C.showToast("Ingesta encolada. Siguiendo progreso...", "ok");
          }
          state.ingestAttempts = 0;
          pollIngestionRuns();
          return;
        }
        var lines = C.collectProblemMessages(res.problem);
        showNotice(lines.length ? lines.join(" ") : "No pudimos iniciar la ingesta.", false);
        btn.disabled = false;
      }).catch(function () {
        showNotice("El servicio no está disponible.", false);
        btn.disabled = false;
      });
    });
  }

  C.mountShell({ activeNav: "connect", requireDba: true }).then(function (ctx) {
    if (!ctx || ctx.denied) { return; }
    wireStep1();
    wireStep2();
    wireStep3();
    wireStep4();
  }).catch(function () {
    var main = $("main");
    if (main) { main.hidden = false; }
    showNotice("No pudimos comprobar tu sesión. Inténtalo de nuevo en unos minutos.", false);
  });
})();
