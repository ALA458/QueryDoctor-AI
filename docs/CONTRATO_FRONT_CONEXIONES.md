# Contrato que espera la pantalla de Conexiones (S1-15 ↔ S1-09)

La pantalla `/` (web/nginx/index.html + web/assets/js/app.js) ya está lista. Hasta que la API exponga estos
endpoints muestra el aviso "Registro de conexiones pendiente". Los nombres siguen las entidades de S1-04
(`core.connection`, `core.capability_profile`). Todo va por la cookie de sesión de S1-06.

## `GET /connections` → 200
Lista (puede ser `[]`). Los enums pueden venir como texto (preferido) o como número.

```json
[
  {
    "id": "6f0c…",
    "platform": "SqlServer",
    "server": "sqlserver,1433",
    "databaseName": "QD_Lab",
    "status": "Active",
    "supportLevel": "Tested",
    "queryStoreState": "READ_WRITE",
    "hasExcessivePermissions": false,
    "engineVersion": "16.0.4165",
    "lastIngestionAt": "2026-11-12T14:40:00Z"
  }
]
```

- `platform`: `SqlServer` | `AzureSqlDatabase`
- `status`: `Active` | `Disabled`
- `supportLevel`: `Tested` | `CompatibleUntested` | `NotSupported` (`null` si aún no hay sondeo)
- `queryStoreState`: `READ_WRITE` | `READ_ONLY` | `OFF` (`null` si aún no hay sondeo)
- `hasExcessivePermissions`: `true` muestra la advertencia de RF-CFG-05
- `lastIngestionAt`: `null` si todavía no hubo ingesta
- Nunca se devuelven credenciales, ni cifradas.

## `POST /connections`
Cuerpo JSON:

```json
{
  "platform": "SqlServer",
  "server": "sqlserver,1433",
  "databaseName": "QD_Lab",
  "authenticationMethod": "SqlAuthentication",
  "encryption": "Mandatory",
  "username": "qd_reader",
  "password": "********"
}
```

- `201` (o `200`) si se registra. La pantalla vuelve a pedir `GET /connections`.
- `400` con `ProblemDetails` (`errors` por campo o `detail`): la pantalla muestra los mensajes tal cual.
- `403` sin rol DBA; `401` sin sesión.
- La contraseña va del navegador a la API (no pasa por PHP ni por ningún intermediario) y la página la borra del formulario.
