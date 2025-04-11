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
    }
} 