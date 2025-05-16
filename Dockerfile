# Etapa 1: Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

# Copia arquivos e restaura dependências
COPY . ./
RUN dotnet publish -c Release -o out

# Etapa 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

# Define a variável de ambiente da porta
ENV PORT=5000

# Expõe a porta que a app vai escutar
EXPOSE 5000

# Copia o app publicado da etapa de build
COPY --from=build /app/out .

# Comando de inicialização
ENTRYPOINT ["dotnet", "OpenAIChatApi.dll"]
