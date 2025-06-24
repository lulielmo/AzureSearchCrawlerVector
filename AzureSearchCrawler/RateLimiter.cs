using AzureSearchCrawler.Interfaces;
using AzureSearchCrawler.Models;
using System.Diagnostics;

namespace AzureSearchCrawler
{
    public class RateLimiter : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private readonly Stopwatch _stopwatch = new();
        private readonly TimeSpan _minTimeBetweenCalls;
        private readonly bool _enabled;
        private readonly IConsole? _console;
        private bool _isFirstCall = true;
        private bool _isDisposed;
        private DateTime _lastCallTime = DateTime.MinValue;
        private readonly TimeSpan _semaphoreTimeout;

        public RateLimiter(TimeSpan minTimeBetweenCalls, bool enabled = true, IConsole? console = null, SemaphoreSlim? semaphore = null, TimeSpan? semaphoreTimeout = null)
        {
            _minTimeBetweenCalls = minTimeBetweenCalls;
            _enabled = enabled;
            _console = console;
            _semaphore = semaphore ?? new SemaphoreSlim(1, 1);
            _semaphoreTimeout = semaphoreTimeout ?? TimeSpan.FromSeconds(10);
            _stopwatch.Start();
            _console?.WriteLine($"RateLimiter created with enabled={enabled}, minTimeBetweenCalls={minTimeBetweenCalls}", LogLevel.Debug);
        }

        public async Task WaitAsync(CancellationToken cancellationToken = default)
        {
            if (!_enabled) 
            {
                _console?.WriteLine("Rate limiter is disabled, returning immediately", LogLevel.Debug);
                return;
            }

            _console?.WriteLine($"Acquiring semaphore for rate limiting", LogLevel.Debug);
            
            try
            {
                // Try to acquire the semaphore with a timeout
                if (!await _semaphore.WaitAsync(_semaphoreTimeout, cancellationToken))
                {
                    _console?.WriteLine("Failed to acquire semaphore within timeout", LogLevel.Warning);
                    throw new TimeoutException("Failed to acquire rate limiter semaphore within timeout");
                }

                try
                {
                    if (_isFirstCall)
                    {
                        _isFirstCall = false;
                        _lastCallTime = DateTime.UtcNow;
                        _console?.WriteLine("First call, no delay needed", LogLevel.Debug);
                        return;
                    }

                    var timeSinceLastCall = DateTime.UtcNow - _lastCallTime;
                    if (timeSinceLastCall < _minTimeBetweenCalls)
                    {
                        var delayTime = _minTimeBetweenCalls - timeSinceLastCall;
                        _console?.WriteLine($"Waiting for {delayTime.TotalSeconds:F2} seconds before next call", LogLevel.Debug);
                        
                        try
                        {
                            await Task.Delay(delayTime, cancellationToken);
                        }
                        catch (OperationCanceledException)
                        {
                            _console?.WriteLine("Rate limit delay was cancelled", LogLevel.Warning);
                            throw;
                        }
                    }

                    _lastCallTime = DateTime.UtcNow;
                }
                finally
                {
                    _semaphore.Release();
                }
            }
            catch (OperationCanceledException)
            {
                _console?.WriteLine("Rate limiter operation was cancelled", LogLevel.Warning);
                throw;
            }
            catch (Exception ex)
            {
                _console?.WriteLine($"Unexpected error in rate limiter: {ex.Message}", LogLevel.Error);
                throw;
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _semaphore.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}