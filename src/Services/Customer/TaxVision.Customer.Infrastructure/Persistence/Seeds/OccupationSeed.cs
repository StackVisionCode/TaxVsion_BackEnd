namespace TaxVision.Customer.Infrastructure.Persistence.Seeds;

// Seed data exported from CustomerDB legacy on 2026-06-27.
// 171 occupations preserved with original Guid identifiers.
// 2026-11-18 (F6): las mal escritas quedan inactivas (hay clientes que las usan) y se agregan las ocupaciones
// de EE. UU. (BLS SOC 2018, en singular), militares y situaciones comunes. Ids nuevos: UUIDv5 del nombre.
internal static class OccupationSeed
{
    public static readonly object[] All =
    [
        new
        {
            Id = Guid.Parse("540522EA-87A5-427F-B119-ADEB650FACFC"),
            Name = "Account Maintenance Clerk",
            DisplayOrder = 0,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("F8CDFF15-0966-4402-9B9F-7B14485D09F9"),
            Name = "Account Opening Clerk",
            DisplayOrder = 1,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("AF3F743B-10F8-4A41-AEE5-8D672ECDB368"),
            Name = "Account Services Associate",
            DisplayOrder = 2,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("BE22FD76-1158-4A77-86BD-32CD7FA70C44"),
            Name = "Account Update Clerk",
            DisplayOrder = 3,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000002"),
            Name = "Accountant",
            DisplayOrder = 4,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("AD23B4EA-FFFD-4CF7-A42B-71BE556D2B49"),
            Name = "Amended Returns Analyst",
            DisplayOrder = 5,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("51421E80-E531-4DF7-92F6-408470960604"),
            Name = "Aml Compliance Analyst",
            DisplayOrder = 6,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("DA739CCB-AF63-4CB5-A3A8-B6BB8042ABB4"),
            Name = "Architect",
            DisplayOrder = 7,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("250D7308-075A-4479-9E3E-B41ECFECEC83"),
            Name = "Archtect",
            DisplayOrder = 8,
            IsActive = false,
        },
        new
        {
            Id = Guid.Parse("93F4DD29-93C6-44EA-A6AF-99CF4D29BCD3"),
            Name = "Attorney",
            DisplayOrder = 9,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("B9AFB09C-5A76-4095-B143-A499D23DD325"),
            Name = "Bank Manager",
            DisplayOrder = 10,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("5DE09840-B680-449E-4809-08DE70045A41"),
            Name = "Baseball Player",
            DisplayOrder = 11,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("95918E66-C31C-43AB-A052-E574E9BE60C7"),
            Name = "Blockchain Developer",
            DisplayOrder = 12,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("6DCD93F1-A068-4843-B963-C9433A4AB33E"),
            Name = "Bookkeeper",
            DisplayOrder = 13,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("BAED383B-8BDE-4EE6-811D-BAC7DD5416A3"),
            Name = "Business Analyst",
            DisplayOrder = 14,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("C70D291B-253E-4959-9B6B-C10F63F33040"),
            Name = "Busness Analyst",
            DisplayOrder = 15,
            IsActive = false,
        },
        new
        {
            Id = Guid.Parse("79475FEB-BFF4-407E-B32A-0285CB5A337E"),
            Name = "Busness Owner",
            DisplayOrder = 16,
            IsActive = false,
        },
        new
        {
            Id = Guid.Parse("CBB60008-0637-431D-A762-A4EBDD438E8E"),
            Name = "Case Intake Coordinator",
            DisplayOrder = 17,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("29D7B24F-CAD1-45B0-937B-5B254188C2E6"),
            Name = "Case Management Assistant",
            DisplayOrder = 18,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("F14368F1-8BCE-4BB7-B081-2423814A3C03"),
            Name = "Case Review Assistant",
            DisplayOrder = 19,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000013"),
            Name = "Cashier",
            DisplayOrder = 20,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("D19D4CED-D043-4F5A-AECF-DE1FE72F32FF"),
            Name = "Chargeback Analyst",
            DisplayOrder = 21,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000012"),
            Name = "Chef",
            DisplayOrder = 22,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("D3EF49DD-46AD-4DFA-B7FC-85326DE3A548"),
            Name = "Civil Engineer",
            DisplayOrder = 23,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("BDD9A6DD-DDAC-4B49-B0B7-20561E419042"),
            Name = "Cl Engneer",
            DisplayOrder = 24,
            IsActive = false,
        },
        new
        {
            Id = Guid.Parse("8C09882B-B79D-48C4-90F6-4B060A41BFF4"),
            Name = "Client Accounts Liaison",
            DisplayOrder = 25,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("5A021023-1030-40F9-891A-94EE100B726C"),
            Name = "Client Data Administrator",
            DisplayOrder = 26,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("0F6B811D-7BA3-48B0-8D1A-3FCAF1B091B6"),
            Name = "Client Data Coordinator",
            DisplayOrder = 27,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("7CCB9D31-3316-4A5D-8B19-E3CD1E158386"),
            Name = "Client Intake Specialist",
            DisplayOrder = 28,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("9D736DF9-9105-4A30-B333-737D6130A531"),
            Name = "Client Profile Reviewer",
            DisplayOrder = 29,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("99DB838E-4497-4F0A-9328-6E602BC5C964"),
            Name = "Client Records Supervisor",
            DisplayOrder = 30,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("1205BDB6-6C68-4A0F-B216-AD217E6705DC"),
            Name = "Client Services Specialist",
            DisplayOrder = 31,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("40AF0297-6CDB-4CC4-A4B4-F713736A6F47"),
            Name = "Cloud Architect",
            DisplayOrder = 32,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("60D14F21-EFCA-4A08-84BD-44686D7EE488"),
            Name = "Collections Supervisor",
            DisplayOrder = 33,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("35FD5C50-7DA4-469C-82F7-B0A54FE63038"),
            Name = "Compliance Case Reviewer",
            DisplayOrder = 34,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("A0FDA2E7-E6F0-4BE1-952E-1AA3973067C5"),
            Name = "Compliance Review Associate",
            DisplayOrder = 35,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("4DDBBD99-C59D-4CED-A1DD-FB3D127137DA"),
            Name = "Compliance Support Analyst",
            DisplayOrder = 36,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000008"),
            Name = "Construction Worker",
            DisplayOrder = 37,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("CD8818AC-1789-4933-BCF0-5B038B57B42D"),
            Name = "Constructon Manager",
            DisplayOrder = 38,
            IsActive = false,
        },
        new
        {
            Id = Guid.Parse("9AA08DB8-E6E3-4253-9C30-197FFBB9B3AD"),
            Name = "Consultant",
            DisplayOrder = 39,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("34D695BC-A11B-40F9-8EEB-374914C13C04"),
            Name = "Content Creator",
            DisplayOrder = 40,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000030"),
            Name = "Courier",
            DisplayOrder = 41,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("62A55DFA-DDD2-4740-9C17-26D2AC5FC556"),
            Name = "Credit File Analyst",
            DisplayOrder = 42,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("B997207B-88AD-41AD-9350-A886AE770D3D"),
            Name = "Cryptocurrency Analyst",
            DisplayOrder = 43,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("EA0D0E04-2BD2-460D-8C96-F560E98922A3"),
            Name = "Customer Care Representative",
            DisplayOrder = 44,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("1F158248-5816-482C-9BD3-4AFABC3ACBF2"),
            Name = "Customer Contact Agent",
            DisplayOrder = 45,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("230A1138-37C2-4491-8BA5-704095D0D08B"),
            Name = "Customer Portfolio Assistant",
            DisplayOrder = 46,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("9CD388DE-7454-49A9-BE03-17EDC88C4012"),
            Name = "Customer Resolution Agent",
            DisplayOrder = 47,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("C73F7570-BD26-48EC-BEEA-813B698A90BE"),
            Name = "Customer Verification Agent",
            DisplayOrder = 48,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("1F516863-BFE9-4D66-B366-DE39556E8BB0"),
            Name = "Cybersecurity Analyst",
            DisplayOrder = 49,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("D8D3148A-BF39-4DD8-9965-77306369776C"),
            Name = "Data Analyst",
            DisplayOrder = 50,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("A671026E-EECB-40DC-BF9A-4384B51CABC1"),
            Name = "Data Science Consultant",
            DisplayOrder = 51,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("CA1D6C4E-F83F-4596-BF84-B60C77A4F7B8"),
            Name = "Database Admnstrator",
            DisplayOrder = 52,
            IsActive = false,
        },
        new
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000029"),
            Name = "Delivery Driver",
            DisplayOrder = 53,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000024"),
            Name = "Dentist",
            DisplayOrder = 54,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("880AE200-7490-4A9C-A277-FE13339793F7"),
            Name = "Dentst",
            DisplayOrder = 55,
            IsActive = false,
        },
        new
        {
            Id = Guid.Parse("F9D800D1-460D-4809-A022-2245DC9F0D35"),
            Name = "Devops Engineer",
            DisplayOrder = 56,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("6C356892-21AB-40A2-AB1F-3BB853A03D30"),
            Name = "Digital Banking Support",
            DisplayOrder = 57,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("29024611-42E2-4305-A950-57401C64826C"),
            Name = "Digital Filing Operator",
            DisplayOrder = 58,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("C77D4DC4-3F5D-4016-BE4A-7E5BA8B5041F"),
            Name = "Digital Filing Specialist",
            DisplayOrder = 59,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("4FD68DEA-3E2A-4EF5-9248-15219E7C36B5"),
            Name = "Digital Support Coordinator",
            DisplayOrder = 60,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("2A0BDEB1-D640-4D8B-8C88-74B4DBCE6FBA"),
            Name = "Digital Support Operator",
            DisplayOrder = 61,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("6EEE34EA-B78E-40D0-A68A-2F5368515E03"),
            Name = "Dispute Resolution Specialist",
            DisplayOrder = 62,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000005"),
            Name = "Doctor",
            DisplayOrder = 63,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("8792E038-8597-4E24-92AD-509E3D5F62D1"),
            Name = "Document Review Specialist",
            DisplayOrder = 64,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("04C1BD39-3E57-4C63-8A83-628ED8A6DB55"),
            Name = "Documentation Analyst",
            DisplayOrder = 65,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("6D35EDBF-D044-4524-B827-272C90B00100"),
            Name = "Electrcan",
            DisplayOrder = 66,
            IsActive = false,
        },
        new
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000006"),
            Name = "Electrician",
            DisplayOrder = 67,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000021"),
            Name = "Engineer",
            DisplayOrder = 68,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("B68E71B4-30E4-4C72-8BA7-99FFEF8D8F35"),
            Name = "Executive Assistant",
            DisplayOrder = 69,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("9EF8711E-31F2-4219-A2D3-6C5BB9DDB087"),
            Name = "Financial Advisor",
            DisplayOrder = 70,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("B0CDACAF-8D8B-459E-98DB-1AED6EEBC849"),
            Name = "Financial Analyst",
            DisplayOrder = 71,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000010"),
            Name = "Firefighter",
            DisplayOrder = 72,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("6CE0F3F5-C967-4BC9-9528-9F50F105FB60"),
            Name = "Flght Attendant",
            DisplayOrder = 73,
            IsActive = false,
        },
        new
        {
            Id = Guid.Parse("66F514F0-3D77-4D5F-ABE3-FD6188A64CCB"),
            Name = "Fnancal Analyst",
            DisplayOrder = 74,
            IsActive = false,
        },
        new
        {
            Id = Guid.Parse("3CE58E23-1465-4014-B270-0B784A91B51E"),
            Name = "Fraud Monitoring Analyst",
            DisplayOrder = 75,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000031"),
            Name = "Freelancer",
            DisplayOrder = 76,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("D1BEA2A6-9435-474A-8710-CC89A398D724"),
            Name = "Frefghter",
            DisplayOrder = 77,
            IsActive = false,
        },
        new
        {
            Id = Guid.Parse("C69E3A1A-8604-4575-A118-30AEE4DDD772"),
            Name = "Full Stack Developer",
            DisplayOrder = 78,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("869CBBF0-18F2-42CA-9497-D26470DB3F73"),
            Name = "Graphc Desgner",
            DisplayOrder = 79,
            IsActive = false,
        },
        new
        {
            Id = Guid.Parse("F501AD68-D8AB-4767-A6EE-F038BE3AF179"),
            Name = "Graphic Designer",
            DisplayOrder = 80,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000016"),
            Name = "Hairdresser",
            DisplayOrder = 81,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("F7272CEC-5B78-428E-480A-08DE70045A41"),
            Name = "Hookie Player",
            DisplayOrder = 82,
            IsActive = false,
        },
        new
        {
            Id = Guid.Parse("2A8557F3-DF3E-4475-96D3-1B8DA47BF910"),
            Name = "Hr Manager",
            DisplayOrder = 83,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("126EFA61-E27E-4854-9909-E1E7571845A0"),
            Name = "Human Resources Manager",
            DisplayOrder = 84,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000018"),
            Name = "Janitor",
            DisplayOrder = 85,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("11F5E518-9DBD-4107-B8B5-90149CBFFB60"),
            Name = "Kyc Specialist",
            DisplayOrder = 86,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000023"),
            Name = "Lawyer",
            DisplayOrder = 87,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("6B0E2409-3BAC-4388-895E-0ED8212DC39A"),
            Name = "Loan Offcer",
            DisplayOrder = 88,
            IsActive = false,
        },
        new
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000027"),
            Name = "Lyft Driver",
            DisplayOrder = 89,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("DC3EE3D1-AAC1-418A-8D07-D5999C04A1F3"),
            Name = "Machine Learning Engineer",
            DisplayOrder = 90,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("C48AF945-F7A6-4778-90DD-2C36861EAB64"),
            Name = "Marketing Manager",
            DisplayOrder = 91,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("02627968-354B-4F39-AF1F-BBBEFF854973"),
            Name = "Marketng Drector",
            DisplayOrder = 92,
            IsActive = false,
        },
        new
        {
            Id = Guid.Parse("C662D9B7-B481-4515-A3FD-F89B87D946F1"),
            Name = "Mechancal Engneer",
            DisplayOrder = 93,
            IsActive = false,
        },
        new
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000017"),
            Name = "Mechanic",
            DisplayOrder = 94,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("E619079B-6776-4346-902C-F3F5DC3A78EC"),
            Name = "Network Engneer",
            DisplayOrder = 95,
            IsActive = false,
        },
        new
        {
            Id = Guid.Parse("339C0A1A-995D-47D5-8D7F-D599859F65D5"),
            Name = "Nsurance Agent",
            DisplayOrder = 96,
            IsActive = false,
        },
        new
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000004"),
            Name = "Nurse",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("55A56267-BF19-468B-B039-8506F576D366"),
            Name = "Nurse Practitioner",
            DisplayOrder = 98,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("04D134EC-C0E1-41FE-B0FB-8381E5BDE282"),
            Name = "Nvestment Banker",
            DisplayOrder = 99,
            IsActive = false,
        },
        new
        {
            Id = Guid.Parse("B220312E-6A1E-431B-9B4E-A7E7FB8EA3F2"),
            Name = "Onboarding Analyst",
            DisplayOrder = 100,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("2B6C9B8D-16D6-438E-B962-C74F98C0B533"),
            Name = "Onboarding Specialist",
            DisplayOrder = 101,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("C7E5E28A-F610-4624-AF27-5A148A374BE7"),
            Name = "Operations Manager",
            DisplayOrder = 102,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("62DF2473-1FC0-432B-BCB0-D2022A6B3E05"),
            Name = "Operatons Manager",
            DisplayOrder = 103,
            IsActive = false,
        },
        new
        {
            Id = Guid.Parse("9CC5E653-0954-4CB0-97DB-BACBDC543474"),
            Name = "Paramedc",
            DisplayOrder = 104,
            IsActive = false,
        },
        new
        {
            Id = Guid.Parse("AF97C99C-3996-4820-B1E5-80F25E021B64"),
            Name = "Payment Operations Specialist",
            DisplayOrder = 105,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("6C0FAD92-114F-4B5D-9D17-B9CD2C7228A3"),
            Name = "Pharmacist",
            DisplayOrder = 106,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("2B4A3EC2-4F69-4921-BEAC-E6F387379722"),
            Name = "Pharmacst",
            DisplayOrder = 107,
            IsActive = false,
        },
        new
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000025"),
            Name = "Photographer",
            DisplayOrder = 108,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("86C69D4D-51C8-4302-9FA7-9C6EB15B8F98"),
            Name = "Physcal Therapst",
            DisplayOrder = 109,
            IsActive = false,
        },
        new
        {
            Id = Guid.Parse("7FDDE48B-70F3-4D57-92FA-BE054542E984"),
            Name = "Physcan",
            DisplayOrder = 110,
            IsActive = false,
        },
        new
        {
            Id = Guid.Parse("E798E342-577F-431E-92DD-23BECDA87B9C"),
            Name = "Physical Therapist",
            DisplayOrder = 111,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("1A0B4A01-27FF-4C82-8254-52DB397374DF"),
            Name = "Physician",
            DisplayOrder = 112,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("50515CD6-28B2-4AE8-8278-87EBF8481B52"),
            Name = "Pilot",
            DisplayOrder = 113,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("3F284266-E85B-4CC4-AAF9-4B923E0B9239"),
            Name = "Plot",
            DisplayOrder = 114,
            IsActive = false,
        },
        new
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000007"),
            Name = "Plumber",
            DisplayOrder = 115,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("0DA584A6-FEF9-4B67-902E-81488E6B773A"),
            Name = "Polce Offcer",
            DisplayOrder = 116,
            IsActive = false,
        },
        new
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000009"),
            Name = "Police Officer",
            DisplayOrder = 117,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("AD35AA17-452A-4EDE-86B9-1832DA7756C8"),
            Name = "Portfolio Assistant",
            DisplayOrder = 118,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("B225F341-50D2-4939-AC5F-B15F581583B9"),
            Name = "Product Manager",
            DisplayOrder = 119,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("60E2ABAC-160F-433D-B5DC-E790E820C46B"),
            Name = "Professor",
            DisplayOrder = 120,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("DC47B76D-CD55-44AE-BE81-3BF6D18E5A89"),
            Name = "Project Manager",
            DisplayOrder = 121,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("ECD1CE7A-2FE0-4637-AA8E-7E4211E0BC87"),
            Name = "Prompt Engineer",
            DisplayOrder = 122,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("C5BC20E6-4125-4EED-BD02-45B1612F9155"),
            Name = "Psychologst",
            DisplayOrder = 123,
            IsActive = false,
        },
        new
        {
            Id = Guid.Parse("58C1B4D8-8534-49AD-BB3D-32AEC2E3305B"),
            Name = "Quality Control Reviewer",
            DisplayOrder = 124,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("AB52EF79-DF50-41C4-B1BD-54F9B796BCD3"),
            Name = "Real Estate Agent",
            DisplayOrder = 125,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000019"),
            Name = "Receptionist",
            DisplayOrder = 126,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("25B69A0D-D639-4560-B613-CE8B26723DA2"),
            Name = "Registered Nurse",
            DisplayOrder = 127,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("DC2CA3E5-DA61-4F11-9333-629167C2AE16"),
            Name = "Remote Accounts Specialist",
            DisplayOrder = 128,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("DBD16191-CC6C-4E82-9F6E-F8A8511656F0"),
            Name = "Remote Case Analyst",
            DisplayOrder = 129,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("80D8622D-EF77-4158-B32A-EF7C575FADA1"),
            Name = "Remote Tax Advisor",
            DisplayOrder = 130,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("4C9C0DC8-EA19-49DB-B759-526AFD252972"),
            Name = "Research Scentst",
            DisplayOrder = 131,
            IsActive = false,
        },
        new
        {
            Id = Guid.Parse("13ADBAE0-26A3-46D6-A107-5A248D76C276"),
            Name = "Restaurant Owner",
            DisplayOrder = 132,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000028"),
            Name = "Rideshare Driver",
            DisplayOrder = 133,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("8F3B14F9-24E3-40AF-A876-55EA0FA6AC5E"),
            Name = "Risk Assessment Officer",
            DisplayOrder = 134,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("B675C62F-3868-4196-8244-B46689797655"),
            Name = "Risk Documentation Clerk",
            DisplayOrder = 135,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("0E85FC8D-04E4-4899-A1B3-333B1D2F7D1D"),
            Name = "Sales Manager",
            DisplayOrder = 136,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("9946A57E-AF10-4B9B-88EA-52EF5A4DE390"),
            Name = "Sales Representative",
            DisplayOrder = 137,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000014"),
            Name = "Salesperson",
            DisplayOrder = 138,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("E929370D-F7DC-47A2-852C-5C93DFFD2641"),
            Name = "Sanctions Screening Officer",
            DisplayOrder = 139,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000020"),
            Name = "Secretary",
            DisplayOrder = 140,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000015"),
            Name = "Security Guard",
            DisplayOrder = 141,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000032"),
            Name = "Self-Employed",
            DisplayOrder = 142,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("21AF6389-89B4-4FD2-BE0A-220E6DB833BF"),
            Name = "Senior Return Preparer",
            DisplayOrder = 143,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("4630663D-C424-4642-958E-D863A7BB481D"),
            Name = "Senior Tax Reviewer",
            DisplayOrder = 144,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("2AEAF528-ED01-4838-BDEF-7D01A0648718"),
            Name = "Service Desk Coordinator",
            DisplayOrder = 145,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("56FA9F9A-8F17-4EEB-9839-B8F729DF00EE"),
            Name = "Service Follow-up Coordinator",
            DisplayOrder = 146,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("A2F9F350-0D4E-4F75-9A15-E4D91640C16B"),
            Name = "Service Quality Assistant",
            DisplayOrder = 147,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("1C38B086-839E-478E-9802-BF9D82AF8015"),
            Name = "Socal Worker",
            DisplayOrder = 148,
            IsActive = false,
        },
        new
        {
            Id = Guid.Parse("2DD43AA6-0C0C-4972-AA52-1FA503ED890E"),
            Name = "Social Media Manager",
            DisplayOrder = 149,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            Name = "Software Developer",
            DisplayOrder = 150,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("7B414C15-62ED-4C70-9474-E10795E93451"),
            Name = "Software Engineer",
            DisplayOrder = 151,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("80DA65E5-3413-419A-8AF1-F2AE24EBA2DB"),
            Name = "Software Engneer",
            DisplayOrder = 152,
            IsActive = false,
        },
        new
        {
            Id = Guid.Parse("898E3C5E-3F6F-463B-964F-3837AFDE019D"),
            Name = "Stockbroker",
            DisplayOrder = 153,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("B6671EEB-AA28-4340-98B5-12950A91267D"),
            Name = "Support Desk Analyst",
            DisplayOrder = 154,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("E817FF5E-A6D8-4728-B83F-CFE37F7CA687"),
            Name = "T Manager",
            DisplayOrder = 155,
            IsActive = false,
        },
        new
        {
            Id = Guid.Parse("9E2947C7-B33C-4146-A299-2B752EB17960"),
            Name = "Tax Advisory Assistant",
            DisplayOrder = 156,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("BCE81FC1-84B3-4C1B-AA9B-18B9DBC4EE03"),
            Name = "Tax Operations Associate",
            DisplayOrder = 157,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("256C2C47-4A9B-4F2B-A0CF-F4376C887185"),
            Name = "Tax Preparer",
            DisplayOrder = 158,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("54C427C1-401F-4472-97FB-E89A6142A48B"),
            Name = "Tax Resolution Specialist",
            DisplayOrder = 159,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000003"),
            Name = "Teacher",
            DisplayOrder = 160,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000011"),
            Name = "Truck Driver",
            DisplayOrder = 161,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("05982B84-EC1D-4263-9688-0860287DB1EC"),
            Name = "Truck Er",
            DisplayOrder = 162,
            IsActive = false,
        },
        new
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000026"),
            Name = "Uber Driver",
            DisplayOrder = 163,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("76C0EA52-AFA3-4C96-AAEA-A598546DEE35"),
            Name = "Underwriting Assistant",
            DisplayOrder = 164,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("293CF1D6-44FC-40CE-9756-C5F63CFDEFDD"),
            Name = "Ux Designer",
            DisplayOrder = 165,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("AB15C41B-CDC8-4AD0-8B8A-0AE6426A5169"),
            Name = "Ux-ui Designer",
            DisplayOrder = 166,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("6BDF890F-3528-4282-BA2C-49C245B3A739"),
            Name = "Veterinarian",
            DisplayOrder = 167,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("77EDC345-9C4A-4440-A0D7-92C1E5E1A9CB"),
            Name = "Veternaran",
            DisplayOrder = 168,
            IsActive = false,
        },
        new
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000022"),
            Name = "Web Developer",
            DisplayOrder = 169,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("ADCFAA18-2FD1-4D1D-A538-D76B87B452DC"),
            Name = "Wire Transfer Operator",
            DisplayOrder = 170,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("4D2285E7-851F-5CBE-AAC1-80A311960EF6"),
            Name = "Chief Executive Officer",
            DisplayOrder = 23,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("E9A8F6E7-A21C-5238-8D1A-ACA581C69528"),
            Name = "Chief Financial Officer",
            DisplayOrder = 23,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("9795C205-367B-5A1F-99B3-9A6D74765DD7"),
            Name = "Chief Operating Officer",
            DisplayOrder = 23,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("9B214563-92FF-5AAA-A464-ED76D809A89D"),
            Name = "General Manager",
            DisplayOrder = 80,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("643A89AC-FC88-5B8C-9CAF-7FE636BF8B20"),
            Name = "Legislator",
            DisplayOrder = 89,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("3C342CFA-3F0C-5B0A-90F3-B0C25C542EA9"),
            Name = "Advertising Manager",
            DisplayOrder = 5,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("AA86B9E7-A651-515A-830F-19D2BC60B296"),
            Name = "Promotions Manager",
            DisplayOrder = 122,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("3BD8EE5D-C685-546C-A793-65EB32896AD3"),
            Name = "Marketing Director",
            DisplayOrder = 91,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("3E63DEDB-4B38-5A6E-8DCB-3031AB361DCC"),
            Name = "Public Relations Manager",
            DisplayOrder = 124,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("3A213049-5290-503A-A54A-5A4119434257"),
            Name = "Fundraising Manager",
            DisplayOrder = 80,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("F5D7FC16-297D-5ADF-AD99-F9AB8FE161EA"),
            Name = "Administrative Services Manager",
            DisplayOrder = 5,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("EE66912D-E7E9-5FC1-8D0F-6D92194F9918"),
            Name = "Office Manager",
            DisplayOrder = 100,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("34671645-8BA8-5540-9FAF-5EC3FE8ECD04"),
            Name = "Facilities Manager",
            DisplayOrder = 70,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("59E5A46C-EC81-5292-B64F-7C8C6C8330E7"),
            Name = "IT Manager",
            DisplayOrder = 85,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("03309EF3-AE69-5105-B5A8-B7BD4596D9AE"),
            Name = "Information Systems Manager",
            DisplayOrder = 85,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("33E7BA16-DE46-5DB8-8BA4-9519470235FC"),
            Name = "Financial Manager",
            DisplayOrder = 72,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("76206B24-9F82-592F-AD99-795599063B78"),
            Name = "Controller",
            DisplayOrder = 41,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("3A94DC18-647A-5EC6-97A9-562496BA6F18"),
            Name = "Treasurer",
            DisplayOrder = 161,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("370ADA44-5293-57DC-B48D-E3572ECACE4D"),
            Name = "Industrial Production Manager",
            DisplayOrder = 85,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("DA7EC543-30B2-51D5-809F-93DBB993F154"),
            Name = "Plant Manager",
            DisplayOrder = 115,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("8C6846E0-B2C3-5992-B7FD-2086C591F9BE"),
            Name = "Purchasing Manager",
            DisplayOrder = 124,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("DA1E241C-A220-542A-8B23-80E2CD039DDD"),
            Name = "Logistics Manager",
            DisplayOrder = 89,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("BCF0D30F-A5BC-586F-8DF8-7AC602F11B0A"),
            Name = "Warehouse Manager",
            DisplayOrder = 169,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("86C7D982-FEE3-5A31-88D7-BD69BF3AC5CE"),
            Name = "Transportation Manager",
            DisplayOrder = 161,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("8820FC2B-4C53-5511-B7AD-85EF505BA8F1"),
            Name = "Compensation and Benefits Manager",
            DisplayOrder = 34,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("2C3F731B-04AB-5532-BF99-89C97E16B539"),
            Name = "Training and Development Manager",
            DisplayOrder = 161,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("ECF6809A-0187-5FDB-A688-47F1D2A31DE5"),
            Name = "Farmer",
            DisplayOrder = 70,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("591CCE75-7543-5330-9ABA-315D87350EFF"),
            Name = "Rancher",
            DisplayOrder = 125,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("6E6B5EF4-97ED-5C98-A56E-344A005267A2"),
            Name = "Agricultural Manager",
            DisplayOrder = 5,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("047C88DE-DC48-51A8-B594-3F27079119F2"),
            Name = "Construction Manager",
            DisplayOrder = 37,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("C45252CB-F2A3-5CD2-B55E-2D0082F979A3"),
            Name = "General Contractor",
            DisplayOrder = 80,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("770C5707-A4D0-579F-BF5A-F6338BD3EA57"),
            Name = "Childcare Center Director",
            DisplayOrder = 23,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("74810AA5-827B-5A60-986E-96DDB7A59A78"),
            Name = "Preschool Director",
            DisplayOrder = 119,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("4A1B5A9C-2524-5A89-AC83-9CB06F35A138"),
            Name = "School Principal",
            DisplayOrder = 140,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("DB2DAA98-C073-502B-8B04-FA968CAF10FF"),
            Name = "School Administrator",
            DisplayOrder = 140,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("C5E5A8C5-6CAB-592C-BD09-3C66877AFA40"),
            Name = "College Administrator",
            DisplayOrder = 34,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("41E9BE41-B5B5-51E9-ADCB-36CBADD1DB83"),
            Name = "Dean",
            DisplayOrder = 53,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("837D443B-8F47-5E1B-AF6D-97FDCDC4B4EE"),
            Name = "Architectural Manager",
            DisplayOrder = 9,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("3EBBF311-70A3-54F7-9233-B1B0D0A7A958"),
            Name = "Engineering Manager",
            DisplayOrder = 69,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("6F00AEA0-8550-5996-808F-7B6A7F129055"),
            Name = "Food Service Manager",
            DisplayOrder = 75,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("9CE805F7-5F13-5548-A247-6C7736CB12F6"),
            Name = "Restaurant Manager",
            DisplayOrder = 132,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("663D1C83-4071-53A1-8B90-842C7B035277"),
            Name = "Casino Manager",
            DisplayOrder = 21,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("5FADC23B-E6EF-5C29-A8AF-9A7613AF88D2"),
            Name = "Entertainment Manager",
            DisplayOrder = 69,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("A4150EC7-A356-54B5-82C5-A84730ABCF3C"),
            Name = "Recreation Manager",
            DisplayOrder = 127,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("6DBE7886-547F-54F1-A7D7-7458B52BBF46"),
            Name = "Hotel Manager",
            DisplayOrder = 83,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("EA2F7321-48A3-5443-9C02-34F5A3483440"),
            Name = "Lodging Manager",
            DisplayOrder = 89,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("07C0A9F4-68FA-5589-8EE6-849E627AEAF7"),
            Name = "Medical and Health Services Manager",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("6B490868-1595-5324-B438-456B1780520D"),
            Name = "Hospital Administrator",
            DisplayOrder = 83,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("65B11900-0E9B-531F-BF3B-B504E45D8D75"),
            Name = "Clinic Manager",
            DisplayOrder = 32,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("2D3FAC51-73C1-542F-8A8D-A64AE1E7B95F"),
            Name = "Natural Sciences Manager",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("3E1237BE-EBBE-558E-96E5-4FED5E27C628"),
            Name = "Postmaster",
            DisplayOrder = 119,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("9415F702-F89F-59F8-8068-8D2B7E25ED62"),
            Name = "Property Manager",
            DisplayOrder = 124,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("CCC51ED2-0EF4-50B1-9B27-2E285795F729"),
            Name = "Real Estate Manager",
            DisplayOrder = 126,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("09E945D7-8776-59F6-883E-57C07C8445D0"),
            Name = "Community Association Manager",
            DisplayOrder = 34,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("BAA5A2CF-2050-5660-827C-D8A4E823C0BC"),
            Name = "Social and Community Service Manager",
            DisplayOrder = 149,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("AAD89615-0915-5B6C-A0D8-560DDACA1B5F"),
            Name = "Nonprofit Director",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("0509A401-B0EE-5C7F-B95F-3128DAF251BB"),
            Name = "Emergency Management Director",
            DisplayOrder = 68,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("B4372A56-83E4-54C4-9122-8ECB140F906C"),
            Name = "Funeral Home Manager",
            DisplayOrder = 80,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("08C99BDF-CEAF-59F4-AC74-1CC813E8CB01"),
            Name = "Personal Service Manager",
            DisplayOrder = 106,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("3D4CEEBF-11B7-5830-ADA7-DA0064CF3295"),
            Name = "Store Manager",
            DisplayOrder = 154,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("7B052958-5FE2-57EE-AA21-429BF45FFA86"),
            Name = "Retail Manager",
            DisplayOrder = 133,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("ABA90A08-F9E8-56E5-878A-0D4EB6FA0972"),
            Name = "Branch Manager",
            DisplayOrder = 14,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("24BBAA0D-9349-5022-AA72-DAE9679435D3"),
            Name = "Program Manager",
            DisplayOrder = 121,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("D7311EFB-32C8-586B-BD46-FB609122A53E"),
            Name = "Business Owner",
            DisplayOrder = 17,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("499071E3-0D94-5964-B221-7F024619A5BC"),
            Name = "Small Business Owner",
            DisplayOrder = 149,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("ED63D204-2EB0-5758-9880-606FE4CABB8F"),
            Name = "Entrepreneur",
            DisplayOrder = 69,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("C2A50E87-C4C3-5E88-B77B-ACB0FA9E8613"),
            Name = "Independent Contractor",
            DisplayOrder = 85,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("902B0072-C7B1-5EE6-9BB5-E2EB09B6B64F"),
            Name = "Talent Agent",
            DisplayOrder = 156,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("D383F28D-CB73-5825-953D-D61A82BAA854"),
            Name = "Business Manager",
            DisplayOrder = 17,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("ED0DBE6E-9B15-5E28-ACF0-A2710EF2ADE2"),
            Name = "Buyer",
            DisplayOrder = 17,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("913A8DC8-8BC1-5D47-9538-59FB58451F64"),
            Name = "Purchasing Agent",
            DisplayOrder = 124,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("8F3C1DCA-D130-5033-ABAF-6258F19F9587"),
            Name = "Claims Adjuster",
            DisplayOrder = 25,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("54D3C0D8-17C3-5393-9A70-67F2B2799D58"),
            Name = "Claims Examiner",
            DisplayOrder = 25,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("D2A2554A-D497-5E1E-9BE5-ABF41C96AFD3"),
            Name = "Insurance Investigator",
            DisplayOrder = 85,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("8AF3B1EF-771C-5463-90B9-A3C23F4D9C62"),
            Name = "Auto Damage Appraiser",
            DisplayOrder = 10,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("DEF0013D-6C0C-51C8-9258-AE00AA5BD5AE"),
            Name = "Compliance Officer",
            DisplayOrder = 35,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("4D002A77-04D4-5C99-8316-9112581DC164"),
            Name = "Cost Estimator",
            DisplayOrder = 41,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("888D1F02-D382-5FB5-97A8-320FBD3255DC"),
            Name = "Human Resources Specialist",
            DisplayOrder = 85,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("8081C823-B60C-55EE-B709-3A8F0B6650CC"),
            Name = "Recruiter",
            DisplayOrder = 127,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("6399C44E-84D0-594A-BC61-AACB81DA28D1"),
            Name = "Farm Labor Contractor",
            DisplayOrder = 70,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("7E13DCFB-ECC5-5211-88BC-DC32D457A361"),
            Name = "Labor Relations Specialist",
            DisplayOrder = 87,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("B554DBAB-EAC3-5F0F-88DA-5FAFCC57BF59"),
            Name = "Logistician",
            DisplayOrder = 89,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("83CB3A19-499A-5DE4-AE76-689E8CF99937"),
            Name = "Project Management Specialist",
            DisplayOrder = 121,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("2CCA97AC-8E6C-5AA8-9D6D-3DB3FA9E94B7"),
            Name = "Management Analyst",
            DisplayOrder = 91,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("B0FE99D8-1967-564D-A201-B5816D051039"),
            Name = "Management Consultant",
            DisplayOrder = 91,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("6BF9A56D-0FD4-5EEF-B9A1-EF109B0579C7"),
            Name = "Business Consultant",
            DisplayOrder = 17,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("EB6D1FF5-3EF6-5374-8770-23258B051161"),
            Name = "Event Planner",
            DisplayOrder = 69,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("6797B70D-BFB0-5D9B-975F-6B017804C6C2"),
            Name = "Meeting Planner",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("6B5E9C17-89AB-57D4-BCDD-67BBB296D8AA"),
            Name = "Wedding Planner",
            DisplayOrder = 170,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("AA1E5D1F-13CF-5789-8015-9EC1B3FAF42B"),
            Name = "Fundraiser",
            DisplayOrder = 80,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("7DC83AE3-099B-5CAB-87E1-5CC8F152E20F"),
            Name = "Compensation Analyst",
            DisplayOrder = 34,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("02BD8B9F-BFA9-5246-8508-C57ECD0F17F9"),
            Name = "Benefits Specialist",
            DisplayOrder = 12,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("7098D86E-D802-51FC-9EED-0595CAA18603"),
            Name = "Training and Development Specialist",
            DisplayOrder = 161,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("B5A4DAA5-4B2C-5BD7-A588-A71B082F2594"),
            Name = "Corporate Trainer",
            DisplayOrder = 41,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("B4145C7C-0857-52EB-9C6C-3A4847F49D95"),
            Name = "Market Research Analyst",
            DisplayOrder = 91,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("E96F6924-E689-5AC1-B5A2-D4D00D525D1D"),
            Name = "Marketing Specialist",
            DisplayOrder = 94,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("C15C5829-C665-5EE5-B9B8-4C9CC21BFB81"),
            Name = "Marketing Coordinator",
            DisplayOrder = 91,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("114D55B0-26BC-55AF-A011-35A02B935F06"),
            Name = "Digital Marketing Specialist",
            DisplayOrder = 60,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("45CD50EA-A572-5BD6-8A3A-A4C5C9F62336"),
            Name = "Business Operations Specialist",
            DisplayOrder = 17,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("BF3D2D0B-4505-57DE-ADCB-088A6F90F9F4"),
            Name = "Auditor",
            DisplayOrder = 10,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("CDC495DC-B0CD-5F0C-977B-055205943C6D"),
            Name = "Certified Public Accountant",
            DisplayOrder = 21,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("E38C7F9C-4531-575F-853C-43F57BCF16D8"),
            Name = "Enrolled Agent",
            DisplayOrder = 69,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("A14E91B0-7718-5667-8D7B-686FD2A5F527"),
            Name = "Property Appraiser",
            DisplayOrder = 124,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("986086D7-3D9E-5619-87A5-289E9A0CC1C5"),
            Name = "Real Estate Appraiser",
            DisplayOrder = 126,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("61A056D9-8DA9-57D3-AF00-BA54E62BC723"),
            Name = "Tax Assessor",
            DisplayOrder = 157,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("8D8F60BF-4E19-57C7-9BBD-630655F1216E"),
            Name = "Budget Analyst",
            DisplayOrder = 14,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("8F609AE9-E9BB-57E4-A890-6E4038295C08"),
            Name = "Credit Analyst",
            DisplayOrder = 42,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("01A1264D-40C8-5A71-B17B-A30F6DBC716E"),
            Name = "Investment Analyst",
            DisplayOrder = 85,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("760BA714-55C0-5104-8927-D6A2ED26F4DE"),
            Name = "Personal Financial Advisor",
            DisplayOrder = 106,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("641204B3-140A-516B-AC8F-3125D328926F"),
            Name = "Wealth Manager",
            DisplayOrder = 169,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("E27C899B-A98D-503F-A632-CBBC6D32E6D7"),
            Name = "Insurance Underwriter",
            DisplayOrder = 85,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("BD1D7417-80C8-5971-87A9-E20D481D37E6"),
            Name = "Financial Risk Specialist",
            DisplayOrder = 72,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("7A251193-E46B-59C3-8B52-783D515C6E9F"),
            Name = "Risk Analyst",
            DisplayOrder = 134,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("0422A272-8775-5B12-A935-5A1A2D99CC3A"),
            Name = "Financial Examiner",
            DisplayOrder = 72,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("4B77127A-6646-56B0-8B4E-79657A736283"),
            Name = "Bank Examiner",
            DisplayOrder = 10,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("FA29AFFF-EA38-55C9-94AF-D85BEA0BE267"),
            Name = "Credit Counselor",
            DisplayOrder = 42,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("25CB66A1-E4EB-5057-BB69-F107F5815DE0"),
            Name = "Loan Officer",
            DisplayOrder = 89,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("427B6029-53D8-5E6F-8C49-5A8547E316BE"),
            Name = "Mortgage Loan Officer",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("D0692F65-8FC4-5034-9666-50CC45481AF7"),
            Name = "Mortgage Broker",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("271E4593-E19F-5AFF-B982-B011BF7A5FBB"),
            Name = "Tax Examiner",
            DisplayOrder = 157,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("65FCE11C-47C2-5985-B93F-3DBDE71DDE1C"),
            Name = "Tax Collector",
            DisplayOrder = 157,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("3490F00F-8E09-5F39-BC72-4269A57432EF"),
            Name = "Revenue Agent",
            DisplayOrder = 133,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("0D854A24-CCCD-5D0B-B8D1-7F4A9D1F478C"),
            Name = "IRS Agent",
            DisplayOrder = 85,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("E49A5694-0FFF-5701-9E2A-5037F46DDF8E"),
            Name = "Financial Specialist",
            DisplayOrder = 72,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("F86BCC20-EAFE-59B2-BCFD-2D73F690EF15"),
            Name = "Financial Planner",
            DisplayOrder = 72,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("62396B03-C00B-5B2C-8B31-ED24F84AF61E"),
            Name = "Investment Banker",
            DisplayOrder = 85,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("F2A6E9C0-6014-54E2-9D89-F9987CEAD2AB"),
            Name = "Trader",
            DisplayOrder = 161,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("BD0936F0-C256-5665-AFD2-31A19CC0F583"),
            Name = "Computer Systems Analyst",
            DisplayOrder = 37,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("EA6C4EE8-D268-5310-BBFD-8433BBE2C4FA"),
            Name = "Systems Analyst",
            DisplayOrder = 156,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("C9CDADA4-6653-5F5C-8608-5C21F452587B"),
            Name = "Information Security Analyst",
            DisplayOrder = 85,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("545A7C11-2145-5B01-871D-CC9696863B8F"),
            Name = "Computer Research Scientist",
            DisplayOrder = 37,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("8A2CCE1C-1570-5AEF-BB25-4E6CC5FD1BFA"),
            Name = "Network Support Specialist",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("0903D96B-7030-5C51-BD8D-F2B0A5130623"),
            Name = "IT Support Specialist",
            DisplayOrder = 85,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("15890F63-88CA-5C86-80C6-FF18A5BA30CC"),
            Name = "Help Desk Technician",
            DisplayOrder = 83,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("A0BCE5F8-4ACF-54CC-B5BA-84E57B81E939"),
            Name = "Computer Technician",
            DisplayOrder = 37,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("32ACB8E6-B23C-523A-9CDA-56D46FE071FD"),
            Name = "Network Architect",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("7C9ED300-77C9-5AC9-B919-5B98A604C86C"),
            Name = "Network Engineer",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("47401A98-371B-5425-9697-671E5F2B7BDB"),
            Name = "Database Administrator",
            DisplayOrder = 53,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("1BB3D102-2BD9-59BA-B435-BAAF1DE9773B"),
            Name = "Database Architect",
            DisplayOrder = 53,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("6701725C-D52D-5267-8565-442863434DB8"),
            Name = "Network Administrator",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("D6F26046-4020-5B19-AC08-00AE023988DD"),
            Name = "Systems Administrator",
            DisplayOrder = 156,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("B2A6CC4A-EE49-5DBA-A8B5-B05D2CB85D08"),
            Name = "Computer Programmer",
            DisplayOrder = 37,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("0E6B1821-87B5-512B-9EA9-647F398CFD8F"),
            Name = "Software Architect",
            DisplayOrder = 150,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("055DE83F-1FE2-5538-9961-C3A72C369FFD"),
            Name = "Mobile App Developer",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("16F9C46F-B0DA-55D8-B6AF-8888736C05EE"),
            Name = "Game Developer",
            DisplayOrder = 80,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("3B02ABBA-4A81-53AC-A61C-B9D6D1DCE140"),
            Name = "QA Engineer",
            DisplayOrder = 124,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("10FDC7B4-8AD7-5BCD-8021-E53E3796B1B6"),
            Name = "Software Tester",
            DisplayOrder = 153,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("A8EF56DA-00B1-5698-AD00-94C46A1F8510"),
            Name = "Web Designer",
            DisplayOrder = 169,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("BD31A589-71AA-573A-831A-A7A6DB8D4153"),
            Name = "UI Designer",
            DisplayOrder = 164,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("7E3292D7-F6AF-5C20-88B1-70DE76715109"),
            Name = "Data Engineer",
            DisplayOrder = 51,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("02E49CC0-9F13-597F-A2EB-0AEA798CC3CB"),
            Name = "Data Scientist",
            DisplayOrder = 53,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("31DBE587-840F-50A4-AFCB-41F9F5E03826"),
            Name = "AI Engineer",
            DisplayOrder = 5,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("AF5FB5C6-249C-5311-9B88-D9ED4D7EFCC9"),
            Name = "IT Consultant",
            DisplayOrder = 85,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("588A5723-4171-53AA-9A8A-19AF54B1F4E1"),
            Name = "Actuary",
            DisplayOrder = 5,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("BE0A9053-A2BD-5C42-A1AA-F35EF19D7DE8"),
            Name = "Mathematician",
            DisplayOrder = 94,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("4E0F354A-C170-587D-8067-82A8E15DA9BE"),
            Name = "Operations Research Analyst",
            DisplayOrder = 105,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("4F5BB3D2-5ABE-519A-ADDF-F607FA5DEE44"),
            Name = "Statistician",
            DisplayOrder = 153,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("FC0A5398-1821-5D6D-AA58-A2C1F393D3C0"),
            Name = "Landscape Architect",
            DisplayOrder = 87,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("6BA485C5-BB40-5726-BD11-697128E82E5D"),
            Name = "Naval Architect",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("3AF13D5C-6079-5A8C-B665-6CC8FA8D5FBC"),
            Name = "Cartographer",
            DisplayOrder = 17,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("4E755178-C2A0-52EC-85C5-A4024C715158"),
            Name = "Photogrammetrist",
            DisplayOrder = 108,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("BA896C1F-0A2A-58EA-B09C-F6AEEE3E4CEA"),
            Name = "Surveyor",
            DisplayOrder = 156,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("16D057D9-0D72-509E-8EB8-181E64FDB9FC"),
            Name = "Land Surveyor",
            DisplayOrder = 87,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("0BAC43AB-1C56-5AF8-A20B-A7623B406ABA"),
            Name = "Aerospace Engineer",
            DisplayOrder = 5,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("33C982D9-A86A-553B-B94B-135A1A5D8B50"),
            Name = "Agricultural Engineer",
            DisplayOrder = 5,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("E391750A-A73A-53EB-8248-E60C897E8394"),
            Name = "Biomedical Engineer",
            DisplayOrder = 12,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("EE41E4A9-3875-5085-9FE9-3FF85769575D"),
            Name = "Chemical Engineer",
            DisplayOrder = 23,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("ABE744AC-0EB5-5BD0-91CE-86A18553EA8C"),
            Name = "Computer Hardware Engineer",
            DisplayOrder = 37,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("0591FEFD-6525-5B35-888C-C09A464A7ADB"),
            Name = "Electrical Engineer",
            DisplayOrder = 67,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("49C5BA9A-27B6-582D-B4FD-5C5EBC6A1893"),
            Name = "Electronics Engineer",
            DisplayOrder = 68,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("7198C8BF-AED7-5E3A-B582-BF983BAC33F4"),
            Name = "Environmental Engineer",
            DisplayOrder = 69,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("1AB80F78-E773-5BCF-8281-0F285A539866"),
            Name = "Safety Engineer",
            DisplayOrder = 136,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("EFC31235-DB3A-5BF6-BA2D-9453ACCDE4CA"),
            Name = "Industrial Engineer",
            DisplayOrder = 85,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("F35BF5A8-EE09-5777-992E-D9BA93CD8270"),
            Name = "Marine Engineer",
            DisplayOrder = 91,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("5914625A-4666-54FE-A35D-145361F27458"),
            Name = "Materials Engineer",
            DisplayOrder = 94,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("3065663B-55EF-51F5-BACE-326545764CB3"),
            Name = "Mechanical Engineer",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("B6E0185C-51A9-54B7-A6B4-86D986E09F68"),
            Name = "Mining Engineer",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("E1162AB4-5D20-563B-B321-4DAC1D29AAD4"),
            Name = "Geological Engineer",
            DisplayOrder = 80,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("B883627A-9F75-57F5-89F8-44691632EB55"),
            Name = "Nuclear Engineer",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("1358968C-FC13-5B02-929A-00D0E88936D7"),
            Name = "Petroleum Engineer",
            DisplayOrder = 106,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("87D78D56-DA93-5A71-A18C-78A5C8309D63"),
            Name = "Structural Engineer",
            DisplayOrder = 154,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("FDEA8A23-6FD9-5699-89F6-F205D109A6A8"),
            Name = "Manufacturing Engineer",
            DisplayOrder = 91,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("A1E01754-A7F6-5C87-8BAB-482CCE8A4D02"),
            Name = "Drafter",
            DisplayOrder = 67,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("D9995867-5CC7-556F-8987-D7059DFB7F54"),
            Name = "CAD Technician",
            DisplayOrder = 17,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("9CF214D2-E4ED-5415-9CB6-6B32660D8964"),
            Name = "Engineering Technician",
            DisplayOrder = 69,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("5AECE172-7C0C-507B-B94B-6651592972A5"),
            Name = "Civil Engineering Technician",
            DisplayOrder = 25,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("49998B8F-4D44-5C24-A16B-D26ABAB52E1D"),
            Name = "Electrical Engineering Technician",
            DisplayOrder = 67,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("9E8C82F0-4688-5C1A-8F1A-214AF454E139"),
            Name = "Electronics Technician",
            DisplayOrder = 68,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("C5735776-8A43-5B13-9D5F-ADF72F599FA3"),
            Name = "Mechatronics Technician",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("8DD077A4-638D-5AC0-941D-632CD3AAD1B1"),
            Name = "Environmental Engineering Technician",
            DisplayOrder = 69,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("C66DD083-791E-5C9A-A48D-749F58DC907C"),
            Name = "Industrial Engineering Technician",
            DisplayOrder = 85,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("681CD013-B618-511C-8261-726967FBA7CF"),
            Name = "Mechanical Engineering Technician",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("3B355368-4564-5520-B54C-2B9E848BF88D"),
            Name = "Calibration Technician",
            DisplayOrder = 17,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("7F0F8F47-FC1F-5082-ADC4-057CDF0A813B"),
            Name = "Surveying Technician",
            DisplayOrder = 156,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("AD546AE8-82DC-5DB8-8E8F-5A8D882BC7A1"),
            Name = "Mapping Technician",
            DisplayOrder = 91,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("6C810D9E-6AA8-5800-9EB0-70C367DB0CA3"),
            Name = "Animal Scientist",
            DisplayOrder = 7,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("B253B407-632B-5758-99BC-2772686F082A"),
            Name = "Food Scientist",
            DisplayOrder = 75,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("A05AB6D9-8248-57F6-B4DC-22BA044C20EA"),
            Name = "Soil Scientist",
            DisplayOrder = 153,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("1CCD0312-1737-5C2F-A50C-1AB75280F097"),
            Name = "Plant Scientist",
            DisplayOrder = 115,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("B49869A0-38BF-548B-9346-D302E1F08B82"),
            Name = "Biochemist",
            DisplayOrder = 12,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("DF946A3A-C123-50A3-9B6C-9A8B875E9E1E"),
            Name = "Biophysicist",
            DisplayOrder = 12,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("C4562BA5-612A-5D58-A87C-2E2C452DF968"),
            Name = "Microbiologist",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("B50B4215-B9B4-5F51-9859-9C7C1DBD8BA0"),
            Name = "Zoologist",
            DisplayOrder = 171,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("CC4A4A0B-FCBE-5B3B-8A65-19C9267980EA"),
            Name = "Wildlife Biologist",
            DisplayOrder = 170,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("00B3A879-3D83-51CC-ADBD-1DF020FFF1D8"),
            Name = "Biologist",
            DisplayOrder = 12,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("3EC6D94C-2ADE-5CBA-9066-BE697887DF36"),
            Name = "Conservation Scientist",
            DisplayOrder = 37,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("E078EA5F-189D-5ECC-9CB6-AD4D5B2D17F7"),
            Name = "Forester",
            DisplayOrder = 75,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("DE96881C-4588-5206-9201-D2C0749642C0"),
            Name = "Epidemiologist",
            DisplayOrder = 69,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("A017CD88-36D3-5FE9-9ACE-9873AC27741F"),
            Name = "Medical Scientist",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("689B66CB-7348-579D-A645-AA4235FA68FB"),
            Name = "Research Scientist",
            DisplayOrder = 132,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("FF37E18C-C43F-51EB-AC22-B82C2336E275"),
            Name = "Astronomer",
            DisplayOrder = 9,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("046705A1-A347-5269-BD43-CBFE944937D1"),
            Name = "Physicist",
            DisplayOrder = 113,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("4E1C8139-747B-52E0-92B6-02FA1CCB9FFA"),
            Name = "Meteorologist",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("76BBC9BD-446C-5C46-86D9-2AD4759F8A9A"),
            Name = "Chemist",
            DisplayOrder = 23,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("25E87ECC-25D7-510E-BA29-15A7E72BFEDB"),
            Name = "Materials Scientist",
            DisplayOrder = 94,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("7B978F43-BDC5-55CE-8280-5A15BE7698D8"),
            Name = "Environmental Scientist",
            DisplayOrder = 69,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("E3FEAEEC-BD0E-5440-BF78-3ACB4584EABB"),
            Name = "Geoscientist",
            DisplayOrder = 80,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("30B23EEA-5B7E-5821-9E5B-D585BBB742B8"),
            Name = "Geologist",
            DisplayOrder = 80,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("B9BB6E24-8196-5353-80EB-916CD8771150"),
            Name = "Hydrologist",
            DisplayOrder = 85,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("A1848AF7-B59F-5048-B8BD-92B71A705DFF"),
            Name = "Economist",
            DisplayOrder = 67,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("8A50A760-1A75-5645-B9D3-C2FAE3DC8B35"),
            Name = "Survey Researcher",
            DisplayOrder = 156,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("DEBB2E3B-F535-5C71-9F41-8BD40E0A74D6"),
            Name = "Industrial-Organizational Psychologist",
            DisplayOrder = 85,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("180CD318-986E-5E17-B899-1D07B0737CDF"),
            Name = "Clinical Psychologist",
            DisplayOrder = 32,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("D7FC2C66-B4A4-55D5-9A33-E70AB3BD37A1"),
            Name = "Counseling Psychologist",
            DisplayOrder = 41,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("00DFEFCB-63CA-583F-ACCC-FF59B20B40AB"),
            Name = "School Psychologist",
            DisplayOrder = 140,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("AF5ACDFE-D671-59B1-96FE-0EEFE4A6112B"),
            Name = "Psychologist",
            DisplayOrder = 124,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("70E52806-0B3E-5BFC-B9D2-42CC6DE6AB3B"),
            Name = "Sociologist",
            DisplayOrder = 150,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("B32ED65F-9091-5292-A1EC-215C9EE191C4"),
            Name = "Urban Planner",
            DisplayOrder = 165,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("B16F1F3A-8D74-5D94-9066-99036FAE63DD"),
            Name = "Regional Planner",
            DisplayOrder = 127,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("6BF7270F-4E4B-5BFA-81B9-889761278749"),
            Name = "Anthropologist",
            DisplayOrder = 7,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("83DE9445-3581-5B3B-88FB-3F3BC062DEFC"),
            Name = "Archaeologist",
            DisplayOrder = 7,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("60887807-3E12-5EA5-8A16-D96491CCB038"),
            Name = "Geographer",
            DisplayOrder = 80,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("6C3B3AD5-FE94-5ACB-8FE9-D1B6F9A17657"),
            Name = "Historian",
            DisplayOrder = 83,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("2B904593-4677-5206-A2EC-65285C6E10B1"),
            Name = "Political Scientist",
            DisplayOrder = 118,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("D22E89A1-B059-5A6C-BA3D-D4548AB7C708"),
            Name = "Agricultural Technician",
            DisplayOrder = 5,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("514234F8-31FA-5C5F-A2B2-908C7D85BDD1"),
            Name = "Food Science Technician",
            DisplayOrder = 75,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("D3ADDE14-F283-54B4-A5B4-593093DCF44A"),
            Name = "Biological Technician",
            DisplayOrder = 12,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("C9BB2CA5-D079-5A3C-8675-9B0508455E21"),
            Name = "Laboratory Technician",
            DisplayOrder = 87,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("09DB1417-57F7-5F28-8C7B-249A2588091F"),
            Name = "Chemical Technician",
            DisplayOrder = 23,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("66418425-18DD-5EEA-A186-CB8E5CBF966D"),
            Name = "Environmental Technician",
            DisplayOrder = 69,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("8F2E3DF4-D373-58E6-BB9F-28226743923C"),
            Name = "Geological Technician",
            DisplayOrder = 80,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("2AC80D52-CCB0-5C9B-A983-E11C971221B6"),
            Name = "Hydrologic Technician",
            DisplayOrder = 85,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("41465280-F53E-5D74-913A-B22D18D8A874"),
            Name = "Nuclear Technician",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("6564EE8C-2FCE-5B78-907F-CD562B1EB84D"),
            Name = "Research Assistant",
            DisplayOrder = 132,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("45CC8096-E1C7-5F6B-B281-1746715532EE"),
            Name = "Forest Technician",
            DisplayOrder = 75,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("51481272-FA06-58F2-9C23-2D9DC387A0C6"),
            Name = "Conservation Technician",
            DisplayOrder = 37,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("42A823EC-989F-5816-9193-EE08B6AD9B5D"),
            Name = "Forensic Science Technician",
            DisplayOrder = 75,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("EAB8406B-06BB-5E97-8B42-B10FDB21DE01"),
            Name = "Occupational Health and Safety Specialist",
            DisplayOrder = 100,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("65404938-B92C-5C2F-9F6B-9536D499FC86"),
            Name = "Safety Specialist",
            DisplayOrder = 136,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("556FB79E-A1AD-5655-8479-7912149E5EDD"),
            Name = "Occupational Health and Safety Technician",
            DisplayOrder = 100,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("7C5C18DC-5750-5708-9CB6-AA1F37F97027"),
            Name = "School Counselor",
            DisplayOrder = 140,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("6B810813-50AD-5F97-8D1A-3E0261CD9673"),
            Name = "Career Counselor",
            DisplayOrder = 17,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("B9298A5D-8EC1-529D-BE87-E83607A9F6ED"),
            Name = "Academic Advisor",
            DisplayOrder = 0,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("4B84B8DA-1290-5BB9-B83A-EEBEDBA3A54E"),
            Name = "Marriage and Family Therapist",
            DisplayOrder = 94,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("6A13EC11-DF25-587B-9CCB-4FF9D9CE881C"),
            Name = "Rehabilitation Counselor",
            DisplayOrder = 128,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("74874251-3F2D-567E-8E2E-CDA8F728D5EB"),
            Name = "Mental Health Counselor",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("EE62E82E-A5EA-5585-9450-355CCB1AB9DE"),
            Name = "Substance Abuse Counselor",
            DisplayOrder = 154,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("BB228669-740C-5EB2-AEA7-1E85430139F9"),
            Name = "Counselor",
            DisplayOrder = 41,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("24B38438-F8EB-57A7-B3A9-6D2695BB0FB8"),
            Name = "Social Worker",
            DisplayOrder = 150,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("DF23FC20-2DCD-5B77-BFE2-01349C450B29"),
            Name = "Child and Family Social Worker",
            DisplayOrder = 23,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("E35D56B4-452A-56A4-B701-EA6DB80EF97F"),
            Name = "Healthcare Social Worker",
            DisplayOrder = 83,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("C3EA15E3-A7E8-5333-8997-B38DE4798F98"),
            Name = "Health Educator",
            DisplayOrder = 83,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("791C9E9C-5B5B-5785-AD5B-7A57154BF96B"),
            Name = "Probation Officer",
            DisplayOrder = 119,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("77047CC7-4F4C-5C10-9230-D048F24DB780"),
            Name = "Correctional Treatment Specialist",
            DisplayOrder = 41,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("B9B881A4-E5E3-5819-AC3A-B83FDF405FB7"),
            Name = "Case Manager",
            DisplayOrder = 19,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("1D8540E8-AD98-5D3C-9498-FFE96590AA88"),
            Name = "Social and Human Service Assistant",
            DisplayOrder = 149,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("A281B465-F9C9-51B9-91BC-7D759B5F6DDD"),
            Name = "Community Health Worker",
            DisplayOrder = 34,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("CE17D62D-6621-5FC2-A600-B7467666F2DB"),
            Name = "Community Outreach Coordinator",
            DisplayOrder = 34,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("E16F3F51-ED91-5834-BC6D-99E212E6143C"),
            Name = "Clergy",
            DisplayOrder = 25,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("101D2DF5-7E52-53C5-9021-DCF7DCAC8C58"),
            Name = "Pastor",
            DisplayOrder = 105,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("2853DFC5-662D-5454-A186-5E5423DB8E2A"),
            Name = "Priest",
            DisplayOrder = 119,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("4C016240-4EFE-525A-B531-25D4826E3A81"),
            Name = "Minister",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("C018B128-A650-5933-AC0B-5EBFC7B0E9CF"),
            Name = "Rabbi",
            DisplayOrder = 125,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("B51C30E8-FD7A-50C1-971F-303719F90F7C"),
            Name = "Imam",
            DisplayOrder = 85,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("22832D83-DA36-528C-98A3-B455B95A5F5E"),
            Name = "Religious Education Director",
            DisplayOrder = 128,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("223D8B4B-E790-5C1C-8E0D-AE9A45493FC5"),
            Name = "Missionary",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("440F430F-6B02-559B-8D42-1B6CAF1CE753"),
            Name = "Youth Pastor",
            DisplayOrder = 171,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("CDB3F9A3-F532-5073-9B65-A3AB47C5DDBE"),
            Name = "Judicial Law Clerk",
            DisplayOrder = 86,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("D162DF7C-5FB8-5D88-A259-9A821D577DC2"),
            Name = "Administrative Law Judge",
            DisplayOrder = 5,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("01D3E322-1D0D-5E4C-BF96-E6FB070297F8"),
            Name = "Hearing Officer",
            DisplayOrder = 83,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("5AC1161C-65D5-58AA-B7D6-F96E049509D3"),
            Name = "Arbitrator",
            DisplayOrder = 7,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("F3313DE9-F30B-55FC-B8E1-B2788389A52A"),
            Name = "Mediator",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("0482C368-49CA-5E05-8457-0F42D93F66A8"),
            Name = "Judge",
            DisplayOrder = 86,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("5FA62A07-016E-51B0-942D-7BCE5190DBE8"),
            Name = "Magistrate",
            DisplayOrder = 91,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("D19F179E-9BCD-55AC-8967-A58B1FFA8AFD"),
            Name = "Paralegal",
            DisplayOrder = 105,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("D7C74F32-073B-5221-A5BC-87A420E7BACC"),
            Name = "Legal Assistant",
            DisplayOrder = 89,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("E572F230-4D88-58CE-88FC-00301FC37EAD"),
            Name = "Title Examiner",
            DisplayOrder = 161,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("53340F70-D7AB-5803-A50E-852F9FEB8813"),
            Name = "Title Searcher",
            DisplayOrder = 161,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("E930F740-D3DF-5EA6-80BD-203E662BA2E0"),
            Name = "Abstractor",
            DisplayOrder = 0,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("1B8AFA5A-9D45-585E-BEBB-CDBD97787B8D"),
            Name = "Notary Public",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("F70FCA9E-ECB2-59A0-80EA-DFFBFA1E99C2"),
            Name = "Legal Secretary",
            DisplayOrder = 89,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("6A699F60-B46A-5181-9105-F86DB48C2204"),
            Name = "College Professor",
            DisplayOrder = 34,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("945CFE78-701E-56A4-B18F-1FF10345A0C3"),
            Name = "Adjunct Professor",
            DisplayOrder = 5,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("B51C8099-4302-581E-AFA1-6CD460A19CB4"),
            Name = "Lecturer",
            DisplayOrder = 89,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("33ACF7CD-DCDB-5505-93E3-EF6009B23C0E"),
            Name = "Nursing Instructor",
            DisplayOrder = 100,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("ECB6DCEF-3C1B-5351-B005-1037DF6FF89D"),
            Name = "Preschool Teacher",
            DisplayOrder = 119,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("6243428B-79BC-5311-A757-74CB06C4E36F"),
            Name = "Kindergarten Teacher",
            DisplayOrder = 86,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("207625A9-6434-510F-8EA0-34F899B7563B"),
            Name = "Elementary School Teacher",
            DisplayOrder = 68,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("7675591D-1FD9-58A7-B748-E4D76129CBDD"),
            Name = "Middle School Teacher",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("B41F0B63-63C3-5728-8CA8-98711FD8BE6B"),
            Name = "High School Teacher",
            DisplayOrder = 83,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("CA2340EA-6BAD-5F9C-B399-84C6539108AB"),
            Name = "Career and Technical Education Teacher",
            DisplayOrder = 17,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("F89B9B50-3664-5D72-B24C-A6C7BAFC577F"),
            Name = "Special Education Teacher",
            DisplayOrder = 153,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("7564F89E-4683-59F0-86E5-AEB4B60AA633"),
            Name = "Adult Education Teacher",
            DisplayOrder = 5,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("61B5CC17-FCC5-5E42-B596-D49829EA9152"),
            Name = "ESL Teacher",
            DisplayOrder = 69,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("4B72B5FC-C239-5383-A9EB-DCC8E01611F9"),
            Name = "Self-Enrichment Teacher",
            DisplayOrder = 143,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("2ECF4140-42F3-5949-9227-47013246560C"),
            Name = "Music Teacher",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("EBDFB957-D4FA-5550-B8B9-049E58C6BB2B"),
            Name = "Art Teacher",
            DisplayOrder = 9,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("C75CBCFB-FBC2-5744-91D8-236783A76DD6"),
            Name = "Driving Instructor",
            DisplayOrder = 67,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("1EDD0700-920A-5E89-89CF-C103BAB945AD"),
            Name = "Substitute Teacher",
            DisplayOrder = 154,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("897E99EB-0726-5E51-B125-43A4CC46F360"),
            Name = "Tutor",
            DisplayOrder = 163,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("A853D61A-F572-5AD6-96F2-6A245E28D1B0"),
            Name = "Archivist",
            DisplayOrder = 9,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("1FFC826A-1358-5369-8ED1-68F4EE1943E0"),
            Name = "Curator",
            DisplayOrder = 44,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("92199EDB-F179-51BE-A5C9-B83CF989ECC8"),
            Name = "Museum Technician",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("1D9BFC94-D292-5B88-9D3C-B16ABA7AC3EB"),
            Name = "Conservator",
            DisplayOrder = 37,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("BE59CB16-1FD1-5708-A54C-041F37E7D72A"),
            Name = "Librarian",
            DisplayOrder = 89,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("080443E1-5C3E-5E55-8C40-72EB881D6125"),
            Name = "Library Technician",
            DisplayOrder = 89,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("8BC9B988-3594-5A51-99A6-A428F3E4C16D"),
            Name = "Instructional Coordinator",
            DisplayOrder = 85,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("7C08E35A-BAC3-50E8-B70A-C38520E42F47"),
            Name = "Teaching Assistant",
            DisplayOrder = 161,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("30BE5E31-A37A-5130-A57C-2F80C902E113"),
            Name = "Teacher Assistant",
            DisplayOrder = 161,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("DA2CC78F-F83C-5B87-9DAD-AE4E12000976"),
            Name = "Paraprofessional",
            DisplayOrder = 105,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("C1E732E1-C00A-50E9-A04D-D342608171E3"),
            Name = "Education Consultant",
            DisplayOrder = 67,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("1BBC48AC-F64E-5836-992B-58A4C1D03B98"),
            Name = "Art Director",
            DisplayOrder = 9,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("04BA10B8-A938-5614-BD7C-E378C766A90B"),
            Name = "Craft Artist",
            DisplayOrder = 42,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("561E0C85-203A-5F64-BEC3-0E62B51D38F2"),
            Name = "Artist",
            DisplayOrder = 9,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("73CEDB57-4C2D-5424-ABDE-7FE78F0ECE41"),
            Name = "Fine Artist",
            DisplayOrder = 72,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("4D0BB465-3236-5FB1-983F-E2419C55F882"),
            Name = "Sculptor",
            DisplayOrder = 140,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("354E0919-DE0A-5BF5-8C79-3A4863EF58BA"),
            Name = "Illustrator",
            DisplayOrder = 85,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("4ECFEFFB-8DC5-58C5-9CCB-6ECE30A250C4"),
            Name = "Animator",
            DisplayOrder = 7,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("E9F9D18C-41C6-5FB4-938E-E93632307343"),
            Name = "Special Effects Artist",
            DisplayOrder = 153,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("E0102079-CA88-5D41-BD98-05561D42143C"),
            Name = "Industrial Designer",
            DisplayOrder = 85,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("60D83CA9-CBC3-54D3-937D-414343D64B1B"),
            Name = "Fashion Designer",
            DisplayOrder = 70,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("76D17ECF-BE8E-5503-9E6F-43A09D5B20A2"),
            Name = "Floral Designer",
            DisplayOrder = 75,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("06C1D8B7-93F0-5FB1-B2D8-93614B285DEC"),
            Name = "Florist",
            DisplayOrder = 75,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("07FA33C0-7AC7-56FA-80CB-CB0754887494"),
            Name = "Interior Designer",
            DisplayOrder = 85,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("F15FA66B-1282-5C66-B523-FB30B80CAF8D"),
            Name = "Visual Merchandiser",
            DisplayOrder = 169,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("7ABAE7B2-0632-5EA8-B90B-C533801CCDE8"),
            Name = "Set Designer",
            DisplayOrder = 149,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("B821CBEB-426D-5F68-93FF-E83B5197B6D6"),
            Name = "Exhibit Designer",
            DisplayOrder = 70,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("884180B2-3F7B-579F-A5B3-60E19B614C54"),
            Name = "Product Designer",
            DisplayOrder = 119,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("AE5EE4E6-14AA-5B79-80B7-19CF5AA3A3B7"),
            Name = "Actor",
            DisplayOrder = 5,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("A6494D4B-4F99-5A6A-8FA1-7576CEB4DABC"),
            Name = "Producer",
            DisplayOrder = 119,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("E2760E69-1837-5649-8D57-B775A8A51F15"),
            Name = "Director",
            DisplayOrder = 62,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("A3FB6851-4640-50DA-8E38-409F9A8AEBF6"),
            Name = "Film Director",
            DisplayOrder = 70,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("5316DDE6-3912-5AA7-8AFE-7950EBCE78F3"),
            Name = "Athlete",
            DisplayOrder = 9,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("8C20CB93-51A9-5472-8FE3-DDD8A95312C5"),
            Name = "Professional Athlete",
            DisplayOrder = 120,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("3DB5E16B-B90F-5D68-8774-320F5A481DF0"),
            Name = "Coach",
            DisplayOrder = 33,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("D11FD865-060B-54D1-92A4-D8635B4B9050"),
            Name = "Scout",
            DisplayOrder = 140,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("49CAE977-C43C-54F6-98F1-CAC424C3C304"),
            Name = "Umpire",
            DisplayOrder = 164,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("3D8029B4-EAF0-5633-B1B7-C13E65B0AEF8"),
            Name = "Referee",
            DisplayOrder = 127,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("B09AF030-CED9-5E8B-873A-21B113BDB5E2"),
            Name = "Dancer",
            DisplayOrder = 50,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("E18EACC0-9980-509B-9F23-7E154373A5FB"),
            Name = "Choreographer",
            DisplayOrder = 23,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("052C5621-BA83-5C9C-BB73-B8068087C429"),
            Name = "Music Director",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("501C60CB-EAC6-57D1-8617-D7860798FFFE"),
            Name = "Composer",
            DisplayOrder = 37,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("DD7BADB2-E0C7-58CE-AD97-A46C213BB999"),
            Name = "Musician",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("D4A16DDE-32BB-5F7F-87E9-2BBA16BAD612"),
            Name = "Singer",
            DisplayOrder = 149,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("F6B5DFCD-2A99-5414-8ED8-ACB7C4E888CD"),
            Name = "DJ",
            DisplayOrder = 63,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("0AEEA07E-8D76-552C-94C8-34C74056AB7F"),
            Name = "Entertainer",
            DisplayOrder = 69,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("A90177C5-60C0-5706-A876-16AD9B9748A5"),
            Name = "Performer",
            DisplayOrder = 106,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("DD37170A-7287-5166-A74F-169A283E78B6"),
            Name = "Comedian",
            DisplayOrder = 34,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("230B5C51-16D0-5107-A3B6-466323B290AB"),
            Name = "Magician",
            DisplayOrder = 91,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("973352F2-4CE9-5279-9DC0-CE89721E9772"),
            Name = "Broadcast Announcer",
            DisplayOrder = 14,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("7AF22654-5AC9-5A26-98BD-6BA824AC79F6"),
            Name = "Radio Host",
            DisplayOrder = 125,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("59BFC7BE-66F7-590F-9717-E4FB0B78B572"),
            Name = "News Anchor",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("26D256C9-2A4A-5DEA-8543-5113F17EF9BC"),
            Name = "Journalist",
            DisplayOrder = 86,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("449BD374-9F59-5CC5-B782-756410403F58"),
            Name = "Reporter",
            DisplayOrder = 132,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("5205F723-DEFC-5A40-A97E-29D812745DEF"),
            Name = "News Analyst",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("351B96D5-A816-5585-8FBD-1A9B4399DA44"),
            Name = "Public Relations Specialist",
            DisplayOrder = 124,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("437F6219-1102-5E08-BCF5-1F32BBFD33C8"),
            Name = "Editor",
            DisplayOrder = 67,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("235114F2-E5C3-568A-A4F7-7FB68FC33FBE"),
            Name = "Technical Writer",
            DisplayOrder = 161,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("7BBAE4D0-DCFA-5847-A2C4-F3A57BE4B603"),
            Name = "Writer",
            DisplayOrder = 171,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("D9FCC966-79F4-53A1-A421-8C3F952D66ED"),
            Name = "Author",
            DisplayOrder = 10,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("A4B2333A-9C01-56F7-8CDD-3403CCEC2905"),
            Name = "Copywriter",
            DisplayOrder = 41,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("0125CF67-8260-5AAA-A62B-F116DE06E9A5"),
            Name = "Blogger",
            DisplayOrder = 13,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("9D4B16C1-491F-534F-B38A-1F6D61569664"),
            Name = "Interpreter",
            DisplayOrder = 85,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("A63D6D76-AC41-5BB7-9C04-FE9A0D616938"),
            Name = "Translator",
            DisplayOrder = 161,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("1B7D273A-284B-58F0-B7C3-3690650E518F"),
            Name = "Court Reporter",
            DisplayOrder = 42,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("30A102C9-A374-5733-9738-3E22F79B5D04"),
            Name = "Captioner",
            DisplayOrder = 17,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("E1952D64-3501-5AFA-AFA3-427F6644D5D3"),
            Name = "Audio Technician",
            DisplayOrder = 10,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("08B072D0-3A54-59FF-89F9-BB53D9A6F819"),
            Name = "Video Technician",
            DisplayOrder = 169,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("283ACD06-A00E-5E95-BC08-ABE78FF6DA2B"),
            Name = "Broadcast Technician",
            DisplayOrder = 14,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("AB17E7C9-5B3B-5655-89A7-D7D2B6DFD132"),
            Name = "Sound Engineer",
            DisplayOrder = 153,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("128A1C71-353E-56D0-A8BF-24718B56D9A3"),
            Name = "Lighting Technician",
            DisplayOrder = 89,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("A8EB1ED6-2468-5316-8F71-3775CA302209"),
            Name = "Videographer",
            DisplayOrder = 169,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("AAAE1E07-E2E2-57ED-9AFA-7E163BD8B9A6"),
            Name = "Camera Operator",
            DisplayOrder = 17,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("7B96060A-6579-5CDF-A063-C2335BD381F8"),
            Name = "Film Editor",
            DisplayOrder = 70,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("E287E0EE-5260-5AD1-8B67-ACCC64863313"),
            Name = "Video Editor",
            DisplayOrder = 169,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("5834C068-531E-58C0-9A0E-7331FF5EC323"),
            Name = "Influencer",
            DisplayOrder = 85,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("A783F869-FA3F-5E5E-87CF-9F58CAEFF292"),
            Name = "Podcaster",
            DisplayOrder = 117,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("A99A1689-A77F-55FC-B5EC-694F396B5FA7"),
            Name = "Streamer",
            DisplayOrder = 154,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("F5C5336C-5EA5-5995-BB88-AA678E4D0DA9"),
            Name = "YouTuber",
            DisplayOrder = 171,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("E768D1B2-B7AD-5391-A91A-29DB31447D30"),
            Name = "Chiropractor",
            DisplayOrder = 23,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("C24F7F5E-590C-52EB-96AF-8CC0A91C3CE3"),
            Name = "Oral Surgeon",
            DisplayOrder = 105,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("B48FBC2D-5EED-5F5B-8236-EE59033FFD4D"),
            Name = "Orthodontist",
            DisplayOrder = 105,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("B7C5E215-3C90-5884-93CB-D5C202594388"),
            Name = "Prosthodontist",
            DisplayOrder = 124,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("438FA339-A16E-5142-9C39-DA7EF7E40742"),
            Name = "Dietitian",
            DisplayOrder = 57,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("AF7D7BE1-B57F-5B5E-BA33-B93D0D95AB87"),
            Name = "Nutritionist",
            DisplayOrder = 100,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("57E6373B-D532-5F71-B9AE-99440C938A91"),
            Name = "Optometrist",
            DisplayOrder = 105,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("9A949032-0449-55C5-A747-6A16735F1E80"),
            Name = "Physician Assistant",
            DisplayOrder = 113,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("0DB4286B-A580-56CA-9765-7A5E12CB9AEB"),
            Name = "Podiatrist",
            DisplayOrder = 117,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("C25F19AD-AE0A-5E8B-97D4-5D2065FB3607"),
            Name = "Occupational Therapist",
            DisplayOrder = 100,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("58A1794C-F6A6-5266-BEB8-A2AF59660F5C"),
            Name = "Radiation Therapist",
            DisplayOrder = 125,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("DF42CC7A-C82D-52BE-87DA-D2BD79ABE6CF"),
            Name = "Recreational Therapist",
            DisplayOrder = 127,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("EAB0DBF5-2C25-5DC5-9B8C-5F4BAD7FDA33"),
            Name = "Respiratory Therapist",
            DisplayOrder = 132,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("3673E199-4AAC-5475-A484-64A7EABDF36C"),
            Name = "Speech-Language Pathologist",
            DisplayOrder = 153,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("0610A3CD-A57E-58C9-BA8D-D088B6C1B279"),
            Name = "Exercise Physiologist",
            DisplayOrder = 70,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("D88BDC2A-948A-52FC-A3A3-E4D96014BB67"),
            Name = "Therapist",
            DisplayOrder = 161,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("A249532A-27FD-5FFF-9E1F-EC1A5BC29D8C"),
            Name = "Nurse Anesthetist",
            DisplayOrder = 98,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("EA51E8C2-C412-51D0-A874-37AC8E044D6D"),
            Name = "Nurse Midwife",
            DisplayOrder = 98,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("64BA5560-603F-5F82-B5F4-692A8321E1C9"),
            Name = "Midwife",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("A8A529E2-6350-5782-97F8-9532713D0A2C"),
            Name = "Audiologist",
            DisplayOrder = 10,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("55E1C680-C8D1-5C7D-BAE1-5F82CF8226B8"),
            Name = "Anesthesiologist",
            DisplayOrder = 7,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("4CD32A2C-3C29-5E52-AF8B-813DF39FF16C"),
            Name = "Cardiologist",
            DisplayOrder = 17,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("DA63C1C3-728F-5CE3-A2D5-6D258F242D9E"),
            Name = "Dermatologist",
            DisplayOrder = 56,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("7599F6EB-CEAC-5D4C-9F3B-1EF8DF3EE704"),
            Name = "Emergency Medicine Physician",
            DisplayOrder = 68,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("3424CCA9-782C-5F09-8E89-D8FCFFF819BE"),
            Name = "Family Medicine Physician",
            DisplayOrder = 70,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("45777654-7242-5C66-9EF1-6B7B97E1B6FF"),
            Name = "Internist",
            DisplayOrder = 85,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("91871D80-1434-5ACF-8E88-4DF8B8B09999"),
            Name = "Neurologist",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("A613FCC3-36AB-5436-8C98-7898E0365610"),
            Name = "Obstetrician",
            DisplayOrder = 100,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("061F68F9-40E2-5F6E-8978-643569FDBB8E"),
            Name = "Gynecologist",
            DisplayOrder = 81,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("593B03F4-C90C-5EBC-A572-1F99ABF1A561"),
            Name = "Pediatrician",
            DisplayOrder = 106,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("0A215ECB-6C73-5FC4-A8E2-90AD92DB6148"),
            Name = "Pathologist",
            DisplayOrder = 105,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("ED473F87-1199-550F-B857-AD7161888A81"),
            Name = "Psychiatrist",
            DisplayOrder = 124,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("E1EFFD77-46F6-505B-8AD5-ED9852523977"),
            Name = "Radiologist",
            DisplayOrder = 125,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("6C027414-36AD-5763-B022-A61B53A7EE5F"),
            Name = "Ophthalmologist",
            DisplayOrder = 105,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("3F67F7A3-117F-565A-9DE5-1029B6BA9861"),
            Name = "Orthopedic Surgeon",
            DisplayOrder = 105,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("04584172-4CE0-5C2F-8524-531142242F2B"),
            Name = "Surgeon",
            DisplayOrder = 156,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("D96E1425-F999-5244-97E0-DD322C7B2496"),
            Name = "Acupuncturist",
            DisplayOrder = 5,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("02E7F6EE-B772-542E-91C2-EA970C7812A4"),
            Name = "Dental Hygienist",
            DisplayOrder = 54,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("D69CEBAD-A0CF-5AE0-A1A2-3E423210FD33"),
            Name = "Medical Laboratory Technologist",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("5D806AAC-E349-5705-B683-D3BEB08D6F55"),
            Name = "Medical Laboratory Technician",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("DD15A427-FE4C-5682-BC67-523F815D342E"),
            Name = "Cardiovascular Technologist",
            DisplayOrder = 17,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("A83F9E8A-3D13-578E-8F3D-618A985DD283"),
            Name = "Sonographer",
            DisplayOrder = 153,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("21BB17D4-E206-5D00-8EC6-05E9035CB32D"),
            Name = "Nuclear Medicine Technologist",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("CCF3411E-D372-51AF-9A30-BF12E3BB6997"),
            Name = "Radiologic Technologist",
            DisplayOrder = 125,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("84E93C31-F5EB-5AD2-861F-165399687363"),
            Name = "X-Ray Technician",
            DisplayOrder = 171,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("F07A38E4-E584-5E84-A193-6130DBCF9518"),
            Name = "MRI Technologist",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("4F83AF1D-5939-550A-9200-B24980ACFEDC"),
            Name = "Medical Dosimetrist",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("D85F7E70-E067-5A2C-8FE6-8391D75EC674"),
            Name = "Emergency Medical Technician",
            DisplayOrder = 68,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("93E18FB2-C3D1-51CF-AA54-65C251E32636"),
            Name = "Dietetic Technician",
            DisplayOrder = 57,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("1075B653-FD48-5940-BA29-A8C12693BD5A"),
            Name = "Pharmacy Technician",
            DisplayOrder = 108,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("BAF45C66-75CB-526A-817C-C3887BCD33B0"),
            Name = "Psychiatric Technician",
            DisplayOrder = 124,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("1C214BAC-05F8-57A1-BA65-B74DD3C1D007"),
            Name = "Surgical Technologist",
            DisplayOrder = 156,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("66C49F37-93A0-5E11-B462-BDAB6A7BDDC7"),
            Name = "Veterinary Technician",
            DisplayOrder = 169,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("C0A2CDA7-5C91-52DA-97E9-054FEA407243"),
            Name = "Ophthalmic Technician",
            DisplayOrder = 105,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("6CFA5DF6-3468-5161-B5CA-F226D8CE3C55"),
            Name = "Licensed Practical Nurse",
            DisplayOrder = 89,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("3C1EC149-A1C6-51B7-855D-DB23BC5F0C80"),
            Name = "Licensed Vocational Nurse",
            DisplayOrder = 89,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("7CC16C3B-7ED2-5949-B47F-68E6F8345943"),
            Name = "Medical Records Specialist",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("4F25E574-C1DB-5062-870C-82A2C2D0242E"),
            Name = "Medical Coder",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("7E4C13E1-D2BD-5FB0-A771-4DBB8D0BB047"),
            Name = "Medical Biller",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("AAA6E5E6-5159-5CA9-8885-86C1EE3BD1BA"),
            Name = "Optician",
            DisplayOrder = 105,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("16495EBF-C49D-5C85-822D-33E2B32D3007"),
            Name = "Orthotist",
            DisplayOrder = 105,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("5F5558C4-545F-5BF4-82B2-958EDE037154"),
            Name = "Prosthetist",
            DisplayOrder = 124,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("AD88EFCF-0C61-5936-B3D2-9C6FB6CE35DC"),
            Name = "Hearing Aid Specialist",
            DisplayOrder = 83,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("27758B45-2D79-5D47-A2F6-15678ADAFAB0"),
            Name = "Health Information Technician",
            DisplayOrder = 83,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("AA9FCC82-CC27-571D-9D38-35C3B8C454F1"),
            Name = "Athletic Trainer",
            DisplayOrder = 9,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("67AEB12C-344F-567E-884F-F889B0FE8117"),
            Name = "Genetic Counselor",
            DisplayOrder = 80,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("FB5F6C64-57FF-53F8-805F-C02CEB82242C"),
            Name = "Surgical Assistant",
            DisplayOrder = 156,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("6852CF90-6E37-519C-BFFC-43D23D26CBDE"),
            Name = "Healthcare Worker",
            DisplayOrder = 83,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("6508CB67-EBAD-57BE-A834-4AF951BB0BD8"),
            Name = "Home Health Aide",
            DisplayOrder = 83,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("F89199EC-3DC0-563D-9EB9-96A2417CD459"),
            Name = "Personal Care Aide",
            DisplayOrder = 106,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("304E3E20-524C-5C3A-A6B0-CA3813FB137B"),
            Name = "Caregiver",
            DisplayOrder = 17,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("2C10D0F7-E86F-5BC2-8B84-142EA88FAADA"),
            Name = "Nursing Assistant",
            DisplayOrder = 100,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("03AD9893-125B-5784-A05A-4E249DB3A7E1"),
            Name = "Certified Nursing Assistant",
            DisplayOrder = 21,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("BC3B3365-333C-5E70-8AE1-104CA8F0A20E"),
            Name = "Orderly",
            DisplayOrder = 105,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("6A3F9732-A69B-5C29-838C-9A16AE0950B1"),
            Name = "Psychiatric Aide",
            DisplayOrder = 124,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("1A1C9E8E-D575-5078-860B-2D09007B70C3"),
            Name = "Occupational Therapy Assistant",
            DisplayOrder = 100,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("7C263BF1-4F07-562B-BB37-BF641AA8CF63"),
            Name = "Occupational Therapy Aide",
            DisplayOrder = 100,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("D6124FD1-5241-5052-8C65-04BDF532E030"),
            Name = "Physical Therapist Assistant",
            DisplayOrder = 112,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("6F16B0A9-50B7-50CA-908A-B440612FED25"),
            Name = "Physical Therapist Aide",
            DisplayOrder = 112,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("E2F58F78-3838-592C-A8E9-FDCF4ADBB3C4"),
            Name = "Massage Therapist",
            DisplayOrder = 94,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("9F531E81-1389-5249-BAF7-E8874FF2DB68"),
            Name = "Dental Assistant",
            DisplayOrder = 54,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("48B037C8-DB0F-532A-91E2-9D8074437734"),
            Name = "Medical Assistant",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("EEDF0B1F-5034-52B7-AB54-EED86AFD4319"),
            Name = "Sterile Processing Technician",
            DisplayOrder = 153,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("9DC9E59C-8C90-5923-8477-C9EBC4EB02B2"),
            Name = "Medical Transcriptionist",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("7FDD9DD6-4312-5EFB-A592-D4CCE5C3C543"),
            Name = "Pharmacy Aide",
            DisplayOrder = 108,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("F0465E61-0545-53CF-9D12-288CB49A97FD"),
            Name = "Veterinary Assistant",
            DisplayOrder = 169,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("DE35FE3B-D146-5631-B507-48E5DBFCDE35"),
            Name = "Phlebotomist",
            DisplayOrder = 108,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("6F07D712-D069-52C1-9582-A31CCE017673"),
            Name = "Patient Care Technician",
            DisplayOrder = 105,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("0F7D1D5C-6E0D-58A2-8DD4-555FFF14BA75"),
            Name = "Correctional Officer Supervisor",
            DisplayOrder = 41,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("9F249D36-FFCA-5BB8-A994-2821626BB122"),
            Name = "Police Sergeant",
            DisplayOrder = 118,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("FC50CB93-E4D7-5831-AE35-E94B642F033D"),
            Name = "Fire Captain",
            DisplayOrder = 72,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("232622A4-6F2D-5E69-B89D-01B8AB7E8D3A"),
            Name = "Security Supervisor",
            DisplayOrder = 142,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("0AB7BC54-5C37-5C8E-91F7-A9B855BED46F"),
            Name = "Fire Inspector",
            DisplayOrder = 72,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("58D13440-5076-5A13-A04D-DF1DEB1B274C"),
            Name = "Fire Investigator",
            DisplayOrder = 72,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("49EDFA5C-FCEA-5B6F-8426-5F57ECC88FD3"),
            Name = "Bailiff",
            DisplayOrder = 10,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("48E80FB7-6572-5032-8621-8CA3CC965B25"),
            Name = "Correctional Officer",
            DisplayOrder = 41,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("33EC5CC6-B330-5761-A631-1E4CA5BC22DE"),
            Name = "Jailer",
            DisplayOrder = 85,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("13E86F8F-E6FF-5F80-ADE1-391041B21DEC"),
            Name = "Detective",
            DisplayOrder = 56,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("C0169DFA-FD95-59F2-A350-D52BAB4C5F6D"),
            Name = "Criminal Investigator",
            DisplayOrder = 43,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("8060AE9B-4117-561D-94F7-A92F311307BD"),
            Name = "Game Warden",
            DisplayOrder = 80,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("6C8C7B30-2EC3-5FAC-BD2D-A65586ADD3BC"),
            Name = "Parking Enforcement Officer",
            DisplayOrder = 105,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("F59FCFD7-EF5C-5AF1-8F72-23756E090878"),
            Name = "Sheriff's Deputy",
            DisplayOrder = 149,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("E4A86732-67C6-5E6C-9B6E-1D44655C1D07"),
            Name = "Sheriff",
            DisplayOrder = 149,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("7E58EF02-1369-5E9C-8093-875909DC0321"),
            Name = "State Trooper",
            DisplayOrder = 153,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("A92864D5-53B0-59DB-9930-7012E216D23B"),
            Name = "Transit Police Officer",
            DisplayOrder = 161,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("0ACBB5DA-59BF-5252-B8E4-A47D9B106041"),
            Name = "Animal Control Officer",
            DisplayOrder = 7,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("931FF928-E13B-57DC-B002-46845FD01007"),
            Name = "Private Investigator",
            DisplayOrder = 119,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("EE986AEB-9FCB-518C-AFD0-9ABF3D8384BA"),
            Name = "Crossing Guard",
            DisplayOrder = 43,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("75B2FD81-6A98-525E-8B01-4CBCEED96925"),
            Name = "Flagger",
            DisplayOrder = 75,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("BC9E9343-81D7-574D-B426-6CF03A440867"),
            Name = "Lifeguard",
            DisplayOrder = 89,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("AAC28F4A-12F2-51C0-97EA-413FAA3783BC"),
            Name = "Ski Patrol",
            DisplayOrder = 149,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("04B7288F-6E48-5DD9-B56E-B99F43FDCC21"),
            Name = "TSA Officer",
            DisplayOrder = 163,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("E4FD5AFF-AB90-51BF-B956-493E0351406D"),
            Name = "School Bus Monitor",
            DisplayOrder = 140,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("8A00D5F4-3011-5377-A9E1-BF14B7168C16"),
            Name = "Bodyguard",
            DisplayOrder = 13,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("623051D9-8EA3-5BE6-A275-A7CA0C616FB2"),
            Name = "Loss Prevention Officer",
            DisplayOrder = 89,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("9BFB9614-39EF-5E3B-B699-6DCEBF6073E0"),
            Name = "Head Cook",
            DisplayOrder = 83,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("E8E96DA0-9B9C-5681-B56D-EEBEF8FB2062"),
            Name = "Kitchen Manager",
            DisplayOrder = 86,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("E3ABEB68-80BC-54AD-8FB6-DF6D8BEF092C"),
            Name = "Cook",
            DisplayOrder = 41,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("C68B4FA5-7F22-5DB7-8C19-8D6C73C1D5DD"),
            Name = "Line Cook",
            DisplayOrder = 89,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("B7F27F83-111C-5C4A-B46F-7AD372BDD2D0"),
            Name = "Short Order Cook",
            DisplayOrder = 149,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("3CCEFB69-29CF-5EA2-BF90-340F4682E544"),
            Name = "Food Preparation Worker",
            DisplayOrder = 75,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("09527989-6089-5722-B8CD-2C74FA9CE239"),
            Name = "Bartender",
            DisplayOrder = 11,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("807C8292-48A1-5E0F-A42A-564781BB668F"),
            Name = "Fast Food Worker",
            DisplayOrder = 70,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("D94C630B-8C9C-51A6-ADE2-C57454824433"),
            Name = "Counter Attendant",
            DisplayOrder = 41,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("2C006845-E272-5225-AC00-78E161E10642"),
            Name = "Barista",
            DisplayOrder = 11,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("60621823-3163-52EC-A7DE-FCC85E61D1F7"),
            Name = "Server",
            DisplayOrder = 145,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("71BFFD28-775D-54AC-85F2-BE3DF320D413"),
            Name = "Waiter",
            DisplayOrder = 169,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("953E38F5-FF75-56EF-B656-4966B2B7AACC"),
            Name = "Waitress",
            DisplayOrder = 169,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("5BCC3FE0-F178-5EBC-929B-9EDC83EAEC05"),
            Name = "Food Server",
            DisplayOrder = 75,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("A534F5AE-38EA-5EC2-A9D4-848456F6491C"),
            Name = "Busser",
            DisplayOrder = 17,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("3BB27A0F-2190-56D3-B1D0-C1ABCCB47F3F"),
            Name = "Dishwasher",
            DisplayOrder = 62,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("C27DB818-D587-5399-BA53-B73685945DEE"),
            Name = "Host",
            DisplayOrder = 83,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("6B24E319-416C-5D91-8B36-AD97BD9D76B0"),
            Name = "Hostess",
            DisplayOrder = 83,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("DAE64779-4D48-5B8A-B4CB-E1734F34B245"),
            Name = "Caterer",
            DisplayOrder = 21,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("E654AC90-898F-5C66-B995-91CB311CA93A"),
            Name = "Baker",
            DisplayOrder = 10,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("C4770EF4-C4E1-5E5E-9EC6-03A53DC81D7B"),
            Name = "Housekeeping Supervisor",
            DisplayOrder = 83,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("81E36CFA-A779-59DE-84BE-48EEBFAEB393"),
            Name = "Landscaping Supervisor",
            DisplayOrder = 87,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("31322E2C-F756-5A8C-8452-55DAE2D86824"),
            Name = "Custodian",
            DisplayOrder = 44,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("1C350E32-8B97-51E2-8DDD-A62FB84E3318"),
            Name = "Cleaner",
            DisplayOrder = 25,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("E6650F5C-89E1-51C2-8006-24A24A8B7548"),
            Name = "Maid",
            DisplayOrder = 91,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("55B775E5-51AA-50B4-983E-2A8155B05BB6"),
            Name = "Housekeeper",
            DisplayOrder = 83,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("A13D3DE9-6E70-54CE-9E21-458719536B1E"),
            Name = "House Cleaner",
            DisplayOrder = 83,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("A9485D36-D384-5130-8218-4F7823C810D9"),
            Name = "Pest Control Technician",
            DisplayOrder = 106,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("6B2C26FD-F315-5E4A-845D-CEBE61B1A805"),
            Name = "Landscaper",
            DisplayOrder = 87,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("72023DF8-A53A-5118-A7CA-7FD7CEE3BF49"),
            Name = "Groundskeeper",
            DisplayOrder = 81,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("B1503410-421D-5943-9EC6-25151BA60630"),
            Name = "Gardener",
            DisplayOrder = 80,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("E3F0D225-1988-5A90-8C29-7F172EDD6C6A"),
            Name = "Lawn Care Worker",
            DisplayOrder = 87,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("3FFD549C-29E8-5510-AB8F-20D462DA0716"),
            Name = "Pesticide Applicator",
            DisplayOrder = 106,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("C9676B76-E29F-5C6D-8145-55EBB67934AB"),
            Name = "Tree Trimmer",
            DisplayOrder = 161,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("1B69C793-BB09-517F-914B-9DF7159D92D7"),
            Name = "Arborist",
            DisplayOrder = 7,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("DF47F0EA-FD73-585A-B626-F3ACBEDE7228"),
            Name = "Casino Supervisor",
            DisplayOrder = 21,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("13F7BE22-7E9F-5ABA-A1E4-2C21C30F1F4B"),
            Name = "Recreation Supervisor",
            DisplayOrder = 127,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("BA2F94F6-FD90-5AC4-9E83-AE466A4F0A1F"),
            Name = "Personal Service Supervisor",
            DisplayOrder = 106,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("CA90569B-8CEE-540D-886B-4605A03EAD86"),
            Name = "Animal Trainer",
            DisplayOrder = 7,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("F3049351-321D-59C9-8C87-C0E369ADCE7A"),
            Name = "Dog Trainer",
            DisplayOrder = 67,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("5C4AD763-0A25-5B07-ABE8-350932CAECCA"),
            Name = "Animal Caretaker",
            DisplayOrder = 7,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("075A4D6F-D492-522D-A6B1-9E34ACC3E38B"),
            Name = "Pet Groomer",
            DisplayOrder = 106,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("415AA474-916F-5AD3-A010-35861387A677"),
            Name = "Dog Walker",
            DisplayOrder = 67,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("57251983-B792-57FF-A993-7270C597F456"),
            Name = "Pet Sitter",
            DisplayOrder = 106,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("A76A8D59-EB28-515B-9C09-1494887F09A0"),
            Name = "Casino Dealer",
            DisplayOrder = 21,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("0908661D-E80A-5C07-8328-874E51218E45"),
            Name = "Card Dealer",
            DisplayOrder = 17,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("D7B329B1-C626-5317-BB17-C0929E13928E"),
            Name = "Sports Book Writer",
            DisplayOrder = 153,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("CAE07BDC-50DD-5F05-9219-3C5EE89BED85"),
            Name = "Projectionist",
            DisplayOrder = 122,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("5950F832-6BD9-53B0-86FB-36030E532DBC"),
            Name = "Usher",
            DisplayOrder = 165,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("6C10ED5D-A91F-5855-BB88-D7572E9219F9"),
            Name = "Ticket Taker",
            DisplayOrder = 161,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("1E80F53C-852F-5E74-BD3B-7EEB0BAAA839"),
            Name = "Amusement Park Attendant",
            DisplayOrder = 7,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("C0638811-96A7-5A1E-9E40-8FBE0692C032"),
            Name = "Recreation Attendant",
            DisplayOrder = 127,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("D006F8F2-3C2C-544B-8FBB-14EE22BA6116"),
            Name = "Costume Attendant",
            DisplayOrder = 41,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("4306A7E1-6C55-549C-B2C1-BB744A36DB38"),
            Name = "Locker Room Attendant",
            DisplayOrder = 89,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("583ADFD0-F0CD-505B-8979-F142A9671E1D"),
            Name = "Embalmer",
            DisplayOrder = 68,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("460FA4CB-2164-5322-9B85-9057C9C6603E"),
            Name = "Crematory Operator",
            DisplayOrder = 43,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("3B6C2804-25F2-5A9F-AE69-24606F9FBD7F"),
            Name = "Funeral Attendant",
            DisplayOrder = 80,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("E93E868F-9C06-538F-9450-CD31354DBAF7"),
            Name = "Funeral Director",
            DisplayOrder = 80,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("B60B369F-F6F4-5A1F-8FB2-EF4FCC7393A9"),
            Name = "Mortician",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("1E46BC33-B12E-5E09-BC91-95133E77F81A"),
            Name = "Barber",
            DisplayOrder = 11,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("9A4E106A-7D5E-5A97-B234-F653D069338F"),
            Name = "Hairstylist",
            DisplayOrder = 83,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("F5611A10-C3EA-5BF6-BF2D-C7AA3B9B4AB2"),
            Name = "Cosmetologist",
            DisplayOrder = 41,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("FA419430-4F8C-5AAB-A585-504246F47D4A"),
            Name = "Makeup Artist",
            DisplayOrder = 91,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("403B3B00-9B9E-5C58-A6AD-AB212EFC0A9F"),
            Name = "Manicurist",
            DisplayOrder = 91,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("2EC7B5AD-CFD0-5A57-86C9-89E0FEA54880"),
            Name = "Nail Technician",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("D64936AE-43B6-5D31-AFFA-B16F9E51EC71"),
            Name = "Esthetician",
            DisplayOrder = 69,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("7F1B7942-BBCF-534C-BAA3-C38955B3182E"),
            Name = "Skincare Specialist",
            DisplayOrder = 149,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("67CA5990-F965-5032-A291-8FD75414590A"),
            Name = "Shampooer",
            DisplayOrder = 149,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("F609D5F3-F0EF-5182-83CE-E90B8D26E930"),
            Name = "Bellhop",
            DisplayOrder = 12,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("C487DD38-5763-5993-8317-F8E4685CA725"),
            Name = "Porter",
            DisplayOrder = 118,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("A095F39F-25D8-5AA3-91A5-8D68FBEA7DAE"),
            Name = "Concierge",
            DisplayOrder = 37,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("457DC8E3-E7D3-53D4-9110-62280A4A949B"),
            Name = "Tour Guide",
            DisplayOrder = 161,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("273459F4-ECB6-53EE-890D-C9EF17F73388"),
            Name = "Travel Guide",
            DisplayOrder = 161,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("3764DFC1-C5FF-5FE4-BE07-6596F2917F85"),
            Name = "Childcare Worker",
            DisplayOrder = 23,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("A5AF3D33-8AE8-5177-90C7-8BCCB83711FC"),
            Name = "Nanny",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("2A97DAAC-C103-5712-86FE-AC3EDD8E7D85"),
            Name = "Babysitter",
            DisplayOrder = 10,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("6995DA2F-A0A7-5551-9BA6-27E7C5086AB2"),
            Name = "Daycare Worker",
            DisplayOrder = 53,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("7157DAB2-D6E4-5022-AD01-FDAA43E6C54C"),
            Name = "Personal Trainer",
            DisplayOrder = 106,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("4AFD6DA7-7848-5438-9CB8-0CC440309A73"),
            Name = "Fitness Instructor",
            DisplayOrder = 75,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("68D2BB31-21C7-53B5-8DC2-E1E3D884FD68"),
            Name = "Yoga Instructor",
            DisplayOrder = 171,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("2FA6B7A5-FF3C-5D9C-9336-46607A6FFF89"),
            Name = "Recreation Worker",
            DisplayOrder = 127,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("827C2C35-27C6-5E63-B9DC-072BE3F5A720"),
            Name = "Camp Counselor",
            DisplayOrder = 17,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("9C4256BD-F074-5FBF-8D1E-6078B64BC260"),
            Name = "Residential Advisor",
            DisplayOrder = 132,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("7D477284-E548-575C-A13D-2E04CBE74A96"),
            Name = "Tattoo Artist",
            DisplayOrder = 156,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("1DEB4033-5320-5068-926F-DE08A983C5C6"),
            Name = "Retail Supervisor",
            DisplayOrder = 133,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("E863CF6C-5B1A-5EF6-B60C-F2DCB108A3AA"),
            Name = "Sales Supervisor",
            DisplayOrder = 138,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("612BC721-C1EF-5578-87EC-98E73C511F87"),
            Name = "Rental Clerk",
            DisplayOrder = 132,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("25D81A48-BE03-564E-A32C-7C1A9CA9A727"),
            Name = "Counter Clerk",
            DisplayOrder = 41,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("6A668A97-D9B1-5961-8339-7B9151CFE0AE"),
            Name = "Parts Salesperson",
            DisplayOrder = 105,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("3D7CFCE9-E585-59FD-A33F-0574B9A1DF52"),
            Name = "Retail Salesperson",
            DisplayOrder = 133,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("B77CC10D-7370-5327-BD43-99A1791F0D08"),
            Name = "Sales Associate",
            DisplayOrder = 136,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("187D11EA-38B6-54E9-ABE4-AE2E9BE35E1C"),
            Name = "Advertising Sales Agent",
            DisplayOrder = 5,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("D2146425-08A4-5C72-9780-0AE39C458571"),
            Name = "Insurance Agent",
            DisplayOrder = 85,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("995D3729-5B53-5ACD-9EAA-B75BB8FFDF27"),
            Name = "Insurance Sales Agent",
            DisplayOrder = 85,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("80AA59E7-28C5-5B72-8233-B19BED1F146E"),
            Name = "Financial Services Sales Agent",
            DisplayOrder = 72,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("71165604-9F87-5B6F-A86A-F2B61401AE0F"),
            Name = "Securities Sales Agent",
            DisplayOrder = 141,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("A60BCA92-D688-5FBB-B980-5BDDA68EFC23"),
            Name = "Travel Agent",
            DisplayOrder = 161,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("65A2058E-04D8-5911-943F-C3CE511DF8B3"),
            Name = "Account Executive",
            DisplayOrder = 0,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("9644D25C-7B91-5D15-9535-9CBFA7936178"),
            Name = "Manufacturer's Representative",
            DisplayOrder = 91,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("3EC77DD7-CC64-5625-A90A-9A3AD9734C3D"),
            Name = "Product Promoter",
            DisplayOrder = 120,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("F90951BB-2045-566D-9E34-C0810E9354DE"),
            Name = "Brand Ambassador",
            DisplayOrder = 14,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("E68F0A69-E1CE-53A1-A08F-9AC34F2427A2"),
            Name = "Model",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("D4D1F0A1-7BA7-5FF1-823C-27761A1AEF14"),
            Name = "Real Estate Broker",
            DisplayOrder = 126,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("B98A1A33-B27A-5D94-A98F-8824CB87128F"),
            Name = "Real Estate Investor",
            DisplayOrder = 126,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("2B4FFF9D-43A4-5CEE-8D2D-C21B76ECF5C6"),
            Name = "Sales Engineer",
            DisplayOrder = 136,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("1C1C7E4A-1A6B-51C5-84CF-5766A4882305"),
            Name = "Telemarketer",
            DisplayOrder = 161,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("E9846829-4571-58CB-B075-38ECE61C50BD"),
            Name = "Street Vendor",
            DisplayOrder = 154,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("BE1C499F-0A6B-596A-BED8-D3918A73919D"),
            Name = "Door-to-Door Salesperson",
            DisplayOrder = 67,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("E3B0BA3B-76D4-5812-AE56-A7647C47F1F4"),
            Name = "Merchant",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("777CE66C-E1EC-5D0D-9C1E-BC851D781E5C"),
            Name = "Online Seller",
            DisplayOrder = 102,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("E046257A-DE47-512C-B8AF-800DDD4B65D5"),
            Name = "E-commerce Seller",
            DisplayOrder = 67,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("DD08E37A-F8EA-5B6A-941D-410634D9E72D"),
            Name = "Office Supervisor",
            DisplayOrder = 100,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("6BA7E65B-50C7-5F3E-B803-B496B0F0335E"),
            Name = "Administrative Supervisor",
            DisplayOrder = 5,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("FB3D58AD-6F9E-5205-9DF1-7E8FAEFFCAA1"),
            Name = "Switchboard Operator",
            DisplayOrder = 156,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("7C88F9E0-9A96-5A65-8B99-F91B1B58D904"),
            Name = "Answering Service Operator",
            DisplayOrder = 7,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("9E4B0852-3E52-5061-9D68-BF18F761D0AA"),
            Name = "Telephone Operator",
            DisplayOrder = 161,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("B28D0264-DB61-57AB-ACBA-DB7E52235F17"),
            Name = "Bill Collector",
            DisplayOrder = 12,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("853B0582-47CE-5D47-9C4E-7F72A2C8EE80"),
            Name = "Debt Collector",
            DisplayOrder = 53,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("E997CB6D-BCC1-5A92-8AAA-53E39B115F27"),
            Name = "Billing Clerk",
            DisplayOrder = 12,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("B72532A0-BE48-5663-A7CD-73D293516DC2"),
            Name = "Bookkeeping Clerk",
            DisplayOrder = 14,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("B4567FAC-3BAE-57B9-A449-4EBB59F0FB8C"),
            Name = "Accounting Clerk",
            DisplayOrder = 5,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("AF773DC2-0946-58BD-9F96-8F732E6FDE7A"),
            Name = "Auditing Clerk",
            DisplayOrder = 10,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("BD29AF37-C952-59CF-85AE-F6FCF646622F"),
            Name = "Payroll Clerk",
            DisplayOrder = 106,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("5AC9A3EC-2162-5E0A-B417-CDE4C1948C3D"),
            Name = "Timekeeping Clerk",
            DisplayOrder = 161,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("F4E98CD6-1AEE-5A68-823F-6F1D5C023677"),
            Name = "Procurement Clerk",
            DisplayOrder = 119,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("E300E56F-B85D-5856-952D-3BE37277E6B7"),
            Name = "Bank Teller",
            DisplayOrder = 11,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("F7540723-2536-5636-9E0D-02615C75F969"),
            Name = "Teller",
            DisplayOrder = 161,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("0B7AC4F5-62DD-567D-9358-DD3ADA554E15"),
            Name = "Brokerage Clerk",
            DisplayOrder = 14,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("79DFC247-3AA5-5F90-88A6-C70DFA404AA5"),
            Name = "Correspondence Clerk",
            DisplayOrder = 41,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("445B8A8F-F8A7-547F-BCDF-E050A9DFEFFB"),
            Name = "Court Clerk",
            DisplayOrder = 42,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("197CD2C5-27B4-5ABC-A6F0-74362B08D4B8"),
            Name = "Municipal Clerk",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("11AC1FEE-C86F-5FD5-8E8E-30DCC8D701DE"),
            Name = "License Clerk",
            DisplayOrder = 89,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("3ED766D8-AD23-59E3-82E5-BF358BD036D5"),
            Name = "Credit Clerk",
            DisplayOrder = 42,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("5A5FE405-9DC5-5319-B009-C0B09D3E76FD"),
            Name = "Customer Service Representative",
            DisplayOrder = 48,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("B3B1DCA3-B6F5-5426-838F-6CD3D11F8182"),
            Name = "Call Center Agent",
            DisplayOrder = 17,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("3B9664C2-8AD3-5FAD-9400-7A1EC56EDEA1"),
            Name = "Eligibility Interviewer",
            DisplayOrder = 68,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("7990F94D-055E-5CE8-9CB9-F2A47557262A"),
            Name = "File Clerk",
            DisplayOrder = 70,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("96029CC7-2166-5146-A4D5-2FC3A2A7EBBF"),
            Name = "Front Desk Clerk",
            DisplayOrder = 78,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("EC625573-FD22-552E-A6CE-BC74D523087F"),
            Name = "Hotel Desk Clerk",
            DisplayOrder = 83,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("10122BDD-B765-5B61-81D7-39D9615E501A"),
            Name = "Interviewer",
            DisplayOrder = 85,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("851BC133-AF3E-57DA-AD9F-5C64E11A9FC6"),
            Name = "Library Assistant",
            DisplayOrder = 89,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("274C5BA6-87EB-5627-8B3B-E65D0E9996A5"),
            Name = "Loan Processor",
            DisplayOrder = 89,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("D668B53D-5017-5A0F-95D6-7911BFB29FCD"),
            Name = "Loan Clerk",
            DisplayOrder = 89,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("7A13E777-3469-52E9-B2FA-FC7F318CB3D7"),
            Name = "New Accounts Clerk",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("DD758E18-16AF-5EED-BA66-3D375B4AEADA"),
            Name = "Order Clerk",
            DisplayOrder = 105,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("5E919452-41F9-5445-9537-8E71DDEA6015"),
            Name = "Human Resources Assistant",
            DisplayOrder = 84,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("24F135FA-6131-59FB-BE86-50E1F81751CB"),
            Name = "Information Clerk",
            DisplayOrder = 85,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("9576E934-EE16-5A38-8E18-1627A9AE3214"),
            Name = "Reservation Agent",
            DisplayOrder = 132,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("7B75BF07-3D03-5A44-B6F2-38DEDD952CE1"),
            Name = "Ticket Agent",
            DisplayOrder = 161,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("663684FC-FC73-596C-ABBD-B3A13445985A"),
            Name = "Travel Clerk",
            DisplayOrder = 161,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("BC4B5055-BF6C-541A-B616-B77516AD92BE"),
            Name = "Records Clerk",
            DisplayOrder = 127,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("D4CC45C6-EA78-5B96-B60B-BB94FFD5B4AD"),
            Name = "Cargo Agent",
            DisplayOrder = 17,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("8904DBFA-363F-5B14-9C18-9E757F45CA2B"),
            Name = "Freight Agent",
            DisplayOrder = 78,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("FD77F689-6E29-501A-9BFB-49225A50BE1B"),
            Name = "Messenger",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("3D70018E-3898-5918-A6F6-B2FE13590055"),
            Name = "Dispatcher",
            DisplayOrder = 62,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("19162D19-E1A7-5E8D-A530-54EE1474D7B1"),
            Name = "911 Dispatcher",
            DisplayOrder = 0,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("D321A85C-1975-58B9-BCB6-FD21E2C115A4"),
            Name = "Meter Reader",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("CD78D85B-299D-533D-8AA1-C885A4633E32"),
            Name = "Postal Clerk",
            DisplayOrder = 119,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("3AE9479C-1FE7-5617-9238-2FC90C7E62B8"),
            Name = "Mail Carrier",
            DisplayOrder = 91,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("490F071C-D210-5BEA-A6D0-77EFC52ADD34"),
            Name = "Mail Sorter",
            DisplayOrder = 91,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("752D0E10-5CE4-5AFE-B786-01D5FF1865AE"),
            Name = "Production Planner",
            DisplayOrder = 120,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("AF4FFA17-0BDB-5B3C-8340-4D570775D21C"),
            Name = "Expediting Clerk",
            DisplayOrder = 70,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("18222349-889B-5016-900C-CA8B1018D8AD"),
            Name = "Shipping Clerk",
            DisplayOrder = 149,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("654384BB-ADD8-5867-A542-2D06DBF0672C"),
            Name = "Receiving Clerk",
            DisplayOrder = 126,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("8AE28FAC-322E-5994-A03D-7FD154A19172"),
            Name = "Inventory Clerk",
            DisplayOrder = 85,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("444B6A69-AC93-5AF9-AFD1-272BB514D0A1"),
            Name = "Stock Clerk",
            DisplayOrder = 153,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("82996D37-65FC-5706-AFAD-6D194A29733E"),
            Name = "Weigher",
            DisplayOrder = 170,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("86464DB4-2346-5480-9860-7CEC011F5B3C"),
            Name = "Administrative Assistant",
            DisplayOrder = 5,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("9118AADB-1916-588C-A19B-F80432C6C611"),
            Name = "Medical Secretary",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("780351E1-A231-5FA6-BE5D-17DB0BB0C47B"),
            Name = "Data Entry Clerk",
            DisplayOrder = 51,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("8EC0C91D-CCA1-504D-A136-3E4FE179B7D3"),
            Name = "Typist",
            DisplayOrder = 163,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("8C7F18C4-2F11-5A6C-A780-A3C6FAE38316"),
            Name = "Word Processor",
            DisplayOrder = 171,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("19585EA9-A1A7-5429-9635-29C41CFB1507"),
            Name = "Desktop Publisher",
            DisplayOrder = 56,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("B5324E62-C4C7-56A6-81C9-D5C61A15745D"),
            Name = "Insurance Claims Clerk",
            DisplayOrder = 85,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("49F3FF1D-0C4F-5F63-B9A4-A651EF9ABF0C"),
            Name = "Policy Processing Clerk",
            DisplayOrder = 118,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("61213863-71EA-5F60-A33E-BB8CFA6ABCDF"),
            Name = "Mail Clerk",
            DisplayOrder = 91,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("BF59AFE7-DF62-5BF8-A032-3BDF1C9B4C90"),
            Name = "Office Clerk",
            DisplayOrder = 100,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("8F7BF562-4B6C-536B-A999-24FEEA69C297"),
            Name = "Clerk",
            DisplayOrder = 25,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("5357444A-2A15-528E-9789-FEB7E41DFEDA"),
            Name = "Office Assistant",
            DisplayOrder = 100,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("5E8F6A62-1A09-580B-8E19-95CE7463285E"),
            Name = "Proofreader",
            DisplayOrder = 124,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("C27216D9-AED9-5B97-A51A-F43A0EAF3F5B"),
            Name = "Statistical Assistant",
            DisplayOrder = 153,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("7AC8F8D4-FFB2-5E82-81C7-D5646664F909"),
            Name = "Virtual Assistant",
            DisplayOrder = 169,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("8995E45C-27B5-56A4-9209-C1E0545C3C73"),
            Name = "Farm Supervisor",
            DisplayOrder = 70,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("081C6998-5A53-54D5-9DCF-956FB54585ED"),
            Name = "Agricultural Inspector",
            DisplayOrder = 5,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("E1408F7D-56EB-5AA3-AD1C-B794E8CB6AA8"),
            Name = "Animal Breeder",
            DisplayOrder = 7,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("3E0C38D6-F7F9-5B00-8932-FAC997C82B44"),
            Name = "Agricultural Grader",
            DisplayOrder = 5,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("1F29127C-6AB7-5CEF-8715-B43B2E210EB1"),
            Name = "Agricultural Equipment Operator",
            DisplayOrder = 5,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("6D7ECCBA-B797-53FE-8FD6-B91891364318"),
            Name = "Farmworker",
            DisplayOrder = 70,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("B4FF46A2-A5FB-5293-8937-D6CC14B56637"),
            Name = "Farm Laborer",
            DisplayOrder = 70,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("A825E765-494A-526E-A207-3CE717B6F581"),
            Name = "Nursery Worker",
            DisplayOrder = 100,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("727738D8-F515-5E90-8C7C-E1F483046FC1"),
            Name = "Greenhouse Worker",
            DisplayOrder = 81,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("A153495D-047E-56C6-9203-121B4C0D84C0"),
            Name = "Ranch Hand",
            DisplayOrder = 125,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("3F5986E3-203E-56E7-92C1-0D66DE86D7D8"),
            Name = "Agricultural Worker",
            DisplayOrder = 5,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("E77A777F-5358-5B5B-93E7-DC54A73B9A6C"),
            Name = "Fisher",
            DisplayOrder = 75,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("79A270B1-9AA8-5FC1-B866-A5D7D5ED5131"),
            Name = "Fisherman",
            DisplayOrder = 75,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("B4832D89-A3FF-5645-A253-85C7378B6563"),
            Name = "Hunter",
            DisplayOrder = 85,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("164B1426-145E-5474-92FC-27506259FAA4"),
            Name = "Trapper",
            DisplayOrder = 161,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("BECD7460-10E4-51DE-A6B3-B21C51D23904"),
            Name = "Forest Worker",
            DisplayOrder = 75,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("A8B85F98-D345-512E-B947-43AA618E9090"),
            Name = "Conservation Worker",
            DisplayOrder = 37,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("413FE6D0-5EA3-53C0-9B96-3BE47837DED7"),
            Name = "Logger",
            DisplayOrder = 89,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("29CD9F8D-4691-554D-9A82-87F047ADAEDC"),
            Name = "Faller",
            DisplayOrder = 70,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("2ADF875B-07E7-590A-A53D-4217378E95F0"),
            Name = "Logging Equipment Operator",
            DisplayOrder = 89,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("C2B56D26-EB6F-5058-AEAD-BFBE6EE68D1B"),
            Name = "Log Grader",
            DisplayOrder = 89,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("67E4A78F-755D-5573-9BA3-C83FC207D874"),
            Name = "Construction Supervisor",
            DisplayOrder = 37,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("1C8ED789-BA97-52D6-B348-6621362EED66"),
            Name = "Construction Foreman",
            DisplayOrder = 37,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("B317ACAB-0D76-5A8D-A760-788CD0FD9459"),
            Name = "Boilermaker",
            DisplayOrder = 13,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("0F3464A5-EB22-5484-AC0E-2B018E9798B8"),
            Name = "Bricklayer",
            DisplayOrder = 14,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("378637AA-02E8-5298-9D75-433B94B9B399"),
            Name = "Brickmason",
            DisplayOrder = 14,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("2F1B2DE8-C94D-5149-8DB5-6E9DA034FD4C"),
            Name = "Blockmason",
            DisplayOrder = 13,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("4DC4E57C-C58C-562A-8628-4ED3DBA3DEFC"),
            Name = "Stonemason",
            DisplayOrder = 154,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("4607A898-4509-5886-AB44-34253ADE8676"),
            Name = "Carpenter",
            DisplayOrder = 17,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("7762B018-8E75-52C1-A676-DE5D349C514C"),
            Name = "Carpet Installer",
            DisplayOrder = 17,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("73A35E03-D1DD-5D80-8EB6-649AFEFCAE4F"),
            Name = "Flooring Installer",
            DisplayOrder = 75,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("0D724DB6-455E-5936-9985-15A75344B313"),
            Name = "Floor Sander",
            DisplayOrder = 75,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("A615DA54-2A39-5FC4-A2D3-14F7D6B29D74"),
            Name = "Tile Setter",
            DisplayOrder = 161,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("256D4B49-EF53-5A82-9A8F-BEF7AE091023"),
            Name = "Stone Setter",
            DisplayOrder = 154,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("CCADF64C-400D-57B6-8417-64E30C915977"),
            Name = "Cement Mason",
            DisplayOrder = 21,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("E593E8EF-8519-5CF3-92C9-9E44FE1F4F5B"),
            Name = "Concrete Finisher",
            DisplayOrder = 37,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("98591C2F-8D06-51BE-A601-AE646535F283"),
            Name = "Terrazzo Worker",
            DisplayOrder = 161,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("35249E66-5C25-5C3B-A7A2-613FFA232E8F"),
            Name = "Construction Laborer",
            DisplayOrder = 37,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("8D1FDAEA-2775-5328-8641-1022B4D3DA73"),
            Name = "Paving Equipment Operator",
            DisplayOrder = 105,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("F8B0A0CD-108B-527B-9BB9-66DD19DABB5F"),
            Name = "Pile Driver Operator",
            DisplayOrder = 113,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("BCEE9552-B5E0-5B43-8557-DA3717BA6571"),
            Name = "Heavy Equipment Operator",
            DisplayOrder = 83,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("1088753F-84AF-5034-976D-9ED48901EA79"),
            Name = "Operating Engineer",
            DisplayOrder = 102,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("BD2D629C-20C5-511F-9FCB-50286D7BF849"),
            Name = "Drywall Installer",
            DisplayOrder = 67,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("CA3AE323-9503-5FC8-AC30-A500A8411E10"),
            Name = "Ceiling Tile Installer",
            DisplayOrder = 21,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("4C1B15BF-5269-51E9-9805-2CD6B67E0848"),
            Name = "Taper",
            DisplayOrder = 156,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("30567F31-9D21-5013-9AA8-6D8C6CF909DE"),
            Name = "Glazier",
            DisplayOrder = 80,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("644C3C87-EF4A-5E70-84DA-BFA21D4992A4"),
            Name = "Insulation Installer",
            DisplayOrder = 85,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("7B82E45B-DC8B-5608-A082-9491526928F3"),
            Name = "Painter",
            DisplayOrder = 105,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("6572CFCE-26CE-5513-9509-32681C0A296F"),
            Name = "House Painter",
            DisplayOrder = 83,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("82410988-2B44-5071-946B-E4B3BEFB6B6D"),
            Name = "Paperhanger",
            DisplayOrder = 105,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("427C9D9D-9395-5D67-B9C4-8CE9BB11EDCE"),
            Name = "Pipelayer",
            DisplayOrder = 115,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("B0EEC103-EDD7-526C-912F-E2A47E0D8289"),
            Name = "Pipefitter",
            DisplayOrder = 115,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("3C373A4B-21CB-5D48-841C-C552F0438BD0"),
            Name = "Steamfitter",
            DisplayOrder = 153,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("629FEF02-B89C-5595-8E86-46146B1312FB"),
            Name = "Plasterer",
            DisplayOrder = 115,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("7AB9EB05-F92F-516A-AF60-ED46A09461FD"),
            Name = "Stucco Mason",
            DisplayOrder = 154,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("FA9E2A03-F615-5628-B423-7D70272FAD07"),
            Name = "Rebar Worker",
            DisplayOrder = 126,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("86070F81-AC7C-5DBE-B412-EED17F85D6BE"),
            Name = "Roofer",
            DisplayOrder = 136,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("4DDA55DB-F370-5C1C-8ED8-165B75220F92"),
            Name = "Sheet Metal Worker",
            DisplayOrder = 149,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("777D149A-492F-58D2-81E2-930C0868ADCE"),
            Name = "Ironworker",
            DisplayOrder = 85,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("9ADBA04B-5029-5B27-BCE8-E474615D25AC"),
            Name = "Solar Panel Installer",
            DisplayOrder = 153,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("4EB83C7A-6D83-5E9A-BC0B-55C9690E4F69"),
            Name = "Construction Helper",
            DisplayOrder = 37,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("47BF64BC-3C44-5C70-991E-7558511F4A75"),
            Name = "Building Inspector",
            DisplayOrder = 14,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("76B5F95D-00C1-56A1-8B26-F773900548D5"),
            Name = "Construction Inspector",
            DisplayOrder = 37,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("6E0D045A-B0CC-5DFE-88C3-15A2EEC605A9"),
            Name = "Home Inspector",
            DisplayOrder = 83,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("F956D267-5756-526E-9880-90945E9836B3"),
            Name = "Elevator Installer",
            DisplayOrder = 68,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("D535D878-8B82-54D9-9FBB-85AFE763753B"),
            Name = "Elevator Repairer",
            DisplayOrder = 68,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("DBB68718-8143-581F-A083-B44DBBD8594E"),
            Name = "Fence Installer",
            DisplayOrder = 70,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("ECB96928-F1F6-5AB0-9E6F-6D572E77C525"),
            Name = "Hazardous Materials Removal Worker",
            DisplayOrder = 83,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("A4737C04-991E-5141-8016-183E6CE331FA"),
            Name = "Asbestos Abatement Worker",
            DisplayOrder = 9,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("31856878-DAB0-52DA-B462-F70C0172DC4B"),
            Name = "Highway Maintenance Worker",
            DisplayOrder = 83,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("C0B675D3-7B28-546F-9157-09104F8986BD"),
            Name = "Track Maintenance Worker",
            DisplayOrder = 161,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("681DDAA5-1D3F-5D44-B41E-D1C452348701"),
            Name = "Septic Tank Servicer",
            DisplayOrder = 145,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("62609645-B42E-5453-8F6A-BF87B52387DF"),
            Name = "Sewer Pipe Cleaner",
            DisplayOrder = 149,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("87004C4D-AED3-587A-AC7C-D20132DF978D"),
            Name = "Handyman",
            DisplayOrder = 83,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("C89B8BD7-8AB1-52F1-ACDB-41AEF0E9D2AE"),
            Name = "Remodeler",
            DisplayOrder = 128,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("6F24883C-1FB4-5F64-8120-85DE577E3EF2"),
            Name = "Derrick Operator",
            DisplayOrder = 56,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("95848BF1-8976-51E1-9201-53DF68ED9098"),
            Name = "Rotary Drill Operator",
            DisplayOrder = 136,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("FD7DF4C8-5B44-565B-B716-37BF98E6BC66"),
            Name = "Oil Field Worker",
            DisplayOrder = 100,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("5418375E-6C08-5760-A940-3A9EA78BB26A"),
            Name = "Excavator Operator",
            DisplayOrder = 69,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("55C6B5E7-CAA1-5DB8-851A-14641FC4770E"),
            Name = "Driller",
            DisplayOrder = 67,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("1B83E0C6-DFD8-5973-B80C-6C8AF1D9B6D8"),
            Name = "Well Driller",
            DisplayOrder = 170,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("9166DBFA-DB6F-537A-B058-F65653895C00"),
            Name = "Blaster",
            DisplayOrder = 12,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("F43D643D-3EA3-53D1-AC84-6CB9D5BC6AE4"),
            Name = "Explosives Worker",
            DisplayOrder = 70,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("6BA4862E-86DF-5E8E-831D-597AEF669480"),
            Name = "Miner",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("26892B94-8A28-5F75-A840-5F3626F4186C"),
            Name = "Coal Miner",
            DisplayOrder = 33,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("2F854D9E-9062-5B8A-9432-271B7C860176"),
            Name = "Roof Bolter",
            DisplayOrder = 136,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("03846F9F-E98A-5966-B01D-38538A053FBD"),
            Name = "Quarry Worker",
            DisplayOrder = 125,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("5AEFE707-4345-562E-9FBC-3E7DC0298F95"),
            Name = "Roustabout",
            DisplayOrder = 136,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("2AA52AE4-2D2B-51BF-810A-ED9A98FBFA47"),
            Name = "Extraction Worker",
            DisplayOrder = 70,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("4889A9AE-7B56-5E85-B1EB-20A921169FEC"),
            Name = "Maintenance Supervisor",
            DisplayOrder = 91,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("8C78930D-2898-5162-A7FD-876EE69F1371"),
            Name = "Computer Repair Technician",
            DisplayOrder = 37,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("42C8ABB1-1647-55DF-846F-8583F707F624"),
            Name = "ATM Technician",
            DisplayOrder = 9,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("E85EC128-4E70-5461-91FF-F3E2D9477E89"),
            Name = "Office Machine Repairer",
            DisplayOrder = 100,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("A7056BFB-CC92-538A-8BD0-8D917B00EA6A"),
            Name = "Cell Tower Technician",
            DisplayOrder = 21,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("E6D3894E-A9A6-544B-A587-10214F03C862"),
            Name = "Telecommunications Technician",
            DisplayOrder = 161,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("0B2B75FD-5A8A-5116-BCAF-879DF03FB6C5"),
            Name = "Avionics Technician",
            DisplayOrder = 10,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("F56B3793-AB45-554F-977A-2A1C058CDBAA"),
            Name = "Motor Repairer",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("403EFF95-1FE5-59A9-AE1D-E82F957E6D95"),
            Name = "Electrical Repairer",
            DisplayOrder = 67,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("12021A9F-CD5F-5FC5-8354-57B9C6CD2279"),
            Name = "Electronics Repairer",
            DisplayOrder = 68,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("DDA91A79-3E5E-5DBB-A04E-C3363A5B4C31"),
            Name = "Car Audio Installer",
            DisplayOrder = 17,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("24D4E309-E8C6-5C55-A9D3-046632D62E46"),
            Name = "Audiovisual Technician",
            DisplayOrder = 10,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("F144EAD5-1596-57AE-B5E7-B0FD55E8D3DD"),
            Name = "Alarm Installer",
            DisplayOrder = 5,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("E6AB7463-6DE0-581B-9D4E-225C8CE4A4E5"),
            Name = "Security Systems Installer",
            DisplayOrder = 142,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("E6102D62-C632-5DFA-89E3-4800A2D1B2DA"),
            Name = "Aircraft Mechanic",
            DisplayOrder = 5,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("60885699-621D-5C7B-A3D1-E322BCC950CC"),
            Name = "Auto Body Technician",
            DisplayOrder = 10,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("FBE04EB2-A9B3-5820-BB64-C649165221C1"),
            Name = "Auto Glass Installer",
            DisplayOrder = 10,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("5916CFC7-5B23-58CB-A6FC-BECAE5F51616"),
            Name = "Automotive Technician",
            DisplayOrder = 10,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("4DA36A51-0059-52E0-9013-3FF774D5A7B3"),
            Name = "Auto Mechanic",
            DisplayOrder = 10,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("EC39CD7A-7BD4-5AE8-8D27-6D79466A7BE7"),
            Name = "Diesel Mechanic",
            DisplayOrder = 57,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("01335F20-8F01-5B08-9D0E-2BD3873CAC27"),
            Name = "Truck Mechanic",
            DisplayOrder = 163,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("49813405-B816-5771-94E1-D63AEB236720"),
            Name = "Bus Mechanic",
            DisplayOrder = 14,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("12FAE53B-6F3B-53B5-887D-5A37ED61E070"),
            Name = "Farm Equipment Mechanic",
            DisplayOrder = 70,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("C754A6F1-A490-5A68-858E-44AA091D80EB"),
            Name = "Heavy Equipment Mechanic",
            DisplayOrder = 83,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("FDB13FC7-24CA-5068-AF6F-89A0DB8ABA9E"),
            Name = "Rail Car Repairer",
            DisplayOrder = 125,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("1507E5C1-7EDA-5970-99CA-FEABA5692023"),
            Name = "Boat Mechanic",
            DisplayOrder = 13,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("A8E10B0E-4761-5228-A494-1B304230E239"),
            Name = "Motorcycle Mechanic",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("7AA98360-721C-5D81-AEB7-E0675A255E6C"),
            Name = "Small Engine Mechanic",
            DisplayOrder = 149,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("4BB510F2-73D6-5BAC-B5B4-2EFFDE8BD825"),
            Name = "Bicycle Repairer",
            DisplayOrder = 12,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("2434433A-BD62-5EB9-A82A-8EC83AD8A77D"),
            Name = "RV Technician",
            DisplayOrder = 136,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("CF33C3BC-1809-5D51-94B2-B4E1B3AA0CA9"),
            Name = "Tire Technician",
            DisplayOrder = 161,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("A7616BF1-D268-5970-B13F-AF3DAD6E03FA"),
            Name = "Door Repairer",
            DisplayOrder = 67,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("6D95936C-1379-557D-8ED3-8DE72A68B442"),
            Name = "Valve Technician",
            DisplayOrder = 167,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("31C1C26F-F783-5D6B-BD02-026C17F4C0BD"),
            Name = "HVAC Technician",
            DisplayOrder = 85,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("C7F822AD-9FD0-5AAB-8CA6-705ADE9A971D"),
            Name = "Refrigeration Technician",
            DisplayOrder = 127,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("09069B31-5D19-5883-86C8-CC395B7298CB"),
            Name = "Appliance Repair Technician",
            DisplayOrder = 7,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("384A36A7-C488-5A26-8048-BDA89750779B"),
            Name = "Industrial Machinery Mechanic",
            DisplayOrder = 85,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("F82C2BCD-0FA2-5BB2-8181-E52B4E252CAF"),
            Name = "Maintenance Worker",
            DisplayOrder = 91,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("AB5B920B-B10D-52B3-89A3-294BEC86FB6C"),
            Name = "Maintenance Technician",
            DisplayOrder = 91,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("583D6BC7-84A6-5683-9772-210F50E66CEC"),
            Name = "Millwright",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("AACF3E4A-AB40-5BF6-89B5-F092E3D8F45C"),
            Name = "Refractory Repairer",
            DisplayOrder = 127,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("4549ABF3-E843-5385-B3D0-0D00BC4A5489"),
            Name = "Lineman",
            DisplayOrder = 89,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("0EB6F992-95AF-5C07-8836-25C0BAECEB07"),
            Name = "Power Line Installer",
            DisplayOrder = 119,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("B0E95E38-52D8-5FBA-BF54-73216CD1D249"),
            Name = "Telecommunications Line Installer",
            DisplayOrder = 161,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("A263C3D9-A27F-51E5-8A80-9FECC3014736"),
            Name = "Cable Installer",
            DisplayOrder = 17,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("65B1B0B1-8B52-56EA-AA4D-F4CE7DDDBEB3"),
            Name = "Camera Repairer",
            DisplayOrder = 17,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("AC369545-DC7F-5AE3-BE83-B65D4B27568D"),
            Name = "Medical Equipment Repairer",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("761439A0-7842-551B-95F1-862505DE5FCB"),
            Name = "Biomedical Equipment Technician",
            DisplayOrder = 12,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("21D0C2EA-D483-59CA-A5FE-2A1B6158B7A0"),
            Name = "Musical Instrument Repairer",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("9F6D9767-C32B-53BF-8B71-D402C1726F01"),
            Name = "Piano Tuner",
            DisplayOrder = 113,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("552253F5-B546-52A8-834C-2E4ACF406091"),
            Name = "Watch Repairer",
            DisplayOrder = 169,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("DFA59F37-D15D-5A86-B2F6-F1911C8B7AB6"),
            Name = "Clock Repairer",
            DisplayOrder = 32,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("863B65D0-C168-5C48-A561-D7003131BADA"),
            Name = "Precision Instrument Repairer",
            DisplayOrder = 119,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("0A705F95-285E-55CF-B4F6-34EE01BF1294"),
            Name = "Wind Turbine Technician",
            DisplayOrder = 170,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("4C893734-39BF-5AFB-AD4E-19617EC143B4"),
            Name = "Vending Machine Technician",
            DisplayOrder = 167,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("900D0BBA-512F-5DF8-934D-4912A557B4EC"),
            Name = "Commercial Diver",
            DisplayOrder = 34,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("CA7B81BA-D5CD-5BDF-9044-A6EC3FCBE854"),
            Name = "Locksmith",
            DisplayOrder = 89,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("06E2DC2E-0D0C-5E57-9535-75B3A43CF4D3"),
            Name = "Mobile Home Installer",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("E09ADB1E-4118-5352-BFAE-16F053A87524"),
            Name = "Rigger",
            DisplayOrder = 134,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("A4FAFA2C-4927-5B5D-A7CC-00C9C85CA915"),
            Name = "Signal Repairer",
            DisplayOrder = 149,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("E5DE023A-3539-53D3-93A1-609F1B7C1094"),
            Name = "Repair Technician",
            DisplayOrder = 132,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("41356C70-71C0-54AE-9E67-296DD4612287"),
            Name = "Production Supervisor",
            DisplayOrder = 120,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("17EC9F43-BC17-5ABA-8294-6C446EB7EE59"),
            Name = "Manufacturing Supervisor",
            DisplayOrder = 91,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("5965B7AE-CC9A-5CD2-8C82-BC06949A3935"),
            Name = "Aircraft Assembler",
            DisplayOrder = 5,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("3FB4FCB7-541E-55D5-BA04-29E49C0C8E1D"),
            Name = "Coil Winder",
            DisplayOrder = 33,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("2A56F7B6-E2AC-5F4D-97CE-D0EF807AB49E"),
            Name = "Electronics Assembler",
            DisplayOrder = 68,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("16C8BF8C-E64B-54BC-804D-85352A565672"),
            Name = "Assembler",
            DisplayOrder = 9,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("B0152FB8-860A-55E3-85E4-16EADC1BB0C0"),
            Name = "Machine Assembler",
            DisplayOrder = 90,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("2F5797AA-EB36-551B-9AEA-377E5054A97C"),
            Name = "Metal Fabricator",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("51D39E8F-8198-5820-8151-E5E68B3DCF72"),
            Name = "Fabricator",
            DisplayOrder = 70,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("504166E5-6A82-566F-9C89-3580F5F6F72B"),
            Name = "Fiberglass Laminator",
            DisplayOrder = 70,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("D09479BC-86AB-5339-A53C-001947C27F75"),
            Name = "Timing Device Assembler",
            DisplayOrder = 161,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("E3772F27-8B18-56FE-B02E-5AB63CF1C63C"),
            Name = "Assembly Line Worker",
            DisplayOrder = 9,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("734673C3-FB08-5565-89C2-B6ADA97C1A41"),
            Name = "Butcher",
            DisplayOrder = 17,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("5AE5FD37-8F44-571E-8979-C8A91F035E13"),
            Name = "Meat Cutter",
            DisplayOrder = 94,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("54AEB647-6779-5EFE-9751-D3242B704CC9"),
            Name = "Fish Cutter",
            DisplayOrder = 75,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("7822A0EC-2077-5BD5-B728-3503C2BBAB5F"),
            Name = "Meat Packer",
            DisplayOrder = 94,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("AC5521FD-A2DD-537B-9BFF-6B2BBC98D1F7"),
            Name = "Food Processing Worker",
            DisplayOrder = 75,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("93CE165A-AC63-5806-B38F-A722360592E0"),
            Name = "Food Batchmaker",
            DisplayOrder = 75,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("F7ED3506-5F09-5453-8CE0-26D4B8C1E774"),
            Name = "Food Machine Operator",
            DisplayOrder = 75,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("C5144690-3827-514A-9B60-C71961932384"),
            Name = "Machine Operator",
            DisplayOrder = 91,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("D86F5598-A04D-566F-A50B-19E1FCACB0D3"),
            Name = "Machine Setter",
            DisplayOrder = 91,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("AF3E8F06-E0B2-52D9-9033-E27C17EF7F8F"),
            Name = "CNC Operator",
            DisplayOrder = 33,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("DA2240BC-E86B-5172-80EB-684A302B2C42"),
            Name = "CNC Programmer",
            DisplayOrder = 33,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("046F4D5B-5D57-52AB-85C7-60E89E588042"),
            Name = "Press Operator",
            DisplayOrder = 119,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("477BBB4A-03D6-53CA-AF41-EBAB42FF52B3"),
            Name = "Lathe Operator",
            DisplayOrder = 87,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("FAFF8943-983F-554D-8662-5E124530605F"),
            Name = "Machinist",
            DisplayOrder = 91,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("6C8CF4CA-977F-5369-B1CA-8777FE2FF56E"),
            Name = "Furnace Operator",
            DisplayOrder = 80,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("7773125C-3810-58A9-A2F9-C31D06C49136"),
            Name = "Metal Caster",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("68D2C040-8C99-5C62-87D3-BC836831E33B"),
            Name = "Model Maker",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("B610CD75-BA7D-5B9F-8DCA-E125C6B1BCC8"),
            Name = "Patternmaker",
            DisplayOrder = 105,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("368FD689-6F7D-5A49-AD12-76F773E8C63E"),
            Name = "Mold Maker",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("DA52DF39-0FC5-5DF3-8D27-3A16329F6772"),
            Name = "Tool and Die Maker",
            DisplayOrder = 161,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("0CE4822E-D060-5EBB-B393-CAFC07EB3517"),
            Name = "Welder",
            DisplayOrder = 170,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("6E64B810-6325-53AF-920A-6ED38FE0B913"),
            Name = "Solderer",
            DisplayOrder = 153,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("8BA852E0-9DFC-5635-A9CF-E4B3224B8D93"),
            Name = "Brazer",
            DisplayOrder = 14,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("6CD1B10D-312C-5AD9-A476-E0E64381A902"),
            Name = "Welding Machine Operator",
            DisplayOrder = 170,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("A4A5255F-26A9-5D4E-BE78-0A5968FD2C39"),
            Name = "Heat Treater",
            DisplayOrder = 83,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("375AAF28-DE0B-5EAD-9105-F02D4FAC4494"),
            Name = "Plater",
            DisplayOrder = 115,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("BDA1FFC2-C51F-517D-82CC-DFE941831C85"),
            Name = "Tool Grinder",
            DisplayOrder = 161,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("ED618969-E1DE-51BC-990E-F80C438AC471"),
            Name = "Metal Worker",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("D178ECFB-5DF5-591A-AB43-45AA6C21BED1"),
            Name = "Prepress Technician",
            DisplayOrder = 119,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("E6803677-8906-571A-B367-824443AB0844"),
            Name = "Printing Press Operator",
            DisplayOrder = 119,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("96748912-8141-56EE-95E7-FE6E5A306A12"),
            Name = "Printer",
            DisplayOrder = 119,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("4C16F886-0393-5D94-A9EA-08A0D31E1C6A"),
            Name = "Bookbinder",
            DisplayOrder = 13,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("5DA41A69-A4B1-5A3D-B1AD-199A8C99FEDC"),
            Name = "Laundry Worker",
            DisplayOrder = 87,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("EF473D49-4540-527C-BF8B-AB8D2CD09CD5"),
            Name = "Dry Cleaner",
            DisplayOrder = 67,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("5F1B06FF-872D-5CF8-804A-D7876507C4B1"),
            Name = "Presser",
            DisplayOrder = 119,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("ED139B68-1BF1-5F43-8127-8042D6BF2BB9"),
            Name = "Sewing Machine Operator",
            DisplayOrder = 149,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("A9F00C7E-2595-5C9D-99BF-3DA98A544B06"),
            Name = "Seamstress",
            DisplayOrder = 140,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("8AAD0B32-EB67-53C5-B144-2222764CDF60"),
            Name = "Shoe Repairer",
            DisplayOrder = 149,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("02D82A8A-8A74-59D7-BB0C-ECB732C4B4D0"),
            Name = "Leather Worker",
            DisplayOrder = 89,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("719DEC71-E7E3-5B7C-81EA-561E525E0B8B"),
            Name = "Tailor",
            DisplayOrder = 156,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("B48A2561-3357-5443-8469-0AB2FA17C7AB"),
            Name = "Dressmaker",
            DisplayOrder = 67,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("8C7278B3-17D3-51E6-82C1-5539C1D5A10C"),
            Name = "Textile Worker",
            DisplayOrder = 161,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("9B4AE70C-B978-5099-A9F4-58E2C52FB734"),
            Name = "Upholsterer",
            DisplayOrder = 165,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("B1062FA8-7CB9-5010-BAF1-49AFDBD1EC3F"),
            Name = "Cabinetmaker",
            DisplayOrder = 17,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("F6EA7971-A9A9-5B2E-AFB6-DDCEECB8AFDC"),
            Name = "Furniture Finisher",
            DisplayOrder = 80,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("882819BE-94C1-5BB2-A778-172A922ED1C0"),
            Name = "Woodworker",
            DisplayOrder = 171,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("FAF73FB6-30E8-58D8-8F53-F1E2956023AD"),
            Name = "Sawmill Worker",
            DisplayOrder = 140,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("6D2BE8A9-6429-5B9F-9979-E7A20F7F4B9E"),
            Name = "Nuclear Power Reactor Operator",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("A4665336-6E69-552A-AB87-B938D490723C"),
            Name = "Power Distributor",
            DisplayOrder = 119,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("C727CFE5-6DCE-5635-A894-99ABC2516A1C"),
            Name = "Power Plant Operator",
            DisplayOrder = 119,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("3193DB52-89A0-5001-8D72-1725AE339514"),
            Name = "Stationary Engineer",
            DisplayOrder = 153,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("BF7E9B7D-7D5B-503F-8C0C-B589DED35CF2"),
            Name = "Boiler Operator",
            DisplayOrder = 13,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("43D75795-44B7-582E-8F21-DC6C36AF2C1E"),
            Name = "Water Treatment Plant Operator",
            DisplayOrder = 169,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("5CB5E4BB-1250-5726-AA29-2E80CB6723A6"),
            Name = "Wastewater Treatment Operator",
            DisplayOrder = 169,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("39821DE3-9859-5E7F-989E-8625CD82A18E"),
            Name = "Chemical Plant Operator",
            DisplayOrder = 23,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("16000F79-02F2-551A-8501-EBBEEF9DDE6C"),
            Name = "Gas Plant Operator",
            DisplayOrder = 80,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("90189172-06B7-599D-98C9-8536ED50CD4F"),
            Name = "Refinery Operator",
            DisplayOrder = 127,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("23A34049-2807-5D75-9ABC-1832AAE767C8"),
            Name = "Plant Operator",
            DisplayOrder = 115,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("92B8B3B5-BDEF-526A-A9AB-94F564B2EFBD"),
            Name = "Chemical Equipment Operator",
            DisplayOrder = 23,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("81AFB617-9C11-5DE8-9428-1098855C25F1"),
            Name = "Mixing Machine Operator",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("DFAFCCFD-79AA-5AB0-B253-C29E5B0302B3"),
            Name = "Cutting Machine Operator",
            DisplayOrder = 49,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("4BB3CCFA-FC86-5A9C-940D-665632ACEE6B"),
            Name = "Kiln Operator",
            DisplayOrder = 86,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("D59C7DE8-83AF-5244-9737-5BC478704A88"),
            Name = "Quality Control Inspector",
            DisplayOrder = 124,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("30FD1D6A-8F5E-528E-9938-D9253AE6CA43"),
            Name = "Inspector",
            DisplayOrder = 85,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("29B00240-8E8C-551C-B0E5-6A47A4677775"),
            Name = "Tester",
            DisplayOrder = 161,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("32C615FE-E21B-5313-B838-E30F307B0341"),
            Name = "Sorter",
            DisplayOrder = 153,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("FBE6EBBE-D3D2-5BF1-B4E4-13834522A093"),
            Name = "Jeweler",
            DisplayOrder = 86,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("2BAE7A0B-5CEB-50F3-811A-800BF458C30D"),
            Name = "Goldsmith",
            DisplayOrder = 80,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("D3B8AEAE-DA6F-5836-803D-2E5CB91B0E17"),
            Name = "Dental Laboratory Technician",
            DisplayOrder = 54,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("A87156CF-E036-59A5-AD22-13EBA1CBF648"),
            Name = "Medical Appliance Technician",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("02E6A1E8-5F9D-5365-9426-5E33CFB20714"),
            Name = "Ophthalmic Laboratory Technician",
            DisplayOrder = 105,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("40AE8C72-8F3F-5288-840A-22756C4FD2C1"),
            Name = "Packaging Machine Operator",
            DisplayOrder = 105,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("ED901ABD-F732-5BCA-8E7A-408E5E3BD53B"),
            Name = "Painting Machine Operator",
            DisplayOrder = 105,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("95B2914C-7E32-56B3-9EC4-E42D45D79630"),
            Name = "Semiconductor Technician",
            DisplayOrder = 143,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("4A524707-7CA9-594D-99BF-B18540057175"),
            Name = "Photo Lab Technician",
            DisplayOrder = 108,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("C23E4F53-551A-5627-ABA2-113CC8F8D959"),
            Name = "Engraver",
            DisplayOrder = 69,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("4E8935B9-F281-563D-B3C6-949201DECAF5"),
            Name = "Etcher",
            DisplayOrder = 69,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("81D1540B-45F1-597C-9080-BFEAC2947A36"),
            Name = "Molder",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("7574F205-7EBA-5148-83A4-18D60E07A405"),
            Name = "Tire Builder",
            DisplayOrder = 161,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("41CA9C2F-C66D-5A8F-8647-2095E6B4A470"),
            Name = "Production Worker",
            DisplayOrder = 120,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("BCF70211-7D4F-5F31-9F68-BE55A63BA081"),
            Name = "Factory Worker",
            DisplayOrder = 70,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("CF46C05D-FDED-55A0-8D65-F6E5404F1BBD"),
            Name = "Production Helper",
            DisplayOrder = 120,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("833D752E-5488-5BCE-9178-53C082892BE6"),
            Name = "Brewer",
            DisplayOrder = 14,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("5DE75BB7-B727-593B-A534-949760CA0B81"),
            Name = "Winemaker",
            DisplayOrder = 170,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("294F93E4-E8AE-5684-A381-A95393B62BAB"),
            Name = "Cargo Supervisor",
            DisplayOrder = 17,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("024C22DD-289E-5B09-A1CF-91962AB44B53"),
            Name = "Transportation Supervisor",
            DisplayOrder = 161,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("2C9AE4E4-66B3-599A-A079-5C9A21BA3841"),
            Name = "Airline Pilot",
            DisplayOrder = 5,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("66BE55BA-9224-5A74-BAEC-F159256299E6"),
            Name = "Commercial Pilot",
            DisplayOrder = 34,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("2983E587-3CDE-56E1-A704-74B27935A2E2"),
            Name = "Copilot",
            DisplayOrder = 41,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("6797D2A2-C125-5BAE-9C0E-C6CD83787F82"),
            Name = "Flight Engineer",
            DisplayOrder = 75,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("3BE98C5E-C562-5CB0-AA17-B5D5C82D530D"),
            Name = "Air Traffic Controller",
            DisplayOrder = 5,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("EB4D1497-241D-554A-8AD8-311897EB10DA"),
            Name = "Airfield Operations Specialist",
            DisplayOrder = 5,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("644832D7-0D44-5E55-8D46-482BB8C20231"),
            Name = "Ambulance Driver",
            DisplayOrder = 5,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("EABD5AA7-DBD9-51AE-9B58-39706FD61E41"),
            Name = "Route Sales Driver",
            DisplayOrder = 136,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("477FDECD-799E-5EFE-9F99-063062703113"),
            Name = "Tractor-Trailer Driver",
            DisplayOrder = 161,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("1A6596FD-2129-5F12-ABE7-2F4C2C158DE0"),
            Name = "Long-Haul Truck Driver",
            DisplayOrder = 89,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("1A5BACB9-4D4B-5DB1-93B4-8EA2E89CFFF8"),
            Name = "Delivery Truck Driver",
            DisplayOrder = 54,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("649BE7FC-9FBC-5B9B-9670-0AA36CB15417"),
            Name = "School Bus Driver",
            DisplayOrder = 140,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("3CABA202-0379-586C-A081-AD5E3D36C03A"),
            Name = "Bus Driver",
            DisplayOrder = 14,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("09485C83-4BE4-528A-9A26-AD31D9D6ACF4"),
            Name = "Shuttle Driver",
            DisplayOrder = 149,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("06F03AAE-CE1C-5B5C-8F96-9D244F4F4617"),
            Name = "Chauffeur",
            DisplayOrder = 22,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("AFB8EB9F-63D2-5C5E-AB7A-330BF89922F7"),
            Name = "Limousine Driver",
            DisplayOrder = 89,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("56FCCB89-BB3B-5034-B46A-BBD7493B1029"),
            Name = "Taxi Driver",
            DisplayOrder = 160,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("4B995195-E120-5009-8FF3-061EA9058451"),
            Name = "Food Delivery Driver",
            DisplayOrder = 75,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("63E239DE-AA66-5910-8CFE-B584FFF82BF6"),
            Name = "Gig Worker",
            DisplayOrder = 80,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("616A39BE-B59E-5B9B-B924-9E0B24BE8231"),
            Name = "Locomotive Engineer",
            DisplayOrder = 89,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("43F5B503-E3F0-5807-8403-AD19E06F4659"),
            Name = "Rail Yard Engineer",
            DisplayOrder = 125,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("EE06831D-CF15-5489-B0BA-D7FCC15E61AE"),
            Name = "Railroad Worker",
            DisplayOrder = 125,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("CDD8B60C-37DD-5038-A042-ACB322D71CA2"),
            Name = "Railroad Conductor",
            DisplayOrder = 125,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("95371220-AFE9-51E3-A85F-8A1ADA507192"),
            Name = "Yardmaster",
            DisplayOrder = 171,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("09833D89-F514-58F8-9B5B-F7A5FD881A43"),
            Name = "Subway Operator",
            DisplayOrder = 154,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("73AB1DB6-224B-5C33-B202-DF83B93567F5"),
            Name = "Streetcar Operator",
            DisplayOrder = 154,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("1CC4EF43-FBE2-5BFC-82EB-4124672181F9"),
            Name = "Sailor",
            DisplayOrder = 136,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("9CB1E088-8F21-5917-AA1D-BB3DC9EFB3FC"),
            Name = "Deckhand",
            DisplayOrder = 53,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("B27E32AB-6AA5-5983-AA65-69D9DB03D9B4"),
            Name = "Ship Captain",
            DisplayOrder = 149,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("DD01C4E0-5373-5859-9972-4E13E6F200C9"),
            Name = "Ship Mate",
            DisplayOrder = 149,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("533FC986-5ADB-59FA-A967-2BAAED6D3FA9"),
            Name = "Harbor Pilot",
            DisplayOrder = 83,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("9FE678DD-2862-5C3B-AC8C-8C6AFD9C58DD"),
            Name = "Motorboat Operator",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("4F88903D-1703-5AFC-A89A-C158C9248EF9"),
            Name = "Ship Engineer",
            DisplayOrder = 149,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("8C943B8E-C637-5530-BD3D-6658F247E899"),
            Name = "Bridge Tender",
            DisplayOrder = 14,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("A5BD9F06-D613-5E29-9976-AFB44D4368FE"),
            Name = "Parking Attendant",
            DisplayOrder = 105,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("66E80B66-A6E8-5252-AEDA-8F9666C72D0E"),
            Name = "Valet",
            DisplayOrder = 167,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("8A7CFE7C-A71C-53FA-B8B8-6EA513FE8577"),
            Name = "Gas Station Attendant",
            DisplayOrder = 80,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("445B975B-8E71-5235-A49C-2CDB2EDC8B5B"),
            Name = "Car Wash Attendant",
            DisplayOrder = 17,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("A5E662CB-F2AD-57DB-8B1E-CBD0F656CBDE"),
            Name = "Aircraft Service Attendant",
            DisplayOrder = 5,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("392ACEE2-8F1D-503F-A4A1-4DBD3EE9A116"),
            Name = "Traffic Technician",
            DisplayOrder = 161,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("5EA59A74-D68B-5E41-A403-F80836AA6731"),
            Name = "Transportation Inspector",
            DisplayOrder = 161,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("45CC573A-D19A-53E2-ADE1-AA7A8CCEF301"),
            Name = "Flight Attendant",
            DisplayOrder = 75,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("530B99C1-CEDE-579F-9A51-0093AF3D9A34"),
            Name = "Passenger Attendant",
            DisplayOrder = 105,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("E9FACFAD-ABC7-50C7-A247-90CD285530A3"),
            Name = "Conveyor Operator",
            DisplayOrder = 41,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("C3B77DD4-76EF-5EC3-B9EA-CA1DE90AA7B7"),
            Name = "Crane Operator",
            DisplayOrder = 42,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("2DF9C44B-CDF4-509B-A412-DBBA06708940"),
            Name = "Dredge Operator",
            DisplayOrder = 67,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("92DC6C2A-52FD-5EAD-B115-E03BB1CBDB8A"),
            Name = "Hoist Operator",
            DisplayOrder = 83,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("7ECEC212-CE3B-5E16-B801-3F948058A992"),
            Name = "Forklift Operator",
            DisplayOrder = 75,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("F658ADBD-7260-53D1-BA25-AE76B4422AF5"),
            Name = "Vehicle Cleaner",
            DisplayOrder = 167,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("F422796F-1C0A-5E1D-A5BC-809FC6DE4BF0"),
            Name = "Warehouse Worker",
            DisplayOrder = 169,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("9EC27D8A-8C89-5E95-92E3-201E34A2358D"),
            Name = "Material Handler",
            DisplayOrder = 94,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("409C0F6A-01B5-56C8-A127-C33893B67A1E"),
            Name = "Laborer",
            DisplayOrder = 87,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("94945E12-A918-5AA1-824C-EFA7CBA48C8D"),
            Name = "Mover",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("4D3250AD-4AB3-58B0-9DB1-9E07E32CE0DF"),
            Name = "Stocker",
            DisplayOrder = 154,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("E8B85851-D71A-55F3-827B-9F50987257C4"),
            Name = "Order Picker",
            DisplayOrder = 105,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("8A7FEC2A-2B3D-505C-9A39-7E3FA2885486"),
            Name = "Packer",
            DisplayOrder = 105,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("A332DF65-3763-55EA-A6FD-D4395155DABF"),
            Name = "Machine Feeder",
            DisplayOrder = 90,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("FA22D8FA-75FD-52C8-B3C2-64833724C2FF"),
            Name = "Pump Operator",
            DisplayOrder = 124,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("E7735D64-2D32-52D7-B089-56CBD9744021"),
            Name = "Garbage Collector",
            DisplayOrder = 80,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("1230DB4C-D7BF-5059-B080-BB36859990E5"),
            Name = "Recycling Collector",
            DisplayOrder = 127,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("13D2C6A2-0CBE-55F8-9687-4FE824BB4594"),
            Name = "Loader",
            DisplayOrder = 89,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("A0C6D57D-AA85-55E0-A2F3-2DB5D021432B"),
            Name = "Military Officer",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("BECF3516-F1CD-5373-9DE2-A1622A25058B"),
            Name = "Enlisted Military Member",
            DisplayOrder = 69,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("3B3542B7-AB4D-5C6C-BE7C-F0E6D4A657BA"),
            Name = "Military Service Member",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("E62CD991-B4F8-5F7A-89C9-98F71C1EE40F"),
            Name = "National Guard Member",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("6492AC44-C9E7-571A-A8CA-4156E898376D"),
            Name = "Army Soldier",
            DisplayOrder = 9,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("34862C72-0570-5D6F-83E7-8C4C610783D7"),
            Name = "Navy Sailor",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("1EA851D6-0A7B-5CF9-9291-F8EE46B7D4D4"),
            Name = "Air Force Airman",
            DisplayOrder = 5,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("1EC6DBCC-D9EC-5DB0-8257-E1DF4973F898"),
            Name = "Marine",
            DisplayOrder = 91,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("156006C0-4887-5A69-A2D2-C20212561028"),
            Name = "Coast Guard Member",
            DisplayOrder = 33,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("731BC2F3-A10A-5524-9BD0-2EA18A752518"),
            Name = "Space Force Guardian",
            DisplayOrder = 153,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("B629C94F-AA1A-54F9-A9F8-ED7ED1AA23F4"),
            Name = "Military Reservist",
            DisplayOrder = 97,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("4ECB4256-E2B3-559B-A173-F30A4626C6E3"),
            Name = "Retired",
            DisplayOrder = 133,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("6240F224-AD5E-5294-8927-4A3DB392BC7C"),
            Name = "Student",
            DisplayOrder = 154,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("9D3669E1-2845-59F2-B394-5F02F84FD36C"),
            Name = "Unemployed",
            DisplayOrder = 165,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("48D1657D-B7F3-568E-AFB1-4E0C3D2F4E9D"),
            Name = "Homemaker",
            DisplayOrder = 83,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("B9A3BA68-3616-5722-928A-09EBBC920DC2"),
            Name = "Stay-at-Home Parent",
            DisplayOrder = 153,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("7CB0B91F-E204-5E56-98DE-6BD688F63C3F"),
            Name = "Disabled",
            DisplayOrder = 62,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("EA0A971B-149E-58FB-8BCB-8526F4BC095F"),
            Name = "Volunteer",
            DisplayOrder = 169,
            IsActive = true,
        },
        new
        {
            Id = Guid.Parse("FCFEEACF-F4CE-58E7-A502-3C8644EC451D"),
            Name = "Other",
            DisplayOrder = 105,
            IsActive = true,
        },
    ];
}
