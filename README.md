# FluentDocker
Simple fluent interface to Docker.DotNet mainly used for unittesting.

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
  
