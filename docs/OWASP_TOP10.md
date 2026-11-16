# OWASP_TOP10.md — Mapeo de riesgos de Query Doctor AI

> Dos listas porque el producto es web **y** usa un LLM: OWASP Top 10:2025 y
> OWASP Top 10 for LLM Applications 2025. Cada fila apunta al requisito que la
> controla y a la prueba que lo demuestra. Estado: ⬜ pendiente · 🟨 parcial · ✅ con prueba.

## OWASP Top 10:2025 (web)
| ID | Riesgo | Superficie en Query Doctor | Control (requisito) | Prueba / tarea | Estado |
|---|---|---|---|---|---|
| A01 | Broken Access Control (incl. SSRF) | API, conexiones de otro usuario; el host de una conexión lo escribe un usuario (SSRF hacia la red `targets`) | `FallbackPolicy` = autenticado, políticas `DbaOnly`/`ConnectionReader`, filtro en la consulta (RNF-SEG-12); solo DBA registra conexiones; solo el Worker llega a `targets` y solo por TDS | S2-12: filtro global de EF por conexión asignada con denegación por defecto (`ConnectionAccessFilterTests`, `ConnectionAccessFilterCoverageTests`, `AccessScopeMiddlewareTests`), 404 para lo no asignado por id directo y 403 al escribir (`DeveloperAccessEndpointsTests`, CA-HU01.5/01.6), asignaciones solo DBA y auditadas en la misma transacción (`AssignmentEndpointsTests`, `AssignmentAuditAtomicityTests`), cero `IgnoreQueryFilters` (`AccessScopeArchitectureTests`). **Falta:** el SSRF (lista de hosts o red restringida) y RLS en PostgreSQL como segunda barrera | 🟨 |
| A02 | Security Misconfiguration | Compose, `/hangfire`, cabeceras, Swagger en demo | Único puerto 443 en `web`; redes segmentadas (§10); `/hangfire` con filtro de autorización DBA (el filtro local por defecto no basta detrás de Nginx); CSP `script-src 'self'`; secretos como Docker secrets | S1-02, S1-10 | ⬜ |
| A03 | Software Supply Chain Failures | NuGet, npm, pip, imágenes Docker, GitHub Actions | Dependabot, `--vulnerable`/`npm audit`/`pip-audit` en CI, acciones fijadas por SHA | CI | ⬜ |
| A04 | Cryptographic Failures | Credenciales, SQL, planes, explicaciones; canal a SQL y al LLM | AES-256-GCM por campo (RNF-SEG-04/14); TLS 1.2+ con validación (RNF-SEG-03) | S1-05 (`SecretProtectorTests`), S1-08 (`QueryStoreGatewaySecurityTests`: Rejects non-TLS / TrustServerCertificate), S1-11 (`IngestConnectionTests`), S2-05 (`EfExplanationStore`) | ✅ |
| A05 | Injection | Consultas a Query Store; texto SQL/planes mostrados; XML | Sentencias constantes en lista blanca (RNF-SEG-01); XmlReader sin DTD y con límite (RF-MOT-16); render solo texto (RNF-SEG-07) | S1-08 (`TelemetryStatementWhitelistTests`, `QueryStoreGatewaySecurityTests`), S1-12 (`ShowplanXmlParserSecurityAndResilienceTests`), S2-11 (`tests/web/explanation_view.test.mjs`) | ✅ |
| A06 | Insecure Design | Que la IA "diagnostique" sin evidencia | Validador (a)(b)(c), TRAZADA ≠ validada (RF-IA-03/10) | S2-03 (`ExplanationValidatorTests`, `NumericEvidenceMatcherTests`), S2-11 (`explanation_view.test.mjs`) | ✅ |
| A07 | Authentication Failures | Login, primer DBA | Identity, hash adaptativo, bloqueo por intentos, límite de tasa en login, cookie HttpOnly+Secure+SameSite=Strict, expiración 30 min †, antifalsificación; DBA inicial desde secreto con cambio obligatorio (RNF-SEG-11/13) | S1-06, `ConnectionEndpointsTests`, `UserManagementAndRateLimitTests` (límite de tasa 429 en `/auth/login`, `POST /users` con `MustChangePassword=true`), `OriginCheckTests` | ✅ |
| A08 | Software or Data Integrity Failures | Traza y auditoría alterables | Rol de app sin UPDATE/DELETE en `audit.*`, trigger que rechaza cambios, `prev_hash` encadenado (RF-TRZ-01..03, §7) | S2-06: trigger que rechaza UPDATE/DELETE/TRUNCATE y cadena verificada (`TraceabilityPersistenceTests`, `TraceabilityEndpointsTests`: fila alterada a mano detectada). **Falta** el rol de base de datos sin UPDATE/DELETE: la app es dueña del esquema (T4) | 🟨 |
| A09 | Security Logging & Alerting Failures | Logins, conexiones, ingestas, borrados | audit_event (RF-TRZ-05); logs sin secretos ni literales (RNF-SEG-09) | S2-06 (`AuditEventsTests`), S2-12 (`DeveloperUserCreated`), S2-13 (`RetentionEndpointsTests`: `DataDeleted` en purga automática y borrado por DBA). Queda `ThresholdsChanged` sin emisor (fuera del MVP) | ✅ |
| A10 | Mishandling of Exceptional Conditions | XML inválido, versión sin capacidad, IA caída, corte de red | REG-11 sin detener el lote; omitir con aviso (RNF-COM-04); degradación (RNF-FIA-02); reanudar ingesta (RNF-FIA-01) | S1-11, S1-12; S2-05: `ExplainQueryTests` (4 causas de IA no disponible con hallazgos visibles), `HttpAiExplanationClientTests` (tiempo límite y red caída), `ExplanationEndpointsTests` (aviso y `/connections` funcionando) | 🟨 |

