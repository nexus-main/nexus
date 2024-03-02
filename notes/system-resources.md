# How to avoid issues with Nexus being killed by the Linux OOM killer?

- The value of `GcInfo.TotalAvailableMemoryBytes` is equal to the available physical memory when the current container the process is running inside has no specific memory limit. If it has a limit, the value of `GcInfo.TotalAvailableMemoryBytes` will be equal to `75%` of that value as long as `System.GC.HeapHardLimitPercent` is not set [[learn.microsoft.com]](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/garbage-collector#heap-limit). This behavior has been confirmed by multiple tests using a simple memory allocation application and Docker Compose.

- When the application allocates memory which is not used immediately, the OS (Linux) commits the memory but does allocate it only when the associated memory page is accessed for the first time. This behavior has the advantage that an underutilization of physical RAM is avoided but the disadvantage is that the system may suddenly run out of memory when the commited memory is actually being used. Since this happens when the memory page is accessed for the first time, it is hard to determine which line of code will have a high chance of triggering the OOM killer of Linux.

- There is also the `GcInfo.HighMemoryLoadThresholdBytes` setting which is at 90 % of the physical memory. I don't know exactly why this value is not adapted to the value of `System.GC.HeapHardLimitPercent` [[github.com]](https://github.com/dotnet/runtime/issues/58974). Microsoft writes the following about the limit: *[...] for the dominant process on a machine with 64GB of memory, it's reasonable for GC to start reacting when there's 10% of memory available.* [[learn.microsoft.com]](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/garbage-collector#high-memory-percent). This may be an explanation why Nexus does not always OOM but only when there is more than 10% of the memory available and Nexus tries to allocat an array > 10 % of the available memory. This would prevent the GC from becoming active and the OS runs the OOM killer.

- On Unix-based OS, there is currently no low memory notification [[github.com]](https://github.com/dotnet/runtime/issues/6051).

- With the above mentioned memory limits set to a value less than the available physical memory (e.g. by using the `DOTNET_GCHeapHardLimit` variable), either the GC runs in time and the allocation succeeds or we get an `OutOfMemoryException` (I don't know why the GC is not always able to free enough memory) but the application stays alive reliably now.

- So the simplest solution to avoid OOM issues might be to
  - configure Docker Compose to apply a memory limit to the container (*Docker resource limits are built on top of cgroups, which is a Linux kernel capability* [[devblogs.microsoft.com/]](https://devblogs.microsoft.com/dotnet/using-net-and-docker-together-dockercon-2019-update/)),
  - use `MemoryPool<T>.Shared.Rent` whereever possible to avoid allocations and
  - catch `OutOfMemoryException` in potentially large array allocations to run the GC and retry once

More resources
- **.NET Core application running in docker gets OOMKilled if swapping is disabled** [[github.com]](https://github.com/dotnet/runtime/issues/851)

- **net5.0 console apps on linux don't show OutOfMemoryExceptions before being OOM-killed** - *It is not possible for .NET runtime to reliably throw OutOfMemoryException on Linux, unless you disable oom killer.
Note that average .NET process uses number of unmanaged libraries. .NET runtime does not have control over allocations 
done by these libraries. If the library happens to make an allocation that overshoots the memory killer limit, 
the Linux OS will happily make this allocation succeed, only to kill the process later.* [[github.com]](https://github.com/dotnet/runtime/issues/46147#issuecomment-747471498)