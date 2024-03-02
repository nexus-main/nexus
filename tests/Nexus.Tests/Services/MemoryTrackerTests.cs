using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nexus.Core;
using Nexus.Services;
using Xunit;

namespace Services
{
    public class MemoryTrackerTests
    {
        [Fact]
        public async Task CanHandleMultipleRequests()
        {
            // Arrange
            var weAreWaiting = new AutoResetEvent(initialState: false);
            var dataOptions = new DataOptions() { TotalBufferMemoryConsumption = 200 };
            
            var memoryTracker = new MemoryTracker(Options.Create(dataOptions), NullLogger<IMemoryTracker>.Instance)
            {
                // TODO: remove this property and test with factor 8
                Factor = 2
            };

            var firstRegistration = default(AllocationRegistration);
            var secondRegistration = default(AllocationRegistration);

            // Act
            var registrationsTask = Task.Run(async () =>
            {
                firstRegistration = await memoryTracker.RegisterAllocationAsync(minimumByteCount: 100, maximumByteCount: 100, CancellationToken.None);

                var firstWaitingTask = memoryTracker.RegisterAllocationAsync(minimumByteCount: 70, maximumByteCount: 70, CancellationToken.None);
                var secondWaitingTask = memoryTracker.RegisterAllocationAsync(minimumByteCount: 80, maximumByteCount: 80, CancellationToken.None);

                Assert.True(firstWaitingTask.Status != TaskStatus.RanToCompletion);
                Assert.True(secondWaitingTask.Status != TaskStatus.RanToCompletion);

                // dispose first registration
                weAreWaiting.Set();

                await Task.WhenAny(firstWaitingTask, secondWaitingTask);

                if (firstWaitingTask.Status == TaskStatus.RanToCompletion)
                    secondRegistration = await firstWaitingTask;

                else if (secondWaitingTask.Status == TaskStatus.RanToCompletion)
                    secondRegistration = await secondWaitingTask;

                Assert.True(secondRegistration is not null);

                // dispose second registration
                weAreWaiting.Set();

                await Task.WhenAll(firstWaitingTask, secondWaitingTask);
            });

            var ConsumingTask = Task.Run(() =>
            {
                weAreWaiting.WaitOne();
                firstRegistration!.Dispose();

                weAreWaiting.WaitOne();
                secondRegistration!.Dispose();
            });

            await registrationsTask;

            // Assert
        }
    }
}
