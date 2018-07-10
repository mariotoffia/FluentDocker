# FluentDocker
FluentDocker is a library to interact with docker-machine, docker-compose and docker. It supports the docker for windows, docker for mac, docker machine, and native linux (however only limited tests on Linux and Mac). 
This library is available at nuget [Ductus.FluentDocker](https://www.nuget.org/packages/Ductus.FluentDocker/ "Nuget Home for Ductus.FluentDocker") 
and the ms test support is available at [Ductus.FluentDocker.MsTest](https://www.nuget.org/packages/Ductus.FluentDocker.MsTest/ "Nuget Home for Ductus.FluentDocker.MsTest").

**Sample Fluent API usage**
```cs
      using (
        var container =
          new Builder().UseContainer()
            .UseImage("kiasaki/alpine-postgres")
            .ExposePort(5432)
            .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
            .WaitForPort("5432/tcp", 30000 /*30s*/)
            .Build()
            .Start())
      {
        var config = container.GetConfiguration(true);
        Assert.AreEqual(ServiceRunningState.Running, config.State.ToServiceState());
      }
```

This library was originally written for .NET framework but has now been ported to .NET Core by smudge202. Many thanks for the contribution. It supports .net standard 1.6, 2.0 and .net 4.51.

### Current project status ###
| | Status |
| ---: | :-: |
|Current build|[![Build status](https://ci.appveyor.com/api/projects/status/kqqrkcv8wp3e9my6?svg=true)](https://ci.appveyor.com/project/mariotoffia/fluentdocker)|
|Current release Core|[![NuGet](https://img.shields.io/nuget/v/Ductus.FluentDocker.svg)](https://www.nuget.org/packages/Ductus.FluentDocker)|
|Current release Test|[![NuGet](https://img.shields.io/nuget/v/Ductus.FluentDocker.MsTest.svg)](https://www.nuget.org/packages/Ductus.FluentDocker.MsTest)|

The library is divided into three thin layers, each layer is accessable:

1. Docker Binaries interactions - Static commands and docker environment
2. Services - thin service layer to manage machines, containers etc.
3. Fluent API - API to build/discover services to be used

The Majority of the service methods are extension methods and not hardwired into the service itself, making them lightweigted and customizable. Since everthing is accessable it is e.g. easy to add extensions method for a service that uses the layer 1 commands to provide functionality. 

## Basic Usage of Commands (Layer 1)
All commands needs a ```DockerUri``` to work with. It is the Uri to the docker daemon, either locally or remote. It can be discoverable or hardcoded. Discovery of local ```DockerUri``` can be done by 
```cs
     var hosts = new Hosts().Discover();
     var _docker = hosts.FirstOrDefault(x => x.IsNative) ?? hosts.FirstOrDefault(x => x.Name == "default");
```
The example snipped will check for native, or docker beta "native" hosts, if not choose the docker-machine "default" as host. If you're using docker-machine and no machine exists or is not started it is easy to create / start a docker-machine by e.g. ```"test-machine".Create(1024,20000000,1)```. This will create a docker machine named "test-machine" with 1GB of RAM, 20GB Disk, and use one CPU.

It is now possible to use the Uri to communicate using the commands. For example to get the version of client and server docker binaries:
```cs
     var result = _docker.Host.Version(_docker.Certificates);
     Debug.WriteLine(result.Data); // Will Print the Client and Server Version and API Versions respectively.
```
All commands return a CommandResponse<T> such that it is possible to check successfactor by ```response.Success```. If any data associated with the command it is returned in the ```response.Data``` property.

Then it is simple as below to start and stop include delete a container using the commands. Below starts a container and do a PS on it and then deletes it.
```cs
     var id = _docker.Host.Run("nginx:latest", null, _docker.Certificates).Data;
     var ps = _docker.Host.Ps(null, _docker.Certificates).Data;
     
     _docker.Host.RemoveContainer(id, true, true, null, _docker.Certificates);
```
When running on windows, one can choose to run linux or windows container. Use the ```LinuxDaemon``` or ```WindowsDaemon``` to control which daemon to talk to.
```cs
     _docker.LinuxDaemon(); // ensures that it will talk to linux daemon, if windows daemon it will switch
```

Some commands returns a stream of data when e.g. events or logs is wanted using a continious stream. Streams can be used in background tasks and support ```CancellationToken```. Below example tails a log.
```cs
     using (var logs = _docker.Host.Logs(id, _docker.Certificates))
     {
          while (!logs.IsFinished)
          {
               var line = logs.TryRead(5000); // Do a read with timeout
               if (null == line)
               {
                    break;
               }

               Debug.WriteLine(line);
          }
     }
```

Utility methods exists for commands. They come in different flaviours such as networking etc. For example when reading a log to the end:
```cs
     using (var logs = _docker.Host.Logs(id, _docker.Certificates))
     {
          foreach (var line in logs.ReadToEnd())
          {
            Debug.WriteLine(line);
          }
     }
```

## Using Fluent API
The highest layer of this library is the fluent API where you can define and control machines, images, and containers. For example to setup a loadbalancer with two nodejs servers reading from a redis server can look like this (node image is custom built if not found in the repository).

```cs
     var fullPath = (TemplateString) @"${TEMP}\fluentdockertest\${RND}";
      var nginx = Path.Combine(fullPath, "nginx.conf");

      Directory.CreateDirectory(fullPath);
      typeof(NsResolver).ResourceExtract(fullPath, "index.js");

        using (var services = new Builder()

          // Define custom node image to be used
          .DefineImage("mariotoffia/nodetest").ReuseIfAlreadyExists()
          .From("ubuntu")
          .Maintainer("Mario Toffia <mario.toffia@gmail.com>")
          .Run("apt-get update &&",
            "apt-get -y install curl &&",
            "curl -sL https://deb.nodesource.com/setup | sudo bash - &&",
            "apt-get -y install python build-essential nodejs")
          .Run("npm install -g nodemon")
          .Add("emb:Ductus.FluentDockerTest/Ductus.FluentDockerTest.MultiContainerTestFiles/package.txt",
            "/tmp/package.json")
          .Run("cd /tmp && npm install")
          .Run("mkdir -p /src && cp -a /tmp/node_modules /src/")
          .UseWorkDir("/src")
          .Add("index.js", "/src")
          .ExposePorts(8080)
          .Command("nodemon", "/src/index.js").Builder()

          // Redis Db Backend
          .UseContainer().WithName("redis").UseImage("redis").Builder()

          // Node server 1 & 2
          .UseContainer().WithName("node1").UseImage("mariotoffia/nodetest").Link("redis").Builder()
          .UseContainer().WithName("node2").UseImage("mariotoffia/nodetest").Link("redis").Builder()

          // Nginx as load balancer
          .UseContainer().WithName("nginx").UseImage("nginx").Link("node1", "node2")
          .CopyOnStart(nginx, "/etc/nginx/nginx.conf")
          .ExposePort(80).Builder()
          .Build().Start())
        {
          Assert.AreEqual(4, services.Containers.Count);

          var ep = services.Containers.First(x => x.Name == "nginx").ToHostExposedEndpoint("80/tcp");
          Assert.IsNotNull(ep);

          var round1 = $"http://{ep.Address}:{ep.Port}".Wget();
          Assert.AreEqual("This page has been viewed 1 times!", round1);

          var round2 = $"http://{ep.Address}:{ep.Port}".Wget();
          Assert.AreEqual("This page has been viewed 2 times!", round2);
        }
```

The fluent API supports from defining a docker-machine to a set of docker instances. It has built-in support for e.g. waiting for a specific port or a process within the container before ```Build()``` completes and thus can be safely be used within a using statement. If specific management on wait timeouts etc. you can always build and start the container and use extension methods to do the waiting on the container itself.

To create a container just omit the start. For example:
```cs
using (
        var container =
          new Builder().UseContainer()
            .UseImage("kiasaki/alpine-postgres")
            .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
            .Build())
      {
        Assert.AreEqual(ServiceRunningState.Stopped, container.State);
      }
```
This example creates a container with postgres, configure one environment variable. Within the using statement it is possible to start the ```IContainerService```. Thus each built container is wrapped in a ```IContainerService```. It is also possible to use the ```IHostService.GetContainers(...)``` to obtain the created, running, and exited containers. From the ```IHostService``` it is also possible to get all the images in the local repository to create containers from.

Whe you want to run a single container do use the fluent or container service start method. For example:
```cs
      using (
        var container =
          new Builder().UseContainer()
            .UseImage("kiasaki/alpine-postgres")
            .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
            .Build()
            .Start())
      {
        var config = container.GetConfiguration();

        Assert.AreEqual(ServiceRunningState.Running, container.State);
        Assert.IsTrue(config.Config.Env.Any(x => x == "POSTGRES_PASSWORD=mysecretpassword"));
      }
```

By default the container is stopped and deleted when the Dispose method is run, in order to keep the container in archve, use the ```KeepContainer()``` on the fluent API. When ```Dispose()``` is invoked it will be stopped but not deleted. It is also possible to keep it running after dispose as well.

### Working with ports
It is possible to expose ports both explicit or randomly. Either way it is possible to resolve the IP (in case of machine) and the port (in case of random port) to use in code. For example:

```cs
      using (
        var container =
          new Builder().UseContainer()
            .UseImage("kiasaki/alpine-postgres")
            .ExposePort(40001, 5432)
            .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
            .Build()
            .Start())
      {
        var endpoint = container.ToHostExposedEndpoint("5432/tcp");
        Assert.AreEqual(40001, endpoint.Port);
      }
```

Here we map the container port 5432 to host port 40001 explicitly. Note the use of ```container.ToHostExposedEndpoint(...)```. This is to always resolve to a working ip and port to communicate with the docker container. It is also possible to map a random port, i.e. let Docker choose a available port. For example:

```cs
      using (
        var container =
          new Builder().UseContainer()
            .UseImage("kiasaki/alpine-postgres")
            .ExposePort(5432)
            .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
            .Build()
            .Start())
      {
        var endpoint = container.ToHostExposedEndpoint("5432/tcp");
        Assert.AreNotEqual(0, endpoint.Port);
      }
```
The only difference here is that only one argument is used when ```ExposePort(...)``` was used to configure the container. The same usage applies otherwise and thus is transparent for the code.

In order to know when a certain service is up and running before starting to e.g. connect to it. It is possible to wait for a specific port to be open. For example:
```cs
      using (
        var container =
          new Builder().UseContainer()
            .UseImage("kiasaki/alpine-postgres")
            .ExposePort(5432)
            .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
            .WaitForPort("5432/tcp", 30000 /*30s*/)
            .Build()
            .Start())
      {
        var config = container.GetConfiguration(true);
        Assert.AreEqual(ServiceRunningState.Running, config.State.ToServiceState());
      }
```
In the above example we wait for the container port 5432 to be opened within 30 seconds. If it fails, it will throw an exception and thus the container will be disposed and removed (since we dont have any keep container etc. configuration).

Sometime it is not sufficient to just wait for a port. Sometimes a container process is much more vital to wait for. Therefore a wait for process method exist in the fluent API as well as an extension method on the container object. For example: 
```cs
      using (
        var container =
          new Builder().UseContainer()
            .UseImage("kiasaki/alpine-postgres")
            .ExposePort(5432)
            .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
            .WaitForProcess("postgres", 30000 /*30s*/)
            .Build()
            .Start())
      {
        var config = container.GetConfiguration(true);
        Assert.AreEqual(ServiceRunningState.Running, config.State.ToServiceState());
      }
```
In the above example ```Build()``` will return control when the process "postgres" have been started within the container.

### Filesystem & Files
In order to make use of containers, sometimes it is neccesary to mount volumes in the container onto the host or just copy from or to the container. Depending on if you're running machine or docker natively volume mapping have the constraint that it must be reachable from the virtual machine.

A normal usecase is to have e.g. a webserver serving content on a docker container and the user edits files on the host file system. In such scenario it is necessary to mount a docker container volume onto the host. For example: 

```cs
     const string html = "<html><head>Hello World</head><body><h1>Hello world</h1></body></html>";
      var hostPath = (TemplateString) @"${TEMP}\fluentdockertest\${RND}";
      Directory.CreateDirectory(hostPath);

      using (
        var container =
          new Builder().UseContainer()
            .UseImage("nginx:latest")
            .ExposePort(80)
            .Mount(hostPath, "/usr/share/nginx/html", MountType.ReadOnly)
            .Build()
            .Start()
            .WaitForPort("80/tcp", 30000 /*30s*/))
      {
          File.WriteAllText(Path.Combine(hostPath, "hello.html"), html);

          var response = $"http://{container.ToHostExposedEndpoint("80/tcp")}/hello.html".Wget();
          Assert.AreEqual(html, response);
      }
```
In the above example a nginx container is started and mounts '/usr/share/nginx/html' onto a (random, in temp directory) host path. A HTML file is copied into the host path and when a HTTP get towards the nginx docker container is done, that same file is served.

Sometimes it is neccesary copy files to and from a container. For example copy a configuration file, configure it and copy it back. More common scenario is to copy a configuration file to the container, just before it is started. The multi container example copies a nginx configuration file just before it is started. Thus it is possible to avoid manually creating a Dockerfile and a image for such a simple task. Instead just use e.g. an official or custom image, copy configuration and run.
```cs
 using (new Builder().UseContainer()
          .UseImage("kiasaki/alpine-postgres")
          .ExposePort(5432)
          .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
          .Build()
          .Start()
          .CopyFrom("/etc/conf.d", fullPath))
        {
          var files = Directory.EnumerateFiles(Path.Combine(fullPath, "conf.d")).ToArray();
          Assert.IsTrue(files.Any(x => x.EndsWith("pg-restore")));
          Assert.IsTrue(files.Any(x => x.EndsWith("postgresql")));
        }
```
Above example copies a directory to a host path (fullPath) from a running container. Note the use of extension method here, thus not using the fluent API (since CopyFrom is after Start()). If you want to copy files from the container just before starting use the Fluent API instead.
```cs
       using (new Builder().UseContainer()
          .UseImage("kiasaki/alpine-postgres")
          .ExposePort(5432)
          .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
          .CopyOnDispose("/etc/conf.d", fullPath)
          .Build()
          .Start())
        {
        }
```

The below example illustrates a much more common scenario where files are copied to the container. This example makes use of the extension method instead of the fluent API version. It takes a Diff snapshot before copy and then just after the copy. In the latter
the hello.html is present.
```cs
       using (
          var container =
            new Builder().UseContainer()
              .UseImage("kiasaki/alpine-postgres")
              .ExposePort(5432)
              .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
              .Build()
              .Start()
              .WaitForProcess("postgres", 30000 /*30s*/)
              .Diff(out before)
              .CopyTo("/bin", fullPath))
        {
          var after = container.Diff();

          Assert.IsFalse(before.Any(x => x.Item == "/bin/hello.html"));
          Assert.IsTrue(after.Any(x => x.Item == "/bin/hello.html"));
        }
```

Sometime is it useful to copy files in the ```IContainerService.Dispose()``` (just before container has stopped). Therefore a fluent API exitst to ensure that it will just do that.
```cs
using (new Builder().UseContainer()
          .UseImage("kiasaki/alpine-postgres")
          .ExposePort(5432)
          .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
          .CopyOnDispose("/etc/conf.d", fullPath)
          .Build()
          .Start())
        {
        }
```
In order to analyze a container, export extension method and fluent API methods exists. Most notably is the possibility to export a container when a ```IContainerService``` is disposed.
```cs
        using (new Builder().UseContainer()
          .UseImage("kiasaki/alpine-postgres")
          .ExposePort(5432)
          .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
          .ExportOnDispose(fullPath)
          .Build()
          .Start())
        {
        }
```
This will produce a container export (tar file) on the host (fullPath). If you rather have it exploaded (un-tared) use the ```ExportExploadedOnDispose``` method instead. Of course you can export the container any time using a extension method on the container.

A useful trick when it comes to unit-testing is to export the container state when the unit test fails for some reason, therefore it exists a Fluent API that will export when a certain Lambda condition is met. For example:
```cs
      var failure = false;
        using (new Builder().UseContainer()
          .UseImage("kiasaki/alpine-postgres")
          .ExposePort(5432)
          .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
          .ExportOnDispose(fullPath, svc => failure)
          .Build()
          .Start())
        {
          failure = true;
        }
```
This snippet will export the container when the using statement is disposing the container since the failure variable is set to true and is used in the ```ExportOnDispose``` expression.

#### IService Hooks
All services can be extended with hooks. In the ```ExportOnDispose(path, lambda)``` installs a Hook when the service state is set to execute the lambda when the state is ```Removing```. It is possible to install and remove hooks on the fly. If multiple hooks are registered on same service instance, with same ```ServiceRunningState```, they will be executed in installation order. 

The hooks are particulary good if you want something to be executed when a state is about to be set (or executed) on the service such as ```Starting```. The Fluent API makes use of those in some situations such as Copy files, export, etc.

## Docker Networking
FluentDocker do support all docker network commands. It can discover networks by ```_docker.NetworkLs()``` where it discovers all networks and some simple parameters defined in ```NetworkRow```. It can also inspect to gain deeper information about the network (such as which containers is in the network and Ipam configuration) by ```_docker.NetworkInspect(network:"networkId")```.

In order to create a new network, use the ```_docker.NetworkCreate("name_of_network")```. It is also possible to supply ```NetworkCreateParams``` where everything can be customized such as creating a _overlay_ network och change the Ipam configuration. To delete a network, just use the ```_docker.NetworkRm(network:"networkId")```.

*Note that networks are not deleted if there are any containers attached to it!*

When a network is created it is possible to put one or more containers into it using the ```_docker.NetworkConnect("containerId","networkId")```. Note that containers may be in several networks at a time, thus can proxy request between isolated networks. To disconnect a container from a network, simple do a ```_docker.NetworkDisconnect("containerId","networkId")```.

The following sample runs a container, creates a new network, and connects the running container into the network. It then disconnect the container, delete it, and delete the network.
```cs
    var cmd = _docker.Run("postgres:9.6-alpine", new ContainerCreateParams
        {
          PortMappings = new[] {"40001:5432"},
          Environment = new[] {"POSTGRES_PASSWORD=mysecretpassword"}
        }, _certificates);

    var container = cmd.Data;
    var network = string.Empty;

    var created = _docker.NetworkCreate("test-network");
    if (created.Success)
      network = created.Data[0];

    _docker.NetworkConnect(container, network);

    // Container is now running and has address in the newly created 'test-network'

    _docker.NetworkDisconnect(container, id, true /*force*/);
    _docker.RemoveContainer(container, true, true);

    // Now it is possible to delete the network since it has been disconnected from the network
    _docker.NetworkRm(network: network);
```
### Fluent Networking
It is also possible to use a fluent builder to build new or reuse existing docker networks. Those can then be referenced while building _containers_. It is possible to build more than one docker network and attach a container to more than one network at a time.
```cs
    using(var nw = new Builder().UseNetwork("test-network")) 
    {
      using (
        var container =
          new DockerBuilder()
            .WithImage("kiasaki/alpine-postgres")
            .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
            .ExposePorts("5432")
            .UseNetwork(nw)
            .WaitForPort("5432/tcp", 30000 /*30s*/)
            .Build())
      {
        container.Create().Start();
      }
    }
```
The above code snippet creates a new network called _test-network_ and then creates a container that is attached to the _test-network_. When the ```Dispose()``` is called on _nw_ it will remove the network.

## Volume Support
FluentDocker supports docker volume management both from commands and from a fluent API. Therefore it is possible to have total control on volumes used in container such if it shall be disposed, reused, what driver to use etc.

```cs
  var volume = _docker.VolumeCreate("test-volume", "local", opts: {
                                      {"type","nfs"},
                                      {"o=addr","192.168.1.1,rw"},
                                      {"device",":/path/to/dir"}
                                    });

  var cfg = _docker.VolumeInspect(_certificates, "test-volume");
  _docker.VolumeRm(force: true, id: "test-volume");
```
The above snippet creates a new volume with name _test-volume_ and is of _NFS_ type. It then inspects the just created volume and lastly force delete the volume.

### Fluent Volume API
It is also possible to use a fluent API to create or use volumes. They can then be used when building a container. This is especially usefull when creation of volumes are special or lifetime needs to be controlled.
```cs
      using (var vol = new Builder().UseVolume("test-volume").RemoveOnDispose().Build())
      {
        using (
          var container =
            new Builder().UseContainer()
              .UseImage("postgres:9.6-alpine")
              .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
              .MountVolume(vol, "/var/lib/postgresql/data", MountType.ReadWrite)
              .Build()
              .Start())
        {
          var config = container.GetConfiguration();

          Assert.AreEqual(1, config.Mounts.Length);
          Assert.AreEqual("test-volume", config.Mounts[0].Name);
        }
      }
```
The above sample creates a new volume called _test-volume_ and it is scheduled to be delete when ```Dispose()``` is invoked on the ```IVolumeService```. The container is created and mounts the newly created volume to _/var/lib/postgresql/data_ as _read/write_ access mode.
Since the container is within the scope of the ```using``` statement of the volume it's lifetime spans the whole container lifetime and then get's deleted.

### Logging
In the full framework it uses verbose logging using the ```System.Diagnostics.Debugger.Log```. For .net core it uses the standard
```Microsoft.Extensions.Logging.ILog``` to log. Both are using the category _Ductus.FluentDocker_ and therefore may be configured
to participate in logging or not and configure different logging destinations.

In .net core you can provide the logging segment in the application config file.
```
{
  "Logging": {
    "IncludeScopes": false,
    "LogLevel": {
      "Ductus.FluentDocker": "None"
      }
   }
}
```
Please check the https://docs.microsoft.com/en-us/aspnet/core/fundamentals/logging/?view=aspnetcore-2.1 for more information.
For full framework please check out the _XML_ needed in the appconfig for the full framework described in https://docs.microsoft.com/en-us/dotnet/framework/wcf/diagnostics/tracing/configuring-tracing.

## Test Support
This repo contains two nuget packages, one for the fluent access and the other is a ms-test base classes to be used while testing. For example in a unit-test it is possible to fire up a postgres container and wait when the the db has booted.
```cs
     using (
        var container =
          new DockerBuilder()
            .WithImage("kiasaki/alpine-postgres")
            .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
            .ExposePorts("5432")
            .WaitForPort("5432/tcp", 30000 /*30s*/)
            .Build())
      {
        container.Create().Start();
      }
```
It is also possible to re-use abstract base classes, for example postgres test base to simplify and make clean unittest towards a container.
```cs
     [TestClass]
     public class PostgresMsTests : PostgresTestBase
     {
          [TestMethod]
          public void Test()
          {
               // We now have a running postgres
               // and a valid connection string to use.
          }
     }
```  
The `FluentDockerTestBase` allows for simple overrides to do whatever custom docker backed test easily. Just create a test class and derive from the `FluentDockerTestBase` and override suitable methods. For example.
```cs
     protected override DockerBuilder Build()
     {
          return new DockerBuilder()
               .WithImage("kiasaki/alpine-postgres")
               .WithEnvironment($"POSTGRES_PASSWORD={PostgresPassword}")
               .ExposePorts("5432")
               .WaitForPort("5432/tcp", 30000 /*30s*/);
     }
```     
This will create a builder with docker image postgres:latest and set one environment string, it will also expose the postgres db port 5432 to the host so one can connect to the db within the container. Lastly it will wait for the port 5432. This ensures that the db is running and have properly booted. If timeout, in this example set to 30 seconds, it will throw an exception and the container is stopped and removed. Note that the host port is not 5432! Use `Container.GetHostPort("5432/tcp")` to get the host port. The host ip can be retrieved by `Container.Host` property and thus shall be used when communicating with the app in the container. 

If a callback is needed when the container has been successfully pulled, created, and started.
```cs
     protected override void OnContainerInitialized()
     {
          ConnectionString = string.Format(PostgresConnectionString, Container.Host,
               Container.GetHostPort("5432/tcp"), PostgresUser,
               PostgresPassword, PostgresDb);
     }
```     
This example renders a proper connection string to the postgresql db within the newly spun up container. This can be used to connect using Npgsql, EF7, NHibernate, Marten or other compatible tools. This method will not be called if pulling of the image from the docker repository or it could not create/start the container.

If a before shutdown container hook is wanted override.
```cs
     protected virtual void OnContainerTearDown()
     {
          // Do stuff before container is shut down.
     }
```
Note that if unamed container, if not properly disposed, the docker container will still run and must be manually removed. This is a feature not a bug since you might want several containers running in your test. The `DockerContainer` class manages the instance id of the container and thus only intract with it and no other container.

When creating / starting a new container it will first check the local repository if the container image is already present and will download it if not found. This may take some time and there's just a Debug Log if enabled it is possible to monitor the download process.
