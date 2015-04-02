﻿using System;
using System.Diagnostics;
using System.Threading;

namespace CacheManager.Core.Cache
{
    internal class CachePerformanceCounters<T> : IDisposable
    {
        private const string Category = ".NET CacheManager";
        private const string Entries = "Total cache items";
        private const string HitRatio = "Hit ratio";
        private const string HitRatioBase = "Cache hit ratio Base";
        private const string Hits = "Total hits";
        private const string HitsPerSecond = "Avg hits per second";
        private const string Misses = "Total misses";
        private const string ReadsPerSecond = "Avg gets per second";
        private const string Writes = "Total cache writes";
        private const string WritesPerSecond = "Avg writes per second";
        private static readonly int NumStatsCounters = Enum.GetValues(typeof(CacheStatsCounterType)).Length;

        private readonly Timer counterTimer;
        private readonly string instanceName = string.Empty;
        private readonly CacheStats<T> stats;
        private readonly long[] statsCounts;
        private PerformanceCounter[] counters;
        private bool enabled = true;
        private object updateLock = new object();

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Mobility", "CA1601:DoNotUseTimersThatPreventPowerStateChanges", Justification = "If perfCounters are enabled, we can live with the power consumption...")]
        public CachePerformanceCounters(string cacheName, string handleName, CacheStats<T> stats)
        {
            if (string.IsNullOrWhiteSpace(cacheName))
            {
                throw new ArgumentNullException("cacheName");
            }

            if (string.IsNullOrWhiteSpace(handleName))
            {
                throw new ArgumentNullException("handleName");
            }

            string processName = Process.GetCurrentProcess().ProcessName;

            this.instanceName = string.Concat(processName + ":" + cacheName + ":" + handleName);

            var invalidInstanceChars = new string[] { "(", ")", "#", "\\", "/" };

            foreach (var ichar in invalidInstanceChars)
            {
                this.instanceName = this.instanceName.Replace(ichar, "");
            }

            if (this.instanceName.Length > 128)
            {
                this.instanceName = this.instanceName.Substring(0, 128);
            }

            this.InitializeCounters();
            this.stats = stats;
            this.statsCounts = new long[NumStatsCounters];
            this.counterTimer = new Timer(new TimerCallback(this.PerformanceCounterWorker), null, 450L, 450L);
        }

        ~CachePerformanceCounters()
        {
            this.Dispose(false);
        }

        public void Decrement(CachePerformanceCounterType type)
        {
            this.GetCounter(type).Decrement();
        }

        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        public void Increment(CachePerformanceCounterType type)
        {
            this.GetCounter(type).Increment();
        }

        public void IncrementBy(CachePerformanceCounterType type, long value)
        {
            this.GetCounter(type).IncrementBy(value);
        }

        public void SetValue(CachePerformanceCounterType type, long value)
        {
            this.GetCounter(type).RawValue = value < 0 ? 0 : value;
        }

