import test from "node:test";
import assert from "node:assert/strict";
import { createRequire } from "node:module";

const require = createRequire(import.meta.url);
const viewModule = require("../../web/assets/js/explanation-view.js");

function createFakeDom() {
  class FakeElement {
    constructor(tagName) {
      this.tagName = tagName.toUpperCase();
      this.className = "";
      this.id = "";
      this.attrs = {};
      this.children = [];
      this.listeners = {};
      this.disabled = false;
      this.open = false;
      this.title = "";
      this.type = "";
      this._textContent = "";
    }

    get ownerDocument() {
      return doc;
    }

    get textContent() {
      if (this.children.length === 0) {
        return this._textContent;
      }
      return this._textContent + this.children.map((c) => c.textContent).join("");
    }

    set textContent(value) {
      this.children = [];
      this._textContent = value === null || value === undefined ? "" : String(value);
    }

    get innerHTML() {
      throw new Error("RNF-SEG-07: no se permite usar innerHTML en pruebas o renderizado.");
    }

    set innerHTML(_) {
      throw new Error("RNF-SEG-07: no se permite asignar innerHTML.");
    }

    setAttribute(name, value) {
      this.attrs[name] = String(value);
    }

    getAttribute(name) {
      return Object.prototype.hasOwnProperty.call(this.attrs, name) ? this.attrs[name] : null;
    }

    appendChild(child) {
      this.children.push(child);
      return child;
    }

    replaceChildren(...nodes) {
      this._textContent = "";
      this.children = nodes.slice();
    }

    addEventListener(event, handler) {
      if (!this.listeners[event]) {
        this.listeners[event] = [];
      }
      this.listeners[event].push(handler);
    }

    click() {
      (this.listeners.click || []).forEach((fn) => fn());
    }
  }

  const doc = {
    createElement(tag) {
      return new FakeElement(tag);
    }
  };

  return doc;
}

function collectElements(root, predicate, acc = []) {
  if (predicate(root)) {
    acc.push(root);
  }
  for (const child of root.children) {
    collectElements(child, predicate, acc);
  }
  return acc;
}

function assertNoForbiddenPositiveWording(fullText) {
  const withoutAllowedStatusCode = fullText.replace(/NO_VERIFICADA/g, "");
  assert.doesNotMatch(withoutAllowedStatusCode, /\bverificad[ao]s?\b/i);
  assert.doesNotMatch(withoutAllowedStatusCode, /\bvalidad[ao]s?\b/i);
}

test("TRAZADA renderiza XSS como texto plano, enlaces a hallazgos/nodos, indicio y avisos de recomendación", () => {
  const doc = createFakeDom();
  const root = doc.createElement("div");
  const focusedFindings = [];
  const focusedNodes = [];

  const xssPayload = "<script>alert('xss')</script><img src=x onerror=alert(1)>";
  const planId = "plan-1111";

  viewModule.renderExplanationState(
    root,
    {
      kind: "view",
      view: {
        status: "COMPLETADA",
        validation_status: "TRAZADA",
        notice: viewModule.PERSISTENT_TRACED_NOTICE,
        from_cache: true,
        model: "qwen2.5",
        prompt_version: "v2",
        completed_at: "2026-11-15T18:00:00Z",
        summary: `Resumen seguro ${xssPayload}`,
        claims: [
          {
            claim_index: 0,
            claim_type: "INTERPRETACION",
            statement: `Interpretación con payload ${xssPayload}`,
            indication_label: "indicio",
            passed: true,
            requires_human_review: false,
            write_cost_warning: false,
            kb_fragment_id: "kb-001",
            references: [{ finding_code: "F-0001", node_ids: [4, 9] }]
          },
          {
            claim_index: 1,
            claim_type: "RECOMENDACION",
            statement: "Evaluar índice compuesto sobre la columna de filtrado.",
            indication_label: null,
            passed: true,
            requires_human_review: true,
            write_cost_warning: true,
            references: [{ finding_code: "F-0002", node_ids: [1] }]
          }
        ],
        validation_failures: [],
        findings: []
      }
    },
    {
      detailFindings: [
        { findingCode: "F-0001", planId, ruleId: "REG-01" },
        { findingCode: "F-0002", planId, ruleId: "REG-02" }
      ],
      findingsCount: 2
    },
    {
      onFocusFinding: (code) => focusedFindings.push(code),
      onFocusNode: (pId, nodeId) => focusedNodes.push([pId, nodeId])
    }
  );

  // Ningún elemento <script> o <img> fue creado (solo textContent)
  assert.equal(collectElements(root, (el) => el.tagName === "SCRIPT" || el.tagName === "IMG").length, 0);

  const text = root.textContent;
  assert.match(text, /TRAZADA/);
  assert.match(text, /Trazada a evidencia del motor; no equivale a un diagnóstico confirmado/);
  assert.match(text, /Desde caché de contexto/);
  assert.match(text, /Indicio/);
  assert.match(text, /<script>alert\('xss'\)<\/script>/);
  assert.match(text, /no ha sido aplicada ni puede ejecutarse desde la plataforma/i);
  assert.match(text, /Advertencia de costo de escritura/i);
  assertNoForbiddenPositiveWording(text);

  // Clic en enlaces de hallazgo y nodos del plan
  const findingBtns = collectElements(root, (el) => el.tagName === "BUTTON" && el.getAttribute("data-target-finding-code") && !el.getAttribute("data-target-node-id"));
  const nodeBtns = collectElements(root, (el) => el.tagName === "BUTTON" && el.getAttribute("data-target-node-id"));
  assert.equal(findingBtns.length, 2);
  assert.equal(nodeBtns.length, 3);

  findingBtns[0].click();
  nodeBtns[1].click();
  assert.deepEqual(focusedFindings, ["F-0001"]);
  assert.deepEqual(focusedNodes, [[planId, 9]]);
});

