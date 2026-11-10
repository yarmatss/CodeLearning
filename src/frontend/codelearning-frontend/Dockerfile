# Dependencies, shared by the dev and build stages
FROM node:24-alpine AS deps
WORKDIR /app
COPY package*.json ./
RUN npm ci

# Development: "ng serve" with live reload; docker-compose.override.yml bind-mounts the sources
FROM deps AS dev
COPY . .
EXPOSE 4200
CMD ["npx", "ng", "serve", "--host", "0.0.0.0", "--port", "4200", "--proxy-config", "proxy.conf.docker.json", "--poll", "2000"]

# Production build of the SPA
FROM deps AS build
COPY . .
RUN npx ng build --configuration production

# Production: nginx serves the static files and proxies /api to the API (nginx.conf)
FROM nginx:1.29-alpine AS final
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/codelearning-frontend/browser /usr/share/nginx/html
EXPOSE 80
