using Nexus.Core;
using Xunit;

namespace Other
{
    public class CacheEntryWrapperTests
    {
        [Fact]
        public async Task CanRead()
        {
            // Arrange
            var expected = new double[] { 0, 2.2, 3.3, 4.4, 0, 6.6 };

            var fileBegin = new DateTime(2020, 01, 01, 0, 0, 0, DateTimeKind.Utc);
            var filePeriod = TimeSpan.FromDays(1);
            var samplePeriod = TimeSpan.FromHours(3);

            var cachedIntervals = new[]
            {
                new Interval(new DateTime(2020, 01, 01, 6, 0, 0, DateTimeKind.Utc), new DateTime(2020, 01, 01, 15, 0, 0, DateTimeKind.Utc)),
                new Interval(new DateTime(2020, 01, 01, 18, 0, 0, DateTimeKind.Utc), new DateTime(2020, 01, 01, 21, 0, 0, DateTimeKind.Utc))
            };

            var stream = new MemoryStream();
            var writer = new BinaryWriter(stream);

            for (int i = 0; i < 8; i++)
            {
                writer.Write(i * 1.1);
            }

            CacheEntryWrapper.WriteCachedIntervals(stream, cachedIntervals);

            var wrapper = new CacheEntryWrapper(fileBegin, filePeriod, samplePeriod, stream);

            var begin = new DateTime(2020, 01, 01, 3, 0, 0, DateTimeKind.Utc);
            var end = new DateTime(2020, 01, 01, 21, 0, 0, DateTimeKind.Utc);
            var actual = new double[6];

            // Act
            var uncachedIntervals = await wrapper.ReadAsync(begin, end, actual, CancellationToken.None);

            // Assert
            var expected1 = new Interval(
                Begin: new DateTime(2020, 01, 01, 3, 0, 0, DateTimeKind.Utc),
                End: new DateTime(2020, 01, 01, 6, 0, 0, DateTimeKind.Utc));

            var expected2 = new Interval(
                Begin: new DateTime(2020, 01, 01, 15, 0, 0, DateTimeKind.Utc),
                End: new DateTime(2020, 01, 01, 18, 0, 0, DateTimeKind.Utc));

            Assert.Collection(uncachedIntervals,
                actual1 => Assert.Equal(expected1, actual1),
                actual2 => Assert.Equal(expected2, actual2));

            Assert.Equal(expected.Length, actual.Length);

            for (int i = 0; i < expected.Length; i++)
            {
                Assert.Equal(expected[i], actual[i], precision: 1);
            }
        }

        [Fact]
        public async Task CanWrite1()
        {
            var fileBegin = new DateTime(2020, 01, 01, 0, 0, 0, DateTimeKind.Utc);
            var filePeriod = TimeSpan.FromDays(1);
            var samplePeriod = TimeSpan.FromHours(3);

            var stream = new MemoryStream();

            var cachedIntervals = new[]
            {
                new Interval(new DateTime(2020, 01, 01, 6, 0, 0, DateTimeKind.Utc), new DateTime(2020, 01, 01, 15, 0, 0, DateTimeKind.Utc)),
                new Interval(new DateTime(2020, 01, 01, 18, 0, 0, DateTimeKind.Utc), new DateTime(2020, 01, 01, 21, 0, 0, DateTimeKind.Utc))
            };

            stream.Seek(8 * sizeof(double), SeekOrigin.Begin);
            CacheEntryWrapper.WriteCachedIntervals(stream, cachedIntervals);

            // Arrange
            var wrapper = new CacheEntryWrapper(fileBegin, filePeriod, samplePeriod, stream);

            var begin1 = new DateTime(2020, 01, 01, 3, 0, 0, DateTimeKind.Utc);
            var sourceBuffer1 = new double[2] { 88.8, 99.9 };

            var begin2 = new DateTime(2020, 01, 01, 15, 0, 0, DateTimeKind.Utc);
            var sourceBuffer2 = new double[1] { 66.6 };

            // Act
            await wrapper.WriteAsync(begin1, sourceBuffer1, CancellationToken.None);

            stream.Seek(8 * sizeof(double), SeekOrigin.Begin);
            var actualIntervals1 = CacheEntryWrapper.ReadCachedIntervals(stream);

            await wrapper.WriteAsync(begin2, sourceBuffer2, CancellationToken.None);

            stream.Seek(8 * sizeof(double), SeekOrigin.Begin);
            var actualIntervals2 = CacheEntryWrapper.ReadCachedIntervals(stream);

            // Assert
            var reader = new BinaryReader(stream);

            var expectedIntervals1 = new[]
            {
                new Interval(
                    Begin: new DateTime(2020, 01, 01, 3, 0, 0, DateTimeKind.Utc),
                    End: new DateTime(2020, 01, 01, 15, 0, 0, DateTimeKind.Utc)),

                new Interval(
                    Begin: new DateTime(2020, 01, 01, 18, 0, 0, DateTimeKind.Utc),
                    End: new DateTime(2020, 01, 01, 21, 0, 0, DateTimeKind.Utc))
            };

            Assert.True(expectedIntervals1.SequenceEqual(actualIntervals1));

            stream.Seek(1 * sizeof(double), SeekOrigin.Begin);
            Assert.Equal(88.8, reader.ReadDouble());
            Assert.Equal(99.9, reader.ReadDouble());

            var expectedIntervals2 = new[]
            {
                new Interval(
                    Begin: new DateTime(2020, 01, 01, 3, 0, 0, DateTimeKind.Utc),
                    End: new DateTime(2020, 01, 01, 21, 0, 0, DateTimeKind.Utc))
            };

            Assert.True(expectedIntervals2.SequenceEqual(actualIntervals2));

            stream.Seek(5 * sizeof(double), SeekOrigin.Begin);
            Assert.Equal(66.6, reader.ReadDouble());
        }

