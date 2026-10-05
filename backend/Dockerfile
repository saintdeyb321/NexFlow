FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY backend.slnx ./
COPY NexFlow.Domain/NexFlow.Domain.csproj NexFlow.Domain/
COPY NexFlow.Application/NexFlow.Application.csproj NexFlow.Application/
COPY NexFlow.Infrastructure/NexFlow.Infrastructure.csproj NexFlow.Infrastructure/
COPY NexFlow.API/NexFlow.API.csproj NexFlow.API/
COPY NexFlow.Migrator/NexFlow.Migrator.csproj NexFlow.Migrator/
RUN dotnet restore backend.slnx

COPY NexFlow.Domain/ NexFlow.Domain/
COPY NexFlow.Application/ NexFlow.Application/
COPY NexFlow.Infrastructure/ NexFlow.Infrastructure/
COPY NexFlow.API/ NexFlow.API/
COPY NexFlow.Migrator/ NexFlow.Migrator/
RUN dotnet publish NexFlow.API/NexFlow.API.csproj --configuration Release --no-restore --output /out/api /p:UseAppHost=false
RUN dotnet publish NexFlow.Migrator/NexFlow.Migrator.csproj --configuration Release --no-restore --output /out/migrator /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
USER $APP_UID

# Optional one-shot image target; it never starts the API or its workers.
FROM runtime AS migrator
COPY --from=build /out/migrator ./
ENTRYPOINT ["dotnet", "NexFlow.Migrator.dll"]

FROM runtime AS final
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=build /out/api ./
ENTRYPOINT ["dotnet", "NexFlow.API.dll"]
