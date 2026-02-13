# FluentDocker

[![CI](https://github.com/mariotoffia/FluentDocker/actions/workflows/ci.yml/badge.svg)](https://github.com/mariotoffia/FluentDocker/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/FluentDocker.svg)](https://www.nuget.org/packages/FluentDocker)
[![Downloads](https://img.shields.io/nuget/dt/FluentDocker.svg)](https://www.nuget.org/packages/FluentDocker)
[![License](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](https://opensource.org/licenses/Apache-2.0)

This library enables `docker` and `docker compose` (Compose V2) interactions using a _Fluent API_. It is supported on Linux, Windows and Mac.

**Have a look at the [project site](https://github.com/mariotoffia/FluentDocker) for more information.**

**Sample Fluent API usage**
```cs
using var kernel = FluentDockerKernel.Create()
    .WithDockerCli("docker", d => d.AsDefault())
    .Build();

using var results = new Builder()
    .WithinDriver("docker", kernel)
    .UseContainer(c => c
        .UseImage("postgres:15-alpine")
        .ExposePort("5432")
        .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
        .WaitForPort("5432/tcp", 30000))
    .Build();

var container = results.Containers.First();
var config = container.GetConfiguration(true);
Assert.AreEqual(ServiceRunningState.Running, config.State.ToServiceState());
```
