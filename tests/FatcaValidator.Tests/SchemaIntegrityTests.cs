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
        ["Schemas/FatcaNotification/v2.5/BASE-FATCA-NOTIFICATION-2.5.xsd"] = "abd48bfaa2b6ea60ead20f9526ffc89d66acceb896f758908812209e14a15533",
        ["Schemas/FatcaNotification/v2.5/FATCA-FILE-ERROR-NOTIFICATION-2.5.xsd"] = "23d346d8d59948b07b1ee293fa5f9bcd28d0a8697848d470e2ef0d46fb611e4e",
        ["Schemas/FatcaNotification/v2.5/FATCA-VALID-FILE-NOTIFICATION-2.5.xsd"] = "706bfb296b1943a7ca02e3e98f8e551845778682b17fb6448b83f67dbdf463dd",
        ["Schemas/FatcaMetadata/v1.3/FATCA-IDES-SenderFileMetadata-1.3.xsd"] = "bf7840fb1c40108acf555633418a9d31034920310194ffbbc13ef944565d5c4e",
        ["Schemas/Carf/v1.5/CARFXML_v1.5.xsd"] = "26d78b9dab464a8ceb80cf08359cfa180456e36f53c2f2cd2e32a6c86a2cc775",
        ["Schemas/Carf/v1.5/isocarftypes_v1.1.xsd"] = "1651976fcbb237085ed4a78305ae8609e708de4db5cc5c219e39f6b31ec511d7",
        ["Schemas/Carf/v1.5/oecdcarftypes_v5.0.xsd"] = "f7ca31e2128c935e1658bb76ddb609734485f778cc9dd16901b713cbe718b8ed",
        ["Schemas/Cbc/v2.0/CbcXML_v2.0.xsd"] = "305bfbaa2211f38cb192521e896be7c3fc9b20cf51f3c79cdc35d80d1f8897cd",
        ["Schemas/Cbc/v2.0/isocbctypes_v1.1.xsd"] = "4d363f553c9dc0aad250ef8a3997af7dcec00a74484792a04ddaa190ace6318b",
        ["Schemas/Cbc/v2.0/oecdcbctypes_v5.0.xsd"] = "81315b00e934aee2442119e20acbc70f11001588ed9eb2005dc747abcbb2bd15",
        ["Schemas/CrsStatusMessage/v2.0/CrsStatusMessageXML_v2.0.xsd"] = "2a35afa5f0c0100ab0d39bc6c9e310b4cdb0a9f3100588e8ec6a37e2f61dc3a5",
        ["Schemas/CrsStatusMessage/v2.0/isocsmtypes_v1.1.xsd"] = "46bd8a8d3622806fe6c84c8a3e1d494f0e0dbf82224822f64de0559858ffd1f9",
        ["Schemas/CbcStatusMessage/v2.0/CbcStatusMessageXML_v2.0.xsd"] = "1a6613c5dbd7fb311e67222424e7516b061239c64e0baf45767e5e48875afb77",
        ["Schemas/CbcStatusMessage/v2.0/isostmtypes_v1.1.xsd"] = "e34628779c7db0dd3eca3a6b697407f2d9406454e7ba95022fa196f8e51f09cf",
        ["Schemas/GenericStatusMessage/v2.0/GenericStatusMessageXML_v2.0.xsd"] = "f86a248cf15712f09520ccffa09daeedadd7deb852f45994b106be8f106e7d75",
        ["Schemas/GenericStatusMessage/v2.0/isogsmtypes_v1.0.xsd"] = "7769d23227d6cbbd78a6f38cc1037c56b57a05ed6609f2f727dffd8e976c844e",
        ["Schemas/Etr/v2.0/EtrXML_v2.0.xsd"] = "484cde01176c703c56d4b8cf5372594c45b46a917e03672cef8ebf47c7e364cf",
        ["Schemas/Etr/v2.0/isoetrtypes_v1.1.xsd"] = "4fce9091a99450864bfa8fa41d8e6732d7aa472c65b9cdc675a9b345cebf9fcc",
        ["Schemas/Etr/v2.0/oecdetrtypes_v5.0.xsd"] = "b9aca8fc69f6e80aa1db826bdc8553a5a19a756fcaa4cd07ccafbe66109e78ed",
        ["Schemas/Etr/v3.0/EtrXML_v3.0.xsd"] = "d5f4ee3f299f559c1f19ffb090f5a20857df637a0aa601f520b0cdc569ccc253",
        ["Schemas/Etr/v3.0/isoetrtypes_v1.1.xsd"] = "e5218143803bc360a75bf64376d87ecb4e878fb9c353a6584faff50def7fa9d4",
        ["Schemas/Etr/v3.0/oecdetrtypes_v5.0.xsd"] = "817bcce4287ffe242c59de4de844ac587cae74466339f1c2f3a377fd9bb35065",
        ["Schemas/EtrStatusMessage/v2.0/EtrStatusMessageXML_v2.0.xsd"] = "411f7eb38a01f1036390b9046f90eba3ff5f12eb2b638db10ccb833c839c9074",
        ["Schemas/EtrStatusMessage/v2.0/isostmtypes_v1.1.xsd"] = "f4d247898419fb3cd4db4217796a38f2017151a5777b45e6b26712a74ee7d639",
        ["Schemas/Mdr/v1.0/IsoMdrTypes_v1.0.xsd"] = "9e36f7f3f8395ad6b1a5156057bc75b40869447fae92c6d1f97f3c97e5215141",
        ["Schemas/Mdr/v1.0/MdrXML_v1.0.xsd"] = "30436dea212b5ea021ebe543faf90fb0288dbd8d9ff0b77201bc1f5e03f239f6",
        ["Schemas/Mdr/v1.0/OecdMdrTypes_v1.0.xsd"] = "46c4c5943ab50741ae45d73f9924f68aaf85eb3326d18a884b276079083f525e",
        ["Schemas/Ntj/v1.0/IsoNtjTypes_v1.0.xsd"] = "7bbaeba9688d688b59401b5cf9e025bfd02e76a1e3abf8d0b430bde438135c7d",
        ["Schemas/Ntj/v1.0/NtjXML_v1.0.xsd"] = "4709fc578f661bc6488f341ffdc68f93d16e8fd03d160c84e9f160c233905eaf",
        ["Schemas/Ntj/v1.0/OecdNtjTypes_v1.0.xsd"] = "7fcbaa5461df867e6eb987d12993d670edfe0ff670cd2918ace0f9151b724782",
        ["Schemas/Stf/v2.2/isotypes_v2.2.xsd"] = "3f9752b4d23ea655d66a84f3007242fd7fd20c6808aee7231288b72f21aea57a",
        ["Schemas/Stf/v2.2/oecdtypes_v1.xsd"] = "46d90e83b6c8d7883bd1382aaec9485bd3abb96d411d0601579f2765aa299f41",
        ["Schemas/Stf/v2.2/specifictypes_v2.xsd"] = "788467258edd26e201296340ca206aa3e087d38cda7c453801a403f2eff6d7a8",
        ["Schemas/Stf/v2.2/stfdirect-2.2.xsd"] = "9264c3774276a8bc171ba8e26acd84a825187fbbb138796d60cde3a85073874f",
        ["Schemas/Stf/v2.2/stftypes-2.2.xsd"] = "9d1b6e7d6978bbb1aab9092b4981083bad0cbc9c9796c1da57be5cb5cdd69956",
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
