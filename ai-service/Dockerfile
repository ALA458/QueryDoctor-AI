# Servicio de IA: FastAPI con adaptador de LLM integrado (S1-18, ADR-02, ADR-10)
FROM python:3.12-slim

WORKDIR /app

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1

# Usuario sin privilegios por seguridad (RNF-SEG-01 / CIS Docker)
RUN useradd --uid 10001 --no-create-home app

COPY requirements.txt .
RUN pip install --no-cache-dir --only-binary=:all: -r requirements.txt

COPY src/ ./src/

USER 10001

EXPOSE 8000

HEALTHCHECK --interval=10s --timeout=3s --retries=5 --start-period=5s \
  CMD python -c "import urllib.request; urllib.request.urlopen('http://localhost:8000/health')" || exit 1

CMD ["uvicorn", "src.main:app", "--host", "0.0.0.0", "--port", "8000"]
