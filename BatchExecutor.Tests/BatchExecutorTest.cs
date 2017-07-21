﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BatchExecutor.Tests
{
	[TestClass]
	public class BatchExecutorTest
	{
		[TestMethod]
		public async Task ExecAsync_ActionTrowsException_ExceptionCatchedByAwait()
		{
			using (var batchExecutor = new BatchExecutor<int, string>(5, async items =>
																		 {
																			 await Task.Delay(10);
																			 throw new Exception("Exception in action");
																		 }, TimeSpan.FromMilliseconds(50)))
			{
				var catched = false;
				try
				{
					await batchExecutor.ExecAsync(1).ConfigureAwait(false);
				}
				catch (Exception)
				{
					catched = true;
				}
				Assert.AreEqual(true, catched);
			}
		}

		[TestMethod]
		public async Task ExecAsync_ProcessQueue_QueueProcessedInCorrectOrder()
		{
			using (var batchExecutor = new BatchExecutor<int, string>(5, async items =>
																		 {
																			 await Task.Delay(10);
																			 var dictionary = items.ToDictionary(i => i, i => i.ToString());
																			 return dictionary;
																		 }, TimeSpan.FromMilliseconds(50)))
			{
				var errorsOccurs = false;
				var tasks = new List<Task<string>>();
				try
				{
					const int maxSteps = 17;
					for (var i = 0; i < maxSteps; i++)
					{
						tasks.Add(batchExecutor.ExecAsync(i));
					}
					await Task.WhenAll(tasks).ConfigureAwait(false);
					for (var i = 0; i < maxSteps; i++)
					{
						Assert.AreEqual(i.ToString(), tasks[i].Result);
					}
				}
				catch (Exception)
				{
					errorsOccurs = true;
				}
				Assert.AreEqual(false, errorsOccurs);
			}
		}

		[TestMethod]
		public async Task ExecAsync_BufferNotFull_ProcessedByTimer()
		{
			var flushInterval = TimeSpan.FromMilliseconds(100);
			using (var batchExecutor = new BatchExecutor<int, string>(500, async items =>
																		   {
																			   await Task.Delay(1);
																			   var dictionary = items.ToDictionary(i => i, i => i.ToString());
																			   return dictionary;
																		   }, flushInterval))
			{

				var sw = Stopwatch.StartNew();
				var result = await batchExecutor.ExecAsync(1).ConfigureAwait(false);
				sw.Stop();
				Console.WriteLine($"Timer: {flushInterval}, elapsed: {sw.Elapsed}");
				Assert.AreEqual("1", result);
				var deviation = flushInterval.TotalMilliseconds / 100 * 50;
				Assert.IsTrue(sw.Elapsed.TotalMilliseconds <= flushInterval.TotalMilliseconds + deviation);
			}
		}

		[TestMethod]
		public async Task ExecAsync_BufferFull_ProcessedImmediately()
		{
			var flushInterval = TimeSpan.FromMilliseconds(10000);
			var batchSize = 50;
			using (var batchExecutor = new BatchExecutor<int, string>(batchSize, async items =>
																		   {
																			   await Task.Delay(1);
																			   var dictionary = items.ToDictionary(i => i, i => i.ToString());
																			   return dictionary;
																		   }, flushInterval))
			{
				var sw = Stopwatch.StartNew();
				var tasks = new List<Task>();
				for (var i = 0; i < batchSize; i++)
				{
					tasks.Add(batchExecutor.ExecAsync(i));
				}
				await Task.WhenAll(tasks).ConfigureAwait(false);
				sw.Stop();
				Console.WriteLine($"Timer: {flushInterval}, elapsed: {sw.Elapsed}");
				var deviation = 50 / 100 * 50;
				Assert.IsTrue(sw.Elapsed.TotalMilliseconds <= flushInterval.TotalMilliseconds + deviation);
			}
		}

		[TestMethod]
		public async Task ExecAsync_ActionTrowsException_AllTasksInBatchHasSameException()
		{
			using (var batchExecutor = new BatchExecutor<int, string>(5, async items =>
																		 {
																			 await Task.Delay(1);
																			 var dictionary = items.ToDictionary(i => 411, i => i.ToString());
																			 return dictionary;
																		 }, TimeSpan.FromMilliseconds(50)))
			{
				var tasks = new List<Task>();
				for (var i = 0; i < 7; i++)
				{
					tasks.Add(batchExecutor.ExecAsync(i));
				}
				var catched = false;
				try
				{
					await Task.WhenAll(tasks).ConfigureAwait(false);
				}
				catch (Exception)
				{
					catched = true;
				}
				Assert.AreEqual(true, catched);
				Task tmp = null;
				foreach (var task in tasks)
				{
					if (tmp == null)
						tmp = task;
					else
					{
						Debug.Assert(tmp.Exception != null, "tmp.Exception != null");
						Debug.Assert(task.Exception != null, "task.Exception != null");
						Assert.AreEqual(tmp.Exception.Message, task.Exception.Message);
					}
				}
			}
		}

		[TestMethod]
		public async Task ExecAsync_LoadTesting_OK()
		{
			var concurrentDict = new ConcurrentDictionary<int, string>();
			var batchExecutor = new BatchExecutor<int, string>(5, async items =>
																  {
																	  await Task.Delay(1);
																	  var dictionary = items.ToDictionary(i => i, i => i.ToString());
																	  return dictionary;
																  }, TimeSpan.FromMilliseconds(50));
			var tasks = new List<Task>();
			const int loopCount = 1000;
			for (var i = 1; i <= loopCount; i++)
			{
				var i1 = i;
				tasks.Add(Task.Run(async () =>
								   {
									   var result = await batchExecutor.ExecAsync(i1).ConfigureAwait(false);
									   concurrentDict[i1] = result;
									   return result;
								   }));
			}
			await Task.WhenAll(tasks).ConfigureAwait(false);
			Assert.AreEqual(loopCount, concurrentDict.Count);
			foreach (var kvp in concurrentDict)
			{
				Assert.AreEqual(kvp.Key.ToString(), kvp.Value);
			}
		}
	}
}