# S1-02/S1-06: Nginx sirve las páginas de acceso y los assets (CSS, JS, fuentes) y hace de proxy hacia la API.
# Contexto de build: ./web (ver docker-compose.yml). Cuando llegue el contenedor php (S1-15), las páginas pasan a PHP
# y /assets sigue igual.
FROM nginxinc/nginx-unprivileged:1.31-alpine
COPY nginx/nginx.conf /etc/nginx/conf.d/default.conf
COPY nginx/index.html nginx/consultas.html nginx/login.html nginx/cambiar-contrasena.html /usr/share/nginx/html/
COPY assets/ /usr/share/nginx/html/assets/
EXPOSE 8080
