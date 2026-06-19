# ── STAGE 1: BUILD ──────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
 
# Restore NuGet packages as a cached layer
COPY ["CareBridge.PatientAPI.csproj", "."]
RUN dotnet restore
 
# Copy remaining source and publish in Release mode
COPY . .
RUN dotnet publish -c Release -o /app/publish
 
# ── STAGE 2: RUNTIME ─────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
 
# Security: create and switch to non-root OS user
RUN addgroup --system appgroup && adduser --system --ingroup appgroup appuser
USER appuser
 
# Copy only compiled output from Stage 1
COPY --from=build /app/publish .
 
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "CareBridge.PatientAPI.dll"]

