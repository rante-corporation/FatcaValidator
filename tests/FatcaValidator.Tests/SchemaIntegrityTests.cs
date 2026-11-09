using System.Security.Cryptography;
using FatcaValidator.Core;

namespace FatcaValidator.Tests;

/// <summary>The embedded schema files must stay byte-for-byte identical to the official ones (see Schemas/README.md).</summary>
public sealed class SchemaIntegrityTests
{
    private static readonly Dictionary<string, string> OfficialSha256 = new(StringComparer.Ordinal)
    {
        ["Schemas/Fatca/v2.0.1/FatcaXML_v2.0.1.xsd"] = "bfab1daf0473aca250bf35831d58db3a16efb3804777cc8bb5313908ee5ba3d6",
        ["Schemas/Fatca/v2.0.1/isofatcatypes_v1.2.xsd"] = "8d3fd18488acac640d9bc86f643c935bf6e2a7ef7aab19e4e4f7b9c26c58c86a",
        ["Schemas/Fatca/v2.0.1/oecdtypes_v4.2.xsd"] = "9a93ec6136c2904dc331003b91944d2d711440079b7867975c02aa45d4a5225d",
        ["Schemas/Fatca/v2.0.1/stffatcatypes_v2.0.xsd"] = "3c2cebf3f19414d6e5998d4e09e59db4627f2d4ac074ec068557a76d5010521c",
        ["Schemas/Crs/v2.0/CrsXML_v2.0.xsd"] = "727a446aaad390de527b956ada05a1198943a5e2dc02c022c890aba3bd321bd1",
        ["Schemas/Crs/v2.0/CommonTypesFatcaCrs_v2.0.xsd"] = "a56b1b123d1bf560e3715def76a73a6f5e93329b6094510487380be95b4b6eff",
        ["Schemas/Crs/v2.0/FatcaTypes_v1.2.xsd"] = "f842eca4f038cf3ee574f0820197c1448de69bc8200f926bb9d5c809417ebb97",
        ["Schemas/Crs/v2.0/isocrstypes_v1.1.xsd"] = "aeed6deaf56cbf93415e8e17387f5b400add48ad573c462fb546cdd64f721281",
        ["Schemas/Crs/v2.0/oecdcrstypes_v5.0.xsd"] = "80d2eb328f6a68b8da15a27f06366cea088b321115d57ceb56dcc2106096bf4f",
        ["Schemas/Crs/v3.0/CrsXML_v3.0.xsd"] ="aa25e255e85f15cf46b889fda3c2df5c290456c8686cac2896b917a9e3bcf925",
        ["Schemas/Crs/v3.0/CommonTypesFatcaCrs_v2.0.xsd"] = "882d6cde408e915fc52b6ace3549a5cf4ddec161024f30f89b3ba400acbe6a80",
        ["Schemas/Crs/v3.0/FatcaTypes_v1.2.xsd"] = "b099b2cdeccc3d5cc0e22f13f312b313e077a59f0b6432d1ce9e7e189c411c72",
        ["Schemas/Crs/v3.0/isocrstypes_v1.1.xsd"] = "aeed6deaf56cbf93415e8e17387f5b400add48ad573c462fb546cdd64f721281",
        ["Schemas/Crs/v3.0/oecdcrstypes_v5.0.xsd"] = "11db640c16a7202ca6274d1c6bf6f9134e9f4863af280927c15e9ae2baf47dac",
    };

    public static TheoryData<string> SchemaFiles => new(OfficialSha256.Keys);

    [Theory]
    [MemberData(nameof(SchemaFiles))]
    public void EmbeddedSchema_MatchesTheOfficialFile(string resourceName)
    {
        using Stream? stream = typeof(SchemaPackage).Assembly.GetManifestResourceStream(resourceName);

        Assert.NotNull(stream);
        Assert.Equal(OfficialSha256[resourceName], Convert.ToHexStringLower(SHA256.HashData(stream)));
    }

    [Fact]
    public void EveryEmbeddedSchema_HasAnOfficialHash()
    {
        IEnumerable<string> embedded = typeof(SchemaPackage).Assembly.GetManifestResourceNames()
            .Where(name => name.EndsWith(".xsd", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);

        Assert.Equal(OfficialSha256.Keys.Order(StringComparer.Ordinal), embedded);
    }
}