        [Fact]
        public async Task CanWrite2()
        {
            var fileBegin = new DateTime(2020, 01, 01, 0, 0, 0, DateTimeKind.Utc);
            var filePeriod = TimeSpan.FromDays(1);
            var samplePeriod = TimeSpan.FromHours(3);

            var stream = new MemoryStream();

            var cachedIntervals = new[]
            {
                new Interval(new DateTime(2020, 01, 01, 0, 0, 0, DateTimeKind.Utc), new DateTime(2020, 01, 01, 6, 0, 0, DateTimeKind.Utc)),
                new Interval(new DateTime(2020, 01, 01, 12, 0, 0, DateTimeKind.Utc), new DateTime(2020, 01, 01, 18, 0, 0, DateTimeKind.Utc))
            };

            stream.Seek(8 * sizeof(double), SeekOrigin.Begin);
            CacheEntryWrapper.WriteCachedIntervals(stream, cachedIntervals);

            // Arrange
            var wrapper = new CacheEntryWrapper(fileBegin, filePeriod, samplePeriod, stream);

            var begin1 = new DateTime(2020, 01, 01, 3, 0, 0, DateTimeKind.Utc);
            var sourceBuffer1 = new double[3] { 77.7, 88.8, 99.9 };

            var begin2 = new DateTime(2020, 01, 01, 18, 0, 0, DateTimeKind.Utc);
            var sourceBuffer2 = new double[2] { 66.6, 77.7 };

            // Act
            await wrapper.WriteAsync(begin1, sourceBuffer1, CancellationToken.None);

            stream.Seek(8 * sizeof(double), SeekOrigin.Begin);
            var actualIntervals1 = CacheEntryWrapper.ReadCachedIntervals(stream);

            await wrapper.WriteAsync(begin2, sourceBuffer2, CancellationToken.None);

            stream.Seek(8 * sizeof(double), SeekOrigin.Begin);
            var actualIntervals2 = CacheEntryWrapper.ReadCachedIntervals(stream);

            // Assert
            var reader = new BinaryReader(stream);

            var expectedIntervals1 = new[]
            {
                new Interval(
                    Begin: new DateTime(2020, 01, 01, 0, 0, 0, DateTimeKind.Utc),
                    End: new DateTime(2020, 01, 01, 18, 0, 0, DateTimeKind.Utc))
            };

            Assert.True(expectedIntervals1.SequenceEqual(actualIntervals1));

            stream.Seek(1 * sizeof(double), SeekOrigin.Begin);
            Assert.Equal(77.7, reader.ReadDouble());
            Assert.Equal(88.8, reader.ReadDouble());
            Assert.Equal(99.9, reader.ReadDouble());

            var expectedIntervals2 = new[]
            {
                new Interval(
                    Begin: new DateTime(2020, 01, 01, 0, 0, 0, DateTimeKind.Utc),
                    End: new DateTime(2020, 01, 02, 0, 0, 0, DateTimeKind.Utc))
            };

            Assert.True(expectedIntervals2.SequenceEqual(actualIntervals2));

            stream.Seek(6 * sizeof(double), SeekOrigin.Begin);
            Assert.Equal(66.6, reader.ReadDouble());
            Assert.Equal(77.7, reader.ReadDouble());
        }
    }
}