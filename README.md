# FluentDocker
FluentDocker is a library to interact with docker-machine and docker. It supports the new docker beta, docker machine or native linux (however only tested on windows machine).
Simple fluent interface to Docker.DotNet to simplify forking one or more docker containers concurrenlty. Search for FluentDocker for *Nuget* packages for pre-compiled assemblies. The main package [Ductus.FluentDocker](https://www.nuget.org/packages/Ductus.FluentDocker/ "Nuget Home for Ductus.FluentDocker") Nuget package and the [Ductus.FluentDocker.MsTest](https://www.nuget.org/packages/Ductus.FluentDocker.MsTest/ "Nuget Home for Ductus.FluentDocker.MsTest") can be found on those locations.

In order to use `DockerBuilder` and `DockerContainer` with boot2docker you must have the docker daemon on the virtual machine, e.g. run the 'Quickstart Terminal' and the DOCKER environment variables. Simplest is to run devenv.exe through the 'Quickstart Terminal' and your'e all set to go. On Linux, just make sure the docker daemon is running and you have all the DOCKER environment variables set. The `DockerContainer`, when using boot2docker, makes use of the installation path to obtain the certificates and keys neccesary to do proper SSL communication. If you have a proxy, you may encounter that it will not be able to communicate properly with the docker daemon.

## Test Support
This repo contains two nuget packages, one for the fluent access and the other is a ms-test base classes to be used while testing. For example in a unit-test it is possible to fire up a postgres container and wait when the the db has booted.
```cs
     using (
        var container =
          new DockerBuilder()
            .WithImage("postgres:latest")
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
               .WithImage("postgres:latest")
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
            .WithImage("postgres:latest")
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
            .WithImage("postgres:latest")
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
            .WithImage("postgres:latest")
            .CopyFromContainer("/bin", "${TEMP}/fluentdockertest/${RND}", "test")
            .Build().Start();
```
This example shows fluent configuration to copy files to a temp path with a random folder. The host path is accessed using ```container.GetHostCopyPath("test")```. These files are copied before Start has finished.

```cs
     var container =
          new DockerBuilder()
            .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
            .WithImage("postgres:latest")
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
              .WithImage("postgres:latest")
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
