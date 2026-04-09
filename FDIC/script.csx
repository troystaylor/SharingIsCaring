using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Web;
using Newtonsoft.Json.Linq;

public class Script : ScriptBase
{
    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        if (this.Context.OperationId == "GetResponseSchema")
        {
            return HandleGetResponseSchema();
        }

        var apiKey = this.Context.ConnectionParameters["api_key"]?.ToString();
        if (!string.IsNullOrEmpty(apiKey))
        {
            var uriBuilder = new UriBuilder(this.Context.Request.RequestUri);
            var query = HttpUtility.ParseQueryString(uriBuilder.Query);
            query["api_key"] = apiKey;
            uriBuilder.Query = query.ToString();
            this.Context.Request.RequestUri = uriBuilder.Uri;
        }

        return await this.Context.SendAsync(this.Context.Request, this.CancellationToken);
    }

    private HttpResponseMessage HandleGetResponseSchema()
    {
        var query = HttpUtility.ParseQueryString(this.Context.Request.RequestUri.Query);
        var dataset = (query.Get("dataset") ?? "institutions").ToLowerInvariant();

        string fieldDefs;
        bool hasTotals = false;

        switch (dataset)
        {
            case "institutions": fieldDefs = FieldDefs_institutions; break;
            case "locations": fieldDefs = FieldDefs_locations; break;
            case "history": hasTotals = true; fieldDefs = FieldDefs_history; break;
            case "summary": hasTotals = true; fieldDefs = FieldDefs_summary; break;
            case "failures": hasTotals = true; fieldDefs = FieldDefs_failures; break;
            case "financials": hasTotals = true; fieldDefs = FieldDefs_financials; break;
            case "sod": hasTotals = true; fieldDefs = FieldDefs_sod; break;
            case "demographics": hasTotals = true; fieldDefs = FieldDefs_demographics; break;
            default: fieldDefs = FieldDefs_institutions; break;
        }

        var fields = ParseFieldDefs(fieldDefs);

        var schemaProps = new JObject
        {
            ["meta"] = new JObject
            {
                ["type"] = "object",
                ["x-ms-summary"] = "Metadata",
                ["properties"] = new JObject
                {
                    ["total"] = new JObject { ["type"] = "integer", ["x-ms-summary"] = "Total Results" },
                    ["parameters"] = new JObject { ["type"] = "object", ["x-ms-summary"] = "Query Parameters" },
                    ["index"] = new JObject
                    {
                        ["type"] = "object",
                        ["x-ms-summary"] = "Index Info",
                        ["properties"] = new JObject
                        {
                            ["name"] = new JObject { ["type"] = "string", ["x-ms-summary"] = "Index Name" },
                            ["createTimestamp"] = new JObject { ["type"] = "string", ["x-ms-summary"] = "Index Created" }
                        }
                    }
                }
            },
            ["data"] = new JObject
            {
                ["type"] = "array",
                ["x-ms-summary"] = "Results",
                ["items"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["data"] = new JObject
                        {
                            ["type"] = "object",
                            ["x-ms-summary"] = "Record",
                            ["properties"] = fields
                        }
                    }
                }
            }
        };

        if (hasTotals)
        {
            schemaProps["totals"] = new JObject
            {
                ["type"] = "object",
                ["x-ms-summary"] = "Totals",
                ["properties"] = new JObject
                {
                    ["count"] = new JObject { ["type"] = "integer", ["x-ms-summary"] = "Total Count" }
                }
            };
        }

        var schema = new JObject { ["type"] = "object", ["properties"] = schemaProps };
        var result = new JObject { ["schema"] = schema };

        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Content = CreateJsonContent(result.ToString(Newtonsoft.Json.Formatting.None));
        return response;
    }

    private static JObject ParseFieldDefs(string defs)
    {
        var p = new JObject();
        foreach (var line in defs.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('|');
            if (parts.Length >= 3)
            {
                var fieldType = parts[1] == "n" ? "number" : "string";
                p[parts[0]] = new JObject { ["type"] = fieldType, ["x-ms-summary"] = parts[2] };
            }
        }
        return p;
    }

    private static string FieldDefs_institutions = @"ACTIVE|n|Institution Status
ADDRESS|s|Street Address
ASSET|n|Total assets
BKCLASS|s|Institution Class
CB|s|Community Bank
CBSA|s|Core Based Statistical Area Name
CBSA_DIV|s|Metropolitan Divisions Name
CBSA_DIV_FLG|s|Metropolitan Divisions Flag
CBSA_DIV_NO|s|Metropolitan Divisions Number
CBSA_METRO|s|Metropolitan Division Number
CBSA_METRO_FLG|s|Metropolitan Division Flag
CBSA_METRO_NAME|s|Metropolitan Division Name
CBSA_MICRO_FLG|s|Micropolitan Division Flag
CBSA_NO|s|Core Based Statistical Areas
CERT|s|FDIC Certificate #
CERTCONS|s|Directly owned by another bank (CERT)
CFPBEFFDTE|s|CFPB Effective Date
CFPBENDDTE|s|CFPB End Date
CFPBFLAG|s|CFPB Flag
PRIORNAME1|s|Previous Name 1
CHANGEC1|s|Change Code
CHANGEC2|s|Change Code
CHANGEC3|s|Change Code
CHANGEC4|s|Change Code
CHANGEC5|s|Change Code
CHANGEC6|s|Change Code
CHANGEC7|s|Change Code
CHANGEC8|s|Change Code
CHANGEC9|s|Change Code
CHANGEC10|s|Change Code
CHANGEC11|s|Change Code
CHANGEC12|s|Change Code
CHANGEC13|s|Change Code
CHANGEC14|s|Change Code
CHANGEC15|s|Change Code
CHARTER|s|OCC Charter Number
CHRTAGNT|s|Chartering Agency
CITY|s|City
CITYHCR|s|City of High Holder
CLCODE|n|Numeric code
CONSERVE|s|Conservatorship
COUNTY|s|County
CSA|s|Combined Statistical Area Name
CSA_NO|s|Numeric Code for the Combined Statistical Area
CSA_FLG|s|CSA Area Flag
DATEUPDT|s|Last update
DENOVO|s|Denovo Institution
DEP|n|Total deposits
DEPDOM|n|Deposits held in domestic offices
DOCKET|s|OTS Docket Number
EFFDATE|s|Last Structure Change Effective Date
ENDEFYMD|s|End date
EQ|s|Equity capital
ESTYMD|s|Established Date
FDICDBS|s|FDIC Geographic Region
FDICREGN|s|FDIC Supervisory Region
FDICSUPV|s|Federal Reserve District
FED|s|Federal Reserve ID Number
FED_RSSD|s|Federal Reserve ID Number
FEDCHRTR|s|Federal charter flag
FORM31|s|FFIEC Call Report 31 Filer
HCTMULT|s|Bank Holding Company Type
IBA|s|Insured offices of foreign banks
INACTIVE|n|Inactive
INSAGNT1|s|Primary Insurance Agency
INSAGNT2|s|Secondary Insurance Fund
INSBIF|s|Bank Insurance Fund
INSCOML|s|Insured commercial banks
INSDATE|s|Date of Deposit Insurance
INSDROPDATE_RAW|s|Date of Dropped Deposit Insurance
INSDROPDATE|s|Date of Dropped Deposit Insurance
INSDIF|s|Deposit Insurance Fund member
INSFDIC|n|FDIC Insured
INSSAIF|s|SAIF Insured
INSSAVE|s|Insured Savings Institution
INSTAG|s|Agricultural lending institution indicator
INSTCRCD|s|Credit Card Institutions
LATITUDE|n|Location Address Latitude
LAW_SASSER_FLG|s|Law Sasser Flag
LONGITUDE|n|Location Address Longitude
MDI_STATUS_CODE|s|Minority Status Code
MDI_STATUS_DESC|s|Minority Status Description
MUTUAL|s|Ownership Type
NAME|s|Institution name
NAMEHCR|s|Bank Holding Company (Regulatory Top Holder)
NETINC|n|Net income
NETINCQ|n|Net income - quarterly
NEWCERT|s|New certificate number
OAKAR|s|Oakar Institutions
OCCDIST|s|Office of the Comptroller
OFFDOM|n|Number of Domestic Offices
OFFFOR|n|Number of Foreign Offices
OFFICES|n|Office
OFFOA|n|Number of US Offices
PARCERT|s|Directly owned by another bank (CERT)
PROCDATE|s|Last Structure Change Process Date
QBPRCOML|s|Quarterly Banking Profile Commercial Bank Region
REGAGNT|s|Primary Regulator
REGAGENT2|s|Secondary Regulator
REPDTE|s|Report Date
RISDATE|s|Report Date
ROA|n|Return on assets (ROA)
ROAPTX|n|Pretax return on assets
ROAPTXQ|n|Quarterly Pretax return on assets
ROAQ|n|Quarterly return on assets
ROE|n|Return on Equity (ROE)
ROEQ|n|Quarterly return on equity
RSSDHCR|s|RSSDID - High Regulatory Holder
RUNDATE|s|Run Date
SASSER|s|Sasser Institutions
SPECGRP|n|Asset Concentration Hierarchy
SPECGRPN|s|Specialization Group
STALP|s|State Alpha code
STALPHCR|s|Regulatory holding company state location
STCHRTR|s|State Charter
STCNTY|s|State and county number
STNAME|s|State Name
STNUM|s|State Number
SUBCHAPS|s|Subchapter S Corporations
SUPRV_FD|s|Supervisory Region Number
TE01N528|s|Web Site URL 01
TE02N528|s|Web Site URL 02
TE03N528|s|Web Site URL 03
TE04N528|s|Web Site URL 04
TE05N528|s|Web Site URL 05
TE06N528|s|Web Site URL 06
TE07N528|s|Web Site URL 07
TE08N528|s|Web Site URL 08
TE09N528|s|Web Site URL 09
TE10N528|s|Web Site URL 10
TE01N529|s|Trade Name 01
TE02N529|s|Trade Name 02
TE03N529|s|Trade Name 03
TE04N529|s|Trade Name 04
TE05N529|s|Trade Name 05
TE06N529|s|Trade Name 06
TRACT|s|
TRUST|s|Trust Powers
ULTCERT|s|Ultimate Cert
UNINUM|s|FDIC's unique number
WEBADDR|s|Primary Internet Web Address
ZIP|s|Zip Code
items|s|";

    private static string FieldDefs_locations = @"ADDRESS|s|Street Address
BKCLASS|s|Institution Class
CBSA|s|Core Based Statistical Area Name
CBSA_DIV|s|Metropolitan Divisions Name
CBSA_DIV_FLG|s|Metropolitan Divisions Flag
CBSA_DIV_NO|s|Metropolitan Divisions Number
CBSA_METRO|s|Metropolitan Division Number
CBSA_METRO_FLG|s|Metropolitan Division Flag
CBSA_METRO_NAME|s|Metropolitan Division Name
CBSA_MICRO_FLG|s|Micropolitan Division Flag
CBSA_NO|s|Core Based Statistical Areas
CERT|s|FDIC Certificate #
CITY|s|City
COUNTY|s|County
CSA|s|Combined Statistical Area Name
CSA_FLG|s|Combined Statistical Area Flag (Branch)
CSA_NO|s|Combined Statistical Area Number (Branch)
ESTYMD|s|Branch Established Date
FI_UNINUM|s|FDIC's unique number
LATITUDE|n|Location Address Latitude
LONGITUDE|n|Location Address Latitude
MDI_STATUS_CODE|s|Minority Status Code
MDI_STATUS_DESC|s|Minority Status Description
MAINOFF|n|Main Office
NAME|s|Institution Name
OFFNAME|s|Office Name
OFFNUM|s|Branch Number
RUNDATE|s|Run Date
SERVTYPE|n|Service Type Code
SERVTYPE_DESC|s|Service Type Code Description
STALP|s|State Alpha Code
STCNTY|s|State and County Number
STNAME|s|Branch State
UNINUM|s|FDIC's Unique Number
ZIP|s|Zip Code";

    private static string FieldDefs_history = @"TRANSNUM|n|System Transaction Number
CHANGECODE|n|Activity Event Code
CHANGECODE_DESC|s|Activity Event Code Description
PROCDATE|s|Process Date
EFFDATE|s|Effective Date
ENDDATE|s|Effective Date
UNINUM|n|FDIC's unique number
ACQ_UNINUM|n|FDIC's unique number of who is Acquiring
OUT_UNINUM|n|FDIC's unique number of who is Divesting
ORG_ROLE_CDE|s|Organization Role Code
REPORT_TYPE|n|Report Type
CLASS|s|TBD
BANK_INSURED|s|Bank Insurance Status
ACQ_CHANGECODE|n|Activity Event Code
ACQ_ORG_EFF_DTE|s|Effective Date
ACQ_INSTNAME|s|Institution name
ACQ_CERT|n|FDIC Certificate #
ACQ_CLCODE|n|Numeric code
ACQ_CHARTER|n|OCC Charter Number
ACQ_CHARTAGENT|s|Acquiring Chartering Agency
ACQ_FDICREGION|n|Supervisory Region Number
ACQ_FDICREGION_DESC|s|Supervisory Region Description
ACQ_PADDR|s|Physical Street Address
ACQ_PCITY|s|City
ACQ_PSTALP|s|State Alpha code
ACQ_PZIP5|s|Zip Code
ACQ_PZIPREST|s|Zip Code Extension
ACQ_MADDR|s|Mailing Street Address
ACQ_MCITY|s|City
ACQ_MSTATE|s|Mailing State
ACQ_MSTALP|s|Mailing State Abbbreviation
ACQ_MZIP5|s|Zip Code
ACQ_MZIPREST|s|Zip Code Extension
ACQ_CLASS|s|TBD
ACQ_CNTYNAME|s|County
ACQ_CNTYNUM|n|TBD
ACQ_INSAGENT1|s|Insurance Fund Membership
ACQ_INSAGENT2|s|Secondary Insurance Fund
ACQ_REGAGENT|s|Acquiring Primary Regulator
ACQ_TRUST|s|Trust Power
ACQ_LATITUDE|n|Location Address Latitude
ACQ_LONGITUDE|n|Location Address Latitude
OUT_INSTNAME|s|Institution name
OUT_CERT|n|FDIC Certificate #
OUT_CLCODE|n|Numeric code
OUT_CHARTER|n|OCC Charter Number
OUT_CHARTAGENT|s|Outgoing Chartering Agency
OUT_FDICREGION|n|Supervisory Region Number
OUT_FDICREGION_DESC|s|Supervisory Region Description
OUT_PADDR|s|Physical Street Address
OUT_PCITY|s|City
OUT_PSTALP|s|State Alpha code
OUT_PZIP5|s|Zip Code
OUT_PZIPREST|s|Zip Code Extension
OUT_MADDR|s|Mailing Street Address
OUT_MCITY|s|City
OUT_MSTATE|s|Mailing State
OUT_MSTALP|s|Mailing State Abbbreviation
OUT_MZIP5|s|Zip Code
OUT_MZIPREST|s|Zip Code Extension
OUT_CLASS|s|TBD
OUT_CNTYNAME|s|County
OUT_CNTYNUM|n|TBD
OUT_INSAGENT1|s|Insurance Fund Membership
OUT_INSAGENT2|s|Secondary Insurance Fund
OUT_REGAGENT|s|Outgoing Primary Regulator
OUT_TRUST|s|Trust Power
OUT_LATITUDE|n|Location Address Latitude
OUT_LONGITUDE|n|Location Address Latitude
SUR_CHANGECODE|n|Activity Event Code
SUR_CHANGECODE_DESC|s|Activity Event Code Description
SUR_INSTNAME|s|Institution name
SUR_CERT|n|FDIC Certificate #
SUR_CLCODE|n|Numeric code
SUR_CHARTER|n|OCC Charter Number
SUR_CHARTAGENT|s|Surviving Chartering Agency
SUR_FDICREGION|n|Supervisory Region Number
SUR_FDICREGION_DESC|s|Supervisory Region Description
SUR_MADDR|s|Mailing Street Address
SUR_MCITY|s|City
SUR_MSTATE|s|Mailing State
SUR_MSTALP|s|Mailing State Abbreviation
SUR_MZIP5|s|Zip Code
SUR_PZIP5|s|Zip Code
SUR_CLASS|s|TBD
SUR_CNTYNAME|s|County
SUR_CNTYNUM|n|TBD
SUR_INSAGENT1|s|Insurance Fund Membership
SUR_INSAGENT2|s|Secondary Insurance Fund
SUR_PADDR|s|Physical Street Address
SUR_PCITY|s|City
SUR_PSTALP|s|State Alpha code
SUR_PZIPREST|s|Zip Code Extension
SUR_REGAGENT|s|Surviving Primary Regulator
SUR_TRUST|s|Trust Power
SUR_LATITUDE|n|Location Address Latitude
SUR_LONGITUDE|n|Location Address Latitude
FRM_CNTYNUM|n|TBD
FRM_PCITY|s|City
FRM_REGAGENT|s|From Primary Regulator
FRM_PSTALP|s|State Alpha code
FRM_TRUST|s|Trust Power
FRM_CLCODE|n|Numeric code
FRM_PADDR|s|Physical Street Address
FRM_CHARTAGENT|s|From/Before Chartering Agency
FRM_CLASS|s|TBD
FRM_PZIP5|s|Zip Code
FRM_PZIPREST|s|Zip Code Extension
FRM_INSTNAME|s|Institution name
FRM_CNTYNAME|s|County
FRM_CERT|n|Previous FDIC Certificate #
FRM_OFF_CNTYNAME|s|County
FRM_OFF_CNTYNUM|n|TBD
FRM_OFF_PADDR|s|Physical Street Address
FRM_OFF_PCITY|s|City
FRM_OFF_PSTALP|s|State Alpha code
FRM_OFF_PZIP5|s|Zip Code
FRM_OFF_PZIPREST|s|Zip Code Extension
FRM_OFF_SERVTYPE|n|Service Type
FRM_OFF_SERVTYPE_DESC|s|Service Type Description
FRM_OFF_STATE|s|Office State
FRM_OFF_NAME|s|Office Name
FRM_OFF_NUM|s|Branch Number
FRM_OFF_TRUST|s|Trust Power
FRM_OFF_CLCODE|n|Numeric code
FRM_OFF_LATITUDE|n|Location Address Latitude
FRM_OFF_LONGITUDE|n|Location Address Latitude
FRM_LATITUDE|n|Location Address Latitude
FRM_LONGITUDE|n|Location Address Latitude
CERT|n|FDIC Certificate #
INSTNAME|s|Institution name
CHARTAGENT|s|Chartering Agency
CLCODE|n|Numeric code
FDICREGION|n|Supervisory Region Number
FDICREGION_DESC|s|Supervisory Region Description
CNTYNAME|s|County
CNTYNUM|n|TBD
INSAGENT1|s|Insurance Fund Membership
INSAGENT2|s|Secondary Insurance Fund
MADDR|s|Mailing Street Address
MCITY|s|City
MSTATE|s|Mailing State
MSTALP|s|Mailing State
MZIP5|s|Zip Code
MZIPREST|s|Zip Code Extension
PADDR|s|Physical Street Address
PZIP5|s|Zip Code
PSTALP|s|State Alpha code
PZIPREST|s|Zip Code Extension
PCITY|s|City
STATE|s|Physical State
TRUST|s|Trust Power
REGAGENT|s|Primary Regulator
SERVTYPE|n|Service Type
SERVTYPE_DESC|s|Service Type Description
OFF_CNTYNAME|s|County
OFF_NUM|n|Branch Number
OFF_CNTYNUM|n|TBD
OFF_PADDR|s|Physical Street Address
OFF_PSTATE|s|Office State
OFF_PZIP5|s|Zip Code
OFF_PZIPREST|s|Zip Code Extension
OFF_NAME|s|Office name
OFF_PSTALP|s|State
OFF_PCITY|s|City
OFF_SERVTYPE|n|Service Type
OFF_LATITUDE|n|Location Address Latitude
OFF_LONGITUDE|n|Location Address Latitude
OFF_SERVTYPE_DESC|s|Service Type Description
ESTDATE|s|Office Established Date
ACQDATE|s|Office Acquired Date
FI_EFFDATE|s|Financial Institution Effective Date
FI_UNINUM|n|FDIC's unique number
ORG_STAT_FLG|s|Organization Status Flag
LATITUDE|n|Location Address Latitude
LONGITUDE|n|Location Address Latitude";

    private static string FieldDefs_summary = @"ALLOTHER|n|All Other Loans
alsonew|n|New Charters to Absorb Another Charter
ASSET|n|Total Assets
BANKS|n|Total Commercial Banks (Filing Y/E Call)
BKPREM|n|Bank Premises and Equipment
BRANCHES|n|Total Branches
BRANCHIN|n|Banks with Branches
BRO|n|Memo: Brokered Deposits
BRWDMONY|n|Borrowed Funds
CB_SI|s|Commercial Banks (CB) vs. Savings Institution (SI)
chartoth|n|Charter Transfers from Commercial Banks
CHBAL|n|Cash & Due from Depository Institutions
CHBALI|n|Interest Earning Balances
chrtrest|n|Non-insured Becoming insured
comboass|n|Assisted Mergers with Thrifts
combos|n|Unassisted Mergers/Consolidations of Thrifts
CONS|n|RTC Conservatorships
CORPBNDS|n|Other Debt Securities
COUNT|n|Total Savings Institutions (Filing Y/E Call)
CRLNLS|n|Loan & Lease Recoveries
DDT|n|Deposits - Domestic Demand
DEP|n|Total Deposits
DEPDOM|n|Total Domestic Deposits
DEPFOR|n|Total Foreign Deposits
DEPI|n|Interest Bearing Deposits
DEPIFOR|n|Foreign Deposits - Interest Bearing
DEPNI|n|Memo: Deposits - Non-interest Bearing
DEPNIFOR|n|Foreign Deposits - Non-interest Bearing
DRLNLS|n|Loan & Lease Charge-offs
EAMINTAN|n|Memo: Amortization of Intangibles
EDEP|n|Int Exp - Total Deposits
EDEPDOM|n|Int Exp - Deposit in Domestic Offices
EDEPFOR|n|Int Exp - Deposits in Foreign Offices
EEREPP|n|Fed Funds Purchased/Securities Sold
EFHLBADV|n|Int Exp Oth - Advances From FHLB
EFREPP|n|Int Exp - Fed Funds Purchased/Securities Sold
EINTEXP|n|Total Interest Expense
EINTEXP2|n|Eintexp2
ELNATR|n|Provision for Loan & Lease Losses
EOTHNINT|n|All Other Non-interest Expenses
EPREMAGG|n|Occupancy Expense
EQ|n|Total Equity
EQCDIV|n|Total Cash Dividends Declared
EQCDIVC|n|Cash Dividends Declared (Common)
EQCDIVP|n|Cash Dividends Declared (Preferred)
EQCS|n|Common Stock
EQDIV|n|Total Cash Divident Declared
EQNM|n|Total Equity Capital
EQNWCERT|n|FDIC Net Worth Certificates
EQOTHCC|n|Other Capital
EQPP|n|Perpetual Preferred Stock
EQSUR|n|Surplus
EQUPTOT|n|Undivided Profits
ESAL|n|Employee Salaries and Benefits
ESUBND|n|Int Exp - Subordinated Notes
EXTRA|n|Net Extraordinary Items
FD_BIF|n|FDIC Supervised, BIF Insured Institutions
FD_SAIF|n|FDIC supervised, SAIF Insured Institutions
FREPO|n|Federal Funds Sold
FREPP|n|Fed Funds & Repos Purchased
ICHBAL|n|Int Inc - Balances Due
IFEE|n|Fee Income
IFREPO|n|Int Inc - Fed Funds Sold/Securities Purchased
IGLSEC|n|Securities Gains and Losses
ILNDOM|n|Int Inc - Domestic Office Loans
ILNFOR|n|Int Inc - Foreign Office Loans
ILNLS|n|Int Inc - Total Loans & Leases
ILNS|n|Int Inc - Loans
ILS|n|Int Inc - Leases
INTAN|n|Intangible Assets
INTBAST|n|Total Interest Earning Assets
INTBLIB|n|Total Interest Bearing Liabilities
INTINC|n|Total Interest Income
INTINC2|n|INTINC2
IRAKEOGH|n|Memo: IRA's and Keogh Plan-Deposits
ISC|n|Int Inc - Investment Securities
ISERCHG|n|Service Charges on Deposit Accounts
ITAX|n|Applicable Income Taxes
ITAXR|n|Pre-Tax Net Operating Income
ITRADE|n|Int Inc - Trading Account Assets
LIAB|n|Total Liabilities
LIABEQ|n|Total Liabilities and Equity Capital
LIQASSTD|n|Assisted Payouts
LIQUNASS|n|Voluntary Liquidations
LNAG|n|Agricultural Loans
LNALLOTH|n|All Other Loans to Individuals
LNATRES|n|Allowance for Losses Loans and Leases
LNAUTO|n|Memo: Loans to Individuals - Auto
LNCI|n|Commercial and Industrial Loans
LNCON|n|Total Loans to Individuals
LNCONOT1|n|Loans to Individuals - Home Improvement
LNCONOTH|n|Loans to Individuals - All Others
LNCRCD|n|Loans to Individuals - Credit Card Plans
LNDEP|n|Loans to Deposit Institutions
LNLS|n|Gross Loans and Leases
LNLSGR|n|Total Loans and Leases
LNLSNET|n|Net Loans and Leases
LNMOBILE|n|Memo: Loans to Individuals - Mobile Homes
LNMUNI|n|Loans to States and Politicial Sub-divisions
LNRE|n|Total Real Estate Loans
LNREAG|n|R/E Loan - Farmland
LNRECONS|n|R/E Loan - Construction & Land Develop
LNREDOM|n|Total R/E Loans in Domestic Offices
LNREFOR|n|Total R/E Loans in Foreign Offices
LNRELOC|n|Memo: Home Equity Loans
LNREMULT|n|R/E Loans - Multifamily
LNRENRES|n|R/E Loan - Non-farm/Non-residential Prop
LNRERES|n|R/E Loan - 1-4 Family
LNRESRE|n|Memo: Contra Account
LNSP|n|Memo: Loans to Individuals - Single Payment
LS|n|Leases
MERGERS|n|Failures: Assisted Merger
MISSADJ|n|Other Misc. Adjustments
MTGLS|n|Mortgage and Other Borrowings
NALNLS|n|Non-accrual Loans & Leases
NCHGREC|n|Net Loans and Leases Charge-offs
NCLNLS|n|Total Non-current Loans & Leases
NETIMIN|n|Net Income Attributable to Noncontrolling Interests
NETINC|n|Net Income
newcount|n|New Charters
New_Char|n|New Charters
NEW6_1|n|Int Exp - Borrowed Money
NEW9_1|n|All Other Assets
NEW10_1|n|Corporate Bonds and Other Securities
NEW10_2|n|Trading Account Securities
NEW10_3|n|Memo: Valuation Reserves
NEW11_1|n|All Other Loans
NEW14_1|n|Borrowed Funds
NEW14_2|n|Other Liabilities
NEW14_3|n|Total Liabilities
NEW14_4|n|Undivided Profits
NEW15_1|n|Deposits - Individuals, Partnerships and Corporations
NEW15_2|n|Deposits - U.S. Government
NEW15_3|n|Deposits - States and Political Subdivisions
NEW15_4|n|Deposits - All Other
NEW15_5|n|Deposits - Domestic Savings
NEW15_7|n|Total Domestic Deposits
NEW16_1|n|Demand Notes and Other Liabilities
NEW16_2|n|Interest Bearing Deposits
NIM|n|Net Interest Income
NONII|n|Total Non-interest Income
NONIX|n|Total Non-interest Expense
NTLNLS|n|Net Loan & Lease Charge-offs
NTR|n|Memo: Domestic Deposits Non-Transaction
NTRTIME|n|Deposits - Domestic Time
NTRTMLG|n|Memo: Time Deposits (Over $100K)
NUMEMP|n|Number of Full Time Employees
OEA|n|Other Earning Assests
OFFICES|n|Offices
OINTBOR|n|Demand Notes and Other Borrowings
OINTEXP|n|Total Other Interest Expenses
OINTINC|n|Int Inc - Total Other
OONONII|n|Other Non-interest Income
ORE|n|Other Real Estate Owned
ORET|n|Other Real Estate
OT_BIF|n|Non FDIC Supervised BIF Insured Institutions
OT_SAIF|n|Non FDIC Supervised SAIF Insured Institutions
OTHASST|n|All Other Assets
OTHBFHLB|n|Advances from FHLB
OTHBORR|n|Int Exp Oth - Borrowed Money
OTHEQ|n|Other Equity
OTHER|n|Other
OTHLIAB|n|Other Liabilities
OTHNBORR|n|Borrowed Funds
OTLNCNTA|n|Less: Other Contra Accounts
PAID_OFF|n|Failures: Paid Off
P3LNLS|n|Loans & Leases P/D 30-89 Days
P9LNLS|n|Loans & Leases P/D 90+ Days
PTXNOINC|n|Pre-Tax Net Operating Income
REL_CO|n|Conversions
SAVINGS|n|Total Savings Institutions (Total Insured)
SC|n|Total Investment Securities (Book Value)
SCAGE|n|U.S. Agencies and Corporation Securities
SCEQ|n|Equity Securities
SCMTGBK|n|Memo: Mortgage Backed Securities
SCMUNI|n|States and Political Subdivisions Securities
SCMV|n|Market Values
SCRES|n|Less: Contra Accounts
SCUS|n|U.S. Treasury & Agency
SCUSA|n|Securities Of Us Agencies
SCUST|n|U.S. Treasury Securities
STNAME|s|Locations
STNUM|s|State Number
SUBLLPF|n|Subordinated Notes
SUBND|n|Subordinated Notes/Debentures
TINTINC|n|Int Inc - Total Other
tochrt|n|Charter Transfers to Commercial Banks
tofail|n|Assisted Mergers with Commercial Banks
TOINTEXP|n|Int Exp - Total Deposits
tomerg|n|Unassisted Mergers with Commercial Banks
tortc|n|Failures Transferred to the RTC
TOTAL|n|Total Commercial Banks (Total Insured)
TOT_FDIC|n|Total FDIC Supervised Savings Institutions
TOT_OTS|n|Total Non FDIC Supervised Savings Institutions
TOT_SAVE|n|Total Savings Institutions
TPD|n|Total Loans and Leases Past Due
TRADE|n|Trading Account Assets
TRADES|n|Less: Trading Accounts
TRN|n|Memo: Domestic Deposits Transaction
UNASSIST|n|Unassisted Mergers
UNINC|n|Unearned Income
UNIT|n|Unit Banks
YEAR|s|Year";

    private static string FieldDefs_failures = @"NAME|s|Institution Name
