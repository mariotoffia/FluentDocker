# FluentDocker
FluentDocker is a library to interact with docker-machine, docker-compose and docker. It supports the new docker beta, docker machine or native linux (however only tested on windows machine and docker beta for windows). This library is available at nuget [Ductus.FluentDocker](https://www.nuget.org/packages/Ductus.FluentDocker/ "Nuget Home for Ductus.FluentDocker") and the ms test support is available at [Ductus.FluentDocker.MsTest](https://www.nuget.org/packages/Ductus.FluentDocker.MsTest/ "Nuget Home for Ductus.FluentDocker.MsTest").

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

## Working with Volumes
It is possible to mount volumes onto the host that are exposed within the docker container. See sample below how to do a simple mount where a nginx server will serve html pages from a host directory.
```cs
    private const string Html = "<html><head>Hello World</head><body><h1>Hello world</h1></body></html>";

     using (var container =
          new DockerBuilder()
            .WithImage("nginx:latest")
            .ExposePorts("80")
            .WaitForPort("80/tcp", 10000 /*10s*/)
            .MountNamedVolume("test", "${TEMP}/fluentdockertest/${RND}", "/usr/share/nginx/html", "ro")
            .WhenDisposed()
               .RemoveVolume("${TEMP}/fluentdockertest")
            .Build().Start())
      {
        var hostdir = container.GetHostVolume("test");
        File.WriteAllText(Path.Combine(hostdir, "hello.html"), Html);
 
        var request = WebRequest.Create($"http://{container.Host}:{container.GetHostPort("80/tcp")}/hello.html");
        using (var response = request.GetResponse())
        {
          var dataStream = response.GetResponseStream();
          using (var reader = new StreamReader(dataStream))
          {
            var responseFromServer = reader.ReadToEnd();
          }
        }
      }
```
This example will create a temporary directory under temp/fluentdocker/random-dir and mount it within the docker container /usr/share/nginx/html. When the `DockerContainer` is disposed it will delete the temp/fluentdocker directory along with all it's subdirectories and files. This is especially good when doing unit-tests.

When using boot2docker, make sure that the (if using standard) virtual box image has the path on the host mounted with correct permissions otherwise it will not be possible for docker processes to read-write to those volumes.

## Working with Container Processes
It is possible to query for running processes within the docker container. This can be used within e.g. unit-tests. It is also possible to wait for a certain process (as with a port) before the `DockerContainer` is considered as started. If fails to satisifie the critera an exception is thrown.

```cs
     using (
        var container =
          new DockerBuilder()
            .WithImage("kiasaki/alpine-postgres")
            .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
            .ExposePorts("5432")
            .WaitForPort("5432/tcp", 30000 /*30s*/)
            .WaitForProcess("postgres",30000/*30s*/)
            .Build())
      {
        container.Start();
        var proc = container.ContainerProcesses();
        Debug.WriteLine(proc.ToString());
      }
```
This example shows a unit-test where it waits fo a certain port to be opened *and* the postgres process to be running before executing the body code. Within the method body it will get the processes and dump those onto the debug log.

## Copy Files and Subdirectories from Running Docker Container
It is possible to copy files from a running docker container to the host for inspection. It also supports using the fluent builder to copy files from the container when executing Start(). In addition it is also possible to fluently declare copy operations from the docker container to host just before the container is disposed. This is particular useful when files are needed to inspect when e.g. a unit-test via docker container has finished and it needs files to assert upon within the container.

```cs
     using (
        var container =
          new DockerBuilder()
            .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
            .WithImage("kiasaki/alpine-postgres")
            .CopyFromContainer("/bin", "${TEMP}/fluentdockertest/${RND}", "test")
            .Build().Start())
      {
        var path = container.GetHostCopyPath("test");
        var files = Directory.EnumerateFiles(Path.Combine(path, "bin")).ToArray();
        Assert.IsTrue(files.Any(x => x.EndsWith("bash")));
      }
```
This example shows a unit-test where it manually copies files from the docker container to the host.

```cs
     var container =
          new DockerBuilder()
            .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
            .WithImage("kiasaki/alpine-postgres")
            .CopyFromContainer("/bin", "${TEMP}/fluentdockertest/${RND}", "test")
            .Build().Start();
```
This example shows fluent configuration to copy files to a temp path with a random folder. The host path is accessed using ```container.GetHostCopyPath("test")```. These files are copied before Start has finished.

```cs
     var container =
          new DockerBuilder()
            .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
            .WithImage("kiasaki/alpine-postgres")
            .WhenDisposed()
              .CopyFromContainer("/bin", "${TEMP}/fluentdockertest/${RND}","test")
            .Build().Start();
```
This example shows fluent configuration to copy files to a temp path with a random folder just before the container is disposed. The host path is accessed using ```container.GetHostCopyPath("test")```.

## Exporting and extracting containers
There are two ways of exporting and extracting a container to the host. Either manually, or explicitly using the container instance.
```cs
using (
          var container =
            new DockerBuilder()
              .WithImage("kiasaki/alpine-postgres")
              .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
              .ExposePorts("5432")
              .WaitForPort("5432/tcp", 30000 /*30s*/)
              .WaitForProcess("postgres", 30000 /*30s*/)
              .WhenDisposed()
               .ExportOnError("${PWD}/${RND}")
              .Build())
        {
          Assert.Fail();
          
          container.Success(); // Never reached and thus exported
        }
```
This example will extract the container onto current path + a random directory since ```container.Sucess()``` is never set. By default it will extract the tar archive, by supply false it will just store the tar file of the container. To manually export, just use ```container.ExportContainer(string hostFilePath, bool explode = true)```.
