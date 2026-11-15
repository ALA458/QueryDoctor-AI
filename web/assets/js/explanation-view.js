/* Query Doctor AI — vista de explicación de IA con trazabilidad y render solo texto (S2-11).
 * Usa exclusivamente createElement y textContent (RNF-SEG-07; compatible con CSP estricta). */
(function (root, factory) {
  var api = factory();
  if (typeof module === "object" && module && module.exports) {
    module.exports = api;
  }
  if (root) {
    root.QueryDoctorExplanationView = api;
  }
})(typeof globalThis !== "undefined" ? globalThis : window, function () {
  "use strict";

  var PERSISTENT_TRACED_NOTICE =
    "Trazada a evidencia del motor; no equivale a un diagnóstico confirmado";
  var HUMAN_REVIEW_ADVISORY =
    "Sugerencia informativa: no ha sido aplicada ni puede ejecutarse desde la plataforma; " +
    "requiere revisión humana y evaluación del DBA antes de aplicarse.";
  var WRITE_COST_ADVISORY =
    "Advertencia de costo de escritura: todo nuevo índice añade sobrecarga en inserciones, " +
    "actualizaciones, eliminaciones y mantenimiento del índice.";
  var NO_FINDINGS_HINT =
    "Esta consulta no registra hallazgos determinísticos en el último análisis.";

  var CLAIM_TYPE_META = {
    OBSERVACION: { label: "Observación", tone: "neutral" },
    INTERPRETACION: { label: "Interpretación", tone: "info" },
    RECOMENDACION: { label: "Recomendación", tone: "warn" }
  };

  function el(doc, tag, className, text) {
    var node = doc.createElement(tag);
    if (className) { node.className = className; }
    if (text !== undefined && text !== null) { node.textContent = String(text); }
    return node;
  }

  function badge(doc, text, tone) {
    var node = el(doc, "span", "badge", text);
    node.setAttribute("data-tone", tone || "neutral");
    return node;
  }

  function defaultFormatDateTime(iso) {
    if (!iso) { return "—"; }
    var d = new Date(iso);
    if (isNaN(d.getTime())) { return "—"; }
    return d.toLocaleString("es-CO", { dateStyle: "short", timeStyle: "short" });
  }

  function normalizeClaimType(raw) {
    return typeof raw === "string" ? raw.trim().toUpperCase() : "";
  }

  function isRecommendationClaim(claim) {
    return Boolean(claim && (normalizeClaimType(claim.claim_type) === "RECOMENDACION" || claim.requires_human_review === true));
  }

  function buildFindingLookup(detailFindings, viewFindings, defaultPlanId) {
    var map = {};
    (detailFindings || []).forEach(function (f) {
      var code = f.findingCode || f.finding_code;
      if (!code) { return; }
      map[code] = {
        planId: f.planId || defaultPlanId || null,
        ruleId: f.ruleId || f.rule_id || "",
        ruleName: f.ruleName || f.rule_name || ""
      };
    });
    (viewFindings || []).forEach(function (f) {
      var code = f.finding_code || f.findingCode;
      if (!code) { return; }
      if (!map[code]) {
        map[code] = {
          planId: defaultPlanId || null,
          ruleId: f.rule_id || f.ruleId || "",
          ruleName: f.rule_name || f.ruleName || ""
        };
      } else {
        if (!map[code].ruleId && (f.rule_id || f.ruleId)) {
          map[code].ruleId = f.rule_id || f.ruleId;
        }
        if (!map[code].ruleName && (f.rule_name || f.ruleName)) {
          map[code].ruleName = f.rule_name || f.ruleName;
        }
      }
    });
    return map;
  }

  function shouldShowWriteCostWarning(claim, findingLookup) {
    if (!claim) { return false; }
    if (claim.write_cost_warning === true) { return true; }
    if (!isRecommendationClaim(claim)) { return false; }
    var refs = claim.references || [];
    for (var i = 0; i < refs.length; i++) {
      var info = findingLookup && findingLookup[refs[i].finding_code];
      if (info && info.ruleId === "REG-02") {
        return true;
      }
    }
    return false;
  }

  function renderReferences(doc, claim, findingLookup, defaultPlanId, callbacks) {
    var refs = claim.references || [];
    if (!refs.length) { return null; }

    var refsBox = el(doc, "div", "exp-refs");
    refsBox.appendChild(el(doc, "span", "exp-refs-label", "Evidencia trazada:"));

    refs.forEach(function (ref) {
      var code = ref.finding_code;
      var info = (findingLookup && findingLookup[code]) || null;
      var ruleSuffix = info && info.ruleId ? " · " + info.ruleId : "";
      var findingBtn = el(doc, "button", "node-jump-btn exp-ref-finding", "Hallazgo " + code + ruleSuffix);
      findingBtn.type = "button";
      findingBtn.setAttribute("data-target-finding-code", code);
      findingBtn.addEventListener("click", function () {
        if (callbacks && typeof callbacks.onFocusFinding === "function") {
          callbacks.onFocusFinding(code);
        }
      });
      refsBox.appendChild(findingBtn);

      var planId = (info && info.planId) || defaultPlanId || null;
      (ref.node_ids || []).forEach(function (nodeId) {
        var nodeBtn = el(doc, "button", "node-jump-btn exp-ref-node", "Nodo #" + nodeId + " en el plan →");
        nodeBtn.type = "button";
        nodeBtn.setAttribute("data-target-finding-code", code);
        nodeBtn.setAttribute("data-target-node-id", String(nodeId));
        if (planId) {
          nodeBtn.setAttribute("data-target-plan-id", planId);
        } else {
          nodeBtn.disabled = true;
          nodeBtn.title = "No se encontró el plan asociado a este hallazgo en la ventana.";
        }
        nodeBtn.addEventListener("click", function () {
          if (planId && callbacks && typeof callbacks.onFocusNode === "function") {
            callbacks.onFocusNode(planId, nodeId);
          }
        });
        refsBox.appendChild(nodeBtn);
      });
    });

    return refsBox;
  }

  function renderClaimsList(doc, claims, validationStatus, findingLookup, defaultPlanId, callbacks) {
    var list = el(doc, "ol", "exp-claim-list");
    var isTraced = validationStatus === "TRAZADA";

    (claims || []).forEach(function (claim) {
      var claimType = normalizeClaimType(claim.claim_type);
      var typeMeta = CLAIM_TYPE_META[claimType] || { label: claim.claim_type || "Afirmación", tone: "neutral" };
      var isRec = isRecommendationClaim(claim);
      var passed = claim.passed !== false;

      var item = el(doc, "li", "exp-claim" + (isRec ? " exp-claim-rec" : "") + (!passed ? " exp-claim-failed" : ""));
      item.setAttribute("data-claim-type", claimType || "OTRO");
      item.setAttribute("data-passed", passed ? "true" : "false");

      var head = el(doc, "div", "exp-claim-head");
      var idxNumber = typeof claim.claim_index === "number" ? claim.claim_index + 1 : list.children.length + 1;
      head.appendChild(el(doc, "span", "exp-claim-index", "#" + idxNumber));
      head.appendChild(badge(doc, typeMeta.label, typeMeta.tone));

      if (claim.indication_label) {
        var indBadge = badge(doc, claim.indication_label === "indicio" ? "Indicio" : claim.indication_label, "warn");
        indBadge.title = "Interpretación apoyada en evidencia estimada o agregada.";
        head.appendChild(indBadge);
      }

      if (!passed) {
        head.appendChild(badge(doc, "No superó criterio", "danger"));
      } else if (isTraced) {
        head.appendChild(badge(doc, "Respaldada por evidencia", "ok"));
      } else {
        head.appendChild(badge(doc, "Criterio individual cumplido", "neutral"));
      }

      if (claim.kb_fragment_id) {
        head.appendChild(el(doc, "span", "exp-kb-ref", "KB: " + claim.kb_fragment_id));
      }

      item.appendChild(head);
      item.appendChild(el(doc, "p", "exp-claim-statement", claim.statement || ""));

      if (claim.indication_label) {
        item.appendChild(el(
          doc,
          "p",
          "exp-indication-note",
          "Etiqueta «" + claim.indication_label + "»: la evidencia asociada es estimada o de ejecución agregada."
        ));
      }

      var refsEl = renderReferences(doc, claim, findingLookup, defaultPlanId, callbacks);
      if (refsEl) {
        item.appendChild(refsEl);
      }

      if (isRec) {
        item.appendChild(el(doc, "p", "exp-advisory", HUMAN_REVIEW_ADVISORY));
        if (shouldShowWriteCostWarning(claim, findingLookup)) {
          item.appendChild(el(doc, "p", "exp-advisory exp-advisory-write", WRITE_COST_ADVISORY));
        }
      }

      list.appendChild(item);
    });

    return list;
  }

  function renderValidationFailures(doc, failures) {
    if (!failures || !failures.length) { return null; }
    var ul = el(doc, "ul", "exp-failures-list");
    failures.forEach(function (f) {
      var li = el(doc, "li", "exp-failure-item");
      var codeText = f.code || "FALLO";
      var claimPart = typeof f.claim_index === "number" ? " · Afirmación #" + (f.claim_index + 1) : "";
      li.appendChild(el(doc, "strong", "exp-failure-code", codeText + claimPart));
      li.appendChild(el(doc, "span", "exp-failure-criterion", ": " + (f.criterion || "Criterio de trazabilidad incumplido.")));
      ul.appendChild(li);
    });
    return ul;
  }

  function createRequestButton(doc, label, inFlight, onRequest) {
    var btn = el(doc, "button", "exp-action-btn", inFlight ? "Enviando solicitud..." : label);
    btn.type = "button";
    btn.disabled = Boolean(inFlight);
    btn.addEventListener("click", function () {
      if (typeof onRequest === "function") { onRequest(); }
    });
    return btn;
  }

  function renderCompletedView(doc, container, view, options, callbacks) {
    var formatDateTime = (options && options.formatDateTime) || defaultFormatDateTime;
    var defaultPlanId = (options && options.defaultPlanId) || null;
    var findingLookup = buildFindingLookup(options && options.detailFindings, view.findings, defaultPlanId);
    var validationStatus = view.validation_status || "NO_VERIFICADA";
    var isTraced = validationStatus === "TRAZADA";

    var statusBar = el(doc, "div", "exp-status-bar");
    var badgeGroup = el(doc, "div", "exp-badges");
    badgeGroup.appendChild(badge(doc, validationStatus, isTraced ? "ok" : "danger"));
    if (view.from_cache) {
      badgeGroup.appendChild(badge(doc, "Desde caché de contexto", "info"));
    }
    statusBar.appendChild(badgeGroup);

    var metaParts = [];
    if (view.model) { metaParts.push("Modelo: " + view.model); }
    if (view.prompt_version) { metaParts.push("Prompt: " + view.prompt_version); }
    if (view.completed_at) { metaParts.push("Completada: " + formatDateTime(view.completed_at)); }
    if (metaParts.length) {
      statusBar.appendChild(el(doc, "span", "exp-meta", metaParts.join(" · ")));
    }
    if (callbacks && typeof callbacks.onRequest === "function") {
      statusBar.appendChild(createRequestButton(doc, "Consultar de nuevo", options && options.requestInFlight, callbacks.onRequest));
    }
    container.appendChild(statusBar);

    if (isTraced) {
      var tracedNotice = el(doc, "div", "exp-banner exp-notice-traced");
      tracedNotice.setAttribute("role", "note");
      tracedNotice.appendChild(el(doc, "strong", null, "Aviso de trazabilidad: "));
      tracedNotice.appendChild(el(doc, "span", null, view.notice || PERSISTENT_TRACED_NOTICE));
      container.appendChild(tracedNotice);

      if (view.summary) {
        var summaryBox = el(doc, "div", "exp-summary");
        summaryBox.appendChild(el(doc, "h3", "sub-title", "Resumen estructurado"));
        summaryBox.appendChild(el(doc, "p", "exp-summary-text", view.summary));
        container.appendChild(summaryBox);
      }

      if (view.claims && view.claims.length) {
        container.appendChild(el(doc, "h3", "sub-title", "Afirmaciones y enlaces a evidencia"));
        container.appendChild(renderClaimsList(doc, view.claims, validationStatus, findingLookup, defaultPlanId, callbacks));
      } else {
        container.appendChild(el(doc, "p", "panel-note", "No se registraron afirmaciones detalladas para esta explicación."));
      }
      return;
    }

    var untracedBanner = el(doc, "div", "exp-banner exp-notice-untraced");
    untracedBanner.setAttribute("role", "alert");
    untracedBanner.appendChild(el(
      doc,
      "p",
      "exp-banner-title",
      "La salida del modelo no superó los criterios determinísticos de evidencia. Revisa los fallos reportados y contrasta con los hallazgos del motor."
    ));
    var failuresEl = renderValidationFailures(doc, view.validation_failures);
    if (failuresEl) {
      untracedBanner.appendChild(failuresEl);
    }
    container.appendChild(untracedBanner);

    var claims = view.claims || [];
    if (view.summary || claims.length) {
      var details = el(doc, "details", "details-toggle exp-unverified-details");
      details.open = false;
      details.appendChild(el(
        doc,
        "summary",
        null,
        "Ver afirmaciones que no superaron el validador (" + claims.length + ")"
      ));
      var inner = el(doc, "div", "exp-unverified-body");
      if (view.summary) {
        inner.appendChild(el(doc, "p", "exp-summary-text", view.summary));
      }
      if (claims.length) {
        inner.appendChild(renderClaimsList(doc, claims, validationStatus, findingLookup, defaultPlanId, callbacks));
      }
      details.appendChild(inner);
      container.appendChild(details);
    }
  }

  function renderExplanationState(container, statePayload, options, callbacks) {
    if (!container) { return; }
    var doc = container.ownerDocument;
    var formatDateTime = (options && options.formatDateTime) || defaultFormatDateTime;
    container.replaceChildren();

    if (!statePayload || statePayload.kind === "loading") {
      container.appendChild(el(doc, "p", "panel-note", "Consultando estado de explicación con IA..."));
      return;
    }

    if (statePayload.kind === "forbidden") {
      container.appendChild(el(
        doc,
        "p",
        "panel-note",
        "Tu rol actual no tiene permiso para consultar o solicitar explicaciones de IA."
      ));
      return;
    }

    if (statePayload.kind === "error") {
      var errBox = el(doc, "div", "exp-banner exp-banner-warn");
      errBox.appendChild(el(doc, "p", null, statePayload.message || "No pudimos consultar el estado de la explicación."));
      if (callbacks && typeof callbacks.onReload === "function") {
        var retryBtn = el(doc, "button", "exp-action-btn exp-action-secondary", "Reintentar consulta");
        retryBtn.type = "button";
        retryBtn.addEventListener("click", callbacks.onReload);
        errBox.appendChild(retryBtn);
      }
      container.appendChild(errBox);
      return;
    }

    if (statePayload.kind === "not_requested") {
      var emptyBox = el(doc, "div", "exp-cta-box");
      var hasFindings = Boolean(options && options.findingsCount > 0);
      emptyBox.appendChild(el(
        doc,
        "p",
        "panel-note",
        hasFindings
          ? "Aún no se ha solicitado una explicación de IA para esta consulta. El contexto se enmascara antes de enviarse y cada afirmación pasa por el control determinístico."
          : NO_FINDINGS_HINT + " Si solicitas explicación, el sistema comprobará si corresponde generarla."
      ));
      if (callbacks && typeof callbacks.onRequest === "function") {
        emptyBox.appendChild(createRequestButton(
          doc,
          "Solicitar explicación de IA",
          options && options.requestInFlight,
          callbacks.onRequest
        ));
      }
      container.appendChild(emptyBox);
      return;
    }

    var view = statePayload.view || {};
    var status = view.status || "NO_GENERADA";

    if (status === "PENDIENTE" || status === "EN_CURSO") {
      var pendingBar = el(doc, "div", "exp-banner exp-banner-info");
      pendingBar.setAttribute("role", "status");
      var top = el(doc, "div", "exp-badges");
      top.appendChild(badge(doc, status, "info"));
      if (view.requested_at) {
        top.appendChild(el(doc, "span", "exp-meta", "Solicitada: " + formatDateTime(view.requested_at)));
      }
      pendingBar.appendChild(top);
      pendingBar.appendChild(el(
        doc,
        "p",
        "exp-banner-text",
        "Generando explicación con contexto enmascarado y comprobando trazabilidad frente a la evidencia del motor..."
      ));
      container.appendChild(pendingBar);
      return;
    }

    if (status === "IA_NO_DISPONIBLE") {
      var unavail = el(doc, "div", "exp-banner exp-banner-warn");
      unavail.setAttribute("role", "status");
      var uHead = el(doc, "div", "exp-badges");
      uHead.appendChild(badge(doc, "IA_NO_DISPONIBLE", "warn"));
      if (view.cause) {
        uHead.appendChild(badge(doc, "Causa: " + view.cause, "neutral"));
      }
      unavail.appendChild(uHead);
      unavail.appendChild(el(
        doc,
        "p",
        "exp-banner-title",
        view.notice || "La explicación de IA no está disponible en este momento; revisa los hallazgos determinísticos del motor."
      ));
      unavail.appendChild(el(
        doc,
        "p",
        "panel-note",
        "Los datos de consumo, el árbol del plan y los hallazgos determinísticos siguen disponibles en los paneles inferiores."
      ));
      if (callbacks && typeof callbacks.onRequest === "function") {
        unavail.appendChild(createRequestButton(
          doc,
          "Reintentar explicación de IA",
          options && options.requestInFlight,
          callbacks.onRequest
        ));
      }
      container.appendChild(unavail);
      return;
    }

    if (status === "SIN_HALLAZGOS" || status === "NO_GENERADA") {
      var noGen = el(doc, "div", "exp-banner");
      var nHead = el(doc, "div", "exp-badges");
      nHead.appendChild(badge(doc, status, "neutral"));
      noGen.appendChild(nHead);
      noGen.appendChild(el(
        doc,
        "p",
        "exp-banner-text",
        view.notice || NO_FINDINGS_HINT
      ));
      container.appendChild(noGen);
      return;
    }

    if (status === "COMPLETADA") {
      renderCompletedView(doc, container, view, options, callbacks);
      return;
    }

    container.appendChild(el(doc, "p", "panel-note", "Estado de explicación no reconocido: " + status));
  }

  return {
    PERSISTENT_TRACED_NOTICE: PERSISTENT_TRACED_NOTICE,
    HUMAN_REVIEW_ADVISORY: HUMAN_REVIEW_ADVISORY,
    WRITE_COST_ADVISORY: WRITE_COST_ADVISORY,
    NO_FINDINGS_HINT: NO_FINDINGS_HINT,
    buildFindingLookup: buildFindingLookup,
    shouldShowWriteCostWarning: shouldShowWriteCostWarning,
    renderExplanationState: renderExplanationState
  };
});
