/* Query Doctor AI — Página dedicada /conectar (monta directamente el flujo de QDWizardUI). */
(function () {
  "use strict";

  var C = window.QDCore;
  var $ = C.$;

  C.mountShell({ activeNav: "connect", requireDba: true }).then(function (ctx) {
    if (!ctx || ctx.denied) { return; }
    if (window.QDWizardUI) {
      window.QDWizardUI.mount($("wizard-page-host"));
    }
  }).catch(function () {
    var main = $("main");
    if (main) { main.hidden = false; }
  });
})();
