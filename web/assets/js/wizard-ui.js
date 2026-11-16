/* Query Doctor AI — Componente reutilizable del Wizard de conexión en 4 pasos (modal en / o página /conectar). */
(function (root, factory) {
  var api = factory();
  if (typeof module === "object" && module && module.exports) {
    module.exports = api;
  }
  if (root) {
    root.QDWizardUI = api;
  }
})(typeof globalThis !== "undefined" ? globalThis : window, function () {
  "use strict";

  function mount(hostEl, options) {
    var C = window.QDCore;
    var W = window.QDWizardState;
    var opts = options || {};
    if (!hostEl || !C || !W) { return null; }

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

    C.clear(hostEl);

    // Stepper
    var stepper = C.el("ol", "wizard-steps");
    stepper.setAttribute("aria-label", "Pasos para conectar una base de datos");
    var stepDefs = [
      ["Paso 1", "Preparación T-SQL"],
      ["Paso 2", "Credenciales"],
      ["Paso 3", "Sondeo en vivo"],
      ["Paso 4", "Primera ingesta"]
    ];
    var stepNodes = stepDefs.map(function (pair, idx) {
      var li = C.el("li", "wizard-step" + (idx === 0 ? " is-active" : ""));
      li.setAttribute("data-step", String(idx + 1));
      li.appendChild(C.el("span", "wizard-step-kicker", pair[0]));
      li.appendChild(C.el("strong", null, pair[1]));
      stepper.appendChild(li);
      return li;
    });
    hostEl.appendChild(stepper);

    var noticeBox = C.el("div", "notice");
    noticeBox.setAttribute("role", "alert");
    noticeBox.setAttribute("aria-live", "polite");
    hostEl.appendChild(noticeBox);

    function showNotice(msg, ok) {
      noticeBox.textContent = msg || "";
      noticeBox.className = msg ? ("notice is-visible" + (ok ? " is-ok" : "")) : "notice";
    }

    function clearTimers() {
      if (state.pollTimer) { clearTimeout(state.pollTimer); state.pollTimer = null; }
      if (state.ingestTimer) { clearTimeout(state.ingestTimer); state.ingestTimer = null; }
    }

    // ---------- Contenedores de cada paso ----------
    var step1 = C.el("section", "wizard-layout");
    var step2 = C.el("section", "wizard-layout");
    step2.hidden = true;
    var step3 = C.el("section", "surface-card");
    step3.hidden = true;
    var step4 = C.el("section", "surface-card");
    step4.hidden = true;

    var stepPanels = [step1, step2, step3, step4];

    function goToStep(stepNumber) {
      state.step = stepNumber;
      showNotice("");
      stepPanels.forEach(function (p, i) { p.hidden = (i + 1) !== stepNumber; });
      stepNodes.forEach(function (li, i) {
        var n = i + 1;
        li.classList.remove("is-active", "is-done", "is-error");
        if (n < stepNumber) { li.classList.add("is-done"); }
        if (n === stepNumber) { li.classList.add("is-active"); }
      });
    }

    function markStep(stepNumber, cls) {
      var li = stepNodes[stepNumber - 1];
      if (!li) { return; }
      li.classList.remove("is-active", "is-done", "is-error");
      li.classList.add(cls);
    }

    function buildSelectField(labelText, optsList) {
      var wrap = C.el("div", "field");
      var lbl = C.el("label", null, labelText);
      var ctrl = C.el("div", "control");
      var sel = C.el("select");
      optsList.forEach(function (o) {
        var opt = C.el("option", null, o[1]);
        opt.value = o[0];
        sel.appendChild(opt);
      });
      ctrl.appendChild(sel);
      wrap.appendChild(lbl);
      wrap.appendChild(ctrl);
      return { wrap: wrap, select: sel };
    }

    function buildInputField(labelText, type, placeholder, value) {
      var wrap = C.el("div", "field");
      var lbl = C.el("label", null, labelText);
      var ctrl = C.el("div", "control");
      var input = C.el("input");
      input.type = type || "text";
      if (placeholder) { input.placeholder = placeholder; }
      if (value !== undefined) { input.value = value; }
      input.spellcheck = false;
      ctrl.appendChild(input);
      wrap.appendChild(lbl);
      wrap.appendChild(ctrl);
      return { wrap: wrap, input: input, ctrl: ctrl };
    }

    // ---------- PASO 1 ----------
    var s1Card = C.el("div", "surface-card");
    s1Card.appendChild(C.el("h3", null, "1. Entorno y cuenta de solo lectura"));
    s1Card.appendChild(C.el("p", "panel-note", "Elige el tipo de base de datos y copia el script de privilegio mínimo para el DBA si aún no creaste el login."));

    var row1 = C.el("div", "row");
    var fPlatform = buildSelectField("Plataforma", [
      ["SqlServer", "SQL Server (2016 – 2022 / Managed Instance)"],
      ["AzureSqlDatabase", "Azure SQL Database"]
    ]);
    var fTls = buildSelectField("Cifrado TLS", [
      ["Mandatory", "Obligatorio (Mandatory)"],
      ["Strict", "Estricto (Strict · TDS 8.0)"]
    ]);
    row1.appendChild(fPlatform.wrap);
    row1.appendChild(fTls.wrap);
    s1Card.appendChild(row1);

    var row1b = C.el("div", "row");
    var fPrevDb = buildInputField("Base objetivo (para vista previa)", "text", "QD_Lab", "QD_Lab");
    var fPrevUser = buildInputField("Login de solo lectura", "text", "qd_reader", "qd_reader");
    row1b.appendChild(fPrevDb.wrap);
    row1b.appendChild(fPrevUser.wrap);
    s1Card.appendChild(row1b);

    var codeCard = C.el("div", "code-card");
    var codeHead = C.el("div", "code-card-head");
    codeHead.appendChild(C.el("span", null, "Script T-SQL de referencia"));
    var copyBtn = C.el("button", "code-copy", "Copiar T-SQL");
    copyBtn.type = "button";
    codeHead.appendChild(copyBtn);
    codeCard.appendChild(codeHead);
    var sqlPre = C.el("pre");
    codeCard.appendChild(sqlPre);
    s1Card.appendChild(codeCard);

    var toStep2Btn = C.el("button", "btn btn-auto", "Continuar a credenciales →");
    toStep2Btn.type = "button";
    s1Card.appendChild(toStep2Btn);
    step1.appendChild(s1Card);

    var s1Aside = C.el("aside", "surface-card");
    s1Aside.appendChild(C.el("p", "eyebrow", "Garantías"));
    s1Aside.appendChild(C.el("h3", null, "Solo lectura verificada"));
    var chk = C.el("ul", "checklist");
    [
      "Únicamente SELECT de lista blanca sobre vistas de Query Store.",
      "Cifrado TLS en tránsito y AES-256-GCM en PostgreSQL.",
      "Recolección periódica automática al aprobar el sondeo."
    ].forEach(function (txt) {
      var li = C.el("li");
      li.appendChild(C.el("span", null, "●"));
      li.appendChild(C.el("span", null, txt));
      chk.appendChild(li);
    });
    s1Aside.appendChild(chk);
    step1.appendChild(s1Aside);

    // ---------- PASO 2 ----------
    var s2Card = C.el("div", "surface-card");
    s2Card.appendChild(C.el("h3", null, "2. Destino y credenciales"));
    s2Card.appendChild(C.el("p", "panel-note", "La contraseña viaja cifrada hacia la API y se borra de la pantalla al enviarse."));

    var form = C.el("form");
    form.autocomplete = "off";
    form.noValidate = true;

    var fServer = buildInputField("Servidor y puerto", "text", "ej. qd-lab-sql,1433");
    var fDb = buildInputField("Base de datos", "text", "ej. QD_Lab");
    var row2 = C.el("div", "row");
    var fUser = buildInputField("Usuario SQL", "text", "qd_reader");
    var fPass = buildInputField("Contraseña", "password", "");
    fPass.input.autocomplete = "new-password";
    var togglePass = C.el("button", "reveal", "Mostrar");
    togglePass.type = "button";
    togglePass.setAttribute("aria-pressed", "false");
    togglePass.addEventListener("click", function () {
      var show = fPass.input.type === "password";
      fPass.input.type = show ? "text" : "password";
      togglePass.textContent = show ? "Ocultar" : "Mostrar";
      togglePass.setAttribute("aria-pressed", show ? "true" : "false");
    });
    fPass.ctrl.appendChild(togglePass);
    row2.appendChild(fUser.wrap);
    row2.appendChild(fPass.wrap);

    form.appendChild(fServer.wrap);
    form.appendChild(fDb.wrap);
    form.appendChild(row2);

    var s2Actions = C.el("div", "btn-row");
    var back1Btn = C.el("button", "btn btn-auto btn-secondary", "← Volver");
    back1Btn.type = "button";
    var submitConnBtn = C.el("button", "btn btn-auto", "Registrar y sondear →");
    submitConnBtn.type = "submit";
    s2Actions.appendChild(back1Btn);
    s2Actions.appendChild(submitConnBtn);
    form.appendChild(s2Actions);
    s2Card.appendChild(form);
    step2.appendChild(s2Card);

    var s2Aside = C.el("aside", "surface-card");
    s2Aside.appendChild(C.el("p", "eyebrow", "Configuración"));
    var sumGrid = C.el("dl", "fact-grid");
    var sumPlatVal = C.el("dd", null, "SQL Server");
    var sumTlsVal = C.el("dd", null, "Mandatory");
    var sf1 = C.el("div", "fact-item");
    sf1.appendChild(C.el("dt", null, "Plataforma"));
    sf1.appendChild(sumPlatVal);
    var sf2 = C.el("div", "fact-item");
    sf2.appendChild(C.el("dt", null, "Cifrado TLS"));
    sf2.appendChild(sumTlsVal);
    sumGrid.appendChild(sf1);
    sumGrid.appendChild(sf2);
    s2Aside.appendChild(sumGrid);
    s2Aside.appendChild(C.el("p", "panel-note", "No se permiten prefijos np:, lpc: ni admin: en el nombre del servidor."));
    step2.appendChild(s2Aside);

    // ---------- PASO 3 ----------
    var probeWrap = C.el("div", "probe-stage");
    var probeTop = C.el("div", "timeline-top");
    var probeTitle = C.el("h3", null, "3. Sondeo de capacidades en curso...");
    var probePill = C.el("span", "probe-pill", "Consultando Worker");
    probeTop.appendChild(probeTitle);
    probeTop.appendChild(probePill);
    probeWrap.appendChild(probeTop);
    var probeSummary = C.el("p", "panel-note", "Validando TLS, permisos mínimos y Query Store...");
    probeWrap.appendChild(probeSummary);
    var probeFacts = C.el("dl", "fact-grid");
    probeWrap.appendChild(probeFacts);
    var probeWarnings = C.el("div");
    probeWrap.appendChild(probeWarnings);

    var s3Actions = C.el("div", "btn-row");
    var retryPollBtn = C.el("button", "btn btn-auto btn-secondary", "Actualizar estado");
    retryPollBtn.type = "button";
    var editCredsBtn = C.el("button", "btn btn-auto btn-secondary", "← Corregir credenciales");
    editCredsBtn.type = "button";
    editCredsBtn.hidden = true;
    var toStep4Btn = C.el("button", "btn btn-auto", "Continuar a primera ingesta →");
    toStep4Btn.type = "button";
    toStep4Btn.hidden = true;
    s3Actions.appendChild(retryPollBtn);
    s3Actions.appendChild(editCredsBtn);
    s3Actions.appendChild(toStep4Btn);
    probeWrap.appendChild(s3Actions);
    step3.appendChild(probeWrap);

    // ---------- PASO 4 ----------
    var s4Wrap = C.el("div", "probe-stage");
    s4Wrap.appendChild(C.el("h3", null, "4. Ejecutar primera ingesta"));
    s4Wrap.appendChild(C.el("p", "panel-note", "Conexión activa y recolección automática programada. Lanza ahora la primera lectura de Query Store para diagnosticar consultas."));
    var s4TopActions = C.el("div", "btn-row");
    var runIngestBtn = C.el("button", "btn btn-auto", "Iniciar primera ingesta ahora →");
    runIngestBtn.type = "button";
    var skipLink = C.el("a", "btn btn-auto btn-secondary", "Ir al explorador de consultas");
    skipLink.href = "/consultas";
    s4TopActions.appendChild(runIngestBtn);
    s4TopActions.appendChild(skipLink);
    s4Wrap.appendChild(s4TopActions);

    var ingestStatusHost = C.el("div");
    s4Wrap.appendChild(ingestStatusHost);

    var finishActions = C.el("div", "btn-row");
    finishActions.hidden = true;
    var goQueriesLink = C.el("a", "btn btn-auto", "Ver consultas diagnosticadas →");
    goQueriesLink.href = "/consultas";
    var goAccessLink = C.el("a", "btn btn-auto btn-secondary", "Asignar a Desarrolladores");
    goAccessLink.href = "/accesos";
    finishActions.appendChild(goQueriesLink);
    finishActions.appendChild(goAccessLink);
    if (typeof opts.onClose === "function") {
      var closeAfterBtn = C.el("button", "btn btn-auto btn-secondary", "Cerrar asistente");
      closeAfterBtn.type = "button";
      closeAfterBtn.addEventListener("click", function () { opts.onClose(); });
      finishActions.appendChild(closeAfterBtn);
    }
    s4Wrap.appendChild(finishActions);
    step4.appendChild(s4Wrap);

    hostEl.appendChild(step1);
    hostEl.appendChild(step2);
    hostEl.appendChild(step3);
    hostEl.appendChild(step4);

    // ---------- Eventos y Lógica ----------
    function refreshSqlPreview() {
      state.platform = fPlatform.select.value;
      state.encryption = fTls.select.value;
      sqlPre.textContent = W.buildLeastPrivilegeScript({
        platform: state.platform,
        dbName: fPrevDb.input.value,
        loginName: fPrevUser.input.value
      });
      sumPlatVal.textContent = C.PLATFORM_LABELS[state.platform] || state.platform;
      sumTlsVal.textContent = state.encryption;
    }

    [fPlatform.select, fTls.select, fPrevDb.input, fPrevUser.input].forEach(function (n) {
      n.addEventListener("input", refreshSqlPreview);
      n.addEventListener("change", refreshSqlPreview);
    });

    copyBtn.addEventListener("click", function () {
      C.copyToClipboard(sqlPre.textContent).then(function () {
        C.showToast("Script T-SQL copiado.", "ok");
      }).catch(function () {
        C.showToast("No pudimos copiar al portapapeles.", "warn");
      });
    });

    toStep2Btn.addEventListener("click", function () {
      var pdb = fPrevDb.input.value.trim();
      var puser = fPrevUser.input.value.trim();
      if (pdb && !fDb.input.value.trim()) { fDb.input.value = pdb; }
      if (puser && !fUser.input.value.trim()) { fUser.input.value = puser; }
      refreshSqlPreview();
      goToStep(2);
      fServer.input.focus();
    });

    back1Btn.addEventListener("click", function () { goToStep(1); });

    function addFact(label, valueOrNode) {
      var item = C.el("div", "fact-item");
      item.appendChild(C.el("dt", null, label));
      var dd = C.el("dd");
      if (typeof valueOrNode === "string") { dd.textContent = valueOrNode; }
      else { dd.appendChild(valueOrNode); }
      item.appendChild(dd);
      probeFacts.appendChild(item);
    }

    function renderProbe(detail) {
      var verdict = W.interpretProbeResult(detail);
      probeTitle.textContent = "3. " + verdict.title;
      probeSummary.textContent = verdict.summary;
      probePill.replaceChildren(C.badgeFromMap(C.CONNECTION_STATUS_META, detail.status, detail.status));

      C.clear(probeFacts);
      addFact("Instancia / Base", detail.server + " / " + detail.databaseName);
      addFact("Último sondeo", C.formatDateTime(detail.lastProbedAt, "Pendiente"));

      if (detail.capabilities) {
        var caps = detail.capabilities;
        var qs = caps.queryStore || {};
        addFact("Motor SQL", (caps.engineVersion || "—") + (caps.edition ? " (" + caps.edition + ")" : ""));
        addFact("Soporte", C.badgeFromMap(C.SUPPORT_META, caps.supportLevel));
        addFact("Query Store", C.badgeFromMap(C.QUERY_STORE_META, qs.actualState, "Sin lectura"));
        addFact("Recolección", qs.intervalLengthMinutes ? ("Cada " + qs.intervalLengthMinutes + " min (automática)") : "Según perfil");
      }

      C.clear(probeWarnings);
      if (verdict.remediation) {
        probeWarnings.appendChild(C.el("div", "notice is-visible", "Sugerencia: " + verdict.remediation));
      }
      (verdict.warnings || []).forEach(function (w) {
        probeWarnings.appendChild(C.el("div", "notice is-visible", w));
      });

      editCredsBtn.hidden = verdict.phase !== "failed";
      toStep4Btn.hidden = !verdict.canProceed;
      if (verdict.phase === "failed") {
        markStep(3, "is-error");
      } else if (verdict.canProceed) {
        markStep(3, "is-done");
        if (typeof opts.onChanged === "function") { opts.onChanged(); }
      }
      return verdict;
    }

    function pollProbe() {
      if (!state.connectionId) { return; }
      if (state.pollTimer) { clearTimeout(state.pollTimer); state.pollTimer = null; }
      C.apiRequest("/api/connections/" + encodeURIComponent(state.connectionId)).then(function (res) {
        if (res.redirected || !res.ok || !res.data) {
          showNotice("No pudimos consultar el estado del sondeo.", false);
          return;
        }
        var verdict = renderProbe(res.data);
        if (W.shouldContinuePolling(res.data.status) && state.pollAttempts < 25) {
          state.pollAttempts += 1;
          state.pollTimer = setTimeout(pollProbe, 1800);
        } else if (verdict.phase === "pending") {
          probeSummary.textContent = "El sondeo sigue en cola. Usa «Actualizar estado» en unos segundos.";
        }
      });
    }

    form.addEventListener("submit", function (event) {
      event.preventDefault();
      showNotice("");

      var payload = {
        platform: state.platform,
        server: fServer.input.value.trim(),
        databaseName: fDb.input.value.trim(),
        authenticationMethod: "SqlAuthentication",
        encryption: state.encryption,
        username: fUser.input.value.trim(),
        password: fPass.input.value
      };

      var check = W.validateConnectionInput(payload);
      if (!check.valid) {
        showNotice(Object.keys(check.errors).map(function (k) { return check.errors[k]; }).join(" "), false);
        return;
      }

      submitConnBtn.disabled = true;
      C.apiRequest("/api/connections", { method: "POST", json: payload }).then(function (res) {
        if (res.redirected) { return; }
        if (res.ok && res.data && res.data.id) {
          state.connectionId = res.data.id;
          state.pollAttempts = 0;
          if (typeof opts.onChanged === "function") { opts.onChanged(); }
          goToStep(3);
          pollProbe();
          return;
        }
        if (res.status === 409 && res.problem && res.problem.existingConnectionId) {
          state.connectionId = res.problem.existingConnectionId;
          state.pollAttempts = 0;
          goToStep(3);
          showNotice("Esta base ya existía en ese servidor. Mostrando su estado actual.", true);
          pollProbe();
          return;
        }
        var lines = C.collectProblemMessages(res.problem);
        showNotice(lines.length ? lines.join(" ") : "No se pudo registrar la conexión.", false);
      }).catch(function () {
        showNotice("El servicio no está disponible.", false);
      }).then(function () {
        fPass.input.value = "";
        submitConnBtn.disabled = false;
      });
    });

    retryPollBtn.addEventListener("click", function () {
      state.pollAttempts = 0;
      pollProbe();
    });
    editCredsBtn.addEventListener("click", function () {
      clearTimers();
      goToStep(2);
      fPass.input.focus();
    });
    toStep4Btn.addEventListener("click", function () {
      clearTimers();
      skipLink.href = "/consultas?connectionId=" + encodeURIComponent(state.connectionId);
      goQueriesLink.href = "/consultas?connectionId=" + encodeURIComponent(state.connectionId);
      goAccessLink.href = "/accesos?connectionId=" + encodeURIComponent(state.connectionId);
      goToStep(4);
    });

    function renderRuns(runs) {
      C.clear(ingestStatusHost);
      if (!runs || !runs.length) {
        ingestStatusHost.appendChild(C.el("p", "panel-note", "Aún no hay ejecuciones registradas."));
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
        var rows = typeof run.rowsUpserted === "number" ? (run.rowsUpserted + " filas procesadas") : "Procesando...";
        card.appendChild(C.el("div", "timeline-meta", rows + (run.finishedAt ? (" · Fin: " + C.formatDateTime(run.finishedAt)) : "")));
        if (run.causeMessage) { card.appendChild(C.el("p", "panel-note", run.causeMessage)); }
        list.appendChild(card);
      });
      ingestStatusHost.appendChild(list);

      var done = latest.status === "Succeeded";
      finishActions.hidden = !done;
      if (done) {
        markStep(4, "is-done");
        if (typeof opts.onChanged === "function") { opts.onChanged(); }
      }
      return W.shouldContinuePolling(latest.status);
    }

    function pollIngest() {
      if (!state.connectionId) { return; }
      if (state.ingestTimer) { clearTimeout(state.ingestTimer); state.ingestTimer = null; }
      C.apiRequest("/api/connections/" + encodeURIComponent(state.connectionId) + "/ingestions").then(function (res) {
        if (res.redirected || !res.ok) { return; }
        var running = renderRuns(res.data || []);
        if (running && state.ingestAttempts < 30) {
          state.ingestAttempts += 1;
          state.ingestTimer = setTimeout(pollIngest, 1800);
        } else if (!running) {
          runIngestBtn.disabled = false;
        }
      });
    }

    runIngestBtn.addEventListener("click", function () {
      if (!state.connectionId) { return; }
      runIngestBtn.disabled = true;
      showNotice("");
      C.apiRequest("/api/connections/" + encodeURIComponent(state.connectionId) + "/ingestions", { method: "POST" }).then(function (res) {
        if (res.redirected) { return; }
        if (res.ok || res.status === 409) {
          C.showToast(res.status === 409 ? "Ya hay una ingesta en curso." : "Primera ingesta iniciada.", "ok");
          state.ingestAttempts = 0;
          pollIngest();
          return;
        }
        var lines = C.collectProblemMessages(res.problem);
        showNotice(lines.length ? lines.join(" ") : "No pudimos iniciar la ingesta.", false);
        runIngestBtn.disabled = false;
      }).catch(function () {
        showNotice("El servicio no está disponible.", false);
        runIngestBtn.disabled = false;
      });
    });

    function reset() {
      clearTimers();
      state.connectionId = null;
      state.pollAttempts = 0;
      state.ingestAttempts = 0;
      form.reset();
      fPass.input.value = "";
      C.clear(probeFacts);
      C.clear(probeWarnings);
      C.clear(ingestStatusHost);
      editCredsBtn.hidden = true;
      toStep4Btn.hidden = true;
      finishActions.hidden = true;
      runIngestBtn.disabled = false;
      refreshSqlPreview();
      goToStep(1);
    }

    refreshSqlPreview();

    return {
      reset: reset,
      destroy: clearTimers
    };
  }

  return { mount: mount };
});
