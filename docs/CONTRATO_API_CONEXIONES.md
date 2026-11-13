# Contrato de la API de conexiones (S1-09 → S1-15)

> **Para:** Daniel (PR #35, pantalla de conexiones). **De:** Álvaro (S1-09).
> Reemplaza las partes de `docs/CONTRATO_FRONT_CONEXIONES.md` que difieren (marcadas con **Cambia**).
> Rutas vistas desde la API; el navegador las llama con el prefijo `/api` (Nginx lo quita).
> Los endpoints de P7 aún no existen en la rama: este documento es el contrato que se va a implementar.

## Reglas comunes

- Todo exige sesión (cookie de S1-06) y rol **DBA** (política `DbaOnly`): sin sesión **401**, sin rol **403**.
  El acceso de Desarrollador a sus conexiones asignadas llega en S2-12.
- **CSRF:** `POST /connections` exige mismo origen (cabecera `Origin` o `Sec-Fetch-Site: same-origin`), como
  `/auth/*`. Desde otro origen: **403**. `fetch` con `credentials: "same-origin"` ya cumple.
- Enumerados siempre como **texto** (nunca número).
- Nunca se devuelven credenciales, ni cifradas.
- Los textos de error son **fijos por código**: la API nunca reenvía el mensaje del motor SQL.

## `POST /connections` — **Cambia**: 202 en vez de 201/200

Cuerpo igual al de tu contrato:

```json
{ "platform": "SqlServer", "server": "sqlserver,1433", "databaseName": "QD_Lab",
  "authenticationMethod": "SqlAuthentication", "encryption": "Mandatory",
  "username": "qd_reader", "password": "********" }
```

| Respuesta | Cuándo | Cuerpo |
|---|---|---|
| **202 Accepted** + `Location: /connections/{id}` | Registrada en `Pending` y sondeo encolado | `{ "id": "…", "status": "Pending", "replacedPrevious": false }` |
| **202** con `replacedPrevious: true` | Ya existía la misma base en `Failed` o `Rejected`: se reemplazan las credenciales y se vuelve a sondear (mismo `id`) | igual |
| **400** `ValidationProblem` | Datos inválidos | `errors` por campo con las mismas claves del cuerpo: `platform`, `server`, `databaseName`, `authenticationMethod`, `encryption`, `username`, `password` |
| **409** `ProblemDetails` (**nuevo**) | La base ya está registrada en `Active`, `Pending` o `Disabled` | `detail` + `existingConnectionId` + `existingStatus` |
| 401 / 403 | Sin sesión / sin rol DBA o sin mismo origen | — |

- Hoy tu JS trata cualquier `response.ok` como éxito (202 ya funciona) y el 409 como "servicio no disponible":
  **cambio de tu lado**, mostrar `detail` (p. ej. "Esta base ya está registrada").
- El servidor se normaliza (minúsculas, sin `tcp:`, sin `,1433`, sin punto final). `np:`, `lpc:` y `admin:` dan 400.
  La base conserva sus mayúsculas, pero `QD_Lab` y `qd_lab` en el mismo servidor son la misma conexión (409).
- `encryption` omitido = `Mandatory`. No existe modo sin cifrado.

## Estados de la conexión — **Cambia**: hay tres nuevos

| `status` | Significado | Qué mostrar |
|---|---|---|
| `Pending` | Sondeo encolado o en curso | "Comprobando conexión…" y consultar `GET /connections/{id}` |
| `Active` | Sondeo correcto (Query Store OFF también queda `Active`, con aviso) | Versión, nivel de soporte, estado de Query Store |
| `Failed` | No se activó (CA-HU01.2) | `failureMessage` |
| `Rejected` | Versión anterior a SQL Server 2016 (CA-HU01.3); credenciales borradas | `failureMessage` (incluye la versión mínima) |
| `Disabled` | Desactivada | — |

**Garantía:** una conexión nunca se queda en `Pending` para siempre. Si el sondeo supera `Probe:TimeoutSeconds`
(15 s por defecto, RF-CFG-02), pasa a `Failed` con `failureCause: "Timeout"`. Sugerencia: mientras esté en
`Pending`, consultar cada 2 s y dejar de hacerlo a los 30 s.

## `failureCause` — **nuevo**: código cerrado

`null` si no hay fallo. El texto para el usuario va en `failureMessage` (fijo por código):

| Código | `failureMessage` |
|---|---|
| `InvalidCredentials` | El servidor rechazó el usuario o la contraseña. |
| `ServerUnreachable` | El servidor no responde o su nombre no se pudo resolver. |
| `TlsCertificateInvalid` | El certificado TLS del servidor no es de confianza. |
| `MissingPermission` | La cuenta no tiene VIEW DATABASE STATE ni VIEW DATABASE PERFORMANCE STATE sobre la base. |
| `Timeout` | El sondeo superó el tiempo límite configurado. |
| `UnsupportedVersion` | La versión del motor no está soportada. La versión mínima es SQL Server 2016 (13.x). |
| `PlatformMismatch` | La plataforma declarada no coincide con la detectada en el servidor. |
| `QueryStoreUnavailable` | No se pudo leer el estado de Query Store de la base. |
| `Unknown` | No se pudo sondear la conexión. |

## `GET /connections` → 200 — **Cambia**: campos nuevos

```json
[
  {
    "id": "6f0c…",
    "platform": "SqlServer",
    "server": "sqlserver",
    "databaseName": "QD_Lab",
    "status": "Active",
    "failureCause": null,
    "failureMessage": null,
    "supportLevel": "Tested",
    "engineVersion": "16.0.4315.2",
    "queryStoreState": "READ_WRITE",
    "hasExcessivePermissions": null,
    "lastProbedAt": "2026-11-13T10:00:03Z",
    "lastIngestionAt": null
  }
]
```

- `supportLevel`, `engineVersion` y `queryStoreState` son `null` mientras no haya sondeo correcto.
- `queryStoreState`: `READ_WRITE` | `READ_ONLY` | `OFF` | `ERROR` | `READ_CAPTURE_SECONDARY` | `null`.
- `hasExcessivePermissions`: **siempre `null`** en S1-09 = "no evaluado". La advertencia de RF-CFG-05 llega en el
  Sprint 2; mostrarla solo si llega `true`. Nunca interpretar `null` como "la cuenta está bien".
- `lastIngestionAt`: `null` hasta S1-11.

## `GET /connections/{id}` → 200 — **nuevo** (detalle y estado del sondeo)

```json
{
  "id": "6f0c…",
  "platform": "SqlServer",
  "server": "sqlserver",
  "databaseName": "QD_Lab",
  "status": "Active",
  "failureCause": null,
  "failureMessage": null,
  "minimumSupportedVersion": "SQL Server 2016 (13.x)",
  "lastProbedAt": "2026-11-13T10:00:03Z",
  "lastIngestionAt": null,
  "capabilities": {
    "engineVersion": "16.0.4315.2",
    "edition": "Developer Edition (64-bit)",
    "supportLevel": "Tested",
    "hasExcessivePermissions": null,
    "queryStore": {
      "actualState": "READ_WRITE",
      "desiredState": "READ_WRITE",
      "readOnlyReason": 0,
      "intervalLengthMinutes": 1,
      "captureMode": "ALL",
      "retentionDays": 30,
      "canIngest": true
    },
    "omittedFeatures": [
      { "code": "TempDbAndLogBytes", "message": "Esta versión no registra uso de TempDB ni bytes de log en Query Store; esas métricas quedarán vacías." }
    ]
  }
}
```

- `capabilities` es `null` si nunca hubo un sondeo correcto (`Pending`, `Failed`, `Rejected`).
- `queryStore` cubre CA-HU02.1 (estado, intervalo, modo de captura, retención) y RF-CFG-04 (estado deseado,
  motivo de solo lectura). `readOnlyReason` es el número de `sys.database_query_store_options` (0 = ninguno).
- Si `canIngest` es `false` (Query Store OFF o ERROR), mostrar: "Query Store no está disponible: la ingesta se
  omitirá" (CA-HU02.2 la implementa S1-11).
- `omittedFeatures` vacío = nada omitido (RF-CFG-07, RNF-COM-04).
- **404** `ProblemDetails` si el `id` no existe.
