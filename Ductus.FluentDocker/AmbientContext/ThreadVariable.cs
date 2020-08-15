using System;
using System.Collections.Generic;
using System.Threading;

namespace Ductus.FluentDocker.AmbientContex
{
  /// <summary>
    /// <h1>Minimod.ThreadVariable, Version 1.0.0, Copyright © Lars Corneliussen 2011</h1>
    /// <para>Makes thread variables (<see cref="ThreadStaticAttribute"/>) available
    /// only inside a specific scope. It also manages nested usage of scopes on
    /// the same variable declaration.</para>
    /// </summary>
    /// <remarks>
    /// Author: Lars Corneliussen, 2008, http://startbigthinksmall.wordpress.com
    /// Licensed under the Apache License, Version 2.0; you may not use this file except in compliance with the License.
    /// http://www.apache.org/licenses/LICENSE-2.0
    /// </remarks>
    /// <seealso cref="http://startbigthinksmall.wordpress.com/2008/04/24/nice-free-and-reusable-net-ambient-context-pattern-implementation/"/>
    /// <seealso cref="http://aabs.wordpress.com/2007/12/31/the-ambient-context-design-pattern-in-net/"/>
    /// <seealso cref="https://github.com/minimod/minimods/blob/master/MiniMods/ThreadVariable/ThreadVariable.cs"/>
    /// <example>
    /// You can store a static instance of the thread variable and use it in any thread.
    /// One possibilty is to make it public, the other one is to wrap it, if you want more
    /// control.
    ///
    /// <code>
    /// <![CDATA[
    /// public static class SomeThreadVars
    /// {
    ///     public static readonly ThreadVariable<bool> IsSuperUser = new ThreadVariable<bool>();
    /// }
    ///
    /// ....
    ///
    /// using(SomeThreadVars.IsSuperUser.Use(true))
    /// {
    ///     SomeThreadVars.IsSuperUser.Current.ShouldBeTrue();
    /// }
    /// ]]>
    /// </code>
    /// </example>
    /// <typeparam name="T">The type of the values to store.</typeparam>
    public class ThreadVariable<T>
    {
        /// <summary>
        /// Storing the default value for a struct, null for a class or a
        /// custom value given in as default to the constructor of
        /// <see cref="ThreadVariable{T}"/>.
        /// </summary>
        private readonly T _fallback;

        /// <summary>
        /// just to avoid comparing fallback with default(T)
        /// </summary>
        private readonly bool _fallbackDefined = false;

        /// <summary>
        /// Stores the values (wrapped in <see cref="ThreadVariableValueScope"/>)
        /// for <c>n</c> <see cref="ThreadVariable{T}"/> within the current thread.
        /// </summary>
        [ThreadStatic]
        private static Dictionary<ThreadVariable<T>, ThreadVariableValueScope> _values;

        /// <summary>
        /// Initializes a threadsafe reusable instance.
        /// </summary>
        public ThreadVariable()
        {
        }

        /// <summary>
        /// Initializes a threadsafe reusable instance with <paramref name="fallback"/>
        /// as fallback value to <see cref="Current"/> and <see cref="CurrentOrDefault"/>.
        /// </summary>
        /// <remarks>
        /// Defaults as 0 for <see cref="int"/> or null for any <see cref="object"/> are allowed,
        /// but <see cref="HasCurrent"/> will then still be <value>true</value> and both
        /// <see cref="Current"/> and <see cref="CurrentOrDefault"/> will return them as
        /// valid values.
        /// </remarks>
        /// <param name="fallback">
        /// The value <see cref="Current"/> and <see cref="CurrentOrDefault"/> will return if
        /// there is no value provided to the current calling thread.
        /// </param>
        public ThreadVariable(T fallback)
        {
            _fallback = fallback;
            _fallbackDefined = true;
        }

        /// <summary>
        /// Returns <see cref="_values"/> and creates a new dictionary if it doesn't exist.
        /// </summary>
        private static Dictionary<ThreadVariable<T>, ThreadVariableValueScope> EnsuredValues
        {
            get
            {
                if (_values == null)
                    _values = new Dictionary<ThreadVariable<T>, ThreadVariableValueScope>(1);
                return _values;
            }
        }

