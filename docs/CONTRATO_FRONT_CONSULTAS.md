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
| `sort` | `consumption` (porcentaje de consumo sobre el total de la ventana, descendente) \| `score` (Query Health Score 0–100 ascendente, priorizando las más críticas) |

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

- `sqlPreview`: texto ya **enmascarado** por la API (`LiteralMasker`, S2-04) y recortado; la pantalla solo lo pinta.
- `consumptionPct`: número de 0 a 100.
- `score`: puntaje Query Health Score 0–100 (nullable si no se ha calculado aún en la ventana).
- `band`: referencia provisional de banda (`Crítica` | `Atención` | `Saludable`), nullable si `score` es `null`.
- `findings[].severity`: `High` | `Medium` | `Low` | `Info` (también se aceptan `Alta`/`Media`/`Baja`).
- `findings[].summary`: texto corto de la evidencia enmascarada, sin HTML.
- Sin `findings` o `[]` se muestra "Sin hallazgos".
- En `/consultas`, cada fila enlaza a `/consulta?id={id}&window={window}` (S2-09).

## `GET /queries/{id}` (S2-09)

Query param opcional: `window=24h|7d|30d` (default `24h`).

Devuelve el detalle completo de la consulta para `/consulta?id={id}`:
- Metadatos (`id`, `connectionId`, `queryStoreQueryId`, `server`, `databaseName`, `platform`, `queryHash`, `originContext`, `window`, `windowStart`, `windowEnd`, `sqlText`, `sqlExpired`, `sqlMaskingFailure`).
- `plans[]`: resumen por `planId` (`queryStorePlanId`, `queryPlanHash`, `engineVersion`, `analysisStatus`, `notAnalyzableCause`, `ingestedAt`).
- `series[]`: series temporales por `(intervalId, intervalStart, intervalEnd, planId)` con `executionCount`, `avgDurationMs`, `avgCpuTimeMs`, `avgLogicalIoReads`, `avgPhysicalIoReads`, `avgRowCount`.
- `findings[]`: hallazgos de la última ejecución por regla/plan con `evidence` enmascarada (o `evidenceMasked=false` + `evidenceMaskingFailure` ante fallo cerrado), `nodes[]` y `context`.
- `score`: puntaje 0–100, `rankInWindow`, `totalQueriesInWindow`, `band`, `bandLabel`, `scaleDirection`, `usageDisclaimer`, pesos/componentes (`impactComponent`, `severityComponent`, `alphaUsed`, `cpuShare`, `durationShare`, `logicalReadsShare`, `weightedConsumptionPct`, `totalExecutions`), `contributingFindings[]` e `history[]`.

## `GET /queries/{id}/plans/{planId}` (S2-10)

Devuelve el visor jerárquico seguro del plan seleccionado para el panel de árbol en `/consulta?id={id}` (`RF-DASH-04`, `HU-13`):
- Primero enmascara el `EncryptedPlanXml` con `IMasker` (`PlanXmlMasker`, `RNF-SEG-05`, fallo cerrado) y luego parsea el XML ya enmascarado con `IPlanParser` (`ShowplanXmlParser`).
- Metadatos y estados seguros: `queryId`, `planId`, `queryStorePlanId`, `queryPlanHash`, `engineVersion`, `schemaVersion`, `analysisStatus`, `notAnalyzableCause`, `planExpired`, `planMaskingFailure`, `parseFailureReason`, `totalOperators`, `highlightedOperatorsCount`.
- `statements[]`: sentencias (`statementType`, `statementText` enmascarado, `statementSubTreeCost`, `statementEstRows`, `warnings[]`, `missingIndexes[]`, `parameters[]`, `memoryGrant`) con su árbol de operadores en **lista plana ordenada en preorden** (`operators[]`).
- `operators[]`: `nodeId`, `parentNodeId`, `depth`, `hasChildren`, `isLastSibling`, `physicalOp`, `logicalOp`, costos (`estimatedTotalSubtreeCost`, `costSharePct`, `estimateIo`, `estimateCpu`), filas (`estimateRows`, `estimatedRowsRead`, `tableCardinality`), `targetObject`, `predicateScalarString` (enmascarado), `implicitConversions[]`, `warnings[]`, `findings[]`, `hasFindings` y `maxFindingSeverity`.

## `POST /queries/{id}/explanations` y `GET /queries/{id}/explanations/latest` (S2-11)

