# use "dotnet/sdk" to get the compiler and "ASPNETCORE_URLS" to compensate for the now missing env variable
FROM mcr.microsoft.com/dotnet/sdk:7.0
ENV ASPNETCORE_URLS=http://+:80
WORKDIR /app
COPY app .

ENTRYPOINT ["./Nexus"]