using Nexus.DataModel;
using System.Text.Json;
using Xunit;

namespace Nexus.Extensibility.Tests
{
    public class DataModelTests : IClassFixture<DataModelFixture>
    {
        private readonly DataModelFixture _fixture;

        public DataModelTests(DataModelFixture fixture)
        {
            _fixture = fixture;
        }

        [Theory]

        // valid
        [InlineData("/a", true)]
        [InlineData("/_a", true)]
        [InlineData("/ab_c", true)]
        [InlineData("/a9_b/c__99", true)]

        // invalid
        [InlineData("", false)]
        [InlineData("/", false)]
        [InlineData("/a/", false)]
        [InlineData("/9", false)]
        [InlineData("a", false)]
        public void CanValidateCatalogId(string id, bool isValid)
        {
            if (isValid)
                _ = new ResourceCatalog(id: id);

            else
                Assert.Throws<ArgumentException>(() => new ResourceCatalog(id: id));
        }

        [Theory]

        // valid
        [InlineData("_temp", true)]
        [InlineData("temp", true)]
        [InlineData("Temp", true)]
        [InlineData("Temp_1", true)]

        // invalid
        [InlineData("", false)]
        [InlineData("1temp", false)]
        [InlineData("teßp", false)]
        [InlineData("ª♫", false)]
        [InlineData("tem p", false)]
        [InlineData("tem-p", false)]
        [InlineData("tem*p", false)]
        public void CanValidateResourceId(string id, bool isValid)
        {
            if (isValid)
                _ = new Resource(id: id);

            else
                Assert.Throws<ArgumentException>(() => new Resource(id: id));
        }

        [Theory]
        [InlineData("00:01:00", true)]
        [InlineData("00:00:00", false)]
        public void CanValidateRepresentationSamplePeriod(string samplePeriodString, bool isValid)
        {
            var samplePeriod = TimeSpan.Parse(samplePeriodString);

            if (isValid)
                _ = new Representation(
                    dataType: NexusDataType.FLOAT64,
                    samplePeriod: samplePeriod);

            else
                Assert.Throws<ArgumentException>(() => new Representation(
                    dataType: NexusDataType.FLOAT64,
                    samplePeriod: samplePeriod));
        }

        [Theory]
        [InlineData(30, true)]
        [InlineData(-1, false)]
        public void CanValidateRepresentationKind(int numericalKind, bool isValid)
        {
            var kind = (RepresentationKind)numericalKind;

            if (isValid)
                _ = new Representation(
                    dataType: NexusDataType.FLOAT64,
                    samplePeriod: TimeSpan.FromSeconds(1),
                    parameters: default,
                    kind: kind);

            else
                Assert.Throws<ArgumentException>(() => new Representation(
                    dataType: NexusDataType.FLOAT64,
                    samplePeriod: TimeSpan.FromSeconds(1),
                    parameters: default,
                    kind: kind));
        }

        [Theory]
        [InlineData(NexusDataType.FLOAT32, true)]
        [InlineData((NexusDataType)0, false)]
        [InlineData((NexusDataType)9999, false)]
        public void CanValidateRepresentationDataType(NexusDataType dataType, bool isValid)
        {
            if (isValid)
                _ = new Representation(
                     dataType: dataType,
                     samplePeriod: TimeSpan.FromSeconds(1));

            else
                Assert.Throws<ArgumentException>(() => new Representation(
                     dataType: dataType,
                     samplePeriod: TimeSpan.FromSeconds(1)));
        }

