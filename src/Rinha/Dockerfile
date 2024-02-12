FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["Rinha.fsproj", "./"]
RUN dotnet restore "Rinha.fsproj"
COPY . .
WORKDIR "/src/"
RUN dotnet build "Rinha.fsproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Rinha.fsproj" -c Release -o /app/publish /p:UseAppHost=false /p:PublishReadyToRun=true

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "App.dll"]