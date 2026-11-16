/* Query Doctor AI — núcleo compartido de DOM seguro, API, formatos y shell por rol (CSP 'self'). */
(function (root, factory) {
  var api = factory();
  if (typeof module === "object" && module && module.exports) {
    module.exports = api;
  }
  if (root) {
    root.QDCore = api;
  }
})(typeof globalThis !== "undefined" ? globalThis : window, function () {
  "use strict";

  var SVG_NS = "http://www.w3.org/2000/svg";
  var ROLE_DBA = "DBA";
  var ROLE_DEVELOPER = "Desarrollador";

  var PLATFORM_LABELS = {
    SqlServer: "SQL Server",
    AzureSqlDatabase: "Azure SQL Database"
  };

  var SUPPORT_META = {
    Tested: { label: "Probado", tone: "ok" },
    CompatibleUntested: { label: "Compatible sin probar", tone: "warn" },
    NotSupported: { label: "No soportado", tone: "danger" }
  };

  var CONNECTION_STATUS_META = {
    Active: { label: "Activa", tone: "ok" },
    Pending: { label: "Sondeando", tone: "info" },
    Failed: { label: "Error de sondeo", tone: "danger" },
    Disabled: { label: "Deshabilitada", tone: "neutral" },
    Rejected: { label: "Rechazada", tone: "danger" }
  };

  var QUERY_STORE_META = {
    READ_WRITE: { label: "READ_WRITE", tone: "ok" },
    READ_ONLY: { label: "READ_ONLY", tone: "warn" },
    OFF: { label: "OFF", tone: "danger" }
  };

  var INGESTION_STATUS_META = {
    Running: { label: "En ejecución", tone: "info" },
    Succeeded: { label: "Completada", tone: "ok" },
    Failed: { label: "Fallida", tone: "danger" },
    Skipped: { label: "Omitida", tone: "warn" }
  };

  var INGESTION_TRIGGER_LABELS = {
    Manual: "Manual",
    Scheduled: "Programada"
  };

  var AUDIT_EVENT_OPTIONS = [
    ["LoginSucceeded", "Inicio de sesión correcto"],
    ["LoginFailed", "Inicio de sesión fallido"],
    ["LoginLockedOut", "Cuenta bloqueada"],
    ["PasswordChanged", "Contraseña actualizada"],
    ["ConnectionRegistered", "Conexión registrada"],
    ["ConnectionReRegistered", "Conexión re-registrada"],
    ["ConnectionAssigned", "Conexión asignada"],
    ["ConnectionUnassigned", "Conexión revocada"],
    ["DeveloperUserCreated", "Usuario Desarrollador creado"],
    ["ManualIngestionRequested", "Ingesta manual solicitada"],
    ["ExplanationRequested", "Explicación IA solicitada"],
    ["ExplanationReused", "Explicación reutilizada (caché)"],
    ["DataDeleted", "Purga / borrado de datos"]
  ];

  var AUDIT_EVENT_MAP = {};
  AUDIT_EVENT_OPTIONS.forEach(function (pair) { AUDIT_EVENT_MAP[pair[0]] = pair[1]; });

  function getDoc(doc) {
    return doc || (typeof document !== "undefined" ? document : null);
  }

  function $(id, doc) {
    var d = getDoc(doc);
    return d ? d.getElementById(id) : null;
  }

  function el(tag, className, text, doc) {
    var d = getDoc(doc);
    var node = d.createElement(tag);
    if (className) { node.className = className; }
    if (text !== undefined && text !== null) { node.textContent = String(text); }
    return node;
  }

  function svgEl(tag, attrs, doc) {
    var d = getDoc(doc);
    var node = d.createElementNS(SVG_NS, tag);
    Object.keys(attrs || {}).forEach(function (key) {
      node.setAttribute(key, String(attrs[key]));
    });
    return node;
  }

  function clear(node) {
    if (!node) { return; }
    if (typeof node.replaceChildren === "function") {
      node.replaceChildren();
    } else {
      while (node.firstChild) { node.removeChild(node.firstChild); }
    }
  }

  function badge(text, tone, doc) {
    var node = el("span", "badge", text || "Sin dato", doc);
    node.setAttribute("data-tone", tone || "neutral");
    return node;
  }

  function badgeFromMap(map, key, fallbackLabel, doc) {
    var entry = key ? map[key] : null;
    return badge(entry ? entry.label : (fallbackLabel || "Sin dato"), entry ? entry.tone : "neutral", doc);
  }

  function formatDateTime(iso, fallback) {
    if (!iso) { return fallback || "—"; }
    var d = new Date(iso);
    if (isNaN(d.getTime())) { return fallback || "—"; }
    return d.toLocaleString("es-CO", { dateStyle: "medium", timeStyle: "short" });
  }

  function formatRelativeTime(iso, nowMs) {
    if (!iso) { return "Sin registro"; }
    var d = new Date(iso);
    if (isNaN(d.getTime())) { return "Sin registro"; }
    var ref = typeof nowMs === "number" ? nowMs : Date.now();
    var diffSec = Math.round((ref - d.getTime()) / 1000);
    if (diffSec < 60 && diffSec >= -30) { return "Hace un momento"; }
    var min = Math.round(diffSec / 60);
    if (min >= 1 && min < 60) { return "Hace " + min + " min"; }
    var hr = Math.round(min / 60);
    if (hr >= 1 && hr < 24) { return "Hace " + hr + " h"; }
    return formatDateTime(iso);
  }

  function formatNumber(value, decimals) {
    if (typeof value !== "number" || isNaN(value)) { return "—"; }
    return value.toLocaleString("es-CO", {
      minimumFractionDigits: decimals || 0,
      maximumFractionDigits: decimals || 0
    });
  }

  function hasRole(me, role) {
    return Boolean(me && Array.isArray(me.roles) && me.roles.indexOf(role) !== -1);
  }

  function isDba(me) {
    return hasRole(me, ROLE_DBA);
  }

  function collectProblemMessages(problem) {
    var lines = [];
    if (problem && problem.errors && typeof problem.errors === "object") {
      Object.keys(problem.errors).forEach(function (key) {
        [].concat(problem.errors[key]).forEach(function (msg) {
          if (msg) { lines.push(String(msg)); }
        });
      });
    }
    if (!lines.length && problem && problem.detail) {
      lines.push(String(problem.detail));
    }
    if (!lines.length && problem && problem.title) {
      lines.push(String(problem.title));
    }
    return lines;
  }

  function goToLogin() {
    var target = window.location.pathname + window.location.search;
    window.location.replace("/login?returnUrl=" + encodeURIComponent(target));
  }

  function apiRequest(path, options) {
    var opts = Object.assign({ credentials: "same-origin" }, options || {});
    var headers = Object.assign({ Accept: "application/json" }, opts.headers || {});
    if (opts.json !== undefined) {
      headers["Content-Type"] = "application/json";
      opts.body = JSON.stringify(opts.json);
      delete opts.json;
    }
    opts.headers = headers;

    return fetch(path, opts).then(function (response) {
      if (response.status === 401) {
        goToLogin();
        return { status: 401, ok: false, redirected: true, data: null, problem: null };
      }
      var ct = response.headers.get("content-type") || "";
      var parseBody = response.status === 204
        ? Promise.resolve(null)
        : (ct.indexOf("json") !== -1 ? response.json().catch(function () { return null; }) : Promise.resolve(null));

      return parseBody.then(function (payload) {
        return {
          status: response.status,
          ok: response.ok,
          redirected: false,
          data: response.ok ? payload : null,
          problem: response.ok ? null : payload
        };
      });
    });
  }

  function copyToClipboard(text) {
    if (navigator.clipboard && typeof navigator.clipboard.writeText === "function") {
      return navigator.clipboard.writeText(text);
    }
    return new Promise(function (resolve, reject) {
      try {
        var ta = document.createElement("textarea");
        ta.value = text;
        ta.setAttribute("readonly", "");
        ta.className = "sr-only";
        document.body.appendChild(ta);
        ta.select();
        document.execCommand("copy");
        document.body.removeChild(ta);
        resolve();
      } catch (err) {
        reject(err);
      }
    });
  }

  var toastTimer = null;
  function showToast(message, tone) {
    var host = $("qd-toast");
    if (!host) {
      host = el("div", "qd-toast");
      host.id = "qd-toast";
      host.setAttribute("role", "status");
      host.setAttribute("aria-live", "polite");
      document.body.appendChild(host);
    }
    host.textContent = message;
    host.setAttribute("data-tone", tone || "info");
    host.classList.add("is-visible");
    if (toastTimer) { clearTimeout(toastTimer); }
    toastTimer = setTimeout(function () {
      host.classList.remove("is-visible");
    }, 4200);
  }

  function buildNavItems(me) {
    var dba = isDba(me);
    var items = [
      { id: "connections", href: "/", label: "Centro de control", code: "01", show: true },
      { id: "connect", href: "/?new=1", label: "Nueva conexión", code: "02", show: dba },
      { id: "queries", href: "/consultas", label: "Explorador de consultas", code: dba ? "03" : "02", show: true },
      { id: "access", href: "/accesos", label: "Accesos y usuarios", code: "04", show: dba },
      { id: "trace", href: "/trazabilidad", label: "Trazabilidad y auditoría", code: "05", show: dba }
    ];
    return items.filter(function (item) { return item.show; });
  }

  function renderBrandMark() {
    var svg = svgEl("svg", { class: "brand-mark", viewBox: "0 0 34 34", fill: "none", "aria-hidden": "true" });
    svg.appendChild(svgEl("circle", { class: "ring", cx: "17", cy: "17", r: "15.5", "stroke-width": "1.5" }));
    svg.appendChild(svgEl("path", {
      class: "beat",
      d: "M4 18h7l3-7 5 14 3-9 2 2h8",
      "stroke-width": "2",
      "stroke-linecap": "round",
      "stroke-linejoin": "round"
    }));
    return svg;
  }

  function renderNavIcon(id) {
    var svg = svgEl("svg", {
      class: "nav-ico",
      viewBox: "0 0 24 24",
      fill: "none",
      "stroke-width": "1.8",
      "stroke-linecap": "round",
      "stroke-linejoin": "round",
      "aria-hidden": "true"
    });
    var dById = {
      connections: "M4 6h16M4 12h16M4 18h10",
      connect: "M12 5v14M5 12h14",
      queries: "M4 5h16v14H4zM8 9h8M8 13h5",
      access: "M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2M9 7a4 4 0 1 0 0-8 4 4 0 0 0 0 8zM22 21v-2a4 4 0 0 0-3-3.87",
      trace: "M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"
    };
    if (id === "access") {
      svg.appendChild(svgEl("circle", { cx: "9", cy: "7", r: "3.5" }));
      svg.appendChild(svgEl("path", { d: "M3 20v-1a4 4 0 0 1 4-4h4a4 4 0 0 1 4 4v1M17 11l2 2 4-4" }));
      return svg;
    }
    svg.appendChild(svgEl("path", { d: dById[id] || "M5 12h14" }));
    return svg;
  }

  function applyTheme(theme) {
    var root = document.documentElement;
    if (theme === "dark") {
      root.setAttribute("data-theme", "dark");
    } else {
      root.removeAttribute("data-theme");
    }
    try { window.localStorage.setItem("qd_theme", theme === "dark" ? "dark" : "light"); } catch (_) { /* noop */ }
  }

  function getSavedTheme() {
    try { return window.localStorage.getItem("qd_theme") === "dark" ? "dark" : "light"; } catch (_) { return "light"; }
  }

  function setCollapsed(collapsed) {
    document.body.classList.toggle("nav-collapsed", Boolean(collapsed));
    try { window.localStorage.setItem("qd_nav_collapsed", collapsed ? "1" : "0"); } catch (_) { /* noop */ }
    var btn = document.querySelector(".side-collapse-btn");
    if (btn) {
      btn.textContent = collapsed ? "»" : "«";
      btn.setAttribute("aria-expanded", collapsed ? "false" : "true");
      btn.setAttribute("title", collapsed ? "Expandir panel lateral" : "Colapsar panel lateral");
    }
  }

  function mountSidebar(sideEl, me, activeNav) {
    if (!sideEl) { return; }
    clear(sideEl);

    var top = el("div", "side-top");
    var headerRow = el("div", "side-header-row");

    var brand = el("a", "brand-link");
    brand.href = "/";
    brand.appendChild(renderBrandMark());
    var brandText = el("div", "brand-text");
    brandText.appendChild(el("strong", null, "Query Doctor AI"));
    brandText.appendChild(el("small", null, "Observabilidad SQL"));
    brand.appendChild(brandText);
    headerRow.appendChild(brand);

    var isCollapsed = document.body.classList.contains("nav-collapsed");
    var collapseBtn = el("button", "side-collapse-btn", isCollapsed ? "»" : "«");
    collapseBtn.type = "button";
    collapseBtn.setAttribute("aria-label", "Alternar panel lateral");
    collapseBtn.setAttribute("aria-expanded", isCollapsed ? "false" : "true");
    collapseBtn.setAttribute("title", isCollapsed ? "Expandir panel lateral" : "Colapsar panel lateral");
    collapseBtn.addEventListener("click", function () {
      setCollapsed(!document.body.classList.contains("nav-collapsed"));
    });
    headerRow.appendChild(collapseBtn);

    var closeMobile = el("button", "side-close", "Cerrar ✕");
    closeMobile.type = "button";
    closeMobile.setAttribute("aria-label", "Cerrar menú");
    closeMobile.addEventListener("click", function () {
      document.body.classList.remove("nav-open");
    });
    headerRow.appendChild(closeMobile);
    top.appendChild(headerRow);

    var navLabel = el("p", "side-section-label", isDba(me) ? "Operación DBA" : "Diagnóstico");
    top.appendChild(navLabel);

    var nav = el("nav", "nav");
    nav.setAttribute("aria-label", "Navegación principal");
    buildNavItems(me).forEach(function (item) {
      var link = el("a", "nav-item" + (item.id === activeNav ? " is-active" : ""));
      link.href = item.href;
      link.setAttribute("title", item.label);
      if (item.id === activeNav) {
        link.setAttribute("aria-current", "page");
      }
      link.appendChild(renderNavIcon(item.id));
      link.appendChild(el("span", "nav-label", item.label));
      if (item.id === "connect" && document.body.getAttribute("data-page") === "connections") {
        link.addEventListener("click", function (ev) {
          ev.preventDefault();
          document.body.classList.remove("nav-open");
          document.dispatchEvent(new CustomEvent("qd:open-wizard"));
        });
      }
      nav.appendChild(link);
    });
    top.appendChild(nav);
    sideEl.appendChild(top);

    var foot = el("div", "side-foot");
    var who = el("div", "who");
    var strong = el("strong", null, me.userName || "Sesión activa");
    strong.id = "who-user";
    var roleText = me.roles && me.roles.length ? me.roles.join(" · ") : "Sin rol";
    var roleSpan = el("span", null, roleText);
    roleSpan.id = "who-role";
    who.appendChild(strong);
    who.appendChild(roleSpan);
    foot.appendChild(who);

    var currentTheme = getSavedTheme();
    var themeBtn = el("button", "side-theme-btn");
    themeBtn.type = "button";
    themeBtn.appendChild(el("span", "theme-label", "Tema"));
    var themeVal = el("span", null, currentTheme === "dark" ? "Oscuro" : "Claro");
    themeBtn.appendChild(themeVal);
    themeBtn.setAttribute("title", "Cambiar entre tema claro y oscuro");
    themeBtn.addEventListener("click", function () {
      var next = getSavedTheme() === "dark" ? "light" : "dark";
      applyTheme(next);
      themeVal.textContent = next === "dark" ? "Oscuro" : "Claro";
    });
    foot.appendChild(themeBtn);

    var logoutForm = el("form");
    logoutForm.method = "post";
    logoutForm.action = "/api/auth/logout";
    var logoutBtn = el("button", "link-btn", "Cerrar sesión");
    logoutBtn.type = "submit";
    logoutForm.appendChild(logoutBtn);
    foot.appendChild(logoutForm);
    sideEl.appendChild(foot);
  }

  function ensureMobileChrome() {
    if (!$("qd-mobile-bar")) {
      var bar = el("div", "mobile-bar");
      bar.id = "qd-mobile-bar";
      var menuBtn = el("button", "mobile-menu-btn", "☰ Menú");
      menuBtn.type = "button";
      menuBtn.addEventListener("click", function () {
        document.body.classList.toggle("nav-open");
      });
      var title = el("span", "mobile-brand", "Query Doctor AI");
      bar.appendChild(menuBtn);
      bar.appendChild(title);
      document.body.insertBefore(bar, document.body.firstChild);
    }
    if (!$("qd-nav-backdrop")) {
      var backdrop = el("div", "nav-backdrop");
      backdrop.id = "qd-nav-backdrop";
      backdrop.addEventListener("click", function () {
        document.body.classList.remove("nav-open");
      });
      document.body.appendChild(backdrop);
      document.addEventListener("keydown", function (ev) {
        if (ev.key === "Escape") { document.body.classList.remove("nav-open"); }
      });
    }
  }

  function mountShell(options) {
    var opts = options || {};
    var activeNav = opts.activeNav || document.body.getAttribute("data-nav") || "";
    applyTheme(getSavedTheme());
    try {
      if (window.localStorage.getItem("qd_nav_collapsed") === "1") {
        document.body.classList.add("nav-collapsed");
      }
    } catch (_) { /* noop */ }
    ensureMobileChrome();

    return apiRequest("/api/auth/me").then(function (res) {
      if (res.redirected) { return null; }
      if (!res.ok || !res.data) { throw new Error("me " + res.status); }
      var me = res.data;
      if (me.mustChangePassword) {
        window.location.replace("/cambiar-contrasena");
        return null;
      }

      var sideEl = document.querySelector(".side");
      mountSidebar(sideEl, me, activeNav);

      var main = $("main");
      if (main) { main.hidden = false; }

      if (opts.requireDba && !isDba(me) && main) {
        clear(main);
        var box = el("section", "empty empty-restricted");
        box.appendChild(el("p", "eyebrow", "Acceso restringido"));
        box.appendChild(el("h1", null, "Esta sección es exclusiva para el rol DBA"));
        box.appendChild(el("p", null, "Tu cuenta tiene perfil de Desarrollador. Puedes consultar las instancias asignadas y diagnosticar consultas desde el explorador."));
        var cta = el("a", "btn btn-auto", "Ir al explorador de consultas →");
        cta.href = "/consultas";
        box.appendChild(cta);
        main.appendChild(box);
        return { me: me, denied: true };
      }

      return { me: me, denied: false };
    });
  }

  return {
    ROLE_DBA: ROLE_DBA,
    ROLE_DEVELOPER: ROLE_DEVELOPER,
    PLATFORM_LABELS: PLATFORM_LABELS,
    SUPPORT_META: SUPPORT_META,
    CONNECTION_STATUS_META: CONNECTION_STATUS_META,
    QUERY_STORE_META: QUERY_STORE_META,
    INGESTION_STATUS_META: INGESTION_STATUS_META,
    INGESTION_TRIGGER_LABELS: INGESTION_TRIGGER_LABELS,
    AUDIT_EVENT_OPTIONS: AUDIT_EVENT_OPTIONS,
    AUDIT_EVENT_MAP: AUDIT_EVENT_MAP,
    $: $,
    el: el,
    svgEl: svgEl,
    clear: clear,
    badge: badge,
    badgeFromMap: badgeFromMap,
    formatDateTime: formatDateTime,
    formatRelativeTime: formatRelativeTime,
    formatNumber: formatNumber,
    hasRole: hasRole,
    isDba: isDba,
    buildNavItems: buildNavItems,
    collectProblemMessages: collectProblemMessages,
    goToLogin: goToLogin,
    apiRequest: apiRequest,
    copyToClipboard: copyToClipboard,
    showToast: showToast,
    mountShell: mountShell
  };
});
