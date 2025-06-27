#!/bin/bash
## Will execute the built docker container
docker run --rm -v /var/run/docker.sock:/var/run/docker.sock docker-in-docker