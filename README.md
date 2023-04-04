[![Quality Gate Status](https://sonarcloud.io/api/project_badges/measure?project=mariotoffia_FluentDocker&metric=alert_status)](https://sonarcloud.io/dashboard?id=mariotoffia_FluentDocker)
[![Build status](https://ci.appveyor.com/api/projects/status/kqqrkcv8wp3e9my6/branch/master?svg=true)](https://ci.appveyor.com/project/mariotoffia/fluentdocker) 

| Package        | NuGet          |
| ---------------|:--------------:|
| FluentDocker   |[![NuGet](https://img.shields.io/nuget/v/Ductus.FluentDocker.svg)](https://www.nuget.org/packages/Ductus.FluentDocker) |
| Microsoft Test | [![NuGet](https://img.shields.io/nuget/v/Ductus.FluentDocker.MsTest.svg)](https://www.nuget.org/packages/Ductus.FluentDocker.MsTest) |
| XUnit Test     | [![NuGet](https://img.shields.io/nuget/v/Ductus.FluentDocker.XUnit.svg)](https://www.nuget.org/packages/Ductus.FluentDocker.XUnit) |

# FluentDocker

This library enables `docker` and `docker-compose` interactions usinga _Fluent API_. It is supported on Linux, Windows and Mac. It also has support for the legazy `docker-machine` interactions.

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
This fires up a postgres and waits for it to be ready. To use compose, just do it like this:

:bulb: **NOTE: Use the AssumeComposeVersion(ComposeVersion.V2) to use the V2 behaviour, default is still V1 (to be changed to default to V2 later this year)**

```cs
      var file = Path.Combine(Directory.GetCurrentDirectory(),
        (TemplateString) "Resources/ComposeTests/WordPress/docker-compose.yml");

      // @formatter:off
      using (var svc = new Builder()
                        .UseContainer()
                        .UseCompose()
                        .FromFile(file)
                        .RemoveOrphans()
                        .WaitForHttp("wordpress", "http://localhost:8000/wp-admin/install.php") 
                        .Build().Start())
        // @formatter:on
      {
        // We now have a running WordPress with a MySql database        
        var installPage = await "http://localhost:8000/wp-admin/install.php".Wget();

        Assert.IsTrue(installPage.IndexOf("https://wordpress.org/", StringComparison.Ordinal) != -1);
        Assert.AreEqual(1, svc.Hosts.Count); // The host used by compose
        Assert.AreEqual(2, svc.Containers.Count); // We can access each individual container
        Assert.AreEqual(2, svc.Images.Count); // And the images used.
      }
```

:bulb **Note for Linux Users:** Docker requires _sudo_ by default and the library by default expects that executing user do not
need to do _sudo_ in order to talk to the docker daemon. More description can be found in the _Talking to Docker Daemon_ chapter.

The fluent _API_ builds up one or more services. Each service may be composite or singular. Therefore it is possible
to e.g. fire up several _docker-compose_ based services and manage each of them as a single service or dig in and use
all underlying services on each _docker-compose_ service. It is also possible to use services directly e.g.
```cs
      var file = Path.Combine(Directory.GetCurrentDirectory(),
        (TemplateString) "Resources/ComposeTests/WordPress/docker-compose.yml");

      using (var svc = new DockerComposeCompositeService(DockerHost, new DockerComposeConfig
      {
        ComposeFilePath = new List<string> { file }, ForceRecreate = true, RemoveOrphans = true,
        StopOnDispose = true
      }))
      {
        svc.Start();
        
        // We now have a running WordPress with a MySql database
        var installPage = await $"http://localhost:8000/wp-admin/install.php".Wget();
        
        Assert.IsTrue(installPage.IndexOf("https://wordpress.org/", StringComparison.Ordinal) != -1);
      }
```
The above example creates a _docker-compose_ service from a single compose file. When the service is disposed all
underlying services is automatically stopped.

The library is supported by .NET full 4.51 framework and higher, .NET standard 1.6, 2.0. It is divided into 
three thin layers, each layer is accessible:

1. Docker Binaries interactions - Static commands and docker environment
2. Services - thin service layer to manage machines, containers etc.
3. Fluent API - API to build/discover services to be used

The Majority of the service methods are extension methods and not hardwired into the service itself, making them lightweight and customizable. Since everything is accessible it is e.g. easy to add extensions method for a service that uses the layer 1 commands to provide functionality. 

## Contribution
I do welcome contribution, though there is no contribution guideline as of yet, make sure to adhere to _.editorconfig_ when doing the Pull Requests.
Otherwise the build will fail. I'll update with a **real** guideline sooner or later this year.

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
All commands return a CommandResponse<T> such that it is possible to check success factor by ```response.Success```. If any data associated with the command it is returned in the ```response.Data``` property.

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

Some commands returns a stream of data when e.g. events or logs is wanted using a continuous stream. Streams can be used in background tasks and support ```CancellationToken```. Below example tails a log.
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
The highest layer of this library is the fluent API where you can define and control machines, images, and containers. For example to setup a load balancer with two nodejs servers reading from a redis server can look like this (node image is custom built if not found in the repository).

```cs
     var fullPath = (TemplateString) @"${TEMP}/fluentdockertest/${RND}";
      var nginx = Path.Combine(fullPath, "nginx.conf");

      Directory.CreateDirectory(fullPath);
      typeof(NsResolver).ResourceExtract(fullPath, "index.js");

        using (var services = new Builder()

          // Define custom node image to be used
          .DefineImage("mariotoffia/nodetest").ReuseIfAlreadyExists()
          .From("ubuntu")
          .Maintainer("Mario Toffia <mario.toffia@xyz.com>")
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

The above example defines a _Dockerfile_, builds it, for the node image. It then uses vanilla redis and nginx. 
If you just want to use an existing _Dockerfile_ it can be done like this.

```cs
        using (var services = new Builder()

          .DefineImage("mariotoffia/nodetest").ReuseIfAlreadyExists()
          .FromFile("/tmp/Dockerfile")
          .Build().Start())
        {
         // Container either build to reused if found in registry and started here.
        }
```
  
The fluent API supports from defining a docker-machine to a set of docker instances. It has built-in support for e.g. 
waiting for a specific port or a process within the container before ```Build()``` completes and thus can be safely 
be used within a using statement. If specific management on wait timeouts etc. you can always build and start the 
container and use extension methods to do the waiting on the container itself.

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


```cs
      using (
        var container =
          new Builder().UseContainer()
            .UseImage("kiasaki/alpine-postgres")
            .ExposePort(5432)
            .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
            .WaitForPort("5432/tcp", 30000 /*30s*/, "127.0.0.1")
            .Build()
            .Start())
      {
        var config = container.GetConfiguration(true);
        Assert.AreEqual(ServiceRunningState.Running, config.State.ToServiceState());
      }
```
Sometimes it is not possible to directly reach the container, by local ip and port, instead e.g. the container has an exposed port on the loopback interface (_127.0.0.1_) and that is the only way of reaching the container from the program. The above example forces the
address to be _127.0.0.1_ but still resolves the host port. By default, _FluentDocker_ uses the network inspect on the container to determine the network configuration.

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
In order to make use of containers, sometimes it is necessary to mount volumes in the container onto the host or just copy from or to the container. Depending on if you're running machine or docker natively volume mapping have the constraint that it must be reachable from the virtual machine.

A normal use case is to have e.g. a webserver serving content on a docker container and the user edits files on the host file system. In such scenario it is necessary to mount a docker container volume onto the host. For example: 

```cs
     const string html = "<html><head>Hello World</head><body><h1>Hello world</h1></body></html>";
      var hostPath = (TemplateString) @"${TEMP}/fluentdockertest/${RND}";
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

Sometimes it is necessary copy files to and from a container. For example copy a configuration file, configure it and copy it back. More common scenario is to copy a configuration file to the container, just before it is started. The multi container example copies a nginx configuration file just before it is started. Thus it is possible to avoid manually creating a Dockerfile and a image for such a simple task. Instead just use e.g. an official or custom image, copy configuration and run.
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
          .CopyOnStart("/etc/conf.d", fullPath)
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

Sometime is it useful to copy files in the ```IContainerService.Dispose()``` (just before container has stopped). Therefore a fluent API exists to ensure that it will just do that.
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
This will produce a container export (tar file) on the host (fullPath). If you rather have it exploded (un-tared) use the ```ExportExplodedOnDispose``` method instead. Of course you can export the container any time using a extension method on the container.

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
It is also possible to do static _IP_ container assignments within the network by `UseIpV4` or `UseIpV6`. For example:
```cs
using (var nw = Fd.UseNetwork("unit-test-nw")
                .UseSubnet("10.18.0.0/16").Build())
{
	using (
		var container =
		Fd.UseContainer()
			.UseImage("postgres:9.6-alpine")
			.WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
			.ExposePort(5432)
			.UseNetwork(nw)
			.UseIpV4("10.18.0.22")              
			.WaitForPort("5432/tcp", 30000 /*30s*/)
			.Build()
			.Start())
	{
		var ip = container.GetConfiguration().NetworkSettings.Networks["unit-test-nw"].IPAddress;
		Assert.AreEqual("10.18.0.22", ip);
	}
}
```
The above example creates a new network _unit-test-nw_ with ip-range _10.18.0.0/16_. It is the used in the new container. The IP for the container is set to _10.18.0.22_ and is static due to `UseIpV4` command.
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
It is also possible to use a fluent API to create or use volumes. They can then be used when building a container. This is especially useful when creation of volumes are special or lifetime needs to be controlled.
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
Since the container is within the scope of the ```using``` statement of the volume it's lifetime spans the whole container lifetime and then gets deleted.

### Events

_FluentDocker_ supports, connecting to the docker event mechanism to listen to the events it sends.

```cs
using (var events = Fd.Native().Events())
{
  using (
      var container =
          new Builder().UseContainer()
              .UseImage("postgres:9.6-alpine")
              .ExposePort(5432)
              .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
              .WaitForPort("5432/tcp", 30000 /*30s*/)
              .Build()
              .Start())
  {
    FdEvent e;
    while ((e= events.TryRead(3000)) != null)
    {
      if (e.Type == EventType.Container && e.Action == EventAction.Start)
        break;
    }
  }
}
```

Event listener is global and may handle many `EventAction` types. 

For example

* Pull
* Create
* Start
* Kill
* Die
* Connect
* Disconnect
* Stop
* Destroy

Depending on action, the event type may differ such as `ContainerKillEvent` for `EventAction.Kill`. All events derive from `FdEvent`. That means all shared properties is in the base event and the explicit ones are in the derived.

For example, the ´ContainerKillEvent` contains the following properties:

```cs
public sealed class ContainerKillActor : EventActor
{
  /// <summary>
  /// The image name and label such as "alpine:latest".
  /// </summary>
  public string Image { get; set; }
  /// <summary>
  /// Name of the container.
  /// </summary>
  public string Name { get; set; }
  /// <summary>
  /// The signal that the container has been signalled.
  /// </summary>
  public string Signal { get; set; }
}
```

This event loop may be used to pick up events and drive your instantiated `IService` instances. Or if you need to react to e.g. a network is added or deleted.

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

There's a quick way of disabling / enabling logging via (```Ductus.FluentDocker.Services```) ```Logging.Enabled()``` or ```Logging.Disabled()```. This will forcefully enable / disable logging.

### Custom IPEndpoint Resolvers

It is possible to override the default mechanism of _FluentDocker_ resolves the container IP from the clients perspective in e.g. `WaitForPort`. This can be overridden on `ContainerBuilder` basis.

The below sample, overrides the _default_ behaviour. When it returns `null` the _default_ resolver kicks in.
  
```cs
using (
  var container =
    Fd.UseContainer()
      .UseImage("postgres:9.6-alpine")
      .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
      .ExposePort(5432)
      .UseCustomResolver((
        ports, portAndProto, dockerUri) =>
      {
        if (null == ports || string.IsNullOrEmpty(portAndProto))
          return null;

        if (!ports.TryGetValue(portAndProto, out var endpoints))
          return null;

        if (null == endpoints || endpoints.Length == 0)
          return null;

        if (CommandExtensions.IsNative())
          return endpoints[0];

        if (CommandExtensions.IsEmulatedNative())
          return CommandExtensions.IsDockerDnsAvailable()
            ? new IPEndPoint(CommandExtensions.EmulatedNativeAddress(), endpoints[0].Port)
            : new IPEndPoint(IPAddress.Loopback, endpoints[0].Port);

        if (Equals(endpoints[0].Address, IPAddress.Any) && null != dockerUri)
          return new IPEndPoint(IPAddress.Parse(dockerUri.Host), endpoints[0].Port);

        return endpoints[0];
      })
      .WaitForPort("5432/tcp", 30000 /*30s*/)
      .Build()
      .Start())
{
  var state = container.GetConfiguration(true/*force*/).State.ToServiceState();
  Assert.AreEqual(ServiceRunningState.Running, state);
}
```

### Talking to custom docker daemon URI without docker-machine

There's limited support to use the _FluentAPI_ to talk to a remote docker daemon without using docker-machine. This is done either by manually creating a instance of a `DockerHostService` or use `FromUri` on `HostBuilder`.

```cs
  using(var container = Fd.UseHost().
                          FromUri(Settings.DockerUri, isWindowsHost: true).
                          UseContainer().
                          Build()) 
  {
  }
```

The above sample connects to a custom `DockerUri` from a setting and is a windows container docker daemon.


* `FromUri` that uses a `DockerUri` to create a `IHostService`. This _uri_ is arbitrary. It also support other properties (_see below_).

```cs
   public HostBuilder FromUri(
      DockerUri uri,
      string name = null,
      bool isNative = true,
      bool stopWhenDisposed = false,
      bool isWindowsHost = false,
      string certificatePath = null) {/*...*/}
```

It will use _"sensible"_ defaults on all parameters. Most of the case the _uri_ is sufficient. For example if not providing the _certificatePath_ it will try to get it from the environment _DOCKER_CERT_PATH_. If not found in the environment, it will default to none.

* `UseHost` that takes a instantiated `IHostService` implementation.

## Docker Compose Support
The library support _docker-compose_ to use existing compose files to render services and manage lifetime of such.

The following sample will do have compose file that fires up a _MySql_ and a _WordPress_. Therefore the single compose
service will have two _container_ services below it. By default, it will stop the services and clean up when 
```Dispose()``` is invoked. This can be overridden by ```KeepContainers()``` in the _fluent_ configuration.

```yml
version: '3.3'

services:
  db:
    image: mysql:5.7
    volumes:
    - db_data:/var/lib/mysql
    restart: always
    environment:
      MYSQL_ROOT_PASSWORD: somewordpress
      MYSQL_DATABASE: wordpress
      MYSQL_USER: wordpress
      MYSQL_PASSWORD: wordpress

  wordpress:
    depends_on:
    - db
    image: wordpress:latest
    ports:
    - "8000:80"
    restart: always
    environment:
      WORDPRESS_DB_HOST: db:3306
      WORDPRESS_DB_USER: wordpress
      WORDPRESS_DB_PASSWORD: wordpress
volumes:
  db_data:
``` 
The above file is the _docker-compose_ file to stitch up the complete service.

```cs
      var file = Path.Combine(Directory.GetCurrentDirectory(),
        (TemplateString) "Resources/ComposeTests/WordPress/docker-compose.yml");

      using (var svc = new Builder()
                        .UseContainer()
                        .UseCompose()
                        .FromFile(file)
                        .RemoveOrphans()
                        .Build().Start())
      {
        var installPage = await "http://localhost:8000/wp-admin/install.php".Wget();

        Assert.IsTrue(installPage.IndexOf("https://wordpress.org/", StringComparison.Ordinal) != -1);
        Assert.AreEqual(1, svc.Hosts.Count);
        Assert.AreEqual(2, svc.Containers.Count);
        Assert.AreEqual(2, svc.Images.Count);
        Assert.AreEqual(5, svc.Services.Count);
      }
``` 
 The above snippet is fluently configuring the _docker-compose_ service and invokes the install page to verify that
 WordPress is indeed working.
 
 It is also possible to do all the operations that a single container supports such as copy on, export, wait operations. For example:
```cs
      var file = Path.Combine(Directory.GetCurrentDirectory(),
        (TemplateString) "Resources/ComposeTests/WordPress/docker-compose.yml");

      // @formatter:off
      using (new Builder()
                .UseContainer()
                .UseCompose()
                .FromFile(file)
                .RemoveOrphans()
                .WaitForHttp("wordpress",  "http://localhost:8000/wp-admin/install.php", continuation: (resp, cnt) =>  
                             resp.Body.IndexOf("https://wordpress.org/", StringComparison.Ordinal) != -1 ? 0 : 500)
                .Build().Start())
        // @formatter:on
      {
        // Since we have waited - this shall now always work.       
        var installPage = await "http://localhost:8000/wp-admin/install.php".Wget();
        Assert.IsTrue(installPage.IndexOf("https://wordpress.org/", StringComparison.Ordinal) != -1);
      }
```
The above snippet fires up the wordpress docker compose project and checks the _URL_ http://localhost:8000/wp-admin/install.php it it returns a certain value in the body 
(in this case "https://wordpress.org/"). If not it returns _500_ and the ```WaitForHttp``` function will wait 500 milliseconds before invoking again. This works for any custom
lambda as well, just use ```WaitFor``` instead. Thus it is possible to e.g. query a database before continuing inside the using scope.

## Talking to Docker Daemon
For Linux and Mac users there are several options how to authenticate towards the socket. _FluentDocker_ supports no _sudo_, _sudo_ without any password (user added as NOPASSWD in /etc/sudoer), or
_sudo_ with password. The default is that FluentDocker expects to be able to talk without any _sudo_. The options ar global but can be changed in runtime.

```cs
     SudoMechanism.None.SetSudo(); // This is the default
     SudoMechanism.Password.SetSudo("<my-sudo-password>");
     SudoMechanism.NoPassword.SetSudo();
```

If you wish to turn off _sudo_ to communicate with the docker daemon, you can follow a docker [tutorial](https://docs.docker.com/install/linux/docker-ce/ubuntu/) and do the last step of adding your user to docker group.

### Connecting to Remote Docker Daemons
FluentDocker supports connection to remote docker daemons. The fluent API supports e.g. 
```cs
new Builder().UseHost().UseMachine().WithName("remote-daemon")
```
where this requires a already pre-setup entry in the _docker-machine_ registry. It is also possible to
define _SSH_ based _docker-machine_ registry entires to connect to remote daemon.

```cs
      using (
        var container =
          new Builder().UseHost()
            .UseSsh("192.168.1.27").WithName("remote-daemon")
            .WithSshUser("solo").WithSshKeyPath("${E_LOCALAPPDATA}/lxss/home/martoffi/.ssh/id_rsa")
            .UseContainer()
            .UseImage("postgres:9.6-alpine")
            .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
            .Build())
      {
        Assert.AreEqual(ServiceRunningState.Stopped, container.State);
      }
```
This example will create a new _docker-machine_ registry entry named _remote-daemon_ that uses _SSH_ with
ip address of _192.168.1.27_ and the _SSH_ user _solo_. If a entry is already found named _remote-daemon_
it will just reuse this entry. Then it gets a _IHostService_ with correct certificates and _URL_ for the
remote daemon. Thus, it is possible then to create a docker container on the remote daemon, in thus case
it is the _postgres_ image. When it disposes the container, as usual it deletes it from the remote docker.
The _IHostService_ do make sure to pick upp all necessary certificates in order to authenticate the connection.

The above example produces this _docker-machine_ registry entry.
```
C:\Users\martoffi>docker-machine ls
NAME           ACTIVE   DRIVER    STATE     URL                       SWARM   DOCKER        ERRORS
remote-daemon  *        generic   Running   tcp://192.168.1.27:2376           v18.06.1-ce
```

In order to use ```UseSsh(...)``` a _SSH_ tunnel with no password must been set up. In addition the user
that uses the tunnel must be allowed to access the docker daemon.

Follow these tutorial how to setup the _SSH_ tunnel and make sure the user can access the docker daemon.
1) https://www.kevinkuszyk.com/2016/11/28/connect-your-docker-client-to-a-remote-docker-host/
2) https://askubuntu.com/questions/192050/how-to-run-sudo-command-with-no-password

Basically create a new rsa key to use with the _SSH_ tunnel using ```ssh-keygen -t rsa``` and then
copy it to the remote host by ```ssh-copy-id {username}@{host}```.

Edit the /etc/sudoers as specified in the second tutorial.

When this is done, you now can access the remote docker daemon by the generic driver or the fluent API
specified above. To do the same thing manually as specified in the example it would look something like this.


```
C:\Users\martoffi>docker-machine.exe create --driver generic --generic-ip-address=192.168.1.27 --generic-ssh-key="%localappdata%/lxss/home/martoffi/.ssh/id_rsa" --generic-ssh-user=solo remote-daemon
Running pre-create checks...
Creating machine...
(remote-daemon) Importing SSH key...
Waiting for machine to be running, this may take a few minutes...
Detecting operating system of created instance...
Waiting for SSH to be available...
Detecting the provisioner...
Provisioning with ubuntu(systemd)...
Installing Docker...
Copying certs to the local machine directory...
Copying certs to the remote machine...
Setting Docker configuration on the remote daemon...
Checking connection to Docker...
Docker is up and running!
To see how to connect your Docker Client to the Docker Engine running on this virtual machine, run: docker-machine.exe env remote-daemon
```

Now the registry entry is created, it is possible set the environment for the terminal docker.

```
C:\Users\martoffi>docker-machine.exe env remote-daemon
SET DOCKER_TLS_VERIFY=1
SET DOCKER_HOST=tcp://192.168.1.24:2376
SET DOCKER_CERT_PATH=C:\Users\martoffi\.docker\machine\machines\remote-daemon
SET DOCKER_MACHINE_NAME=remote-daemon
SET COMPOSE_CONVERT_WINDOWS_PATHS=true
REM Run this command to configure your shell:
REM     @FOR /f "tokens=*" %i IN ('docker-machine.exe env remote-daemon') DO @%i
```

Run this to make docker client use the remote docker daemon.
```
@FOR /f "tokens=*" %i IN ('docker-machine.exe env remote-daemon') DO @%i
```
All commands using the ```docker``` binary will now execute on the remote docker daemon.

### Hyper-V
When creating and querying, via machine, a hyper-v docker machine the process needs to be elevated since Hyper-V will not
respond to API calls in standard user mode.

## Misc

### Health Check
It is possible to specify a health check for the docker container to report the state of the container based on such activity. The following example
is using a health check that the container has exited or not. It is possible to check the configuration (make sure to force refresh) what status
the health check is reporting.
```cs
      using (
        var container =
          Fd.UseContainer()
            .UseImage("postgres:latest", force: true)
            .HealthCheck("exit")
            .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
            .Build()
            .Start())
      {
        var config = container.GetConfiguration(true);
        AreEqual(HealthState.Starting, config.State.Health.Status);
      }
```

### ulimit
It is possible via the _Fluent API_ and `ContainerCreateParams` specify ulimit to the docker container to e.g. limit the number of open files etc. For example
using the _Fluent API_ could look like this when restricting the number of open files to 2048 (both soft and hard).
```cs
using (
        var container =
          Fd.UseContainer()
            .UseImage("postgres:latest", force: true)
            .UseUlimit(Ulimit.NoFile,2048, 2048)
            .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
            .Build()
            .Start())
      {
        // Do stuff
      }
```

## Test Support
This repo contains three nuget packages, one for the fluent access, one for ms-test base classes and another for xunit base classes to be used while testing. For example in a unit-test it is possible to fire up a postgres container and wait when the the db has booted.

### XUnit
```cs
     public class PostgresXUnitTests : IClassFixture<PostgresTestBase>
     {
          [Fact]
          public void Test()
          {
               // We now have a running postgres
               // and a valid connection string to use.
          }
     }
```


### MSTest
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
Note that if un-named container, if not properly disposed, the docker container will still run and must be manually removed. This is a feature not a bug since you might want several containers running in your test. The `DockerContainer` class manages the instance id of the container and thus only interact with it and no other container.

When creating / starting a new container it will first check the local repository if the container image is already present and will download it if not found. This may take some time and there's just a Debug Log if enabled it is possible to monitor the download process.

## Miscellanious

### Unhandled Exceptions
When a unhandled exception occurs and the application _FailFast_ i.e. terminates quickly it
will *not* invoke ```finally``` clause. Thus a failing ```WaitForPort``` inside a ```using``` statement will *not* 
dispose the container service. Therefore the container is is still running. To fix this, either have a global 
try...catch or inject one locally e.g.
```cs
            try
            {
                using (var container =
                        new Builder().UseContainer()
                            .UseImage("postgres:9.6-alpine")
                            .ExposePort(5432)
                            .WithEnvironment("POSTGRES_PASSWORD=postgres")
                            .WaitForPort("5777/tcp", 10000) // Fail here since 5777 is not valid port
                            .Build())
                {
                    container.Start(); // FluentDockerException is thrown here since WaitForPort is executed
                }
            } catch { throw; }
```
But it this is only when application termination is done due to the ```FluentDockerException``` thrown in the
```WaitForPort```, otherwise it will dispose the container properly and thus the ```try...catch``` is not needed.

This could also be solved using the ```Fd.Build``` functions (_see Using Builder Extensions_ for more information).

### Using Builder Extensions
The class ```Fd``` is a static _class_ that provides convenience methods for building and running single
and composed containers. To build a container just use:
```cs
    var build = Fd.Build(c => c.UseContainer()
                    .UseImage("postgres:9.6-alpine")
                    .ExposePort(5432)
                    .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
                    .WaitForPort("5432/tcp", TimeSpan.FromSeconds(30)));

// This is the equivalent of
    var build = new Builder().UseContainer()
                             .UseImage("postgres:9.6-alpine")
                             .ExposePort(5432)
                             .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
                             .WaitForPort("5432/tcp", TimeSpan.FromSeconds(30));
```
This can then be used to start the containers within a _safe_ ```using``` clause that is **guaranteed** to be
disposed even if uncaught exception.
```cs
    build.Container(svc =>
    {
        var config = svc.GetConfiguration();
        // Do stuff...
    });   
```
After the ```Container``` method has been executed the container is in this case stopped and removed. This is the quivalent of
```cs
   // This is equivalent of
    try
    {
        using(var svc = build.Build()) 
        {
            svc.Start();
            
            var config = svc.GetConfiguration();
            // Do stuff...            
        }
    }
    catch
    {
        Log(...);
        throw;
    }
```


It is also possible to combine builder and running e.g. via:
```cs
      Fd.Container(c => c.UseContainer()
          .UseImage("postgres:9.6-alpine")
          .ExposePort(5432)
          .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
          .WaitForPort("5432/tcp", TimeSpan.FromSeconds(30)),
        svc =>
        {
          var config = svc.GetConfiguration();
          // Do stuff...
        });
```
The above example will build the container, start, stop, and finally delete the container. Even if and
```Exception``` is thrown it will be ```Disposed```. Of course it is possible to use compsed container using 
```composite``` extension methods as with ```container```.
