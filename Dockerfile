FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src

COPY ["AI_WebApi/AI_WebApi.csproj", "AI_WebApi/"]
COPY ["AI_WebApi.Common/AI_WebApi.Common.csproj", "AI_WebApi.Common/"]
COPY ["AI_WebApi.Extensions/AI_WebApi.Extensions.csproj", "AI_WebApi.Extensions/"]
COPY ["AI_WebApi.IServices/AI_WebApi.IServices.csproj", "AI_WebApi.IServices/"]
COPY ["AI_WebApi.Model/AI_WebApi.Model.csproj", "AI_WebApi.Model/"]
COPY ["AI_WebApi.Repository/AI_WebApi.Repository.csproj", "AI_WebApi.Repository/"]
COPY ["AI_WebApi.Services/AI_WebApi.Services.csproj", "AI_WebApi.Services/"]

RUN dotnet restore "AI_WebApi/AI_WebApi.csproj"

COPY . .
WORKDIR /src/AI_WebApi
RUN dotnet publish "AI_WebApi.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
ENV TZ=Asia/Shanghai

EXPOSE 8080

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "AI_WebApi.dll"]
