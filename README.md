# Query Doctor AI

> Plataforma de diagnóstico de consultas SQL Server / Azure SQL Database vía Query Store con motor determinístico sobre Showplan XML, cálculo de *Query Health Score* y explicaciones con IA estrictamente trazadas a la evidencia técnica.

Proyecto de Ingeniería I · **Universidad Tecnológica de Bolívar (UTB), 2026-2**  
Base normativa: **SRS v1.0 + Adenda v1.1** · Documento de Arquitectura **v0.1**

---

## Visión General

Query Doctor AI es una herramienta de observabilidad y diagnóstico no invasiva orientada a DBAs y desarrolladores. Analiza el rendimiento de consultas a partir de los datos históricos agregados de **Query Store** y los planes de ejecución (**Showplan XML**), identificando patrones ineficientes sin ejecutar código DDL/DML ni forzar planes en los entornos de producción.

### Principios e Invariantes del Sistema

1. **Solo lectura estricta:** La plataforma solo ejecuta consultas `SELECT` predefinidas de una lista blanca contra las vistas de catálogo y Query Store (`RNF-SEG-01`). Jamás emite escrituras ni modificaciones.
2. **El motor determinístico es la única fuente de verdad técnica:** Los hallazgos y el score se calculan en el núcleo .NET mediante reglas matemáticas y de árbol sin dependencias externas.
3. **La IA no diagnostica; solo explica:** Las explicaciones generadas por el LLM se contrastan de forma determinista contra los hallazgos del motor y se catalogan exclusivamente como `TRAZADA` o `NO_VERIFICADA` (`RF-IA-02/03/10`).
4. **Enmascarado léxico obligatorio:** Antes de enviar texto SQL al servicio de IA, todo literal, valor constante o comentario se anonimiza en el núcleo .NET utilizando el analizador sintáctico oficial `ScriptDom` (`RNF-SEG-05`).
5. **Cifrado en reposo:** Las credenciales de bases de datos, consultas y planes se protegen en reposo mediante cifrado por sobre (*envelope*) con AES-256-GCM (`RNF-SEG-04`, `RNF-SEG-14`).

---

## Arquitectura

El sistema está diseñado como un **Monolito Modular** distribuido en 5 contenedores independientes con redes segmentadas:

```text
Navegador ──443──▶ web (Nginx + React SPA) ──▶ api (ASP.NET Core REST, AuthN/RBAC)
                                                    │ Hangfire sobre PostgreSQL
                                                    ▼
   Bases Monitoreadas ◀──TDS/TLS── worker (Ingesta, PlanParser, RuleEngine,
   (Solo SELECTs)                           Score, Masker ScriptDom, Validador)
                                                    │ Contexto enmascarado
                                                    ▼
                                      ai (FastAPI + RAG + Pydantic) ──▶ LLM (OpenRouter/Ollama)
                          db: PostgreSQL 16+ con pgvector (Repositorio local)
```

### Componentes de la Solución .NET (`QueryDoctor.slnx`)

* **`src/QueryDoctor.Domain`:** Entidades de dominio, tipos de valor e invariantes del negocio (cero dependencias de infraestructura o frameworks).
* **`src/QueryDoctor.Application`:** Interfaces de servicios, casos de uso, abstracciones de persistencia y contratos de seguridad (`IFieldEncryptor`).
* **`src/QueryDoctor.Infrastructure`:** Implementaciones de acceso a datos, pasarela de Query Store (`QueryStoreGateway`) y seguridad criptográfica (`SecretProtector`).
* **`src/QueryDoctor.Api`:** Host ASP.NET Core con autenticación, endpoints REST y documentación OpenAPI.
* **`src/QueryDoctor.Worker`:** Host en segundo plano para tareas asíncronas e ingesta periódica con Hangfire.
* **`tests/QueryDoctor.ArchitectureTests`:** Pruebas de cumplimiento arquitectónico con `NetArchTest`.
* **`tests/QueryDoctor.UnitTests`:** Pruebas unitarias de las capas de dominio, aplicación e infraestructura.

---

## Requisitos y Ejecución Local

### Prerrequisitos
* [.NET 10 SDK](https://dotnet.microsoft.com/download) (verificado con SDK 10.0.100 o superior).
* Git.

### Comandos de Desarrollo

1. **Restaurar y compilar la solución:**
   ```bash
   dotnet build QueryDoctor.slnx
   ```

2. **Ejecutar pruebas unitarias y de arquitectura:**
   ```bash
   dotnet test
   ```

3. **Verificar formato de código (.editorconfig):**
   ```bash
   dotnet format --verify-no-changes
   ```

4. **Ejecutar la API:**
   ```bash
   dotnet run --project src/QueryDoctor.Api/QueryDoctor.Api.csproj
   ```
   * Verificación de salud: `http://localhost:5277/health`
   * OpenAPI: `http://localhost:5277/openapi/v1.json`

5. **Laboratorio SQL Server 2022 y protocolo empírico del Apéndice A (`S1-07`):**
   ```powershell
   docker compose up -d sqlserver
   powershell -ExecutionPolicy Bypass -File .\lab\Run-Lab.ps1 -Step all
   ```
   * Documentación y guía de uso: [`lab/README.md`](./lab/README.md)
   * Informe de verificación empírica F1–F8 (`RF-EVA-04`, `DA-07`): [`lab/results/S1-07-verificacion.md`](./lab/results/S1-07-verificacion.md)
   * Fixtures de Showplan XML (`.sqlplan`): `tests/fixtures/plans/`
   * *Nota (`RF-EVA-04`, Invariante 10):* La plataforma no afirma eficacia, precisión ni exhaustividad sin remitir al informe versionado del laboratorio.

---

## Seguridad y Gestión de Secretos

El sistema implementa el servicio `SecretProtector` para cifrar datos sensibles en reposo mediante **AES-256-GCM**:
* Requiere una clave maestra de 256 bits (32 bytes) codificada en Base64 o Hexadecimal (64 caracteres).
* La clave se inyecta externamente mediante la variable de entorno `MASTER_ENCRYPTION_KEY`, la sección de configuración `Security:SecretProtector:MasterKey`, o mediante Docker secrets en `/run/secrets/master_encryption_key`.

---

## Equipo de Desarrollo

* **Álvaro Ayala:** Ingesta y persistencia.
* **Joel González:** Motor de reglas, parser de planes y cálculo de score.
* **Maykol Madrid:** Seguridad, cifrado de campos, integración de IA y gateway.
* **Daniel Tache:** Interfaz web SPA, gobernanza y autenticación.
