Sometimes Nexus crashes due to OOM. A dump has been created and analyzed to find the root cause of this:

```
> dumpheap -stat

...
7fdb4387b9d8  2.620       247.892 System.Int32[]
7fdb44b292d8  3.110       248.800 System.Signature
7fdb43808390  6.434       257.360 System.RuntimeType
7fdb44b2a0e8  4.069       390.624 System.Reflection.RuntimeParameterInfo
7fdb4380a508  2.445       473.648 System.Object[]
7fdb44b288e8  5.215       542.360 System.Reflection.RuntimeMethodInfo
7fdb442ca8b0 18.529       889.392 System.Text.StringBuilder
55ee449d4640 15.286     1.014.144 Free
7fdb43931038 31.823    31.415.824 System.String
7fdb439972d8 18.635    41.864.014 System.Char[]
7fdb44ad0dc8 10.838 9.910.785.807 System.Byte[]
Total 199.498 objects, 9.993.265.315 bytes

```

```
> dumpheap -type System.Byte[]

...
    7f9d04456e28     7fdb44ad0dc8             61 
    7f9d04456f18     7fdb44ad0dc8             61 
    7f9d04800040     7fdb44ad0dc8  1.154.400.024 
    7f9d4a800040     7fdb44ad0dc8  1.610.400.024 
    7f9daa800040     7fdb44ad0dc8  1.154.400.024 

Statistics:
          MT  Count     TotalSize Class Name
...
7fdb44b06050     64         5.632 System.Byte[][]
7fdb44ad0dc8 10.838 9.910.785.807 System.Byte[]
Total 10.927 objects, 9.910.793.175 bytes
```

```
> gcroot 7f9daa800040 

Found 0 unique roots.
```

Additionally, a forced `GC.Collect()` actually reduced to memory consumption to a very low level. This leads to the assumption that the large object heap (LOH) is fragmented (https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/large-object-heap).

# Plot heap content

`dotnet dump analyze core.dmp -c "dumpheap" -c exit > heap.txt`

Then run the python script memory-analysis.py. Red circles show free space. With the available dump none of the red circles were large, so heap fragmentation does not seem to be an issue here. 

# Future work

- Find out who is allocating these large byte arrays
- Find out why there is an OOM when a simple GC helps to free the allocated memory

# Batch Stream Backpressure

The v2 batch data stream endpoint uses one `System.IO.Pipelines.Pipe` per requested resource/channel. Each HTTP channel copies directly from its backend `PipeReader` to the response body writer.

The intended backpressure chain is:

```text
Data source/file read -> backend Pipe -> HTTP response flow control -> client reader
```

Reverse-proxy response buffering breaks this chain because the proxy, rather than the final client, becomes the immediate consumer and can accumulate streamed data in memory.

The server must not drain one resource pipe completely before reading the others. `DataSourceController.ReadAsync(...)` writes a chunk to every resource pipe and awaits flushes. If any channel is not consumed, its pipe can fill up and block the producer, which is the correct backpressure behavior only after the corresponding HTTP channel has attached.

For this reason, a v2 batch session starts the source read only after all expected channel requests have attached. If the session times out or a channel aborts, the whole session is canceled and all pipe/controller resources are cleaned up.
