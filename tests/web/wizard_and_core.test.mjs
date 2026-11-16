import test from "node:test";
import assert from "node:assert/strict";
import { createRequire } from "node:module";

const require = createRequire(import.meta.url);
const core = require("../../web/assets/js/qd-core.js");
const wizard = require("../../web/assets/js/wizard-state.js");

test("buildNavItems filtra rutas DBA frente a Desarrollador", () => {
  const dbaNav = core.buildNavItems({ userName: "dba", roles: ["DBA"] }).map((i) => i.id);
  const devNav = core.buildNavItems({ userName: "dev", roles: ["Desarrollador"] }).map((i) => i.id);

  assert.deepEqual(dbaNav, ["connections", "connect", "queries", "access", "trace"]);
  assert.deepEqual(devNav, ["connections", "queries"]);
});

test("collectProblemMessages extrae errores de validacion y detail RFC 7807", () => {
  assert.deepEqual(
    core.collectProblemMessages({ errors: { server: ["Host invalido"], password: ["Falta"] } }),
    ["Host invalido", "Falta"]
  );
  assert.deepEqual(core.collectProblemMessages({ detail: "Solo DBA." }), ["Solo DBA."]);
});

test("buildLeastPrivilegeScript genera T-SQL seguro segun plataforma y escapa corchetes", () => {
  const onPrem = wizard.buildLeastPrivilegeScript({
    platform: "SqlServer",
    dbName: "Ventas]Prod",
    loginName: "lector'sql"
  });
  assert.match(onPrem, /USE \[Ventas\]\]Prod\];/);
  assert.match(onPrem, /GRANT VIEW DATABASE STATE TO \[lector'sql\];/);
  assert.match(onPrem, /name = N'lector''sql'/);

  const azure = wizard.buildLeastPrivilegeScript({
    platform: "AzureSqlDatabase",
    dbName: "AppDb",
    loginName: "qd_reader"
  });
  assert.match(azure, /VIEW DATABASE PERFORMANCE STATE/);
  assert.doesNotMatch(azure, /USE \[master\]/);
});

test("validateConnectionInput rechaza protocolos prohibidos y puertos invalidos", () => {
  assert.equal(wizard.validateConnectionInput({
    server: "np:\\\\.\\pipe\\sql\\query",
    databaseName: "QD_Lab",
    username: "qd",
    password: "secret"
  }).valid, false);

  assert.equal(wizard.validateConnectionInput({
    server: "sql,99999",
    databaseName: "QD_Lab",
    username: "qd",
    password: "secret"
  }).valid, false);

  assert.equal(wizard.validateConnectionInput({
    server: "qd-lab-sql,1433",
    databaseName: "QD_Lab",
    username: "qd_reader",
    password: "secret"
  }).valid, true);
});

test("interpretProbeResult distingue Pending, Active con avisos y Failed con remediacion", () => {
  const pending = wizard.interpretProbeResult({ status: "Pending" });
  assert.equal(pending.phase, "pending");
  assert.equal(pending.canProceed, false);
  assert.equal(wizard.shouldContinuePolling("Pending"), true);

  const active = wizard.interpretProbeResult({
    status: "Active",
    capabilities: {
      hasExcessivePermissions: true,
      queryStore: { actualState: "READ_ONLY" },
      omittedFeatures: [{ code: "TempDbAndLogBytes", message: "No disponible en 2016" }]
    }
  });
  assert.equal(active.phase, "active");
  assert.equal(active.canProceed, true);
  assert.equal(active.warnings.length, 3);

  const failed = wizard.interpretProbeResult({
    status: "Failed",
    failureCause: "MissingPermission",
    failureMessage: "Falta VIEW DATABASE STATE"
  });
  assert.equal(failed.phase, "failed");
  assert.equal(failed.canProceed, false);
  assert.match(failed.remediation, /GRANT VIEW DATABASE STATE/);
});
