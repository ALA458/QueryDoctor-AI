import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";

const repoRoot = path.resolve(import.meta.dirname, "..", "..");

function readWebFile(relPath) {
  return fs.readFileSync(path.join(repoRoot, relPath), "utf8");
}

test("tokens.css garantiza que [hidden] supere la especificidad de clases simples", () => {
  const tokens = readWebFile("web/assets/css/tokens.css");
  assert.match(
    tokens,
    /\[hidden\](?::not\(#_\)|\[hidden\])\s*\{\s*display:\s*none;\s*\}/,
    "tokens.css debe dar mayor especificidad a [hidden] para que gane a display: flex/grid"
  );
});

test("reglas flex/grid de paneles con estado hidden usan :not([hidden])", () => {
  const consultaCss = readWebFile("web/assets/css/consulta.css");
  assert.match(consultaCss, /\.detail-tab-panel:not\(\[hidden\]\)\s*\{[^}]*display:\s*flex\b/s);
  assert.doesNotMatch(
    consultaCss,
    /(^|\n)\s*\.detail-tab-panel\s*\{[^}]*\bdisplay\s*:/s,
    ".detail-tab-panel no debe declarar display sin :not([hidden])"
  );

  const workbenchCss = readWebFile("web/assets/css/workbench.css");
  assert.match(workbenchCss, /\.wizard-layout:not\(\[hidden\]\)\s*\{[^}]*display:\s*grid\b/s);
  assert.doesNotMatch(workbenchCss, /(^|\n)\s*\.wizard-layout\s*\{[^}]*\bdisplay\s*:/s);

  assert.match(workbenchCss, /\.btn-row:not\(\[hidden\]\)\s*\{[^}]*display:\s*flex\b/s);
  assert.doesNotMatch(workbenchCss, /(^|\n)\s*\.btn-row\s*\{[^}]*\bdisplay\s*:/s);

  assert.match(workbenchCss, /\.chip-bar:not\(\[hidden\]\)\s*\{[^}]*display:\s*flex\b/s);

  const authCss = readWebFile("web/assets/css/auth.css");
  assert.match(authCss, /\.shell\s+\.panel\s*\{/);
  assert.doesNotMatch(authCss, /(^|\n)\s*\.panel\s*\{/, "auth.css debe acotar .panel dentro de .shell");
  assert.doesNotMatch(authCss, /\.btn:not\(\[hidden\]\)/, ".btn no debe superar en especificidad a .btn-sm/.btn-secondary");
  assert.match(authCss, /(^|\n)\s*\.btn\s*\{[^}]*display:\s*inline-flex\b/s);
});

test("consulta.js enlaza pestañas ARIA y usa focusFindingCard al saltar desde el inspector de nodos", () => {
  const consultaJs = readWebFile("web/assets/js/consulta.js");

  assert.match(consultaJs, /btn\.setAttribute\("aria-controls",\s*"detail-panel-"\s*\+\s*t\.id\)/);
  assert.match(consultaJs, /aria-labelledby",\s*"detail-tab-/);
  assert.match(consultaJs, /detailTabState\.panels\[id\]\.hidden\s*=\s*id\s*!==\s*tabId/);
  assert.match(consultaJs, /ev\.key\s*===\s*"ArrowRight"/);
  assert.match(consultaJs, /ev\.key\s*===\s*"ArrowLeft"/);

  // El botón "Ir al hallazgo" dentro de renderOperatorInspector debe cambiar de pestaña vía focusFindingCard.
  const inspectorSection = consultaJs.slice(
    consultaJs.indexOf("function renderOperatorInspector"),
    consultaJs.indexOf("function renderLoadedPlanTree")
  );
  assert.match(inspectorSection, /focusFindingCard\(f\.findingCode\)/);

  // Opción A en pestaña principal (SQL arriba + IA debajo) y Score antes que Hallazgos en pFindings.
  const renderDetailSection = consultaJs.slice(
    consultaJs.indexOf("function renderDetail"),
    consultaJs.indexOf("function getQueryIdFromUrl")
  );
  assert.doesNotMatch(renderDetailSection, /detail-workbench/);
  const scoreIdx = renderDetailSection.indexOf("renderScorePanel(detail.score)");
  const findingsIdx = renderDetailSection.indexOf("renderFindingsPanel(detail.findings)");
  assert.ok(scoreIdx !== -1 && findingsIdx !== -1 && scoreIdx < findingsIdx, "Score debe mostrarse antes que Hallazgos");

  const consultaCss = readWebFile("web/assets/css/consulta.css");
  assert.match(consultaCss, /(^|\n)\.panel\s*\{[^}]*display:\s*block\b/s);
  assert.match(consultaCss, /\.exp-claim-list\s*\{[^}]*display:\s*grid\b/s);
  assert.match(consultaCss, /\.plan-workbench-split\s*\{[^}]*display:\s*grid\b/s);
  assert.match(consultaCss, /\.series-table-card:not\(\[hidden\]\)/);
  assert.match(consultaCss, /dialog\.chart-dialog:not\(\[open\]\)/);
  assert.match(consultaJs, /function openSeriesModal\(/);
  assert.match(consultaJs, /SERIES_METRICS/);
});
