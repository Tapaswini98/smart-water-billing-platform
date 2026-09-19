# syntax=docker/dockerfile:1.7

# ---------------------------------------------------------------------------
# Build stage
# package.json and the lockfile copy first so an unchanged dependency graph
# reuses the cached npm ci layer.
# ---------------------------------------------------------------------------
FROM node:24-alpine AS build
WORKDIR /app

COPY web/package.json web/package-lock.json ./
RUN --mount=type=cache,target=/root/.npm npm ci

# The OpenAPI document the client is generated from. Copied rather than fetched,
# so the image build cannot depend on a running API.
COPY openapi/ /openapi/
COPY web/ ./

# Regenerate rather than trust the committed client: if the two have drifted, the
# TypeScript build below fails here instead of shipping a client that lies about
# the contract.
RUN npm run generate:api && npm run build

# ---------------------------------------------------------------------------
# Runtime stage
# ---------------------------------------------------------------------------
FROM nginx:1.27-alpine AS runtime

COPY web/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html

# nginx:alpine ships an unprivileged `nginx` user; the directories it needs to
# write at runtime are handed over so the server does not run as root.
RUN touch /var/run/nginx.pid \
    && chown -R nginx:nginx /var/run/nginx.pid /var/cache/nginx /usr/share/nginx/html
USER nginx

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD wget --quiet --spider http://localhost:8080/ || exit 1

CMD ["nginx", "-g", "daemon off;"]
