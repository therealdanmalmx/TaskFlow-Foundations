# Multi-stage build: the image students deploy in the course.
#
#   Stage 1 (node)   — builds the React app to static files
#   Stage 2 (sdk)    — publishes the .NET API with those files in wwwroot
#   Stage 3 (aspnet) — small runtime-only image, runs as a non-root user
#
# The final image contains no compilers, no source code, and no node_modules —
# only the published app. That keeps it small and shrinks the attack surface.

# ---- Stage 1: build the React frontend -------------------------------------
FROM node:24-alpine AS web-build
WORKDIR /web

# Copy the manifests first so `npm ci` is cached until dependencies change.
COPY src/TaskFlow.Web/package.json src/TaskFlow.Web/package-lock.json ./
RUN npm ci

COPY src/TaskFlow.Web/ ./

# CI passes the commit SHA in as a build arg; the footer shows it, so you can
# always tell exactly which commit is live — artifact traceability in one line.
ARG GIT_SHA=dev
ENV VITE_GIT_SHA=$GIT_SHA
RUN npm run build \
    && printf '{"sha":"%s"}\n' "$GIT_SHA" > dist/version.json

# ---- Stage 2: publish the .NET API ------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api-build
WORKDIR /src

# Same trick: restore is cached until the .csproj changes.
COPY src/TaskFlow.Api/TaskFlow.Api.csproj ./
RUN dotnet restore

COPY src/TaskFlow.Api/ ./

# Drop the built SPA into wwwroot so `dotnet publish` bundles it into the API.
COPY --from=web-build /web/dist ./wwwroot/
RUN dotnet publish -c Release -o /app

# ---- Stage 3: runtime --------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
WORKDIR /app
COPY --from=api-build /app ./

# Run as the non-root `app` user built into the .NET images. If the container
# is ever compromised, the attacker isn't root.
USER app

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "TaskFlow.Api.dll"]
