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
            Assert.True(stopwatch.ElapsedMilliseconds < 100); // Bör returnera nästan omedelbart
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
            Assert.True(stopwatch.ElapsedMilliseconds < 100); // Första anropet bör vara snabbt
        }

        [Fact]
        public async Task WaitAsync_SecondCallWithinTimeSpan_WaitsForRemaining()
        {
            // Arrange
            var waitTime = TimeSpan.FromSeconds(1);
            var limiter = new RateLimiter(waitTime);
            
            // Act
            await limiter.WaitAsync(); // Första anropet
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            await limiter.WaitAsync(); // Andra anropet
            stopwatch.Stop();

            // Assert
            Assert.True(stopwatch.ElapsedMilliseconds >= 900); // Minst 90% av väntetiden
            Assert.True(stopwatch.ElapsedMilliseconds <= 1200); // Max 20% över väntetiden
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
                Assert.True(diff >= waitTime * 0.9); // Tillåt 10% marginal
            }
        }

        [Fact]
        public async Task WaitAsync_CallAfterTimeSpan_ReturnsImmediately()
        {
            // Arrange
            var limiter = new RateLimiter(TimeSpan.FromSeconds(1));
            await limiter.WaitAsync();
            await Task.Delay(1500); // Vänta längre än timespan

            // Act
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            await limiter.WaitAsync();
            stopwatch.Stop();

            // Assert
            Assert.True(stopwatch.ElapsedMilliseconds < 100); // Bör returnera nästan omedelbart
        }
    }
} 