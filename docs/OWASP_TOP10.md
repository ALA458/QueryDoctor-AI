# OWASP_TOP10.md — Mapeo de riesgos de Query Doctor AI

> Dos listas porque el producto es web **y** usa un LLM: OWASP Top 10:2025 y
> OWASP Top 10 for LLM Applications 2025. Cada fila apunta al requisito que la
> controla y a la prueba que lo demuestra. Estado: ⬜ pendiente · 🟨 parcial · ✅ con prueba.

## OWASP Top 10:2025 (web)
| ID | Riesgo | Superficie en Query Doctor | Control (requisito) | Prueba / tarea | Estado |
|---|---|---|---|---|---|
| A01 | Broken Access Control (incl. SSRF) | API, conexiones de otro usuario; el host de una conexión lo escribe un usuario (SSRF hacia la red `targets`) | `FallbackPolicy` = autenticado, políticas `DbaOnly`/`ConnectionMember`, filtro en la consulta (RNF-SEG-12); solo DBA registra conexiones; solo el Worker llega a `targets` y solo por TDS | CA-HU01.5/01.6, pruebas por endpoint y rol, S2-12 | ⬜ |
| A02 | Security Misconfiguration | Compose, `/hangfire`, cabeceras, Swagger en demo | Único puerto 443 en `web`; redes segmentadas (§10); `/hangfire` con filtro de autorización DBA (el filtro local por defecto no basta detrás de Nginx); CSP `script-src 'self'`; secretos como Docker secrets | S1-02, S1-10 | ⬜ |
| A03 | Software Supply Chain Failures | NuGet, npm, pip, imágenes Docker, GitHub Actions | Dependabot, `--vulnerable`/`npm audit`/`pip-audit` en CI, acciones fijadas por SHA | CI | ⬜ |
| A04 | Cryptographic Failures | Credenciales, SQL, planes, explicaciones; canal a SQL y al LLM | AES-256-GCM por campo (RNF-SEG-04/14); TLS 1.2+ con validación (RNF-SEG-03) | S1-05, S1-08 | ⬜ |
| A05 | Injection | Consultas a Query Store; texto SQL/planes mostrados; XML | Sentencias constantes en lista blanca (RNF-SEG-01); XmlReader sin DTD y con límite (RF-MOT-16); render solo texto (RNF-SEG-07) | S1-08, S1-12, S2-11 | ⬜ |
| A06 | Insecure Design | Que la IA "diagnostique" sin evidencia | Validador (a)(b)(c), TRAZADA ≠ validada (RF-IA-03/10) | S2-03 | ⬜ |
| A07 | Authentication Failures | Login, primer DBA | Identity, hash adaptativo, bloqueo por intentos, límite de tasa en login, cookie HttpOnly+Secure+SameSite=Strict, expiración 30 min †, antifalsificación; DBA inicial desde secreto con cambio obligatorio (RNF-SEG-11/13) | S1-06 | ⬜ |
| A08 | Software or Data Integrity Failures | Traza y auditoría alterables | Rol de app sin UPDATE/DELETE en `audit.*`, trigger que rechaza cambios, `prev_hash` encadenado (RF-TRZ-01..03, §7) | S2-06 | ⬜ |
| A09 | Security Logging & Alerting Failures | Logins, conexiones, ingestas, borrados | audit_event (RF-TRZ-05); logs sin secretos ni literales (RNF-SEG-09) | S2-06 | ⬜ |
| A10 | Mishandling of Exceptional Conditions | XML inválido, versión sin capacidad, IA caída, corte de red | REG-11 sin detener el lote; omitir con aviso (RNF-COM-04); degradación (RNF-FIA-02); reanudar ingesta (RNF-FIA-01) | S1-11 (hecho: plan irrecuperable → `NotAnalyzable` sin detener la ingesta; fallo a mitad no avanza la marca de agua, `IngestionPersistenceTests`; reintento exponencial de fallos transitorios; corte de red real en el Sprint 2), S1-12, S2-05 | ⬜ |

## OWASP Top 10 for LLM Applications 2025
| ID | Riesgo | Superficie | Control (requisito) | Prueba / tarea | Estado |
|---|---|---|---|---|---|
| LLM01 | Prompt Injection | Texto SQL, comentarios y nombres de objetos son datos no confiables | Delimitación de datos no confiables + suite de 10 casos (RNF-SEG-06) | S2-07 | ⬜ |
| LLM02 | Sensitive Information Disclosure | Literales en SQL/plan/evidencia; datos reales hacia proveedor gratuito | LiteralMasker con ScriptDom en el Worker, sin opción de desactivar ni respaldo con regex (RNF-SEG-05, RA-03); candado `APP_ENV=prod` contra perfiles que comparten datos (ADR-10) | S2-04, S1-18 | ⬜ |
| LLM03 | Supply Chain | Proveedor/modelo vía OpenRouter (otro endpoint puede servir otra cuantización) | Perfiles documentados; perfil de pago sin *fallbacks*; la traza registra perfil y `served_by` (ADR-10, §8.3) | S1-18 | ⬜ |
| LLM04 | Data and Model Poisoning | Base de conocimiento del RAG | KB curada, cargada por migración, con fuente citada (RF-IA-04) | S1-17 | ⬜ |
| LLM05 | Improper Output Handling | Salida del modelo en la UI | Esquema Pydantic (lo que no cumple se rechaza); validador en .NET (ADR-06); render solo texto + CSP (RNF-SEG-07) | S1-18, S2-03, S2-11 | ⬜ |
| LLM06 | Excessive Agency | — | El LLM no tiene herramientas; la red de `ai` no llega a bases objetivo. Mantenerlo así | Diseño (§8, §10) | ✅ por diseño |
| LLM07 | System Prompt Leakage | Prompt v1 | El prompt no contiene secretos ni lógica de autorización | Revisión S1-18 | ⬜ |
| LLM08 | Vector and Embedding Weaknesses | pgvector / KB | KB de solo lectura en runtime; sin ingesta de documentos de usuario | S1-17 | ⬜ |
| LLM09 | Misinformation | Atribución causal equivocada | Afirmaciones tipadas, causalidad solo desde relaciones curadas, falsos pasantes medidos (RF-IA-02/03, RNF-FUN-03) | S2-03, S3-02/03 | ⬜ |
| LLM10 | Unbounded Consumption | Costo/cuotas del proveedor | Timeout 60 s †, 2 reintentos, límites de tokens y de solicitudes por minuto en `llm_client` (RNF-FIA-02); caché por hash (RF-IA-07); sin hallazgos no hay explicación | S1-18, S2-05 | ⬜ |

## Riesgos residuales declarados
- Los nombres de tablas y columnas viajan al LLM sin seudonimizar (DA-13, aceptado como
  riesgo residual el 28-oct-2026; seudonimización planificada después).
- La salida de `ai` restringida a un dominio no es nativa en Compose: firewall del host o
  proxy de salida, control parcial (RA-05).
- Las metas marcadas † son provisionales hasta calibrar con el laboratorio.
