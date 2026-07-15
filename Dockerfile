# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# 依存の復元 (レイヤキャッシュのため csproj を先にコピー)
COPY src/IruTagUpdater/IruTagUpdater.csproj src/IruTagUpdater/
RUN dotnet restore src/IruTagUpdater/IruTagUpdater.csproj

# ソースをコピーして publish
COPY src/ src/
RUN dotnet publish src/IruTagUpdater/IruTagUpdater.csproj -c Release -o /app

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./

# config.json はイメージに含めず、実行時に IRU_CONFIG_BASE64 で渡す。
# IRU_API_TOKEN は Secret Manager から環境変数として注入する。
ENTRYPOINT ["dotnet", "IruTagUpdater.dll"]