Consumo desde `/consulta?id={id}` (`RF-DASH-05`, `RF-DASH-06`, `RF-IA-10`, `RF-IA-12`, `RNF-SEG-07`):
- `GET /queries/{id}/explanations/latest`: devuelve `404` (`NotRequested`) si nunca se ha solicitado o `200` con `status` (`PENDIENTE`, `EN_CURSO`, `COMPLETADA`, `IA_NO_DISPONIBLE`, `SIN_HALLAZGOS`, `NO_GENERADA`), `validation_status` (`TRAZADA` | `NO_VERIFICADA`), `notice`, `from_cache`, `summary`, `validation_failures[]`, `findings[]` y `claims[]`.
- En `claims[]`: cada afirmación expone `claim_index`, `claim_type` (`OBSERVACION` | `INTERPRETACION` | `RECOMENDACION`), `statement` (pintado siempre con `textContent`), `kb_fragment_id`, `indication_label` (`"indicio"` cuando aplica), `passed`, `requires_human_review`, `write_cost_warning` (cuando es `RECOMENDACION` y cita un hallazgo `REG-02`) y `references[]` (`finding_code`, `node_ids[]`) para enlazar directamente a `#finding-card-<finding_code>` y al nodo `#plan-node-<planId>-<nodeId>` del visor jerárquico.
- `POST /queries/{id}/explanations`: responde `202` (`PENDIENTE`) o `409` (`AlreadyInProgress`) para iniciar polling sobre `/latest`, o `200` (`SIN_HALLAZGOS`) sin encolar cuando no hay hallazgos (`CA-HU09.2`).
- `NO_VERIFICADA`: muestra el código literal, los motivos en `validation_failures[]` y oculta el contenido generado dentro de un bloque colapsado por defecto, manteniendo visibles los hallazgos determinísticos.
- `IA_NO_DISPONIBLE`: muestra `notice` y `cause` permitiendo reintentar sin ocultar SQL, series, árbol del plan ni hallazgos (`CA-HU12.1`, `CA-HU12.2`).

## Evidencia expirada y borrado a pedido (S2-13, RNF-SEG-10)

El Worker purga el texto SQL, el XML del plan y la evidencia de los hallazgos de esos planes cuando vence el plazo
de retención (`Retention:SqlTextAndPlanDays`, 30 días por defecto, contados desde la ingesta). El DBA también puede
borrarlos a pedido. Las filas, ids, hashes, métricas, puntajes, explicaciones y la traza **quedan**. "Expirado"
sale de una marca explícita (`purged_at`), nunca de que el valor esté vacío.

| Endpoint | Campo | Significado |
|---|---|---|
| `GET /queries` | `sqlExpired` | `true` solo si el texto se purgó. `sqlPreview` vale entonces `"[Evidencia expirada]"`. Un texto que nunca llegó da `sqlExpired=false` y `"[Sin texto SQL]"` |
| `GET /queries/{id}` | `sqlExpired`, `sqlExpiredAt` | Igual, con la fecha de la purga. `sqlText` es `null` |
| `GET /queries/{id}` | `plans[].planExpired`, `plans[].expiredAt` | El XML de ese plan se purgó |
| `GET /queries/{id}` | `findings[].evidenceExpired` | La evidencia del hallazgo se vació junto con su plan: `evidence` llega `{}`. Regla, severidad, nodos y contexto siguen |
| `GET /queries/{id}/plans/{planId}` | `planExpired`, `planExpiredAt` | El plan se purgó: `statements` vacío. Un plan `TooLarge` **no** sale expirado (lo explica `notAnalyzableCause`) |
| `GET /queries/{id}/explanations/latest` | `status=NO_GENERADA`, `cause=EvidenceExpired`, `notice` | Se pidió una explicación con la evidencia ya expirada: no se llama a la IA. Las explicaciones anteriores a la purga se siguen mostrando |

Pendiente en la web (Daniel): el listado y el detalle ya pintan "Evidencia expirada" con `sqlExpired` y
`planExpired`. Falta marcar la tarjeta del hallazgo con `evidenceExpired` y mostrar la causa `EvidenceExpired`.

### `DELETE /queries/{id}/evidence` y `DELETE /connections/{id}/evidence` (solo DBA)

- Borran el texto SQL, los planes y la evidencia de la consulta o de todas las consultas de la conexión. **No** borran
  la consulta, la conexión ni sus asignaciones.
- `204`: borrado, o ya no quedaba nada (es idempotente). `404`: no existe. `401` sin sesión, `403` para el
  Desarrollador (también con la conexión asignada) y para otro origen (CSRF, `UseSameOriginForms`).
- Cada llamada que llega a borrar deja un evento `DataDeleted` en `/audit`, en la misma transacción. El evento lleva
  el usuario, `subject_type` (`query` | `connection`), `subject_id` y `outcome` (`ok` | `nothing_to_delete`). La
  purga automática deja uno por conexión con `user_name = "system:retention"` y sin `user_id`.
- Un texto de Query Store puede pertenecer a varias consultas. Al borrarlo por una, deja de verse en todas.
- Lo purgado no vuelve con la siguiente ingesta: la ingesta solo inserta.