        /// <summary>
        /// Returns a scope for the <paramref name="value"/>. The value is held in the
        /// current thread, until the scope is disposed.
        /// </summary>
        /// <remarks>
        /// Within the scope, the value will be available via <see cref="Current"/> and
        /// <see cref="CurrentOrDefault"/>, <see cref="HasCurrent"/> will always
        /// be <value>true</value>.
        /// </remarks>
        public IDisposable Use(T value)
        {
            ThreadVariableValueScope old;
            var v = EnsuredValues;
            v.TryGetValue(this, out old);
            return v[this] = new ThreadVariableValueScope(this, value, old);
        }

        /// <summary>
        /// Returns the value of the current inner most scope. Throws an
        /// <see cref="InvalidOperationException"/>, if no scope is available
        /// in the calling context.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Is thrown when the calling context hasn't provided any value to
        /// the current scope.
        /// </exception>
        public T Current
        {
            get
            {
                ThreadVariableValueScope current;
                EnsuredValues.TryGetValue(this, out current);
                if (current == null)
                {
                    if (_fallbackDefined)
                        return _fallback;

                    throw new InvalidOperationException("There is currently no value available.");
                }

                return current.value;
            }
        }


        /// <summary>
        /// Returns the value of the current inner most scope. If the calling context
        /// doesn't have any value, <c>default(T)</c> is returned.
        /// </summary>
        /// <remarks>
        /// default(T) is usually
        /// </remarks>
        public T CurrentOrDefault
        {
            get { return HasCurrent ? Current : default(T); }
        }

        /// <summary>
        /// Gets a value indicating wether the current calling context has provided any values.
        /// </summary>
        public bool HasCurrent
        {
            get { return _fallbackDefined || EnsuredValues.ContainsKey(this); }
        }


        /// <summary>
        /// Disposes the scope <paramref name="scopeToDispose"/> and it's other
        /// nested scopes.
        /// </summary>
        private void DisposeScope(ThreadVariableValueScope scopeToDispose)
        {
            // 1) The programmer simply did an error if he tried to dispose a scope on a different thread
            // 2) If the garbage collection takes care of disposal, it will get an error and ignore it
            if (Thread.CurrentThread.ManagedThreadId != scopeToDispose.threadId)
                throw new InvalidOperationException("The thread variable value scope "
                                                    + "has to be disposed on the same thread it was created on!");

            /* Let's say we have three scopes: outer, middle and inner.
             *
             * When disposing 'inner', usually the value is
             * recovered to 'middle'.
             * But if 'middle' is disposed before 'inner',
             * 'inner' should be disposed automatically, too.
             *
             * Else, disposing 'inner' afterwards, would end in
             * setting the current value to 'middle' - which
             * already is disposed
             */

            /* start on the current scope wich is always scopeToDispose
             * or one of its inner scopes - actually the inner most one */
            var v = EnsuredValues;
            ThreadVariableValueScope innerMost;
            v.TryGetValue(this, out innerMost);

            // dispose all inner scopes
            while (innerMost != scopeToDispose)
            {
                innerMost.MarkAsDisposed();
                innerMost = innerMost.previous;
            }

            // mark current one as disposed
            scopeToDispose.MarkAsDisposed();

            // remove, or recover previous value
            if (scopeToDispose.previous == null)
                v.Remove(this);
            else
                v[this] = scopeToDispose.previous;
        }


        /// <summary>
        /// Inner helper class wrapping the current value, it's precedor as well
        /// as some meta data.
        /// </summary>
        private class ThreadVariableValueScope : IDisposable
        {
            private readonly ThreadVariable<T> _key;
            private bool _isDisposed;

            /* These values are accessed from ThreadVariable and therefor
             * marked as internal. It's all private, so i drop properties
             * wich just would make it slower. */
            internal readonly T value;
            internal readonly ThreadVariableValueScope previous;
            internal readonly int threadId;

            /* the previous value is stored in order to avoid the overhead of an stack or linked list */

            public ThreadVariableValueScope(ThreadVariable<T> key, T value, ThreadVariableValueScope previous)
            {
                threadId = Thread.CurrentThread.ManagedThreadId;
                _key = key;
                this.value = value;
                this.previous = previous;
            }

            internal void MarkAsDisposed()
            {
                _isDisposed = true;

                // Dispose() will then not be called by the GarbageCollector
                GC.SuppressFinalize(this);
            }

            void IDisposable.Dispose()
            {
                if (_isDisposed)
                    return;

                _key.DisposeScope(this);
            }
        }
    }
}
