#!/bin/bash
## Builds the docker image
dotnet build
docker build -t docker-in-docker -f Dockerfile .
