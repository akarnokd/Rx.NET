﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information. 

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using Microsoft.Reactive.Testing;
using Xunit;
using ReactiveTests.Dummies;
using System.Reflection;
using System.Threading;
using System.Reactive.Disposables;
using System.Reactive.Subjects;

namespace ReactiveTests.Tests
{
    public class UsingAsyncTest : ReactiveTest
    {

        [Fact]
        public void UsingAsync_ArgumentChecking()
        {
            ReactiveAssert.Throws<ArgumentNullException>(() => Observable.Using<int, IDisposable>(null, (res, ct) => null));
            ReactiveAssert.Throws<ArgumentNullException>(() => Observable.Using<int, IDisposable>(ct => null, null));
        }

        [Fact]
        public void UsingAsync_Simple()
        {
            var done = false;

            var xs = Observable.Using<int, IDisposable>(
                ct => Task.Factory.StartNew<IDisposable>(() => Disposable.Create(() => done = true)),
                (_, ct) => Task.Factory.StartNew<IObservable<int>>(() => Observable.Return(42))
            );

            var res = xs.ToEnumerable().ToList();

            Assert.True(new[] { 42 }.SequenceEqual(res));
            Assert.True(done);
        }

        [Fact]
        public void UsingAsync_CancelResource()
        {
            var N = 10;// 0000;
            for (int i = 0; i < N; i++)
            {
                var called = false;

                var s = new ManualResetEvent(false);
                var e = new ManualResetEvent(false);
                var x = new ManualResetEvent(false);

                var xs = Observable.Using<int, IDisposable>(
                    ct => Task.Factory.StartNew<IDisposable>(() =>
                    {
                        s.Set();
                        e.WaitOne();
                        while (!ct.IsCancellationRequested)
                            ;
                        x.Set();
                        return Disposable.Empty;
                    }),
                    (_, ct) =>
                    {
                        called = true;
                        return Task.Factory.StartNew<IObservable<int>>(() =>
                            Observable.Return(42)
                        );
                    }
                );

                var d = xs.Subscribe(_ => { });

                s.WaitOne();
                d.Dispose();

                e.Set();
                x.WaitOne();

                Assert.False(called);
            }
        }

        [Fact]
        public void UsingAsync_CancelFactory()
        {
            var N = 10;// 0000;
            for (int i = 0; i < N; i++)
            {
                var gate = new object();
                var disposed = false;
                var called = false;

                var s = new ManualResetEvent(false);
                var e = new ManualResetEvent(false);
                var x = new ManualResetEvent(false);

                var xs = Observable.Using<int, IDisposable>(
                    ct => Task.Factory.StartNew<IDisposable>(() =>
                        Disposable.Create(() =>
                        {
                            lock (gate)
                                disposed = true;
                        })
                    ),
                    (_, ct) => Task.Factory.StartNew<IObservable<int>>(() =>
                    {
                        s.Set();
                        e.WaitOne();
                        while (!ct.IsCancellationRequested)
                            ;
                        x.Set();
                        return Observable.Defer<int>(() =>
                        {
                            called = true;
                            return Observable.Return(42);
                        });
                    })
                );

                var d = xs.Subscribe(_ => { });

                s.WaitOne();

                //
                // This will *eventually* set the CancellationToken. There's a fundamental race between observing the CancellationToken
                // and returning the IDisposable that will set the CancellationTokenSource. Notice this is reflected in the code above,
                // by looping until the CancellationToken is set.
                //
                d.Dispose();

                e.Set();
                x.WaitOne();

                while (true)
                {
                    lock (gate)
                        if (disposed)
                            break;
                }

                Assert.False(called, i.ToString());
            }
        }

    }
}