        private static void InitializeCategory()
        {
            if (PerformanceCounterCategory.Exists(Category))
            {
                //PerformanceCounterCategory.Delete(Category); // testing only,
                return; // TODO: comment out deleting and return if exists.
            }

            PerformanceCounterCategory.Create(
                Category,
                "CacheManager counters per handle",
                PerformanceCounterCategoryType.MultiInstance,
                new CounterCreationDataCollection
				{
					new CounterCreationData
					{
						CounterName = Entries,
						CounterHelp = "Current number of cache items stored within the cache handle",
						CounterType = PerformanceCounterType.NumberOfItems64
					},
					new CounterCreationData
					{
						CounterName = HitRatio,
						CounterHelp = "Cache hit ratio of the cache handle",
						CounterType = PerformanceCounterType.AverageCount64
					},
					new CounterCreationData
					{
					    CounterName = HitRatioBase,
                        CounterHelp = HitRatioBase,
                        CounterType = PerformanceCounterType.AverageBase
					},
					new CounterCreationData
					{
						CounterName = Hits,
                        CounterHelp = "Total number of cache hits of the cache handle",
                        CounterType = PerformanceCounterType.NumberOfItems64
					},
					new CounterCreationData
					{
                        CounterName = Misses,
                        CounterHelp = "Total number of cache misses of the cache handle",
                        CounterType = PerformanceCounterType.NumberOfItems64
					},
					new CounterCreationData
					{
                        CounterName = Writes,
                        CounterHelp = "Total number of cache writes (add,put,remove) of the cache handle",
                        CounterType = PerformanceCounterType.NumberOfItems64
					},
					new CounterCreationData
					{
                        CounterName = WritesPerSecond,
                        CounterHelp = WritesPerSecond,
                        CounterType = PerformanceCounterType.RateOfCountsPerSecond64
					},
					new CounterCreationData
					{
                        CounterName = ReadsPerSecond,
                        CounterHelp = ReadsPerSecond,
                        CounterType = PerformanceCounterType.RateOfCountsPerSecond64
					},
					new CounterCreationData
					{
                        CounterName = HitsPerSecond,
                        CounterHelp = HitsPerSecond,
                        CounterType = PerformanceCounterType.RateOfCountsPerSecond64
					},
                });
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes", Justification = "On Dispose the catch is just for safety...")]
        private void Dispose(bool disposeManaged)
        {
            if (disposeManaged)
            {
                this.counterTimer.Dispose();

                try
                {
                    ResetCounters();
                }
                catch { }
                finally
                {
                    foreach (var counter in this.counters)
                    {
                        counter.Dispose();
                    }
                }
            }
        }

        private PerformanceCounter GetCounter(CachePerformanceCounterType type)
        {
            return this.counters[(int)type];
        }

        private void InitializeCounters()
        {
            InitializeCategory();

            this.counters = new PerformanceCounter[9];
            this.counters[0] = new PerformanceCounter(Category, Entries, this.instanceName, false);
            this.counters[1] = new PerformanceCounter(Category, HitRatio, this.instanceName, false);
            this.counters[2] = new PerformanceCounter(Category, HitRatioBase, this.instanceName, false);
            this.counters[3] = new PerformanceCounter(Category, Hits, this.instanceName, false);
            this.counters[4] = new PerformanceCounter(Category, Misses, this.instanceName, false);
            this.counters[5] = new PerformanceCounter(Category, Writes, this.instanceName, false);
            this.counters[6] = new PerformanceCounter(Category, ReadsPerSecond, this.instanceName, false);
            this.counters[7] = new PerformanceCounter(Category, WritesPerSecond, this.instanceName, false);
            this.counters[8] = new PerformanceCounter(Category, HitsPerSecond, this.instanceName, false);

            // resetting them cleans up previous runs on the same category, which will otherwise
            // stay in perfmon forever
            this.ResetCounters();
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
        private void PerformanceCounterWorker(object state)
        {
            if (enabled && Monitor.TryEnter(updateLock))
            {
                try
                {
                    long[] previousCounts = new long[NumStatsCounters];
                    Array.Copy(this.statsCounts, previousCounts, NumStatsCounters);

                    for (int i = 0; i < NumStatsCounters; i++)
                    {
                        this.statsCounts[i] = this.stats.GetStatistic((CacheStatsCounterType)i);
                    }

                    var writes = this.statsCounts[4] + this.statsCounts[5] + this.statsCounts[7] + this.statsCounts[8];
                    var previousWrites = previousCounts[4] + previousCounts[5] + previousCounts[7] + previousCounts[8];
                    var hits = this.statsCounts[0] - previousCounts[0];

                    this.SetValue(CachePerformanceCounterType.Items, this.statsCounts[2]);

                    this.IncrementBy(CachePerformanceCounterType.HitRatioBase, this.statsCounts[6] - previousCounts[6]);
                    this.IncrementBy(CachePerformanceCounterType.HitRatio, hits);
                    this.IncrementBy(CachePerformanceCounterType.TotalHits, hits);
                    this.IncrementBy(CachePerformanceCounterType.TotalMisses, this.statsCounts[1] - previousCounts[1]);
                    this.IncrementBy(CachePerformanceCounterType.TotalWrites, writes - previousWrites);
                    this.IncrementBy(CachePerformanceCounterType.ReadsPerSecond, this.statsCounts[6] - previousCounts[6]);
                    this.IncrementBy(CachePerformanceCounterType.WritesPerSecond, writes - previousWrites);
                    this.IncrementBy(CachePerformanceCounterType.HitsPerSecond, hits);
                }
                catch (Exception e)
                {
                    enabled = false;
                    Trace.TraceError(e.Message + "\n" + e.StackTrace);
                }
                finally
                {
                    Monitor.Exit(updateLock);
                }
            }
        }

        private void ResetCounters()
        {
            for (int i = 0; i < Enum.GetValues(typeof(CachePerformanceCounterType)).Length; i++)
            {
                this.SetValue((CachePerformanceCounterType)i, 0L);
            }
        }
    }
}