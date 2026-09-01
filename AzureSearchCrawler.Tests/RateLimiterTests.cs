/*
 * Note: The tests in this class intentionally take longer to run (1-2 seconds) because they test
 * functionality that is directly related to time delays and operation scheduling.
 * 
 * This is expected behavior because we need to verify that:
 * 1. Time delays are actually respected
 * 2. Operations are scheduled correctly across multiple threads
 * 3. Minimum spacing between operations is maintained
 * 
 * Possible future optimizations:
 * - Use a "fake" clock for testing
 * - Inject a controllable time source
 * - Use virtual time instead of real time
 * - Mark tests as "slow" and run them separately
 */

using System.Collections.Concurrent;
using System.Diagnostics;
using Xunit;
using System.Threading;
using AzureSearchCrawler.TestUtilities;

namespace AzureSearchCrawler.Tests
{
    public class RateLimiterTests
    {
        [Fact]
        public async Task WaitAsync_WhenDisabled_ReturnsImmediately()
        {
            // Arrange
            var limiter = new RateLimiter(TimeSpan.FromSeconds(4), enabled: false);
            var stopwatch = new Stopwatch();

            // Act
            stopwatch.Start();
            await limiter.WaitAsync();
            stopwatch.Stop();

            // Assert
            Assert.True(stopwatch.ElapsedMilliseconds < 100); // Should return almost immediately
        }

        [Fact]
        public async Task WaitAsync_FirstCall_ReturnsImmediately()
        {
            // Arrange
            var limiter = new RateLimiter(TimeSpan.FromSeconds(1));
            var stopwatch = new Stopwatch();

            // Act
            stopwatch.Start();
            await limiter.WaitAsync();
            stopwatch.Stop();

            // Assert
            Assert.True(stopwatch.ElapsedMilliseconds < 100); // First call should be fast
        }

        [Fact]
        public async Task WaitAsync_SecondCallWithinTimeSpan_WaitsForRemaining()
        {
            // Arrange
            var waitTime = TimeSpan.FromSeconds(1);
            var limiter = new RateLimiter(waitTime);
            
            // Act
            await limiter.WaitAsync(); // First call
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            await limiter.WaitAsync(); // Second call
            stopwatch.Stop();

            // Assert
            Assert.True(stopwatch.ElapsedMilliseconds >= 900); // At least 90% of wait time
            Assert.True(stopwatch.ElapsedMilliseconds <= 1200); // Max 20% over wait time
        }

        [Fact]
        public async Task WaitAsync_MultipleThreads_MaintainsMinimumSpacing()
        {
            // Arrange
            var waitTime = TimeSpan.FromSeconds(1);
            var limiter = new RateLimiter(waitTime);
            var tasks = new List<Task>();
            var timestamps = new ConcurrentBag<DateTime>();

            // Act
            for (int i = 0; i < 3; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    await limiter.WaitAsync();
                    timestamps.Add(DateTime.UtcNow);
                }));
            }
            await Task.WhenAll(tasks);

            // Assert
            var orderedTimestamps = timestamps.OrderBy(t => t).ToList();
            for (int i = 1; i < orderedTimestamps.Count; i++)
            {
                var diff = orderedTimestamps[i] - orderedTimestamps[i - 1];
                Assert.True(diff >= waitTime * 0.9); // Allow 10% margin
            }
        }

        [Fact]
        public async Task WaitAsync_CallAfterTimeSpan_ReturnsImmediately()
        {
            // Arrange
            var limiter = new RateLimiter(TimeSpan.FromSeconds(1));
            await limiter.WaitAsync();
            await Task.Delay(1500); // Wait longer than timespan

            // Act
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            await limiter.WaitAsync();
            stopwatch.Stop();

            // Assert
            Assert.True(stopwatch.ElapsedMilliseconds < 100); // Should return almost immediately
        }

        [Fact]
        public async Task WaitAsync_Timeout_ThrowsTimeoutExceptionAndLogsWarning()
        {
            // Arrange
            var console = new TestConsole();
            var testSemaphore = new SemaphoreSlim(0, 1);
            var rateLimiter = new RateLimiter(TimeSpan.FromMilliseconds(1), true, console, testSemaphore, TimeSpan.FromMilliseconds(10));
            var cts = new CancellationTokenSource();

            // Act & Assert
            var ex = await Assert.ThrowsAsync<TimeoutException>(() =>
                rateLimiter.WaitAsync(cts.Token)
            );
            Assert.Contains("Failed to acquire rate limiter semaphore within timeout", ex.Message);
            Assert.Contains(console.Output, line => line.Contains("Failed to acquire semaphore within timeout"));
        }

        [Fact]
        public async Task WaitAsync_WhenCancelled_ThrowsOperationCanceledExceptionAndLogsWarning()
        {
            // Arrange
            var console = new TestConsole();
            var testSemaphore = new SemaphoreSlim(0, 1); // No available space
            var rateLimiter = new RateLimiter(TimeSpan.FromSeconds(1), true, console, testSemaphore);
            var cts = new CancellationTokenSource();

            // Start the wait in a separate task
            var waitTask = rateLimiter.WaitAsync(cts.Token);

            // Cancel token after a short while
            cts.CancelAfter(10);

            // Act & Assert
            var ex = await Assert.ThrowsAsync<OperationCanceledException>(() => waitTask);
            Assert.Contains(console.Output, line => line.Contains("Rate limiter operation was cancelled"));
        }

        [Fact]
        public async Task WaitAsync_WhenDelayCancelled_ThrowsOperationCanceledExceptionAndLogsWarning()
        {
            // Arrange
            var console = new TestConsole();
            var rateLimiter = new RateLimiter(TimeSpan.FromSeconds(1), true, console);
            var cts = new CancellationTokenSource();

            // First call goes through directly
            await rateLimiter.WaitAsync();

            // Start the wait in a separate task (now we're in the delay)
            var waitTask = rateLimiter.WaitAsync(cts.Token);

            // Cancel token during delay
            cts.CancelAfter(10);

            // Act & Assert
            var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waitTask);
            Assert.Contains(console.Output, line => line.Contains("Rate limit delay was cancelled"));
        }

        [Fact]
        public void Dispose_CanBeCalledMultipleTimes()
        {
            var limiter = new RateLimiter(TimeSpan.FromSeconds(1));
            limiter.Dispose();
            limiter.Dispose(); // Should not throw
        }
    }
} 