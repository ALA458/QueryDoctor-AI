# Contrato que espera el listado de consultas (S1-16 ↔ S1-11 / S1-14)

La pantalla `/consultas` (web/nginx/consultas.html + web/assets/js/consultas.js) ya está lista. Hasta que la API
exponga este endpoint muestra "Listado de consultas pendiente". Cookie de sesión de S1-06; todo texto se pinta con
`textContent`.

## `GET /queries`
Parámetros (todos opcionales):

| Parámetro | Valores |
|---|---|
| `connectionId` | id de una conexión (de `GET /connections`) |
| `window` | `24h` \| `7d` \| `30d` (por defecto `24h`) |
| `severity` | `High` \| `Medium` \| `Low` (filtra consultas con al menos un hallazgo de esa severidad) |
| `sort` | `consumption` (porcentaje de consumo sobre el total de la ventana, descendente). El score llega en S2-02 |

Respuesta `200`: lista (puede ser `[]`), ya ordenada y filtrada por la API (RNF-SEG-12: el filtro de permisos va en la consulta).

```json
[
  {
    "id": "a1b2…",
    "connectionId": "6f0c…",
    "databaseName": "QD_Lab",
    "sqlPreview": "SELECT … FROM Orders WHERE CustomerId = @p0",
    "consumptionPct": 37.4,
    "executions": 12840,
    "findings": [
      { "ruleId": "REG-01", "severity": "High", "summary": "Conversión implícita en Orders.CustomerId (nvarchar → int) en Index Seek" },
      { "ruleId": "REG-04", "severity": "Medium", "summary": "Key Lookup con 1 200 ejecuciones estimadas (umbral 100)" }
    ]
  }
]
```

- `sqlPreview`: texto ya **enmascarado** por la API (LiteralMasker, S2-04) y recortado; la pantalla solo lo pinta.
- `consumptionPct`: número de 0 a 100.
- `findings[].severity`: `High` | `Medium` | `Low` (también se aceptan `Alta`/`Media`/`Baja`).
- `findings[].summary`: texto corto de la evidencia, sin HTML.
- Sin `findings` o `[]` se muestra "Sin hallazgos".
- Pendiente para S2-09: `GET /queries/{id}` con el detalle; esta pantalla aún no enlaza a él.
