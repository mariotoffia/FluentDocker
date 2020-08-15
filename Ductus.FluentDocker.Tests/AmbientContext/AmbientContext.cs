using System;
using System.Threading;
using Ductus.FluentDocker.AmbientContex;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ductus.FluentDocker.Tests.AmbientContext
{
  [TestClass]
  public class AmbientContext
  {
    [TestMethod]
    public void Test1000000Nested()
    {
      for (var i = 0; i < 1000000; i++)
      {
        var thrVar = new ThreadVariable<string>();
        using (thrVar.Use("my value"))
        {
          var x = thrVar.Current;
          x = thrVar.Current;
          x = thrVar.Current;
          using (thrVar.Use("my value 2"))
          {
            var x2 = thrVar.Current;
            x2 = thrVar.Current;
            x2 = thrVar.Current;
          }
        }
      }
    }

    [TestMethod]
    public void TestSimple()
    {
      var thrVar = new ThreadVariable<string>();
      ShouldBeEmpty(thrVar);
      using (thrVar.Use("my value"))
      {
        ShouldEqual(thrVar, "my value");
      }

      ShouldBeEmpty(thrVar);
    }

    private static void ShouldEqual<T>(ThreadVariable<T> thrVar, T expected)
    {
      Assert.IsTrue(thrVar.HasCurrent);
      Assert.AreEqual(thrVar.CurrentOrDefault, expected);
      Assert.AreEqual(thrVar.Current, expected);
    }

    private static void ShouldBeEmpty<T>(ThreadVariable<T> thrVar)
    {
      Assert.IsFalse(thrVar.HasCurrent);
      Assert.ThrowsException<InvalidOperationException>(() => Assert.IsNull(thrVar.Current));
      Assert.AreEqual(thrVar.CurrentOrDefault, default(T));
    }

    [TestMethod]
    public void TestFallback()
    {
      var thrVar = new ThreadVariable<string>("default");
      ShouldEqual(thrVar, "default");
      using (thrVar.Use("my value"))
      {
        ShouldEqual(thrVar, "my value");
      }

      ShouldEqual(thrVar, "default");
    }

    [TestMethod]
    public void TestValueTypeSupport()
    {
      var thrVar = new ThreadVariable<bool>();
      ShouldBeEmpty(thrVar);
      Assert.IsFalse(thrVar.CurrentOrDefault);
      using (thrVar.Use(true))
      {
        ShouldEqual(thrVar, true);
      }

      ShouldBeEmpty(thrVar);
      Assert.IsFalse(thrVar.CurrentOrDefault);
    }

    [TestMethod]
    public void TestNullableSupport()
    {
      var thrVar = new ThreadVariable<bool?>();
      ShouldBeEmpty(thrVar);
      Assert.IsFalse(thrVar.CurrentOrDefault.HasValue);
      using (thrVar.Use(true))
      {
        ShouldEqual(thrVar, true);
      }

      ShouldBeEmpty(thrVar);
      Assert.IsFalse(thrVar.CurrentOrDefault.HasValue);
    }

    [TestMethod]
    public void TestNested()
    {
      var thrVar = new ThreadVariable<string>();
      ShouldBeEmpty(thrVar);
      using (thrVar.Use("my value"))
      {
        ShouldEqual(thrVar, "my value");
        using (thrVar.Use("my value 2"))
        {
          ShouldEqual(thrVar, "my value 2");
        }

        ShouldEqual(thrVar, "my value");
      }

      ShouldBeEmpty(thrVar);
    }

    [TestMethod]
    public void TestMultipleValues()
    {
      var thrVar1 = new ThreadVariable<int?>();
      var thrVar2 = new ThreadVariable<int?>();
      var thrVar3 = new ThreadVariable<int?>();

      IDisposable scope1 = thrVar1.Use(1);
      IDisposable scope2 = thrVar2.Use(2);
      IDisposable scope3 = thrVar3.Use(3);

      Assert.AreEqual(thrVar1.Current, 1);
      Assert.AreEqual(thrVar2.Current, 2);
      Assert.AreEqual(thrVar3.Current, 3);

      scope1.Dispose();
      scope2.Dispose();
      scope3.Dispose();

      ShouldBeEmpty(thrVar1);
      ShouldBeEmpty(thrVar1);
      ShouldBeEmpty(thrVar1);

      Assert.IsNull(thrVar1.CurrentOrDefault);
      Assert.IsNull(thrVar2.CurrentOrDefault);
      Assert.IsNull(thrVar3.CurrentOrDefault);

      Assert.IsFalse(thrVar1.CurrentOrDefault.HasValue);
      Assert.IsFalse(thrVar2.CurrentOrDefault.HasValue);
      Assert.IsFalse(thrVar3.CurrentOrDefault.HasValue);
    }

    [TestMethod]
    public void TestDisposeInIncorrectOrder()
    {
      var thrVar = new ThreadVariable<int?>();

      ShouldBeEmpty(thrVar);
      using (thrVar.Use(1)) // outer scope
      {
        ShouldEqual(thrVar, 1);
        IDisposable middle = thrVar.Use(2);
        ShouldEqual(thrVar, 2);
        IDisposable inner = thrVar.Use(3);
        ShouldEqual(thrVar, 3);
        middle.Dispose();
        ShouldEqual(thrVar, 1);

        /* When disposing 'inner', usually the value is
         * recovered to 'middle'.Value.
         * But 'middle' is already disposed, so it should
         * go back to 'outer' instead.
         *
         * Internally 'middle'.Dispose() should also
         * dispose inner scopes, e.g. 'inner', so that
         * 'inner'.Dispose() just does nothing.
        */
        inner.Dispose();
        ShouldEqual(thrVar, 1);
      }

      ShouldBeEmpty(thrVar);
    }

    [TestMethod]
    public void TestMultithread()
    {
      var thrVar = new ThreadVariable<string>();
      var letWi1 = new AutoResetEvent(false);
      var letWi2 = new AutoResetEvent(false);

      WaitCallback W2 = state =>
      {
        Console.WriteLine("WI 2 Startup");

        ShouldBeEmpty(thrVar);
        Console.WriteLine("WI 2 is empty");

        using (thrVar.Use("B"))
        {
          Console.WriteLine("WI 2 Set");
          ShouldEqual(thrVar, "B");
          Console.WriteLine("WI 2 is B");

          letWi1.Set(); // goto 001

          // wait
          letWi2.WaitOne(); // 002
        }

        Console.WriteLine("WI 2 Disposed");
        ShouldBeEmpty(thrVar);
        Console.WriteLine("WI 2 is empty");
        Console.WriteLine("WI 2 End");
        letWi1.Set(); // goto 003
      };
      // start
      Console.WriteLine("WI 1 Startup");
      using (thrVar.Use("A"))
      {
        Console.WriteLine("WI 1 Set");
        ShouldEqual(thrVar, "A");
        Console.WriteLine("WI 1 is A");
        ThreadPool.QueueUserWorkItem(W2);

        // wait
        letWi1.WaitOne(); // 001
        ShouldEqual(thrVar, "A");
        Console.WriteLine("WI 1 is still A");
        letWi2.Set(); // goto 002

        // wait
        letWi1.WaitOne(); // 003
        ShouldEqual(thrVar, "A");
        Console.WriteLine("WI 1 is still A");
      }

      Console.WriteLine("WI 1 Disposed");
      ShouldBeEmpty(thrVar);
      Console.WriteLine("WI 1 is empty");
    }
  }
}
