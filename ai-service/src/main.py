"""Aplicación FastAPI para el microservicio de IA de Query Doctor (ADR-02, ADR-10, RF-IA-02)."""

from contextlib import asynccontextmanager
from typing import Annotated, Any, Dict
from fastapi import Depends, FastAPI, HTTPException, status
from fastapi.responses import JSONResponse
from src.config import Settings, get_settings
from src.models.schemas import ExplainRequest, ExplainResponse
from src.providers.base import BaseLLMProvider
from src.providers.factory import get_llm_provider
from src.security_lock import enforce_security_lock, get_data_policy


@asynccontextmanager
async def lifespan(app: FastAPI):
    """Ciclo de vida de FastAPI: ejecuta el candado de seguridad al iniciar (ADR-10, RES-06)."""
    settings = get_settings()
    # Si la configuración viola el candado de seguridad, el microservicio no arranca
    enforce_security_lock(settings)
    yield


app = FastAPI(
    title="Query Doctor AI - Explain Service",
    version="1.0.0",
    description="Microservicio de IA para diagnóstico explicable de consultas SQL Server",
    lifespan=lifespan,
)


def get_provider(
    settings: Annotated[Settings, Depends(get_settings)],
) -> BaseLLMProvider:
    """Inyección de dependencias para el proveedor de LLM."""
    return get_llm_provider(settings)


@app.get("/health", summary="Healthcheck", tags=["Monitoreo"])
async def health_check() -> Dict[str, str]:
    """Endpoint de comprobación de salud para Docker y orquestación."""
    return {"status": "healthy"}


@app.get("/info", summary="Metadatos del servicio", tags=["Monitoreo"])
async def get_service_info(
    settings: Annotated[Settings, Depends(get_settings)],
) -> Dict[str, Any]:
    """Devuelve el estado de gobernanza y perfil activo de LLM."""
    return {
        "app_name": "QueryDoctor-AI-Service",
        "version": "1.0.0",
        "app_env": settings.APP_ENV,
        "llm_profile": settings.LLM_PROFILE,
        "data_policy": get_data_policy(settings.LLM_PROFILE),
        "prompt_version": settings.PROMPT_VERSION,
        "timeout_seconds": settings.LLM_TIMEOUT_SECONDS,
        "max_retries": settings.LLM_MAX_RETRIES,
    }


@app.post(
    "/explain",
    status_code=status.HTTP_200_OK,
    summary="Generar explicación diagnóstica estructurada",
    tags=["Explicación"],
)
async def explain_query(
    request: ExplainRequest,
    provider: Annotated[BaseLLMProvider, Depends(get_provider)],
) -> ExplainResponse:
    """Genera una explicación estructurada en afirmaciones tipadas (RF-IA-02).

    Requiere que la consulta tenga al menos un hallazgo determinístico (RF-IA-01).
    Devuelve afirmaciones (OBSERVACION, INTERPRETACION, RECOMENDACION) validadas.
    """
    try:
        response = await provider.generate_explanation(request)
        return response
    except ValueError as exc:
        raise HTTPException(
            status_code=status.HTTP_422_UNPROCESSABLE_ENTITY,
            detail=f"Error en validación de esquema o salida de IA: {str(exc)}",
        ) from exc
    except RuntimeError as exc:
        raise HTTPException(
            status_code=status.HTTP_502_BAD_GATEWAY,
            detail=f"Fallo en comunicación con el proveedor de IA: {str(exc)}",
        ) from exc
    except Exception as exc:
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail=f"Error interno no controlado: {str(exc)}",
        ) from exc


if __name__ == "__main__":
    import uvicorn

    config = get_settings()
    uvicorn.run("src.main:app", host=config.HOST, port=config.PORT, reload=False)
