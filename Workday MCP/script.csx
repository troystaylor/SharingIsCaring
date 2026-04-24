using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class Script : ScriptBase
{
    private string _correlationId;

    // ========================================
    // WORKDAY CONFIGURATION — REPLACE BEFORE DEPLOYMENT
    // ========================================
    private const string SoapBaseUrl = "https://[WORKDAY_HOSTNAME].workday.com/ccx/service/[TENANT_NAME]";
    private const string ApiVersion = "v46.0";
    private const string ProtocolVersion = "2025-11-25";

    // ========================================
    // APPLICATION INSIGHTS CONFIGURATION
    // ========================================
    private const string APP_INSIGHTS_CONNECTION_STRING = "";
    private const string ConnectorName = "Workday MCP";

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        _correlationId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;

        await LogToAppInsights("RequestReceived", new Dictionary<string, string>
        {
            ["CorrelationId"] = _correlationId,
            ["OperationId"] = this.Context.OperationId
        });

        try
        {
            HttpResponseMessage result;
            switch (this.Context.OperationId)
            {
                case "InvokeMCP":
                    result = await HandleMcpRequest().ConfigureAwait(false);
                    break;

                // Human Resources
                case "GetWorkers":
                    result = await HandleDirectOperation("Get_Workers", "Human_Resources", "get_workers").ConfigureAwait(false);
                    break;
                case "GetEmployee":
                    result = await HandleDirectOperation("Get_Employee", "Human_Resources", "get_employee").ConfigureAwait(false);
                    break;
                case "GetEmployeePersonalInfo":
                    result = await HandleDirectOperation("Get_Employee_Personal_Info", "Human_Resources", "get_employee_personal_info").ConfigureAwait(false);
                    break;
                case "GetEmployeeEmploymentInfo":
                    result = await HandleDirectOperation("Get_Employee_Employment_Info", "Human_Resources", "get_employee_employment_info").ConfigureAwait(false);
                    break;
                case "GetOrganizations":
                    result = await HandleDirectOperation("Get_Organizations", "Human_Resources", "get_organizations").ConfigureAwait(false);
                    break;
                case "GetOrganization":
                    result = await HandleDirectOperation("Get_Organization", "Human_Resources", "get_organization").ConfigureAwait(false);
                    break;
                case "GetLocations":
                    result = await HandleDirectOperation("Get_Locations", "Human_Resources", "get_locations").ConfigureAwait(false);
                    break;
                case "GetJobProfiles":
                    result = await HandleDirectOperation("Get_Job_Profiles", "Human_Resources", "get_job_profiles").ConfigureAwait(false);
                    break;
                case "GetJobFamilies":
                    result = await HandleDirectOperation("Get_Job_Families", "Human_Resources", "get_job_families").ConfigureAwait(false);
                    break;
                case "GetServerTimestamp":
                    result = await HandleDirectOperation("Get_Server_Timestamp", "Human_Resources", "get_server_timestamp").ConfigureAwait(false);
                    break;
                case "ChangeWorkContactInfo":
                    result = await HandleDirectOperation("Change_Work_Contact_Information", "Human_Resources", "change_work_contact_info").ConfigureAwait(false);
                    break;
                case "ChangeBusinessTitle":
                    result = await HandleDirectOperation("Change_Business_Title", "Human_Resources", "change_business_title").ConfigureAwait(false);
                    break;
                case "ChangePreferredName":
                    result = await HandleDirectOperation("Change_Preferred_Name", "Human_Resources", "change_preferred_name").ConfigureAwait(false);
                    break;
                case "MaintainContactInfo":
                    result = await HandleDirectOperation("Maintain_Contact_Information", "Human_Resources", "maintain_contact_info").ConfigureAwait(false);
                    break;

                // Staffing
                case "GetPositions":
                    result = await HandleDirectOperation("Get_Positions", "Staffing", "get_positions").ConfigureAwait(false);
                    break;
                case "GetApplicants":
                    result = await HandleDirectOperation("Get_Applicants", "Staffing", "get_applicants").ConfigureAwait(false);
                    break;
                case "GetWorkerDocuments":
                    result = await HandleDirectOperation("Get_Worker_Documents", "Staffing", "get_worker_documents").ConfigureAwait(false);
                    break;

                // Absence Management
                case "GetAbsenceInputs":
                    result = await HandleDirectOperation("Get_Absence_Inputs", "Absence_Management", "get_absence_inputs").ConfigureAwait(false);
                    break;
                case "GetLeaveRequests":
                    result = await HandleDirectOperation("Get_Leave_Requests", "Absence_Management", "get_leave_requests").ConfigureAwait(false);
                    break;
                case "GetTimeOffPlans":
                    result = await HandleDirectOperation("Get_Time_Off_Plans", "Absence_Management", "get_time_off_plans").ConfigureAwait(false);
                    break;

                // Compensation
                case "GetCompensationPlans":
                    result = await HandleDirectOperation("Get_Compensation_Plans", "Compensation", "get_compensation_plans").ConfigureAwait(false);
                    break;
                case "GetCompensationPackages":
                    result = await HandleDirectOperation("Get_Compensation_Packages", "Compensation", "get_compensation_packages").ConfigureAwait(false);
                    break;

                // Recruiting
                case "GetJobPostings":
                    result = await HandleDirectOperation("Get_Job_Postings", "Recruiting", "get_job_postings").ConfigureAwait(false);
                    break;
                case "GetJobApplications":
                    result = await HandleDirectOperation("Get_Job_Applications", "Recruiting", "get_job_applications").ConfigureAwait(false);
                    break;

                // Time Tracking
                case "GetTimeEntries":
                    result = await HandleDirectOperation("Get_Time_Entries", "Time_Tracking", "get_time_entries").ConfigureAwait(false);
                    break;
                case "GetWorkSchedules":
                    result = await HandleDirectOperation("Get_Work_Schedule_Calendars", "Human_Resources", "get_work_schedules").ConfigureAwait(false);
                    break;

                // Benefits Administration
                case "GetBenefitPlans":
                    result = await HandleDirectOperation("Get_Benefit_Plans", "Benefits_Administration", "get_benefit_plans").ConfigureAwait(false);
                    break;
                case "GetBenefitEnrollments":
                    result = await HandleDirectOperation("Get_Benefit_Annual_Rates", "Benefits_Administration", "get_benefit_enrollments").ConfigureAwait(false);
                    break;
                case "GetBenefitAnnualRates":
                    result = await HandleDirectOperation("Get_Benefit_Annual_Rates", "Benefits_Administration", "get_benefit_annual_rates").ConfigureAwait(false);
                    break;
                case "GetHealthCareRates":
                    result = await HandleDirectOperation("Get_Health_Care_Rates", "Benefits_Administration", "get_health_care_rates").ConfigureAwait(false);
                    break;

                // Performance Management
                case "GetEmployeeReviews":
                    result = await HandleDirectOperation("Get_Employee_Reviews", "Performance_Management", "get_employee_reviews").ConfigureAwait(false);
                    break;
                case "GetCompetencies":
                    result = await HandleDirectOperation("Get_Competencies", "Performance_Management", "get_competencies").ConfigureAwait(false);
                    break;
                case "GetOrganizationGoals":
                    result = await HandleDirectOperation("Get_Organization_Goals", "Performance_Management", "get_organization_goals").ConfigureAwait(false);
                    break;
                case "GetCheckIns":
                    result = await HandleDirectOperation("Get_Check-Ins", "Performance_Management", "get_check_ins").ConfigureAwait(false);
                    break;
                case "ManageGoals":
                    result = await HandleDirectOperation("Manage_Goals", "Performance_Management", "manage_goals").ConfigureAwait(false);
                    break;
                case "GiveFeedback":
                    result = await HandleDirectOperation("Give_Feedback", "Performance_Management", "give_feedback").ConfigureAwait(false);
                    break;

                // Talent
                case "GetSkills":
                    result = await HandleDirectOperation("Get_Skills", "Talent", "get_skills").ConfigureAwait(false);
                    break;
                case "GetWorkerSkills":
                    result = await HandleDirectOperation("Get_Manage_Skills", "Talent", "get_manage_skills").ConfigureAwait(false);
                    break;
                case "GetSuccessionPlans":
                    result = await HandleDirectOperation("Get_Succession_Plans", "Talent", "get_succession_plans").ConfigureAwait(false);
                    break;
                case "GetTalentPools":
                    result = await HandleDirectOperation("Get_Talent_Pools", "Talent", "get_talent_pools").ConfigureAwait(false);
                    break;
                case "GetDevelopmentItems":
                    result = await HandleDirectOperation("Get_Development_Items", "Talent", "get_development_items").ConfigureAwait(false);
                    break;
                case "GetWorkExperiences":
                    result = await HandleDirectOperation("Get_Work_Experiences", "Talent", "get_work_experiences").ConfigureAwait(false);
                    break;
                case "ManageSkills":
                    result = await HandleDirectOperation("Manage_Skills", "Talent", "manage_skills").ConfigureAwait(false);
                    break;

                // Learning
                case "GetLearningCourses":
                    result = await HandleDirectOperation("Get_Learning_Courses", "Learning", "get_learning_courses").ConfigureAwait(false);
                    break;
                case "GetLearningEnrollments":
                    result = await HandleDirectOperation("Get_Learning_Enrollments", "Learning", "get_learning_enrollments").ConfigureAwait(false);
                    break;
                case "GetLearningCourseOfferings":
                    result = await HandleDirectOperation("Get_Learning_Course_Offerings", "Learning", "get_learning_course_offerings").ConfigureAwait(false);
                    break;
                case "GetLearningPrograms":
                    result = await HandleDirectOperation("Get_Learning_Programs", "Learning", "get_learning_programs").ConfigureAwait(false);
                    break;

                // Payroll Interface
                case "GetPayees":
                    result = await HandleDirectOperation("Get_Payees", "Payroll_Interface", "get_payees").ConfigureAwait(false);
                    break;
                case "GetWorkerCostingAllocations":
                    result = await HandleDirectOperation("Get_Worker_Costing_Allocations", "Payroll_Interface", "get_worker_costing_allocations").ConfigureAwait(false);
                    break;
                case "GetExternalPayrollInputs":
                    result = await HandleDirectOperation("Get_External_Payroll_Inputs", "Payroll_Interface", "get_external_payroll_inputs").ConfigureAwait(false);
                    break;

                // Financial Management
                case "GetJournals":
                    result = await HandleDirectOperation("Get_Journals", "Financial_Management", "get_journals").ConfigureAwait(false);
                    break;
                case "GetWorkdayCompanies":
                    result = await HandleDirectOperation("Get_Workday_Companies", "Financial_Management", "get_workday_companies").ConfigureAwait(false);
                    break;
                case "GetCostCenters":
                    result = await HandleDirectOperation("Get_Cost_Centers", "Financial_Management", "get_cost_centers").ConfigureAwait(false);
                    break;
                case "GetCurrencyConversionRates":
                    result = await HandleDirectOperation("Get_Currency_Conversion_Rates", "Financial_Management", "get_currency_conversion_rates").ConfigureAwait(false);
                    break;
                case "GetProjects":
                    result = await HandleDirectOperation("Get_Basic_Projects", "Financial_Management", "get_projects").ConfigureAwait(false);
                    break;
                case "GetBusinessUnits":
                    result = await HandleDirectOperation("Get_Business_Units", "Financial_Management", "get_business_units").ConfigureAwait(false);
                    break;

                // Resource Management
                case "GetSuppliers":
                    result = await HandleDirectOperation("Get_Suppliers", "Resource_Management", "get_suppliers").ConfigureAwait(false);
                    break;
                case "GetExpenseReports":
                    result = await HandleDirectOperation("Get_Expense_Reports", "Resource_Management", "get_expense_reports").ConfigureAwait(false);
                    break;
                case "GetPurchaseOrders":
                    result = await HandleDirectOperation("Get_Purchase_Orders", "Resource_Management", "get_purchase_orders").ConfigureAwait(false);
                    break;
                case "GetSupplierInvoices":
                    result = await HandleDirectOperation("Get_Supplier_Invoices", "Resource_Management", "get_supplier_invoices").ConfigureAwait(false);
                    break;
                case "GetAssets":
                    result = await HandleDirectOperation("Get_Assets", "Resource_Management", "get_assets").ConfigureAwait(false);
                    break;
                case "GetRequisitions":
                    result = await HandleDirectOperation("Get_Requisitions", "Resource_Management", "get_requisitions").ConfigureAwait(false);
                    break;

                default:
                    result = new HttpResponseMessage(HttpStatusCode.BadRequest)
                    {
                        Content = new StringContent($"Unknown operation: {this.Context.OperationId}")
                    };
                    break;
            }

            await LogToAppInsights("RequestCompleted", new Dictionary<string, string>
            {
                ["CorrelationId"] = _correlationId,
                ["OperationId"] = this.Context.OperationId,
                ["StatusCode"] = ((int)result.StatusCode).ToString(),
                ["DurationMs"] = (DateTime.UtcNow - startTime).TotalMilliseconds.ToString("F0")
            });

            return result;
        }
        catch (Exception ex)
        {
            await LogExceptionToAppInsights(ex);
            throw;
        }
    }

    #region MCP Protocol Handler

    private async Task<HttpResponseMessage> HandleDirectOperation(string soapAction, string serviceName, string toolName)
    {
        JObject args = new JObject();
        try
        {
            var bodyContent = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(bodyContent))
                args = JObject.Parse(bodyContent);
        }
        catch { /* No body or invalid JSON — use empty args */ }

        // Build the appropriate SOAP body using existing tool builders
        string requestBody;
        switch (toolName)
        {
            case "get_workers": requestBody = BuildGetWorkersBody(args); break;
            case "get_employee": requestBody = BuildRefBody("Employee_Reference", "Employee_ID", args["employee_reference"]); break;
            case "get_employee_personal_info": requestBody = BuildRefBody("Employee_Reference", "Employee_ID", args["employee_reference"]); break;
            case "get_employee_employment_info": requestBody = BuildRefBody("Employee_Reference", "Employee_ID", args["employee_reference"]); break;
            case "get_organizations": requestBody = BuildGetOrganizationsBody(args); break;
            case "get_organization": requestBody = BuildRefBody("Organization_Reference", "Organization_Reference_ID", args["organization_reference"]); break;
            case "get_locations": requestBody = BuildGetLocationsBody(args); break;
            case "get_job_profiles": requestBody = BuildOptRefBody("Job_Profile_Reference", "Job_Profile_ID", args["reference_id"]); break;
            case "get_job_families": requestBody = BuildOptRefBody("Job_Family_Reference", "Job_Family_ID", args["reference_id"]); break;
            case "get_server_timestamp": requestBody = ""; break;
            case "change_work_contact_info": requestBody = BuildChangeWorkContactBody(args); break;
            case "change_business_title": requestBody = BuildChangeBusinessTitleBody(args); break;
            case "change_preferred_name": requestBody = BuildChangePreferredNameBody(args); break;
            case "maintain_contact_info": requestBody = BuildMaintainContactInfoBody(args); break;
            case "get_positions": requestBody = BuildOptRefBody("Position_Reference", "Position_ID", args["reference_id"]); break;
            case "get_applicants": requestBody = BuildOptRefBody("Applicant_Reference", "Applicant_ID", args["reference_id"]); break;
            case "get_worker_documents": requestBody = BuildRefBody("Worker_Reference", "Employee_ID", args["worker_reference"]); break;
            case "get_absence_inputs": requestBody = BuildOptRefBody("Worker_Reference", "Employee_ID", args["worker_reference"]); break;
            case "get_leave_requests": requestBody = BuildOptRefBody("Worker_Reference", "Employee_ID", args["worker_reference"]); break;
            case "get_time_off_plans": requestBody = BuildOptRefBody("Time_Off_Plan_Reference", "Time_Off_Plan_ID", args["reference_id"]); break;
            case "get_compensation_plans": requestBody = BuildOptRefBody("Compensation_Plan_Reference", "Compensation_Plan_ID", args["reference_id"]); break;
            case "get_compensation_packages": requestBody = BuildOptRefBody("Compensation_Package_Reference", "Compensation_Package_ID", args["reference_id"]); break;
            case "get_job_postings": requestBody = BuildOptRefBody("Job_Posting_Reference", "Job_Posting_ID", args["reference_id"]); break;
            case "get_job_applications": requestBody = BuildOptRefBody("Job_Application_Reference", "Job_Application_ID", args["reference_id"]); break;
            case "get_time_entries": requestBody = BuildOptRefBody("Worker_Reference", "Employee_ID", args["worker_reference"]); break;
            case "get_work_schedules": requestBody = BuildOptRefBody("Work_Schedule_Calendar_Reference", "Work_Schedule_Calendar_ID", args["reference_id"]); break;
            case "get_benefit_plans": requestBody = BuildOptRefBody("Benefit_Plan_Reference", "Benefit_Plan_ID", args["reference_id"]); break;
            case "get_benefit_enrollments": requestBody = BuildOptRefBody("Worker_Reference", "Employee_ID", args["worker_reference"]); break;
            case "get_benefit_annual_rates": requestBody = BuildOptRefBody("Worker_Reference", "Employee_ID", args["worker_reference"]); break;
            case "get_health_care_rates": requestBody = BuildOptRefBody("Health_Care_Rate_Reference", "Health_Care_Rate_ID", args["reference_id"]); break;
            case "get_employee_reviews": requestBody = BuildOptRefBody("Worker_Reference", "Employee_ID", args["worker_reference"]); break;
            case "get_competencies": requestBody = BuildOptRefBody("Competency_Reference", "Competency_ID", args["reference_id"]); break;
            case "get_organization_goals": requestBody = BuildRefBody("Organization_Reference", "Organization_Reference_ID", args["organization_reference"]); break;
            case "get_check_ins": requestBody = BuildOptRefBody("Worker_Reference", "Employee_ID", args["worker_reference"]); break;
            case "manage_goals": requestBody = BuildManageGoalsBody(args); break;
            case "give_feedback": requestBody = BuildGiveFeedbackBody(args); break;
            case "get_skills": requestBody = BuildOptRefBody("Skill_Reference", "Skill_ID", args["reference_id"]); break;
            case "get_manage_skills": requestBody = BuildRefBody("Worker_Reference", "Employee_ID", args["worker_reference"]); break;
            case "get_succession_plans": requestBody = BuildOptRefBody("Position_Reference", "Position_ID", args["reference_id"]); break;
            case "get_talent_pools": requestBody = BuildOptRefBody("Talent_Pool_Reference", "Talent_Pool_ID", args["reference_id"]); break;
            case "get_development_items": requestBody = BuildOptRefBody("Development_Item_Reference", "Development_Item_ID", args["reference_id"]); break;
            case "get_work_experiences": requestBody = BuildOptRefBody("Worker_Reference", "Employee_ID", args["worker_reference"]); break;
            case "manage_skills": requestBody = BuildManageSkillsBody(args); break;
            case "get_learning_courses": requestBody = BuildOptRefBody("Learning_Course_Reference", "Learning_Course_ID", args["reference_id"]); break;
            case "get_learning_enrollments": requestBody = BuildOptRefBody("Worker_Reference", "Employee_ID", args["worker_reference"]); break;
            case "get_learning_course_offerings": requestBody = BuildOptRefBody("Learning_Course_Offering_Reference", "Learning_Course_Offering_ID", args["reference_id"]); break;
            case "get_learning_programs": requestBody = BuildOptRefBody("Learning_Program_Reference", "Learning_Program_ID", args["reference_id"]); break;
            case "get_payees": requestBody = BuildOptRefBody("Worker_Reference", "Employee_ID", args["worker_reference"]); break;
            case "get_worker_costing_allocations": requestBody = BuildOptRefBody("Worker_Reference", "Employee_ID", args["worker_reference"]); break;
            case "get_external_payroll_inputs": requestBody = BuildOptRefBody("Worker_Reference", "Employee_ID", args["worker_reference"]); break;
            case "get_journals": requestBody = BuildOptRefBody("Journal_Reference", "Journal_Reference_ID", args["reference_id"]); break;
            case "get_workday_companies": requestBody = BuildOptRefBody("Company_Reference", "Company_Reference_ID", args["reference_id"]); break;
            case "get_cost_centers": requestBody = BuildOptRefBody("Cost_Center_Reference", "Cost_Center_Reference_ID", args["reference_id"]); break;
            case "get_currency_conversion_rates": requestBody = BuildGetCurrencyRatesBody(args); break;
            case "get_projects": requestBody = BuildOptRefBody("Project_Reference", "Project_ID", args["reference_id"]); break;
            case "get_business_units": requestBody = BuildOptRefBody("Business_Unit_Reference", "Business_Unit_ID", args["reference_id"]); break;
            case "get_suppliers": requestBody = BuildOptRefBody("Supplier_Reference", "Supplier_ID", args["reference_id"]); break;
            case "get_expense_reports": requestBody = BuildOptRefBody("Expense_Report_Reference", "Expense_Report_ID", args["reference_id"]); break;
            case "get_purchase_orders": requestBody = BuildOptRefBody("Purchase_Order_Reference", "Purchase_Order_ID", args["reference_id"]); break;
            case "get_supplier_invoices": requestBody = BuildOptRefBody("Supplier_Invoice_Reference", "Supplier_Invoice_ID", args["reference_id"]); break;
            case "get_assets": requestBody = BuildOptRefBody("Asset_Reference", "Asset_ID", args["reference_id"]); break;
            case "get_requisitions": requestBody = BuildOptRefBody("Requisition_Reference", "Requisition_ID", args["reference_id"]); break;
            default: requestBody = ""; break;
        }

        var result = await CallWorkday(soapAction, serviceName, requestBody).ConfigureAwait(false);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(result.ToString(Newtonsoft.Json.Formatting.Indented), Encoding.UTF8, "application/json")
        };
    }

    private async Task<HttpResponseMessage> HandleMcpRequest()
    {
        var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var mcpRequest = JObject.Parse(body);

        var method = mcpRequest["method"]?.ToString() ?? "";
        var id = mcpRequest["id"];
        var normalizedMethod = method.ToLowerInvariant().Trim();

        await LogToAppInsights("McpRequest", new Dictionary<string, string>
        {
            ["Method"] = method,
            ["CorrelationId"] = _correlationId
        });

        switch (normalizedMethod)
        {
            case "initialize":
                var clientProtocol = mcpRequest["params"]?["protocolVersion"]?.ToString() ?? ProtocolVersion;
                var negotiatedProtocol = clientProtocol;
                var initResult = new JObject
                {
                    ["protocolVersion"] = negotiatedProtocol,
                    ["capabilities"] = new JObject
                    {
                        ["tools"] = new JObject { ["listChanged"] = false },
                        ["logging"] = new JObject()
                    },
                    ["serverInfo"] = new JObject
                    {
                        ["name"] = "workday-mcp",
                        ["version"] = "1.0.0",
                        ["title"] = "Workday MCP Server",
                        ["description"] = "Power MCP tool server for Workday Web Services (v46.0). Covers HCM, Staffing, Benefits, Performance, Talent, Learning, Compensation, Recruiting, Absence, Time Tracking, Payroll, Finance, and Resource Management."
                    }
                };
                return CreateMcpResponse(id, initResult);

            case "initialized":
            case "notifications/initialized":
            case "notifications/cancelled":
                return CreateMcpResponse(id, new JObject());

            case "tools/list":
                var tools = GetToolDefinitions();
                return CreateMcpResponse(id, new JObject { ["tools"] = tools });

            case "tools/call":
                return await HandleToolCall(mcpRequest, id).ConfigureAwait(false);

            case "resources/list":
                return CreateMcpResponse(id, new JObject { ["resources"] = new JArray() });

            case "ping":
                return CreateMcpResponse(id, new JObject());

            default:
                return CreateMcpError(id, -32601, "Method not found", method);
        }
    }

    #endregion

    #region Tool Definitions

    private JArray GetToolDefinitions()
    {
        var tools = new JArray();

        // ===== HUMAN RESOURCES =====
        tools.Add(Tool("get_workers", "Get Workers", "Search and retrieve workers from Workday. Returns employee and contingent worker data. Use worker_reference to get a specific worker by Employee_ID or Workday_ID.", new JObject
        {
            ["worker_reference"] = Prop("string", "Worker reference ID (Employee_ID or Workday_ID)"),
            ["count"] = Prop("integer", "Maximum number of results to return (default 50)"),
            ["page"] = Prop("integer", "Page number for pagination (default 1)")
        }));
        tools.Add(Tool("get_employee", "Get Employee", "Retrieve full employee record including employment, personal, and compensation data.", new JObject
        {
            ["employee_reference"] = Prop("string", "Employee reference ID (Employee_ID or Workday_ID)")
        }, new JArray { "employee_reference" }));
        tools.Add(Tool("get_employee_personal_info", "Get Employee Personal Info", "Retrieve personal information for an employee including biographic and demographic data.", new JObject
        {
            ["employee_reference"] = Prop("string", "Employee reference ID")
        }, new JArray { "employee_reference" }));
        tools.Add(Tool("get_employee_employment_info", "Get Employee Employment Info", "Retrieve employment information including position, job, and status data.", new JObject
        {
            ["employee_reference"] = Prop("string", "Employee reference ID")
        }, new JArray { "employee_reference" }));
        tools.Add(Tool("get_organizations", "Get Organizations", "Retrieve organizations by type (Company, Cost Center, Supervisory, Region, etc.).", new JObject
        {
            ["organization_type"] = Prop("string", "Organization type (e.g., Supervisory, Company, Cost_Center, Region)"),
            ["organization_reference"] = Prop("string", "Specific organization reference ID"),
            ["include_inactive"] = Prop("boolean", "Include inactive organizations (default false)")
        }));
        tools.Add(Tool("get_organization", "Get Organization", "Retrieve detailed information for a single organization.", new JObject
        {
            ["organization_reference"] = Prop("string", "Organization reference ID")
        }, new JArray { "organization_reference" }));
        tools.Add(Tool("get_locations", "Get Locations", "Retrieve location data from Workday.", new JObject
        {
            ["location_reference"] = Prop("string", "Location reference ID"),
            ["location_name"] = Prop("string", "Search by location name")
        }));
        tools.Add(Tool("get_job_profiles", "Get Job Profiles", "Retrieve job profile data.", new JObject
        {
            ["job_profile_reference"] = Prop("string", "Job profile reference ID")
        }));
        tools.Add(Tool("get_job_families", "Get Job Families", "Retrieve job family data.", new JObject
        {
            ["job_family_reference"] = Prop("string", "Job family reference ID")
        }));
        tools.Add(Tool("get_server_timestamp", "Get Server Timestamp", "Retrieve Workday server timestamp. Use as a health check or connectivity test.", new JObject()));

        // HR Write tools
        tools.Add(Tool("change_work_contact_info", "Change Work Contact Information", "Update work contact information for a person (email, phone, address).", new JObject
        {
            ["worker_reference"] = Prop("string", "Worker reference ID"),
            ["email_address"] = Prop("string", "New work email address"),
            ["phone_number"] = Prop("string", "New work phone number"),
            ["phone_device_type"] = Prop("string", "Phone device type (e.g., Landline, Mobile)")
        }, new JArray { "worker_reference" }));
        tools.Add(Tool("change_business_title", "Change Business Title", "Update a worker's business title.", new JObject
        {
            ["worker_reference"] = Prop("string", "Worker reference ID"),
            ["business_title"] = Prop("string", "New business title")
        }, new JArray { "worker_reference", "business_title" }));
        tools.Add(Tool("change_preferred_name", "Change Preferred Name", "Update a person's preferred name.", new JObject
        {
            ["worker_reference"] = Prop("string", "Worker reference ID"),
            ["first_name"] = Prop("string", "Preferred first name"),
            ["last_name"] = Prop("string", "Preferred last name")
        }, new JArray { "worker_reference" }));
        tools.Add(Tool("maintain_contact_info", "Maintain Contact Information", "Create or update contact information for a person.", new JObject
        {
            ["worker_reference"] = Prop("string", "Worker reference ID"),
            ["email_address"] = Prop("string", "Email address"),
            ["phone_number"] = Prop("string", "Phone number"),
            ["address_line_1"] = Prop("string", "Address line 1"),
            ["city"] = Prop("string", "City"),
            ["state"] = Prop("string", "State/region"),
            ["postal_code"] = Prop("string", "Postal code"),
            ["country"] = Prop("string", "Country ISO code")
        }, new JArray { "worker_reference" }));

        // ===== STAFFING =====
        tools.Add(Tool("get_positions", "Get Positions", "Retrieve position management data. Identifies workers filling positions.", new JObject
        {
            ["position_reference"] = Prop("string", "Position reference ID"),
            ["supervisory_organization"] = Prop("string", "Supervisory organization reference ID")
        }));
        tools.Add(Tool("get_applicants", "Get Applicants", "Retrieve pre-hire (applicant) data.", new JObject
        {
            ["applicant_reference"] = Prop("string", "Applicant reference ID")
        }));
        tools.Add(Tool("get_worker_documents", "Get Worker Documents", "Retrieve worker document data.", new JObject
        {
            ["worker_reference"] = Prop("string", "Worker reference ID")
        }, new JArray { "worker_reference" }));

        // ===== ABSENCE MANAGEMENT =====
        tools.Add(Tool("get_absence_inputs", "Get Absence Inputs", "Retrieve absence inputs and accrual data for workers.", new JObject
        {
            ["worker_reference"] = Prop("string", "Worker reference ID")
        }));
        tools.Add(Tool("get_leave_requests", "Get Leave Requests", "Retrieve leave of absence request data.", new JObject
        {
            ["worker_reference"] = Prop("string", "Worker reference ID")
        }));
        tools.Add(Tool("get_time_off_plans", "Get Time Off Plans", "Retrieve time off plan configurations.", new JObject
        {
            ["time_off_plan_reference"] = Prop("string", "Time off plan reference ID")
        }));

        // ===== COMPENSATION =====
        tools.Add(Tool("get_compensation_plans", "Get Compensation Plans", "Retrieve compensation plan data.", new JObject
        {
            ["compensation_plan_reference"] = Prop("string", "Compensation plan reference ID")
        }));
        tools.Add(Tool("get_compensation_packages", "Get Compensation Packages", "Retrieve compensation package data.", new JObject
        {
            ["compensation_package_reference"] = Prop("string", "Compensation package reference ID")
        }));

        // ===== RECRUITING =====
        tools.Add(Tool("get_job_postings", "Get Job Postings", "Retrieve active job posting data.", new JObject
        {
            ["job_posting_reference"] = Prop("string", "Job posting reference ID"),
            ["show_only_active"] = Prop("boolean", "Show only active postings (default true)")
        }));
        tools.Add(Tool("get_job_applications", "Get Job Applications", "Retrieve job application and candidate data.", new JObject
        {
            ["job_application_reference"] = Prop("string", "Job application reference ID"),
            ["job_posting_reference"] = Prop("string", "Filter by job posting")
        }));

        // ===== TIME TRACKING =====
        tools.Add(Tool("get_time_entries", "Get Time Entries", "Retrieve worker time entry data.", new JObject
        {
            ["worker_reference"] = Prop("string", "Worker reference ID"),
            ["start_date"] = Prop("string", "Start date (YYYY-MM-DD)"),
            ["end_date"] = Prop("string", "End date (YYYY-MM-DD)")
        }));
        tools.Add(Tool("get_work_schedules", "Get Work Schedules", "Retrieve work schedule data.", new JObject
        {
            ["work_schedule_reference"] = Prop("string", "Work schedule reference ID")
        }));

        // ===== BENEFITS ADMINISTRATION =====
        tools.Add(Tool("get_benefit_plans", "Get Benefit Plans", "Retrieve benefit plan data.", new JObject
        {
            ["benefit_plan_reference"] = Prop("string", "Benefit plan reference ID")
        }));
        tools.Add(Tool("get_benefit_enrollments", "Get Benefit Enrollments", "Retrieve current benefit election data for workers.", new JObject
        {
            ["worker_reference"] = Prop("string", "Worker reference ID")
        }));
        tools.Add(Tool("get_benefit_annual_rates", "Get Benefit Annual Rates", "Retrieve benefit annual rates for employees.", new JObject
        {
            ["worker_reference"] = Prop("string", "Worker reference ID")
        }));
        tools.Add(Tool("get_health_care_rates", "Get Health Care Rates", "Retrieve health care rate data.", new JObject
        {
            ["health_care_rate_reference"] = Prop("string", "Health care rate reference ID")
        }));

        // ===== PERFORMANCE MANAGEMENT =====
        tools.Add(Tool("get_employee_reviews", "Get Employee Reviews", "Retrieve in-progress and completed employee reviews.", new JObject
        {
            ["worker_reference"] = Prop("string", "Worker reference ID"),
            ["review_reference"] = Prop("string", "Specific review reference ID")
        }));
        tools.Add(Tool("get_competencies", "Get Competencies", "Retrieve competency definitions.", new JObject
        {
            ["competency_reference"] = Prop("string", "Competency reference ID")
        }));
        tools.Add(Tool("get_organization_goals", "Get Organization Goals", "Retrieve goals for an organization.", new JObject
        {
            ["organization_reference"] = Prop("string", "Organization reference ID")
        }, new JArray { "organization_reference" }));
        tools.Add(Tool("get_check_ins", "Get Check-Ins", "Retrieve check-in records.", new JObject
        {
            ["worker_reference"] = Prop("string", "Worker reference ID"),
            ["check_in_reference"] = Prop("string", "Check-in reference ID")
        }));
        tools.Add(Tool("manage_goals", "Manage Goals", "Add a new goal or edit an existing goal for a worker.", new JObject
        {
            ["worker_reference"] = Prop("string", "Worker reference ID"),
            ["goal_name"] = Prop("string", "Goal name/title"),
            ["goal_description"] = Prop("string", "Goal description"),
            ["due_date"] = Prop("string", "Due date (YYYY-MM-DD)"),
            ["status"] = Prop("string", "Goal status")
        }, new JArray { "worker_reference", "goal_name" }));
        tools.Add(Tool("give_feedback", "Give Feedback", "Add anytime feedback for a worker.", new JObject
        {
            ["worker_reference"] = Prop("string", "Worker reference ID to give feedback about"),
            ["feedback_comment"] = Prop("string", "Feedback comment text"),
            ["from_worker_reference"] = Prop("string", "Worker giving the feedback (optional, defaults to authenticated user)")
        }, new JArray { "worker_reference", "feedback_comment" }));

        // ===== TALENT =====
        tools.Add(Tool("get_skills", "Get Skills", "Retrieve skill item definitions.", new JObject
        {
            ["skill_reference"] = Prop("string", "Skill reference ID")
        }));
        tools.Add(Tool("get_manage_skills", "Get Worker Skills", "Retrieve existing skills assigned to a worker.", new JObject
        {
            ["worker_reference"] = Prop("string", "Worker reference ID")
        }, new JArray { "worker_reference" }));
        tools.Add(Tool("get_succession_plans", "Get Succession Plans", "Retrieve succession plans for positions.", new JObject
        {
            ["position_reference"] = Prop("string", "Position reference ID")
        }));
        tools.Add(Tool("get_talent_pools", "Get Talent Pools", "Retrieve talent pool and membership information.", new JObject
        {
            ["talent_pool_reference"] = Prop("string", "Talent pool reference ID")
        }));
        tools.Add(Tool("get_development_items", "Get Development Items", "Retrieve development items.", new JObject
        {
            ["development_item_reference"] = Prop("string", "Development item reference ID")
        }));
        tools.Add(Tool("get_work_experiences", "Get Work Experiences", "Retrieve work experience records.", new JObject
        {
            ["worker_reference"] = Prop("string", "Worker reference ID")
        }));
        tools.Add(Tool("manage_skills", "Manage Skills", "Add or remove skills for a worker.", new JObject
        {
            ["worker_reference"] = Prop("string", "Worker reference ID"),
            ["skill_reference"] = Prop("string", "Skill reference ID to add"),
            ["remove"] = Prop("boolean", "Set to true to remove the skill (default false)")
        }, new JArray { "worker_reference", "skill_reference" }));

        // ===== LEARNING =====
        tools.Add(Tool("get_learning_courses", "Get Learning Courses", "Retrieve blended and digital learning courses.", new JObject
        {
            ["course_reference"] = Prop("string", "Course reference ID")
        }));
        tools.Add(Tool("get_learning_enrollments", "Get Learning Enrollments", "Retrieve learning enrollment records.", new JObject
        {
            ["worker_reference"] = Prop("string", "Worker reference ID"),
            ["enrollment_reference"] = Prop("string", "Enrollment reference ID")
        }));
        tools.Add(Tool("get_learning_course_offerings", "Get Learning Course Offerings", "Retrieve course offering schedules.", new JObject
        {
            ["course_offering_reference"] = Prop("string", "Course offering reference ID")
        }));
        tools.Add(Tool("get_learning_programs", "Get Learning Programs", "Retrieve learning program data.", new JObject
        {
            ["program_reference"] = Prop("string", "Program reference ID")
        }));

        // ===== PAYROLL INTERFACE =====
        tools.Add(Tool("get_payees", "Get Payees", "Retrieve payee data for employees in external pay groups.", new JObject
        {
            ["pay_group_reference"] = Prop("string", "External pay group reference ID"),
            ["worker_reference"] = Prop("string", "Worker reference ID")
        }));
        tools.Add(Tool("get_worker_costing_allocations", "Get Worker Costing Allocations", "Retrieve worker or position costing allocations.", new JObject
        {
            ["worker_reference"] = Prop("string", "Worker reference ID")
        }));
        tools.Add(Tool("get_external_payroll_inputs", "Get External Payroll Inputs", "Retrieve external payroll input data.", new JObject
        {
            ["worker_reference"] = Prop("string", "Worker reference ID")
        }));

        // ===== FINANCIAL MANAGEMENT =====
        tools.Add(Tool("get_journals", "Get Journals", "Retrieve accounting journal entries.", new JObject
        {
            ["journal_reference"] = Prop("string", "Journal reference ID"),
            ["company_reference"] = Prop("string", "Company reference ID")
        }));
        tools.Add(Tool("get_workday_companies", "Get Workday Companies", "Retrieve company data including accounting details.", new JObject
        {
            ["company_reference"] = Prop("string", "Company reference ID")
        }));
        tools.Add(Tool("get_cost_centers", "Get Cost Centers", "Retrieve cost center data.", new JObject
        {
            ["cost_center_reference"] = Prop("string", "Cost center reference ID")
        }));
        tools.Add(Tool("get_currency_conversion_rates", "Get Currency Conversion Rates", "Retrieve currency conversion/FX rates.", new JObject
        {
            ["currency_rate_type"] = Prop("string", "Rate type reference"),
            ["from_currency"] = Prop("string", "Source currency ISO code"),
            ["to_currency"] = Prop("string", "Target currency ISO code")
        }));
        tools.Add(Tool("get_projects", "Get Projects", "Retrieve project worktag data.", new JObject
        {
            ["project_reference"] = Prop("string", "Project reference ID")
        }));
        tools.Add(Tool("get_business_units", "Get Business Units", "Retrieve business unit data.", new JObject
        {
            ["business_unit_reference"] = Prop("string", "Business unit reference ID")
        }));

        // ===== RESOURCE MANAGEMENT =====
        tools.Add(Tool("get_suppliers", "Get Suppliers", "Retrieve supplier data.", new JObject
        {
            ["supplier_reference"] = Prop("string", "Supplier reference ID")
        }));
        tools.Add(Tool("get_expense_reports", "Get Expense Reports", "Retrieve expense report data.", new JObject
        {
            ["expense_report_reference"] = Prop("string", "Expense report reference ID"),
            ["worker_reference"] = Prop("string", "Filter by worker")
        }));
        tools.Add(Tool("get_purchase_orders", "Get Purchase Orders", "Retrieve purchase order data.", new JObject
        {
            ["purchase_order_reference"] = Prop("string", "Purchase order reference ID"),
            ["company_reference"] = Prop("string", "Company reference ID")
        }));
        tools.Add(Tool("get_supplier_invoices", "Get Supplier Invoices", "Retrieve supplier invoice data.", new JObject
        {
            ["supplier_invoice_reference"] = Prop("string", "Supplier invoice reference ID"),
            ["company_reference"] = Prop("string", "Company reference ID")
        }));
        tools.Add(Tool("get_assets", "Get Business Assets", "Retrieve business asset data.", new JObject
        {
            ["asset_reference"] = Prop("string", "Asset reference ID"),
            ["company_reference"] = Prop("string", "Company reference ID")
        }));
        tools.Add(Tool("get_requisitions", "Get Requisitions", "Retrieve requisition data.", new JObject
        {
            ["requisition_reference"] = Prop("string", "Requisition reference ID"),
            ["company_reference"] = Prop("string", "Company reference ID")
        }));

        return tools;
    }

    private JObject Tool(string name, string title, string description, JObject properties, JArray required = null)
    {
        var schema = new JObject
        {
            ["type"] = "object",
            ["properties"] = properties
        };
        if (required != null && required.Count > 0)
            schema["required"] = required;

        return new JObject
        {
            ["name"] = name,
            ["title"] = title,
            ["description"] = description,
            ["inputSchema"] = schema
        };
    }

    private JObject Prop(string type, string description)
    {
        return new JObject
        {
            ["type"] = type,
            ["description"] = description
        };
    }

    #endregion

    #region Tool Call Router

    private async Task<HttpResponseMessage> HandleToolCall(JObject mcpRequest, JToken id)
    {
        var toolName = mcpRequest["params"]?["name"]?.ToString() ?? "";
        var arguments = mcpRequest["params"]?["arguments"] as JObject ?? new JObject();
        var toolStart = DateTime.UtcNow;

        await LogToAppInsights("McpToolCall", new Dictionary<string, string>
        {
            ["ToolName"] = toolName,
            ["CorrelationId"] = _correlationId
        });

        try
        {
            JObject result;
            switch (toolName)
            {
                // Human Resources
                case "get_workers": result = await CallWorkday("Get_Workers", "Human_Resources", BuildGetWorkersBody(arguments)).ConfigureAwait(false); break;
                case "get_employee": result = await CallWorkday("Get_Employee", "Human_Resources", BuildRefBody("Employee_Reference", "Employee_ID", arguments["employee_reference"])).ConfigureAwait(false); break;
                case "get_employee_personal_info": result = await CallWorkday("Get_Employee_Personal_Info", "Human_Resources", BuildRefBody("Employee_Reference", "Employee_ID", arguments["employee_reference"])).ConfigureAwait(false); break;
                case "get_employee_employment_info": result = await CallWorkday("Get_Employee_Employment_Info", "Human_Resources", BuildRefBody("Employee_Reference", "Employee_ID", arguments["employee_reference"])).ConfigureAwait(false); break;
                case "get_organizations": result = await CallWorkday("Get_Organizations", "Human_Resources", BuildGetOrganizationsBody(arguments)).ConfigureAwait(false); break;
                case "get_organization": result = await CallWorkday("Get_Organization", "Human_Resources", BuildRefBody("Organization_Reference", "Organization_Reference_ID", arguments["organization_reference"])).ConfigureAwait(false); break;
                case "get_locations": result = await CallWorkday("Get_Locations", "Human_Resources", BuildGetLocationsBody(arguments)).ConfigureAwait(false); break;
                case "get_job_profiles": result = await CallWorkday("Get_Job_Profiles", "Human_Resources", BuildOptRefBody("Job_Profile_Reference", "Job_Profile_ID", arguments["job_profile_reference"])).ConfigureAwait(false); break;
                case "get_job_families": result = await CallWorkday("Get_Job_Families", "Human_Resources", BuildOptRefBody("Job_Family_Reference", "Job_Family_ID", arguments["job_family_reference"])).ConfigureAwait(false); break;
                case "get_server_timestamp": result = await CallWorkday("Get_Server_Timestamp", "Human_Resources", "").ConfigureAwait(false); break;
                case "change_work_contact_info": result = await CallWorkday("Change_Work_Contact_Information", "Human_Resources", BuildChangeWorkContactBody(arguments)).ConfigureAwait(false); break;
                case "change_business_title": result = await CallWorkday("Change_Business_Title", "Human_Resources", BuildChangeBusinessTitleBody(arguments)).ConfigureAwait(false); break;
                case "change_preferred_name": result = await CallWorkday("Change_Preferred_Name", "Human_Resources", BuildChangePreferredNameBody(arguments)).ConfigureAwait(false); break;
                case "maintain_contact_info": result = await CallWorkday("Maintain_Contact_Information", "Human_Resources", BuildMaintainContactInfoBody(arguments)).ConfigureAwait(false); break;

                // Staffing
                case "get_positions": result = await CallWorkday("Get_Positions", "Staffing", BuildOptRefBody("Position_Reference", "Position_ID", arguments["position_reference"])).ConfigureAwait(false); break;
                case "get_applicants": result = await CallWorkday("Get_Applicants", "Staffing", BuildOptRefBody("Applicant_Reference", "Applicant_ID", arguments["applicant_reference"])).ConfigureAwait(false); break;
                case "get_worker_documents": result = await CallWorkday("Get_Worker_Documents", "Staffing", BuildRefBody("Worker_Reference", "Employee_ID", arguments["worker_reference"])).ConfigureAwait(false); break;

                // Absence Management
                case "get_absence_inputs": result = await CallWorkday("Get_Absence_Inputs", "Absence_Management", BuildOptRefBody("Worker_Reference", "Employee_ID", arguments["worker_reference"])).ConfigureAwait(false); break;
                case "get_leave_requests": result = await CallWorkday("Get_Leave_Requests", "Absence_Management", BuildOptRefBody("Worker_Reference", "Employee_ID", arguments["worker_reference"])).ConfigureAwait(false); break;
                case "get_time_off_plans": result = await CallWorkday("Get_Time_Off_Plans", "Absence_Management", BuildOptRefBody("Time_Off_Plan_Reference", "Time_Off_Plan_ID", arguments["time_off_plan_reference"])).ConfigureAwait(false); break;

                // Compensation
                case "get_compensation_plans": result = await CallWorkday("Get_Compensation_Plans", "Compensation", BuildOptRefBody("Compensation_Plan_Reference", "Compensation_Plan_ID", arguments["compensation_plan_reference"])).ConfigureAwait(false); break;
                case "get_compensation_packages": result = await CallWorkday("Get_Compensation_Packages", "Compensation", BuildOptRefBody("Compensation_Package_Reference", "Compensation_Package_ID", arguments["compensation_package_reference"])).ConfigureAwait(false); break;

                // Recruiting
                case "get_job_postings": result = await CallWorkday("Get_Job_Postings", "Recruiting", BuildOptRefBody("Job_Posting_Reference", "Job_Posting_ID", arguments["job_posting_reference"])).ConfigureAwait(false); break;
                case "get_job_applications": result = await CallWorkday("Get_Job_Applications", "Recruiting", BuildOptRefBody("Job_Application_Reference", "Job_Application_ID", arguments["job_application_reference"])).ConfigureAwait(false); break;

                // Time Tracking
                case "get_time_entries": result = await CallWorkday("Get_Time_Entries", "Time_Tracking", BuildOptRefBody("Worker_Reference", "Employee_ID", arguments["worker_reference"])).ConfigureAwait(false); break;
                case "get_work_schedules": result = await CallWorkday("Get_Work_Schedule_Calendars", "Human_Resources", BuildOptRefBody("Work_Schedule_Calendar_Reference", "Work_Schedule_Calendar_ID", arguments["work_schedule_reference"])).ConfigureAwait(false); break;

                // Benefits Administration
                case "get_benefit_plans": result = await CallWorkday("Get_Benefit_Plans", "Benefits_Administration", BuildOptRefBody("Benefit_Plan_Reference", "Benefit_Plan_ID", arguments["benefit_plan_reference"])).ConfigureAwait(false); break;
                case "get_benefit_enrollments": result = await CallWorkday("Get_Benefit_Annual_Rates", "Benefits_Administration", BuildOptRefBody("Worker_Reference", "Employee_ID", arguments["worker_reference"])).ConfigureAwait(false); break;
                case "get_benefit_annual_rates": result = await CallWorkday("Get_Benefit_Annual_Rates", "Benefits_Administration", BuildOptRefBody("Worker_Reference", "Employee_ID", arguments["worker_reference"])).ConfigureAwait(false); break;
                case "get_health_care_rates": result = await CallWorkday("Get_Health_Care_Rates", "Benefits_Administration", BuildOptRefBody("Health_Care_Rate_Reference", "Health_Care_Rate_ID", arguments["health_care_rate_reference"])).ConfigureAwait(false); break;

                // Performance Management
                case "get_employee_reviews": result = await CallWorkday("Get_Employee_Reviews", "Performance_Management", BuildOptRefBody("Worker_Reference", "Employee_ID", arguments["worker_reference"])).ConfigureAwait(false); break;
                case "get_competencies": result = await CallWorkday("Get_Competencies", "Performance_Management", BuildOptRefBody("Competency_Reference", "Competency_ID", arguments["competency_reference"])).ConfigureAwait(false); break;
                case "get_organization_goals": result = await CallWorkday("Get_Organization_Goals", "Performance_Management", BuildRefBody("Organization_Reference", "Organization_Reference_ID", arguments["organization_reference"])).ConfigureAwait(false); break;
                case "get_check_ins": result = await CallWorkday("Get_Check-Ins", "Performance_Management", BuildOptRefBody("Worker_Reference", "Employee_ID", arguments["worker_reference"])).ConfigureAwait(false); break;
                case "manage_goals": result = await CallWorkday("Manage_Goals", "Performance_Management", BuildManageGoalsBody(arguments)).ConfigureAwait(false); break;
                case "give_feedback": result = await CallWorkday("Give_Feedback", "Performance_Management", BuildGiveFeedbackBody(arguments)).ConfigureAwait(false); break;

                // Talent
                case "get_skills": result = await CallWorkday("Get_Skills", "Talent", BuildOptRefBody("Skill_Reference", "Skill_ID", arguments["skill_reference"])).ConfigureAwait(false); break;
                case "get_manage_skills": result = await CallWorkday("Get_Manage_Skills", "Talent", BuildRefBody("Worker_Reference", "Employee_ID", arguments["worker_reference"])).ConfigureAwait(false); break;
                case "get_succession_plans": result = await CallWorkday("Get_Succession_Plans", "Talent", BuildOptRefBody("Position_Reference", "Position_ID", arguments["position_reference"])).ConfigureAwait(false); break;
                case "get_talent_pools": result = await CallWorkday("Get_Talent_Pools", "Talent", BuildOptRefBody("Talent_Pool_Reference", "Talent_Pool_ID", arguments["talent_pool_reference"])).ConfigureAwait(false); break;
                case "get_development_items": result = await CallWorkday("Get_Development_Items", "Talent", BuildOptRefBody("Development_Item_Reference", "Development_Item_ID", arguments["development_item_reference"])).ConfigureAwait(false); break;
                case "get_work_experiences": result = await CallWorkday("Get_Work_Experiences", "Talent", BuildOptRefBody("Worker_Reference", "Employee_ID", arguments["worker_reference"])).ConfigureAwait(false); break;
                case "manage_skills": result = await CallWorkday("Manage_Skills", "Talent", BuildManageSkillsBody(arguments)).ConfigureAwait(false); break;

                // Learning
                case "get_learning_courses": result = await CallWorkday("Get_Learning_Courses", "Learning", BuildOptRefBody("Learning_Course_Reference", "Learning_Course_ID", arguments["course_reference"])).ConfigureAwait(false); break;
                case "get_learning_enrollments": result = await CallWorkday("Get_Learning_Enrollments", "Learning", BuildOptRefBody("Worker_Reference", "Employee_ID", arguments["worker_reference"])).ConfigureAwait(false); break;
                case "get_learning_course_offerings": result = await CallWorkday("Get_Learning_Course_Offerings", "Learning", BuildOptRefBody("Learning_Course_Offering_Reference", "Learning_Course_Offering_ID", arguments["course_offering_reference"])).ConfigureAwait(false); break;
                case "get_learning_programs": result = await CallWorkday("Get_Learning_Programs", "Learning", BuildOptRefBody("Learning_Program_Reference", "Learning_Program_ID", arguments["program_reference"])).ConfigureAwait(false); break;

                // Payroll Interface
                case "get_payees": result = await CallWorkday("Get_Payees", "Payroll_Interface", BuildOptRefBody("Worker_Reference", "Employee_ID", arguments["worker_reference"])).ConfigureAwait(false); break;
                case "get_worker_costing_allocations": result = await CallWorkday("Get_Worker_Costing_Allocations", "Payroll_Interface", BuildOptRefBody("Worker_Reference", "Employee_ID", arguments["worker_reference"])).ConfigureAwait(false); break;
                case "get_external_payroll_inputs": result = await CallWorkday("Get_External_Payroll_Inputs", "Payroll_Interface", BuildOptRefBody("Worker_Reference", "Employee_ID", arguments["worker_reference"])).ConfigureAwait(false); break;

                // Financial Management
                case "get_journals": result = await CallWorkday("Get_Journals", "Financial_Management", BuildOptRefBody("Journal_Reference", "Journal_Reference_ID", arguments["journal_reference"])).ConfigureAwait(false); break;
                case "get_workday_companies": result = await CallWorkday("Get_Workday_Companies", "Financial_Management", BuildOptRefBody("Company_Reference", "Company_Reference_ID", arguments["company_reference"])).ConfigureAwait(false); break;
                case "get_cost_centers": result = await CallWorkday("Get_Cost_Centers", "Financial_Management", BuildOptRefBody("Cost_Center_Reference", "Cost_Center_Reference_ID", arguments["cost_center_reference"])).ConfigureAwait(false); break;
                case "get_currency_conversion_rates": result = await CallWorkday("Get_Currency_Conversion_Rates", "Financial_Management", BuildGetCurrencyRatesBody(arguments)).ConfigureAwait(false); break;
                case "get_projects": result = await CallWorkday("Get_Basic_Projects", "Financial_Management", BuildOptRefBody("Project_Reference", "Project_ID", arguments["project_reference"])).ConfigureAwait(false); break;
                case "get_business_units": result = await CallWorkday("Get_Business_Units", "Financial_Management", BuildOptRefBody("Business_Unit_Reference", "Business_Unit_ID", arguments["business_unit_reference"])).ConfigureAwait(false); break;

                // Resource Management
                case "get_suppliers": result = await CallWorkday("Get_Suppliers", "Resource_Management", BuildOptRefBody("Supplier_Reference", "Supplier_ID", arguments["supplier_reference"])).ConfigureAwait(false); break;
                case "get_expense_reports": result = await CallWorkday("Get_Expense_Reports", "Resource_Management", BuildOptRefBody("Expense_Report_Reference", "Expense_Report_ID", arguments["expense_report_reference"])).ConfigureAwait(false); break;
                case "get_purchase_orders": result = await CallWorkday("Get_Purchase_Orders", "Resource_Management", BuildOptRefBody("Purchase_Order_Reference", "Purchase_Order_ID", arguments["purchase_order_reference"])).ConfigureAwait(false); break;
                case "get_supplier_invoices": result = await CallWorkday("Get_Supplier_Invoices", "Resource_Management", BuildOptRefBody("Supplier_Invoice_Reference", "Supplier_Invoice_ID", arguments["supplier_invoice_reference"])).ConfigureAwait(false); break;
                case "get_assets": result = await CallWorkday("Get_Assets", "Resource_Management", BuildOptRefBody("Asset_Reference", "Asset_ID", arguments["asset_reference"])).ConfigureAwait(false); break;
                case "get_requisitions": result = await CallWorkday("Get_Requisitions", "Resource_Management", BuildOptRefBody("Requisition_Reference", "Requisition_ID", arguments["requisition_reference"])).ConfigureAwait(false); break;

                default:
                    return CreateMcpResponse(id, new JObject
                    {
                        ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = $"Unknown tool: {toolName}" } },
                        ["isError"] = true
                    });
            }

            var duration = (DateTime.UtcNow - toolStart).TotalMilliseconds;
            await LogToAppInsights("McpToolResult", new Dictionary<string, string>
            {
                ["ToolName"] = toolName,
                ["Success"] = "true",
                ["DurationMs"] = duration.ToString("F0"),
                ["CorrelationId"] = _correlationId
            });

            return CreateMcpResponse(id, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject
                    {
                        ["type"] = "text",
                        ["text"] = result.ToString(Newtonsoft.Json.Formatting.Indented)
                    }
                },
                ["isError"] = false
            });
        }
        catch (Exception ex)
        {
            await LogToAppInsights("McpToolError", new Dictionary<string, string>
            {
                ["ToolName"] = toolName,
                ["Error"] = ex.Message,
                ["CorrelationId"] = _correlationId
            });

            return CreateMcpResponse(id, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject
                    {
                        ["type"] = "text",
                        ["text"] = $"Error executing {toolName}: {ex.Message}"
                    }
                },
                ["isError"] = true
            });
        }
    }

    #endregion

    #region SOAP Request Body Builders

    private string BuildGetWorkersBody(JObject args)
    {
        var sb = new StringBuilder();
        var workerRef = args["worker_reference"]?.ToString();
        var count = args["count"]?.Value<int?>() ?? 50;
        var page = args["page"]?.Value<int?>() ?? 1;

        if (!string.IsNullOrEmpty(workerRef))
        {
            sb.Append($@"
        <wd:Request_References>
            <wd:Worker_Reference>
                <wd:ID wd:type=""Employee_ID"">{EscapeXml(workerRef)}</wd:ID>
            </wd:Worker_Reference>
        </wd:Request_References>");
        }

        sb.Append($@"
        <wd:Response_Filter>
            <wd:Page>{page}</wd:Page>
            <wd:Count>{count}</wd:Count>
        </wd:Response_Filter>");

        return sb.ToString();
    }

    private string BuildGetOrganizationsBody(JObject args)
    {
        var sb = new StringBuilder();
        var orgRef = args["organization_reference"]?.ToString();
        var orgType = args["organization_type"]?.ToString();
        var includeInactive = args["include_inactive"]?.Value<bool?>() ?? false;

        if (!string.IsNullOrEmpty(orgRef))
        {
            sb.Append($@"
        <wd:Request_References>
            <wd:Organization_Reference>
                <wd:ID wd:type=""Organization_Reference_ID"">{EscapeXml(orgRef)}</wd:ID>
            </wd:Organization_Reference>
        </wd:Request_References>");
        }
        else
        {
            sb.Append("<wd:Request_Criteria>");
            if (!string.IsNullOrEmpty(orgType))
            {
                sb.Append($@"
            <wd:Organization_Type_Reference>
                <wd:ID wd:type=""Organization_Type_ID"">{EscapeXml(orgType)}</wd:ID>
            </wd:Organization_Type_Reference>");
            }
            if (includeInactive)
                sb.Append("<wd:Include_Inactive>true</wd:Include_Inactive>");
            sb.Append("</wd:Request_Criteria>");
        }

        return sb.ToString();
    }

    private string BuildGetLocationsBody(JObject args)
    {
        var locRef = args["location_reference"]?.ToString();
        var locName = args["location_name"]?.ToString();

        if (!string.IsNullOrEmpty(locRef))
        {
            return $@"
        <wd:Request_References>
            <wd:Location_Reference>
                <wd:ID wd:type=""Location_ID"">{EscapeXml(locRef)}</wd:ID>
            </wd:Location_Reference>
        </wd:Request_References>";
        }
        if (!string.IsNullOrEmpty(locName))
        {
            return $@"
        <wd:Request_Criteria>
            <wd:Location_Name>{EscapeXml(locName)}</wd:Location_Name>
        </wd:Request_Criteria>";
        }
        return "";
    }

    private string BuildRefBody(string refElement, string idType, JToken refValue)
    {
        var val = refValue?.ToString() ?? "";
        if (string.IsNullOrEmpty(val)) return "";
        return $@"
        <wd:Request_References>
            <wd:{refElement}>
                <wd:ID wd:type=""{idType}"">{EscapeXml(val)}</wd:ID>
            </wd:{refElement}>
        </wd:Request_References>";
    }

    private string BuildOptRefBody(string refElement, string idType, JToken refValue)
    {
        var val = refValue?.ToString() ?? "";
        if (string.IsNullOrEmpty(val)) return "";
        return BuildRefBody(refElement, idType, refValue);
    }

    private string BuildChangeWorkContactBody(JObject args)
    {
        var workerRef = args["worker_reference"]?.ToString() ?? "";
        var sb = new StringBuilder();
        sb.Append($@"
        <wd:Change_Work_Contact_Information_Data>
            <wd:Worker_Reference>
                <wd:ID wd:type=""Employee_ID"">{EscapeXml(workerRef)}</wd:ID>
            </wd:Worker_Reference>
            <wd:Work_Contact_Information_Data>");

        var email = args["email_address"]?.ToString();
        if (!string.IsNullOrEmpty(email))
        {
            sb.Append($@"
                <wd:Email_Address_Data>
                    <wd:Email_Address>{EscapeXml(email)}</wd:Email_Address>
                    <wd:Usage_Data wd:Public=""true"">
                        <wd:Type_Data wd:Primary=""true"">
                            <wd:Type_Reference>
                                <wd:ID wd:type=""Communication_Usage_Type_ID"">WORK</wd:ID>
                            </wd:Type_Reference>
                        </wd:Type_Data>
                    </wd:Usage_Data>
                </wd:Email_Address_Data>");
        }

        var phone = args["phone_number"]?.ToString();
        if (!string.IsNullOrEmpty(phone))
        {
            var deviceType = args["phone_device_type"]?.ToString() ?? "Mobile";
            sb.Append($@"
                <wd:Phone_Data>
                    <wd:Phone_Number>{EscapeXml(phone)}</wd:Phone_Number>
                    <wd:Phone_Device_Type_Reference>
                        <wd:ID wd:type=""Phone_Device_Type_ID"">{EscapeXml(deviceType)}</wd:ID>
                    </wd:Phone_Device_Type_Reference>
                    <wd:Usage_Data wd:Public=""true"">
                        <wd:Type_Data wd:Primary=""true"">
                            <wd:Type_Reference>
                                <wd:ID wd:type=""Communication_Usage_Type_ID"">WORK</wd:ID>
                            </wd:Type_Reference>
                        </wd:Type_Data>
                    </wd:Usage_Data>
                </wd:Phone_Data>");
        }

        sb.Append(@"
            </wd:Work_Contact_Information_Data>
        </wd:Change_Work_Contact_Information_Data>");
        return sb.ToString();
    }

    private string BuildChangeBusinessTitleBody(JObject args)
    {
        var workerRef = args["worker_reference"]?.ToString() ?? "";
        var title = args["business_title"]?.ToString() ?? "";
        return $@"
        <wd:Change_Business_Title_Data>
            <wd:Worker_Reference>
                <wd:ID wd:type=""Employee_ID"">{EscapeXml(workerRef)}</wd:ID>
            </wd:Worker_Reference>
            <wd:Business_Title_Data>
                <wd:Business_Title>{EscapeXml(title)}</wd:Business_Title>
            </wd:Business_Title_Data>
        </wd:Change_Business_Title_Data>";
    }

    private string BuildChangePreferredNameBody(JObject args)
    {
        var workerRef = args["worker_reference"]?.ToString() ?? "";
        var firstName = args["first_name"]?.ToString() ?? "";
        var lastName = args["last_name"]?.ToString() ?? "";
        return $@"
        <wd:Change_Preferred_Name_Data>
            <wd:Worker_Reference>
                <wd:ID wd:type=""Employee_ID"">{EscapeXml(workerRef)}</wd:ID>
            </wd:Worker_Reference>
            <wd:Name_Data>
                <wd:First_Name>{EscapeXml(firstName)}</wd:First_Name>
                <wd:Last_Name>{EscapeXml(lastName)}</wd:Last_Name>
            </wd:Name_Data>
        </wd:Change_Preferred_Name_Data>";
    }

    private string BuildMaintainContactInfoBody(JObject args)
    {
        var workerRef = args["worker_reference"]?.ToString() ?? "";
        var sb = new StringBuilder();
        sb.Append($@"
        <wd:Maintain_Contact_Information_for_Person_Event_Data>
            <wd:Worker_Reference>
                <wd:ID wd:type=""Employee_ID"">{EscapeXml(workerRef)}</wd:ID>
            </wd:Worker_Reference>
            <wd:Worker_Contact_Information_Data>");

        var email = args["email_address"]?.ToString();
        if (!string.IsNullOrEmpty(email))
        {
            sb.Append($@"
                <wd:Email_Address_Data>
                    <wd:Email_Address>{EscapeXml(email)}</wd:Email_Address>
                    <wd:Usage_Data wd:Public=""true"">
                        <wd:Type_Data wd:Primary=""true"">
                            <wd:Type_Reference>
                                <wd:ID wd:type=""Communication_Usage_Type_ID"">WORK</wd:ID>
                            </wd:Type_Reference>
                        </wd:Type_Data>
                    </wd:Usage_Data>
                </wd:Email_Address_Data>");
        }

        var phone = args["phone_number"]?.ToString();
        if (!string.IsNullOrEmpty(phone))
        {
            sb.Append($@"
                <wd:Phone_Data>
                    <wd:Phone_Number>{EscapeXml(phone)}</wd:Phone_Number>
                    <wd:Usage_Data wd:Public=""true"">
                        <wd:Type_Data wd:Primary=""true"">
                            <wd:Type_Reference>
                                <wd:ID wd:type=""Communication_Usage_Type_ID"">WORK</wd:ID>
                            </wd:Type_Reference>
                        </wd:Type_Data>
                    </wd:Usage_Data>
                </wd:Phone_Data>");
        }

        var addr1 = args["address_line_1"]?.ToString();
        if (!string.IsNullOrEmpty(addr1))
        {
            var city = args["city"]?.ToString() ?? "";
            var state = args["state"]?.ToString() ?? "";
            var postal = args["postal_code"]?.ToString() ?? "";
            var country = args["country"]?.ToString() ?? "US";
            sb.Append($@"
                <wd:Address_Data>
                    <wd:Country_Reference>
                        <wd:ID wd:type=""ISO_3166-1_Alpha-2_Code"">{EscapeXml(country)}</wd:ID>
                    </wd:Country_Reference>
                    <wd:Address_Line_Data wd:Type=""ADDRESS_LINE_1"">{EscapeXml(addr1)}</wd:Address_Line_Data>
                    <wd:Municipality>{EscapeXml(city)}</wd:Municipality>
                    <wd:Country_Region_Reference>
                        <wd:ID wd:type=""Country_Region_ID"">{EscapeXml(state)}</wd:ID>
                    </wd:Country_Region_Reference>
                    <wd:Postal_Code>{EscapeXml(postal)}</wd:Postal_Code>
                    <wd:Usage_Data wd:Public=""true"">
                        <wd:Type_Data wd:Primary=""true"">
                            <wd:Type_Reference>
                                <wd:ID wd:type=""Communication_Usage_Type_ID"">WORK</wd:ID>
                            </wd:Type_Reference>
                        </wd:Type_Data>
                    </wd:Usage_Data>
                </wd:Address_Data>");
        }

        sb.Append(@"
            </wd:Worker_Contact_Information_Data>
        </wd:Maintain_Contact_Information_for_Person_Event_Data>");
        return sb.ToString();
    }

    private string BuildManageGoalsBody(JObject args)
    {
        var workerRef = args["worker_reference"]?.ToString() ?? "";
        var goalName = args["goal_name"]?.ToString() ?? "";
        var goalDesc = args["goal_description"]?.ToString() ?? "";
        var dueDate = args["due_date"]?.ToString() ?? "";

        var sb = new StringBuilder();
        sb.Append($@"
        <wd:Manage_Goals_Data>
            <wd:Worker_Reference>
                <wd:ID wd:type=""Employee_ID"">{EscapeXml(workerRef)}</wd:ID>
            </wd:Worker_Reference>
            <wd:Goal_Data>
                <wd:Name>{EscapeXml(goalName)}</wd:Name>");
        if (!string.IsNullOrEmpty(goalDesc))
            sb.Append($"<wd:Description>{EscapeXml(goalDesc)}</wd:Description>");
        if (!string.IsNullOrEmpty(dueDate))
            sb.Append($"<wd:Due_Date>{EscapeXml(dueDate)}</wd:Due_Date>");
        sb.Append(@"
            </wd:Goal_Data>
        </wd:Manage_Goals_Data>");
        return sb.ToString();
    }

    private string BuildGiveFeedbackBody(JObject args)
    {
        var workerRef = args["worker_reference"]?.ToString() ?? "";
        var comment = args["feedback_comment"]?.ToString() ?? "";
        return $@"
        <wd:Give_Feedback_Data>
            <wd:Workers_to_Receive_Feedback_Reference>
                <wd:ID wd:type=""Employee_ID"">{EscapeXml(workerRef)}</wd:ID>
            </wd:Workers_to_Receive_Feedback_Reference>
            <wd:Feedback_Comment>{EscapeXml(comment)}</wd:Feedback_Comment>
        </wd:Give_Feedback_Data>";
    }

    private string BuildManageSkillsBody(JObject args)
    {
        var workerRef = args["worker_reference"]?.ToString() ?? "";
        var skillRef = args["skill_reference"]?.ToString() ?? "";
        return $@"
        <wd:Manage_Skills_Data>
            <wd:Worker_Reference>
                <wd:ID wd:type=""Employee_ID"">{EscapeXml(workerRef)}</wd:ID>
            </wd:Worker_Reference>
            <wd:Skill_Item_Data>
                <wd:Skill_Reference>
                    <wd:ID wd:type=""Skill_ID"">{EscapeXml(skillRef)}</wd:ID>
                </wd:Skill_Reference>
            </wd:Skill_Item_Data>
        </wd:Manage_Skills_Data>";
    }

    private string BuildGetCurrencyRatesBody(JObject args)
    {
        var rateType = args["currency_rate_type"]?.ToString();
        var fromCurrency = args["from_currency"]?.ToString();
        var toCurrency = args["to_currency"]?.ToString();
        if (string.IsNullOrEmpty(rateType) && string.IsNullOrEmpty(fromCurrency)) return "";

        var sb = new StringBuilder("<wd:Request_Criteria>");
        if (!string.IsNullOrEmpty(rateType))
        {
            sb.Append($@"
            <wd:Currency_Rate_Type_Reference>
                <wd:ID wd:type=""Currency_Rate_Type_ID"">{EscapeXml(rateType)}</wd:ID>
            </wd:Currency_Rate_Type_Reference>");
        }
        if (!string.IsNullOrEmpty(fromCurrency))
        {
            sb.Append($@"
            <wd:From_Currency_Reference>
                <wd:ID wd:type=""Currency_ID"">{EscapeXml(fromCurrency)}</wd:ID>
            </wd:From_Currency_Reference>");
        }
        if (!string.IsNullOrEmpty(toCurrency))
        {
            sb.Append($@"
            <wd:Target_Currency_Reference>
                <wd:ID wd:type=""Currency_ID"">{EscapeXml(toCurrency)}</wd:ID>
            </wd:Target_Currency_Reference>");
        }
        sb.Append("</wd:Request_Criteria>");
        return sb.ToString();
    }

    #endregion

    #region Workday SOAP Infrastructure

    private async Task<JObject> CallWorkday(string soapAction, string serviceName, string requestBody)
    {
        var soapUrl = $"{SoapBaseUrl}/{serviceName}/{ApiVersion}";
        var bearerToken = GetBearerToken();

        if (string.IsNullOrEmpty(bearerToken))
            throw new InvalidOperationException("No OAuth bearer token found. Ensure the connection is authenticated.");

        var soapEnvelope = BuildSoapEnvelope(soapAction, requestBody);
        var soapStartTime = DateTime.UtcNow;

        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(soapUrl));
        request.Content = new StringContent(soapEnvelope, Encoding.UTF8, "text/xml");
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/xml"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        var response = await this.Context.SendAsync(request, this.CancellationToken).ConfigureAwait(false);
        var responseContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        await LogToAppInsights("SoapRequestCompleted", new Dictionary<string, string>
        {
            ["SoapAction"] = soapAction,
            ["Service"] = serviceName,
            ["StatusCode"] = ((int)response.StatusCode).ToString(),
            ["Success"] = response.IsSuccessStatusCode.ToString(),
            ["DurationMs"] = (DateTime.UtcNow - soapStartTime).TotalMilliseconds.ToString("F0"),
            ["CorrelationId"] = _correlationId
        });

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Workday SOAP request failed ({response.StatusCode}): {TruncateForError(responseContent)}");
        }

        return ParseWorkdayResponse(responseContent, soapAction);
    }

    private string BuildSoapEnvelope(string soapAction, string body)
    {
        return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<soapenv:Envelope
    xmlns:soapenv=""http://schemas.xmlsoap.org/soap/envelope/""
    xmlns:wd=""urn:com.workday/bsvc"">
    <soapenv:Header/>
    <soapenv:Body>
        <wd:{soapAction}_Request wd:version=""{ApiVersion}"">
            {body}
        </wd:{soapAction}_Request>
    </soapenv:Body>
</soapenv:Envelope>";
    }

    private string GetBearerToken()
    {
        if (this.Context.Request.Headers.Authorization != null &&
            this.Context.Request.Headers.Authorization.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase))
        {
            return this.Context.Request.Headers.Authorization.Parameter ?? "";
        }
        return "";
    }

    private string EscapeXml(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }

    private string TruncateForError(string content)
    {
        if (string.IsNullOrEmpty(content)) return "(empty)";
        return content.Length > 2000 ? content.Substring(0, 2000) + "..." : content;
    }

    #endregion

    #region Response Parsing

    private JObject ParseWorkdayResponse(string soapResponse, string soapAction)
    {
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(soapResponse);

            var nsMgr = new XmlNamespaceManager(doc.NameTable);
            nsMgr.AddNamespace("soapenv", "http://schemas.xmlsoap.org/soap/envelope/");
            nsMgr.AddNamespace("wd", "urn:com.workday/bsvc");

            // Check for SOAP faults
            var faultNode = doc.SelectSingleNode("//soapenv:Fault", nsMgr);
            if (faultNode != null)
            {
                var faultString = faultNode.SelectSingleNode("faultstring")?.InnerText ?? "Unknown SOAP fault";
                var faultDetail = faultNode.SelectSingleNode("detail")?.InnerText ?? "";
                return new JObject
                {
                    ["success"] = false,
                    ["error"] = faultString,
                    ["detail"] = faultDetail
                };
            }

            // Find Response_Data or Response_Results
            var responseData = doc.SelectSingleNode("//*[local-name()='Response_Data']", nsMgr);
            var responseResults = doc.SelectSingleNode("//*[local-name()='Response_Results']", nsMgr);

            var result = new JObject { ["success"] = true };

            if (responseResults != null)
            {
                var totalResults = responseResults.SelectSingleNode("*[local-name()='Total_Results']")?.InnerText;
                var totalPages = responseResults.SelectSingleNode("*[local-name()='Total_Pages']")?.InnerText;
                var pageResults = responseResults.SelectSingleNode("*[local-name()='Page_Results']")?.InnerText;
                var currentPage = responseResults.SelectSingleNode("*[local-name()='Page']")?.InnerText;

                if (totalResults != null) result["totalResults"] = int.Parse(totalResults);
                if (totalPages != null) result["totalPages"] = int.Parse(totalPages);
                if (pageResults != null) result["pageResults"] = int.Parse(pageResults);
                if (currentPage != null) result["currentPage"] = int.Parse(currentPage);
            }

            if (responseData != null)
            {
                result["data"] = ConvertXmlToJson(responseData);
            }
            else
            {
                // For operations that return results directly (like Get_Server_Timestamp)
                var responseNode = doc.SelectSingleNode($"//*[local-name()='{soapAction}_Response']", nsMgr);
                if (responseNode != null)
                {
                    result["data"] = ConvertXmlToJson(responseNode);
                }
                else
                {
                    result["data"] = new JObject();
                    result["rawResponse"] = TruncateForError(soapResponse);
                }
            }

            return result;
        }
        catch (XmlException ex)
        {
            return new JObject
            {
                ["success"] = false,
                ["error"] = $"Failed to parse Workday XML response: {ex.Message}",
                ["rawResponse"] = TruncateForError(soapResponse)
            };
        }
    }

    private JToken ConvertXmlToJson(XmlNode node)
    {
        if (node == null) return JValue.CreateNull();

        // If node has no children or only text, return text
        if (!node.HasChildNodes)
            return node.InnerText ?? "";

        if (node.ChildNodes.Count == 1 && node.FirstChild.NodeType == XmlNodeType.Text)
            return node.InnerText;

        var obj = new JObject();

        // Group child elements by local name to detect arrays
        var groups = new Dictionary<string, List<XmlNode>>();
        foreach (XmlNode child in node.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            var name = child.LocalName;
            if (!groups.ContainsKey(name))
                groups[name] = new List<XmlNode>();
            groups[name].Add(child);
        }

        // Add attributes
        if (node.Attributes != null)
        {
            foreach (XmlAttribute attr in node.Attributes)
            {
                if (attr.NamespaceURI == "http://www.w3.org/2000/xmlns/" ||
                    attr.NamespaceURI == "urn:com.workday/bsvc") continue;
                obj["@" + attr.LocalName] = attr.Value;
            }
        }

        foreach (var group in groups)
        {
            if (group.Value.Count > 1)
            {
                var arr = new JArray();
                foreach (var childNode in group.Value)
                    arr.Add(ConvertXmlToJson(childNode));
                obj[group.Key] = arr;
            }
            else
            {
                obj[group.Key] = ConvertXmlToJson(group.Value[0]);
            }
        }

        return obj;
    }

    #endregion

    #region JSON-RPC Response Helpers

    private HttpResponseMessage CreateMcpResponse(JToken id, JObject result)
    {
        var response = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["result"] = result
        };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(response.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
    }

    private HttpResponseMessage CreateMcpError(JToken id, int code, string message, string data = null)
    {
        var error = new JObject
        {
            ["code"] = code,
            ["message"] = message
        };
        if (data != null)
            error["data"] = data;

        var response = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = error
        };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(response.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
    }

    #endregion

    #region Application Insights

    private async Task LogToAppInsights(string eventName, Dictionary<string, string> properties)
    {
        if (string.IsNullOrEmpty(APP_INSIGHTS_CONNECTION_STRING) || APP_INSIGHTS_CONNECTION_STRING.Contains("INSERT_YOUR"))
            return;

        try
        {
            var iKey = ExtractInstrumentationKey(APP_INSIGHTS_CONNECTION_STRING);
            var endpoint = ExtractIngestionEndpoint(APP_INSIGHTS_CONNECTION_STRING);
            if (string.IsNullOrEmpty(iKey)) return;

            if (!properties.ContainsKey("CorrelationId"))
                properties["CorrelationId"] = _correlationId ?? "";
            if (!properties.ContainsKey("ConnectorName"))
                properties["ConnectorName"] = ConnectorName;

            var telemetry = new JObject
            {
                ["name"] = $"Microsoft.ApplicationInsights.{iKey}.Event",
                ["time"] = DateTime.UtcNow.ToString("O"),
                ["iKey"] = iKey,
                ["data"] = new JObject
                {
                    ["baseType"] = "EventData",
                    ["baseData"] = new JObject
                    {
                        ["ver"] = 2,
                        ["name"] = eventName,
                        ["properties"] = JObject.FromObject(properties)
                    }
                }
            };

            var aiRequest = new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint.TrimEnd('/') + "/v2/track"));
            aiRequest.Content = new StringContent(
                telemetry.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json"
            );
            this.Context.SendAsync(aiRequest, this.CancellationToken).ConfigureAwait(false);
        }
        catch { /* Silent fail for telemetry */ }
    }

    private async Task LogExceptionToAppInsights(Exception ex)
    {
        if (string.IsNullOrEmpty(APP_INSIGHTS_CONNECTION_STRING) || APP_INSIGHTS_CONNECTION_STRING.Contains("INSERT_YOUR"))
            return;

        try
        {
            var iKey = ExtractInstrumentationKey(APP_INSIGHTS_CONNECTION_STRING);
            var endpoint = ExtractIngestionEndpoint(APP_INSIGHTS_CONNECTION_STRING);
            if (string.IsNullOrEmpty(iKey)) return;

            var telemetry = new JObject
            {
                ["name"] = $"Microsoft.ApplicationInsights.{iKey}.Exception",
                ["time"] = DateTime.UtcNow.ToString("O"),
                ["iKey"] = iKey,
                ["data"] = new JObject
                {
                    ["baseType"] = "ExceptionData",
                    ["baseData"] = new JObject
                    {
                        ["ver"] = 2,
                        ["exceptions"] = new JArray
                        {
                            new JObject
                            {
                                ["typeName"] = ex.GetType().FullName,
                                ["message"] = ex.Message,
                                ["stack"] = ex.StackTrace ?? ""
                            }
                        },
                        ["properties"] = new JObject
                        {
                            ["CorrelationId"] = _correlationId ?? "",
                            ["ConnectorName"] = ConnectorName,
                            ["OperationId"] = this.Context.OperationId ?? ""
                        }
                    }
                }
            };

            var aiRequest2 = new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint.TrimEnd('/') + "/v2/track"));
            aiRequest2.Content = new StringContent(
                telemetry.ToString(Newtonsoft.Json.Formatting.None),
                Encoding.UTF8,
                "application/json"
            );
            this.Context.SendAsync(aiRequest2, this.CancellationToken).ConfigureAwait(false);
        }
        catch { /* Silent fail for telemetry */ }
    }

    private string ExtractInstrumentationKey(string connectionString)
    {
        if (string.IsNullOrEmpty(connectionString)) return null;
        foreach (var part in connectionString.Split(';'))
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith("InstrumentationKey=", StringComparison.OrdinalIgnoreCase))
                return trimmed.Substring("InstrumentationKey=".Length);
        }
        return null;
    }

    private string ExtractIngestionEndpoint(string connectionString)
    {
        if (string.IsNullOrEmpty(connectionString)) return "https://dc.services.visualstudio.com/";
        foreach (var part in connectionString.Split(';'))
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith("IngestionEndpoint=", StringComparison.OrdinalIgnoreCase))
                return trimmed.Substring("IngestionEndpoint=".Length);
        }
        return "https://dc.services.visualstudio.com/";
    }

    #endregion
}
