# FluentDocker
Simple fluent interface to Docker.DotNet to simplify forking one or more docker containers concurrenlty. Search for FluentDocker for *Nuget* packages for pre-compiled assemblies.

In order to use `DockerBuilder` and `DockerContainer` with boot2docker you must have the docker daemon on the virtual machine, e.g. run the 'Quickstart Terminal' and the DOCKER environment variables. Simplest is to run devenv.exe through the 'Quickstart Terminal' and your'e all set to go. On Linux, just make sure the docker daemon is running and you have all the DOCKER environment variables set. The `DockerContainer`, when using boot2docker, makes use of the installation path to obtain the certificates and keys neccesary to do proper SSL communication. If you have a proxy, you may encounter that it will not be able to communicate properly with the docker daemon.

This repo contains two nuget packages, one for the fluent access and the other is a mstest base classes to be used while testing. For example in a unit-test it is possible to fire up a postgres container and wait when the the db has booted.

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

It is also possible to re-use abstract base classes, for example postgres test base to simplify and make clean unittest towards a container.

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
  
The `FluentDockerTestBase` allows for simple overrides to do whatever custom docker backed test easily. Just create a test class and derive from the `FluentDockerTestBase` and override suitable methods. For example.

     protected override DockerBuilder Build()
     {
          return new DockerBuilder()
               .WithImage("postgres:latest")
               .WithEnvironment($"POSTGRES_PASSWORD={PostgresPassword}")
               .ExposePorts("5432")
               .WaitForPort("5432/tcp", 30000 /*30s*/);
     }
     
This will create a builder with docker image postgres:latest and set one environment string, it will also expose the postgres db port 5432 to the host so one can connect to the db within the container. Lastly it will wait for the port 5432. This ensures that the db is running and have properly booted. If timeout, in this example set to 30 seconds, it will throw an exception and the container is stopped and removed. Note that the host port is not 5432! Use `Container.GetHostPort("5432/tcp")` to get the host port. The host ip can be retrieved by `Container.Host` property and thus shall be used when communicating with the app in the container. 

If a callback is needed when the container has been successfully pulled, created, and started.

     protected override void OnContainerInitialized()
     {
          ConnectionString = string.Format(PostgresConnectionString, Container.Host,
               Container.GetHostPort("5432/tcp"), PostgresUser,
               PostgresPassword, PostgresDb);
     }
     
This example renders a proper connection string to the postgresql db within the newly spun up container. This can be used to connect using Npgsql, EF7, NHibernate, Marten or other compatible tools. This method will not be called if pulling of the image from the docker repository or it could not create/start the container.

If a before shutdown container hook is wanted override.

     protected virtual void OnContainerTearDown()
     {
          // Do stuff before container is shut down.
     }

Note that if unamed container, if not properly disposed, the docker container will still run and must be manually removed. This is a feature not a bug since you might want several containers running in your test. The `DockerContainer` class manages the instance id of the container and thus only intract with it and no other container.

When creating / starting a new container it will first check the local repository if the container image is already present and will download it if not found. This may take some time and there's just a Debug Log if enabled it is possible to monitor the download process.
