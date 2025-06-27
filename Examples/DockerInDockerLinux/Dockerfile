FROM mcr.microsoft.com/dotnet/sdk:3.1

WORKDIR /App
COPY bin/Debug/netcoreapp3.1 .

RUN apt-get -qq update && apt-get -qq install wget
RUN wget --quiet https://download.docker.com/linux/static/stable/x86_64/docker-20.10.8.tgz && \
    tar -xzf docker-20.10.8.tgz

RUN cp docker/* /usr/bin/ && rm -rf docker

ENTRYPOINT ["dotnet", "DockerInDockerLinux.dll"]