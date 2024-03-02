using Nexus.DataModel;

namespace Nexus.Extensibility.Tests
{
    public class DataModelFixture
    {
        public DataModelFixture()
        {
            // catalogs
            Catalog0_V0 = new ResourceCatalogBuilder(id: "/A/B/C")
                .WithProperty("C_0_A", "A_0")
                .WithProperty("C_0_B", "B_0")
                .Build(); ;

            Catalog0_V1 = new ResourceCatalogBuilder(id: "/A/B/C")
                .WithProperty("C_0_A", "A_1")
                .WithProperty("C_0_C", "C_0")
                .Build();

            Catalog0_V2 = new ResourceCatalogBuilder(id: "/A/B/C")
                .WithProperty("C_0_C", "C_0")
                .Build();

            Catalog0_Vmerged = new ResourceCatalogBuilder(id: "/A/B/C")
                .WithProperty("C_0_A", "A_1")
                .WithProperty("C_0_B", "B_0")
                .WithProperty("C_0_C", "C_0")
                .Build();

            Catalog0_Vxor = new ResourceCatalogBuilder(id: "/A/B/C")
                .WithProperty("C_0_A", "A_0")
                .WithProperty("C_0_B", "B_0")
                .WithProperty("C_0_C", "C_0")
                .Build();

            // resources
            Resource0_V0 = new ResourceBuilder(id: "Resource0")
                .WithUnit("U_0")
                .WithDescription("D_0")
                .WithProperty("R_0_A", "A_0")
                .WithProperty("R_0_B", "B_0")
                .Build();

            Resource0_V1 = new ResourceBuilder(id: "Resource0")
                .WithUnit("U_1")
                .WithDescription("D_1")
                .WithGroups("G_1")
                .WithProperty("R_0_A", "A_1")
                .WithProperty("R_0_C", "C_0")
                .Build();

            Resource0_V2 = new ResourceBuilder(id: "Resource0")
                .WithGroups("G_1")
                .WithProperty("R_0_C", "C_0")
                .Build();

            Resource0_Vmerged = new ResourceBuilder(id: "Resource0")
                .WithUnit("U_1")
                .WithDescription("D_1")
                .WithProperty("R_0_A", "A_1")
                .WithProperty("R_0_B", "B_0")
                .WithGroups("G_1")
                .WithProperty("R_0_C", "C_0")
                .Build();

            Resource0_Vxor = new ResourceBuilder(id: "Resource0")
                .WithUnit("U_0")
                .WithDescription("D_0")
                .WithProperty("R_0_A", "A_0")
                .WithProperty("R_0_B", "B_0")
                .WithGroups("G_1")
                .WithProperty("R_0_C", "C_0")
                .Build();

            Resource1_V0 = new ResourceBuilder(id: "Resource1")
                .WithUnit("U_0")
                .WithDescription("D_0")
                .WithGroups("G_0")
                .WithProperty("R_1_A", "A_0")
                .WithProperty("R_1_B", "B_0")
                .Build();

            Resource2_V0 = new ResourceBuilder(id: "Resource2")
                .WithUnit("U_0")
                .WithDescription("D_0")
                .WithGroups("G_0")
                .WithProperty("R_2_A", "A_0")
                .WithProperty("R_2_B", "B_0")
                .Build();

            Resource3_V0 = new Resource(id: "Resource3");
            Resource3_V1 = Resource3_V0;
            Resource3_Vmerged = Resource3_V0;

            Resource4_V0 = new Resource(id: "Resource4");
            Resource4_V1 = Resource4_V0;
            Resource4_Vmerged = Resource4_V0;

            // representations
            Representation0_V0 = new Representation(
                dataType: NexusDataType.FLOAT32,
                samplePeriod: TimeSpan.FromMinutes(10));

            Representation0_V1 = Representation0_V0;

            Representation0_Vmerged = Representation0_V0;

            Representation0_Vxor = Representation0_V0;

            Representation1_V0 = new Representation(
               dataType: NexusDataType.FLOAT64,
               samplePeriod: TimeSpan.FromMinutes(20));

            Representation2_V0 = new Representation(
               dataType: NexusDataType.UINT16,
               samplePeriod: TimeSpan.FromMinutes(100));
        }

        public ResourceCatalog Catalog0_V0 { get; }
        public ResourceCatalog Catalog0_V1 { get; }
        public ResourceCatalog Catalog0_V2 { get; }
        public ResourceCatalog Catalog0_Vmerged { get; }
        public ResourceCatalog Catalog0_Vxor { get; }

        public Resource Resource0_V0 { get; }
        public Resource Resource0_V1 { get; }
        public Resource Resource0_V2 { get; }
        public Resource Resource0_Vmerged { get; }
        public Resource Resource0_Vxor { get; }
        public Resource Resource1_V0 { get; }
        public Resource Resource2_V0 { get; }
        public Resource Resource3_V0 { get; }
        public Resource Resource3_V1 { get; }
        public Resource Resource3_Vmerged { get; }
        public Resource Resource4_V0 { get; }
        public Resource Resource4_V1 { get; }
        public Resource Resource4_Vmerged { get; }

        public Representation Representation0_V0 { get; }
        public Representation Representation0_V1 { get; }
        public Representation Representation0_Vmerged { get; }
        public Representation Representation0_Vxor { get; }
        public Representation Representation1_V0 { get; }
        public Representation Representation2_V0 { get; }
    }
}
