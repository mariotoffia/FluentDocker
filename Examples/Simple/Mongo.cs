using System;
using Ductus.FluentDocker.Builders;
using Ductus.FluentDocker.Services.Extensions;
using MongoDB.Driver;

namespace Simple
{
  public class Mongo
  {
    struct User
    {
      public string Id { get; set; }
      public string Name { get; set; }
    }

    public static void RunMongoDb()
    {
      const string root = "root";
      const string secret = "secret";

      using (
          var container =
              new Builder().UseContainer()
                  .UseImage("mongo")
                  .WithEnvironment($"MONGO_INITDB_ROOT_USERNAME:{root}")
                  .WithEnvironment($"MONGO_INITDB_ROOT_PASSWORD:{secret}")
                  .ExposePort(27017)
                  .WaitForPort("27017/tcp", 30000 /*30s*/)
              .Build()
              .Start())
      {

        var ep = container.ToHostExposedEndpoint("27017/tcp");
        string connectionString = $"mongodb://{root}:{secret}@{ep}";

        // insert data 
        const string collectionName = "Users";

        var mongoClient = new MongoClient(connectionString);
        var database = mongoClient.GetDatabase("Users");

        var collection = database.GetCollection<User>(collectionName);

        var user = new User { Id = "1", Name = "John" };

        collection.InsertOne(user);
        var count = collection.CountDocuments<User>(u => u.Id == "1");

        Console.WriteLine($"Found {count} users with Id = 1");

      }
    }
  }
}