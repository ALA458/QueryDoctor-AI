/* Query Doctor AI — comportamiento de las pantallas de acceso (sin dependencias, compatible con la CSP). */
(function () {
  "use strict";

  var params = new URLSearchParams(window.location.search);
  var page = document.body.getAttribute("data-page");

  // Solo mensajes fijos: nunca se pinta texto que venga de la URL.
  var MESSAGES = {
    login: {
      error: {
        "1": ["Usuario o contraseña incorrectos.", false],
        locked: ["Cuenta bloqueada tras 5 intentos fallidos. Vuelve a intentarlo en 15 minutos.", false]
      },
      changed: { "1": ["Contraseña actualizada. Entra con la nueva.", true] },
      bye: { "1": ["Sesión cerrada.", true] }
    },
    change: {
      error: {
        coinciden: ["Las contraseñas nuevas no coinciden.", false],
        igual: ["La contraseña nueva debe ser distinta de la actual.", false],
        actual: ["La contraseña actual no es correcta.", false],
        politica: ["La contraseña nueva no cumple los requisitos.", false]
      }
    }
  };

  function showNotice() {
    var box = document.getElementById("notice");
    var table = MESSAGES[page];
    if (!box || !table) { return; }
    Object.keys(table).forEach(function (key) {
      var entry = table[key][params.get(key)];
      if (entry) {
        box.textContent = entry[0];
        box.className = "notice is-visible" + (entry[1] ? " is-ok" : "");
      }
    });
  }

  function carryReturnUrl() {
    var field = document.getElementById("returnUrl");
    var value = params.get("returnUrl");
    // El servidor vuelve a validar: solo se aceptan rutas locales.
    if (field && value && value.charAt(0) === "/" && value.charAt(1) !== "/" && value.charAt(1) !== "\\") {
      field.value = value;
    }
  }

  function wireReveal() {
    document.querySelectorAll("[data-reveal]").forEach(function (button) {
      button.addEventListener("click", function () {
        var input = document.getElementById(button.getAttribute("data-reveal"));
        var show = input.type === "password";
        input.type = show ? "text" : "password";
        button.textContent = show ? "Ocultar" : "Mostrar";
        button.setAttribute("aria-pressed", show ? "true" : "false");
      });
    });
  }

  function wireRules() {
    var nueva = document.getElementById("nueva");
    var confirmar = document.getElementById("confirmar");
    var submit = document.getElementById("submit");
    var rules = document.getElementById("rules");
    if (!nueva || !confirmar || !submit || !rules) { return; }

    function check() {
      var value = nueva.value;
      var met = {
        len: value.length >= 12,
        upper: /[A-Z]/.test(value),
        lower: /[a-z]/.test(value),
        digit: /[0-9]/.test(value),
        symbol: /[^A-Za-z0-9]/.test(value),
        match: value.length > 0 && value === confirmar.value
      };
      var all = true;
      rules.querySelectorAll("li").forEach(function (li) {
        var ok = met[li.getAttribute("data-rule")];
        li.classList.toggle("is-met", ok);
        all = all && ok;
      });
      submit.disabled = !all;
    }

    nueva.addEventListener("input", check);
    confirmar.addEventListener("input", check);
    check();
  }

  // Página de inicio provisional: comprueba la sesión con la API.
  function loadSession() {
    var card = document.getElementById("home");
    if (!card) { return; }
    fetch("/api/auth/me", { credentials: "same-origin" })
      .then(function (response) {
        if (response.status === 401) { window.location.replace("/login?returnUrl=" + encodeURIComponent(window.location.pathname)); return null; }
        if (!response.ok) { throw new Error("me " + response.status); }
        return response.json();
      })
      .then(function (me) {
        if (!me) { return; }
        if (me.mustChangePassword) { window.location.replace("/cambiar-contrasena"); return; }
        document.getElementById("who-user").textContent = me.userName;
        document.getElementById("who-roles").textContent = me.roles.length ? me.roles.join(", ") : "—";
        card.hidden = false;
      })
      .catch(function () {
        document.getElementById("who-user").textContent = "No se pudo comprobar la sesión";
        card.hidden = false;
      });
  }

  showNotice();
  carryReturnUrl();
  wireReveal();
  wireRules();
  loadSession();
})();
