# Placeholder de S1-02: Nginx sirve una página de prueba y hace de proxy hacia la API.
# En S1-15 se agrega una etapa de build con Node (Vite) que reemplaza index.html por la SPA.
FROM nginxinc/nginx-unprivileged:1.27-alpine
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY index.html /usr/share/nginx/html/index.html
EXPOSE 8080