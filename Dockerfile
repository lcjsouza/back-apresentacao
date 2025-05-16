# Etapa 1: Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

# Copia tudo (incluindo subpastas)
COPY . ./

# Publica o projeto principal
RUN dotnet publish OpenAIChatApi/OpenAIChatApi.csproj -c Release -o out

# Etapa 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

ENV PORT=5000
EXPOSE 5000

# Copia o app publicado
COPY --from=build /app/out ./

# Copia a pasta Personas da subpasta OpenAIChatApi
COPY OpenAIChatApi/Personas ./Personas

# Start
ENTRYPOINT ["dotnet", "OpenAIChatApi.dll"]
