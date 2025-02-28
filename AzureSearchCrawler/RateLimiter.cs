using System;
using System.Threading;
using System.Threading.Tasks;

namespace AzureSearchCrawler
{

    public class RateLimiter
    {
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private DateTime _lastCallTime = DateTime.MinValue;
        private readonly TimeSpan _minTimeBetweenCalls;
        private readonly bool _enabled;

        public RateLimiter(TimeSpan minTimeBetweenCalls, bool enabled = true)
        {
            _minTimeBetweenCalls = minTimeBetweenCalls;
            _enabled = enabled;
        }

        public async Task WaitAsync()
        {
            if (!_enabled) return;

            await _semaphore.WaitAsync();
            try
            {
                var timeSinceLastCall = DateTime.UtcNow - _lastCallTime;
                if (timeSinceLastCall < _minTimeBetweenCalls)
                {
                    await Task.Delay(_minTimeBetweenCalls - timeSinceLastCall);
                }
                _lastCallTime = DateTime.UtcNow;
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }
}