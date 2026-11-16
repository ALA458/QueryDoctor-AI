/* Query Doctor AI — lógica pura del Asistente de Conexión (/conectar), testeable en Node sin navegador. */
(function (root, factory) {
  var api = factory();
  if (typeof module === "object" && module && module.exports) {
    module.exports = api;
  }
  if (root) {
    root.QDWizardState = api;
  }
})(typeof globalThis !== "undefined" ? globalThis : window, function () {
  "use strict";

  var FORBIDDEN_PREFIXES = ["np:", "lpc:", "admin:"];

  var PROBE_REMEDIATION = {
    InvalidCredentials: "Verifica usuario SQL y contraseña; confirma que el inicio de sesión esté habilitado y sin expiración.",
    ServerUnreachable: "Revisa host, puerto (ej. servidor,1433) y conectividad desde la red Docker del Worker.",
    TlsCertificateInvalid: "El certificado TLS del servidor no es de confianza o su CN/SAN no coincide con el host configurado.",
    MissingPermission: "Ejecuta GRANT VIEW DATABASE STATE (o VIEW DATABASE PERFORMANCE STATE en Azure SQL) sobre la base objetivo.",
    Timeout: "El servidor tardó demasiado en responder al sondeo. Verifica firewall o carga transitoria.",
    UnsupportedVersion: "La instancia es anterior a SQL Server 2016 (13.x). Query Doctor AI requiere SQL Server 2016+ o Azure SQL.",
    PlatformMismatch: "Cambia la plataforma declarada entre SQL Server y Azure SQL Database en el paso anterior.",
    QueryStoreUnavailable: "No fue posible consultar sys.database_query_store_options en esa base.",
    Unknown: "Ocurrió un error inesperado al sondear la instancia. Revisa los registros del Worker."
  };

  function sanitizeSqlIdentifier(raw, fallback) {
    var trimmed = typeof raw === "string" ? raw.trim() : "";
    if (!trimmed) { return fallback || "MiBaseDeDatos"; }
    return trimmed.slice(0, 128).replace(/\]/g, "]]");
  }

  function sanitizeSqlStringLiteral(raw, fallback) {
    var trimmed = typeof raw === "string" ? raw.trim() : "";
    if (!trimmed) { return fallback || "qd_reader"; }
    return trimmed.slice(0, 128).replace(/'/g, "''");
  }

  function buildLeastPrivilegeScript(opts) {
    var cfg = opts || {};
    var platform = cfg.platform === "AzureSqlDatabase" ? "AzureSqlDatabase" : "SqlServer";
    var db = sanitizeSqlIdentifier(cfg.dbName, "QD_Lab");
    var user = sanitizeSqlIdentifier(cfg.loginName, "qd_reader");
    var literalUser = sanitizeSqlStringLiteral(cfg.loginName, "qd_reader");

    if (platform === "AzureSqlDatabase") {
      return [
        "-- Ejecutar conectado directamente a la base [" + db + "] (Azure SQL Database)",
        "IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'" + literalUser + "')",
        "    CREATE USER [" + user + "] WITH PASSWORD = N'<DEFINIR_CLAVE_FUERTE>';",
        "GO",
        "GRANT VIEW DATABASE PERFORMANCE STATE TO [" + user + "];",
        "-- Si tu nivel de Azure SQL aún usa el permiso clásico:",
        "-- GRANT VIEW DATABASE STATE TO [" + user + "];",
        "GO",
        "-- Verificar Query Store (recomendado READ_WRITE):",
        "SELECT actual_state_desc, desired_state_desc, interval_length_minutes",
        "FROM sys.database_query_store_options;"
      ].join("\n");
    }

    return [
      "-- 1) En master: crear login de solo lectura (sin roles de servidor ni escritura)",
      "USE [master];",
      "GO",
      "IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'" + literalUser + "')",
      "    CREATE LOGIN [" + user + "] WITH PASSWORD = N'<DEFINIR_CLAVE_FUERTE>', CHECK_POLICY = ON;",
      "GO",
      "-- 2) En la base monitoreada: otorgar únicamente lectura de telemetría",
      "USE [" + db + "];",
      "GO",
      "IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'" + literalUser + "')",
      "    CREATE USER [" + user + "] FOR LOGIN [" + user + "];",
      "GO",
      "GRANT VIEW DATABASE STATE TO [" + user + "];",
      "GO",
      "-- 3) Verificar que Query Store esté activo (Query Doctor nunca lo modifica):",
      "SELECT actual_state_desc, desired_state_desc, interval_length_minutes",
      "FROM sys.database_query_store_options;"
    ].join("\n");
  }

  function validateConnectionInput(input) {
    var data = input || {};
    var errors = {};
    var server = typeof data.server === "string" ? data.server.trim() : "";
    var databaseName = typeof data.databaseName === "string" ? data.databaseName.trim() : "";
    var username = typeof data.username === "string" ? data.username.trim() : "";
    var password = typeof data.password === "string" ? data.password : "";

    if (!server) {
      errors.server = "El servidor es obligatorio.";
    } else if (server.length > 255) {
      errors.server = "El servidor no puede superar 255 caracteres.";
    } else {
      var lower = server.toLowerCase();
      var hasForbidden = FORBIDDEN_PREFIXES.some(function (prefix) { return lower.indexOf(prefix) === 0; });
      if (hasForbidden) {
        errors.server = "No se permiten prefijos de protocolo (np:, lpc:, admin:).";
      } else {
        var comma = server.lastIndexOf(",");
        if (comma !== -1) {
          var portText = server.slice(comma + 1).trim();
          var port = Number(portText);
          if (!/^\d+$/.test(portText) || port < 1 || port > 65535) {
            errors.server = "El puerto después de la coma debe estar entre 1 y 65535.";
          }
        }
      }
    }

    if (!databaseName) {
      errors.databaseName = "El nombre de la base de datos es obligatorio.";
    } else if (databaseName.length > 128) {
      errors.databaseName = "La base de datos no puede superar 128 caracteres.";
    }

    if (!username) {
      errors.username = "El usuario SQL es obligatorio.";
    } else if (username.length > 128) {
      errors.username = "El usuario no puede superar 128 caracteres.";
    }

    if (!password) {
      errors.password = "La contraseña es obligatoria.";
    }

    return {
      valid: Object.keys(errors).length === 0,
      errors: errors
    };
  }

  function interpretProbeResult(detail) {
    if (!detail) {
      return {
        phase: "pending",
        canProceed: false,
        title: "Esperando respuesta de la API",
        summary: "Consultando estado del sondeo...",
        remediation: null
      };
    }

    if (detail.status === "Pending") {
      return {
        phase: "pending",
        canProceed: false,
        title: "Sondeo en curso",
        summary: "El Worker está validando TLS, permisos de solo lectura, versión del motor y configuración de Query Store.",
        remediation: null
      };
    }

    if (detail.status === "Active") {
      var caps = detail.capabilities || {};
      var qs = caps.queryStore || {};
      var warnings = [];
      if (caps.hasExcessivePermissions) {
        warnings.push("La cuenta tiene permisos superiores a VIEW DATABASE STATE. Se recomienda restringirla a solo lectura.");
      }
      if (qs.actualState && qs.actualState !== "READ_WRITE") {
        warnings.push("Query Store reporta estado " + qs.actualState + ": no habrá nuevas capturas hasta habilitar READ_WRITE en el servidor.");
      }
      if (Array.isArray(caps.omittedFeatures) && caps.omittedFeatures.length > 0) {
        caps.omittedFeatures.forEach(function (f) {
          if (f && f.message) { warnings.push(f.message); }
        });
      }
      return {
        phase: "active",
        canProceed: true,
        title: "Conexión verificada y activa",
        summary: "El Worker completó el sondeo y programó la ingesta automática periódica.",
        warnings: warnings,
        remediation: null
      };
    }

    var cause = detail.failureCause || "Unknown";
    return {
      phase: "failed",
      canProceed: false,
      title: detail.status === "Rejected" ? "Instancia no compatible" : "El sondeo no pudo activar la conexión",
      summary: detail.failureMessage || PROBE_REMEDIATION[cause] || PROBE_REMEDIATION.Unknown,
      remediation: PROBE_REMEDIATION[cause] || PROBE_REMEDIATION.Unknown
    };
  }

  function shouldContinuePolling(status) {
    return status === "Pending" || status === "Running";
  }

  return {
    PROBE_REMEDIATION: PROBE_REMEDIATION,
    sanitizeSqlIdentifier: sanitizeSqlIdentifier,
    buildLeastPrivilegeScript: buildLeastPrivilegeScript,
    validateConnectionInput: validateConnectionInput,
    interpretProbeResult: interpretProbeResult,
    shouldContinuePolling: shouldContinuePolling
  };
});