CERT|s|Cert
FIN|s|FIN
CITYST|s|Location
FAILDATE|s|Effective Date
FAILYR|s|Year
SAVR|s|Insurance Fund
RESTYPE1|s|Transaction Type
CHCLASS1|s|Charter Class
RESDATE|s|Date of Resolution
RESTYPE|s|Resolution
QBFDEP|n|Total Deposits
QBFASSET|n|Total Assets
COST|n|Estimated Loss
PSTALP|s|State
BIDCITY|s|BIDCITY
BIDNAME|s|BIDNAME
BIDSTATE|s|BIDSTATE
CITY|s|CITY
FUND|s|Insurance fund
PTRDATE|s|PTRDATE";

    private static string FieldDefs_financials = @"ACTEVT|s|Activity Event Code
ASSET|n|Total assets
BRANCH|n|BRANCHING
CALLFORM|n|Call Form Number
CB|s|Community Bank
CBSADIV|n|Core Based Statistical Division Number
CBSANAME|s|Core Based Statistical Division Name
STMULT|s|Multi State Offices Flag
ADDRESS|s|ADDRESS
CBLRIND|n|Community Bank Ratio
CD1T3|n|TIME DEP $250,000 OR MORE REMAINING MATURITY REPRICING OF 1-
CD1T3R|n|TIME DEP $250,000 OR MORE REMAINING MATURITY REPRICING OF 1-
CD3LES|n|TIME DEP $250,000 OR MORE REMAINING MATURITY REPRICING OF 3 
CD3LESR|n|TIME DEP $250,000 OR MORE REMAINING MATURITY REPRICING OF 3 
CD3LESS|n|TIME DEP $250,000 OR LESS REMAINING MATURITY REPRICING OF 3 
CD3LESSR|n|TIME DEP $250,000 OR LESS REMAINING MATURITY REPRICING OF 3 
CDOV3|n|TIME DEP $250,000 OR MORE REMAINING MATURITY OR REPRICING OV
CDOV3R|n|TIME DEP $250,000 OR MORE REMAINING MATURITY OR REPRICING OV
CDOV3S|n|TIME DEP $250,000 OR LESS REMAINING MATURITY OR REPRICING OV
CDOV3SR|n|TIME DEP $250,000 OR LESS REMAINING MATURITY OR REPRICING OV
CD3T12|n|TIME DEP $250,000 OR MORE REMAINING MATURITY OR REPRICING 3-
CD3T12R|n|TIME DEP $250,000 OR MORE REMAINING MATURITY OR REPRICING 3-
CD3T12S|n|TIME DEP $250,000 OR LESS REMAINING MATURITY OR REPRICING 3-
CD3T12SR|n|TIME DEP $250,000 OR LESS REMAINING MATURITY OR REPRICING 3-
CD1T3S|n|TIME DEP $250,000 OR LESS REMAINING MATURITY OR REPRICING 1-
CD1T3SR|n|TIME DEP $250,000 OR LESS REMAINING MATURITY OR REPRICING 1-
CERT|n|FDIC Certificate #
CERTCONS|s|Directly owned by another bank (CERT)
CITYHCR|s|City of High Holder
CLCODE|n|Classcode
CLOSED|n|Closed Institution Flag
CNTRYALP|s|FIPS Country Code
CNTRYNUM|n|FIPS Country Number
CNTYNUM|n|FIPS County Number
CSA|s|Combined Statistical Area
DENOVO|s|Denovo Institution
DEP|n|Total deposits
DEPR|n|TOTAL DEPOSITS RATIO
DEPDOM|n|Deposits held in domestic offices
DEPDOMR|n|DEPOSITS HELD IN DOM OFF RATIO
DIVISION|n|Division Flag
DOCKET|n|Docket Number
EDGECODE|n|International Activity Flag
ENTTYPE|n|Entity Type
EQ|n|Equity capital
EQ2|n|Equity capital
EQR|n|EQUITY CAPITAL RATIO
FAILED|n|Failed Institution Flag
FDICAREA|n|FDIC Compliance Area
FDICTERR|s|FDIC Compliance Territory
FLDOFDCA|s|DCA Field Office
FORM31|s|FFIEC Call Report 31 Filer
HCTMULT|s|Bank Holding Company Type
HCTNONE|n|Bank Not Member of Hold Company
INSAGNT2|s|Secondary Insurer
INSBIF|n|TBD
INSDIF|s|Deposit Insurance Fund member
INSTAG|s|Agricultural lending institution indicator
INSTCRCD|s|Credit Card Institutions
INSSAIF|n|SAIF Insured
MINORITY|n|MINORITY OWNED INSTITUTIONS
MUTUAL|n|Ownership Type
NAMEHCR|s|Bank Holding Company (Regulatory Top Holder)
NETINC|n|Net income
NETINCR|n|NET INCOME - RATIO
NETINCQ|n|Net income - quarterly
NETINCQA|n|Net income - quarterly
NETINCQR|n|NET INCOME - QUARTERLY RATIO
OFFDOM|n|Number of Domestic Offices
OFFFOR|n|Number of Foreign Offices
OFFOA|n|Number of US Offices
PARCERT|s|Directly owned by another bank (CERT)
L_REPDTE|s|Report Date
REPDTE_RAW|s|Report Date
REPDTE|s|Report Date
REPYEAR|s|REPORT YEAR
RISDATE|s|Report Date
ROA|n|Return on assets (ROA)
ROAPTX|n|Pretax return on assets
ROAPTXQ|n|Quarterly Pretax return on assets
ROAQ|n|Quarterly return on assets
ROE|n|Return on Equity (ROE)
ROEQ|n|Quarterly return on equity
RSSDHCR|s|RSSDID - High Regulatory Holder
SPECGRP|n|Asset Concentration Hierarchy
SPECGRPDESC|s|Asset Concentration Hierarchy Description
STALPHCR|s|Regulatory holding company state location
SUBCHAPS|n|Subchapter S Corporations
TRACT|n|
TRUST|s|Trust Powers
ACEPT|n|BANKS LIABILITY ON ACCEPTANCES
ACTIVE|n|ACTIVE INSTITUTION FLAG
BKCLASS|s|INSTITUTION CLASS
BKPREM|n|PREMISES AND FIXED ASSETS
BKPREMR|n|PREMISES AND FIXED ASSETS RATIO
BRO|n|BROKERED DEP
BROR|n|BROKERED RATIO
CALLYM|n|REPORT DATE (CCYYMM)
CHBAL|n|CASH & DUE FROM DEPOSITORY INST
CHBALR|n|CASH & DUE FROM DEPOSITORY INST RATIO
CHBALI|n|INTEREST-BEARING CASH & DUE
CHBALIR|n|INTEREST-BEARING CASH & DUE RATIO
CHRTAGNT|s|CHARTER AGENT
CONSERVE|n|RTC CONSERVATORSHIP FLAG
CRLNLS|n|TOTAL LN&LS RECOVERIES
CRLNLSR|n|TOTAL LN&LS RECOVERIES RATIO
CRLNLSQ|n|TOTAL LN&LS RECOVERIES QUARTERLY
CRLNLSQR|n|TOTAL LN&LS RECOVERIES QUARTERLY RATIO
CUSLI|n|CUSTOMERS ACCEPTANCES
DDT|n|DDA TRANS-TOTAL
DDTR|n|DDA TRANS-TOTAL RATIO
DEPFOR|n|TOTAL DEPOSITS-FOR
DEPFORR|n|TOTAL DEPOSITS-FOR RATIO
DEPI|n|INTEREST-BEARING DEP
DEPIFOR|n|INTEREST-BEARING DEP-FOR
DEPIFORR|n|INTEREST-BEARING DEP-FOR RATIO
DEPIPCCF|n|IPC & OFFICIAL CHECKS-FOR
DEPIPCCFR|n|IPC & OFFICIAL CHECKS-FOR RATIO
DEPIPCF|n|IPC-FOR
DEPNI|n|NONINTEREST-BEARING DEP
DEPNIFOR|n|NONINTEREST-BEARING DEP-FOR
DEPNIFORR|n|NONINTEREST-BEARING DEP-FOR RATIO
DRLNLS|n|TOTAL LN&LS CHARGE-OFFS
DRLNLSR|n|TOTAL LN&LS CHARGE-OFFS RATIO
DRLNLSQ|n|TOTAL LN&LS CHARGE-OFFS QUARTERLY
DRLNLSQR|n|TOTAL LN&LS CHARGE-OFFS QUARTERLY RATIO
EAMINTAN|n|AMORT & IMPAIR LOSS AST
EAMINTANR|n|AMORT & IMPAIR LOSS AST RATIO
EAMINTQ|n|AMORT & IMPAIR LOSS AST QUARTERLY
EAMINTQR|n|AMORT & IMPAIR LOSS AST QUARTERLY RATIO
EDEP|n|DEPOSIT INTEREST EXPENSE
EDEPDOM|n|DEPOSIT INTEREST EXPENSE-DOM
EDEPDOMR|n|DEPOSIT INTEREST EXPENSE-DOM RATIO
EDEPDOMQ|n|DEPOSIT INTEREST EXPENSE-DOM QUARTERLY
EDEPDOMQR|n|DEPOSIT INTEREST EXPENSE-DOM QUARTERLY RATIO
EDEPFOR|n|DEPOSIT INTEREST EXPENSE-FOR
EDEPFORR|n|DEPOSIT INTEREST EXPENSE-FOR RATIO
EDEPFORQ|n|DEPOSIT INTEREST EXPENSE-FOR QUARTERLY
EDEPFORQR|n|DEPOSIT INTEREST EXPENSE-FOR QUARTERLY RATIO
EFHLBADV|n|ADVANCES FROM FHLBANK INT EXP
EFREPP|n|FED FUNDS & REPOS INT EXPENSE
EFREPPR|n|FED FUNDS & REPOS INT EXPENSE RATIO
EFREPPQ|n|FED FUNDS & REPOS INT EXPENSE QUARTERLY
EFREPPQR|n|FED FUNDS & REPOS INT EXPENSE QUARTERLY RATIO
EINTEXP|n|TOTAL INTEREST EXPENSE
EINTEXPR|n|TOTAL INTEREST EXPENSE RATIO
EINTXQ|n|TOTAL INTEREST EXPENSE QUARTERLY
EINTXQA|n|TOTAL INTEREST EXPENSE QUARTERLY
EINTEXPA|n|TOTAL INTEREST EXPENSE ANNUALLY
EINTXQR|n|TOTAL INTEREST EXPENSE QUARTERLY RATIO
ELNATR|n|PROVISIONS FOR CREDIT LOSSES
ELNATRR|n|PROVISIONS FOR CREDIT LOSSES RATIO
ELNATQ|n|PROVISIONS FOR CREDIT LOSSES QUARTERLY
ELNATQA|n|PROVISIONS FOR CREDIT LOSSES QUARTERLY
ELNATQR|n|PROVISIONS FOR CREDIT LOSSES QUARTERLY RATIO
ELNLOSQ|n|PROVISIONS FOR CREDIT LOSSES QUARTERLY RATIO
NTTOTQ|n|PROVISIONS FOR CREDIT LOSSES QUARTERLY RATIO
ELNLOS|n|PROVISIONS FOR LN & LEASE LOSSES
EMTGLS|n|MORTGAGE DEBT INTEREST EXPENSE
ADDNONINTEXP|n|ADDITIONAL NONINTEREST EXPENSE
ADDNONINTEXPR|n|ADDITIONAL NONINTEREST EXPENSE RATIO
ADDNONINTEXPQ|n|ADDITIONAL NONINTEREST EXPENSE QUARTERLY
ADDNONINTEXPQR|n|ADDITIONAL NONINTEREST EXPENSE QUARTERLY RATIO
EOTHNINT|n|ALL OTHER NONINTEREST EXPENSE
EOTHNINTR|n|ALL OTHER NONINTEREST EXPENSE RATIO
EOTHNINQ|n|ALL OTHER NONINTEREST EXPENSE QUARTERLY
EOTHNINQR|n|ALL OTHER NONINTEREST EXPENSE QUARTERLY RATIO
EPREMAGG|n|PREMISES & FIXED ASSETS EXPENSE
EPREMAGGR|n|PREMISES & EQUIPMENT EXPENSE RATIO
EPREMAGQ|n|PREMISES & FIXED ASSETS EXPENSE QUARTERLY
EPREMAGQR|n|PREMISES & EQUIPMENT EXPENSE QUARTERLY RATIO
EQCDIV|n|CASH DIVIDENDS ON COMM & PREF
EQCDIVR|n|CASH DIVIDENDS ON COMM & PREF RATIO
EQCDIVC|n|CASH DIVIDENDS ON COMM STOCK
EQCDIVCR|n|CASH DIVIDENDS ON COMM STOCK RATIO
EQCDIVP|n|CASH DIVIDENDS ON PREF STOCK
EQCDIVPR|n|CASH DIVIDENDS ON PREF STOCK RATIO
EQCDIVQ|n|CASH DIVIDENDS ON COMM & PREF QUARTERLY
EQCDIVQR|n|CASH DIVIDENDS ON COMM & PREF QUARTERLY RATIO
EQCFCTA|n|EQCFCTA
EQCONSUB|n|MINOR INT IN CONSOL SUBS-EQ
EQCS|n|COMMON STOCK
EQCSR|n|COMMON STOCK RATIO
EQNWCERT|n|NET WORTH CERTIFICATES
EQOTHCC|n|OTHER EQUITY CAPITAL COMPONENTS
EQPP|n|PERPETUAL PREFERRED STOCK
EQPPR|n|PERPETUAL PREFERRED STOCK RATIO
EQSUR|n|SURPLUS
EQSURR|n|SURPLUS RATIO
EQUP|n|EQUP
EQUPTOT|n|UP-NET & OTHER CAPITAL COMP
EQUPTOTR|n|UP-NET & OTHER CAPITAL RATIO
ESAL|n|SALARIES AND EMPLOYEE BENEFITS
ESALR|n|SALARIES AND EMPLOYEE BENEFITS RATIO
ESALQ|n|SALARIES AND EMPLOYEE BENEFITS QUARTERLY
ESALQR|n|SALARIES AND EMPLOYEE BENEFITS QUARTERLY RATIO
ESUBND|n|SUBORDINATED NOTES INT EXPENSE
ETTLOTBO|n|TT&L & OTHER BORROWINGS INT EXP
EXTRA|n|NET DISCONTINUED OPERATIONS
EXTRAR|n|NET DISCONTINUED RATIO
EXTRAQ|n|NET DISCONTINUED OPERATIONS QUARTERLY
EXTRAQR|n|NET DISCONTINUED OPERATIONS QUARTERLY RATIO
FDICDBS|n|FDIC REGION
FDICDBSDESC|s|FDIC REGION DESC
FDICSUPV|n|FDIC REGION - SUPERVISORY
FDICSUPVDESC|s|FDIC REGION - SUPERVISORY DESC
FED|n|FED DISTRICT
FEDDESC|s|FED DISTRICT DESC
FEDCHRTR|n|FEDERAL CHARTER FLAG
FLDOFF|s|FDIC RISK MANAGEMENT FIELD OFFICE
FORCHRTR|n|FOREIGN CHARTER FLAG
FORMCFR|n|COMMERCIAL FINANCIAL REPORT FLAG
FREPO|n|FED FUNDS & REPOS SOLD
FREPOR|n|FED FUNDS & REPOS SOLD
FREPP|n|FED FUNDS & REPOS PURCHASED
FREPPR|n|FED FUNDS & REPOS PURCHASED RATIO
FRSMEM|n|FRS MEMBER FLAG
HCTONE|n|MEMBER OF A ONE BANK HOLDING CO
IBA|n|INTL BANKING ACT ENTITY FLAG
IBEFTAX|n|INCOME BEFORE INC TAXES & DISC
ICHBAL|n|DEPOSITORY INSTITUTIONS INT INC
ICHBALR|n|BALANCES FROM DEPOSITORY INSTITUTIONS YTD RATIO
ICHBALQ|n|DEPOSITORY INSTITUTIONS INT INC QUARTERLY
ICHBALQR|n|DEPOSITORY INSTITUTIONS INT INC QUARTERLY RATIO
IFREPO|n|FED FUNDS & REPO INTEREST INCOME
IFREPOR|n|FEDERAL FUNDS SOLD YTD RATIO
IFREPOQ|n|FED FUNDS & REPO INTEREST INCOME QUARTERLY
IFREPOQR|n|FED FUNDS & REPO INTEREST INCOME QUARTERLY RATIO
IGLSEC|n|SECURITIES GAINS AND LOSSES
IGLSECR|n|SECURITIES GAINS AND LOSSES RATIO
IGLSECQR|n|SECURITIES GAINS AND LOSSES QUARTERLY RATIO
ILNDOM|n|LOAN INCOME-DOM
ILNDOMR|n|DOMESTIC OFFICE LOANS YTD RATIO
ILNDOMQ|n|LOAN INCOME-DOM QUARTERLY
ILNDOMQR|n|LOAN INCOME-DOM QUARTERLY RATIO
ILNFOR|n|LOAN INCOME-FOR
ILNFORR|n|FOREIGN OFFICE LOANS YTD RATIO
ILNFORQ|n|LOAN INCOME-FOR QUARTERLY
ILNFORQR|n|LOAN INCOME-FOR QUARTERLY RATIO
ILS|n|LEASE INCOME
ILSR|n|LEASE FINANCING RECEIVABLES YTD RATIO
ILSQ|n|LEASE INCOME QUARTERLY
ILSQR|n|LEASE INCOME QUARTERLY RATIO
INSALL|n|INSURED INSTITUTION FLAG
INSCOML|n|INSURED COMMERCIAL FLAG
INSFDIC|n|FDIC INSURED FLAG
INSNONE|n|NOT FEDERALLY INSURED FLAG
INSSAVE|n|INSURED SAVINGS INSTITUTION FLAG
INSTCOML|n|COMMERCIAL INSTITUTION FLAG
INSTSAVE|n|SAVING & S&L INSTITUTION FLAG
INSTTYPE|s|INSTITUTION TYPE
INTAN|n|INTANGIBLE ASSETS
INTANR|n|INTANGIBLE ASSETS RATIO
INTEXPY|n|INTEREST EXPENSE TO EARNING ASSETS RATIO
INTEXPYQ|n|COST OF FUNDING EARNING ASSETS QUARTERLY
INTINC|n|TOTAL INTEREST INCOME
INTINCR|n|TOTAL INTEREST INCOME YTD RATIO
INTINQ|n|TOTAL INTEREST INCOME QUARTERLY
INTINQR|n|TOTAL INTEREST INCOME QUARTERLY RATIO
INTINQA|n|
INVSUB|n|INVEST IN UNCONSOLIDATED SUBS
INVSUORE|n|INVESTMENTS IN RE
IOTHFEE|n|OTHER FEE INCOME
IOTHII|n|OTHER INTEREST INCOME
IOTHIIR|n|OTHER INTEREST INCOME YTD RATIO
IOTHIIQ|n|OTHER INTEREST INCOME QUARTERLY
IOTHIIQR|n|OTHER INTEREST INCOME QUARTERLY RATIO
IRAKEOGH|n|IRAS AND KEOGH PLANS-DEPOSITS
IRAKEOGHR|n|IRAS AND KEOGH PLANS-DEPOSITS RATIO
ISC|n|TOTAL SECURITY INCOME
ISCR|n|SECURITIES YTD RATIO
ISCQ|n|TOTAL SECURITY INCOME QUARTERLY
ISCQR|n|TOTAL SECURITY INCOME QUARTERLY RATIO
ISERCHG|n|SERVICE CHARGE ON DEPOSIT ACCTS
ISERCHGR|n|SERVICE CHARGE ON DEPOSIT ACCTS RATIO
ITAX|n|APPLICABLE INCOME TAXES
ITAXR|n|APPLICABLE INCOME TAXES RATIO
ITAXQ|n|APPLICABLE INCOME TAXES QUARTERLY
ITAXQR|n|APPLICABLE INCOME TAXES QUARTERLY RATIO
ITRADE|n|INTEREST INCOME ON TRADING ACCTS
ITRADER|n|TRADING ACCOUNTS YTD RATIO
ITRADEQ|n|INTEREST INCOME ON TRADING ACCTS QUARTERLY
ITRADEQR|n|INTEREST INCOME ON TRADING ACCTS QUARTERLY RATIO
LIAB|n|TOTAL LIABILITIES
LIABR|n|TOTAL LIABILITIES RATIO
LIABEQ|n|TOTAL LIABILITIES & CAPITAL
LIABEQR|n|TOTAL LIABILITIES & CAPITAL RATIO
LIPMTG|n|MORTGAGE LOANS IN PROCESS
LLPFDSTK|n|LIMITED-LIFE PREFERRED STOCK
LNACOTH|n|ACCEPTANCES OF OTHER BANKS
LNAG|n|AGRICULTURAL LOANS
LNAGR|n|AGRICULTURAL LOANS RATIO
LNATRES|n|ALLOW FOR LOANS LOSS ADJUSTED
LNATRESJ|n|ALLOW FOR LOANS + ALLOC TRN RISK
LNATRESRR|n|ALLOW FOR LOANS + ALLOC TRN RISK RATIO
LNAUTO|n|CONSUMER LOANS - AUTO
LNAUTOR|n|CONSUMER LOANS-AUTO RATIO
LNCI|n|C&I LOANS
LNCIR|n|C&I LOANS RATIO
LNCON|n|CONSUMER LOANS
LNCONR|n|CONSUMER LOANS RATIO
LNCONOT1|n|CONSUMER LOANS-HOME IMPROVEMENT
LNCONOTH|n|CONSUMER LOANS-OTHER
LNCONOTHR|n|CONSUMER LOANS-OTHER RATIO
LNCRCD|n|CONSUMER LOANS-CREDIT CARD PLAN
LNCRCDR|n|CONSUMER LOANS-CREDIT CARD PLAN RATIO
LNCRCDRP|n|LNS-CREDIT CD & RELATED PLAN
LNDEP|n|DEP INSTITUTION LOANS
LNFG|n|FOREIGN GOVT LOANS
LNFGR|n|FOREIGN GOVT LOANS RATIO
LNLS|n|LN&LS + UNEARNED INC
LNLSGR|n|LOANS AND LEASES-TOTAL
LNLSGR2|n|LOANS AND LEASES-TOTAL
LNLSGRJ|n|LOANS AND LEASES-TOTAL ADJUSTED
LNLSGRR|n|LOANS AND LEASES-TOTAL RATIO
LNLSNET|n|LOANS AND LEASES-NET
LNLSNETR|n|LOANS AND LEASES-NET RATIO
LNMUNI|n|MUNI LOANS
LNMUNIR|n|MUNI LOANS RATIO
LNNDEPC|n|LOANS TO NONDEP FINANCIAL INST-CON
LNNDEPD|n|LOANS TO NONDEP FINANCIAL INST-DOM
LNNDEPDR|n|LOANS TO NONDEP FINANCIAL INST-DOM
LNOTCI|n|OTHER LNS & LS-COMM-QBP
LNOTCIR|n|OTHER LNS & LS-COMM-QBP RATIO
LNOTHER|n|LN TO NONDEP FIN INST & OTH LN
LNSOTHER|n|OTHER LOANS
LNSOTHERR|n|OTHER LOANS
LNRE|n|RE LOANS
LNRE2|n|RE LOANS
LNRECON2|n|
LNREMUL2|n|
LNREJ|n|RE LOANS ADJUSTED
LNRE5|n|RE LOANS CAVG5
LNRER|n|RE LOANS RATIO
LNREAG|n|RE AGRICULTURAL
LNRECON5|n|RE CONSTRUCTION & LAND DEV-CAV5
LNREAGR|n|RE AGRICULTURAL RATIO
LNRECONS|n|RE CONSTRUCTION & LAND DEVELOP
LNRECONSR|n|RE CONSTRUCTION & LAND DEVELOP RATIO
LNREDOM|n|RE LOANS-DOM
LNREDOMR|n|RE LOANS-DOM RATIO
LNREFOR|n|RE LOANS-FOR
LNREFORR|n|RE LOANS-FOR RATIO
LNRELOC|n|RE 1-4 FAMILY-LINE
LNRELOCR|n|RE 1-4 FAMILY-LINE RATIO
LNRELOC2|n|RE 1-4 FAMILY-LINE2
LNRELOC5|n|RE 1-4 FAMILY-LINE-CAVG5
LNREMULT|n|RE MULTIFAMILY
LNREMUL5|n|RE MULTIFAMILY-CAVG5
LNREMULTR|n|RE MULTIFAMILY RATIO
LNRENRES|n|RE NONFARM NONRESIDENTIAL PROP
LNRENRE5|n|RE NONFARM NONRESIDENTIAL CAVG5
LNRENRE2|n|RE NONFARM NONRESIDENTIAL CAVG5
LNRENRESR|n|RE NONFARM NONRESIDENTIAL PROP RATIO
LNREPP|n|PREPAID TAXES & INS ON MTG LNS
LNRERES|n|RE 1-4 FAMILY
LNRERESR|n|RE 1-4 FAMILY RATIO
LNRERES2|n|RE 1-4 FAMILY2
LNRERES5|n|RE 1-4 FAMILY-CAVG5
LNRESRE|n|ALLOWANCE FOR RE LOAN
LS|n|LEASES
LSR|n|LEASES RATIO
METRO|n|METROPOLITAN FLAG
MI|n|INSURED SAVINGS BANK FLAG
MICRO|n|MICROPOLITAN FLAG
MNRTYCDE|n|MINORITY CODE
MNRTYDTE|n|EFFECTIVE DTE OF MINORITY STATUS
MTGLS|n|NATIONAL BANK FLAG
NALNLS|n|NONACCRUAL-LOANS & LEASES
NC|n|NONINSURED COMMERCIAL INST FLAG
NCLNLS|n|TOTAL N/C-LOANS & LEASES
NETIMIN|n|NET INC - ATTRIB TO MINORITY INT
NETIMINR|n|NET INC - ATTRIB TO MINORITY INT RATIO
NETIMINQ|n|NET INC - ATTRIB TO MINORITY INT QUARTERLY
NETIMINQR|n|NET INC - ATTRIB TO MINORITY INT QUARTERLY RATIO
NETINBM|n|NET INC - BANK & MINORITY INT
NETINBMR|n|NET INC - BANK & MINORITY INT RATIO
NETINBMQ|n|NET INC - BANK & MINORITY INT QUARTERLY
NETINBXA|n|NET INCOME BEFORE TAXES ANNUALLY
NETIBXQA|n|
NETINBMQR|n|NET INC - BANK & MINORITY INT QUARTERLY RATIO
NEWINST|n|NEW INSTITUTION FLAG
NFAA|n|NUMBER OF FIDUCIARY ACCOUNTS AND RELATED ASSET ACCOUNTS
NIM|n|NET INTEREST INCOME
NIMR|n|NET INTEREST INCOME RATIO
NIMQ|n|NET INTEREST INCOME QUARTERLY
NIMQA|n|NET INTEREST INCOME QUARTERLY
NIMA|n|NET INTEREST INCOME ANNUALLY
NIMQR|n|NET INTEREST INCOME QUARTERLY RATIO
NM|n|NONMEMBER INSURED INST FLAG
NONII|n|TOTAL NONINTEREST INCOME
NONIIR|n|TOTAL NONINTEREST INCOME RATIO
NONIX|n|TOTAL NONINTEREST EXPENSE
NONIXR|n|TOTAL NONINTEREST EXPENSE RATIO
NONIXQ|n|TOTAL NONINTEREST EXPENSE QUARTERLY
NONIXQA|n|TOTAL NONINTEREST EXPENSE QUARTERLY
NONIXQR|n|TOTAL NONINTEREST EXPENSE QUARTERLY RATIO
NS|n|NONINSURED SAVINGS INST FLAG
NTLNLS|n|TOTAL LN&LS NET CHARGE-OFFS
NTLNLSCOR|n|TOTAL LN&LS NET CHARGE-OFFS RATIO
NTLNLSQ|n|TOTAL LN&LS NET CHARGE-OFFS QUARTERLY
NTLNLSQA|n|TOTAL LN&LS NET CHARGE-OFFS QUARTERLY
NTLNLSCOQR|n|TOTAL LN&LS NET CHARGE-OFFS QUARTERLY RATIO
NTR|n|NONTRANSACTION-TOTAL
NTRR|n|NONTRANSACTION-TOTAL RATIO
NTRIPC|n|NONTRANSACTION-IPC
NTRIPCR|n|NONTRANSACTION-IPC RATIO
NTRMUNI|n|NONTRANSACTION-MUNI
NTRMUNIR|n|NONTRANSACTION-MUNI RATIO
NTRTIME|n|TIME DEPOSITS-TOTAL
NTRTMLG|n|TIME DEPOSITS OVER $100M
NTRTMLGJ|n|AMT TOTAL TIME DEP MORE THAN $250,000
NTRTMLGJR|n|AMT TOTAL TIME DEP MORE THAN $250,000 RATIO
NTRTMMED|n|AMT TIME DEP OF $250,000 OR LESS
NTRTMMEDR|n|AMT TIME DEP OF $250,000 OR LESS RATIO
NTRUSGOV|n|NONTRANSACTION-U.S. GOVERNMENT
NTRUSGOVR|n|NONTRANSACTION-U.S. GOVERNMENT RATIO
NTIRTA|n|RETAINED EARNINGS ANUALLY
NTTOT|n|TOTAL LN & LS LOSS NET CHG-OFFS
NUMEMP|n|NUMBER OF FULL TIME EMPLOYEES
OA|n|OTHER ASSETS
OAKAR|n|OAKAR FLAG
OCCDIST|n|OCC DISTRICT
OCCDISTDESC|s|OCC DISTRICT DESC
OFFDMULT|n|DOMESTIC MULTI-SERVICE OFFICES
OFFNDOM|n|NONDOMESTIC OFFICES
OFFOTH|n|DOMESTIC OTHER OFFICES
OFFSOD|n|SOD OFFICES
OFFSTATE|n|NUMBER OF STATES WITH OFFICES
OFFTOT|n|TOTAL OFFICES
OFFUSOA|n|U.S. AND OTHER AREA OFFICES
OI|n|INSURED IBA OFFICE FLAG
OLMIN|n|OTHER LIAB & MINOR IN SUBS
ORE|n|OTHER REAL ESTATE OWNED
ORER|n|OTHER REAL ESTATE OWNED RATIO
OTHBFHLB|n|OTHER LIABILITIES-FHLB
OTHBFHLBR|n|OTHER LIABILITIES-FHLB RATIO
OTHBOR|n|OTHER BORROWED MONEY
OTHBRF|n|OTH BORROWED FUNDS
OTHBRFR|n|OTH BORROWED FUNDS RATIO
OTBFH1L|n|FHLB ADV MAT REP ONE YR OR LESS
OTBFH1LR|n|FHLB ADV MAT REP ONE YR OR LESS RATIO
OTBFH1T3|n|FHLB ADV MAT REP ONE YR THROUGH THREE
OTBFH1T3R|n|FHLB ADV MAT REP ONE YR THROUGH THREE
OTBFH3T5|n|FHLB ADV MAT REP THREE THROUGH FIVE
OTBFH3T5R|n|FHLB ADV MAT REP THREE THROUGH FIVE RATIO
OTBFHOV5|n|FHLB ADV MAT REP OVER FIVE YEARS
OTBFHOV5R|n|FHLB ADV MAT REP OVER FIVE YEARS RATIO
OTHBFH1L|n|FHLB ADV WITH REMAINING MAT ONE YR OR LESS
OTHBFH1LR|n|FHLB ADV WITH REMAINING MAT ONE YR OR LESS RATIO
OTBFHSTA|n|FHLB STRUCTURED ADV
OTBFHSTAR|n|FHLB STRUCTURED ADV
OTBOT1L|n|OTH BORR MAT OR NEXT REPRICING ONE YR OR LESS
OTBOT1LR|n|OTH BORR MAT OR NEXT REPRICING ONE YR OR LESS RATIO
OTBOT1T3|n|OTH BORR MAT OR NEXT REPRICING ONE YR THROUGH THREE
OTBOT1T3R|n|OTH BORR MAT OR NEXT REPRICING ONE YR THROUGH THREE RATIO
OTBOT3T5|n|OTH BORR MAT OR NEXT REPRICING THREE YR THROUGH FIVE
OTBOT3T5R|n|OTH BORR MAT OR NEXT REPRICING THREE YR THROUGH FIVE RATIO
OTBOTOV5|n|OTH BORR MAT OR NEXT REPRICING OVER FIVE YRS
OTBOTOV5R|n|OTH BORR MAT OR NEXT REPRICING OVER FIVE YRS RATIO
OTHBOT1L|n|OTH BORR MAT REMAINING MAT OF ONE YR OR LESS
OTHBOT1LR|n|OTH BORR MAT REMANING MAT OF ONE YR OR LESS RATIO
ALLOTHL|n|ALL OTHER LIABILITIES
ALLOTHLR|n|ALL OTHER LIABILITIES RATIO
P3LNLS|n|30-89 DAYS P/D-LOANS & LEASES
P9LNLS|n|90+ DAYS P/D-LOANS & LEASES
QBPRCOML|n|QBP COMMERCIAL BANK REGION
QBPRCOMLDESC|s|QBP COMMERCIAL BANK REGION DESC
QBPRSAVB|n|QBP BIF FUND SAVINGS REGION
QBPRSAVS|n|QBP SAVING SAIF FUND REGION
QTRNO|n|QUARTER NUMBER
REGAGNT|s|PRIMARY REGULATING AGENCY
RISKTERR|s|FDIC RISK TERRITORY
S10T250B|n|ASSETS 10B TO 250B FLAG
SASSER|n|SASSER FLAG
SB|n|SAVINGS BANK FLAG
SC|n|SECURITIES
SCR|n|SECURITIES RATIO
SCAA|n|TOTAL AVAILABLE-FOR-SALE AT AMORTIZED COST SECURITIES ON A C
SCHF|n|TOTAL HELD-TO-MATURITY AT FAIR VALUE SECURITIES ON A CONSOLI
SCAGE|n|U.S. AGENCY
SCASPNHA|n|U.S. AGENCY
SCASPNAF|n|U.S. AGENCY
SCASPNSUM|n|NON-MORT BACKED ISSUES BY US GOVT OR SPONSORED AGENCIES
SCASPNSUMR|n|NON-MORT BACKED ISSUES BY US GOVT OR SPONSORED AGENCIES RATI
SCDEQ|n|DOMESTIC SEC*DEBT & EQUITY - CON
SCDOMO|n|OTHER DOMESTIC DEBT
SCDOMOR|n|OTHER DOMESTIC DEBT RATIO
SCEQ|n|EQUITY SECURITIES
SCFDEQ|n|FOREIGN DEBT & EQUITY
SCFORD|n|FOREIGN DEBT SECURITIES
SCFORDR|n|FOREIGN DEBT SECURITIES RATIO
SCMTGBK|n|MORTGAGE BACKED SECURITIES
SCMTGBKR|n|MORTGAGE BACKED SECURITIES RATIO
SCMUNI|n|MUNICIPAL SECURITIES
SCMUNIR|n|MUNICIPAL RATIO
SCMV|n|SECURITIES-MV
SCODPC|n|RES-OTH DOM DEBT*PRIV CERTS
SCODPCR|n|RES-OTH DOM DEBT*PRIV CERTS RATIO
SCRES|n|CONTRA-ASSETS TO SECURITIES
SCUS|n|U.S. TREASURY & AGENCY
SCUSR|n|U.S. TREASURY & AGENCY RATIO
SCUSA|n|U.S. AGENCY ALL OTHER
SCUST|n|U.S. TREASURY SECURITIES
SCUSTR|n|U.S. TREASURY SECURITIES RATIO
SIMS_LAT|n|GEOGRAPHIC LATITUDE OF MAIN OFFICE
SIMS_LONG|n|GEOGRAPHIC LONGITUDE OF MAIN OFFICE
SL|n|SAVINGS AND LOAN FLAG
SM|n|STATE MEMBER BANK FLAG
STALP|s|FIPS STATE ALPHA CODE
STCHRTR|n|STATE CHARTER FLAG
STNAME|s|STATE NAME
STNUM|n|FIPS STATE NUMBER
SUBLLPF|n|SUB. DEBT & L/L PREFERRED STK
SUBND|n|SUBORDINATED NOTES & DEBENTURES
SZ25|n|ASSETS UNDER 25M FLAG
SZ100|n|ASSETS UNDER 100M FLAG
SZ100MP|n|ASSETS OVER 100M FLAG
SZ100T3|n|ASSETS 100M TO 300M FLAG
SZ100T5|n|ASSETS 100M TO 500M FLAG
SZ100T1B|n|ASSETS 100M TO 1B FLAG
SZ10BP|n|ASSETS OVER 10B FLAG
SZ1BP|n|ASSETS OVER 1B FLAG
SZ1BT10B|n|ASSETS 1B TO 10B FLAG
SZ1BT3B|n|ASSETS 1B TO 3B FLAG
SZ1BT5B|n|ASSETS 1B TO 5B FLAG
SZ250BP|n|ASSETS OVER 250B FLAG
SZ25T50|n|ASSETS 25M TO 50M FLAG
SZ300T5|n|ASSETS 300M TO 500M FLAG
SZ3BT10B|n|ASSETS 3B TO 10B FLAG
SZ500T1B|n|ASSETS 500M TO 1B FLAG
SZ50T100|n|ASSETS 50M TO 100M FLAG
SZ5BP|n|ASSETS OVER 5B FLAG
TFRA|n|TOTAL FIDUCIARY AND RELATED ASSETS
TRADE|n|TRADING ACCOUNTS
TRADEL|n|TRADING LIABILITIES
TRADELR|n|TRADING LIABILITIES RATIO
TRADER|n|TRADING ACCOUNTS RATIO
TRN|n|TRANSACTION-TOTAL
TRNR|n|TRANSACTION-TOTAL RATIO
TRNIPC|n|TRANSACTION-IPC
TRNIPCOC|n|TRAN-IPC-OFFICIAL CHECKS
TRNIPCOCR|n|TRAN-IPC-OFFICIAL CHECKS RATIO
TRNMUNI|n|TRANSACTION-MUNI
TRNMUNIR|n|TRANSACTION-MUNI RATIO
TRNUSGOV|n|TRANSACTION-U.S. GOVERNMENT
TRNUSGOVR|n|TRANSACTION-U.S. GOVERNMENT RATIO
TRUSTPWR|n|TRUST POWER GRANTED CODES
TS|n|TIME & SAVINGS DEPOSITS-TOTAL
TSR|n|TIME & SAVINGS DEPOSITS-TOTAL RATIO
TTL|n|TT&L NOTE OPTION
TTLOTBOR|n|TT&L & OTHER BORROWINGS
UNINC|n|UNEARNED INCOME
UNINUM|n|BANK UNIQUE NUMBER
USA|n|USA LOCATED INSTITUTION
UYAMTG|n|UNAMORTIZED YIELD ADJ-MTG LOANS
ABCUBK|n|ASST-BCK UNUSED COMMIT - RELATED
ABCUBKR|n|ASST-BCK UNUSED COMMIT - RELATED RATIO
ABCUOTH|n|ASSET-BACK UNUSED COMMIT - OTHER
ABCUOTHR|n|ASSET-BACK UNUSED COMMIT - OTHER RATIO
ABCXBK|n|ASSET-BACK CREDIT EX-RELATED
ABCXBKR|n|ASSET-BACK CREDIT EX-RELATED RATIO
ABCXOTH|n|ASSET-BACK CREDIT EX-OTHER
ABCXOTHR|n|ASSET-BACK CREDIT EX-OTHER RATIO
ASCEOTH|n|C.E. RECOURSE NOT SECUR. - OTH
ASCEOTHR|n|C.E. RECOURSE NOT SECUR. - OTH RATIO
ASCERES|n|C.E. RECOURSE NOT SECUR. - RES
ASCERESR|n|C.E. RECOURSE NOT SECUR. - RES RATIO
ASDROTH|n|SOLD W/RECOURSE N/SECUR. - OTH
ASDROTHR|n|SOLD W/RECOURSE N/SECUR. - OTH RATIO
ASDRRES|n|SOLD W/RECOURSE N/SECUR.- RES
ASDRRESR|n|SOLD W/RECOURSE N/SECUR.- RES RATIO
ASSET2|n|TOTAL ASSETS-CAVG2
ASSET5|n|TOTAL ASSETS-CAVG5
ASSETFOR|n|TOTAL ASSETS-FOR
ASSTLT|n|LONG-TERM ASSETS (5+ YEARS)-QBP
ASSTLTR|n|LONG-TERM ASSETS (5+ YEARS) RATIO
ASTEMPM|n|ASSETS PER EMPLOYEE IN MILLION
AVASSETJ|n|AVERAGE ASSETS-ADJUSTED-PCA
AVASSETJR|n|AVERAGE ASSETS-ADJUSTED-PCA RATIO
BROINS|n|BROKERED DEP-INSURED
BROINSR|n|BROKERED DEP-INSURED RATIO
CALLYMD|n|REPORT DATE (CCYYMMDD)
CHBALFOR|n|CASH & DUE FROM DEP INST-FOR
CHBALNI|n|NONINTEREST-BEARING CASH & DUE
CHBALNIR|n|NONINTEREST-BEARING CASH & DUE RATIO
CHCIC|n|CASH ITEMS
CHCICR|n|CASH ITEMS RATIO
CHCOIN|n|CURRENCY & COIN
CHCOINR|n|CURRENCY & COIN RATIO
CHFLA|n|NET OPERATING CASH FLOW-ANN
CHFLQ|n|NET OPERATING CASH FLOW-ANN Quarterly
CHFRB|n|BAL DUE FROM FRB
CHFRBR|n|BAL DUE FROM FRB RATIO
CHITEM|n|CASH ITEM COLLEC IN DOMESTIC OFFICES
CHITEMR|n|CASH ITEMS COLLEC IN DOMESTIC OFFICES RATIO
CHNUS|n|BAL DUE FROM BK FOR COUNTRY
CHNUSR|n|BAL DUE FROM BK FOR COUNTRY RATIOS
CHNUSFBK|n|BAL DUE FROM FOR BR OF OTH US BK
CHUS|n|BAL DUE FROM DEP INST U.S.
CHUSR|n|BAL DUE FROM DEP INST U.S. RATIO
CHUSFBK|n|BAL DUE FROM U.S. BR OF FOR BKS
CITY|s|CITY
COREDEP|n|CORE DEPOSITS
COREDEPR|n|CORE DEPOSITS RATIO
CRAG|n|AGRICULTURAL LOAN RECOVERIES
CRAGR|n|AGRICULTURAL LOAN RECOVERIES RATIO
CRAGQ|n|AGRICULTURAL LOAN RECOVERIES QUARTERLY
CRAGQR|n|AGRICULTURAL LOAN RECOVERIES QUARTERLY RATIO
CRAGSM|n|AG LOAN RECOVERIES*SMALL BKS
CRAGSMR|n|AAG LOAN RECOVERIES*SMALL BKS RATIO
CRAGSMQ|n|AG LOAN RECOVERIES*SMALL BKS QUARTERLY
CRAGSMQR|n|AG LOAN RECOVERIES*SMALL BKS QUARTERLY RATIO
CRAUTO|n|AUTO LOANS - RECOVERIES
CRAUTOR|n|AUTO LOANS - RECOVERIES RATIO
CRAUTOQ|n|AUTO LOANS - RECOVERIES QUARTERLY
CRAUTOQR|n|AUTO LOANS - RECOVERIES QUARTERLY RATIO
CRCI|n|COMMERCIAL LOAN RECOVERIES
CRCIR|n|COMMERCIAL LOAN RECOVERIES RATIO
CRCIQ|n|COMMERCIAL LOAN RECOVERIES QUARTERLY
CRCIQR|n|COMMERCIAL LOAN RECOVERIES QUARTERLY RATIO
CRCINUS|n|COMMERCIAL LOAN RECOVERIES NON-U.S.
CRCINUSR|n|COMMERCIAL LOAN RECOVERIES NON-U.S. RATIO
CRCINUSQ|n|COMMERCIAL LOAN RECOVERIES NON-U.S. QUARTERLY
CRCINUSQR|n|COMMERCIAL LOAN RECOVERIES NON-U.S. QUARTERLY RATIO
CRCON|n|CONSUMER LOAN RECOVERIES
CRCONR|n|CONSUMER LOAN RECOVERIES RATIO
CRCONQ|n|CONSUMER LOAN RECOVERIES QUARTERLY
CRCONQR|n|CONSUMER LOAN RECOVERIES QUARTERLY RATIO
CRCONOTH|n|OTHER CONSUMER LOAN RECOVERIES
CRCONOTHR|n|OTHER CONSUMER LOAN RECOVERIES RATIO
CRCONOTQ|n|OTHER CONSUMER LOAN RECOVERIES QUARTERLY
CRCONOTQR|n|OTHER CONSUMER LOAN RECOVERIES QUARTERLY RATIO
CRCRCD|n|CREDIT CARD LOAN RECOVERIES
CRCRCDR|n|CREDIT CARD LOAN RECOVERIES RATIO
CRCRCDQ|n|CREDIT CARD LOAN RECOVERIES QUARTERLY
CRCRCDQR|n|CREDIT CARD LOAN RECOVERIES QUARTERLY RATIO
CRDEP|n|DEPOSITORY INST LOAN RECOVERIES
CRDEPR|n|DEPOSITORY INST LOAN RECOVERIES RATIO
CRDEPQ|n|DEPOSITORY INST LOAN RECOVERIES QUARTERLY
CRDEPQR|n|DEPOSITORY INST LOAN RECOVERIES Quarterly RATIO
CRDEPNUS|n|FOREIGN DEPS INST LN RECOVERIES
CRDEPNUSR|n|FOREIGN DEPS INST LN RECOVERIES RATIO
CRDEPNUQ|n|FOREIGN DEPS INST LN RECOVERIES QUARTERLY
CRDEPNUQR|n|FOREIGN DEPS INST LN RECOVERIES QUARTERLY RATIO
CRFORGV|n|FOREIGN GOVERNMENT LN RECOVERIES
CRFORGVR|n|FOREIGN GOVERNMENT LN RECOVERIES RATIO
CRFORGVQ|n|FOREIGN GOVERNMENT LN RECOVERIES QUARTERLY
CRFORGVQR|n|FOREIGN GOVERNMENT LN RECOVERIES QUARTERLY RATIO
CRLS|n|LEASE RECOVERIES
CRLSR|n|LEASE RECOVERIES RATIO
CRLSQ|n|LEASE RECOVERIES QUARTERLY
CRLSQR|n|LEASE RECOVERIES QUARTERLY RATIO
CROTHER|n|ALL OTHER LOAN RECOVERIES
CROTHERR|n|ALL OTHER LOAN RECOVERIES RATIO
CROTHQ|n|ALL OTHER LOAN RECOVERIES QUARTERLY
CROTHQR|n|ALL OTHER LOAN RECOVERIES QUARTERLY RATIO
CRRE|n|REAL ESTATE LOAN RECOVERIES
CRRER|n|REAL ESTATE LOAN RECOVERIES RATIO
CRREQ|n|REAL ESTATE LOAN RECOVERIES QUARTERLY
CRREQR|n|REAL ESTATE LOAN RECOVERIES QUARTERLY RATIO
CRREAG|n|FARMLAND RE LN RECOVERIES
CRREAGR|n|FARMLAND RE LN RECOVERIES RATIO
CRREAGQ|n|FARMLAND RE LN RECOVERIES-QTR
CRREAGQR|n|FARMLAND RE LN RECOVERIES QUARTERLY RATIO
CRRECNFM|n|1-4 FAM CONSTRUCT LN RECOVERIES
CRRECNOT|n|OTHER CONSTRUCT LN RECOVERIES
CRRECONQ|n|CONSTRUCTION RE LN RECOVER-QTR
CRRECONQR|n|CONSTRUCTION RE LN RECOVERIES QUARTERLY RATIO
CRRECONS|n|CONSTRUCTION RE LN RECOVERIES
CRRECONSR|n|CONSTRUCTION RE LN RECOVERIES RATIO
CRREFOR|n|REAL ESTATE LN RECOVERIES - FOR
CRREFORR|n|REAL ESTATE LN RECOVERIES - FOR RATIO
CRREFORQ|n|REAL ESTATE LN RECOVERIES - FOR QUARTERLY
CRREFORQR|n|REAL ESTATE LN RECOVERIES - FOR QUARTERLY RATIO
CRRELOC|n|LINE OF CREDIT RE LN RECOVERIES
CRRELOCR|n|LINE OF CREDIT RE LN RECOVERIES RATIO
CRRELOCQ|n|LINE OF CREDIT RE LN RECOVERIES QUARTERLY
CRRELOCQR|n|LINE OF CREDIT RE LN RECOVERIES QUARTERLY RATIO
CRREMULQ|n|MULTIFAMILY RE LN RECOVERIES-QTR
CRREMULQR|n|MULTIFAMILY RES RE LN RECOVERIES QUARTERLY RATIO
CRREMULT|n|MULTIFAMILY RES RE LN RECOVERIES
CRREMULTR|n|MULTIFAMILY RES RE LN RECOVERIES RATIO
CRRENRES|n|NONFARM NONRES RE LN RECOVERIES
CRRENRESR|n|NONFARM NONRES RE LN RECOVERIES RATIO
CRRENROT|n|OTHER NONFARM NONRES RECOVERIES
CRRENROW|n|OWN-OCCUP NONFARM NONRES RECOV
CRRENRSQ|n|NONFARM NONRES RE LN RECOVER-QTR
CRRENRSQR|n|NONFARM NONRES RE LN RECOVER-QTR RATIO
CRRENUS|n|NON-U.S. RE LN RECOVERIES
CRRENUSR|n|NON-U.S. RE LN RECOVERIES RATIO
CRRENUSQ|n|NON-U.S. RE LN RECOVERIES QUARTERLY
CRRENUSQR|n|NON-U.S. RE LN RECOVERIES QUARTERLY RATIO
CRRERES|n|RE LOANS 1-4 FAMILY RECOVERIES
CRRERESR|n|RE LOANS 1-4 FAMILY RECOVERIES RATIO
CRRERESQ|n|RE LOANS 1-4 FAMILY RECOVER-QTR
CRRERESQR|n|RE LOANS 1-4 FAMILY RECOVERIES QUARTERLY RATIO
CRRERSF2|n|RE LOAN 1-4 FAM JR LIEN-RECOVER
CRRERSF2R|n|RE LOAN 1-4 FAM JR LIEN-RECOVER RATIO
CRRERS2Q|n|RE LOAN 1-4 FAM JR LIEN-RECOVER QUARTERLY
CRRERS2QR|n|RE LOAN 1-4 FAM JR LIEN-RECOVER QUARTERLY RATIO
CRRERSFM|n|RE LOAN 1-4 FAM FIRST LIEN-RECOV
CRRERSFMR|n|RE LOAN 1-4 FAM FIRST LIEN-RECOV RATIO
CRRERSFQ|n|RE LOAN 1-4 FAM FIRST LIEN-RECOV QUARTERLY
CRRERSFQR|n|RE LOAN 1-4 FAM FIRST LIEN-RECOV QUARTERLY RATIO
CRREOFFDOM|n|RE LOAN RECOVERIES DOMESTIC OFFICES
CRREOFFDOMR|n|RE LOAN RECOVERIES DOMESTIC OFFICES RATIO
CRREOFFDOMQ|n|RE LOAN RECOVERIES DOMESTIC OFFICES QUARTERLY
CRREOFFDOMQR|n|RE LOAN RECOVERIES DOMESTIC OFFICES QUARTERLY RATIO
CTDERBEN|n|CR DER (NET)-PURCHASE PROTECT
CTDERGTY|n|CR DER(NET) - SOLD PROTECTION
DEPBEFEX|n|TOTAL DEPOSIT LIAB BEF EXCLUSION
DEPCSBQ|n|DEPOSIT LIABILITIES AFTER EXCLUSIONS
DEPCSBQR|n|DEPOSIT LIABILITIES AFTER EXCLUSIONS RATIO
DEPDASTR|n|TOT DOMESTIC DEPOSIT / ASSET
DEPFBKF|n|FOREIGN BANKS-FOR
DEPFBKFR|n|FOREIGN BANKS-FOR RATIO
DEPFGOVF|n|FOREIGN GOVERNMENTS-FOR
DEPFGOVFR|n|FOREIGN GOVERNMENTS-FOR RATIO
DEPIDOM|n|INTEREST-BEARING DEP-DOM
DEPIDOMR|n|INTEREST-BEARING DEP-DOM RATIO
DEPINS|n|ESTIMATED INSURED DEPOSITS
DEPINSR|n|ESTIMATED INSURED DEPOSITS RATIO
DEPLGAMT|n|AMT DEP ACC GREATER THAN $250,000
DEPLGAMTR|n|AMT DEP ACC GREATER THAN $250,000 RATIO
DEPLGB|n|NUM DEP ACC GREATER THAN $250,000
DEPLGRA|n|AMT OF RETIREMENT DEP ACC OF MORE THAN $250,000
DEPLGRAR|n|AMT OF RETIREMENT DEP ACC OF MORE THAN $250,000 RATIO
DEPLGRN|n|NUM OF RETIREMENT DEP ACC MORE THAN $250,000
DEPLSNB|n|DEP THRU LIST SVC NOT BROKERED
DEPLSNBR|n|DEP THRU LIST SVC NOT BROKERED RATIO
DEPNIDOM|n|NONINTEREST-BEARING DEP-DOM
DEPNIDOMR|n|NONINTEREST-BEARING DEP-DOM RATIO
DEPSMAMT|n|AMT DEP ACC AT $250,000 OR LESS
DEPSMAMTR|n|AMT DEP ACC AT $250,000 OR LESS RATIO
DEPSMB|n|NUM DEP ACC EQUAL OR LESS THAN EQUAL TO $250,000
DEPSMRA|n|AMT RETIREMENT DEP ACC OF $250,000 OR LESS
DEPSMRAR|n|AMT RETIREMENT DEP ACC OF $250,000 OR LESS RATIO
DEPSMRN|n|NUM RETIREMENT DEP ACC OF $250,000
DEPALLEX|n|TOTAL ALLOWABLE EXCLUSIONS (INCLUDING FOREIGN DEPOSITS)
DEPUNA|n|EST UNINSURED DEP IN DOM-OFF IN INSURED BRANCHES IN US TERR 
DEPUNAR|n|EST UNINSURED DEP IN DOM-OFF IN INSURED BRANCHES IN US TERR 
DEPUNINS|n|ESTIMATED UNINSURED DEPOSITS IN DOMESTIC OFFICES AND IN INSU
DEPUSBKF|n|U.S. BANKS&OTH.US INST-FOR
DEPUSBKFR|n|U.S. BANKS&OTH.US INST-FOR RATIO
DEPUSMF|n|U.S.GOVT & ST & POL SUBS-FOR
DEPUSMFR|n|U.S.GOVT & ST & POL SUBS-FOR RATIO
DRAG|n|AGRICULTURAL LOAN CHARGE-OFFS
DRAGR|n|AGRICULTURAL LOAN CHARGE-OFFS RATIO
DRAGQ|n|AGRICULTURAL LOAN CHARGE-OFFS QUARTERLY
DRAGQR|n|AGRICULTURAL LOAN CHARGE-OFFS QUARTERLY RATIO
DRAGSM|n|AG LOAN CHARGE-OFFS*SMALL BKS
DRAGSMR|n|AG LOAN CHARGE-OFFS*SMALL BKS RATIO
DRAGSMQ|n|AG LOAN CHARGE-OFFS*SMALL BKS QUARTERLY
DRAGSMQR|n|AG LOAN CHARGE-OFFS*SMALL BKS QUARTERLY RATIO
DRAUTO|n|AUTO LOANS - CHARGE-OFFS
DRAUTOR|n|AUTO LOANS - CHARGE-OFFS RATIO
DRAUTOQ|n|AUTO LOANS - CHARGE-OFFS QUARTERLY
DRAUTOQR|n|AUTO LOANS - CHARGE-OFFS QUARTERLY RATIO
DRCI|n|COMMERCIAL LOAN CHARGE-OFFS
DRCIR|n|COMMERCIAL LOAN CHARGE-OFFS RATIO
DRCIQ|n|COMMERCIAL LOAN CHARGE-OFFS QUARTERLY
DRCIQR|n|COMMERCIAL LOAN CHARGE-OFFS QUARTERLY RATIO
DRCINUS|n|COMMERCIAL LOAN CHARGE-OFFS NON-U.S.
DRCINUSR|n|COMMERCIAL LOAN CHARGE-OFFS NON-U.S. RATIO
DRCINUSQ|n|COMMERCIAL LOAN CHARGE-OFFS NON-U.S. QUARTERLY
DRCINUSQR|n|COMMERCIAL LOAN CHARGE-OFFS NON-U.S. QUARTERLY RATIO
DRCON|n|CONSUMER LOAN CHARGE-OFFS
DRCONR|n|CONSUMER LOAN CHARGE-OFFS RATIO
DRCONQ|n|CONSUMER LOAN CHARGE-OFFS QUARTERLY
DRCONQR|n|CONSUMER LOAN CHARGE-OFFS QUARTERLY RATIO
DRCONOTH|n|OTHER CONSUMER LOAN CHARGE-OFFS
DRCONOTHR|n|OTHER CONSUMER LOAN CHARGE-OFFS RATIO
DRCONOTQ|n|OTHER CONSUMER LOAN CHARGE-OFFS QUARTERLY
DRCONOTQR|n|OTHER CONSUMER LOAN CHARGE-OFFS QUARTERLY RATIO
DRCRCD|n|CREDIT CARD LOAN CHARGE-OFFS
DRCRCDR|n|CREDIT CARD LOAN CHARGE-OFFS RATIO
DRCRCDQ|n|CREDIT CARD LOAN CHARGE-OFFS QUARTERLY
DRCRCDQR|n|CREDIT CARD LOAN CHARGE-OFFS QUARTERLY RATIO
DRDEP|n|DEPOSITORY INST LOAN CHARGE-OFFS
DRDEPR|n|DEPOSITORY INST LOAN CHARGE-OFFS RATIO
DRDEPQ|n|DEPOSITORY INST LOAN CHARGE-OFFS QUARTERLY
DRDEPQR|n|DEPOSITORY INST LOAN CHARGE-OFFS QUARTERLY RATIO
DRDEPNUS|n|FOREIGN DEPS INST LN CHG-OFFS
DRDEPNUSR|n|FOREIGN DEPS INST LN CHG-OFFS RATIO
DRDEPNUQ|n|FOREIGN DEPS INST LN CHG-OFFS QUARTERLY
DRDEPNUQR|n|FOREIGN DEPS INST LN CHG-OFFS QUARTERLY RATIO
DRFORGV|n|FOREIGN GOVERNMENT LN CHG-OFFS
DRFORGVR|n|FOREIGN GOVERNMENT LN CHG-OFFS RATIO
DRFORGVQ|n|FOREIGN GOVERNMENT LN CHG-OFFS QUARTERLY
DRFORGVQR|n|FOREIGN GOVERNMENT LN CHG-OFFS QUARTERLY RATIO
DRLS|n|LEASE CHARGE-OFFS
DRLSR|n|LEASE CHARGE-OFFS RATIO
DRLSQ|n|LEASE CHARGE-OFFS QUARTERLY
DRLSQR|n|LEASE CHARGE-OFFS QUARTERLY RATIO
DROTHER|n|ALL OTHER LOAN CHARGE-OFFS
DROTHERR|n|ALL OTHER LOAN CHARGE-OFFS RATIO
DROTHQ|n|ALL OTHER LOAN CHARGE-OFFS QUARTERLY
DROTHQR|n|ALL OTHER LOAN CHARGE-OFFS QUARTERLY RATIO
DRRE|n|REAL ESTATE LOAN CHARGE-OFFS
DRRER|n|REAL ESTATE LOAN CHARGE-OFFS RATIO
DRREQ|n|REAL ESTATE LOAN CHARGE-OFFS QUARTERLY
DRREQR|n|REAL ESTATE LOAN CHARGE-OFFS QUARTERLY RATIO
DRREAG|n|FARMLAND RE LN CHARGE-OFFS
DRREAGR|n|FARMLAND RE LN CHARGE-OFFS RATIO
DRREAGQ|n|FARMLAND RE LN CHG-OFFS-QTR
DRREAGQR|n|FARMLAND RE LN CHARGE-OFFS QUARTERLY RATIO
DRRECNFM|n|1-4 FAM CONSTRUCT LN CHARGE-OFFS
DRRECNOT|n|OTHER CONSTRUCT LN CHARGE-OFFS
DRRECONQ|n|CONSTRUCTION RE LN CHG-OFFS-QTR
DRRECONQR|n|CONSTRUCTION RE LN CHARGE-OFFS QUARTERLY RATIO
DRRECONS|n|CONSTRUCTION RE LN CHARGE-OFFS
DRRECONSR|n|CONSTRUCTION RE LN CHARGE-OFFS RATIO
DRREFOR|n|REAL ESTATE LOAN CHRG-OFFS-FOR
DRREFORR|n|REAL ESTATE LOAN CHRG-OFFS-FOR RATIO
DRREFORQ|n|REAL ESTATE LOAN CHRG-OFFS-FOR QUARTERLY
DRREFORQR|n|REAL ESTATE LOAN CHRG-OFFS-FOR QUARTERLY RATIO
DRRELOC|n|LINE OF CREDIT RE LN CHARGE-OFFS
DRRELOCR|n|LINE OF CREDIT RE LN CHARGE-OFFS RATIO
DRRELOCQ|n|LINE OF CREDIT RE LN CHARGE-OFFS QUARTERLY
DRRELOCQR|n|LINE OF CREDIT RE LN CHARGE-OFFS RATIO
DRREMULQ|n|MULTIFAMILY RE LN CHG-OFFS-QTR
DRREMULQR|n|MULTIFAMILY RES RE LN CHARGE-OFF QUARTERLY RATIO
DRREMULT|n|MULTIFAMILY RES RE LN CHARGE-OFF
DRREMULTR|n|MULTIFAMILY RES RE LN CHARGE-OFF RATIO
DRRENRES|n|NONFARM NONRES RE LN CHARGE-OFFS
DRRENRESR|n|NONFARM NONRES RE LN CHARGE-OFFS RATIO
DRRENROT|n|OTHER NONFARM NONRES RE CHG-OFF
DRRENROW|n|OWN-OCCUP NONFARM NONRES CHG-OFF
DRRENRSQ|n|NONFARM NONRES RE LN CHG-OFF-QTR
DRRENRSQR|n|NONFARM NONRES RE LN CHARGE-OFFS QUARTERLY RATIO
DRRENUS|n|NON-U.S. RE LN CHARGE-OFFS
DRRENUSR|n|NON-U.S. RE LN CHARGE-OFFS RATIO
DRRENUSQ|n|NON-U.S. RE LN CHARGE-OFFS QUARTERLY
DRRENUSQR|n|NON-U.S. RE LN CHARGE-OFFS RATIO
DRRERES|n|RE LOANS 1-4 FAMILY CHARGE-OFFS
DRRERESR|n|RE LOANS 1-4 FAMILY CHARGE-OFFS RATIO
DRRERESQ|n|RE LOANS 1-4 FAMILY CHG-OFFS-QTR
DRRERESQR|n|RE LOANS 1-4 FAMILY CHARGE-OFFS QUARTERLY RATIO
DRRERSF2|n|RE LN 1-4 FAM JR LIEN-CHG-OFF
DRRERSF2R|n|RE LN 1-4 FAM JR LIEN-CHG-OFF RATIO
DRRERS2Q|n|RE LN 1-4 FAM JR LIEN-CHG-OFF QUARTERLY
DRRERS2QR|n|RE LN 1-4 FAM JR LIEN-CHG-OFF QUARTERLY RATIO
DRRERSFM|n|RE LN 1-4 FAM FIRST LIEN-CHG-OFF
DRRERSFMR|n|RE LN 1-4 FAM FIRST LIEN-CHG-OFF RATIO
DRRERSFQ|n|RE LN 1-4 FAM FIRST LIEN-CHG-OFF QUARTERLY
DRRERSFQR|n|RE LN 1-4 FAM FIRST LIEN-CHG-OFF QUARTERLY RATIO
DRREOFFDOM|n|REAL ESTATE LOAN CHARGE-OFFS DOMESTIC OFFICES
DRREOFFDOMR|n|REAL ESTATE LOAN CHARGE-OFFS DOMESTIC OFFICES RATIO
DRREOFFDOMQ|n|REAL ESTATE LOAN CHARGE-OFFS DOMESTIC OFFICES QUARTERLY
DRREOFFDOMQR|n|REAL ESTATE LOAN CHARGE-OFFS DOMESTIC OFFICES QUARTERLY RATI
EDCM|n|EQUITY
EEFF|n|EFFICIENCY RATIO EXPENSE
EEFFQ|n|EFFICIENCY RATIO EXPENSE QUARTERLY
EEFFR|n|EFFICIENCY RATIO
EEFFQR|n|EFFICIENCY QUARTERLY RATIO
EFFDATE|n|EFFECTIVE DATE
EINTGW|n|GOODWILL IMPAIRMENT LOSSES
EINTGWR|n|GOODWILL IMPAIRMENT LOSSES RATIO
EINTGWQ|n|GOODWILL IMPAIRMENT LOSSES QUARTERLY
EINTGWQR|n|GOODWILL IMPAIRMENT LOSSES QUARTERLY RATIO
EINTOTH|n|AMORT & IMPAIR LOSSES OTH INTAN
EINTOTHR|n|AMORT & IMPAIR LOSSES OTH INTAN RATIO
EINTOTHQ|n|AMORT & IMPAIR LOSSES OTH INTAN QUARTERLY
EINTOTHQR|n|AMORT & IMPAIR LOSSES OTH INTAN QUARTERLY RATIO
ELNANTR|n|LOAN LOSS PROV/NT CHG-OFFS
ELNATRA|n|ELNATRA
ELNATRY|n|CREDIT LOSS PROV/AVE ASSETS
ELNATRYQ|n|CREDIT LOSS PROV/AVE ASSETS QUARTERLY
ENCEAUTO|n|CR EXPOSURE-ENHANCEMENTS - AUTO
ENCEAUTOR|n|CR EXPOSURE-ENHANCEMENTS - AUTO RATIO
ENCECI|n|CR EXPOSURE - ENHANCEMENTS - CI
ENCECIR|n|CR EXPOSURE - ENHANCEMENTS - CI RATIO
ENCECON|n|CR EXPOSURE - ENHANCEMENTS - CON
ENCECONR|n|CR EXPOSURE - ENHANCEMENTS - CON RATIO
ENCEOTH|n|CR EXPOSURE - ENHANCEMENTS - OTH
ENCEOTHR|n|CR EXPOSURE - ENHANCEMENTS - OTH RATIO
ENCERES|n|CR EXPOSURE - ENHANCEMENTS - RES
ENCERESR|n|CR EXPOSURE - ENHANCEMENTS - RES RATIO
EOTHINT|n|OTHER INTEREST EXPENSE
EOTHINTR|n|OTHER INTEREST EXPENSE RATIO
EOTHINTQ|n|OTHER INTEREST EXPENSE QUARTERLY
EOTHINTQR|n|OTHER INTEREST EXPENSE QUARTERLY RATIO
EQ5|n|TOTAL BANK EQUITY CAPITAL-CAVG5
EQCBHCTR|n|TRANSACTIONS WITH BHC
EQCBHCTRR|n|TRANSACTIONS WITH BHC RATIO
EQCCOMPI|n|OTHER COMPREHENSIVE INCOME
EQCCOMPIR|n|OTHER COMPREHENSIVE INCOME RATIO
EQCDIVA|n|CASH DIVIDENDS ON COMM & PFD-ANN
EQCMRG|n|CHANGES DUE TO MERGERS
EQCMRGR|n|CHANGES DUE TO MERGERS RATIO
EQCPREV|n|BK EQ CAP MOST RECENTLY REPORTED
EQCPREVR|n|BK EQ CAP MOST RECENTLY REPORTED RATIO
EQCREST|n|ACCOUNTING CHANGES & CORRECTIONS
EQCRESTR|n|ACCOUNTING CHANGES & CORRECTIONS RATIO
EQCSTKRX|n|SALE OF CAPITAL STOCK
EQCSTKRXR|n|SALE OF CAPITAL STOCK RATIO
EQCSXQ|n|SALE OF CAPITAL STOCK QUARTERLY
EQCSXQR|n|SALE OF CAPITAL STOCK QUARTERLY RATIO
EQCTRSTX|n|TREASURY STOCK TRANSACTIONS
EQCTRSTXR|n|TREASURY STOCK TRANSACTIONS RATIO
EQTOT|n|TOTAL EQUITY CAPITAL
EQTOTR|n|TOTAL EQUITY CAPITAL RATIO
EQV|n|BANK EQUITY CAPITAL/ASSETS
ERNAST|n|TOTAL EARNING ASSETS
ERNAST2|n|TOTAL EARNING ASSETS
ERNAST5|n|TOTAL EARNING ASSETS-CAVG5I
ERNASTR|n|EARNING ASSETS / TOTAL ASSETS
ESTYMD|n|ESTABLISHED DATE
ENDEFYMD|n|INACTIVE DATE
ORG_END_NUM_DTE|n|INACTIVE DATE
ETTLOTMG|n|TT&L
FORMTFR|n|THRIFT FINANCIAL REPORT FLAG
FX|n|FOREIGN EXCHANGE-TOTAL CONTRACTS
FXFFC|n|FOR EXCH-FUTURES & FORWARD CONTR
FXNVS|n|FOR EXCHANGE-SWAPS
FXPOC|n|FOR EXCH-PUR OPTION CONTRACTS
FXSPOT|n|SPOT FOREIGN EXCHANGE CONTRACTS
FXWOC|n|FOR EXCH-WRITTEN OPTION CONTRACT
IBEFTXQ|n|INC BEFORE INC TAXS & DISC-QTR
IBEFXTR|n|INCOME BEFORE DISC OPR
IBEFXTRR|n|INCOME BEFORE DISC OPR RATIO
IBEFXTRQ|n|INCOME BEFORE DISC OPR QUARTERLY
IEFF|n|EFFICIENCY RATIO INCOME
IEFFQ|n|EFFICIENCY RATIO INCOME QUARTERLY
IBEFXTRQR|n|INCOME BEFORE DISC OPR QUARTERLY RATIO
IFIDUC|n|FIDUCIARY ACTIVITIES INCOME
IFIDUCR|n|FIDUCIARY ACTIVITIES INCOME RATIO
IFIDUCQ|n|FIDUCIARY ACTIVITIES INCOME-QTR
IFIDUCQR|n|FIDUCIARY ACTIVITIES INCOME-QTR RATIO
IGLCMEX|n|TRADING ACCOUNT-COMMODITY
IGLCMEXR|n|TRADING ACCOUNT-COMMODITY RATIO
IGLCMEXQ|n|TRADING ACCOUNT-COMMODITY QUARTERLY
IGLCMEXQR|n|TRADING ACCOUNT-COMMODITY RATIO QUARTERLY
IGLCREX|n|TRADING REVENUE- CREDIT EXPOSURE
IGLCREXR|n|TRADING REVENUE- CREDIT EXPOSURE RATIO
IGLCREXQ|n|TRADING REVENUE- CREDIT EXPOSURE QUARTERLY
IGLCREXQR|n|TRADING REVENUE- CREDIT EXPOSURE QUARTERLY RATIO
IGLEDEX|n|TRADING ACCOUNT-EQ DERIVATIVE
IGLEDEXR|n|TRADING ACCOUNT-EQ DERIVATIVE RATIO
IGLEDEXQ|n|TRADING ACCOUNT-EQ DERIVATIVE QUARTERLY
IGLEDEXQR|n|TRADING ACCOUNT-EQ DERIVATIVE QUARTERLY RATIO
IGLFXEX|n|TRADING ACCOUNT-FOREIGN EXCHANGE
IGLFXEXR|n|RADING ACCOUNT-FOREIGN EXCHANGE RATIO
IGLFXEXQ|n|TRADING ACCOUNT-FOREIGN EXCHANGE QUARTERLY
IGLFXEXQR|n|RADING ACCOUNT-FOREIGN EXCHANGE QUARTERLY RATIO
IGLRTEX|n|TRADING ACCOUNT-INTEREST RATE
IGLRTEXR|n|TRADING ACCOUNT-INTEREST RATE RATIO
IGLRTEXQ|n|TRADING ACCOUNT-INTEREST RATE QUARTERLY
IGLRTEXQR|n|TRADING ACCOUNT-INTEREST RATE QUARTERLY RATIO
IGLSECQ|n|SECURITIES GAINS AND LOSSES-QTR
IGLTRAD|n|TRADING REVENUES-TOTAL
IGLTRADR|n|TRADING REVENUES-TOTAL RATIO
IGLTRDQ|n|TRADING REVENUE-QTR
IGLTRDQR|n|TRADING REVENUE-QTR RATIO
IINSCOM|n|INSURANCE COMMISSIONS & FEES
IINSCOMR|n|INSURANCE COMMISSIONS & FEES RATIO
IINSCOMQ|n|INSURANCE COMMISSIONS & FEES QUARTERLY
IINSCOMQR|n|INSURANCE COMMISSIONS & FEES QUARTERLY RATIO
IINSOTH|n|INSURANCE COM+FEES-OTHER
IINSOTHR|n|INSURANCE COM+FEES-OTHER RATIO
IINSOTHQ|n|INSURANCE COM+FEES-OTHER QUARTERLY
IINSOTHQR|n|INSURANCE COM+FEES-OTHER QUARTERLY RATIO
IINSUND|n|INSURANCE UNDERWRITNG INCOME
IINSUNDR|n|INSURANCE UNDERWRITNG INCOME RATIO
IINSUNDQ|n|INSURANCE UNDERWRITNG INCOME QUARTERLY
IINSUNDQR|n|INSURANCE UNDERWRITNG INCOME QUARTERLY RATIO
IINVFEE|n|INVEST BANK
IINVFEER|n|INVEST BANK RATIO
IINVFEEQ|n|INVEST BANK QUARTERLY
IINVFEEQR|n|INVEST BANK QUARTERLY RATIO
INSAGNT1|s|PRIMARY INSURER
INTANGCC|n|PURCH CC REL & NONMTG SER ASTS
INTANGW|n|GOODWILL
INTANGWR|n|GOODWILL RATIO
INTANMSR|n|MORTGAGE SERVICING ASSETS
INTANMSRR|n|MORTGAGE SERVICING ASSETS RATIO
INTANOTH|n|OTHER IDENTIFIABLE INTANG ASSETS
INTANOTHR|n|OTHER IDENTIFIABLE INTANG ASSETS RATIO
INTINCYQ|n|INTEREST INCOME/EARNING ASSETS QUARTERLY
INTINCA|n|TOTAL INTEREST INCOME ANNUAL
IOTNII|n|OTHER NONINTEREST INCOME
IOTNIIR|n|OTHER NONINTEREST INCOME RATIO
IOTNIIQ|n|OTHER NONINTEREST INCOME QUARTERLY
IOTNIIQR|n|OTHER NONINTEREST INCOME QUARTERLY RATIO
ISECZ|n|SECURITIZATION INCOME
ISECZR|n|SECURITIZATION INCOME RATIO
ISECZQ|n|SECURITIZATION INCOME QUARTERLY
ISECZQR|n|SECURITIZATION INCOME QUARTERLY RATIO
ISERCHGQ|n|SERVICE CHARGE ON DEP ACCTS-QTR
ISERCHGQR|n|SERVICE CHARGE ON DEPOSIT ACCTS-QTR RATIO
ISERFEE|n|SERVICING FEES
ISERFEER|n|SERVICING FEES RATIO
ISERFEEQ|n|SERVICING FEES QUARTERLY
ISERFEEQR|n|SERVICING FEES QUARTERLY RATIO
IVENCAP|n|VENTURE CAPITAL REVENUE
IVENCAPR|n|VENTURE CAPITAL REVENUE RATIO
IVENCAPQ|n|VENTURE CAPITAL REVENUE QUARTERLY
IVENCAPQR|n|VENTURE CAPITAL REVENUE QUARTERLY RATIO
LAG|n|AG LOANS - LOSS SHARE
LAGR|n|AG LOANS - LOSS SHARE RATIO
LCI|n|C&I LOANS - LOSS SHARE
LCIR|n|C&I LOANS - LOSS SHARE RATIO
LCON|n|CONSUMER LOANS - LOSS SHARE
LCONR|n|CONSUMER LOANS - LOSS SHARE RATIO
LIABFOR|n|TOTAL LIABILITIES-FOR
LNAG1|n|AGRICULTURAL LOANS-UNDER 100-$
LNAG1R|n|AGRICULTURAL LOANS-UNDER 100-$ RATIO
LNAG2|n|AGRICULTURAL LOANS-100-250-$
LNAG2R|n|AGRICULTURAL LOANS-100-250-$ RATIO
LNAG3|n|AGRICULTURAL LOANS-250-500-$
LNAG3R|n|AGRICULTURAL LOANS-250-500-$ RATIO
LNAG4|n|AGRICULTURAL LOANS-UNDER 500-$
LNAG4R|n|AGRICULTURAL LOANS-UNDER 500-$ RATIO
LNAG5|n|AG LOANS-CAVG5
LNAG22|n|AG LOANS-CAVG2
LNAG1N|n|AGRICULTURAL LOANS-UNDER 100-NUM
LNAG1NR|n|AGRICULTURAL LOANS-UNDER 100-NUM RATIO
LNAG2N|n|AGRICULTURAL LOANS-100-250-NUM
LNAG2NR|n|AGRICULTURAL LOANS-100-250-NUM RATIO
LNAG3N|n|AGRICULTURAL LOANS-250-500-NUM
LNAG3NR|n|AGRICULTURAL LOANS-250-500-NUM RATIO
LNAG4N|n|AGRICULTURAL LOANS-UNDER 500-NUM
LNAG4NR|n|AGRICULTURAL LOANS-UNDER 500-NUM RATIO
LNAGFOR|n|AGRICULTURAL LOANS-FOR
LNAGFORR|n|AGRICULTURAL LOANS-FOR RATIO
LNATRESR|n|LOAN LOSS RESERVE/GROSS LN&LS
LNAUTO2|n|CONSUMER LOANS - AUTO - CAVG2
LNAUTO5|n|CONSUMER LOANS - AUTO - CAVG5
LNCI1|n|C&I LOANS-UNDER-100-$
LNCI1R|n|C&I LOANS-UNDER-100-$ RATIO
LNCI2|n|C&I LOANS-100-250-$
LNCI2R|n|C&I LOANS-100-250-$ RATIO
LNCI3|n|C&I LOANS-250-1M-$
LNCI3R|n|C&I LOANS-250-1M-$ RATIO
LNCI4|n|C&I LOANS-UNDER-1M-$
LNCI4R|n|C&I LOANS-UNDER-1M-$ RATIO
LNCI5|n|C&I LOANS-CAVG5
LNCI22|n|C&I LOANS-CAVG2
LNCI1N|n|C&I LOANS-UNDER-100-NUM
LNCI1NR|n|C&I LOANS-UNDER-100-NUM RATIO
LNCI2N|n|C&I LOANS-100-250-NUM
LNCI2NR|n|C&I LOANS-250-1M-NUM RATIO
LNCI3N|n|C&I LOANS-250-1M-NUM
LNCI3NR|n|C&I LOANS-250-1M-NUM RATIO
LNCI4N|n|C&I LOANS-UNDER-1M-NUM
LNCI4NR|n|C&I LOANS-UNDER-1M-NUM RATIO
LNCIFOR|n|C&I LOANS-FOR
LNCIFORR|n|C&I LOANS-FOR RATIO
LNCINUS|n|C&I LOANS-NON-U.S. DOMICILE
LNCINUSF|n|C&I LOANS-NON-U.S. DOMICILE-FOR
LNCINUSFR|n|C&I LOANS-NON-U.S. DOMICILE-FOR RATIO
LNCOMRE|n|COMMERCIAL RE LOANS
LNCOMRER|n|COMMERCIAL RE LOANS RATIO
LNCOMRE2|n|COMMERCIAL RE LOANS2
LNCOMRE5|n|COMMERCIAL RE LOANS CAVG5
LNCON2|n|CONSUMER LOANS-CAVG2
LNCON5|n|CONSUMER LOANS-CAVG5
LNCONFOR|n|CONSUMER LOANS-FOR
LNCONFORR|n|CONSUMER LOANS-FOR RATIO
LNCONORP|n|OTHER CONSUMER & RELATED PLANS
LNCONOT2|n|OTHER CONSUMER LOANS-CAVG2
LNCONOT5|n|OTHER CONSUMER LOANS-CAVG5
LNCONRP|n|CONSUMER LNS-RELATED PLANS
LNCONRPR|n|CONSUMER LNS-RELATED PLANS RATIO
LNCONTRA|n|OTHER CONTRA ACCOUNTS
LNCONTRAR|n|OTHER CONTRA ACCOUNTS RATIO
LNCRCD2|n|CREDIT CARD PLANS-CAVG2
LNCRCD5|n|CREDIT CARD PLANS-CAVG5
LNDEPAC|n|TOTAL DEP INST LNS & ACCEPT
LNDEPACD|n|TOTAL DEP INST LNS & ACCEPT-DOM
LNDEPAOBK|n|LOANS TO DEPOSITORY INSTITUTIONS AND ACCEPTANCE OF OTHER BAN
LNDEPAOBKR|n|LOANS TO DEPOSITORY INSTITUTIONS AND ACCEPTANCE OF OTHER BAN
LNDEPCB|n|DEP INST LNS-COMMERCIAL BANKS
LNDEPCBF|n|DEP INST LNS-COMMERCIAL BK-FOR
LNDEPCBFR|n|DEP INST LNS-COMMERCIAL BK-FOR RATIO
LNDEPFC|n|DEP INST LNS-FOR COUNTRY
LNDEPFCF|n|DEP INST LNS-FOR COUNTRY-FOR
LNDEPFCFR|n|DEP INST LNS-FOR COUNTRY-FOR RATIO
LNDEPFUS|n|DEP INST LNS-FOR COUNTRY-U.S. BR
LNDEPUS|n|DEP INST LNS-OTH U.S. INST
LNDEPUSB|n|DEP INST LNS-COM BKS-U.S.BRANCH
LNDEPUSF|n|DEP INST LNS-OTH U.S. INST-FOR
LNDEPUSFR|n|DEP INST LNS-OTH U.S. INST-FOR RATIO
LNEXAMT|n|EXECUTIVE OFFICER LOANS-AMOUNT
LNEXAMTR|n|EXECUTIVE OFFICER LOANS-AMOUNT RATIO
LNFGFOR|n|FOREIGN GOVT LOANS-FOR
LNFGFORR|n|FOREIGN GOVT LOANS-FOR RATIO
LNLSDEPR|n|NET LOANS & LEASES/DEPOSITS
LNLSFOR|n|LN&LS + UNEARNED INC-FOR
LNLSFORR|n|LN&LS + UNEARNED INC-FOR RATIO
LNLSGR5|n|LOANS AND LEASES-TOTAL-CAVG5
LNLSGRF|n|LOANS AND LEASES-TOTAL-FOR
LNLSGRFR|n|LOANS AND LEASES-TOTAL-FOR RATIO
LNLSNTV|n|NET LOANS & LEASES/ASSETS
LNLSNQR|n|NET LOANS & LEASES/ASSETS QUARTERLY RATIO
LNLSSALE|n|LOANS & LEASES HELD FOR RESALE
LNLSSALER|n|LOANS & LEASES HELD FOR RESALE RATIO
LNPLEDGE|n|PLEDGED LOANS AND LEASES
LNPLEDGER|n|PLEDGED LOANS AND LEASES RATIO
LNMUNIF|n|MUNI LOANS-FOR
LNMUNIFR|n|MUNI LOANS-FOR RATIO
LNOT1T3|n|ALL OTHER LNS & LS * 1-3 YEARS
LNOT1T3R|n|ALL OTHER LNS & LS * 1-3 YEARS RATIO
LNOT3LES|n|ALL OTHER LNS & LS*3 MO OR LESS
LNOT3LESR|n|ALL OTHER LNS & LS*3 MO OR LESS RATIO
LNOT3T5|n|ALL OTHER LNS & LS * 3-5 YEARS
LNOT3T5R|n|ALL OTHER LNS & LS * 3-5 YEARS RATIO
LNOT3T12|n|ALL OTHER LNS & LS * 3-12 MONS
LNOT3T12R|n|ALL OTHER LNS & LS * 3-12 MONS RATIO
LNOT5T15|n|ALL OTHER LNS & LS * 5-15 YEARS
LNOT5T15R|n|ALL OTHER LNS & LS * 5-15 YEARS RATIO
LNOTCI2|n|OTHER LOANS & LEASES-QBP-CAVG2
LNOTCI5|n|OTHER LOANS & LEASES-QBP-CAVG5
LNOTHERF|n|LN TO NONDEP FIN INST & OTH-FGN
LNOTHERFR|n|LN TO NONDEP FIN INST & OTH-FGN RATIO
LNOTOV15|n|ALL OTHER LNS & LS * OVER 15 YRS
LNOTOV15R|n|ALL OTHER LNS & LS * OVER 15 YRS RATIO
LNREAG1|n|RE AGRICULTURAL-UNDER 100-$
LNREAG1R|n|RE AGRICULTURAL-UNDER 100-$ RATIO
LNREAG2|n|RE AGRICULTURAL-100-250-$
LNREAG2R|n|RE AGRICULTURAL-100-250-$ RATIO
LNREAG3|n|RE AGRICULTURAL-250-500-$
LNREAG3R|n|RE AGRICULTURAL-250-500-$ RATIO
LNREAG4|n|RE AGRICULTURAL-UNDER 500-$
LNREAG4R|n|RE AGRICULTURAL-UNDER 500-$ RATIO
LNREAG1N|n|RE AGRICULTURAL-UNDER 100-NUM
LNREAG1NR|n|RE AGRICULTURAL-UNDER 100-NUM RATIO
LNREAG2N|n|RE AGRICULTURAL-100-250-NUM
LNREAG2NR|n|RE AGRICULTURAL-100-250-NUM RATIO
LNREAG3N|n|RE AGRICULTURAL-250-500-NUM
LNREAG3NR|n|RE AGRICULTURAL-250-500-NUM RATIO
LNREAG4N|n|RE AGRICULTURAL-UNDER 500-NUM
LNREAG4NR|n|RE AGRICULTURAL-UNDER 500-NUM RATIO
LNRECNFM|n|1-4 FAM RE CONSTRUCTION LOANS
LNRECNFMR|n|1-4 FAM RE CONSTRUCTION LOANS RATIO
LNRECNOT|n|OTHER RE CONSTRUCTION & LAND LN
LNRECNOTR|n|OTHER RE CONSTRUCTION & LAND LN
LNREOTH|n|ALL OTHER RE OWNED-1-4 FAMILY
LNREOTH2|n|ALL OTHER RE OWNED-1-4 FAMILY2
LNREOTH5|n|RE 1-4 FAMILY OTHER LOANS CAVG5
LNRENR1|n|RE NONFARM NONRES-UNDER 100-$
LNRENR1R|n|RE NONFARM NONRES-UNDER 100-$ RATIO
LNRENR2|n|RE NONFARM NONRES-100-250-$
LNRENR2R|n|RE NONFARM NONRES-100-250-$ RATIO
LNRENR3|n|RE NONFARM NONRES-250-1M-$
LNRENR3R|n|RE NONFARM NONRES-250-1M-$ RATIO
LNRENR4|n|RE NONFARM NONRES-UNDER 1M-$
LNRENR4R|n|RE NONFARM NONRES-UNDER 1M-$ RATIO
LNRENR1N|n|RE NONFARM NONRES-UNDER 100-NUM
LNRENR1NR|n|RE NONFARM NONRES-UNDER 100-NUM RATIO
LNRENR2N|n|RE NONFARM NONRES-100-250-NUM
LNRENR2NR|n|RE NONFARM NONRES-100-250-NUM RATIO
LNRENR3N|n|RE NONFARM NONRES-250-1M-NUM
LNRENR3NR|n|RE NONFARM NONRES-250-1M-NUM RATIO
LNRENR4N|n|RE NONFARM NONRES-UNDER 1M-NUM
LNRENR4NR|n|RE NONFARM NONRES-UNDER 1M-NUM RATIO
LNRENROT|n|OTHER NONFARM NONRES RE LNS
LNRENROTR|n|OTHER NONFARM NONRES RE LNS RATIO
LNRENROW|n|OWNER-OCC NONFARM NONRES RE LNS
LNRENROWR|n|OWNER-OCC NONFARM NONRES RE LNS
LNRENUS|n|RE LNS-NON US ADDRESSEES
LNRENUSR|n|RE LNS-NON US ADDRESSEES RATIO
LNRERSF1|n|RE 1-4 FAMILY-FIRST LIENS-ADJUST
LNRERSF1R|n|RE 1-4 FAMILY-FIRST LIENS-ADJUST RATIO
LNRERSF2|n|RE 1-4 FAMILY-SECOND LIENS
LNRERSF2R|n|RE 1-4 FAMILY-SECOND LIENS RATIO
LNRERSFM|n|RE 1-4 FAMILY-FIRST LIENS
LNRERSFMR|n|RE 1-4 FAMILY-FIRST LIENS RATIO
LNRESNCR|n|LOAN LOSS RESERVE/N/C LOANS
LNRS1T3|n|RE 1-4 FAMILY * 1-3 YEARS
LNRS1T3R|n|RE 1-4 FAMILY * 1-3 YEARS RATIO
LNRS3LES|n|RE 1-4 FAMILY * 3 MONS OR LESS
LNRS3LESR|n|RE 1-4 FAMILY * 3 MONS OR LESS RATIO
LNRS3T5|n|RE 1-4 FAMILY * 3-5 YEARS
LNRS3T5R|n|RE 1-4 FAMILY * 3-5 YEARS RATIO
LNRS3T12|n|RE 1-4 FAMILY * 3-12 MONTHS
LNRS3T12R|n|RE 1-4 FAMILY * 3-12 MONTHS RATIO
LNRS5T15|n|RE 1-4 FAMILY * 5-15 YEARS
LNRS5T15R|n|RE 1-4 FAMILY * 5-15 YEARS RATIO
LNRSOV15|n|RE 1-4 FAMILY * OVER 15 YEARS
LNRSOV15R|n|RE 1-4 FAMILY * OVER 15 YEARS RATIO
LNSB|n|SMALL BUSINESS LNS SOLD-AMT
LNSBR|n|SMALL BUSINESS LNS SOLD
LNSERV|n|PRIN BAL- LNS SERVICE FOR OTHERS
LNSERVR|n|PRIN BAL- LNS SERVICE FOR OTHERS RATIO
LOCCOM|n|COMMERCIAL LETTERS OF CREDIT
LOCCOMR|n|COMMERCIAL LETTERS OF CREDIT RATIO
LOCFPSB|n|FIN & PERFORM STANDBY LOC
LOCFPSBR|n|FIN & PERFORM STANDBY LOC RATIO
LOCFPSBK|n|FIN & PERFORM STANDBY LOC-CONVEY
LOCFPSBKR|n|FIN & PERFORM STANDBY LOC-CONVEY RATIO
LOCFSB|n|FINANCIAL STANDBY LOC
LOCFSBR|n|FINANCIAL STANDBY LOC RATIO
LOCFSBK|n|FINANCIAL STANDBY LOC-CONVEYED
LOCFSBKR|n|FINANCIAL STANDBY LOC-CONVEYED RATIO
LOCPSB|n|PERFORMANCE STANDBY LOC
LOCPSBR|n|PERFORMANCE STANDBY LOC RATIO
LOCPSBK|n|PERFORMANCE STANDBY LOC-CONVEYED
LOCPSBKR|n|PERFORMANCE STANDBY LOC-CONVEYED RATIO
LOREGTY|n|ORE PROTECTED - LOSS SHARE
LOREGTYR|n|ORE PROTECTED - LOSS SHARE RATIO
LOTH|n|ALL OTHER LN & LS - LOSS SHARE
LOTHR|n|ALL OTHER LN & LS - LOSS SHARE RATIO
LREAG|n|RE FARMLAND LN - LOSS SH
LREAGR|n|RE FARMLAND LN - LOSS SH RATIO
LRECONS|n|RE CONSTRUCT LN - LOSS SHARE
LRECONSR|n|RE CONSTRUCT LN - LOSS SHARE RATIO
LREMULT|n|RE MULTIFAMILY LN-LOSS SH
LREMULTR|n|RE MULTIFAMILY LN-LOSS SH RATIO
LRENRES|n|RE NONFARM NONRES LN - LOSS SH
LRENRESR|n|RE NONFARM NONRES LN - LOSS SH RATIO
LRERES|n|RE 1-4 FAMILY LNS - LOSS SHARE
LRERESR|n|RE 1-4 FAMILY LNS - LOSS SHARE RATIO
LSALNLS|n|CARRY AMT LOSS SHARE-LNLS
LSALNLSR|n|CARRY AMT LOSS SHARE-LNLS RATIO
LSAOA|n|CARRY AMT LOSS SHARE -OTH ASSET
LSAOAR|n|CARRY AMT LOSS SHARE -OTH ASSET RATIO
LSAORE|n|CARRY AMT LOSS SHARE- ORE
LSAORER|n|CARRY AMT LOSS SHARE- ORE RATIO
LSASCDBT|n|CARRY AMT LOSS SHARE -DEBT SEC
LSASCDBTR|n|CARRY AMT LOSS SHARE -DEBT SEC RATIO
LSFOR|n|LEASES-FOR
LSFORR|n|LEASES-FOR RATIO
MSA|n|FIPS MSA CODE
MSA_NAME|s|FIPS MSA NAME
MSRECE|n|OUT PRIN BAL MORT W/ RECOURSE
MSRECER|n|OUT PRIN BAL MORT W/ RECOURSE RATIO
MSRESFCL|n|1-4 FM SERVICED IN FORECLOSURE
MSRESFCLR|n|1-4 FM SERVICED IN FORECLOSURE RATIO
MSRNRECE|n|OUT PRIN BAL MORT W/ NO RECOURSE
MSRNRECER|n|OUT PRIN BAL MORT W/ NO RECOURSE RATIO
NAAG|n|NONACCRUAL-AGRICULTURAL LNS
NAAGR|n|NONACCRUAL-AGRICULTURAL LNS RATIO
NAAGSM|n|NONACCRUAL-AG LNS*SMALL BKS
NAAGSMR|n|NONACCRUAL-AG LNS*SMALL BKS RATIO
NAASSET|n|NONACCRUAL-TOTAL ASSETS
NAASSETR|n|NONACCRUAL-AG LNS*SMALL BKS RATIO
NAAUTO|n|NONACCRUAL AUTO LOANS
NAAUTOR|n|NONACCRUAL AUTO LOANS RATIO
NACI|n|NONACCRUAL-C&I LOANS
NACIR|n|NONACCRUAL-C&I LOANS RATIO
NACINUS|n|NONACCRUAL-C&I*NON-U.S.
NACINUSR|n|NONACCRUAL-C&I*NON-U.S. RATIO
NACON|n|NONACCRUAL-CONSUMER LOANS
NACONR|n|NONACCRUAL-CONSUMER LOANS RATIO
NACONOTH|n|NONACCRUAL-OTHER CONSUMER
NACONOTHR|n|NONACCRUAL-OTHER CONSUMER RATIO
NACRCD|n|NONACCRUAL-CREDIT CARD PLANS
NACRCDR|n|NONACCRUAL-CREDIT CARD PLANS RATIO
NADEP|n|NONACCRUAL-DEP INST LOANS
NADEPR|n|NONACCRUAL-DEP INST LOANS RATIO
NADEPNUS|n|NONACCRUAL-DEP INST*NON U.S.
NADEPNUSR|n|NONACCRUAL-DEP INST*NON U.S. RATIO
NAFG|n|NONACCRUAL-FOREIGN GOVT
NAFGR|n|NONACCRUAL-FOREIGN GOVT RATIO
NAGTY|n|NONACCRUAL-GTY LN&LS
NAGTYR|n|NONACCRUAL -GTY LN&LS
NAGTYGNM|n|NONACCRUAL REBOOKED GNMA LOANS
NAGTYGNMR|n|NONACCRUAL REBOOKED GNMA LNS
NAGTYPAR|n|NONACCRUAL-PART GTY LN&LS
NAGTYPARR|n|NONACCRUAL-PART GTY LN&LS RATIO
NALAG|n|NONACCRUAL AG LOANS-LOSS SH
NALAGR|n|NONACCRUAL AG LOANS-LOSS SH RATIO
NALCI|n|NONACCRUAL C&I LNS-LOSS SH
NALCIR|n|NONACCRUAL C&I LNS-LOSS SH RATIO
NALCON|n|NONACCRUAL CONSUMER LN -LOSS SH
NALCONR|n|NONACCRUAL CONSUMER LN -LOSS SH RATIO
NALGTY|n|NONACCR PROTECT (GTY)-LOSS SH
NALGTYR|n|NONACCRUAL PROTECT (GTY)-LOSS SH RATIO
NALNSALE|n|NONACCRUAL-L&L HELD FOR SALE
NALNSALER|n|NONACCRUAL-L&L HELD FOR SALE RATIO
NALOTH|n|NONACCRUAL OTHER LNS-LOSS SH
NALOTHR|n|NONACCRUAL OTHER LNS-LOSS SH RATIO
NALREAG|n|NONACCRUAL RE FARM-LOSS SH
NALREAGR|n|NONACCRUAL RE FARM LOSS SH RATIO
NALRECON|n|NONACCRUAL CONSTR LN -LOSS SH
NALRECONR|n|NONACCRUAL CONSTR LN -LOSS SH RATIO
NALREMUL|n|NONACCRUAL MULTIFAM - LOSS SH
NALREMULR|n|NONACCRUAL MULTIFAM - LOSS SH RATIO
NALRENRS|n|NONACCRUAL NFNR LN - LOSS SH
NALRENRSR|n|NONACCRUAL NFNR LN - LOSS SH RATIO
NALRERES|n|NONACCRUAL 1-4 FM LN-LOSS SH
NALRERESR|n|NONACCRUAL 1-4 FM LN-LOSS SH RATIO
NALS|n|NONACCRUAL-LEASES
NALSR|n|NONACCRUAL-LEASES RATIO
NALTOT|n|NONACCRUAL TOTAL LOANS - LOSS SH
NALTOTR|n|NONACCRUAL TOTAL LOANS - LOSS SH RATIO
NAME|s|INSTITUTION NAME
NAMEFULL|s|INSTITUTION FULL NAME
NAOTHLN|n|NONACCRUAL-ALL OTHER LOANS
NAOTHLNR|n|NONACCRUAL-ALL OTHER LOANS RATIO
NARE|n|NONACCRUAL-REAL ESTATE LOANS
NARER|n|NONACCRUAL-REAL ESTATE LOANS RATIO
NAREAG|n|NONACCRUAL-RE*FARMLAND
NAREAGR|n|NONACCRUAL-RE*FARMLAND RATIO
NARECNFM|n|NONACCRUAL 1-4 FAM CONSTRUCT LN
NARECNFMR|n|NONACCRUAL 1-4 FAM CONSTRUCT LN RATIO
NARECNOT|n|NONACCRUAL OTHER CONSTR & LAND
NARECNOTR|n|NONACCRUAL OTHER CONSTR & LAND RATIO
NARECONS|n|NONACCRUAL-RE*CONSTRUCTION
NARECONSR|n|NONACCRUAL-RE*CONSTRUCTION RATIO
NAREFOR|n|NONACCRUAL-RE*FOREIGN
NAREFORR|n|NONACCRUAL-RE*FOREIGN RATIO
NARELOC|n|NONACCRUAL-RE*1-4 FAM LINES
NARELOCR|n|NONACCRUAL-RE*1-4 FAM LINES RATIO
NAREMULT|n|NONACCRUAL-RE*MULTIFAMILY
NAREMULTR|n|NONACCRUAL-RE*MULTIFAMILY RATIO
NARENRES|n|NONACCRUAL-RE*NONFARM NONRES
NARENRESR|n|NONACCRUAL-RE*NONFARM NONRES RATIO
NARENROT|n|NONACCRUAL OTHER NONFARM NONRES
NARENROTR|n|NONACCRUAL OTHER NONFARM NONRES RATIO
NARENROW|n|NONACCRUAL 0WN-OCC NONFRM NONRS
NARENROWR|n|NONACCRUAL OWN-OCC NONFRM NONRS RATIO
NARENUS|n|NONACCRUAL-RE*NON-U.S.
NARENUSR|n|NONACCRUAL-RE*NON-U.S. RATIO
NARERES|n|NONACCRUAL-RE*1-4 FAMILY
NARERESR|n|NONACCRUAL-RE*1-4 FAMILY RATIO
NARERSF2|n|NONACCRUAL-RE*1-4 JUNIOR LIEN
NARERSF2R|n|NONACCRUAL-RE*1-4 JN LIEN RATIO
NARERSFM|n|NONACCRUAL-RE*1-4 IST LIEN
NARERSFMR|n|NONACCRUAL-RE*1-4 IST LIEN RATIO
NARSCI|n|NONACCRUAL RESTRUCT C&I LN
NARSCONS|n|NONACCR RESTRUCT CONSTRUCTION
NARSLNFM|n|NONACCRUAL RESTRU LN- 1-4 FAM
NARSLNFMR|n|NONACCRUAL RESTRU LN- 1-4 FAM RATIO
NARSLNLS|n|NONACCRUAL RESTRU LN EXCL 1-4 FM
NARSLNLSR|n|NONACCRUAL RESTRU LN EXCL 1-4 FM RATIO
NARSLNLT|n|NONACCRUAL RESTRUCT LN- TOTAL
NARSLNLTR|n|NONACCRUAL RESTRUCT LN- TOTAL RATIO
NARSMULT|n|NONACCRUAL RESTRUCT MULTIFAMILY
NARSNRES|n|NONACCR RESTRUCTURED NFNR LN
NARSOTH|n|NONACCRUAL RESTRUCT ALL OTH LN
NASCDEBT|n|NONACCRUAL-DEBT SECURITIES
NASCDEBTR|n|NONACCRUAL-DEBT SECURITIES RATIO
NCAG|n|TOTAL N/C-AGRICULTURAL LNS
NCAUTO|n|N/C AUTO LOANS
NCCI|n|TOTAL N/C-C&I LOANS
NCCOMRER|n|NC COMMERCIAL RE/COMMERCIAL RE
NCCOMRE|n|NC COMMERCIAL RE/COMMERCIAL RE
NCCON|n|TOTAL N/C-CONSUMER LOANS
NCCONOTH|n|TOTAL N/C-OTHER CONSUMER
NCCRCD|n|TOTAL N/C CREDIT CARD PLANS
NCDEP|n|TOTAL N/C-DEP INST LOANS
NCFG|n|TOTAL N/C-FOREIGN GOVT
NCGTYPAR|n|TOTAL N/C-PART GTY LN&LS
NCLNLSR|n|N/C LNS & LS/GROSS LNS & LS
NCLS|n|TOTAL N/C-LEASES
NCOTHLN|n|TOTAL N/C-ALL OTHER LOANS
NCRE|n|TOTAL N/C REAL ESTATE LOANS
NCRECONR|n|N/C CONST REAL ESTATE/CONST RE
NCRECONS|n|TOTAL N/C CONST REAL ESTATE CONSTRUCTION
NCRELOC|n|TOTAL N/C-RE 1-4 FAMILY LINES
NCRELOCR|n|N/C HOME EQUITY/HOME EQUITY
NCREMULR|n|N/C MULTIFAMLY RE/MULTIFAMLY RE
NCREMULT|n|TOTAL N/C MULTIFAMLY RE
NCRENRER|n|N/C NONFARM NONRES RE/NONRES RE
NCRENRES|n|TOTAL N/C NONFARM NONRES RE
NCRER|n|N/C REAL ESTATE LNS/REAL ESTATE
NCRERESO|n|N/C 1-4 OTHER RE/1-4 OTHER RE
NCREREOR|n|N/C 1-4 OTHER RE/1-4 OTHER RE
NCRERES|n|N/C 1-4 FAMILY RE
NCRERESR|n|N/C 1-4 FAMILY RE/1-4 FAMILY RE
NETGNAST|n|NET G/L ON SALES OF FIX ASSETS
NETGNASTR|n|NET G/L ON SALES OF FIX ASSETS RATIO
NTGLFXAQ|n|NET G/L ON SALES OF FIX ASSETS QUARTERLY
NTGLFXAQR|n|NET G/L ON SALES OF FIX ASSETS QUARTERLY RATIO
NETGNSLN|n|NET G/L ON SALES OF LOANS
NETGNSLNR|n|NET G/L ON SALES OF LOANS RATIO
NTGLLNQ|n|NET G/L ON SALES OF LOANS QUARTERLY
NTGLLNQR|n|NET G/L ON SALES OF LOANS QUARTERLY RATIO
NETGNSRE|n|NET G/L ON OTHER RE OWNED
NETGNSRER|n|NET G/L ON OTHER RE OWNED RATIO
NTGLREQ|n|NET G/L ON OTHER RE OWNED QUARTERLY
NTGLREQR|n|NET G/L ON OTHER RE OWNED QUARTERLY RATIO
NETINCA|n|NET INCOME- BANK- ANN
NIMY|n|NET INTEREST MARGIN
NIMYQ|n|NET INTEREST MARGIN QUARTERLY
NOIJ|n|NET OPERATING INCOME-ADJ
NOIJR|n|NET OPERATING INCOME-ADJ RATIO
NOIJY|n|NET OPERATING INCOME-ADJ/ASSETS
NOIJYQ|n|NET OPERATING INCOME-ADJ/ASSETS QUARTERLY
NOIJA|n|NET OPERATING INCOME-ADJ ANNUALLY
NOIJQ|n|NET OPERATING INCOME-ADJ QUARTERLY
NOIJQA|n|NET OPERATING INCOME-ADJ QUARTERLY
NOIJQR|n|NET OPERATING INCOME-ADJ QUARTERLY RATIO
NONIIAY|n|NONINTEREST INC/AVERAGE ASSETS
NONIIAYQ|n|NONINTEREST INC/AVERAGE ASSETS QUARTERLY
NONIIA|n|TOTAL NONINTEREST INCOME ANNUALLY
NONIIQ|n|TOTAL NONINTEREST INCOME-QTR
NONIIQA|n|TOTAL NONINTEREST INCOME-QTR
NONIIQR|n|TOTAL NONINTEREST INCOME-QTR RATIO
NONIXAY|n|NONINTEREST EXP/AVERAGE ASSETS
NONIXAYQ|n|NONINTEREST EXP/AVERAGE ASSETS QUARTERLY
NONIXA|n|TOTAL NONINTEREST EXPENSE ANNUALLY
NPERF|n|NONPERF ASSETS/TOTAL ASSETS
NPERFV|n|NONPERF ASSETS/TOTAL ASSETS
NTAG|n|AGRICULTURAL LN NET CHARGE-OFFS
NTAGR|n|AGRICULTURAL LN NET CHARGE-OFFS RATIO
NTAGA|n|AGRICULTURAL LN NET-CHG-ANN
NTAGQ|n|AG LOAN NET CHARGE-OFFS-QTR
NTAGQR|n|AG LOAN NET CHARGE-OFFS-QTR RATIO
NTAGSM|n|AG LN NET CHARGE-OFFS*SMALL BKS
NTAGSMR|n|AG LN NET CHARGE-OFFS*SMALL BKS RATIO
NTAGSMQ|n|AG LN NET CHARGE-OFFS*SMALL BKS QUARTERLY
NTAGSMQR|n|AG LN NET CHARGE-OFFS*SMALL BKS QUARTERLY RATIO
NTAUTO|n|AUTO LOANS - NET CHARGE-OFFS
NTAUTOR|n|AUTO LOANS - NET CHARGE-OFFS RATIO
NTAUTOA|n|AUTO LNS - NET CHG-OFFS - ANN
NTAUTOQ|n|AUTO LNS - NET CHG-OFFS - QTR
NTAUTOLNQR|n|AUTO LNS - NET CHG-OFFS - QTR RATIO
NTAUTOQR|n|AUTO LN-CHG-OFF- QTR/AUTO LN
NTCI|n|COMMERCIAL LOAN NET CHARGE-OFFS
NTCIR|n|COMMERCIAL LOAN NET CHARGE-OFFS RATIO
NTCIA|n|COMMERCIAL LOAN NET-CHG-ANN
NTCINUS|n|NON-U.S.COMMERCIAL LN NET CHG-OF
NTCINUSR|n|NON-U.S.COMMERCIAL LN NET CHG-OF RATIO
NTCINUSQ|n|NON-U.S.COMMERCIAL LN NET CHG-OF QUARTERLY
NTCINUSQR|n|NON-U.S.COMMERCIAL LN NET CHG-OF QUARTERLY RATIO
NTCIQ|n|COMMERCIAL LOAN NET-CHG-QTR
NTCIQR|n|COMMERCIAL LOAN NET-CHG-QTR RATIO
NTCOMRER|n|COMMERCIAL RE CHG-OFF/COMM RE LN
NTCOMREQ|n|COMMERCIAL RE CHG-OFF/COMM RE LN QUARTERLY
NTCOMREA|n|COMMERCIAL RE LN CHG-ANN
NTCON|n|CONSUMER LOAN NET CHARGE-OFFS
NTCONR|n|CONSUMER LOAN NET CHARGE-OFFS RATIO
NTCONA|n|CONSUMER LOAN NET-CHG-ANN
NTCONOTA|n|OTHER CONSUMER LOAN NET-CHG-ANN
NTCONOTH|n|OTHER CONSUMER LN NET CHARGE-OFF
NTCONOTHR|n|OTHER CONSUMER LN NET CHARGE-OFF RATIO
NTCONOTQ|n|OTHER CONSUMER LN NET-CHG-QTR
NTCONOTQR|n|OTHER CONSUMER LN NET-CHG-QTR RATIO
NTCONQ|n|CONSUMER LOAN NET-CHG-QTR
NTCONQR|n|CONSUMER LOAN NET-CHG-QTR RATIO
NTCONTQR|n|OTH.CONSUMER CHGOFF-QTR/OTH.CONS
NTCRCD|n|CREDIT CARD LOAN NET CHARGE-OFFS
NTCRCDR|n|CREDIT CARD LOAN NET CHARGE-OFFS RATIO
NTCRCDA|n|CREDIT CARD LOAN NET-CHG-ANN
NTCRCDQ|n|CREDIT CARD LN NET-CHG-QTR
NTCRCDQR|n|CREDIT CARD LN NET-CHG-QTR RATIO
NTDEP|n|DEPOSITORY INST LOAN NET CHG-OFF
NTDEPR|n|DEPOSITORY INST LOAN NET CHG-OFF RATIO
NTDEPNUS|n|FOREIGN DEP INST LN NET CHG-OFFS
NTDEPNUSR|n|FOREIGN DEP INST LN NET CHG-OFFS RATIO
NTDEPNUQ|n|FOREIGN DEP INST LN NET CHG-OFFS QUARTERLY
NTDEPNUQR|n|FOREIGN DEP INST LN NET CHG-OFFS QUARTERLY RATIO
NTDEPQ|n|DEPOSITORY INST LOAN NET-CHG-QTR
NTDEPQR|n|DEPOSITORY INST LOAN NET-CHG-QTR RATIO
NTFORGV|n|FOREIGN GOVT LN NET CHG-OFFS
NTFORGVR|n|FOREIGN GOVT LN NET CHG-OFFS RATIO
NTFORGVQ|n|FOREIGN GOV LN NET-CHG-QTR
NTFORGVQR|n|FOREIGN GOV LN NET-CHG-QTR RATIO
NTINCHPP|n|NET INCOME-BK-HIGHER-PP
NTINCL|n|NET INCOME-BANK- LOSERS
NTINCLQ|n|NET INCOME-BK-LOSER-QTR
NTLNLSA|n|TOTAL LN&LS NET-CHG-ANN
NTINQHPP|n|
NTLNLSR|n|NET CHARGE-OFFS/LOANS & LEASES
NTLNLSQR|n|NET CHARGE-OFFS/LOANS & LEASES QUARTERLY
NTLS|n|LEASE NET CHARGE-OFFS
NTLSR|n|LEASE NET CHARGE-OFFS RATIO
NTLSQ|n|LEASE NET CHARGE-OFFS-QTR
NTLSQR|n|LEASE NET CHARGE-OFFS-QTR RATIO
NTOTHER|n|ALL OTHER LOAN NET CHARGE-OFFS
NTOTHERR|n|ALL OTHER LOAN NET CHARGE-OFFS RATIO
NTOTHQ|n|ALL OTHER LN NET-CHG-QTR
NTOTHQR|n|ALL OTHER LN NET-CHG-QTRS RATIO
NTRCDSM|n|AMT TIME DEP OF $100,000 OR LESS
NTRCDSMR|n|AMT TIME DEP OF $100,000 OR LESS RATIO
NTRCOMOT|n|NONTRANSACTN-COM BKS & OTH U.S.
NTRCOMOTR|n|NONTRANSACTN-COM BKS & OTH U.S RATIO
NTRE|n|REAL ESTATE LOAN NET CHARGE-OFFS
NTREMUQA|n|
NTRECOQA|n|
NTRELNR|n|REAL ESTATE LOAN NET CHARGE-OFFS RATIO
NTREQ|n|REAL ESTATE LOAN NET CHARGE-OFFS QUARTERLY
NTREQA|n|REAL ESTATE LOAN NET CHARGE-OFFS QUARTERLY
NTRERQ|n|REAL ESTATE LOAN NET CHARGE-OFFS QUARTERLY RATIO
NTREAG|n|FARMLAND RE LN NET CHARGE-OFFS
NTREAGR|n|FARMLAND RE LN NET CHARGE-OFFS RATIO
NTREAGQ|n|FARMLAND RE LN NET-CHG-QTR
NTREA|n|RE LN NET-CHG-ANN
NTREAGQR|n|FARMLAND RE LN NET-CHG-QTR RATIO
NTRECNFM|n|1-4 FAM CONST LN NET-OFF
NTRECNOT|n|OTHER CONSTRUCT NET CHG-OFF
NTRECONQ|n|CONSTRUCTION RE LN NET-CHG-QTR
NTRECONQR|n|CONSTRUCTION RE LN NET-CHG-QTR RATIO
NTRECONS|n|CONSTRUCTION RE LN NET CHG-OFFS
NTRECOSA|n|CONST RE LOANS NET-CHG-ANN
NTRECONSR|n|CONSTRUCTION RE LN NET CHG-OFFS RATIO
NTRECOSR|n|CONST RE CHG-OFF/CONST RE LOANS
NTRECOQR|n|CONST RE CHG-OFF/CONST RE LOANS QUARTERLY
NTREFOR|n|REAL ESTATE LN NET CHG-OFF-FOR
NTREFORR|n|REAL ESTATE LN NET CHG-OFF-FOR RATIO
NTREFORQ|n|REAL ESTATE LN NET CHG-OFF-FOR QUARTERLY
NTREFORQR|n|REAL ESTATE LN NET CHG-OFF-FOR QUARTERLY RATIO
NTRELOC|n|LINE OF CREDIT RE LN NET CHG-OFF
NTRELOCLNR|n|LINE OF CREDIT RE LN NET CHG-OFF RATIO
NTRELOCQ|n|LINE OF CREDIT RE LN NET CHG-OFF QUARTERLY
NTRELOCA|n|LINE OF CREDIT RE LN NET CHG-OFF ANNUALLY
NTRELOCQR|n|LINE OF CREDIT RE LN NET CHG-OFF QUARTERLY RATIO
NTRELOCRQ|n|HOME EQUITY CHG-OFF/HOME EQ LNS QUARTERLY RATIO
NTRELOCR|n|HOME EQUITY CHG-OFF/HOME EQ LNS
NTREMULQ|n|MULTIFAMILY RE LN NET-CHG-QTR
NTREMULA|n|MULTIFAMILY RES RE LN NET-CHG-ANN
NTREMULQR|n|MULTIFAMILY RE LN NET-CHG-QTR RATIO
NTREMULR|n|MULTIFAM RE CHG-OFF/MULTI RE LN
NTREMUQR|n|MULTIFAM RE CHG-OFF/MULTI RE LN QUARTERLY
NTREMULT|n|MULTIFAMLY RES RE LN NET CHG-OFF
NTREMULTR|n|MULTIFAMLY RES RE LN NET CHG-OFF RATIO
NTRENRES|n|NONFARM NONRES RE LN NET CHG-OFF
NTRENRESR|n|NONFARM NONRES RE LN NET CHG-OFF RATIO
NTRENROT|n|OTHER NONFARM NONRS NET CHG-OFF
NTRENROW|n|OWN OCC NONFRM NONRS NET CHG-OFF
NTRENRSA|n|NONFARM NONRES RE LN NET-CHG-ANN
NTRENRSQ|n|NONFARM NONRES RE LN NET-CHG-QTR
NTRENRSQR|n|NONFARM NONRES RE LN NET-CHG-QTR RATIO
NTRENRSR|n|NONRES CHG-OFF/NONRES LOANS
NTRENRQR|n|NONRES CHG-OFF/NONRES LOANS QUARTERLY
NTRENUS|n|NON-U.S. RE LN NET CHARGE-OFFS
NTRENUSR|n|NON-U.S. RE LN NET CHARGE-OFFS RATIO
NTRENUSQ|n|NON-U.S. RE LN NET CHARGE-OFFS QUARTERLY
NTREOTHA|n|OTHER 1-4 FAM RE OTHER LN NET-CHG-ANN
NTRENUSQR|n|NON-U.S. RE LN NET CHARGE-OFFS QUARTERLY RATIO
NTREOTHR|n|OTHER 1-4 FAM RE CHG-OFF/OTH 1-4
NTREOTHRQR|n|OTHER 1-4 FAM RE CHG-OFF/OTH 1-4 QUARTERLY RATIO
NTREOTQA|n|OTHER 1-4 FAM RE CHG-OFF/OTH 1-4 QUARTERLY
NTRER|n|RE CHARGE-OFF/RE LOANS
NTREQR|n|RE CHARGE-OFF/RE LOANS QUARTERLY
NTRERES|n|RE LOANS 1-4 FAMILY NET CHG-OFFS
NTRERESLNR|n|RE LOANS 1-4 FAMILY NET CHG-OFFS RATIO
NTRERESQ|n|RE LOANS 1-4 FAMILY NET-CHG-QTR
NTRERESA|n|RE LOANS 1-4 FAMILY NET-CHG-ANN
NTRERESQR|n|RE LOANS 1-4 FAMILY NET-CHG-QTR RATIO
NTRERESR|n|1-4 FAM RE CHG-OFF/1-4 FAM LOANS
NTRERESRQ|n|1-4 FAM RE CHG-OFF/1-4 FAM LOANS QUARTERLY RATIO
NTRERSF2|n|RE LN 1-4 FAM JR LIEN-NET C/OFF
NTRERSF2R|n|RE LN 1-4 FAM JR LIEN-NET C/OFF RATIO
NTRERS2Q|n|RE LN 1-4 FAM JR LIEN-NET C/OFF QUARTERLY
NTRERS2QR|n|RE LN 1-4 FAM JR LIEN-NET C/OFF QUARTERLY RATIO
NTRERSFM|n|RE LN 1-4FAM IST LIEN-NET C/OFF
NTRERSFMR|n|RE LN 1-4FAM IST LIEN-NET C/OFF RATIO
NTRERSFQ|n|RE LN 1-4FAM IST LIEN-NET C/OFF QUARTERLY
NTRERSFQR|n|RE LN 1-4FAM IST LIEN-NET C/OFF QUARTERLY RATIO
NTREOFFDOM|n|REAL ESTATE LOAN NET CHARGE-OFFS DOMESTIC OFFICES
NTREOFFDOMR|n|REAL ESTATE LOAN NET CHARGE-OFFS DOMESTIC OFFICES RATIO
NTREOFFDOMQ|n|REAL ESTATE LOAN NET CHARGE-OFFS DOMESTIC OFFICES QUARTERLY
NTREOFFDOMQR|n|REAL ESTATE LOAN NET CHARGE-OFFS DOMESTIC OFFICES QUARTERLY 
NTRFC|n|NONTRANSACTION-FOR COUNTRY
NTRFCFG|n|NONTRANSACTION-FOR CNTRY & GOVT
NTRFCFGR|n|NONTRANSACTION-FOR CNTRY & GOVT RATIO
NTRFG|n|NONTRANSACTION-FOR GOVERNMENT
NTRSMMDA|n|SAVINGS DEP-MMDA
NTRSMMDAR|n|SAVINGS DEP-MMDA RATIO
NTRSOTH|n|SAVINGS DEP-OTHER
NTRSOTHR|n|SAVINGS DEP-OTHER RATIO
OAIENC|n|INCOME EARNED NOT COLLECTED
OALIFGEN|n|LIFE INS ASSETS - GENERAL ACC
OALIFGENR|n|LIFE INS ASSETS - GENERAL ACC RATIO
OALIFHYB|n|LIFE INS ASSETS - HYBRID ACC
OALIFHYBR|n|LIFE INS ASSETS - HYBRID ACC RATIO
OALIFINS|n|LIFE INSURANCE ASSETS
OALIFINSR|n|LIFE INSURANCE RATIO
OALIFSEP|n|LIFE INS ASSETS - SEPARATE ACC
OALIFSEPR|n|LIFE INS ASSETS - SEPARATE ACC RATIO
OBSDIR|n|OFF-BALANCE SHEET DERIVATIVES
OREAG|n|ALL OTHER RE OWNED-FARMLAND
OREAGR|n|ALL OTHER RE OWNED-FARMLAND RATIO
ORECONS|n|ALL OTHER RE OWNED-CONST
ORECONSR|n|ALL OTHER RE OWNED-CONST RATIO
OREGNMA|n|ALL OTHER RE OWNED-GNMA LOANS
OREINV|n|DIRECT & INDIRECT INVEST IN ORE
OREINVR|n|DIRECT & INDIRECT INVEST IN ORE RATIO
OREMULT|n|ALL OTHER RE OWNED-MULTI
OREMULTR|n|ALL OTHER RE OWNED-MULTI RATIO
ORENRES|n|ALL OTHER RE OWNED-NONFARM
ORENRESR|n|ALL OTHER RE OWNED-NONFARM RATIO
OREOTH|n|OTHER REAL ESTATE OWNED
OREOTHR|n|OTHER REAL ESTATE OWNED RATIO
OREOTHF|n|OTHER REAL ESTATE OWNED-FOR
OREOTHFR|n|OTHER REAL ESTATE OWNED-FOR RATIO
ORERES|n|ALL OTHER RE OWNED-1-4 FAMILY
ORERESR|n|ALL OTHER RE OWNED 1-4 FAMILIY RATIO
OTHBORF|n|OTHER BORROWED MONEY-FOR
OTHFFC|n|OTHER-FUTURES & FORWARD CONTRACT
OTHFFCR|n|OTHER-FUTURES & FORWARD CONTRACT RATIO
OTHNVS|n|OTHER-NOTIONAL VALUE SWAPS
OTHOFFBS|n|ALL OTH OFF-BALANCE SHEET LIAB
OTHOFFBSR|n|ALL OTH OFF-BALANCE SHEET LIAB RATIO
OTHPOC|n|OTHER-PURCHASED OPTION CONTRACTS
OTHWOC|n|OTHER-WRITTEN OPTION CONTRACTS
OWNCRCI|n|REC OWN INTEREST SEC - CI
OWNCRCRD|n|REC OWN INTEREST SEC - CRCD
OWNCRHEL|n|REC OWN INTEREST SEC - HEL
OWNDRCI|n|C/O OWN INTEREST SEC - CI
OWNDRCRD|n|C/O OWN INTEREST SEC - CRCD
OWNDRHEL|n|C/O OWN INTEREST SEC - HEL
OWNLNCI|n|LN SECURE HELD IN SEC - CI
OWNLNCRD|n|LN SECURE HELD IN SEC - CRCD
OWNLNHEL|n|LN SECURE HELD IN SEC - HEL
OWNP3CI|n|PD 30-89 OWN INTEREST SEC - CI
OWNP3CRD|n|PD 30-89 OWN INTEREST SEC - CRCD
OWNP3HEL|n|PD30-89 OWN INTEREST SEC - HEL
OWNP9CI|n|PD 90 + OWN INTEREST SEC - CI
OWNP9CRD|n|PD 90 + OWN INTEREST SEC - CRCD
OWNP9HEL|n|PD 90 + OWN INTEREST SEC - HEL
OWNSCCI|n|SEC. SECURE HELD IN RC-B - CI
OWNSCCRD|n|SEC. SECURE HELD IN RC-B - CRCD
OWNSCHEL|n|SEC. SECURE HELD IN RC-B - HEL
P3AG|n|30-89 DAYS P/D-AGRICULTURAL LNS
P3AGR|n|30-89 DAYS P/D-AGRICULTURAL LNS RATIO
P3AGSM|n|30-89 DAYS P/D-AG LNS*SMALL BKS
P3AGSMR|n|30-89 DAYS P/D-AG LNS*SMALL BKS RATIO
P3ASSET|n|30-89 DAYS P/D-TOTAL ASSETS
P3ASSETR|n|30-89 DAYS P/D TOTAL ASSETS RATIO
P3AUTO|n|30-89 DAYS P/D AUTO LOANS
P3AUTOR|n|30-89 DAYS P/D AUTO LOANS RATIO
P3CI|n|30-89 DAYS P/D-C&I LOANS
P3CIR|n|30-89 DAYS P/D-C&I LOANS RATIO
P3CINUS|n|30-89 DAYS P/D-C&I*NON-U.S.
P3CINUSR|n|30-89 DAYS P/D-C&I*NON-U.S. RATIO
P3CON|n|30-89 DAYS P/D-CONSUMER LOANS
P3CONR|n|30-89 DAYS P/D-CONSUMER LOANS RATIO
P3CONOTH|n|30-89 DAYS P/D-OTHER CONSUMER
P3CONOTHR|n|30-89 DAYS P/D-OTHER CONSUMER RATIO
P3CRCD|n|30-89 DAYS P/D-CREDIT CARD PLANS
P3CRCDR|n|30-89 DAYS P/D-CREDIT CARD PLANS RATIO
P3DEP|n|30-89 DAYS P/D-DEP INST LOANS
P3DEPR|n|30-89 DAYS P/D-DEP INST LOANS
P3DEPNUS|n|30-89 DAYS P/D-DEP INST*NON U.S.
P3DEPNUSR|n|30-89 DAYS P/D-DEP INST*NON U.S.
P3FG|n|30-89 DAYS P/D-FOREIGN GOVT
P3FGR|n|30-89 DAYS P/D-FOREIGN GOVT RATIO
P3GTY|n|30-89 DAYS P/D-GTY LN&LS
P3GTYR|n|30-89 DAYS P/D-GTY LN&LS
P3GTYGNM|n|30-89 DAY P/D-REBOOKED GNMA LNS
P3GTYGNMR|n|30-89 DAY P/D-REBOOKED GNMA LNS
P3GTYPAR|n|30-89 DAYS P/D-PART GTY LN&LS
P3GTYPARR|n|30-89 DAYS P/D-PART GTY LN&LS RATIO
P3LAG|n|30-89 DAY P/D AG LOANS-LOSS SH
P3LAGR|n|30-89 DAY P/D AG LOANS-LOSS SH RATIO
P3LCI|n|30-89 DAYS P/D C&I LNS-LOSS SH
P3LCIR|n|30-89 DAYS P/D C&I LNS-LOSS SH RATIO
P3LCON|n|30-89 D P/D CONSUMER -LOSS SH
P3LCONR|n|30-89 D P/D CONSUMER -LOSS SH RATIO
P3LGTY|n|30-89 P/D PROTECT (GTY)-LOSS SH
P3LGTYR|n|30-89 P/D PROTECT (GTY)-LOSS SH RATIO
P3LNSALE|n|30-89 DAYS P/D-L&L HELD FOR SALE
P3LNSALER|n|30-89 DAYS P/D-L&L HELD FOR SALE RATIO
P3LOTH|n|30-89 DAYS P/D OTH LNS-LOSS SH
P3LOTHR|n|30-89 DAYS P/D OTH LNS-LOSS SH RATIO
P3LREAG|n|30-89 DAY P/D RE FARM-LOSS SH
P3LREAGR|n|30-89 DAY P/D RE FARM-LOSS SH RATIO
P3LRECON|n|30-89 P/D CONSTRUCTION -LOSS SH
P3LRECONR|n|30-89 P/D CONSTRUCTION -LOSS SH RATIO
P3LREMUL|n|30-89 DAY P/D MULTIFAM -LOSS SH
P3LREMULR|n|30-89 DAY P/D MULTIFAM -LOSS SH RATIO
P3LRENRS|n|30-89 P/D NONFRM NONRS -LOSS SH
P3LRENRSR|n|30-89 P/D NONFRM NONRS -LOSS SH RATIO
P3LRERES|n|30-89 D P/D 1-4 FAMILY -LOSS SH
P3LRERESR|n|30-89 P/D 1-4 FAMILY -LOSS SH RATIO
P3LS|n|30-89 DAYS P/D-LEASES
P3LSR|n|30-89 DAYS P/D-LEASES RATIO
P3LTOT|n|30-89 D P/D TOTAL LOANS-LOSS SH
P3LTOTR|n|30-89 DAYS P/D-TOTAL LOANS-LOSS SH RATIO
P3OTHLN|n|30-89 DAYS P/D-ALL OTHER LOANS
P3OTHLNR|n|30-89 DAYS P/D-ALL OTHER LOANS RATIO
P3RE|n|30-89 DAYS P/D-REAL ESTATE LOANS
P3RER|n|30-89 DAYS P/D-REAL ESTATE LOANS RATIO
P3REAG|n|30-89 DAYS P/D-RE*FARMLAND
P3REAGR|n|30-89 DAYS P/D-RE*FARMLAND
P3RECNFM|n|30-89 DAYS P/D 1-4 FAM CONSTR LN
P3RECNFMR|n|30-89 DAYS P/D 1-4 FAM CONSTR LN
P3RECNOT|n|30-89 DAYS P/D OTH CONSTR & LAND
P3RECNOTR|n|30-89 DAYS P/D OTH CONSTR & LAND
P3RECONS|n|30-89 DAYS P/D-RE*CONSTRUCTION
P3RECONSR|n|30-89 DAYS P/D-RE*CONSTRUCTION
P3REFOR|n|30-89 DAYS P/D-RE*FOREIGN
P3REFORR|n|30-89 DAYS P/D-RE*FOREIGN RATIO
P3RELOC|n|30-89 DAYS P/D-RE*1-4 FAM LINES
P3RELOCR|n|30-89 DAYS P/D-RE*1-4 FAM LINES RATIO
P3REMULT|n|30-89 DAYS P/D-RE*MULTIFAMILY
P3REMULTR|n|30-89 DAYS P/D-RE*MULTIFAMILY
P3RENRES|n|30-89 DAYS P/D-RE*NONFARM NONRES
P3RENRESR|n|30-89 DAYS P/D-RE*NONFARM NONRES
P3RENROT|n|30-89 DAYS P/D OTH NONFRM NONRES
P3RENROTR|n|30-89 DAYS P/D OTH NONFRM NONRES
P3RENROW|n|30-89 DAYS P/D 0WN-OCC NONF NONRS
P3RENROWR|n|30-89 DAYS P/D OWN-OCC NONF NONRS RATIO
P3RENUS|n|30-89 DAYS P/D-RE*NON-U.S.
P3RENUSR|n|30-89 DAYS P/D-RE*NON-U.S.
P3RERES|n|30-89 DAYS P/D-RE*1-4 FAMILY
P3RERESR|n|30-89 DAYS P/D-RE*1-4 FAMILY
P3RERSF2|n|30-89 DAYS P/D-RE*1-4 JN LIEN
P3RERSF2R|n|30-89 DAYS P/D-RE*1-4 JN LIEN RATIO
P3RERSFM|n|30-89 DAYS P/D-RE*1-4 IST LIEN
P3RERSFMR|n|30-89 DAYS P/D-RE*1-4 IST LIEN RATIO
P3RSCI|n|30-89 DAY P/D RESTRUCT C&I LN
P3RSCONS|n|30-89 P/D RESTRUCT CONSTRUCTION
P3RSLNFM|n|30-89 DAY P/D RESTR LN- 1-4 FAM
P3RSLNFMR|n|30-89 DAY P/D RESTR LN- 1-4 FAM RATIO
P3RSLNLS|n|30-89 D P/D RESTR LN EXCL1-4 FM
P3RSLNLSR|n|30-89 D P/D RESTR LN EXCL1-4 FM RATIO
P3RSLNLT|n|30-89 DAY P/D RESTR LN- TOTAL
P3RSLNLTR|n|30-89 DAY P/D RESTR LN- TOTAL RATIO
P3RSMULT|n|30-89 D P/D RESTRUCT MULTIFAM
P3RSNRES|n|30-89 DAY P/D RESTRUCT NFNR LN
P3RSOTH|n|30-89 D P/D RESTRUCT ALL OTH LN
P3SCDEBT|n|30-89 DAYS P/D-DEBT SECURITIES
P3SCDEBTR|n|30-89 DAYS P/D-DEBT SECURITIES RATIO
P9AG|n|90+ DAYS P/D-AGRICULTURAL LNS
P9AGR|n|90+ DAYS P/D-AGRICULTURAL LNS RATIO
P9AGSM|n|90+ DAYS P/D-AG LNS*SMALL BKS
P9AGSMR|n|90+ DAYS P/D-AG LNS*SMALL BKS RATIO
P9ASSET|n|90+ DAYS P/D-TOTAL ASSETS
P9ASSETR|n|90+ DAYS P/D-TOTAL ASSETS RATIO
P9AUTO|n|90+ DAYS P/D AUTO LOANS
P9AUTOR|n|90+ DAYS P/D AUTO LOANS RATIO
P9CI|n|90+ DAYS P/D-C&I LOANS
P9CIR|n|90+ DAYS P/D-C&I LOANS RATIO
P9CINUS|n|90+ DAYS P/D-C&I*NON-U.S.
P9CINUSR|n|90+ DAYS P/D-C&I*NON-U.S. RATIO
P9CON|n|90+ DAYS P/D-CONSUMER LOANS
P9CONR|n|90+ DAYS P/D-CONSUMER LOANS RATIO
P9CONOTH|n|90+ DAYS P/D-OTHER CONSUMER
P9CONOTHR|n|90+ DAYS P/D-OTHER CONSUMER RATIO
P9CRCD|n|90+ DAYS P/D-CREDIT CARD PLANS
P9CRCDR|n|90+ DAYS P/D-CREDIT CARD PLANS RATIO
P9DEP|n|90+ DAYS P/D-DEP INST LOANS
P9DEPR|n|90+ DAYS P/D-DEP INST LOANS RATIO
P9DEPNUS|n|90+ DAYS P/D-DEP INST*NON U.S.
P9DEPNUSR|n|90+ DAYS P/D-DEP INST*NON U.S. RATIO
P9FG|n|90+ DAYS P/D-FOREIGN GOVT
P9FGR|n|90+ DAYS P/D-FOREIGN GOVT RATIO
P9GTY|n|90+ DAYS P/D-GTY LN&LS
P9GTYR|n|90+ DAYS P/D-GTY LN&LS
P9GTYGNM|n|90+ DAYS P/D-REBOOKED GNMA LNS
P9GTYGNMR|n|90+ DAY P/D-REBOOKED GNMA LNS
P9GTYPAR|n|90+ DAYS P/D-PART GTY LN&LS
P9GTYPARR|n|90+ DAYS P/D-PART GTY LN&LS RATIO
P9LAG|n|90+ DAYS P/D AG LOANS-LOSS SH
P9LAGR|n|90+ DAYS P/D AG LOANS-LOSS SH RATIO
P9LCI|n|90+DAYS P/D C&I LNS-LOSS SH
P9LCIR|n|90+ DAYS P/D C&I LNS-LOSS SH RATIO
P9LCON|n|90+ D P/D CONSUMER LN - LOSS SH
P9LCONR|n|90+ D P/D CONSUMER LN - LOSS SH RATIO
P9LGTY|n|90+ D P/D PROTECT (GTY)-LOSS SH
P9LGTYR|n|90+ D P/D PROTECT (GTY)-LOSS SH RATIO
P9LNSALE|n|90 DAYS P/D-L&L HELD FOR SALE
P9LNSALER|n|90+ DAYS P/D-L&L HELD FOR SALE RATIO
P9LOTH|n|90+ DAYS P/D OTHER LNS-LOSS SH
P9LOTHR|n|90+ DAYS P/D OTHER LNS-LOSS SH RATIO
P9LREAG|n|90+ DAY P/D RE FARM-LOSS SH
P9LREAGR|n|90+ DAY P/D RE FARM-LOSS SH RATIO
P9LRECON|n|90+ D P/D CONSTRUCTION -LOSS SH
P9LRECONR|n|90+ D P/D CONSTRUCTION -LOSS SH RATIO
P9LREMUL|n|90+ DAY P/D MULTIFAM - LOSS SH
P9LREMULR|n|90+ DAY P/D MULTIFAM - LOSS SH RATIO
P9LRENRS|n|90+ D P/D NFNR - LOSS SHARE
P9LRENRSR|n|90+ D P/D NFNR - LOSS SH RATIO
P9LRERES|n|90+ D P/D 1-4 FAMILY - LOSS SH
P9LRERESR|n|90+ D P/D 1-4 FAMILY - LOSS SH RATIO
P9LS|n|90+ DAYS P/D-LEASES
P9LSR|n|90+ DAYS P/D-LEASES RATIO
P9LTOT|n|90+ D P/D TOTAL LOANS - LOSS SH
P9LTOTR|n|90+ D P/D TOTAL LOANS - LOSS SH RATIO
P9OTHLN|n|90+ DAYS P/D-ALL OTHER LOANS
P9OTHLNR|n|90+ DAYS P/D-ALL OTHER LOANS RATIO
P9RE|n|90+ DAYS P/D-REAL ESTATE LOANS
P9RER|n|90+ DAYS P/D-REAL ESTATE RATIO
P9REAG|n|90+ DAYS P/D-RE*FARMLAND
P9REAGR|n|90+ DAYS P/D-RE*FARMLAND
P9RECNFM|n|90+ DAYS P/D 1-4 FAM CONSTRUC LN
P9RECNFMR|n|90+ DAYS P/D 1-4 FAM CONSTRUC LN RATIO
P9RECNOT|n|90+ DAYS P/D OTHER CONSTR & LAND
P9RECNOTR|n|90+ DAYS P/D OTHER CONSTR & LAND RATIO
P9RECONS|n|90+ DAYS P/D-RE*CONSTRUCTION
P9RECONSR|n|90+ DAYS P/D-RE*CONSTRUCTION RATIO
P9REFOR|n|90 + DAYS P/D-RE*FOREIGN
P9REFORR|n|90+ DAYS P/D-RE*FOREIGN RATIO
P9RELOC|n|90+ DAYS P/D-RE*1-4 FAM LINES
P9RELOCR|n|90+ DAYS P/D-RE*1-4 FAM LINES RATIO
P9REMULT|n|90+ DAYS P/D-RE*MULTIFAMILY
P9REMULTR|n|90+ DAYS P/D-RE*MULTIFAMILY RATIO
P9RENRES|n|90+ DAYS P/D-RE*NONFARM NONRES
P9RENRESR|n|90+ DAYS P/D-RE*NONFARM NONRES RATIO
P9RENROT|n|90+ DAYS P/D OTHER NONFRM NONRES
P9RENROTR|n|90+ DAYS P/D OTHER NONFRM NONRES RATIO
P9RENROW|n|90+ DAYS P/D 0WN-OCC NONFR NONRS
P9RENROWR|n|90+ DAYS P/D OWN-OCC NONFR NONRS RATIO
P9RENUS|n|90+ DAYS P/D-RE*NON-U.S.
P9RENUSR|n|90+ DAYS P/D-RE*NON-U.S.
P9RERES|n|90+ DAYS P/D-RE*1-4 FAMILY
P9RERESR|n|90+ DAYS P/D-RE*1-4 FAMILY RATIO
P9RERSF2|n|90+ DAYS P/D-RE*1-4 JN LIEN
P9RERSF2R|n|90+ DAYS P/D-RE*1-4 JN LIEN RATIO
P9RERSFM|n|90+ DAYS P/D-RE*1-4 IST LIEN
P9RERSFMR|n|90+ DAYS P/D-RE*1-4 IST LIEN RATIO
P9RSCI|n|90+ DAY P/D RESTRUCT C&I LN
P9RSCONS|n|90+ D P/D RESTRUCT CONSTRUCTION
P9RSLNFM|n|90+ DAYS P/D RESTR LN- 1-4 FAM
P9RSLNFMR|n|90+ DAYS P/D RESTR LN- 1-4 FAM RATIO
P9RSLNLS|n|90+ DAY P/D RESTRU LN EXCL 1-4 FM
P9RSLNLSR|n|90+ DAY P/D RESTRU LN EXCL 1-4 FM RATIO
P9RSLNLT|n|90+ DAY P/D RESTR LN- TOTAL
P9RSLNLTR|n|90+ DAY P/D RESTR LN- TOTAL RATIO
P9RSMULT|n|90+ DAY P/D RESTRUCT MULTIFAM
P9RSNRES|n|90+ DAY P/D RESTRUCT NFNR LN
P9RSOTH|n|90+ D P/D RESTRUCT ALL OTH LN
P9SCDEBT|n|90+ DAYS P/D-DEBT SECURITIES
P9SCDEBTR|n|90+ DAYS P/D-DEBT SECURITIES RATIO
PARTACQU|n|PARTICIPATIONS ACQUIRED
PARTCONV|n|PARTICIPATIONS CONVEYED
PARTCONVR|n|PARTICIPATIONS CONVEYED RATIO
RB2LNRES|n|ALLOWANCE FOR L&L IN TIER 2
RB2LNRESR|n|ALLOWANCE FOR L&L IN TIER 2 RATIO
RBC|n|RBC-TOTAL-PCA
RBCT1|n|TIER 1 RBC-PCA
RBCT2|n|RBC-TIER2-PCA
RBCT2R|n|RBC-TIER2-PCA RATIO
RBCT1C|n|RC-R COMMON EQ TIER 1 CAPITAL
RBCT1CER|n|COMMON EQUITY TIER 1 RATIO
RBCT1J|n|TIER 1 RBC ADJUSTED LLR - PCA
RBCT1JR|n|TIER 1 RBC ADJUSTED LLR - PCA RATIO
RBC1AAJ|n|LEVERAGE RATIO-PCA
RBC1RWAJ|n|TIER 1 RBC RATIO-PCA
RBCRWAJ|n|TOTAL RBC RATIO-PCA
REPOPURF|n|REPURCHASE AGREEMENT-FOR
REPOSLDF|n|REVERSE REPURCHASE AGREEMENT-FOR
ROEINJR|n|RETAINED EARNINGS/AVG BK EQUITY
RSCI|n|RESTRUCTURED LN - C&I
RSCONS|n|RESTRUCTURED LN - CONSTRUCTION
RSLNLS|n|RESTRUCTURED LN EXCL 1-4 FM
RSLNLSR|n|RESTRUCTURED LN EXCL 1-4 FM RATIO
RSLNLTOT|n|RESTRUCTURED LOANS - TOTAL
RSLNLTOTR|n|RESTRUCTURED LOANS - TOTAL RATIO
RSLNREFM|n|RESTRUCTURED LOANS - 1-4 FAMILY
RSLNREFMR|n|RESTRUCTURED LOANS - 1-4 FAMILY RATIO
RSMULT|n|RESTRUCTURED LN - MULTIFAMILY
RSNRES|n|RESTRUCT LN - NONFARM NONRES
RSOTHER|n|RESTRUCTURED LN - ALL OTHER
RSSDID|n|FEDERAL RESERVE ID NUMBER
RT|n|INTEREST RATE-TOTAL CONTRACTS
RTFFC|n|INT RATE-FUTURES & FORWARD CONTR
RTNVS|n|INT RATE-SWAPS
RTPOC|n|INT RATE-PUR OPTION CONTRACTS
RTWOC|n|INT RATE-WRITTEN OPTION CONTRACT
RWAJ|n|RWA-ADJUST-PCA-T1 & CET1 RATIO
RWAJT|n|RWA-ADJUSTED-PCA-TOTAL RBC RAT
RWAJTR|n|RWA-ADJUSTED-PCA-TOTAL RBC RAT RATIO
SCABS|n|ABS-TOTAL-B/S
SCABSR|n|ABS-TOTAL-B/S RATIO
SCAF|n|SECURITIES-AF
SCAFR|n|SECURITIES-AF RATIO
SCAOT|n|U.S. AGENCY ALL OTH
SCCMMB|n|COMMERCIAL MBS - TOTAL
SCCMOG|n|OTHER COMMERCIAL MBS-GOVT
SCCMOGR|n|OTHER COMMERCIAL MBS-GOVT RATIO
SCCMOT|n|OTHER COMMERCIAL MBS
SCCMOTR|n|OTHER COMMERCIAL MBS RATIO
SCCMPT|n|COMMERCIAL MBS PASS-THROUGH
SCCMPTR|n|COMMERCIAL MBS PASS-THROUGH RATIO
SCCOL|n|U.S. AGENCY COLLATERAL MTG-RES
SCCOLR|n|U.S. AGENCY COLLATERAL MTG-RES RATIO
SCCPTG|n|COMM MBS PASS-THRU-GOVT
SCCPTGR|n|COMM MBS PASS-THRU-GOVT RATIO
SCEQFV|n|EQ SEC READILY DET FV
SCEQFVR|n|EQ SEC READILY DET FV RATIO
SCFMN|n|U.S. AGENCY ISSUED*FNMA-RES
SCFMNR|n|U.S. AGENCY ISSUED*FNMA-RES RATIO
SCGNM|n|U.S. AGENCY GTY BY GNMA
SCGNMR|n|U.S. AGENCY GTY BY GNMA RATIO
SCGTY|n|U.S. AGENCY ISSUED OR GTY-RES
SCGTYR|n|U.S. AGENCY ISSUED OR GTY-RES RATIO
SCHA|n|SECURITIES-HA
SCHAR|n|SECURITIES-HA RATIO
SCHTMRES|n|LESS ALLOW FOR CREDIT LOSSES ON HELD TO MATURITY DEBT SECURI
SCHTMRESR|n|LESS ALLOW FOR CREDIT LOSSES ON HELD TO MATURITY DEBT SECURI
SCLENT|n|SECURITIES LENT
SCLENTR|n|SECURITIES LENT RATIO
SCNM1T3|n|NONMTG DEBT SEC * 1-3 YEARS
SCNM1T3R|n|NONMTG DEBT SEC * 1-3 YEARS RATIO
SCNM3LES|n|NONMTG DEBT SEC*3 MONS OR LESS
SCNM3LESR|n|NONMTG DEBT SEC*3 MONS OR LESS RATIO
SCNM3T5|n|NONMTG DEBT SEC * 3-5 YEARS
SCNM3T5R|n|NONMTG DEBT SEC * 3-5 YEARS RATIO
SCNM3T12|n|NONMTG DEBT SEC * 3-12 MONTHS
SCNM3T12R|n|NONMTG DEBT SEC * 3-12 MONTHS RATIO
SCNM5T15|n|NONMTG DEBT SEC * 5-15 YEARS
SCNM5T15R|n|NONMTG DEBT SEC * 5-15 YEARS RATIO
SCNMOV15|n|NONMTG DEBT SEC * OVER 15 YEARS
SCNMOV15R|n|NONMTG DEBT SEC * OVER 15 YEARS RATIO
SCO3YLES|n|OTH MORTGAGE SEC * 3 YR OR LESS
SCO3YLESR|n|OTH MORTGAGE SEC * 3 YR OR LESS RATIO
SC1LES|n|Fixed and floating rate debt securities (included above) wit
SC1LESR|n|Fixed and floating rate debt securities (included above) wit
SCODOT|n|OTH DOM DEBT*ALL OTHER
SCODOTR|n|OTH DOM DEBT*ALL OTHER RATIO
SCODPI|n|CMO PRIV ISSUED
SCODPIR|n|CMO PRIV ISSUED RATIO
SCOOV3Y|n|OTH MORTGAGE SEC * OVER 3 YRS
SCOOV3YR|n|OTH MORTGAGE SEC * OVER 3 YRS RATIO
SCPLEDGE|n|PLEDGED SECURITIES
SCPLEDGER|n|PLEDGED SECURITIES RATIO
SCPT1T3|n|MTG PASS-THRU SEC * 1-3 YEARS
SCPT1T3R|n|MTG PASS-THRU SEC * 1-3 YEARS RATIO
SCPT3LES|n|MTG PASS-THRU SEC*3 MON OR LESS
SCPT3LESR|n|MTG PASS-THRU SEC*3 MON OR LESS RATIO
SCPT3T5|n|MTG PASS-THRU SEC * 3-5 YEARS
SCPT3T5R|n|MTG PASS-THRU SEC * 3-5 YEARS RATIO
SCPT3T12|n|MTG PASS-THRU SEC * 3-12 MONTHS
SCPT3T12R|n|MTG PASS-THRU SEC * 3-12 MONTHS RATIO
SCPT5T15|n|MTG PASS-THRU SEC * 5-15 YEARS
SCPT5T15R|n|MTG PASS-THRU SEC * 5-15 YEARS RATIO
SCPTOV15|n|MTG PASS-THRU SEC * OVER 15 YRS
SCPTOV15R|n|MTG PASS-THRU SEC * OVER 15 YRS RATIO
SCRDEBT|n|DEBT SECURITIES
SCRDEBTR|n|DEBT SECURITIES RATIO
SCSFP|n|STRUCTURED FIN PROD - TOTAL
SCSFPR|n|STRUCTURED FIN PROD - TOTAL RATIO
SCSNHAA|n|STRUCTURED NOTES AMORTIZED COST
SCSNHAAR|n|STRUCTURED NOTES AMORTIZED COST RATIO
SCSNHAF|n|STRUCTURED NOTES-FAIR VALUE
SCSNHAFR|n|STRUCTURED NOTES-FAIR VALUE RATIO
SCSPN|n|U.S. AGENCY GOVT SPONSORED
SZ30AUTO|n|30-89 PD LN-SECURITIZATION-AUTO
SZ30AUTOR|n|30-89 PD LN-SECURITIZATION-AUTO RATIO
SZ30CI|n|30-89 PD LN-SECURITIZATION-CI
SZ30CIR|n|30-89 PD LN-SECURITIZATION-CI RATIO
SZ30CON|n|30-89 PD LN-SECURITIZATION-CON
SZ30CONR|n|30-89 PD LN-SECURITIZATION-CON RATIO
SZ30CRCD|n|30-89 PD LN-SECURITIZATION-CRCD
SZ30CRCDR|n|30-89 PD LN-SECURITIZATION-CRCD RATIO
SZ30HEL|n|30-89 PD LN-SECURITIZATION-HEL
SZ30HELR|n|30-89 PD LN-SECURITIZATION-HEL RATIO
SZ30OTH|n|30-89 PD LN-SECURITIZATION-OTH
SZ30OTHR|n|30-89 PD LN-SECURITIZATION-OTH RATIO
SZ30RES|n|30-89 PD LN-SECURITIZATION -RES
SZ30RESR|n|30-89 PD LN-SECURITIZATION -RES RATIO
SZ90AUTO|n|90 + PD LN-SECURITIZATION-AUTO
SZ90AUTOR|n|90 + PD LN-SECURITIZATION-AUTO RATIO
SZ90CI|n|90 + PD LN-SECURITIZATION-CI
SZ90CIR|n|90 + PD LN-SECURITIZATION-CI RATIO
SZ90CON|n|90 + PD LN-SECURITIZATION-CON
SZ90CONR|n|90 + PD LN-SECURITIZATION-CON RATIO
SZ90CRCD|n|90 + PD LN-SECURITIZATION-CRCD
SZ90CRCDR|n|90 + PD LN-SECURITIZATION-CRCD RATIO
SZ90HEL|n|90+ PD LN-SECURITIZATION-HEL
SZ90HELR|n|90+ PD LN-SECURITIZATION-HEL RATIO
SZ90OTH|n|90 + PD LN-SECURITIZATION-OTH
SZ90OTHR|n|90 + PD LN-SECURITIZATION-OTH RATIO
SZ90RES|n|90 + PD LN-SECURITIZATION-RES
SZ90RESR|n|90 + PD LN-SECURITIZATION-RES RATION
SZCRAUTO|n|REC ASSET SECURITIZATION-AUTO
SZCRAUTOR|n|REC ASSET SECURITIZATION-AUTO
SZCRCDFE|n|OUTSTDG CC FEES IN SECURITZD CC
SZCRCDFER|n|OUTSTDG CC FEES IN SECURITZD CC RATIO
SZCRCI|n|REC ASSET SECURITIZATION-CI
SZCRCIR|n|REC ASSET SECURITIZATION-CI RATIO
SZCRCON|n|REC ASSET SECURITIZATION-CON
SZCRCONR|n|REC ASSET SECURITIZATION-CON RATIO
SZCRCRCD|n|REC ASSET SECURITIZATION - CRCD
SZCRCRCDR|n|REC ASSET SECURITIZATION - CRCD RATIO
SZCRHEL|n|RE PRIN SEC ASSET SOLD-HEL
SZCRHELR|n|RE PRIN SEC ASSET SOLD-HEL RATIO
SZCROTH|n|REC ASSET SECURITIZATION-
SZCROTHR|n|REC ASSET SECURITIZATION- RATIO
SZCRRES|n|REC ASSET SECURITIZATION-RES
SZCRRESR|n|REC ASSET SECURITIZATION-RES RATIO
SZDRAUTO|n|C/O ON ASSET SECURITIZATION-AUTO
SZDRAUTOR|n|C/O ON ASSET SECURITIZATION-AUTO RATIO
SZDRCI|n|C/O ON ASSET SECURITIZATION-CI
SZDRCIR|n|C/O ON ASSET SECURITIZATION-CI RATIO
SZDRCON|n|C/O ON ASSET SECURITIZATION-CON
SZDRCONR|n|C/O ON ASSET SECURITIZATION-CON RATIO
SZDRCRCD|n|C/O ON ASSET SECURITIZATION-CRCD
SZDRCRCDR|n|C/O ON ASSET SECURITIZATION-CRCD RATIO
SZDRHEL|n|C/O ON ASSET SECURITIZATION-HEL
SZDRHELR|n|C/O ON ASSET SECURITIZATION-HEL RATIO
SZDROTH|n|C/O ON ASSET SECURITIZATION-OTH
SZDROTHR|n|C/O ON ASSET SECURITIZATION-OTH RATIO
SZDRRES|n|C/O ON ASSET SECURITIZATION-RES
SZISLAUT|n|CR EXP ON SECURITIZATN - AUTO
SZISLAUTR|n|CR EXP ON SECURITIZATN - AUTO RATIO
SZISLCCD|n|CR EXP ON SECURITIZATN - CRCD
SZISLCCDR|n|CR EXP ON SECURITIZATN - CRCD RATIO
SZISLCI|n|CR EXP ON SECURITIZATN -CI
SZISLCIR|n|CR EXP ON SECURITIZATN -CI RATIO
SZISLCON|n|CR EXP ON SECURITIZATN - CON
SZISLCONR|n|CR EXP ON SECURITIZATN - CON RATIO
SZISLHEL|n|CR EXP ON SECURITIZATN - HEL
SZISLHELR|n|CR EXP ON SECURITIZATN - HEL RATIO
SZISLOTH|n|CR EXP ON SECURITIZATN -OTH
SZISLOTHR|n|CR EXP ON SECURITIZATN -OTH RATIO
SZISLRES|n|CR EXP ON SECURITIZATION RES
SZISLRESR|n|CR EXP ON SECURITIZATION RES RATIO
SZLAUTO|n|RE PRIN SEC ASSET SOLD - AUTO
SZLAUTOR|n|RE PRIN SEC ASSET SOLD - AUTO RATIO
SZLNCI|n|RE PRIN SEC ASSET SOLD - CI
SZLNCIR|n|RE PRIN SEC ASSET SOLD - CI RATIO
SZLNCON|n|RE PRIN SEC ASSET SOLD - CONS
SZLNCONR|n|RE PRIN SEC ASSET SOLD - CONS RATIO
SZLNCRCD|n|RE PRIN SEC ASSET SOLD - CRCD
SZLNCRCDR|n|RE PRIN SEC ASSET SOLD - CRCD RATIO
SZLNHEL|n|RE PRIN SEC ASSET SOLD - HEL
SZLNHELR|n|RE PRIN SEC ASSET SOLD - HEL RATIO
SZLNOTH|n|RE PRIN SEC ASSET SOLD - OTH
SZLNOTHR|n|RE PRIN SEC ASSET SOLD - OTH RATIO
SZLNRES|n|RE PRIN SEC ASSET SOLD-RES
SZLNRESR|n|RE PRIN SEC ASSET SOLD-RES RATIO
SZUCAUTO|n|COMMITS FOR LIQUIDITY - AUTO
SZUCCI|n|COMMITS FOR LIQUIDITY - CI
SZUCCON|n|COMMITS FOR LIQUIDITY - CON
SZUCCRCD|n|COMMITS FOR LIQUIDITY - CRCD
SZUCHEL|n|COMMITS FOR LIQUIDITY - HEL
SZUCOTH|n|COMMITS FOR LIQUIDITY - OTH
SZUCRES|n|COMMITS FOR LIQUIDITY - RES
TCAMA|n|CORP TRUST-MANAGED-AMT
TCAMANUM|n|CORP TRUST-MANAGED-NUM
TCANMA|n|CORP TRUST-NON-MANAGED-AMT
TCANMNUM|n|CORP TRUST-NON-MANAGED-NUM
TCANUM|n|CORP TRUST-TRUSTEESHIPS-NUM
TCANUMD|n|CORP & MUNI-TRUSTEE-DEFAULT-NUM
TCAPAO|n|CORP TRUST-TRUSTEESHIPS-AMT
TCAPAOD|n|CORP & MUNI-TRUSTEE-DEFAULT-AMT
TCATNUM|n|CORP TRUST-TRANSFER-NUM
TCDEMV|n|CIFS -DOM EQUITY-AMT
TCDENUM|n|CIFS -DOM EQUITY-NUM
TCIEMV|n|CIFS -INTL/GLOBAL-EQ-AMT
TCIENUM|n|CIFS -INTL/GLOBAL-EQ-NUM
TCMBMV|n|CIFS-MUNICIPAL BOND-AMT
TCMBNUM|n|CIFS-MUNICIPAL BOND-NUM
TCSBMV|n|CIFS -STOCK/BOND-AMT
TCSBNUM|n|CIFS -STOCK/BOND-NUM
TCSNMA|n|CUST AND SAFE ACCT-NON-MAN-AMT
TCSNMNUM|n|CUST AND SAFE ACCT-NON-MAN-NUM
TCSOMV|n|CIFS-SPECIALTY/OTHER-AMT
TCSONUM|n|CIFS-SPECIALTY/OTHER-NUM
TCSTMV|n|CIFS-SHORT TERM INV-AMT
TCSTNUM|n|CIFS-SHORT TERM INV-NUM
TCTBMV|n|CIFS - TAXABLE BOND-AMT
TCTBNUM|n|CIFS - TAXABLE BOND-NUM
TCTOTMV|n|CIFS-TOTAL-AMT
TCTOTNUM|n|CIFS-TOTAL-NUM
TEBMA|n|EMP BENE-DEF BENE-MANAGE-AMT
TEBMANUM|n|EMP BENE-DEF BENE-MANAGED-NUM
TEBNMA|n|EMP BENE-DEF BENE-NON-MAN-AMT
TEBNMNUM|n|EMP BENE-DEF BENE-NON-MAN-NUM
TECMA|n|EMP BENE-CONTRIB-MANAGED-AMT
TECMANUM|n|EMP BENE-CONTRI-MANAGED-NUM
TECNMA|n|EMP BENE-CONTRI-NON-MAN-AMT
TECNMNUM|n|EMP BENE-CONTRI-NON-MANAGE-NUM
TECPS|n|EMP BEN & RET TR - COM & PF STK
TEEQF|n|EMP BEN & RET TR - EQ MUT FUND
TEI|n|EMP BEN & RET TR - INT BEARING
TEMATOT|n|EMP BEN & RET TR-TOT MANAGE AST
TEMISC|n|EMP BEN & RET TR - MISC ASSET
TEMMF|n|EMP BEN & RET TR - MONEY MKT
TENI|n|EMP BEN & RET TR - NONINT BEAR
TEOTHB|n|EMP BEN & RET TR-OTH NOTE & BND
TEOTHF|n|EMP BEN & RET TR - OTH MUT FUND
TERE|n|EMP BEN & RET TR - REAL ESTATE
TEREMTG|n|EMP BEN & RET TR - RE MTG
TESCMUN|n|EMP BEN & RET TR - MUNI
TESCUS|n|EMP BEN & RET TR -U.S TREAS & OB
TESTO|n|EMP BEN & RET TR - SHRT TERM OB
TETOT|n|EXPENSE FIDUCIARY - YTD
TETRF|n|EMP BEN & RET TR - TRUST FUND
TEUF|n|EMP BEN & RET TR - UNREG FUNDS
TFEMA|n|FOUNDATION & ENDOW-MANAGED-AMT
TFEMANUM|n|FOUNDATION & ENDOW-MANAGED-NUM
TFENMA|n|FOUNDATION & END-NON-MANAGE-AMT
TFENMNUM|n|FOUNDATION & END-NON-MANAGE-NUM
TICA|n|GR.INC-CORP TRUST & AGENCY-YTD
TICS|n|GR.INC-CUSTODY-YTD
TIEB|n|GR.INC-EMP. BENEFIT-BENEFIT-YTD
TIEC|n|GR.INC-EMP. BENEFIT- CONTRI-YTD
TIFE|n|GR. INC- FOUNDATION & ENDOW-YTD
TIMA|n|GR.INC - INVESTMENT AGCY - YTD
TIMMA|n|INVESTMENT AGENCY-MANAGED-AMT
TIMMANUM|n|INVESTMENT AGENCY-MANAGED-NUM
TIMNMA|n|INVESTMENT AGCY NON-MANAGED-AMT
TIMNMNUM|n|INVESTMENT AGCY NON-MANAGED-NUM
TINTRA|n|INTRACOMPANY INC FIDUCIARY-YTD
TIOF|n|GR.INC-OTHER FIDUCIARY-YTD
TIOR|n|GR.INC-OTHER RETIREMENT -YTD
TIP|n|GR.INC-PERSONAL & AG ACCTS-YTD
TIR|n|GR.INC-RELATED SERV-YTD
TITOTF|n|TOT FOREIGN OFF GROSS FIDUC-YTD
TMAF|n|FIDUCIARY FGN OFF-MANAGED-AMT
TMAFNUM|n|FIDUCIARY FGN OFF-MANAGED-AMT
TMASMF|n|ADVISED/SPONSORED MUT FND -AMT
TMASMFN|n|ADVISED/SPONSORED MUTAL FND-NUM
TNI|n|NET FIDUCIARY INCOME -YTD
TNL|n|NET LOSS FROM FIDUCIARY-YTD
TNMAF|n|FIDUCIARY FGN OFF-NON-MAN-AMT
TNMNUMF|n|FIDUCIARY FGN OFF-NON-MAN-NUM
TOCPS|n|ALL OTH MAN ASSET-COM & PFD STK
TOEQF|n|ALL OTH MANAGE AST - EQ MUT FND
TOFMA|n|OTH FIDUCIARY-MANAGED-AMT
TOFMANUM|n|OTH FIDUCIARY-MANAGED-NUM
TOFNMA|n|OTH FIDUCIARY NON-MANAGED-AMT
TOFNMNUM|n|OTH FIDUCIARY-NON-MANAGED-NUM
TOI|n|ALL OTH MANAGE ASSET - INT BEAR
TOMATOT|n|ALL OTHER MANAGED ASSET- TOTAL
TOMISC|n|ALL OTH MAN ASSET - MISC ASSET
TOMMF|n|ALL OTH MANAGE AST - MONEY MKT
TONI|n|ALL OTH MAN ASSET - NONINT BEAR
TOOTHB|n|ALL OTH MAN AST -OTH NOTE & BND
TOOTHF|n|ALL OTH MAN ASSET - OTH MUT FND
TORE|n|ALL OTH MAN ASSET - REAL ESTATE
TOREMTG|n|ALL OTHER MANAGE ASSET - RE MTG
TORMA|n|OTH RETIREMENT-MANAGED-AMT
TORMANUM|n|OTH RETIREMENT-MANAGED-NUM
TORNMA|n|OTH RETIREMENT-NON-MAN-AMT
TORNMNUM|n|OTH RETIREMENT-NON-MAN-NUM
TOSCMUN|n|ALL OTHER MANAGED ASSET - MUNI
TOSCUS|n|ALL OTH MAN AST-U.S. TREAS & OB
TOSTO|n|ALL OTH MAN AST - SHRT TERM OBL
TOTRF|n|ALL OTH MAN ASSET - TRUST FUND
TOUF|n|ALL OTH MAN ASSET - UNREG FUNDS
TPICPS|n|PER TR & INV AGY- COM & PRF STK
TPIEQF|n|PER TR & INV AGY - EQ MUT FUND
TPII|n|PER TR & INV AGY - INT BEARING
TPIMATOT|n|PER TR & INV AGY-TOT MANAGE AST
TPIMISC|n|PER TR & INV AGY - MISC
TPIMMF|n|PER TR & INV AGY - MONEY MKT
TPINI|n|PER TR & INV AGY-NONINT BEARING
TPIOTHB|n|PER TR & INV AGY-OTH NOTE & BND
TPIOTHF|n|PER TR & INV AGY - OTH MUT FUND
TPIRE|n|PER TR & INV AGY - REAL ESTATE
TPIREMTG|n|PER TR & INV AGY - RE MTG
TPISCMUN|n|PER TR & INV AGY - MUNI
TPISCUS|n|PER TR & INV AGY-U.S TREAS & OB
TPISTO|n|PER TR & INV AGY - SHRT TERM OB
TPITRF|n|PER TR & INV AGY - TRUST FUND
TPIUF|n|PER TR & INV AGY- UNREG FUNDS
TPMA|n|MANAGED ASSET-PER & AGEN-AMT
TPMANUM|n|MANAGED ASSET - PER&AGEN-NUM
TPNMA|n|NON-MANAGED - PER&AGEN-AMT
TPNMNUM|n|NON-MANAGED ASSET-PER&AGEN-NUM
TREXER|n|TRUST POWERS EXERCISED
TRFOR|n|TRADING ACCOUNTS-FOR
TRHMA|n|IRA
TRHMANUM|n|IRA
TRHNMA|n|IRA
TRHNMNUM|n|IRA
TRLREVAL|n|TRADE-DERIVATIVES NEG VAL
TRLREVALR|n|TRADE-DERIVATED NEG VAL RATIO
TRNCBO|n|TRANSACTION-COM BKS& OTHER
TRNCBOR|n|TRANSACTION-COM BKS& OTHER RATIO
TRNFC|n|TRANSACTION-FOR COUNTRY
TRNFCFG|n|TRANSACTION-FOR COUNTRY & GOVT
TRNFCFGR|n|TRANSACTION-FOR COUNTRY & GOVT RATIO
TRNFG|n|TRANSACTION-FOREIGN GOVERNMENT
TRNNIA|n|AMT NON-INTEREST BEARING TRANSACTION ACC MORE THAN $250,000
TRNNIAR|n|AMT NON-INTEREST BEARING TRANSACTION ACC MORE THAN $250,000
TRNNIN|n|NUM NON-INTEREST BEARING TRANSACTION ACC MORE THAN $250,000
TRPOWER|n|INSTITUTION HAS TRUST POWER
TRREVALD|n|TRADE-DERIV POS VAL-DOM
TRREVALF|n|TRADE-DERIV POS VALUE-FOR
TRREVALSUM|n|REVALUATION GAINS ON OFF-BALANCE SHEET CONTRACTS
TRREVALSUMR|n|REVALUATION GAINS ON OFF-BALANCE SHEET CONTRACTS RATIO
TTMA|n|TOT FIDUCIARY ACCTS-MAN-AMT
TTNANUM|n|TOT FIDUCIARY ACCTS-MAN-NUM
TTNMA|n|TOT FIDUCIARY ACCTS-NON-MAN-AMT
TTNMNUM|n|TOT FIDUCIARY ACCTS-NON-MAN-NUM
UC|n|UNUSED COMMIT-TOTAL
UCR|n|UNUSED COMMIT-TOTAL RATIO
UCCOMRE|n|UNUSED COMMIT-COM RE
UCCOMRER|n|UNUSED COMMIT-COM RE RATIO
UCCOMRES|n|UNUSED COMMIT-SECURED COM RE
UCCOMRESR|n|UNUSED COMMIT-SECURED COM RE RATIO
UCCOMREU|n|UNUSED COMMIT-UNSECURED COM RE
UCCOMREUR|n|UNUSED COMMIT-UNSECURED COM RE RATIO
UCCRCD|n|UNUSED COMMIT-CREDIT CARD LINES
UCCRCDR|n|UNUSED COMMIT-CREDIT CARD LINES RATIO
UCLN|n|UNUSED COMMIT-TOTAL LOANS
UCLOC|n|UNUSED COMMIT-HOME EQUITY LINES
UCLOCR|n|UNUSED COMMIT-HOME EQUITY LINES RATIO
UCOTHER|n|UNUSED COMMIT-ALL OTHER
UCOTHERR|n|UNUSED COMMIT-ALL OTHER RATIO
UCOVER1|n|UNUSED COM-OVER 1 YR-RC-R COL A
UCOVER1R|n|UNUSED COM-OVER 1 YR-RC-R COL A RATIO
UCSC|n|UNUSED COMMIT-SEC UNDERWRITING
UCSCR|n|UNUSED COMMIT-SEC UNDERWRITING RATIO
UCSZAUTO|n|UNUSED COMMIT FOR SECUR. - AUTO
UCSZCI|n|UNUSED COMMIT FOR SECUR. - CI
UCSZCON|n|UNUSED COMMIT FOR SECUR. - CON
UCSZCRCD|n|UNUSED COMMIT FOR SECUR. - CRCD
UCSZHEL|n|UNUSED COMMIT FOR SECUR. - HEL
UCSZOTH|n|UNUSED COMMIT FOR SECUR. - OTH
UCSZRES|n|UNUSED COMMIT FOR SECUR. - RES
UNINCFOR|n|UNEARNED INCOME-FOR
UNINCFORR|n|UNEARNED INCOME-FOR RATIO
VOLIAB|n|VOLATILE LIABILITIES
VOLIABR|n|VOLATILE LIABILITIES RATIO
ZIP|n|ZIP CODE
LIPNMTG|n|NONMORTGAGE LOANS IN PROCESS
UYANMTG|n|UNAMORTIZED YIELD ADJ-NONMTG LNS
ILNLS|n|LOAN & LEASE INCOME
UNIT|n|BANKS UNIT
PTAXNETINC|n|PRE-TAX NET INCOME OPERATING INCOME
PTAXNETINCR|n|PRE-TAX NET INCOME OPERATING INCOME RATIO
PTAXNETINCQ|n|PRE-TAX NET INCOME OPERATING INCOME QUARTERLY
PTAXNETINCQR|n|PRE-TAX NET INCOME OPERATING INCOME QUARTERLY RATIO
ADDNONII|n|ADDITIONAL NONINTEREST INCOME
ADDNONIIR|n|ADDITIONAL NONINTEREST INCOME RATIO
ADDNONIIQ|n|ADDITIONAL NONINTEREST INCOME QUARTERLY
ADDNONIIQR|n|ADDITIONAL NONINTEREST INCOME QUARTERLY RATIO
AVMMLF|n|Quarterly average amount of assets purchased under the MMLF 
AVMMLFR|n|Quarterly average amount of assets purchased under the MMLF 
AVPPPPLG|n|Quarterly average amount of PPP loans pledged to the PPPLF a
AVPPPPLGR|n|Quarterly average amount of PPP loans pledged to the PPPLF a
MMLFBAL|n|Outstanding balance of assets purchased under the Money Mark
MMLFBALR|n|Outstanding balance of assets purchased under the Money Mark
PPPLFOV1|n|Outstanding balance under the PPPLF with a remaining maturit
PPPLFOV1R|n|Outstanding balance under the PPPLF with a remaining maturit
PPPLNBAL|n|Outstanding balance of PPP loans
PPPLNBALR|n|Outstanding balance of PPP loans ratio
PPPLNNUM|n|Number of PPP loans outstanding
PPPLNNUMR|n|Number of PPP loans outstanding ratio
PPPLNPLG|n|Outstanding balance of PPP loans pledged to the PPPLF
PPPLNPLGR|n|Outstanding balance of PPP loans pledged to the PPPLF ratio
PPPLF1LS|n|Outstanding balance under the PPPLF with a remaining maturit
PPPLF1LSR|n|Outstanding balance under the PPPLF with a remaining maturit
IDNTCIR|n|COMMERCIAL & INDUSTRIAL LOANS
IDNTCIQR|n|COMMERCIAL & INDUSTRIAL LOANS QUARTERLY
IDNTCONR|n|LOANS TO INDIVIDUALS
IDNTCRDR|n|CREDIT CARDS & RELATED PLANS
IDNTCOOR|n|OTHER LOANS TO INDIVIDUALS
IDNTCOOQR|n|OTHER LOANS TO INDIVIDUALS
IDNTCRDQR|n|CREDIT CARDS & RELATED PLANS QUARTERLY
INSTCNT|n|
IDNTILR|n|
IDOTHNII|n|
NTAUTOPR|n|AUTOMOBILE LOANS
NTCONOTR|n|OTHER CONSUMER LOANS
IDERNCVR|n|EARNINGS COVERAGE OF NET LOAN CHARGE-OFFS (X)
IDERNCVQR|n|Earnings coverage of net loan charge-offs
EQCDIVNTINC|n|CASH DIVIDENDS TO NET INCOME (YTD ONLY)
NACDIR|n|NOTIONAL AMOUNT OF CREDIT DERIVATIVES
NTCOMREQR|n|COMMERCIAL RE CHG-OFF/COMM RE LN QUARTERLY RATIO
NTALLOTHNUM|n|Net Charge-offs All other loans & leases (including farm) Nu
NTALLOTHDEN|n|Net Charge-offs All other loans & leases (including farm) de
NTALLOTHR|n|ALL OTHER LOANS & LEASES (INCLUDING FARM)
NTALLOTHQR|n|Net Charge-offs All other loans & leases (including farm)
IDNCCOOR|n|Other loans to individuals
IDNCOTHR|n|All other loans & leases (including farm )
IDNCCIR|n|COMMERCIAL & INDUSTRIAL LOANS RATIO
IDNCCONR|n|LOANS TO INDIVIDUALS RATIO
IDNCCRDR|n|CREDIT CARDS & RELATED PLANS RATIO
IDNCATOR|n|AUTOMOBILE LOANS RATIO
IDNTATOR|n|
IDNTCOTR|n|
IDDEPINR|n|IDDEPINR
IDDIVNIR|n|
IDNCCOTR|n|OTHER CONSUMER LOANS RATIO
INTINCY|n|INTEREST INCOME TO EARNING ASSETS RATIO
IDNCGTPR|n|NONCURRENT LOANS WHICH ARE WHOLLY OR PARTIALLY GUARANTEED BY
IDLNCORR|n|NET LOANS AND LEASES TO CORE DEPOSITS RATIO
IDT1CNOCB|n|ID NO CB FLAG
IDT1JNOCB|n|ID NO J CB FLAG
IDT1CER|n|COMMON EQUITY TIER 1 CAPITAL RATIO
IDT1RWAJR|n|TIER 1 RISK-BASED CAPITAL RATIO
SCEQNFT|n|EQUITY SECURITIES NOT HELD FOR TRADING
SCRMBPI|n|PRIV ISSUED RES MORTGAGE-BACKED SECURITIES
SCRMBPIR|n|PRIV ISSUED RES MORTGAGE-BACKED SECURITIES RATIO
SCUSO|n|U.S GOVERNMENT OBLIGATIONS
SCUSOR|n|U.S GOVERNMENT OBLIGATIONS RATIOS
SCCMOS|n|OTHER COMM MORTGAGE-BACKED SEC
SCCMOSR|n|OTHER COMM MORTGAGE-BACKED SEC
SCTATFR|n|ASSETS HELD IN TRADING ACCOUNTS FOR TFR REPORTERS
LNLSGRS|n|LOANS AND LEASES, GROSS
LNLSGRSR|n|LOANS AND LEASES, GROSS RATIO
AOA|n|ALL OTH ASSETS
AOAR|n|ALL OTH ASSETS RATIO
ESTINS|n|PERCENTAGE INSURED ESTIMATED
ESTINSR|n|PERCENTAGE INSURED ESTIMATED RATIO
P3RELNDO|n|P/D 30-89 REAL ESTATE LOANS IN DOMESTIC OFFICES
P3RELNDOR|n|P/D 30-89 REAL ESTATE LOANS IN DOMESTIC OFFICES RATIO
P9RELNDO|n|90+ REAL ESTATE LOANS IN DOMESTIC OFFICES
P9RELNDOR|n|90+ REAL ESTATE LOANS IN DOMESTIC OFFICES RATIO
NARELNDO|n|90+ REAL ESTATE LOANS IN DOMESTIC OFFICES
NARELNDOR|n|90+ REAL ESTATE LOANS IN DOMESTIC OFFICES RATIO
STCNTY|s|State and County Nunber
CBSA|s|Metropolitan Statistical Area
INSDATE|s|Date of Deposit Insurance
UPDDATE|s|Last Structure Change Process Date
ASSETR|n|Total Assets Ratio
AVASSET|n|AVG TOTAL ASSETS
BROINSLG|n|BROKERED DEP-INSURED-LARGE
CT1AJTOT|n|RC-R TOTAL ADJ & DED COM EQ T1
CT1BADJ|n|RC-R COM EQUITY T1 BEFORE ADJ
DEP2|n|TOTAL DEPOSITS-CAVG2
DEP5|n|TOTAL DEPOSITS-CAVG5
DEPIY1|n|INTEREST-BEARING-DEP-Y1
ECD100|n|INT EXPENSE TIME CD GT $250
ECD100A|n|INT EXP TIME CD GT $250
ECD100Q|n|INT EXP TIME CD GT $250
EFREPPA|n|FED FUNDS & REPO INT EXPENSE-ANN
EOTHTIMA|n|INT EXP TIME CD LE $250
EOTHTIME|n|INT EXPENSE TIME CD LE $250
EOTHTIMQ|n|INT EXP TIME CD LE $250
EQUPGR|n|UNDIVIDED PROFITS
ESAVDP|n|NONTRANSACTION SAV ACCTS INT EXP
ESAVDPA|n|NONTRANSACT SAV ACCT INT EXT-ANN
ESAVDPQ|n|NONTRANSACT SAV ACCT INT EXP-QTR
ESUBNDA|n|SUBORDINATED NOTES INT EXP-ANN
ETRANDEP|n|TRANSACTION ACCOUNTS INT EXPENSE
ETRANDPA|n|TRANSACTION ACCOUNTS INT EXP-ANN
ETRANDPQ|n|TRANSACTION ACCOUNTS INT EXP-QTR
ETTLOTBA|n|TT&L & OTHER BORROW INT EXP-ANN
ETTLOTBQ|n|TT&L & OTHER BORROW INT EXP-QTR
FFPUR|n|FEDERAL FUNDS PURCHASED
IBEFTXA|n|INC BEFORE INC TAXS & DISC-ANN
IGLSCA|n|AVAILABLE-FOR-SALE SECS G/L
IGLSCAQ|n|AVAILABLE-FOR-SALE SEC G/L-QTR
IGLSCH|n|HELD-TO-MATURITY SECS G/L
ILNA|n|LOAN INCOME-ANN
ILNLSA|n|LOAN & LEASE INCOME-ANN
ILNLSQ|n|LOAN & LEASE INCOME-QTR
ILNLSXA|n|TAX-EXEMPT LN & LS INT INC-ANN
ILNLSXQ|n|TAX-EXEMPT LN & LS INT INC-QTR
ILNMUNIQ|n|MUNICIPAL LOAN INCOME-QTR
ILNQ|n|LOAN INCOME-QTR
ISCA|n|TOTAL SECURITY INCOME-ANN
ISERCHGA|n|SERVICE CHARGE ON DEP ACCTS-ANN
ITAXA|n|APPLICABLE INCOME TAXES-ANN
ITAXQA|n|APPLICABLE INCOME TAXES-QTR-ANN
LNCDT1R|n|CONSTR & LAND DEV LNS/TIER 1
LNCIT1R|n|C&I LOANS/TIER 1
LNCONT1R|n|CONSUMER LOANS/TIER 1
LNLSRES|n|ALLOWANCE FOR LOAN AND LEASES
LNREAG5|n|RE AGRICULTURAL-CAVG5
LNRERT1R|n|RE LOANS/TIER 1
NCREAG|n|TOTAL N/C-RE*FARMLAND
NCRECNFM|n|N/C 1-4 FAMILY CONSTRUCTION LOAN
NCRECNOT|n|N/C OTHER CONSTRUCT & LAND DEV
NCRENROT|n|N/C OTHER NONFARM NONRES RE LN
NCRENROW|n|N/C OWN-OCCUPIED NONFARM NONRES
NCRERS2R|n|N/C 1-4 FAM JR LN/1-4 FAM JR LN
NCRERSF2|n|N/C RE 1-4 FAM JUNIOR LIEN
NCRERSFM|n|N/C RE 1-4 FAM FIRST LIEN
NCRERSFR|n|N/C 1-4 FAM 1STLN/1-4 FAM IST LN
NCRSLNLS|n|NC RESTRUCT LOANS EXCL 1-4 FM
NOIQ|n|NET OPERATING INCOME-QTR
NTAGQA|n|AG LOAN NET CHARGE-OFFS-QTR-ANN
NTAGSMA|n|AG LN NET CHARGE-OFFS ANN*SM BKS
NTAGSMQA|n|AG LOAN NET-CHG-QTR-ANN*SMALL BK
NTCIQA|n|COMMERCIAL LOAN NET-CHG-QTR-ANN
NTCOMRE|n|COMMERCIAL RE LN NET CHARGE-OFFS
NTCOMRQA|n|COMML RE NET-CHARGE-OFF-QTR-ANN
NTCONQA|n|CONSUMER LN NET-CHG-QTR-ANN
NTCRCDQA|n|CREDIT CARD LN NET-CHG-QTR-ANN
NTIRTQ|n|RETAINED EARNINGS- BANK- QTR
NTRCDSMJ|n|Time Deposits Less Than Or Equal To insurance Limit
NTREAGA|n|FARMLAND RE LN NET-CHG-ANN
NTREAGQA|n|FARM RE LN NET CHRG-OFF-QTR-ANN
OBOR|n|OTHER BORROWED FUNDS
OBOR2|n|OTHER BORROWED FUNDS-CAVG2
OBOR5|n|OTHER BORROWED FUNDS-CAVG5
OTHBFH03|n|OTH BOR FHLB-OVER 3 YRS
OTHBFH13|n|OTH BOR. FHLB-1 TO 3 YRS
P3COMRE|n|30-89 DAYS P/D-COMMERCIAL RE
P3RECONR|n|30-89 PAST DUE CONST RE/CONST RE
P3RERS2R|n|30-89 P/D 1-4FAM JR/1-4 FAM JR
RBCEQUP|n|RETAINED EARNINGS - RBC
RBCT1W|n|TIER 1 CAPITAL - REPORTED
REPOPUR|n|REPURCHASE AGREEMENTS
SC2|n|SECURITIES-CAVG2
SC5|n|SECURITIES-CAVG5
SCMUNIAA|n|MUNICIPAL SECURITIES -AA
SCMUNIAF|n|MUNICIPAL SECURITIES -AF
SCMUNIHA|n|MUNICIPAL SECURITIES -HA
SCMUNIHF|n|MUNICIPAL SECURITIES -HF";

    private static string FieldDefs_sod = @"ADDRESBR|s|ADDRESS (BRANCH)
