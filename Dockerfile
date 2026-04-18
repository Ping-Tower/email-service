FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/Domain/Domain.csproj", "src/Domain/"]
COPY ["src/Application/Application.csproj", "src/Application/"]
COPY ["src/Infrastructure/Infrastructure.csproj", "src/Infrastructure/"]
COPY ["src/EmailWorker/EmailWorker.csproj", "src/EmailWorker/"]

RUN dotnet restore "src/EmailWorker/EmailWorker.csproj"

COPY . .

WORKDIR /src/src/EmailWorker
RUN dotnet publish "EmailWorker.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "EmailWorker.dll"]

