# DockerInDockerLinux

This is a simple example of how to run Docker commands in parent, host, environment within a Docker container. Since the docker container do mount the host docker socket on the default linux socket, _FluentDocker_ will execute the commands as it would if it was running on the host.

The example is showing two steps necessary to achieve this.

1. Make sure that the docker client binary is installed and on the containers environment PATH (see _build.sh_).
2. Mount the docker socket properly when running the container (_see run.sh_)

This reflects [Issue #199](https://github.com/mariotoffia/FluentDocker/issues/199) for _Linux_ and [Issue #99](https://github.com/mariotoffia/FluentDocker/issues/99) for _Windows_.

When running the container it should _at least_ output one container (this _docker-in-docker_ container).