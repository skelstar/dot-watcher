# The bot-dots service. Build with bot-dots/ as the context:
#   docker build -t dot-watcher-bot-dots bot-dots
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY BotDots.Service/ BotDots.Service/
RUN dotnet publish BotDots.Service/BotDots.Service.csproj -c Release -o /out

FROM mcr.microsoft.com/dotnet/runtime:9.0
WORKDIR /app
COPY --from=build /out .
ENTRYPOINT ["dotnet", "BotDots.Service.dll"]