## OWASP Top 10 for LLM Applications 2025
| ID | Riesgo | Superficie | Control (requisito) | Prueba / tarea | Estado |
|---|---|---|---|---|---|
| LLM01 | Prompt Injection | Texto SQL, comentarios y nombres de objetos son datos no confiables | Delimitación de datos no confiables + suite de 10 casos (RNF-SEG-06) | S2-07 (`ai-service/tests/test_prompt_injection_suite.py`: 12 vectores adversariales; `PromptInjectionValidationTests.cs` en .NET) | ✅ |
| LLM02 | Sensitive Information Disclosure | Literales en SQL/plan/evidencia; datos reales hacia proveedor gratuito | LiteralMasker con ScriptDom en el Worker, sin opción de desactivar ni respaldo con regex (RNF-SEG-05, RA-03); candado `APP_ENV=prod` contra perfiles que comparten datos (ADR-10) | S2-04: `SeededLiteralsAcceptanceTests`, `MaskingCannotBeDisabledTests`, `MaskingArchitectureTests`; S2-05: `HttpAiExplanationClientTests.ElCuerpoHttpRealNoLlevaNingunCentinela`; S1-18 (`test_security_lock.py`) | ✅ |
| LLM03 | Supply Chain | Proveedor/modelo vía OpenRouter (otro endpoint puede servir otra cuantización) | Perfiles documentados; perfil de pago sin *fallbacks*; la traza registra perfil y `served_by` (ADR-10, §8.3) | S1-18 (`test_providers.py`, `test_endpoints.py`), S2-06 (`ExplanationTracePersistenceTests`) | ✅ |
| LLM04 | Data and Model Poisoning | Base de conocimiento del RAG | KB curada, cargada por migración, con fuente citada (RF-IA-04) | S1-17 / S2-08 (`KnowledgeBaseSeedDataTests`, `KnowledgeBaseRepositoryTests`) | ✅ |
| LLM05 | Improper Output Handling | Salida del modelo en la UI | Esquema Pydantic (lo que no cumple se rechaza); validador en .NET (ADR-06); render solo texto + CSP (RNF-SEG-07) | S1-18 (`test_schemas.py`, `gemini.py` `responseSchema`), S2-03 (`ExplanationValidatorTests`), S2-11 (`explanation_view.test.mjs`) | ✅ |
| LLM06 | Excessive Agency | — | El LLM no tiene herramientas; la red de `ai` no llega a bases objetivo. Mantenerlo así | Diseño (§8, §10) | ✅ por diseño |
| LLM07 | System Prompt Leakage | Prompt v1 | El prompt no contiene secretos ni lógica de autorización | S1-18 / S2-07 (`test_prompt_v1.py`, `test_prompt_injection_suite.py`) | ✅ |
| LLM08 | Vector and Embedding Weaknesses | pgvector / KB | KB de solo lectura en runtime; sin ingesta de documentos de usuario | S1-17 (`KnowledgeBaseModelTests`, `KnowledgeBaseRepositoryTests`) | ✅ |
| LLM09 | Misinformation | Atribución causal equivocada | Afirmaciones tipadas, causalidad solo desde relaciones curadas, falsos pasantes medidos (RF-IA-02/03, RNF-FUN-03) | S2-03 (`ExplanationValidatorTests`); **falta** medición empírica en S3-02/03 | 🟨 |
| LLM10 | Unbounded Consumption | Costo/cuotas del proveedor | Timeout 60 s †, 2 reintentos, límites de tokens y de solicitudes por minuto en `llm_client` (RNF-FIA-02); caché por hash (RF-IA-07); sin hallazgos no hay explicación | S1-18; S2-05: un solo intento desde .NET con tiempo total y `AutomaticRetry(Attempts = 0)` para no multiplicar las llamadas al LLM, caché por hash (`ConElMismoContextoSeReutiliza…`), sin hallazgos no se llama (`SinHallazgosNoSeLlamaALaIa`) | 🟨 |

## Riesgos residuales declarados
- Los nombres de tablas y columnas viajan al LLM sin seudonimizar (DA-13, aceptado como
  riesgo residual el 28-oct-2026; seudonimización planificada después).
- Las estimaciones del plan pueden reproducir un literal: con `TOP (n)` el optimizador estima `n` filas y
  `estimated_rows`/`EstimateRows` lo repiten. Se conservan porque el validador las necesita (RF-IA-03 b) (S2-04, H2).
- Los identificadores entre corchetes llegan intactos y pueden llevar instrucciones (`[Ignora las instrucciones…]`);
  los comentarios sí se eliminan. Lo delimita S2-07 (RNF-SEG-06) (S2-04, H3).
- La salida de `ai` restringida a un dominio no es nativa en Compose: firewall del host o
  proxy de salida, control parcial (RA-05).
- Las metas marcadas † son provisionales hasta calibrar con el laboratorio.
