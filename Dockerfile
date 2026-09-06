ARG RUNTIME_IMAGE=mcr.microsoft.com/dotnet/aspnet:6.0
FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
ARG NUGET_SOURCE=https://mirrors.huaweicloud.com/repository/nuget/v3/index.json

COPY ["AI_WebApi/AI_WebApi.csproj", "AI_WebApi/"]
COPY ["AI_WebApi.Common/AI_WebApi.Common.csproj", "AI_WebApi.Common/"]
COPY ["AI_WebApi.Extensions/AI_WebApi.Extensions.csproj", "AI_WebApi.Extensions/"]
COPY ["AI_WebApi.IServices/AI_WebApi.IServices.csproj", "AI_WebApi.IServices/"]
COPY ["AI_WebApi.Model/AI_WebApi.Model.csproj", "AI_WebApi.Model/"]
COPY ["AI_WebApi.Repository/AI_WebApi.Repository.csproj", "AI_WebApi.Repository/"]
COPY ["AI_WebApi.Services/AI_WebApi.Services.csproj", "AI_WebApi.Services/"]

RUN dotnet restore "AI_WebApi/AI_WebApi.csproj" --source "${NUGET_SOURCE}"

COPY . .
WORKDIR /src/AI_WebApi
RUN dotnet publish "AI_WebApi.csproj" -c Release --no-restore -o /app/publish /p:UseAppHost=false

FROM ${RUNTIME_IMAGE} AS runtime
WORKDIR /app
ARG DEBIAN_MIRROR=https://mirrors.ustc.edu.cn/debian
ARG DEBIAN_SECURITY_MIRROR=https://mirrors.ustc.edu.cn/debian-security
ARG INSTALL_RUNTIME_DEPENDENCIES=true

RUN if [ "${INSTALL_RUNTIME_DEPENDENCIES}" = "true" ]; then \
    sed -i \
        -e "s|http://deb.debian.org/debian-security|${DEBIAN_SECURITY_MIRROR}|g" \
        -e "s|http://security.debian.org/debian-security|${DEBIAN_SECURITY_MIRROR}|g" \
        -e "s|http://deb.debian.org/debian|${DEBIAN_MIRROR}|g" \
        /etc/apt/sources.list \
    && apt-get -o Acquire::Retries=2 -o Acquire::https::Timeout=30 update \
    && apt-get -o Acquire::Retries=2 -o Acquire::https::Timeout=30 install -y --no-install-recommends chromium fonts-noto-cjk \
    && rm -rf /var/lib/apt/lists/*; \
    else \
        test -x /usr/bin/chromium \
        && chromium --version \
        && dpkg-query -W chromium fonts-noto-cjk; \
    fi

ENV ASPNETCORE_URLS=http://+:8080
ENV TZ=Asia/Shanghai
ENV IReGoTools__ReportImage__BrowserExecutablePath=/usr/bin/chromium

EXPOSE 8080

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "AI_WebApi.dll"]