ADDRESS|s|ADDRESS
ASSET|n|TOTAL ASSETS
BKCLASS|s|INSTITUTION CLASS
BKMO|n|MAIN OFFICE DESIGNATION FLAG
BRCENM|s|CENCODES C
BRNUM|n|BRANCH NUMBER
BRSERTYP|n|BRANCH SERVICE TYPE
CALL|s|REPORT TYPE
CB|n|COMMUNITY BANK FLAG
CBSA_DIV_NAMB|s|CBSA DIVISION NAME (BRANCH)
CERT|n|FDIC CERT NUMBER
CHARTER|s|CHARTER
CHRTAGNN|s|CHARTER AGENT NAME
CHRTAGNT|s|CHARTER AGENT CODE
CITY|s|CITY (MAIN OFFICE)
CITY2BR|s|PREFERRED CITY (BRANCH)
CITYBR|s|CITY (BRANCH)
CITYHCR|s|CITY-HOLDING CO.- REGULATORY
CLCODE|n|CLASS NUMBER
CNTRYNA|s|COUNTRY NAME (MAIN OFFICE)
CNTRYNAB|s|COUNTRY NAME (BRANCH)
CNTYNAMB|s|COUNTY NAME (BRANCH)
CNTYNUMB|n|FIPS COUNTY CODE (BRANCH)
CONSOLD|n|CONSOLIDATED BRANCH NUMBER
CSABR|n|CSA NUMBER (BRANCH)
CSANAMBR|s|CSA NAME (BRANCH)
DENOVO|n|DENOVO FLAG
DEPDOM|n|TOTAL DOMESTIC DEPOSITS
DEPSUM|n|TOTAL DEPOSITS
DEPSUMBR|n|DOMESTIC DEPOSITS (SOD)
DIVISIONB|n|CBSA DIVISION CODE (BRANCH)
DOCKET|n|OTS DOCKET NUMBER
ESCROW|n|ESCROW ACCOUNTS - TFR
FDICDBS|n|FDIC REGION NUMBER
FDICNAME|s|FDIC REGION NAME
FED|n|FRB DISTRICT NUMBER
FEDNAME|s|FED DISTRICT NAME
HCTMULT|s|MULTI-BANK HOLDING CO
INSAGNT1|s|PRIMARY INSURANCE FUND
INSBRDD|n|DEMAND DEPOSITS IN INSURED BRANCHES
INSBRTS|n|TIME & SAVINGS DEPOSITS IN INSURED BRANCHES
INSURED|s|INSURED
METROBR|n|METRO FLAG (BRANCH)
MICROBR|n|MICRO FLAG (BRANCH)
MSABR|n|MSA (BRANCH)
MSANAMB|s|MSA NAME (BRANCH)
NAMEBR|s|INSTITUTION NAME (BRANCH)
NAMEFULL|s|INSTITUTION NAME (MAIN OFFICE)
NAMEHCR|s|BANK HOLDING CO. NAME
NECNAMB|s|NECTA NAME (BRANCH)
NECTABR|s|NECTA (BRANCH)
OCCDIST|n|OCC DISTRICT NUMBER
OCCNAME|s|OCC REGION NAME
PLACENUM|n|PLACE CODE NUMBER (DF)
REGAGNT|s|PRIMARY FEDERAL REGULATOR
RSSDHCR|n|FRB ID NUMBER - BHC
RSSDID|n|FRB ID NUMBER
SIMS_ACQUIRED_DATE|s|SIMS ACQUIRED DATE
SIMS_DESCRIPTION|s|SIMS MATCH CODE (DESCRIPTION)
SIMS_ESTABLISHED_DATE|s|SIMS ESTABLISHED DATE
SIMS_LATITUDE|n|SIMS GEOGRAPHIC LATITUDE
SIMS_LONGITUDE|n|SIMS GEOGRAPHIC LONGITUDE
SIMS_PROJECTION|s|SIMS SCORE (PROJECTION)
SPECDESC|s|SPECGRP DESCRIPTION
SPECGRP|n|INDUSTRY SPECIALIZATION GROUP
STALP|s|FIPS STATE ALPHA CODE (MAIN OFFICE)
STALPBR|s|FIPS STATE ALPHA CODE (BRANCH)
STALPHCR|s|FIPS STATE ALPHA CODE - BHC
STCNTY|n|FIPS STATE & COUNTY NO. (MAIN OFFICE)
STCNTYBR|n|
STNAME|s|FIPS STATE NAME (MAIN OFFICE)
STNAMEBR|s|STATE NAME (BRANCH)
STNUMBR|n|FIPS STATE CODE (BRANCH)
UNINUMBR|n|UNINUM (BRANCH)
UNIT|n|UNIT BANK FLAG
USA|n|USA LOCATED INSTITUTION
YEAR|n|YEAR
ZIP_RAW|s|
ZIPBR_RAW|s|
ZIP|s|Zip Code
ZIPBR|s|Zip Code";

    private static string FieldDefs_demographics = @"ACTEVT|s|STRUCTURE ACTIVITY EVENT CODE. MERGER OR CLOSING CODES ONLY.