        [Theory]
        [InlineData("00:00:01", "MeanPolarDeg", "1_s_mean_polar_deg")]
        public void CanInferRepresentationId(string samplePeriodString, string kindString, string expected)
        {
            var kind = Enum.Parse<RepresentationKind>(kindString);
            var samplePeriod = TimeSpan.Parse(samplePeriodString);

            var representation = new Representation(
                dataType: NexusDataType.FLOAT32,
                samplePeriod: samplePeriod,
                parameters: default,
                kind: kind);

            var actual = representation.Id;

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void CanMergeCatalogs()
        {
            // arrange

            // prepare catalog 0
            var representation0_V0 = _fixture.Representation0_V0;
            var representation1_V0 = _fixture.Representation1_V0;
            var resource0_V0 = _fixture.Resource0_V0 with { Representations = new List<Representation>() { representation0_V0, representation1_V0 } };
            var resource1_V0 = _fixture.Resource1_V0 with { Representations = default };
            var resource3_V0 = _fixture.Resource3_V0 with { Representations = default };
            var resource4_V0 = _fixture.Resource4_V0 with { Representations = new List<Representation>() { representation0_V0, representation1_V0 } };
            var catalog0_V0 = _fixture.Catalog0_V0 with { Resources = new List<Resource>() { resource0_V0, resource1_V0, resource3_V0, resource4_V0 } };

            // prepare catalog 1
            var representation0_V1 = _fixture.Representation0_V1;
            var representation2_V0 = _fixture.Representation2_V0;
            var resource0_V1 = _fixture.Resource0_V1 with { Representations = new List<Representation>() { representation0_V1, representation2_V0 } };
            var resource2_V0 = _fixture.Resource2_V0 with { Representations = default };
            var resource3_V1 = _fixture.Resource3_V1 with { Representations = new List<Representation>() { representation0_V1, representation1_V0 } };
            var resource4_V1 = _fixture.Resource4_V1 with { Representations = default };
            var catalog0_V1 = _fixture.Catalog0_V1 with { Resources = new List<Resource>() { resource0_V1, resource2_V0, resource3_V1, resource4_V1 } };

            // prepare merged
            var representation0_Vnew = _fixture.Representation0_Vmerged;
            var resource0_Vnew = _fixture.Resource0_Vmerged with { Representations = new List<Representation>() { representation0_Vnew, representation1_V0, representation2_V0 } };
            var resource3_Vnew = _fixture.Resource3_Vmerged with { Representations = new List<Representation>() { representation0_V1, representation1_V0 } };
            var resource4_Vnew = _fixture.Resource4_Vmerged with { Representations = new List<Representation>() { representation0_V0, representation1_V0 } };
            var catalog0_Vnew = _fixture.Catalog0_Vmerged with { Resources = new List<Resource>() { resource0_Vnew, resource1_V0, resource3_Vnew, resource4_Vnew, resource2_V0 } };

            // act
            var catalog0_actual = catalog0_V0.Merge(catalog0_V1);

            // assert
            var expected = JsonSerializer.Serialize(catalog0_Vnew);
            var actual = JsonSerializer.Serialize(catalog0_actual);

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void CatalogMergeThrowsForNonMatchingIdentifiers()
        {
            // Arrange
            var catalog1 = new ResourceCatalog(id: "/C1");
            var catalog2 = new ResourceCatalog(id: "/C2");

            // Act
            void action() => catalog1.Merge(catalog2);

            // Assert
            Assert.Throws<ArgumentException>(action);
        }

        [Fact]
        public void CatalogConstructorThrowsForNonUniqueResource()
        {
            // Act
            static void action()
            {
                var catalog = new ResourceCatalog(
                    id: "/C",
                    resources: new List<Resource>()
                    {
                        new Resource(id: "R1"),
                        new Resource(id: "R2"),
                        new Resource(id: "R2")
                    });
            }

            // Assert
            Assert.Throws<ArgumentException>(action);
        }

        [Fact]
        public void ResourceMergeThrowsForNonEqualRepresentations()
        {
            // Arrange
            var resource1 = new Resource(
                id: "myresource",
                representations: new List<Representation>()
                {
                    new Representation(dataType: NexusDataType.FLOAT32, samplePeriod: TimeSpan.FromSeconds(1))
                });

            var resource2 = new Resource(
                id: "myresource",
                representations: new List<Representation>()
                {
                    new Representation(dataType: NexusDataType.FLOAT64, samplePeriod: TimeSpan.FromSeconds(1)),
                    new Representation(dataType: NexusDataType.FLOAT32, samplePeriod: TimeSpan.FromSeconds(2)),
                    new Representation(dataType: NexusDataType.FLOAT32, samplePeriod: TimeSpan.FromSeconds(3))
                });

            // Act
            void action() => resource1.Merge(resource2);

            // Assert
            Assert.Throws<Exception>(action);
        }

        [Fact]
        public void ResourceMergeThrowsForNonMatchingIdentifiers()
        {
            // Arrange
            var resource1 = new Resource(id: "R1");
            var resource2 = new Resource(id: "R2");

            // Act
            void action() => resource1.Merge(resource2);

            // Assert
            Assert.Throws<ArgumentException>(action);
        }

        [Fact]
        public void CanFindCatalogItem()
        {
            var representation = new Representation(
                dataType: NexusDataType.FLOAT32,
                samplePeriod: TimeSpan.FromSeconds(1));

            var resource = new Resource(id: "Resource1", representations: new List<Representation>() { representation });
            var catalog = new ResourceCatalog(id: "/A/B/C", resources: new List<Resource>() { resource });

            var catalogItem = new CatalogItem(
                catalog with { Resources = default },
                resource with { Representations = default },
                representation,
                Parameters: default);

            var foundCatalogItem = catalog.Find(catalogItem.ToPath());

            Assert.Equal(catalogItem, foundCatalogItem);
        }

        [Fact]
        public void CanTryFindCatalogItem()
        {
            var representation = new Representation(
                dataType: NexusDataType.FLOAT32,
                samplePeriod: TimeSpan.FromSeconds(1));

            var resource = new Resource(id: "Resource1", representations: new List<Representation>() { representation });
            var catalog = new ResourceCatalog(id: "/A/B/C", resources: new List<Resource>() { resource });

            var catalogItem = new CatalogItem(
                catalog with { Resources = default },
                resource with { Representations = default },
                representation,
                Parameters: default);

            _ = DataModelUtilities.TryParseResourcePath(catalogItem.ToPath(), out var parseResult);
            var success = catalog.TryFind(parseResult!, out var foundCatalogItem1);

            Assert.Equal(catalogItem, foundCatalogItem1);
            Assert.True(success);
        }

        [Theory]
        [InlineData("/A/B/C/Resource1/1_s(param1=2)")]
        [InlineData("/A/B/C/Resource2/1_s")]
        [InlineData("/A/B/D/Resource1/1_s")]
        [InlineData("/A/B/D/Resource1/10_s#base=2_s")]
        public void ThrowsForInvalidResourcePath(string resourcePath)
        {
            var representation = new Representation(
               dataType: NexusDataType.FLOAT32,
               samplePeriod: TimeSpan.FromSeconds(1),
               kind: RepresentationKind.Original,
               parameters: default);

            var resource = new Resource(id: "Resource1", representations: new List<Representation>() { representation });
            var catalog = new ResourceCatalog(id: "/A/B/C", resources: new List<Resource>() { resource });
            var catalogItem = new CatalogItem(catalog, resource, representation, Parameters: default);

            void action() => catalog.Find(resourcePath);
            Assert.Throws<Exception>(action);
        }
    }
}