test("NO_VERIFICADA muestra código literal, fallos de validación y colapsa contenido sin aviso de TRAZADA", () => {
  const doc = createFakeDom();
  const root = doc.createElement("div");

  viewModule.renderExplanationState(
    root,
    {
      kind: "view",
      view: {
        status: "COMPLETADA",
        validation_status: "NO_VERIFICADA",
        notice: null,
        from_cache: false,
        summary: "Texto no trazado",
        validation_failures: [
          { code: "UnknownFindingId", claim_index: 0, criterion: "La afirmación cita un hallazgo inexistente." }
        ],
        claims: [
          {
            claim_index: 0,
            claim_type: "OBSERVACION",
            statement: "Afirmación fallida",
            passed: false,
            references: []
          }
        ],
        findings: []
      }
    },
    { detailFindings: [], findingsCount: 1 },
    {}
  );

  const text = root.textContent;
  assert.match(text, /NO_VERIFICADA/);
  assert.doesNotMatch(text, /Trazada a evidencia del motor/);
  assert.match(text, /UnknownFindingId/);
  assert.match(text, /No superó criterio/);
  assertNoForbiddenPositiveWording(text);

  const details = collectElements(root, (el) => el.tagName === "DETAILS");
  assert.equal(details.length, 1);
  assert.equal(details[0].open, false);
});

test("IA_NO_DISPONIBLE y consulta sin hallazgos muestran sus avisos y botón de solicitud", () => {
  const doc = createFakeDom();
  const root = doc.createElement("div");
  let requestedCount = 0;

  viewModule.renderExplanationState(
    root,
    {
      kind: "view",
      view: {
        status: "IA_NO_DISPONIBLE",
        cause: "Timeout",
        notice: "La explicación de IA no está disponible en este momento; revisa los hallazgos determinísticos del motor."
      }
    },
    { detailFindings: [], findingsCount: 1 },
    { onRequest: () => { requestedCount += 1; } }
  );

  let text = root.textContent;
  assert.match(text, /IA_NO_DISPONIBLE/);
  assert.match(text, /Timeout/);
  assert.match(text, /hallazgos determinísticos/i);
  assertNoForbiddenPositiveWording(text);

  const retryBtn = collectElements(root, (el) => el.tagName === "BUTTON")[0];
  retryBtn.click();
  assert.equal(requestedCount, 1);

  viewModule.renderExplanationState(
    root,
    { kind: "not_requested" },
    { detailFindings: [], findingsCount: 0 },
    { onRequest: () => { requestedCount += 1; } }
  );
  text = root.textContent;
  assert.match(text, /Solicitar explicación de IA/);
  assert.match(text, /no registra hallazgos determinísticos/i);
  assertNoForbiddenPositiveWording(text);
});