BRANCH|n|A FLAG USED TO INDICATE WHETHER AN INSTITUTION HAS BRANCHES.
CALLYM|s|REPRESENTS THE CALENDER DATE FOR WHICH THE FINANCIAL DATA WA
CALLYMD|s|REPRESENTS THE CALANDER DATE FOR WHICH THE FINANCIAL DATA WA
CBSANAME|s|THE U.S. CENSUS BUREAU OFFICE OF MANANGEMENT AND BUDGET DEFI
CERT|n|FDIC Certificate #
CLCODE|n|A TWO DIGIT NUMERIC CODE WHICH IDENTIFIES THE MAJOR AND MINO
CNTRYALP|s|THE FEDERAL INFORMATION PROCESSING STANDARDS (FIPS) ALPHABET
CNTRYNUM|s|THE FEDERAL INFORMATION PROCESSING STANDARDS (FIPS) NUMERIC 
CNTYNUM|s|THE FEDERAL INFORMATION PROCESSING STANDARDS (FIPS) NUMERIC 
CSA|s|THE U.S. CENSUS BUREAU OFFICE OF MANANGEMENT AND BUDGET DEFI
DIVISION|n|A FLAG USED TO INDICATE WHETHER AN INSTITUTION IS IN A CBSA 
DOCKET|n|A UNIQUE IDENTIFICATION NUMBER ASSIGNED TO INSTITUTIONS CHAR
FDICAREA|n|A NUMBER USED TO IDENTIFY THE FDIC COMPLIANCE AREA IN WHICH 
FDICTERR|s|AN ABBREVIATION OF THE CURRENT COMPLIANCE TERRITORY WHERE AN
FLDOFDCA|s|THE NAME OF THE COMPLIANCE FIELD OFFICE TO WHICH AN INSTITUT
HCTNONE|n|A FLAG USED TO INDICATE WHETHER AN INSTITUTION IS AN INDEPEN
INSAGNT2|s|THE SECONDARY INSURER, INSURANCE AGENT, OR INSURANCE STATUS 
METRO|n|A FLAG USED TO INDICATE WHETHER AN INSTITUTION IS IN A METRO
MICRO|n|A FLAG USED TO INDICATE WHETHER AN INSTITUTION IS IN A MICRO
MNRTYCDE|n|A CHARACTER FIELD ON THE INSTITUTION FILE CORRESPONDING TO A
MNRTYDTE|s|REPRESENTS THE EFFECTIVE DATE ON WHICH AN INSTITUTION IS ASS
OAKAR|n|A FLAG USED TO INDICATE WHETHER AN INSTITUTION ACQUIRED DEPO
OFFDMULT|n|THE NUMBER OF MULTIPLE SERVICE DOMESTIC OFFICES OPERATED BY 
OFFNDOM|n|THE NUMBER OF NONDOMESTIC OFFICES OPERATED BY AN INSTITUTION
OFFOTH|n|THE NUMBER OF DOMESTIC NON-MULTIPLE SERVICE OFFICES OPERATED
OFFSOD|n|THE NUMBER OF OFFICES OPERATED BY AN INSTITUTION BASED ON TH
OFFSTATE|n|THE NUMBER OF STATES WITH OFFICES (INCLUDING ITS MAIN OFFICE
OFFTOT|n|THE TOTAL NUMBER OF OFFICES OPERATED BY AN INSTITUTION.
OFFUSOA|n|THE NUMBER OF DOMESTIC AND U.S. TERRITORIES OFFICES OPERATED
QTRNO|n|IDENTIFIES THE CALENDAR QUARTER. 1 = MARCH; 2 = JUNE; 3 = SE
REPDTE|s|Report Date
REPDTE_INT|s|Report Date Integer
RISKTERR|s|AN ABBREVIATION OF THE CURRENT RISK TERRITORY FOR AN INSTITU
SASSER|n|A FLAG USED TO INDICATE WHETHER AN INSTITUTION WAS A FORMER 
SIMS_LAT|n|GEOGRAPHIC LATITUDE OF MAIN OFFICE.
SIMS_LONG|n|GEOGRAPHIC LONGITUDE OF MAIN OFFICE.
WEBADDR|s|Primary Internet Web Address
TE01N528|s|Web Site URL 01
TE02N528|s|Web Site URL 02
TE03N528|s|Web Site URL 03
TE04N528|s|Web Site URL 04
TE05N528|s|Web Site URL 05
TE06N528|s|Web Site URL 06
TE07N528|s|Web Site URL 07
TE08N528|s|Web Site URL 08
TE09N528|s|Web Site URL 09
TE10N528|s|Web Site URL 10
TE01N529|s|Trade Name 01
TE02N529|s|Trade Name 02
TE03N529|s|Trade Name 03
TE04N529|s|Trade Name 04
TE05N529|s|Trade Name 05
TE06N529|s|Trade Name 06";

}
