using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Net;
using System.Text;

public class Script : ScriptBase
{
    private const string APP_INSIGHTS_CONNECTION_STRING = "";

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var request = this.Context.Request;
        var requestBody = await request.Content.ReadAsStringAsync();
        var startTime = DateTime.UtcNow;

        string method = null;
        JObject requestObject = null;
        var transformations = new List<string>();

        try
        {
            var parsed = JsonConvert.DeserializeObject(requestBody);
            if (parsed is JArray array && array.Count > 0)
            {
                requestObject = array[0] as JObject;
            }
            else if (parsed is JObject obj)
            {
                requestObject = obj;
            }

            if (requestObject != null)
            {
                method = requestObject["method"]?.ToString();
                
                // Normalize JSON-RPC id to integer
                NormalizeJsonRpcId(requestObject, transformations);
                
                // If method is missing, default to tools/list
                if (string.IsNullOrEmpty(method))
                {
                    requestObject["method"] = "tools/list";
                    transformations.Add("method:null->tools/list");
                    method = "tools/list";
                }

                // For initialize method, inject protocolVersion
                if (method == "initialize")
                {
                    var paramsObj = requestObject["params"] as JObject ?? new JObject();
                    var currentProtocolVersion = paramsObj["protocolVersion"]?.ToString();
                    if (currentProtocolVersion != "2025-06-18")
                    {
                        paramsObj["protocolVersion"] = "2025-06-18";
                        transformations.Add($"params.protocolVersion:{currentProtocolVersion ?? "null"}->2025-06-18");
                        requestObject["params"] = paramsObj;
                    }
                }

                // Update request body with transformations
                if (transformations.Count > 0)
                {
                    requestBody = JsonConvert.SerializeObject(requestObject);
                }
            }
        }
        catch
        {
            // Ignore parse errors and just forward the original payload
        }

        // Log original request
        await LogToAppInsights("OriginalRequest", new
        {
            Method = method,
            RequestBody = requestBody,
            UserAgent = request.Headers.UserAgent?.ToString(),
            Transformations = transformations
        });

        if (method == "notifications/initialized")
        {
            await LogToAppInsights("NotificationHandled", new
            {
                Method = "notifications/initialized",
                Action = "Return acknowledgment"
            });

            var ackResponse = new JObject { { "jsonrpc", "2.0" } };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonConvert.SerializeObject(ackResponse), Encoding.UTF8, "application/json")
            };
        }

        if (method == "prompts/list")
        {
            await LogToAppInsights("PromptsListHandled", new { Method = "prompts/list" });

            var promptResponse = new JObject
            {
                { "jsonrpc", "2.0" },
                { "id", requestObject?["id"] ?? 1 },
                { "result", new JObject
                    {
                        { "prompts", new JArray(
                            new JObject { { "name", "generate_select" }, { "description", "Generate a SELECT query for a table" } },
                            new JObject { { "name", "analyze_schema" }, { "description", "Analyze table schema and suggest optimizations" } },
                            new JObject { { "name", "aggregate_query" }, { "description", "Write aggregation query with GROUP BY" } },
                            new JObject { { "name", "join_tables" }, { "description", "Create join query between tables" } },
                            new JObject { { "name", "count_distinct" }, { "description", "Count distinct values in column" } },
                            new JObject { { "name", "running_total" }, { "description", "Create running total with window functions" } },
                            new JObject { { "name", "date_range_query" }, { "description", "Query records in date range" } },
                            new JObject { { "name", "time_series" }, { "description", "Aggregate data by time period" } },
                            new JObject { { "name", "ranking" }, { "description", "Rank rows with window functions" } },
                            new JObject { { "name", "data_quality_check" }, { "description", "Check for NULLs, duplicates, outliers" } },
                            new JObject { { "name", "json_parsing" }, { "description", "Parse and extract from JSON column" } },
                            new JObject { { "name", "query_optimization" }, { "description", "Optimize slow queries" } },
                            new JObject { { "name", "data_validation" }, { "description", "Create validation queries" } },
                            new JObject { { "name", "copy_into_table" }, { "description", "Load data from stage into table" } },
                            new JObject { { "name", "create_stream" }, { "description", "Create CDC stream on table" } },
                            new JObject { { "name", "create_task" }, { "description", "Create scheduled task" } },
                            new JObject { { "name", "flatten_nested" }, { "description", "Flatten arrays or VARIANT data" } },
                            new JObject { { "name", "qualify_filtering" }, { "description", "Filter window results with QUALIFY" } },
                            new JObject { { "name", "try_casting" }, { "description", "Safe type casting without errors" } },
                            new JObject { { "name", "incremental_load" }, { "description", "Design incremental load logic" } },
                            new JObject { { "name", "percentile_analysis" }, { "description", "Calculate percentiles for distribution analysis" } },
                            new JObject { { "name", "string_operations" }, { "description", "Apply string transformations" } },
                            new JObject { { "name", "regex_pattern" }, { "description", "Extract patterns with regex" } },
                            new JObject { { "name", "lag_lead" }, { "description", "Compare with previous/next rows" } },
                            new JObject { { "name", "null_analysis" }, { "description", "Analyze NULL values in columns" } },
                            new JObject { { "name", "array_operations" }, { "description", "Flatten or aggregate array data" } },
                            new JObject { { "name", "clustering_recommendation" }, { "description", "Suggest clustering keys" } },
                            new JObject { { "name", "materialized_view" }, { "description", "Create materialized view" } },
                            new JObject { { "name", "type_conversion" }, { "description", "Convert column types" } },
                            new JObject { { "name", "create_table" }, { "description", "Create table with schema" } },
                            new JObject { { "name", "grants_management" }, { "description", "Manage grants and privileges" } },
                            new JObject { { "name", "create_pipe" }, { "description", "Configure Snowpipe for auto-ingestion" } },
                            new JObject { { "name", "create_warehouse" }, { "description", "Create and configure warehouse" } },
                            new JObject { { "name", "text_search" }, { "description", "Search text with case-insensitive matching" } },
                            new JObject { { "name", "age_calculation" }, { "description", "Calculate age or duration from dates" } },
                            new JObject { { "name", "cardinality_analysis" }, { "description", "Analyze column cardinality and distribution" } },
                            new JObject { { "name", "vector_similarity" }, { "description", "Find similar vectors using distance metrics" } },
                            new JObject { { "name", "nested_object_query" }, { "description", "Query nested OBJECT data" } },
                            new JObject { { "name", "array_indexing" }, { "description", "Access array elements by index" } },
                            new JObject { { "name", "alter_table_add_column" }, { "description", "Add column to existing table" } },
                            new JObject { { "name", "copy_into_location" }, { "description", "Unload data to stage" } },
                            new JObject { { "name", "create_file_format" }, { "description", "Define file format (CSV/JSON/Parquet)" } },
                            new JObject { { "name", "date_truncation" }, { "description", "Truncate dates for time-based rollups" } },
                            new JObject { { "name", "timezone_conversion" }, { "description", "Convert between timezones" } },
                            new JObject { { "name", "split_to_table" }, { "description", "Split delimited text into rows" } },
                            new JObject { { "name", "object_construct_agg" }, { "description", "Build objects from rows" } },
                            new JObject { { "name", "ntile_bucketing" }, { "description", "Bucket rows into N quantiles" } },
                            new JObject { { "name", "conditional_logic" }, { "description", "Apply IF/CASE conditional expressions" } },
                            new JObject { { "name", "geospatial_query" }, { "description", "Query geospatial data with ST_ functions" } },
                            new JObject { { "name", "ml_forecast" }, { "description", "Time series forecasting with ML" } },
                            new JObject { { "name", "external_table" }, { "description", "Create external table on cloud storage" } },
                            new JObject { { "name", "secure_view" }, { "description", "Create secure view for data masking" } },
                            new JObject { { "name", "row_access_policy" }, { "description", "Implement row-level security" } },
                            new JObject { { "name", "data_sharing" }, { "description", "Share data across accounts" } },
                            new JObject { { "name", "query_profile" }, { "description", "Analyze query performance" } },
                            new JObject { { "name", "merge_statement" }, { "description", "Upsert with MERGE INTO" } },
                            new JObject { { "name", "pivot_unpivot" }, { "description", "Pivot or unpivot data" } },
                            new JObject { { "name", "stored_procedure" }, { "description", "Create stored procedure" } },
                            new JObject { { "name", "user_defined_function" }, { "description", "Create UDF in SQL/JavaScript/Python" } },
                            new JObject { { "name", "transaction_control" }, { "description", "BEGIN/COMMIT/ROLLBACK transactions" } },
                            new JObject { { "name", "resource_monitor" }, { "description", "Create resource monitor for cost control" } },
                            new JObject { { "name", "network_policy" }, { "description", "Configure network access policies" } },
                            new JObject { { "name", "stage_management" }, { "description", "Create and manage stages" } },
                            new JObject { { "name", "time_travel_query" }, { "description", "Query historical data with AT/BEFORE" } },
                            new JObject { { "name", "zero_copy_clone" }, { "description", "Clone table/schema/database instantly" } },
                            new JObject { { "name", "cortex_sentiment" }, { "description", "Analyze sentiment with Snowflake Cortex AI" } },
                            new JObject { { "name", "cortex_summarize" }, { "description", "Summarize text with Cortex AI" } },
                            new JObject { { "name", "cortex_translate" }, { "description", "Translate text with Cortex AI" } },
                            new JObject { { "name", "dynamic_table" }, { "description", "Create auto-refreshing dynamic table" } },
                            new JObject { { "name", "search_optimization" }, { "description", "Enable search optimization service" } },
                            new JObject { { "name", "tag_based_masking" }, { "description", "Apply masking policies with tags" } },
                            new JObject { { "name", "replication_setup" }, { "description", "Set up database replication" } },
                            new JObject { { "name", "failover_group" }, { "description", "Configure failover groups" } },
                            new JObject { { "name", "account_usage_query" }, { "description", "Query account usage views" } },
                            new JObject { { "name", "cost_analysis" }, { "description", "Analyze warehouse and storage costs" } },
                            new JObject { { "name", "query_history" }, { "description", "Analyze query history and patterns" } },
                            new JObject { { "name", "notification_integration" }, { "description", "Configure event notifications" } },
                            new JObject { { "name", "session_policy" }, { "description", "Create session timeout policies" } },
                            new JObject { { "name", "data_classification" }, { "description", "Tag and classify sensitive data" } },
                            new JObject { { "name", "warehouse_autoscaling" }, { "description", "Configure auto-scaling for warehouses" } },
                            new JObject { { "name", "result_cache_query" }, { "description", "Optimize for result cache usage" } },
                            new JObject { { "name", "data_retention_policy" }, { "description", "Set retention and lifecycle policies" } },
                            new JObject { { "name", "audit_log_query" }, { "description", "Query access and audit logs" } },
                            new JObject { { "name", "sequence_generator" }, { "description", "Create and use sequences" } },
                            new JObject { { "name", "alert_creation" }, { "description", "Create alerts for monitoring" } },
                            new JObject { { "name", "data_lineage" }, { "description", "Track data lineage and dependencies" } },
                            new JObject { { "name", "cortex_complete" }, { "description", "Use Cortex LLM completion" } },
                            new JObject { { "name", "unstructured_data" }, { "description", "Query unstructured data with @" } },
                            new JObject { { "name", "snowpark_udf" }, { "description", "Create Snowpark UDF (Python/Java/Scala)" } },
                            new JObject { { "name", "change_tracking" }, { "description", "Enable change tracking on tables" } },
                            new JObject { { "name", "parameter_tuning" }, { "description", "Optimize session parameters" } },
                            new JObject { { "name", "hybrid_table" }, { "description", "Create hybrid tables for low-latency" } },
                            new JObject { { "name", "kafka_connector" }, { "description", "Configure Kafka connector" } },
                            new JObject { { "name", "table_sampling" }, { "description", "Sample data with TABLESAMPLE" } },
                            new JObject { { "name", "window_frame_clause" }, { "description", "Use window frames (ROWS/RANGE BETWEEN)" } },
                            new JObject { { "name", "recursive_cte" }, { "description", "Build recursive common table expressions" } },
                            new JObject { { "name", "table_constraints" }, { "description", "Define primary/foreign key constraints" } },
                            new JObject { { "name", "data_masking_policy" }, { "description", "Create column-level masking policies" } },
                            new JObject { { "name", "schema_evolution" }, { "description", "Handle schema changes in pipelines" } },
                            new JObject { { "name", "lateral_flatten" }, { "description", "Use LATERAL FLATTEN for arrays" } },
                            new JObject { { "name", "multicluster_warehouse" }, { "description", "Configure multi-cluster warehouses" } },
                            new JObject { { "name", "identity_column" }, { "description", "Create auto-increment identity columns" } },
                            new JObject { { "name", "variance_analysis" }, { "description", "Calculate variance and standard deviation" } },
                            new JObject { { "name", "iceberg_table" }, { "description", "Create Apache Iceberg tables" } },
                            new JObject { { "name", "data_clean_room" }, { "description", "Configure data clean rooms for privacy-preserving analytics" } },
                            new JObject { { "name", "native_app" }, { "description", "Create Snowflake Native App" } },
                            new JObject { { "name", "container_service" }, { "description", "Deploy container services" } },
                            new JObject { { "name", "query_acceleration" }, { "description", "Enable query acceleration service" } },
                            new JObject { { "name", "automatic_clustering" }, { "description", "Enable automatic clustering" } },
                            new JObject { { "name", "column_lineage" }, { "description", "Track column-level lineage" } },
                            new JObject { { "name", "budget_monitor" }, { "description", "Create budget-based resource monitor" } },
                            new JObject { { "name", "sql_variables" }, { "description", "Use SQL variables and session state" } },
                            new JObject { { "name", "table_function" }, { "description", "Create user-defined table functions (UDTF)" } },
                            new JObject { { "name", "external_access" }, { "description", "Configure external access integrations" } },
                            new JObject { { "name", "secrets_management" }, { "description", "Manage secrets and credentials" } },
                            new JObject { { "name", "oauth_integration" }, { "description", "Configure OAuth integrations" } },
                            new JObject { { "name", "warehouse_resource_optimization" }, { "description", "Optimize warehouse resource usage" } },
                            new JObject { { "name", "schema_detection" }, { "description", "Auto-detect schema from semi-structured data" } },
                            new JObject { { "name", "stream_on_view" }, { "description", "Create streams on views" } },
                            new JObject { { "name", "task_graph" }, { "description", "Build task dependency graphs (DAGs)" } },
                            new JObject { { "name", "data_metric_function" }, { "description", "Create data metric functions" } },
                            new JObject { { "name", "aggregation_policy" }, { "description", "Define aggregation policies" } },
                            new JObject { { "name", "projection_policy" }, { "description", "Create projection policies" } },
                            new JObject { { "name", "external_function" }, { "description", "Create external functions with API integration" } },
                            new JObject { { "name", "git_integration" }, { "description", "Integrate with Git repositories" } },
                            new JObject { { "name", "streamlit_app" }, { "description", "Create Snowflake Streamlit apps" } },
                            new JObject { { "name", "document_ai" }, { "description", "Extract data from documents with Document AI" } },
                            new JObject { { "name", "email_notification" }, { "description", "Configure email notifications" } },
                            new JObject { { "name", "data_encryption" }, { "description", "Configure encryption and key management" } },
                            new JObject { { "name", "object_tagging_automation" }, { "description", "Automate object tagging" } },
                            new JObject { { "name", "warehouse_suspension" }, { "description", "Configure warehouse auto-suspend policies" } },
                            new JObject { { "name", "java_udf" }, { "description", "Create Java UDFs" } },
                            new JObject { { "name", "connection_pooling" }, { "description", "Optimize connection pooling" } },
                            new JObject { { "name", "iceberg_partitioning" }, { "description", "Configure Iceberg table partitioning" } },
                            new JObject { { "name", "privilege_analysis" }, { "description", "Analyze privilege grants and access" } },
                            new JObject { { "name", "reference_column" }, { "description", "Create reference columns for relationships" } },
                            new JObject { { "name", "lateral_view" }, { "description", "Create lateral views for complex queries" } },
                            new JObject { { "name", "data_governance" }, { "description", "Implement data governance framework" } },
                            new JObject { { "name", "federation" }, { "description", "Set up Snowflake federation" } },
                            new JObject { { "name", "snowpipe_notification" }, { "description", "Configure Snowpipe notification channels" } },
                            new JObject { { "name", "dynamic_schema_evolution" }, { "description", "Handle dynamic schema changes" } },
                            new JObject { { "name", "api_integration" }, { "description", "Create API integrations" } },
                            new JObject { { "name", "cost_allocation_tags" }, { "description", "Tag resources for cost allocation" } },
                            new JObject { { "name", "materialized_view_mode" }, { "description", "Configure materialized view refresh modes" } },
                            new JObject { { "name", "multi_statement_task" }, { "description", "Create multi-statement tasks" } },
                            new JObject { { "name", "stage_encryption" }, { "description", "Configure stage-level encryption" } },
                            new JObject { { "name", "dynamic_warehouse_sizing" }, { "description", "Configure dynamic warehouse scaling" } },
                            new JObject { { "name", "row_filtering_policy" }, { "description", "Create row filtering policies" } },
                            new JObject { { "name", "view_clustering_key" }, { "description", "Recommend clustering keys for views" } },
                            new JObject { { "name", "query_result_cache_optimization" }, { "description", "Optimize query result caching" } },
                            new JObject { { "name", "join_optimization" }, { "description", "Optimize join performance" } },
                            new JObject { { "name", "subquery_flattening" }, { "description", "Enable subquery flattening" } },
                            new JObject { { "name", "partition_pruning" }, { "description", "Enable partition pruning" } },
                            new JObject { { "name", "cross_cluster_replication" }, { "description", "Set up cross-cluster replication" } },
                            new JObject { { "name", "budget_alert_policy" }, { "description", "Create budget alert policies" } },
                            new JObject { { "name", "user_provisioning" }, { "description", "Automate user provisioning" } },
                            new JObject { { "name", "password_policy" }, { "description", "Create password policies" } },
                            new JObject { { "name", "session_timeout_policy" }, { "description", "Configure session timeout policies" } },
                            new JObject { { "name", "ip_allowlist" }, { "description", "Configure IP allowlist" } },
                            new JObject { { "name", "vault_integration" }, { "description", "Integrate with secret vaults" } },
                            new JObject { { "name", "okta_integration" }, { "description", "Configure Okta SSO integration" } },
                            new JObject { { "name", "api_rate_limiting" }, { "description", "Configure API rate limiting" } },
                            new JObject { { "name", "query_result_sharing" }, { "description", "Share query results with other accounts" } },
                            new JObject { { "name", "compute_pool" }, { "description", "Create and manage compute pools" } },
                            new JObject { { "name", "network_rule" }, { "description", "Create network security rules" } },
                            new JObject { { "name", "iceberg_metadata_pruning" }, { "description", "Optimize Iceberg metadata pruning" } },
                            new JObject { { "name", "query_queue" }, { "description", "Manage query queues" } },
                            new JObject { { "name", "sort_key_recommendation" }, { "description", "Get sort key recommendations" } },
                            new JObject { { "name", "column_statistics" }, { "description", "Analyze column statistics" } },
                            new JObject { { "name", "index_optimization" }, { "description", "Recommend index optimizations" } },
                            new JObject { { "name", "data_freshness" }, { "description", "Monitor data freshness metrics" } },
                            new JObject { { "name", "data_completeness" }, { "description", "Check data completeness" } },
                            new JObject { { "name", "query_dependency_analysis" }, { "description", "Analyze query dependencies" } },
                            new JObject { { "name", "delta_lake_integration" }, { "description", "Integrate with Delta Lake" } },
                            new JObject { { "name", "apache_spark_integration" }, { "description", "Integrate with Apache Spark" } },
                            new JObject { { "name", "gpu_acceleration" }, { "description", "Enable GPU acceleration" } },
                            new JObject { { "name", "anomaly_detection" }, { "description", "Detect data anomalies" } },
                            new JObject { { "name", "metadata_api" }, { "description", "Use Metadata API for catalog queries" } },
                            new JObject { { "name", "snowsight_dashboard" }, { "description", "Create Snowsight dashboards" } },
                            new JObject { { "name", "model_registry" }, { "description", "Manage model registry" } },
                            new JObject { { "name", "prediction_function" }, { "description", "Create prediction functions" } },
                            new JObject { { "name", "time_series_analysis" }, { "description", "Analyze time series data" } },
                            new JObject { { "name", "graph_database" }, { "description", "Use graph databases" } },
                            new JObject { { "name", "dynamic_sql" }, { "description", "Execute dynamic SQL queries" } },
                            new JObject { { "name", "stored_procedure_optimization" }, { "description", "Optimize stored procedures" } },
                            new JObject { { "name", "feature_store" }, { "description", "Implement feature store" } },
                            new JObject { { "name", "event_stream_integration" }, { "description", "Integrate event streaming" } },
                            new JObject { { "name", "marketplace_sharing" }, { "description", "Share data on Snowflake marketplace" } },
                            new JObject { { "name", "bulk_load_optimization" }, { "description", "Optimize bulk load operations" } },
                            new JObject { { "name", "incremental_update" }, { "description", "Implement incremental updates" } },
                            new JObject { { "name", "deduplication" }, { "description", "Remove duplicate records" } },
                            new JObject { { "name", "cdc_optimization" }, { "description", "Optimize CDC patterns" } },
                            new JObject { { "name", "table_evolution_tracking" }, { "description", "Track table schema evolution" } },
                            new JObject { { "name", "cost_tracking_department" }, { "description", "Track costs by department" } },
                            new JObject { { "name", "query_rewrite" }, { "description", "Rewrite queries for optimization" } },
                            new JObject { { "name", "materialized_view_refresh" }, { "description", "Configure materialized view refresh strategies" } },
                            new JObject { { "name", "workload_management" }, { "description", "Manage workload groups" } },
                            new JObject { { "name", "query_acceleration_analysis" }, { "description", "Analyze query acceleration effectiveness" } },
                            new JObject { { "name", "column_masking_review" }, { "description", "Review masking policies" } },
                            new JObject { { "name", "share_monitoring" }, { "description", "Monitor data shares" } },
                            new JObject { { "name", "replication_lag_monitoring" }, { "description", "Monitor replication lag" } },
                            new JObject { { "name", "warehouse_efficiency" }, { "description", "Analyze warehouse efficiency" } },
                            new JObject { { "name", "query_plan_analysis" }, { "description", "Analyze query execution plans" } },
                            new JObject { { "name", "cardinality_estimation" }, { "description", "Improve cardinality estimation" } },
                            new JObject { { "name", "memory_optimization" }, { "description", "Optimize memory usage" } },
                            new JObject { { "name", "spill_optimization" }, { "description", "Minimize query spill to disk" } },
                            new JObject { { "name", "failover_testing" }, { "description", "Test failover procedures" } },
                            new JObject { { "name", "stored_procedure_validation" }, { "description", "Validate stored procedure parameters" } },
                            new JObject { { "name", "view_dependency_analysis" }, { "description", "Analyze view dependencies" } },
                            new JObject { { "name", "query_caching_strategy" }, { "description", "Optimize query caching strategy" } },
                            new JObject { { "name", "materialized_view_staleness" }, { "description", "Monitor materialized view staleness" } },
                            new JObject { { "name", "load_pattern_analysis" }, { "description", "Analyze data load patterns" } },
                            new JObject { { "name", "warehouse_queue_analysis" }, { "description", "Analyze warehouse queue depth" } },
                            new JObject { { "name", "query_complexity_metrics" }, { "description", "Calculate query complexity metrics" } },
                            new JObject { { "name", "data_pipeline_monitoring" }, { "description", "Monitor data pipeline health" } },
                            new JObject { { "name", "sla_monitoring" }, { "description", "Monitor SLAs and KPIs" } },
                            new JObject { { "name", "performance_baseline" }, { "description", "Establish performance baselines" } },
                            new JObject { { "name", "cost_anomaly_detection" }, { "description", "Detect cost anomalies" } },
                            new JObject { { "name", "resource_contention" }, { "description", "Analyze resource contention" } },
                            new JObject { { "name", "backup_recovery_strategy" }, { "description", "Plan backup and recovery" } },
                            new JObject { { "name", "disaster_recovery_drill" }, { "description", "Execute disaster recovery drill" } },
                            new JObject { { "name", "multi_region_deployment" }, { "description", "Deploy across multiple regions" } },
                            new JObject { { "name", "application_event_table" }, { "description", "Create and manage application event tables" } },
                            new JObject { { "name", "query_result_partitioning" }, { "description", "Partition query results" } },
                            new JObject { { "name", "time_travel_analysis" }, { "description", "Analyze historical data with time-travel" } },
                            new JObject { { "name", "schema_comparison" }, { "description", "Compare schemas across environments" } },
                            new JObject { { "name", "data_contract_validation" }, { "description", "Validate data contracts" } },
                            new JObject { { "name", "schema_registry" }, { "description", "Integrate with schema registry" } },
                            new JObject { { "name", "service_principal" }, { "description", "Create and manage service principals" } },
                            new JObject { { "name", "periodic_table_refresh" }, { "description", "Configure periodic table refresh" } },
                            new JObject { { "name", "aggregation_performance" }, { "description", "Optimize aggregation performance" } },
                            new JObject { { "name", "clone_consistency_check" }, { "description", "Verify clone consistency" } },
                            new JObject { { "name", "workload_isolation" }, { "description", "Isolate workloads" } },
                            new JObject { { "name", "tenant_isolation_pattern" }, { "description", "Implement tenant isolation" } },
                            new JObject { { "name", "data_sampling_strategy" }, { "description", "Define data sampling strategies" } },
                            new JObject { { "name", "quality_metrics_reporting" }, { "description", "Report data quality metrics" } },
                            new JObject { { "name", "security_posture_analysis" }, { "description", "Analyze security posture" } },
                            new JObject { { "name", "query_fingerprinting" }, { "description", "Fingerprint and group similar queries" } },
                            new JObject { { "name", "performance_regression_detection" }, { "description", "Detect query performance regressions" } },
                            new JObject { { "name", "metadata_version_tracking" }, { "description", "Track metadata versions" } },
                            new JObject { { "name", "storage_optimization" }, { "description", "Optimize storage usage" } },
                            new JObject { { "name", "table_growth_trending" }, { "description", "Track table growth trends" } },
                            new JObject { { "name", "concurrent_query_analysis" }, { "description", "Analyze concurrent query impacts" } },
                            new JObject { { "name", "result_cache_tuning" }, { "description", "Tune result caching settings" } },
                            new JObject { { "name", "materialized_view_sustainability" }, { "description", "Monitor materialized view sustainability" } },
                            new JObject { { "name", "dynamic_pruning" }, { "description", "Enable dynamic partition pruning" } },
                            new JObject { { "name", "lateral_join_optimization" }, { "description", "Optimize lateral joins" } },
                            new JObject { { "name", "bloom_filter_tuning" }, { "description", "Tune bloom filter parameters" } },
                            new JObject { { "name", "dictionary_encoding" }, { "description", "Enable dictionary encoding" } },
                            new JObject { { "name", "compression_analysis" }, { "description", "Analyze compression ratios" } },
                            new JObject { { "name", "data_skew_detection" }, { "description", "Detect data skew issues" } },
                            new JObject { { "name", "parallel_execution_tuning" }, { "description", "Tune parallel execution" } },
                            new JObject { { "name", "query_semantic_parsing" }, { "description", "Parse query semantic meaning" } },
                            new JObject { { "name", "join_selectivity" }, { "description", "Analyze join selectivity" } },
                            new JObject { { "name", "estimated_execution_time" }, { "description", "Estimate query execution time" } },
                            new JObject { { "name", "partitioning_strategy" }, { "description", "Design optimal partitioning strategy" } },
                            new JObject { { "name", "index_selectivity" }, { "description", "Analyze index selectivity" } },
                            new JObject { { "name", "predicate_pushdown" }, { "description", "Enable predicate pushdown" } },
                            new JObject { { "name", "statistics_collection" }, { "description", "Collect column statistics" } },
                            new JObject { { "name", "query_plan_caching" }, { "description", "Cache query execution plans" } },
                            new JObject { { "name", "cost_based_optimization" }, { "description", "Enable cost-based optimization" } },
                            new JObject { { "name", "adaptive_query_execution" }, { "description", "Enable adaptive query execution" } },
                            new JObject { { "name", "vectorization" }, { "description", "Enable SIMD vectorization" } },
                            new JObject { { "name", "null_handling_optimization" }, { "description", "Optimize NULL handling" } },
                            new JObject { { "name", "operator_ordering" }, { "description", "Optimize operator ordering" } },
                            new JObject { { "name", "expression_simplification" }, { "description", "Simplify query expressions" } },
                            new JObject { { "name", "aggregate_function_optimization" }, { "description", "Optimize aggregate functions" } },
                            new JObject { { "name", "window_function_optimization" }, { "description", "Optimize window functions" } },
                            new JObject { { "name", "subquery_optimization" }, { "description", "Optimize subqueries" } },
                            new JObject { { "name", "cte_optimization" }, { "description", "Optimize common table expressions" } },
                            new JObject { { "name", "correlated_subquery" }, { "description", "Handle correlated subqueries" } },
                            new JObject { { "name", "scalar_function_inlining" }, { "description", "Inline scalar functions" } },
                            new JObject { { "name", "join_reordering" }, { "description", "Optimize join reordering" } },
                            new JObject { { "name", "table_elimination" }, { "description", "Eliminate unnecessary tables" } },
                            new JObject { { "name", "constant_folding" }, { "description", "Enable constant folding" } },
                            new JObject { { "name", "transitive_closure" }, { "description", "Apply transitive closure optimization" } },
                            new JObject { { "name", "redundant_join_removal" }, { "description", "Remove redundant joins" } },
                            new JObject { { "name", "view_merging" }, { "description", "Merge views into queries" } },
                            new JObject { { "name", "grouping_set_optimization" }, { "description", "Optimize grouping sets" } },
                            new JObject { { "name", "sort_elimination" }, { "description", "Eliminate unnecessary sorts" } },
                            new JObject { { "name", "distinct_elimination" }, { "description", "Eliminate unnecessary distincts" } },
                            new JObject { { "name", "unused_column_elimination" }, { "description", "Eliminate unused columns" } },
                            new JObject { { "name", "semi_join_optimization" }, { "description", "Optimize semi-joins" } },
                            new JObject { { "name", "anti_join_optimization" }, { "description", "Optimize anti-joins" } },
                            new JObject { { "name", "outer_join_optimization" }, { "description", "Optimize outer joins" } },
                            new JObject { { "name", "hash_aggregate_optimization" }, { "description", "Optimize hash aggregates" } },
                            new JObject { { "name", "sort_aggregate_optimization" }, { "description", "Optimize sort aggregates" } },
                            new JObject { { "name", "limit_pushdown" }, { "description", "Push limits down in queries" } },
                            new JObject { { "name", "early_filtering" }, { "description", "Apply early filtering" } },
                            new JObject { { "name", "expression_elimination" }, { "description", "Eliminate redundant expressions" } },
                            new JObject { { "name", "constraint_propagation" }, { "description", "Propagate constraints through query" } },
                            new JObject { { "name", "null_propagation" }, { "description", "Optimize NULL propagation" } },
                            new JObject { { "name", "boolean_simplification" }, { "description", "Simplify boolean expressions" } },
                            new JObject { { "name", "function_elimination" }, { "description", "Eliminate unnecessary functions" } },
                            new JObject { { "name", "type_coercion_optimization" }, { "description", "Optimize type coercions" } },
                            new JObject { { "name", "collation_optimization" }, { "description", "Optimize collation handling" } },
                            new JObject { { "name", "temporal_optimization" }, { "description", "Optimize temporal queries" } },
                            new JObject { { "name", "implicit_casting_analysis" }, { "description", "Analyze implicit casting" } },
                            new JObject { { "name", "type_inference" }, { "description", "Infer data types" } },
                            new JObject { { "name", "data_type_compatibility" }, { "description", "Check type compatibility" } },
                            new JObject { { "name", "precision_loss_detection" }, { "description", "Detect precision loss" } },
                            new JObject { { "name", "overflow_checking" }, { "description", "Check for overflow conditions" } },
                            new JObject { { "name", "rounding_modes" }, { "description", "Configure rounding modes" } },
                            new JObject { { "name", "interval_arithmetic" }, { "description", "Perform interval arithmetic" } },
                            new JObject { { "name", "date_arithmetic" }, { "description", "Perform date arithmetic" } },
                            new JObject { { "name", "timestamp_precision" }, { "description", "Handle timestamp precision" } },
                            new JObject { { "name", "timezone_aware_operations" }, { "description", "Handle timezone-aware operations" } },
                            new JObject { { "name", "locale_specific_operations" }, { "description", "Handle locale-specific operations" } },
                            new JObject { { "name", "string_encoding" }, { "description", "Handle string encoding" } },
                            new JObject { { "name", "character_set_handling" }, { "description", "Handle character sets" } },
                            new JObject { { "name", "unicode_normalization" }, { "description", "Normalize Unicode strings" } },
                            new JObject { { "name", "collation_comparison" }, { "description", "Compare collations" } },
                            new JObject { { "name", "lock_management" }, { "description", "Manage row and table locks" } },
                            new JObject { { "name", "transaction_isolation" }, { "description", "Configure transaction isolation levels" } },
                            new JObject { { "name", "deadlock_detection" }, { "description", "Detect and handle deadlocks" } },
                            new JObject { { "name", "optimistic_locking" }, { "description", "Implement optimistic locking" } },
                            new JObject { { "name", "pessimistic_locking" }, { "description", "Implement pessimistic locking" } },
                            new JObject { { "name", "distributed_transactions" }, { "description", "Handle distributed transactions" } },
                            new JObject { { "name", "two_phase_commit" }, { "description", "Implement two-phase commit" } },
                            new JObject { { "name", "savepoint_management" }, { "description", "Manage transaction savepoints" } },
                            new JObject { { "name", "cursor_management" }, { "description", "Manage database cursors" } },
                            new JObject { { "name", "fetch_optimization" }, { "description", "Optimize fetch operations" } },
                            new JObject { { "name", "prefetching" }, { "description", "Configure result prefetching" } },
                            new JObject { { "name", "streaming_results" }, { "description", "Stream query results" } },
                            new JObject { { "name", "pagination" }, { "description", "Implement result pagination" } },
                            new JObject { { "name", "batch_processing" }, { "description", "Configure batch processing" } },
                            new JObject { { "name", "connection_pool_optimization" }, { "description", "Optimize connection pool settings" } },
                            new JObject { { "name", "error_handling" }, { "description", "Implement error handling in queries" } },
                            new JObject { { "name", "exception_management" }, { "description", "Manage exceptions in procedures" } },
                            new JObject { { "name", "query_logging" }, { "description", "Configure query logging" } },
                            new JObject { { "name", "debug_mode" }, { "description", "Enable debug mode and tracing" } },
                            new JObject { { "name", "monitoring_setup" }, { "description", "Set up monitoring infrastructure" } },
                            new JObject { { "name", "alert_rules" }, { "description", "Create alert rules" } },
                            new JObject { { "name", "unit_testing" }, { "description", "Create unit tests for UDFs" } },
                            new JObject { { "name", "integration_testing" }, { "description", "Create integration tests" } },
                            new JObject { { "name", "data_validation_framework" }, { "description", "Build data validation framework" } },
                            new JObject { { "name", "documentation_generation" }, { "description", "Generate schema documentation" } },
                            new JObject { { "name", "migration_planning" }, { "description", "Plan data migration strategy" } },
                            new JObject { { "name", "capacity_planning" }, { "description", "Plan warehouse capacity" } },
                            new JObject { { "name", "resource_allocation" }, { "description", "Allocate computing resources" } },
                            new JObject { { "name", "integration_patterns" }, { "description", "Implement integration patterns" } },
                            new JObject { { "name", "compliance_framework" }, { "description", "Build compliance framework" } },
                            new JObject { { "name", "query_tuning_advisor" }, { "description", "Get query tuning recommendations" } },
                            new JObject { { "name", "index_recommendation" }, { "description", "Get index recommendations" } },
                            new JObject { { "name", "hot_cold_partitioning" }, { "description", "Implement hot/cold data partitioning" } },
                            new JObject { { "name", "data_archival" }, { "description", "Archive historical data" } },
                            new JObject { { "name", "table_compression" }, { "description", "Configure table compression" } },
                            new JObject { { "name", "delta_merge" }, { "description", "Optimize delta merge operations" } },
                            new JObject { { "name", "slowly_changing_dimensions" }, { "description", "Implement slowly changing dimensions" } },
                            new JObject { { "name", "fact_table_optimization" }, { "description", "Optimize fact tables" } },
                            new JObject { { "name", "dimension_table_optimization" }, { "description", "Optimize dimension tables" } },
                            new JObject { { "name", "star_schema_design" }, { "description", "Design star schema" } },
                            new JObject { { "name", "snowflake_schema_design" }, { "description", "Design snowflake schema" } },
                            new JObject { { "name", "data_vault_modeling" }, { "description", "Implement data vault modeling" } },
                            new JObject { { "name", "lambda_architecture" }, { "description", "Implement lambda architecture" } },
                            new JObject { { "name", "kappa_architecture" }, { "description", "Implement kappa architecture" } },
                            new JObject { { "name", "medallion_architecture" }, { "description", "Implement medallion (bronze/silver/gold) architecture" } },
                            new JObject { { "name", "cicd_pipeline" }, { "description", "Set up CI/CD pipeline for Snowflake" } },
                            new JObject { { "name", "gitops_workflow" }, { "description", "Implement GitOps workflow" } },
                            new JObject { { "name", "infrastructure_as_code" }, { "description", "Define infrastructure as code" } },
                            new JObject { { "name", "blue_green_deployment" }, { "description", "Implement blue-green deployment" } },
                            new JObject { { "name", "canary_deployment" }, { "description", "Implement canary deployment" } },
                            new JObject { { "name", "feature_flags" }, { "description", "Manage feature flags" } },
                            new JObject { { "name", "observability_framework" }, { "description", "Build observability framework" } },
                            new JObject { { "name", "distributed_tracing" }, { "description", "Implement distributed tracing" } },
                            new JObject { { "name", "metric_collection" }, { "description", "Configure metric collection" } },
                            new JObject { { "name", "log_aggregation" }, { "description", "Set up log aggregation" } },
                            new JObject { { "name", "anomaly_alerting" }, { "description", "Configure anomaly alerting" } },
                            new JObject { { "name", "real_time_analytics" }, { "description", "Build real-time analytics" } },
                            new JObject { { "name", "stream_processing" }, { "description", "Implement stream processing" } },
                            new JObject { { "name", "event_sourcing" }, { "description", "Implement event sourcing pattern" } },
                            new JObject { { "name", "cqrs_pattern" }, { "description", "Implement CQRS pattern" } },
                            new JObject { { "name", "cortex_embeddings" }, { "description", "Generate text embeddings with Cortex" } },
                            new JObject { { "name", "cortex_image_classification" }, { "description", "Classify images with Cortex AI" } },
                            new JObject { { "name", "cortex_ocr" }, { "description", "Extract text from images with OCR" } },
                            new JObject { { "name", "vector_search_similarity" }, { "description", "Perform vector similarity search" } },
                            new JObject { { "name", "cortex_model_fine_tuning" }, { "description", "Fine-tune Cortex models" } },
                            new JObject { { "name", "cortex_batch_inference" }, { "description", "Run batch inference with Cortex" } },
                            new JObject { { "name", "cortex_model_monitoring" }, { "description", "Monitor Cortex model performance" } },
                            new JObject { { "name", "cortex_prompt_engineering" }, { "description", "Optimize prompts for Cortex LLMs" } },
                            new JObject { { "name", "cortex_entity_extraction" }, { "description", "Extract entities from text" } },
                            new JObject { { "name", "cortex_classification" }, { "description", "Classify text with Cortex" } },
                            new JObject { { "name", "vector_index_creation" }, { "description", "Create vector indexes" } },
                            new JObject { { "name", "approximate_nearest_neighbor" }, { "description", "Perform ANN search" } },
                            new JObject { { "name", "embedding_dimension_reduction" }, { "description", "Reduce embedding dimensions" } },
                            new JObject { { "name", "cortex_reranking" }, { "description", "Rerank search results with Cortex" } },
                            new JObject { { "name", "rag_pattern" }, { "description", "Implement RAG (Retrieval Augmented Generation)" } },
                            new JObject { { "name", "snowpark_dataframe_operations" }, { "description", "Perform Snowpark DataFrame operations" } },
                            new JObject { { "name", "snowpark_ml_pipeline" }, { "description", "Build Snowpark ML pipelines" } },
                            new JObject { { "name", "snowpark_custom_transformer" }, { "description", "Create custom Snowpark transformers" } },
                            new JObject { { "name", "snowpark_distributed_computing" }, { "description", "Implement distributed computing with Snowpark" } },
                            new JObject { { "name", "snowpark_python_specific" }, { "description", "Use Python-specific Snowpark features" } },
                            new JObject { { "name", "snowpark_scala_specific" }, { "description", "Use Scala-specific Snowpark features" } },
                            new JObject { { "name", "snowpark_java_specific" }, { "description", "Use Java-specific Snowpark features" } },
                            new JObject { { "name", "snowpark_feature_engineering" }, { "description", "Engineer features with Snowpark" } },
                            new JObject { { "name", "snowpark_model_training" }, { "description", "Train models with Snowpark" } },
                            new JObject { { "name", "snowpark_model_deployment" }, { "description", "Deploy models with Snowpark" } },
                            new JObject { { "name", "snowpark_batch_prediction" }, { "description", "Run batch predictions with Snowpark" } },
                            new JObject { { "name", "snowpark_data_preprocessing" }, { "description", "Preprocess data with Snowpark" } },
                            new JObject { { "name", "snowpark_cross_validation" }, { "description", "Perform cross-validation with Snowpark" } },
                            new JObject { { "name", "snowpark_hyperparameter_tuning" }, { "description", "Tune hyperparameters with Snowpark" } },
                            new JObject { { "name", "snowpark_model_registry_integration" }, { "description", "Integrate with model registry" } },
                            new JObject { { "name", "parquet_optimization" }, { "description", "Optimize Parquet file format" } },
                            new JObject { { "name", "avro_schema_evolution" }, { "description", "Manage Avro schema evolution" } },
                            new JObject { { "name", "orc_compression" }, { "description", "Optimize ORC compression" } },
                            new JObject { { "name", "delta_lake_integration" }, { "description", "Integrate with Delta Lake" } },
                            new JObject { { "name", "apache_hudi_integration" }, { "description", "Integrate with Apache Hudi" } },
                            new JObject { { "name", "json_compression" }, { "description", "Optimize JSON compression" } },
                            new JObject { { "name", "csv_optimization" }, { "description", "Optimize CSV file handling" } },
                            new JObject { { "name", "file_format_detection" }, { "description", "Auto-detect file formats" } },
                            new JObject { { "name", "columnar_storage_optimization" }, { "description", "Optimize columnar storage" } },
                            new JObject { { "name", "compression_codec_selection" }, { "description", "Select compression codec" } },
                            new JObject { { "name", "file_splitting_strategy" }, { "description", "Design file splitting strategy" } },
                            new JObject { { "name", "schema_inference" }, { "description", "Infer schema from data" } },
                            new JObject { { "name", "file_format_conversion" }, { "description", "Convert between file formats" } },
                            new JObject { { "name", "nested_data_optimization" }, { "description", "Optimize nested data structures" } },
                            new JObject { { "name", "data_lake_partitioning" }, { "description", "Design data lake partitioning" } },
                            new JObject { { "name", "attribute_based_access_control" }, { "description", "Implement ABAC" } },
                            new JObject { { "name", "dynamic_data_masking_policy" }, { "description", "Create dynamic masking policies" } },
                            new JObject { { "name", "pii_detection" }, { "description", "Detect PII in data" } },
                            new JObject { { "name", "sensitive_data_discovery" }, { "description", "Discover sensitive data" } },
                            new JObject { { "name", "encryption_key_rotation" }, { "description", "Rotate encryption keys" } },
                            new JObject { { "name", "external_tokenization" }, { "description", "Implement external tokenization" } },
                            new JObject { { "name", "data_loss_prevention" }, { "description", "Implement DLP policies" } },
                            new JObject { { "name", "audit_policy_creation" }, { "description", "Create audit policies" } },
                            new JObject { { "name", "session_variable_security" }, { "description", "Secure session variables" } },
                            new JObject { { "name", "oauth_integration" }, { "description", "Configure OAuth integration" } },
                            new JObject { { "name", "saml_sso_configuration" }, { "description", "Configure SAML SSO" } },
                            new JObject { { "name", "multi_factor_authentication" }, { "description", "Configure MFA" } },
                            new JObject { { "name", "privileged_access_management" }, { "description", "Implement PAM" } },
                            new JObject { { "name", "security_event_monitoring" }, { "description", "Monitor security events" } },
                            new JObject { { "name", "compliance_reporting" }, { "description", "Generate compliance reports" } },
                            new JObject { { "name", "organization_management" }, { "description", "Manage organization" } },
                            new JObject { { "name", "account_provisioning" }, { "description", "Provision accounts" } },
                            new JObject { { "name", "credit_allocation" }, { "description", "Allocate credits" } },
                            new JObject { { "name", "billing_analysis" }, { "description", "Analyze billing" } },
                            new JObject { { "name", "usage_tracking" }, { "description", "Track usage" } },
                            new JObject { { "name", "resource_quota_management" }, { "description", "Manage resource quotas" } },
                            new JObject { { "name", "account_replication_setup" }, { "description", "Setup account replication" } },
                            new JObject { { "name", "cross_region_connectivity" }, { "description", "Configure cross-region connectivity" } },
                            new JObject { { "name", "warehouse_policy_management" }, { "description", "Manage warehouse policies" } },
                            new JObject { { "name", "storage_policy_configuration" }, { "description", "Configure storage policies" } },
                            new JObject { { "name", "account_parameter_tuning" }, { "description", "Tune account parameters" } },
                            new JObject { { "name", "organization_billing_dashboard" }, { "description", "Create billing dashboard" } },
                            new JObject { { "name", "cost_center_allocation" }, { "description", "Allocate cost centers" } },
                            new JObject { { "name", "service_level_agreement" }, { "description", "Define SLA" } },
                            new JObject { { "name", "capacity_planning_analysis" }, { "description", "Analyze capacity planning" } },
                            new JObject { { "name", "healthcare_fhir_processing" }, { "description", "Process FHIR healthcare data" } },
                            new JObject { { "name", "financial_fraud_detection" }, { "description", "Detect financial fraud" } },
                            new JObject { { "name", "retail_customer_360" }, { "description", "Build retail customer 360" } },
                            new JObject { { "name", "manufacturing_quality_control" }, { "description", "Implement quality control" } },
                            new JObject { { "name", "iot_sensor_data_processing" }, { "description", "Process IoT sensor data" } },
                            new JObject { { "name", "telecommunications_cdr_analysis" }, { "description", "Analyze telecom CDR" } },
                            new JObject { { "name", "insurance_claims_processing" }, { "description", "Process insurance claims" } },
                            new JObject { { "name", "ecommerce_recommendation_engine" }, { "description", "Build recommendation engine" } },
                            new JObject { { "name", "supply_chain_optimization" }, { "description", "Optimize supply chain" } },
                            new JObject { { "name", "energy_smart_grid_analytics" }, { "description", "Analyze smart grid data" } },
                            new JObject { { "name", "media_content_analytics" }, { "description", "Analyze media content" } },
                            new JObject { { "name", "gaming_player_behavior" }, { "description", "Analyze player behavior" } },
                            new JObject { { "name", "automotive_telematics" }, { "description", "Process telematics data" } },
                            new JObject { { "name", "real_estate_market_analysis" }, { "description", "Analyze real estate market" } },
                            new JObject { { "name", "education_student_analytics" }, { "description", "Analyze student performance" } },
                            new JObject { { "name", "scd_type2_implementation" }, { "description", "Implement SCD Type 2" } },
                            new JObject { { "name", "scd_type3_implementation" }, { "description", "Implement SCD Type 3" } },
                            new JObject { { "name", "scd_type6_hybrid" }, { "description", "Implement SCD Type 6 hybrid" } },
                            new JObject { { "name", "bridge_table_design" }, { "description", "Design bridge tables" } },
                            new JObject { { "name", "factless_fact_table" }, { "description", "Create factless fact tables" } },
                            new JObject { { "name", "accumulating_snapshot_fact" }, { "description", "Create accumulating snapshot" } },
                            new JObject { { "name", "periodic_snapshot_fact" }, { "description", "Create periodic snapshot" } },
                            new JObject { { "name", "late_arriving_dimension" }, { "description", "Handle late-arriving dimensions" } },
                            new JObject { { "name", "incremental_dimension_load" }, { "description", "Load dimensions incrementally" } },
                            new JObject { { "name", "full_outer_merge" }, { "description", "Perform full outer merge" } },
                            new JObject { { "name", "upsert_pattern" }, { "description", "Implement upsert pattern" } },
                            new JObject { { "name", "soft_delete_pattern" }, { "description", "Implement soft delete" } },
                            new JObject { { "name", "data_pipeline_orchestration" }, { "description", "Orchestrate data pipelines" } },
                            new JObject { { "name", "dependency_management" }, { "description", "Manage pipeline dependencies" } },
                            new JObject { { "name", "error_recovery_pattern" }, { "description", "Implement error recovery" } },
                            new JObject { { "name", "idempotent_pipeline" }, { "description", "Create idempotent pipelines" } },
                            new JObject { { "name", "query_timeout_debugging" }, { "description", "Debug query timeouts" } },
                            new JObject { { "name", "memory_spill_analysis" }, { "description", "Analyze memory spills" } },
                            new JObject { { "name", "warehouse_contention" }, { "description", "Identify warehouse contention" } },
                            new JObject { { "name", "slow_query_diagnosis" }, { "description", "Diagnose slow queries" } },
                            new JObject { { "name", "lock_timeout_resolution" }, { "description", "Resolve lock timeouts" } },
                            new JObject { { "name", "session_hung_debugging" }, { "description", "Debug hung sessions" } },
                            new JObject { { "name", "out_of_memory_errors" }, { "description", "Troubleshoot OOM errors" } },
                            new JObject { { "name", "connection_pooling_issues" }, { "description", "Fix connection pooling" } },
                            new JObject { { "name", "network_latency_diagnosis" }, { "description", "Diagnose network latency" } },
                            new JObject { { "name", "authentication_failures" }, { "description", "Debug authentication failures" } },
                            new JObject { { "name", "permission_denied_errors" }, { "description", "Resolve permission errors" } },
                            new JObject { { "name", "data_type_mismatch" }, { "description", "Fix data type mismatches" } },
                            new JObject { { "name", "compilation_errors" }, { "description", "Debug compilation errors" } },
                            new JObject { { "name", "execution_plan_analysis" }, { "description", "Analyze execution plans" } },
                            new JObject { { "name", "bottleneck_identification" }, { "description", "Identify bottlenecks" } },
                            new JObject { { "name", "resource_exhaustion" }, { "description", "Debug resource exhaustion" } },
                            new JObject { { "name", "tableau_live_connection" }, { "description", "Setup Tableau live connection" } },
                            new JObject { { "name", "powerbi_direct_query" }, { "description", "Configure Power BI DirectQuery" } },
                            new JObject { { "name", "looker_pdt_optimization" }, { "description", "Optimize Looker PDTs" } },
                            new JObject { { "name", "dbt_incremental_model" }, { "description", "Create dbt incremental models" } },
                            new JObject { { "name", "dbt_snapshot" }, { "description", "Implement dbt snapshots" } },
                            new JObject { { "name", "dbt_test_creation" }, { "description", "Create dbt tests" } },
                            new JObject { { "name", "dbt_macro_development" }, { "description", "Develop dbt macros" } },
                            new JObject { { "name", "tableau_extract_refresh" }, { "description", "Refresh Tableau extracts" } },
                            new JObject { { "name", "powerbi_dataflow" }, { "description", "Setup Power BI dataflows" } },
                            new JObject { { "name", "looker_aggregate_table" }, { "description", "Create Looker aggregate tables" } },
                            new JObject { { "name", "superset_integration" }, { "description", "Integrate with Apache Superset" } },
                            new JObject { { "name", "metabase_query_optimization" }, { "description", "Optimize Metabase queries" } },
                            new JObject { { "name", "qlik_sense_integration" }, { "description", "Integrate with Qlik Sense" } },
                            new JObject { { "name", "sisense_elasticube" }, { "description", "Setup Sisense ElastiCube" } },
                            new JObject { { "name", "thoughtspot_embrace" }, { "description", "Configure ThoughtSpot Embrace" } },
                            new JObject { { "name", "visualization_best_practices" }, { "description", "BI visualization best practices" } },
                            new JObject { { "name", "multi_tenant_architecture" }, { "description", "Design multi-tenant architecture" } },
                            new JObject { { "name", "chargeback_model" }, { "description", "Implement chargeback model" } },
                            new JObject { { "name", "query_prioritization" }, { "description", "Prioritize queries" } },
                            new JObject { { "name", "workload_isolation" }, { "description", "Isolate workloads" } },
                            new JObject { { "name", "resource_governor" }, { "description", "Configure resource governor" } },
                            new JObject { { "name", "tenant_isolation_pattern" }, { "description", "Implement tenant isolation" } },
                            new JObject { { "name", "shared_schema_pattern" }, { "description", "Implement shared schema" } },
                            new JObject { { "name", "schema_per_tenant" }, { "description", "Implement schema-per-tenant" } },
                            new JObject { { "name", "database_per_tenant" }, { "description", "Implement database-per-tenant" } },
                            new JObject { { "name", "workload_classification" }, { "description", "Classify workloads" } },
                            new JObject { { "name", "queue_management" }, { "description", "Manage query queues" } },
                            new JObject { { "name", "concurrency_scaling_policy" }, { "description", "Configure concurrency scaling" } },
                            new JObject { { "name", "resource_group_management" }, { "description", "Manage resource groups" } },
                            new JObject { { "name", "tenant_onboarding" }, { "description", "Onboard new tenants" } },
                            new JObject { { "name", "tenant_offboarding" }, { "description", "Offboard tenants" } },
                            new JObject { { "name", "cross_tenant_analytics" }, { "description", "Perform cross-tenant analytics" } },
                            new JObject { { "name", "zero_downtime_migration" }, { "description", "Perform zero-downtime migration" } },
                            new JObject { { "name", "cross_cloud_replication" }, { "description", "Setup cross-cloud replication" } },
                            new JObject { { "name", "data_lakehouse_pattern" }, { "description", "Implement data lakehouse" } },
                            new JObject { { "name", "streaming_analytics" }, { "description", "Implement streaming analytics" } },
                            new JObject { { "name", "real_time_data_warehouse" }, { "description", "Build real-time warehouse" } },
                            new JObject { { "name", "modern_data_stack" }, { "description", "Design modern data stack" } },
                            new JObject { { "name", "reverse_etl" }, { "description", "Implement reverse ETL" } },
                            new JObject { { "name", "data_mesh_implementation" }, { "description", "Implement data mesh" } },
                            new JObject { { "name", "data_product_catalog" }, { "description", "Create data product catalog" } },
                            new JObject { { "name", "federated_governance" }, { "description", "Implement federated governance" } },
                            new JObject { { "name", "data_quality_framework" }, { "description", "Build data quality framework" } },
                            new JObject { { "name", "metadata_driven_pipeline" }, { "description", "Create metadata-driven pipeline" } },
                            new JObject { { "name", "schema_registry_integration" }, { "description", "Integrate schema registry" } },
                            new JObject { { "name", "data_versioning" }, { "description", "Implement data versioning" } },
                            new JObject { { "name", "active_active_deployment" }, { "description", "Setup active-active deployment" } },
                            new JObject { { "name", "comprehensive_monitoring" }, { "description", "Setup comprehensive monitoring" } }
                        )}
                    }
                }
            };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonConvert.SerializeObject(promptResponse), Encoding.UTF8, "application/json")
            };
        }

        if (method == "prompts/get")
        {
            var promptName = requestObject?["params"]?["name"]?.ToString();
            await LogToAppInsights("PromptsGetHandled", new { Method = "prompts/get", PromptName = promptName });

            var promptTemplates = new Dictionary<string, JObject>
            {
                { "generate_select", new JObject { { "name", "generate_select" }, { "description", "Generate SELECT query" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "analyze_schema", new JObject { { "name", "analyze_schema" }, { "description", "Analyze schema" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "aggregate_query", new JObject { { "name", "aggregate_query" }, { "description", "Aggregation query" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }, new JObject { { "name", "group_by_column" }, { "description", "Column to group by" }, { "required", true } }) } } },
                { "join_tables", new JObject { { "name", "join_tables" }, { "description", "Join query" }, { "arguments", new JArray(new JObject { { "name", "table1" }, { "description", "First table" }, { "required", true } }, new JObject { { "name", "table2" }, { "description", "Second table" }, { "required", true } }) } } },
                { "count_distinct", new JObject { { "name", "count_distinct" }, { "description", "Count distinct" }, { "arguments", new JArray(new JObject { { "name", "column" }, { "description", "Column name" }, { "required", true } }, new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "running_total", new JObject { { "name", "running_total" }, { "description", "Running total" }, { "arguments", new JArray(new JObject { { "name", "column" }, { "description", "Column to sum" }, { "required", true } }, new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }, new JObject { { "name", "order_by_column" }, { "description", "Order column" }, { "required", true } }) } } },
                { "date_range_query", new JObject { { "name", "date_range_query" }, { "description", "Date range query" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }, new JObject { { "name", "date_column" }, { "description", "Date column" }, { "required", true } }, new JObject { { "name", "start_date" }, { "description", "Start date" }, { "required", true } }, new JObject { { "name", "end_date" }, { "description", "End date" }, { "required", true } }) } } },
                { "time_series", new JObject { { "name", "time_series" }, { "description", "Time series aggregation" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }, new JObject { { "name", "time_period" }, { "description", "Time period" }, { "required", true } }, new JObject { { "name", "metric_column" }, { "description", "Metric column" }, { "required", true } }) } } },
                { "ranking", new JObject { { "name", "ranking" }, { "description", "Ranking query" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }, new JObject { { "name", "rank_column" }, { "description", "Rank column" }, { "required", true } }, new JObject { { "name", "partition_column" }, { "description", "Partition column" }, { "required", true } }) } } },
                { "data_quality_check", new JObject { { "name", "data_quality_check" }, { "description", "Data quality check" }, { "arguments", new JArray(new JObject { { "name", "column" }, { "description", "Column name" }, { "required", true } }, new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "json_parsing", new JObject { { "name", "json_parsing" }, { "description", "JSON parsing" }, { "arguments", new JArray(new JObject { { "name", "json_column" }, { "description", "JSON column" }, { "required", true } }, new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "query_optimization", new JObject { { "name", "query_optimization" }, { "description", "Query optimization" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "data_validation", new JObject { { "name", "data_validation" }, { "description", "Data validation" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "copy_into_table", new JObject { { "name", "copy_into_table" }, { "description", "COPY INTO table" }, { "arguments", new JArray(new JObject { { "name", "stage_name" }, { "description", "Stage name" }, { "required", true } }, new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "create_stream", new JObject { { "name", "create_stream" }, { "description", "Create CDC stream" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "create_task", new JObject { { "name", "create_task" }, { "description", "Create scheduled task" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "flatten_nested", new JObject { { "name", "flatten_nested" }, { "description", "Flatten nested data" }, { "arguments", new JArray(new JObject { { "name", "json_column" }, { "description", "JSON/VARIANT column" }, { "required", true } }) } } },
                { "qualify_filtering", new JObject { { "name", "qualify_filtering" }, { "description", "QUALIFY filtering" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "try_casting", new JObject { { "name", "try_casting" }, { "description", "Safe type casting" }, { "arguments", new JArray(new JObject { { "name", "column" }, { "description", "Column name" }, { "required", true } }, new JObject { { "name", "source_type" }, { "description", "Source type" }, { "required", true } }, new JObject { { "name", "target_type" }, { "description", "Target type" }, { "required", true } }) } } },
                { "incremental_load", new JObject { { "name", "incremental_load" }, { "description", "Incremental load design" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }, new JObject { { "name", "timestamp_column" }, { "description", "Timestamp column" }, { "required", true } }) } } },
                { "percentile_analysis", new JObject { { "name", "percentile_analysis" }, { "description", "Percentile analysis" }, { "arguments", new JArray(new JObject { { "name", "column" }, { "description", "Column name" }, { "required", true } }, new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "string_operations", new JObject { { "name", "string_operations" }, { "description", "String transformations" }, { "arguments", new JArray(new JObject { { "name", "column" }, { "description", "Column name" }, { "required", true } }, new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "regex_pattern", new JObject { { "name", "regex_pattern" }, { "description", "Regex extraction" }, { "arguments", new JArray(new JObject { { "name", "regex_pattern" }, { "description", "Regex pattern" }, { "required", true } }, new JObject { { "name", "column" }, { "description", "Column name" }, { "required", true } }, new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "lag_lead", new JObject { { "name", "lag_lead" }, { "description", "LAG/LEAD comparison" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "null_analysis", new JObject { { "name", "null_analysis" }, { "description", "NULL analysis" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "array_operations", new JObject { { "name", "array_operations" }, { "description", "Array operations" }, { "arguments", new JArray(new JObject { { "name", "column" }, { "description", "Array column" }, { "required", true } }, new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "clustering_recommendation", new JObject { { "name", "clustering_recommendation" }, { "description", "Clustering keys" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "materialized_view", new JObject { { "name", "materialized_view" }, { "description", "Materialized view" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "type_conversion", new JObject { { "name", "type_conversion" }, { "description", "Type conversion" }, { "arguments", new JArray(new JObject { { "name", "column" }, { "description", "Column name" }, { "required", true } }, new JObject { { "name", "source_type" }, { "description", "Source type" }, { "required", true } }, new JObject { { "name", "target_type" }, { "description", "Target type" }, { "required", true } }, new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "create_table", new JObject { { "name", "create_table" }, { "description", "Create table" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "grants_management", new JObject { { "name", "grants_management" }, { "description", "Manage grants" }, { "arguments", new JArray(new JObject { { "name", "object_name" }, { "description", "Object name" }, { "required", true } }, new JObject { { "name", "role_name" }, { "description", "Role name" }, { "required", true } }) } } },
                { "create_pipe", new JObject { { "name", "create_pipe" }, { "description", "Create Snowpipe" }, { "arguments", new JArray(new JObject { { "name", "stage_name" }, { "description", "Stage name" }, { "required", true } }, new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "create_warehouse", new JObject { { "name", "create_warehouse" }, { "description", "Create warehouse" }, { "arguments", new JArray(new JObject { { "name", "warehouse_name" }, { "description", "Warehouse name" }, { "required", true } }) } } },
                { "text_search", new JObject { { "name", "text_search" }, { "description", "Text search" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }, new JObject { { "name", "column" }, { "description", "Column to search" }, { "required", true } }, new JObject { { "name", "search_term" }, { "description", "Search term" }, { "required", true } }) } } },
                { "age_calculation", new JObject { { "name", "age_calculation" }, { "description", "Age calculation" }, { "arguments", new JArray(new JObject { { "name", "date_column" }, { "description", "Date column" }, { "required", true } }, new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "cardinality_analysis", new JObject { { "name", "cardinality_analysis" }, { "description", "Cardinality analysis" }, { "arguments", new JArray(new JObject { { "name", "column" }, { "description", "Column name" }, { "required", true } }, new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "vector_similarity", new JObject { { "name", "vector_similarity" }, { "description", "Vector similarity" }, { "arguments", new JArray(new JObject { { "name", "vector_column" }, { "description", "Vector column" }, { "required", true } }, new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "nested_object_query", new JObject { { "name", "nested_object_query" }, { "description", "Nested object query" }, { "arguments", new JArray(new JObject { { "name", "column" }, { "description", "OBJECT column" }, { "required", true } }, new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "array_indexing", new JObject { { "name", "array_indexing" }, { "description", "Array indexing" }, { "arguments", new JArray(new JObject { { "name", "column" }, { "description", "Array column" }, { "required", true } }, new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "alter_table_add_column", new JObject { { "name", "alter_table_add_column" }, { "description", "Add column" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }, new JObject { { "name", "column_name" }, { "description", "New column name" }, { "required", true } }, new JObject { { "name", "data_type" }, { "description", "Column data type" }, { "required", true } }) } } },
                { "copy_into_location", new JObject { { "name", "copy_into_location" }, { "description", "Unload to stage" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }, new JObject { { "name", "stage_name" }, { "description", "Stage name" }, { "required", true } }) } } },
                { "create_file_format", new JObject { { "name", "create_file_format" }, { "description", "Create file format" }, { "arguments", new JArray(new JObject { { "name", "format_name" }, { "description", "Format name" }, { "required", true } }) } } },
                { "date_truncation", new JObject { { "name", "date_truncation" }, { "description", "Date truncation" }, { "arguments", new JArray(new JObject { { "name", "date_column" }, { "description", "Date column" }, { "required", true } }, new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "timezone_conversion", new JObject { { "name", "timezone_conversion" }, { "description", "Timezone conversion" }, { "arguments", new JArray(new JObject { { "name", "timestamp_column" }, { "description", "Timestamp column" }, { "required", true } }) } } },
                { "split_to_table", new JObject { { "name", "split_to_table" }, { "description", "Split to table" }, { "arguments", new JArray(new JObject { { "name", "column" }, { "description", "Column to split" }, { "required", true } }, new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "object_construct_agg", new JObject { { "name", "object_construct_agg" }, { "description", "Build objects" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "ntile_bucketing", new JObject { { "name", "ntile_bucketing" }, { "description", "NTILE bucketing" }, { "arguments", new JArray(new JObject { { "name", "order_by_column" }, { "description", "Order column" }, { "required", true } }, new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "conditional_logic", new JObject { { "name", "conditional_logic" }, { "description", "Conditional logic" }, { "arguments", new JArray(new JObject { { "name", "column" }, { "description", "Column name" }, { "required", true } }, new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "geospatial_query", new JObject { { "name", "geospatial_query" }, { "description", "Geospatial query" }, { "arguments", new JArray(new JObject { { "name", "geometry_column" }, { "description", "Geometry column" }, { "required", true } }, new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "ml_forecast", new JObject { { "name", "ml_forecast" }, { "description", "ML forecasting" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }, new JObject { { "name", "timestamp_column" }, { "description", "Timestamp column" }, { "required", true } }, new JObject { { "name", "value_column" }, { "description", "Value column" }, { "required", true } }) } } },
                { "external_table", new JObject { { "name", "external_table" }, { "description", "External table" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }, new JObject { { "name", "stage_name" }, { "description", "Stage name" }, { "required", true } }) } } },
                { "secure_view", new JObject { { "name", "secure_view" }, { "description", "Secure view" }, { "arguments", new JArray(new JObject { { "name", "view_name" }, { "description", "View name" }, { "required", true } }, new JObject { { "name", "source_table" }, { "description", "Source table" }, { "required", true } }) } } },
                { "row_access_policy", new JObject { { "name", "row_access_policy" }, { "description", "Row access policy" }, { "arguments", new JArray(new JObject { { "name", "policy_name" }, { "description", "Policy name" }, { "required", true } }, new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "data_sharing", new JObject { { "name", "data_sharing" }, { "description", "Data sharing" }, { "arguments", new JArray(new JObject { { "name", "share_name" }, { "description", "Share name" }, { "required", true } }) } } },
                { "query_profile", new JObject { { "name", "query_profile" }, { "description", "Query profile" }, { "arguments", new JArray(new JObject { { "name", "query_id" }, { "description", "Query ID" }, { "required", true } }) } } },
                { "merge_statement", new JObject { { "name", "merge_statement" }, { "description", "MERGE upsert" }, { "arguments", new JArray(new JObject { { "name", "target_table" }, { "description", "Target table" }, { "required", true } }, new JObject { { "name", "source_table" }, { "description", "Source table" }, { "required", true } }) } } },
                { "pivot_unpivot", new JObject { { "name", "pivot_unpivot" }, { "description", "Pivot/Unpivot" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "stored_procedure", new JObject { { "name", "stored_procedure" }, { "description", "Stored procedure" }, { "arguments", new JArray(new JObject { { "name", "procedure_name" }, { "description", "Procedure name" }, { "required", true } }) } } },
                { "user_defined_function", new JObject { { "name", "user_defined_function" }, { "description", "UDF" }, { "arguments", new JArray(new JObject { { "name", "function_name" }, { "description", "Function name" }, { "required", true } }) } } },
                { "transaction_control", new JObject { { "name", "transaction_control" }, { "description", "Transaction control" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "resource_monitor", new JObject { { "name", "resource_monitor" }, { "description", "Resource monitor" }, { "arguments", new JArray(new JObject { { "name", "monitor_name" }, { "description", "Monitor name" }, { "required", true } }) } } },
                { "network_policy", new JObject { { "name", "network_policy" }, { "description", "Network policy" }, { "arguments", new JArray(new JObject { { "name", "policy_name" }, { "description", "Policy name" }, { "required", true } }) } } },
                { "stage_management", new JObject { { "name", "stage_management" }, { "description", "Stage management" }, { "arguments", new JArray(new JObject { { "name", "stage_name" }, { "description", "Stage name" }, { "required", true } }) } } },
                { "time_travel_query", new JObject { { "name", "time_travel_query" }, { "description", "Time travel query" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }, new JObject { { "name", "timestamp" }, { "description", "Timestamp or offset" }, { "required", true } }) } } },
                { "zero_copy_clone", new JObject { { "name", "zero_copy_clone" }, { "description", "Zero-copy clone" }, { "arguments", new JArray(new JObject { { "name", "source_object" }, { "description", "Source object" }, { "required", true } }, new JObject { { "name", "clone_name" }, { "description", "Clone name" }, { "required", true } }) } } },
                { "cortex_sentiment", new JObject { { "name", "cortex_sentiment" }, { "description", "Cortex sentiment" }, { "arguments", new JArray(new JObject { { "name", "text_column" }, { "description", "Text column" }, { "required", true } }, new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "cortex_summarize", new JObject { { "name", "cortex_summarize" }, { "description", "Cortex summarize" }, { "arguments", new JArray(new JObject { { "name", "text_column" }, { "description", "Text column" }, { "required", true } }, new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "cortex_translate", new JObject { { "name", "cortex_translate" }, { "description", "Cortex translate" }, { "arguments", new JArray(new JObject { { "name", "text_column" }, { "description", "Text column" }, { "required", true } }, new JObject { { "name", "target_language" }, { "description", "Target language" }, { "required", true } }, new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "dynamic_table", new JObject { { "name", "dynamic_table" }, { "description", "Dynamic table" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }, new JObject { { "name", "source_query" }, { "description", "Source query" }, { "required", true } }) } } },
                { "search_optimization", new JObject { { "name", "search_optimization" }, { "description", "Search optimization" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "tag_based_masking", new JObject { { "name", "tag_based_masking" }, { "description", "Tag-based masking" }, { "arguments", new JArray(new JObject { { "name", "tag_name" }, { "description", "Tag name" }, { "required", true } }, new JObject { { "name", "policy_name" }, { "description", "Policy name" }, { "required", true } }) } } },
                { "replication_setup", new JObject { { "name", "replication_setup" }, { "description", "Replication setup" }, { "arguments", new JArray(new JObject { { "name", "database_name" }, { "description", "Database name" }, { "required", true } }) } } },
                { "failover_group", new JObject { { "name", "failover_group" }, { "description", "Failover group" }, { "arguments", new JArray(new JObject { { "name", "group_name" }, { "description", "Group name" }, { "required", true } }) } } },
                { "account_usage_query", new JObject { { "name", "account_usage_query" }, { "description", "Account usage" }, { "arguments", new JArray(new JObject { { "name", "metric" }, { "description", "Metric to query" }, { "required", true } }) } } },
                { "cost_analysis", new JObject { { "name", "cost_analysis" }, { "description", "Cost analysis" }, { "arguments", new JArray(new JObject { { "name", "time_period" }, { "description", "Time period" }, { "required", true } }) } } },
                { "query_history", new JObject { { "name", "query_history" }, { "description", "Query history" }, { "arguments", new JArray(new JObject { { "name", "time_range" }, { "description", "Time range" }, { "required", true } }) } } },
                { "notification_integration", new JObject { { "name", "notification_integration" }, { "description", "Notification integration" }, { "arguments", new JArray(new JObject { { "name", "integration_name" }, { "description", "Integration name" }, { "required", true } }) } } },
                { "session_policy", new JObject { { "name", "session_policy" }, { "description", "Session policy" }, { "arguments", new JArray(new JObject { { "name", "policy_name" }, { "description", "Policy name" }, { "required", true } }) } } },
                { "data_classification", new JObject { { "name", "data_classification" }, { "description", "Data classification" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "warehouse_autoscaling", new JObject { { "name", "warehouse_autoscaling" }, { "description", "Warehouse auto-scaling" }, { "arguments", new JArray(new JObject { { "name", "warehouse_name" }, { "description", "Warehouse name" }, { "required", true } }) } } },
                { "result_cache_query", new JObject { { "name", "result_cache_query" }, { "description", "Result cache optimization" }, { "arguments", new JArray(new JObject { { "name", "query_pattern" }, { "description", "Query pattern" }, { "required", true } }) } } },
                { "data_retention_policy", new JObject { { "name", "data_retention_policy" }, { "description", "Data retention" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }, new JObject { { "name", "retention_days" }, { "description", "Retention days" }, { "required", true } }) } } },
                { "audit_log_query", new JObject { { "name", "audit_log_query" }, { "description", "Audit log query" }, { "arguments", new JArray(new JObject { { "name", "time_range" }, { "description", "Time range" }, { "required", true } }) } } },
                { "sequence_generator", new JObject { { "name", "sequence_generator" }, { "description", "Sequence generator" }, { "arguments", new JArray(new JObject { { "name", "sequence_name" }, { "description", "Sequence name" }, { "required", true } }) } } },
                { "alert_creation", new JObject { { "name", "alert_creation" }, { "description", "Alert creation" }, { "arguments", new JArray(new JObject { { "name", "alert_name" }, { "description", "Alert name" }, { "required", true } }, new JObject { { "name", "condition" }, { "description", "Alert condition" }, { "required", true } }) } } },
                { "data_lineage", new JObject { { "name", "data_lineage" }, { "description", "Data lineage" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "cortex_complete", new JObject { { "name", "cortex_complete" }, { "description", "Cortex LLM completion" }, { "arguments", new JArray(new JObject { { "name", "prompt" }, { "description", "Prompt text" }, { "required", true } }, new JObject { { "name", "model" }, { "description", "Model name" }, { "required", true } }) } } },
                { "unstructured_data", new JObject { { "name", "unstructured_data" }, { "description", "Unstructured data query" }, { "arguments", new JArray(new JObject { { "name", "stage_path" }, { "description", "Stage path" }, { "required", true } }) } } },
                { "snowpark_udf", new JObject { { "name", "snowpark_udf" }, { "description", "Snowpark UDF" }, { "arguments", new JArray(new JObject { { "name", "function_name" }, { "description", "Function name" }, { "required", true } }, new JObject { { "name", "language" }, { "description", "Language (Python/Java/Scala)" }, { "required", true } }) } } },
                { "change_tracking", new JObject { { "name", "change_tracking" }, { "description", "Change tracking" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "parameter_tuning", new JObject { { "name", "parameter_tuning" }, { "description", "Parameter tuning" }, { "arguments", new JArray(new JObject { { "name", "parameter_name" }, { "description", "Parameter name" }, { "required", true } }) } } },
                { "hybrid_table", new JObject { { "name", "hybrid_table" }, { "description", "Hybrid table" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "kafka_connector", new JObject { { "name", "kafka_connector" }, { "description", "Kafka connector" }, { "arguments", new JArray(new JObject { { "name", "connector_name" }, { "description", "Connector name" }, { "required", true } }) } } },
                { "table_sampling", new JObject { { "name", "table_sampling" }, { "description", "Table sampling" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }, new JObject { { "name", "sample_method" }, { "description", "Sample method (ROW/BLOCK)" }, { "required", true } }) } } },
                { "window_frame_clause", new JObject { { "name", "window_frame_clause" }, { "description", "Window frame" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "recursive_cte", new JObject { { "name", "recursive_cte" }, { "description", "Recursive CTE" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "table_constraints", new JObject { { "name", "table_constraints" }, { "description", "Table constraints" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "data_masking_policy", new JObject { { "name", "data_masking_policy" }, { "description", "Masking policy" }, { "arguments", new JArray(new JObject { { "name", "policy_name" }, { "description", "Policy name" }, { "required", true } }, new JObject { { "name", "column_type" }, { "description", "Column data type" }, { "required", true } }) } } },
                { "schema_evolution", new JObject { { "name", "schema_evolution" }, { "description", "Schema evolution" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "lateral_flatten", new JObject { { "name", "lateral_flatten" }, { "description", "LATERAL FLATTEN" }, { "arguments", new JArray(new JObject { { "name", "array_column" }, { "description", "Array column" }, { "required", true } }, new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "multicluster_warehouse", new JObject { { "name", "multicluster_warehouse" }, { "description", "Multi-cluster warehouse" }, { "arguments", new JArray(new JObject { { "name", "warehouse_name" }, { "description", "Warehouse name" }, { "required", true } }) } } },
                { "identity_column", new JObject { { "name", "identity_column" }, { "description", "Identity column" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "variance_analysis", new JObject { { "name", "variance_analysis" }, { "description", "Variance analysis" }, { "arguments", new JArray(new JObject { { "name", "column" }, { "description", "Numeric column" }, { "required", true } }, new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "iceberg_table", new JObject { { "name", "iceberg_table" }, { "description", "Apache Iceberg table" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }, new JObject { { "name", "catalog" }, { "description", "Catalog integration" }, { "required", true } }) } } },
                { "data_clean_room", new JObject { { "name", "data_clean_room" }, { "description", "Data clean room" }, { "arguments", new JArray(new JObject { { "name", "clean_room_name" }, { "description", "Clean room name" }, { "required", true } }) } } },
                { "native_app", new JObject { { "name", "native_app" }, { "description", "Native app" }, { "arguments", new JArray(new JObject { { "name", "app_name" }, { "description", "App name" }, { "required", true } }) } } },
                { "container_service", new JObject { { "name", "container_service" }, { "description", "Container service" }, { "arguments", new JArray(new JObject { { "name", "service_name" }, { "description", "Service name" }, { "required", true } }, new JObject { { "name", "image" }, { "description", "Container image" }, { "required", true } }) } } },
                { "query_acceleration", new JObject { { "name", "query_acceleration" }, { "description", "Query acceleration" }, { "arguments", new JArray(new JObject { { "name", "warehouse_name" }, { "description", "Warehouse name" }, { "required", true } }) } } },
                { "automatic_clustering", new JObject { { "name", "automatic_clustering" }, { "description", "Automatic clustering" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "column_lineage", new JObject { { "name", "column_lineage" }, { "description", "Column lineage" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }, new JObject { { "name", "column_name" }, { "description", "Column name" }, { "required", true } }) } } },
                { "budget_monitor", new JObject { { "name", "budget_monitor" }, { "description", "Budget monitor" }, { "arguments", new JArray(new JObject { { "name", "monitor_name" }, { "description", "Monitor name" }, { "required", true } }, new JObject { { "name", "budget_amount" }, { "description", "Budget amount" }, { "required", true } }) } } },
                { "sql_variables", new JObject { { "name", "sql_variables" }, { "description", "SQL variables" }, { "arguments", new JArray(new JObject { { "name", "variable_name" }, { "description", "Variable name" }, { "required", true } }) } } },
                { "table_function", new JObject { { "name", "table_function" }, { "description", "Table function (UDTF)" }, { "arguments", new JArray(new JObject { { "name", "function_name" }, { "description", "Function name" }, { "required", true } }) } } },
                { "external_access", new JObject { { "name", "external_access" }, { "description", "External access" }, { "arguments", new JArray(new JObject { { "name", "integration_name" }, { "description", "Integration name" }, { "required", true } }, new JObject { { "name", "endpoint_url" }, { "description", "Endpoint URL" }, { "required", true } }) } } },
                { "secrets_management", new JObject { { "name", "secrets_management" }, { "description", "Secrets management" }, { "arguments", new JArray(new JObject { { "name", "secret_name" }, { "description", "Secret name" }, { "required", true } }) } } },
                { "oauth_integration", new JObject { { "name", "oauth_integration" }, { "description", "OAuth integration" }, { "arguments", new JArray(new JObject { { "name", "integration_name" }, { "description", "Integration name" }, { "required", true } }) } } },
                { "warehouse_resource_optimization", new JObject { { "name", "warehouse_resource_optimization" }, { "description", "Warehouse optimization" }, { "arguments", new JArray(new JObject { { "name", "warehouse_name" }, { "description", "Warehouse name" }, { "required", true } }) } } },
                { "schema_detection", new JObject { { "name", "schema_detection" }, { "description", "Schema detection" }, { "arguments", new JArray(new JObject { { "name", "stage_path" }, { "description", "Stage path" }, { "required", true } }, new JObject { { "name", "file_format" }, { "description", "File format" }, { "required", true } }) } } },
                { "stream_on_view", new JObject { { "name", "stream_on_view" }, { "description", "Stream on view" }, { "arguments", new JArray(new JObject { { "name", "view_name" }, { "description", "View name" }, { "required", true } }) } } },
                { "task_graph", new JObject { { "name", "task_graph" }, { "description", "Task graph" }, { "arguments", new JArray(new JObject { { "name", "root_task_name" }, { "description", "Root task name" }, { "required", true } }) } } },
                { "data_metric_function", new JObject { { "name", "data_metric_function" }, { "description", "Data metric function" }, { "arguments", new JArray(new JObject { { "name", "metric_name" }, { "description", "Metric name" }, { "required", true } }, new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "aggregation_policy", new JObject { { "name", "aggregation_policy" }, { "description", "Aggregation policy" }, { "arguments", new JArray(new JObject { { "name", "policy_name" }, { "description", "Policy name" }, { "required", true } }) } } },
                { "projection_policy", new JObject { { "name", "projection_policy" }, { "description", "Projection policy" }, { "arguments", new JArray(new JObject { { "name", "policy_name" }, { "description", "Policy name" }, { "required", true } }) } } },
                { "external_function", new JObject { { "name", "external_function" }, { "description", "External function" }, { "arguments", new JArray(new JObject { { "name", "function_name" }, { "description", "Function name" }, { "required", true } }, new JObject { { "name", "api_integration" }, { "description", "API integration" }, { "required", true } }) } } },
                { "git_integration", new JObject { { "name", "git_integration" }, { "description", "Git integration" }, { "arguments", new JArray(new JObject { { "name", "repository_name" }, { "description", "Repository name" }, { "required", true } }) } } },
                { "streamlit_app", new JObject { { "name", "streamlit_app" }, { "description", "Streamlit app" }, { "arguments", new JArray(new JObject { { "name", "app_name" }, { "description", "App name" }, { "required", true } }) } } },
                { "document_ai", new JObject { { "name", "document_ai" }, { "description", "Document AI" }, { "arguments", new JArray(new JObject { { "name", "document_path" }, { "description", "Document path" }, { "required", true } }) } } },
                { "email_notification", new JObject { { "name", "email_notification" }, { "description", "Email notification" }, { "arguments", new JArray(new JObject { { "name", "integration_name" }, { "description", "Integration name" }, { "required", true } }) } } },
                { "data_encryption", new JObject { { "name", "data_encryption" }, { "description", "Data encryption" }, { "arguments", new JArray(new JObject { { "name", "encryption_type" }, { "description", "Encryption type" }, { "required", true } }) } } },
                { "object_tagging_automation", new JObject { { "name", "object_tagging_automation" }, { "description", "Object tagging automation" }, { "arguments", new JArray(new JObject { { "name", "tag_name" }, { "description", "Tag name" }, { "required", true } }) } } },
                { "warehouse_suspension", new JObject { { "name", "warehouse_suspension" }, { "description", "Warehouse suspension" }, { "arguments", new JArray(new JObject { { "name", "warehouse_name" }, { "description", "Warehouse name" }, { "required", true } }, new JObject { { "name", "suspend_minutes" }, { "description", "Auto-suspend minutes" }, { "required", true } }) } } },
                { "java_udf", new JObject { { "name", "java_udf" }, { "description", "Java UDF" }, { "arguments", new JArray(new JObject { { "name", "function_name" }, { "description", "Function name" }, { "required", true } }) } } },
                { "connection_pooling", new JObject { { "name", "connection_pooling" }, { "description", "Connection pooling" }, { "arguments", new JArray(new JObject { { "name", "pool_name" }, { "description", "Pool name" }, { "required", true } }) } } },
                { "iceberg_partitioning", new JObject { { "name", "iceberg_partitioning" }, { "description", "Iceberg partitioning" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }, new JObject { { "name", "partition_column" }, { "description", "Partition column" }, { "required", true } }) } } },
                { "privilege_analysis", new JObject { { "name", "privilege_analysis" }, { "description", "Privilege analysis" }, { "arguments", new JArray(new JObject { { "name", "role_name" }, { "description", "Role name" }, { "required", true } }) } } },
                { "reference_column", new JObject { { "name", "reference_column" }, { "description", "Reference column" }, { "arguments", new JArray(new JObject { { "name", "column_name" }, { "description", "Column name" }, { "required", true } }, new JObject { { "name", "reference_table" }, { "description", "Reference table" }, { "required", true } }) } } },
                { "lateral_view", new JObject { { "name", "lateral_view" }, { "description", "Lateral view" }, { "arguments", new JArray(new JObject { { "name", "view_name" }, { "description", "View name" }, { "required", true } }) } } },
                { "data_governance", new JObject { { "name", "data_governance" }, { "description", "Data governance" }, { "arguments", new JArray(new JObject { { "name", "policy_name" }, { "description", "Policy name" }, { "required", true } }) } } },
                { "federation", new JObject { { "name", "federation" }, { "description", "Federation setup" }, { "arguments", new JArray(new JObject { { "name", "account_url" }, { "description", "Account URL" }, { "required", true } }) } } },
                { "snowpipe_notification", new JObject { { "name", "snowpipe_notification" }, { "description", "Snowpipe notification" }, { "arguments", new JArray(new JObject { { "name", "pipe_name" }, { "description", "Pipe name" }, { "required", true } }) } } },
                { "dynamic_schema_evolution", new JObject { { "name", "dynamic_schema_evolution" }, { "description", "Dynamic schema evolution" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "api_integration", new JObject { { "name", "api_integration" }, { "description", "API integration" }, { "arguments", new JArray(new JObject { { "name", "integration_name" }, { "description", "Integration name" }, { "required", true } }, new JObject { { "name", "api_url" }, { "description", "API URL" }, { "required", true } }) } } },
                { "cost_allocation_tags", new JObject { { "name", "cost_allocation_tags" }, { "description", "Cost allocation tags" }, { "arguments", new JArray(new JObject { { "name", "tag_key" }, { "description", "Tag key" }, { "required", true } }, new JObject { { "name", "cost_center" }, { "description", "Cost center" }, { "required", true } }) } } },
                { "materialized_view_mode", new JObject { { "name", "materialized_view_mode" }, { "description", "Materialized view mode" }, { "arguments", new JArray(new JObject { { "name", "view_name" }, { "description", "View name" }, { "required", true } }, new JObject { { "name", "refresh_mode" }, { "description", "Refresh mode" }, { "required", true } }) } } },
                { "multi_statement_task", new JObject { { "name", "multi_statement_task" }, { "description", "Multi-statement task" }, { "arguments", new JArray(new JObject { { "name", "task_name" }, { "description", "Task name" }, { "required", true } }) } } },
                { "stage_encryption", new JObject { { "name", "stage_encryption" }, { "description", "Stage encryption" }, { "arguments", new JArray(new JObject { { "name", "stage_name" }, { "description", "Stage name" }, { "required", true } }, new JObject { { "name", "encryption_key" }, { "description", "Encryption key" }, { "required", true } }) } } },
                { "dynamic_warehouse_sizing", new JObject { { "name", "dynamic_warehouse_sizing" }, { "description", "Dynamic warehouse sizing" }, { "arguments", new JArray(new JObject { { "name", "warehouse_name" }, { "description", "Warehouse name" }, { "required", true } }) } } },
                { "row_filtering_policy", new JObject { { "name", "row_filtering_policy" }, { "description", "Row filtering policy" }, { "arguments", new JArray(new JObject { { "name", "policy_name" }, { "description", "Policy name" }, { "required", true } }, new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "view_clustering_key", new JObject { { "name", "view_clustering_key" }, { "description", "View clustering key" }, { "arguments", new JArray(new JObject { { "name", "view_name" }, { "description", "View name" }, { "required", true } }) } } },
                { "query_result_cache_optimization", new JObject { { "name", "query_result_cache_optimization" }, { "description", "Query result cache optimization" }, { "arguments", new JArray(new JObject { { "name", "query_pattern" }, { "description", "Query pattern" }, { "required", true } }) } } },
                { "join_optimization", new JObject { { "name", "join_optimization" }, { "description", "Join optimization" }, { "arguments", new JArray(new JObject { { "name", "table1" }, { "description", "First table" }, { "required", true } }, new JObject { { "name", "table2" }, { "description", "Second table" }, { "required", true } }) } } },
                { "subquery_flattening", new JObject { { "name", "subquery_flattening" }, { "description", "Subquery flattening" }, { "arguments", new JArray(new JObject { { "name", "query" }, { "description", "Query with subqueries" }, { "required", true } }) } } },
                { "partition_pruning", new JObject { { "name", "partition_pruning" }, { "description", "Partition pruning" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }, new JObject { { "name", "partition_column" }, { "description", "Partition column" }, { "required", true } }) } } },
                { "cross_cluster_replication", new JObject { { "name", "cross_cluster_replication" }, { "description", "Cross-cluster replication" }, { "arguments", new JArray(new JObject { { "name", "database_name" }, { "description", "Database name" }, { "required", true } }, new JObject { { "name", "target_region" }, { "description", "Target region" }, { "required", true } }) } } },
                { "budget_alert_policy", new JObject { { "name", "budget_alert_policy" }, { "description", "Budget alert policy" }, { "arguments", new JArray(new JObject { { "name", "policy_name" }, { "description", "Policy name" }, { "required", true } }, new JObject { { "name", "alert_threshold" }, { "description", "Alert threshold amount" }, { "required", true } }) } } },
                { "user_provisioning", new JObject { { "name", "user_provisioning" }, { "description", "User provisioning" }, { "arguments", new JArray(new JObject { { "name", "user_email" }, { "description", "User email" }, { "required", true } }) } } },
                { "password_policy", new JObject { { "name", "password_policy" }, { "description", "Password policy" }, { "arguments", new JArray(new JObject { { "name", "policy_name" }, { "description", "Policy name" }, { "required", true } }) } } },
                { "session_timeout_policy", new JObject { { "name", "session_timeout_policy" }, { "description", "Session timeout policy" }, { "arguments", new JArray(new JObject { { "name", "timeout_minutes" }, { "description", "Timeout in minutes" }, { "required", true } }) } } },
                { "ip_allowlist", new JObject { { "name", "ip_allowlist" }, { "description", "IP allowlist" }, { "arguments", new JArray(new JObject { { "name", "ip_address" }, { "description", "IP address or CIDR" }, { "required", true } }) } } },
                { "vault_integration", new JObject { { "name", "vault_integration" }, { "description", "Vault integration" }, { "arguments", new JArray(new JObject { { "name", "vault_type" }, { "description", "Vault type (HashiCorp/Azure/AWS)" }, { "required", true } }, new JObject { { "name", "vault_url" }, { "description", "Vault URL" }, { "required", true } }) } } },
                { "okta_integration", new JObject { { "name", "okta_integration" }, { "description", "Okta integration" }, { "arguments", new JArray(new JObject { { "name", "okta_account" }, { "description", "Okta account URL" }, { "required", true } }) } } },
                { "api_rate_limiting", new JObject { { "name", "api_rate_limiting" }, { "description", "API rate limiting" }, { "arguments", new JArray(new JObject { { "name", "requests_per_minute" }, { "description", "Requests per minute limit" }, { "required", true } }) } } },
                { "query_result_sharing", new JObject { { "name", "query_result_sharing" }, { "description", "Query result sharing" }, { "arguments", new JArray(new JObject { { "name", "account_id" }, { "description", "Target account ID" }, { "required", true } }) } } },
                { "compute_pool", new JObject { { "name", "compute_pool" }, { "description", "Compute pool" }, { "arguments", new JArray(new JObject { { "name", "pool_name" }, { "description", "Pool name" }, { "required", true } }, new JObject { { "name", "pool_size" }, { "description", "Pool size (S/M/L/XL)" }, { "required", true } }) } } },
                { "network_rule", new JObject { { "name", "network_rule" }, { "description", "Network rule" }, { "arguments", new JArray(new JObject { { "name", "rule_name" }, { "description", "Rule name" }, { "required", true } }, new JObject { { "name", "ip_address" }, { "description", "IP address pattern" }, { "required", true } }) } } },
                { "iceberg_metadata_pruning", new JObject { { "name", "iceberg_metadata_pruning" }, { "description", "Iceberg metadata pruning" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "query_queue", new JObject { { "name", "query_queue" }, { "description", "Query queue" }, { "arguments", new JArray(new JObject { { "name", "queue_name" }, { "description", "Queue name" }, { "required", true } }) } } },
                { "sort_key_recommendation", new JObject { { "name", "sort_key_recommendation" }, { "description", "Sort key recommendation" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "column_statistics", new JObject { { "name", "column_statistics" }, { "description", "Column statistics" }, { "arguments", new JArray(new JObject { { "name", "column_name" }, { "description", "Column name" }, { "required", true } }, new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "index_optimization", new JObject { { "name", "index_optimization" }, { "description", "Index optimization" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "data_freshness", new JObject { { "name", "data_freshness" }, { "description", "Data freshness" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "data_completeness", new JObject { { "name", "data_completeness" }, { "description", "Data completeness" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "query_dependency_analysis", new JObject { { "name", "query_dependency_analysis" }, { "description", "Query dependency analysis" }, { "arguments", new JArray(new JObject { { "name", "query_id" }, { "description", "Query ID" }, { "required", true } }) } } },
                { "delta_lake_integration", new JObject { { "name", "delta_lake_integration" }, { "description", "Delta Lake integration" }, { "arguments", new JArray(new JObject { { "name", "table_location" }, { "description", "Table location path" }, { "required", true } }) } } },
                { "apache_spark_integration", new JObject { { "name", "apache_spark_integration" }, { "description", "Apache Spark integration" }, { "arguments", new JArray(new JObject { { "name", "database_name" }, { "description", "Database name" }, { "required", true } }) } } },
                { "gpu_acceleration", new JObject { { "name", "gpu_acceleration" }, { "description", "GPU acceleration" }, { "arguments", new JArray(new JObject { { "name", "warehouse_name" }, { "description", "Warehouse name" }, { "required", true } }) } } },
                { "anomaly_detection", new JObject { { "name", "anomaly_detection" }, { "description", "Anomaly detection" }, { "arguments", new JArray(new JObject { { "name", "column_name" }, { "description", "Column name" }, { "required", true } }, new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "metadata_api", new JObject { { "name", "metadata_api" }, { "description", "Metadata API" }, { "arguments", new JArray(new JObject { { "name", "object_type" }, { "description", "Object type (table/view/column)" }, { "required", true } }) } } },
                { "snowsight_dashboard", new JObject { { "name", "snowsight_dashboard" }, { "description", "Snowsight dashboard" }, { "arguments", new JArray(new JObject { { "name", "dashboard_name" }, { "description", "Dashboard name" }, { "required", true } }) } } },
                { "model_registry", new JObject { { "name", "model_registry" }, { "description", "Model registry" }, { "arguments", new JArray(new JObject { { "name", "model_name" }, { "description", "Model name" }, { "required", true } }, new JObject { { "name", "model_version" }, { "description", "Model version" }, { "required", true } }) } } },
                { "prediction_function", new JObject { { "name", "prediction_function" }, { "description", "Prediction function" }, { "arguments", new JArray(new JObject { { "name", "function_name" }, { "description", "Function name" }, { "required", true } }, new JObject { { "name", "model_name" }, { "description", "Model name" }, { "required", true } }) } } },
                { "time_series_analysis", new JObject { { "name", "time_series_analysis" }, { "description", "Time series analysis" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }, new JObject { { "name", "timestamp_column" }, { "description", "Timestamp column" }, { "required", true } }) } } },
                { "graph_database", new JObject { { "name", "graph_database" }, { "description", "Graph database" }, { "arguments", new JArray(new JObject { { "name", "graph_name" }, { "description", "Graph name" }, { "required", true } }) } } },
                { "dynamic_sql", new JObject { { "name", "dynamic_sql" }, { "description", "Dynamic SQL" }, { "arguments", new JArray(new JObject { { "name", "sql_template" }, { "description", "SQL template" }, { "required", true } }) } } },
                { "stored_procedure_optimization", new JObject { { "name", "stored_procedure_optimization" }, { "description", "Stored procedure optimization" }, { "arguments", new JArray(new JObject { { "name", "procedure_name" }, { "description", "Procedure name" }, { "required", true } }) } } },
                { "feature_store", new JObject { { "name", "feature_store" }, { "description", "Feature store" }, { "arguments", new JArray(new JObject { { "name", "store_name" }, { "description", "Store name" }, { "required", true } }) } } },
                { "event_stream_integration", new JObject { { "name", "event_stream_integration" }, { "description", "Event stream integration" }, { "arguments", new JArray(new JObject { { "name", "topic_name" }, { "description", "Topic name" }, { "required", true } }) } } },
                { "marketplace_sharing", new JObject { { "name", "marketplace_sharing" }, { "description", "Marketplace sharing" }, { "arguments", new JArray(new JObject { { "name", "listing_name" }, { "description", "Listing name" }, { "required", true } }) } } },
                { "bulk_load_optimization", new JObject { { "name", "bulk_load_optimization" }, { "description", "Bulk load optimization" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }, new JObject { { "name", "file_format" }, { "description", "File format" }, { "required", true } }) } } },
                { "incremental_update", new JObject { { "name", "incremental_update" }, { "description", "Incremental update" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }, new JObject { { "name", "update_key" }, { "description", "Update key column" }, { "required", true } }) } } },
                { "deduplication", new JObject { { "name", "deduplication" }, { "description", "Deduplication" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }, new JObject { { "name", "unique_key" }, { "description", "Unique key column" }, { "required", true } }) } } },
                { "cdc_optimization", new JObject { { "name", "cdc_optimization" }, { "description", "CDC optimization" }, { "arguments", new JArray(new JObject { { "name", "stream_name" }, { "description", "Stream name" }, { "required", true } }) } } },
                { "table_evolution_tracking", new JObject { { "name", "table_evolution_tracking" }, { "description", "Table evolution tracking" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "cost_tracking_department", new JObject { { "name", "cost_tracking_department" }, { "description", "Cost tracking by department" }, { "arguments", new JArray(new JObject { { "name", "department" }, { "description", "Department name" }, { "required", true } }) } } },
                { "query_rewrite", new JObject { { "name", "query_rewrite" }, { "description", "Query rewrite" }, { "arguments", new JArray(new JObject { { "name", "query" }, { "description", "Query to optimize" }, { "required", true } }) } } },
                { "materialized_view_refresh", new JObject { { "name", "materialized_view_refresh" }, { "description", "Materialized view refresh" }, { "arguments", new JArray(new JObject { { "name", "view_name" }, { "description", "View name" }, { "required", true } }, new JObject { { "name", "refresh_mode" }, { "description", "Refresh mode (AUTO/FULL/INCREMENTAL)" }, { "required", true } }) } } },
                { "workload_management", new JObject { { "name", "workload_management" }, { "description", "Workload management" }, { "arguments", new JArray(new JObject { { "name", "workload_group" }, { "description", "Workload group name" }, { "required", true } }) } } },
                { "query_acceleration_analysis", new JObject { { "name", "query_acceleration_analysis" }, { "description", "Query acceleration analysis" }, { "arguments", new JArray(new JObject { { "name", "warehouse_name" }, { "description", "Warehouse name" }, { "required", true } }) } } },
                { "column_masking_review", new JObject { { "name", "column_masking_review" }, { "description", "Column masking review" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "share_monitoring", new JObject { { "name", "share_monitoring" }, { "description", "Share monitoring" }, { "arguments", new JArray(new JObject { { "name", "share_name" }, { "description", "Share name" }, { "required", true } }) } } },
                { "replication_lag_monitoring", new JObject { { "name", "replication_lag_monitoring" }, { "description", "Replication lag monitoring" }, { "arguments", new JArray(new JObject { { "name", "database_name" }, { "description", "Database name" }, { "required", true } }) } } },
                { "warehouse_efficiency", new JObject { { "name", "warehouse_efficiency" }, { "description", "Warehouse efficiency" }, { "arguments", new JArray(new JObject { { "name", "warehouse_name" }, { "description", "Warehouse name" }, { "required", true } }) } } },
                { "query_plan_analysis", new JObject { { "name", "query_plan_analysis" }, { "description", "Query plan analysis" }, { "arguments", new JArray(new JObject { { "name", "query_id" }, { "description", "Query ID" }, { "required", true } }) } } },
                { "cardinality_estimation", new JObject { { "name", "cardinality_estimation" }, { "description", "Cardinality estimation" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }, new JObject { { "name", "column_name" }, { "description", "Column name" }, { "required", true } }) } } },
                { "memory_optimization", new JObject { { "name", "memory_optimization" }, { "description", "Memory optimization" }, { "arguments", new JArray(new JObject { { "name", "warehouse_name" }, { "description", "Warehouse name" }, { "required", true } }) } } },
                { "spill_optimization", new JObject { { "name", "spill_optimization" }, { "description", "Spill optimization" }, { "arguments", new JArray(new JObject { { "name", "query_id" }, { "description", "Query ID" }, { "required", true } }) } } },
                { "failover_testing", new JObject { { "name", "failover_testing" }, { "description", "Failover testing" }, { "arguments", new JArray(new JObject { { "name", "failover_group" }, { "description", "Failover group name" }, { "required", true } }) } } },
                { "stored_procedure_validation", new JObject { { "name", "stored_procedure_validation" }, { "description", "Stored procedure validation" }, { "arguments", new JArray(new JObject { { "name", "procedure_name" }, { "description", "Procedure name" }, { "required", true } }) } } },
                { "view_dependency_analysis", new JObject { { "name", "view_dependency_analysis" }, { "description", "View dependency analysis" }, { "arguments", new JArray(new JObject { { "name", "view_name" }, { "description", "View name" }, { "required", true } }) } } },
                { "query_caching_strategy", new JObject { { "name", "query_caching_strategy" }, { "description", "Query caching strategy" }, { "arguments", new JArray(new JObject { { "name", "warehouse_name" }, { "description", "Warehouse name" }, { "required", true } }) } } },
                { "materialized_view_staleness", new JObject { { "name", "materialized_view_staleness" }, { "description", "Materialized view staleness" }, { "arguments", new JArray(new JObject { { "name", "view_name" }, { "description", "View name" }, { "required", true } }) } } },
                { "load_pattern_analysis", new JObject { { "name", "load_pattern_analysis" }, { "description", "Load pattern analysis" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "warehouse_queue_analysis", new JObject { { "name", "warehouse_queue_analysis" }, { "description", "Warehouse queue analysis" }, { "arguments", new JArray(new JObject { { "name", "warehouse_name" }, { "description", "Warehouse name" }, { "required", true } }) } } },
                { "query_complexity_metrics", new JObject { { "name", "query_complexity_metrics" }, { "description", "Query complexity metrics" }, { "arguments", new JArray(new JObject { { "name", "query_id" }, { "description", "Query ID" }, { "required", true } }) } } },
                { "data_pipeline_monitoring", new JObject { { "name", "data_pipeline_monitoring" }, { "description", "Data pipeline monitoring" }, { "arguments", new JArray(new JObject { { "name", "pipeline_name" }, { "description", "Pipeline name" }, { "required", true } }) } } },
                { "sla_monitoring", new JObject { { "name", "sla_monitoring" }, { "description", "SLA monitoring" }, { "arguments", new JArray(new JObject { { "name", "sla_name" }, { "description", "SLA name" }, { "required", true } }) } } },
                { "performance_baseline", new JObject { { "name", "performance_baseline" }, { "description", "Performance baseline" }, { "arguments", new JArray(new JObject { { "name", "metric_name" }, { "description", "Metric name" }, { "required", true } }) } } },
                { "cost_anomaly_detection", new JObject { { "name", "cost_anomaly_detection" }, { "description", "Cost anomaly detection" }, { "arguments", new JArray(new JObject { { "name", "time_period" }, { "description", "Time period" }, { "required", true } }) } } },
                { "resource_contention", new JObject { { "name", "resource_contention" }, { "description", "Resource contention" }, { "arguments", new JArray(new JObject { { "name", "warehouse_name" }, { "description", "Warehouse name" }, { "required", true } }) } } },
                { "backup_recovery_strategy", new JObject { { "name", "backup_recovery_strategy" }, { "description", "Backup recovery strategy" }, { "arguments", new JArray(new JObject { { "name", "database_name" }, { "description", "Database name" }, { "required", true } }) } } },
                { "disaster_recovery_drill", new JObject { { "name", "disaster_recovery_drill" }, { "description", "Disaster recovery drill" }, { "arguments", new JArray(new JObject { { "name", "recovery_plan" }, { "description", "Recovery plan name" }, { "required", true } }) } } },
                { "multi_region_deployment", new JObject { { "name", "multi_region_deployment" }, { "description", "Multi-region deployment" }, { "arguments", new JArray(new JObject { { "name", "regions" }, { "description", "Regions (comma-separated)" }, { "required", true } }) } } },
                { "application_event_table", new JObject { { "name", "application_event_table" }, { "description", "Application event table" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "query_result_partitioning", new JObject { { "name", "query_result_partitioning" }, { "description", "Query result partitioning" }, { "arguments", new JArray(new JObject { { "name", "partition_column" }, { "description", "Partition column" }, { "required", true } }) } } },
                { "time_travel_analysis", new JObject { { "name", "time_travel_analysis" }, { "description", "Time travel analysis" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }, new JObject { { "name", "time_offset" }, { "description", "Time offset or timestamp" }, { "required", true } }) } } },
                { "schema_comparison", new JObject { { "name", "schema_comparison" }, { "description", "Schema comparison" }, { "arguments", new JArray(new JObject { { "name", "schema1" }, { "description", "First schema" }, { "required", true } }, new JObject { { "name", "schema2" }, { "description", "Second schema" }, { "required", true } }) } } },
                { "data_contract_validation", new JObject { { "name", "data_contract_validation" }, { "description", "Data contract validation" }, { "arguments", new JArray(new JObject { { "name", "contract_name" }, { "description", "Contract name" }, { "required", true } }, new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "schema_registry", new JObject { { "name", "schema_registry" }, { "description", "Schema registry" }, { "arguments", new JArray(new JObject { { "name", "registry_url" }, { "description", "Registry URL" }, { "required", true } }) } } },
                { "service_principal", new JObject { { "name", "service_principal" }, { "description", "Service principal" }, { "arguments", new JArray(new JObject { { "name", "principal_name" }, { "description", "Principal name" }, { "required", true } }) } } },
                { "periodic_table_refresh", new JObject { { "name", "periodic_table_refresh" }, { "description", "Periodic table refresh" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }, new JObject { { "name", "refresh_interval" }, { "description", "Refresh interval (hours)" }, { "required", true } }) } } },
                { "aggregation_performance", new JObject { { "name", "aggregation_performance" }, { "description", "Aggregation performance" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "clone_consistency_check", new JObject { { "name", "clone_consistency_check" }, { "description", "Clone consistency check" }, { "arguments", new JArray(new JObject { { "name", "source_object" }, { "description", "Source object" }, { "required", true } }, new JObject { { "name", "clone_object" }, { "description", "Clone object" }, { "required", true } }) } } },
                { "workload_isolation", new JObject { { "name", "workload_isolation" }, { "description", "Workload isolation" }, { "arguments", new JArray(new JObject { { "name", "workload_name" }, { "description", "Workload name" }, { "required", true } }) } } },
                { "tenant_isolation_pattern", new JObject { { "name", "tenant_isolation_pattern" }, { "description", "Tenant isolation pattern" }, { "arguments", new JArray(new JObject { { "name", "tenant_id" }, { "description", "Tenant ID" }, { "required", true } }) } } },
                { "data_sampling_strategy", new JObject { { "name", "data_sampling_strategy" }, { "description", "Data sampling strategy" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }, new JObject { { "name", "sample_size" }, { "description", "Sample size (%)" }, { "required", true } }) } } },
                { "quality_metrics_reporting", new JObject { { "name", "quality_metrics_reporting" }, { "description", "Quality metrics reporting" }, { "arguments", new JArray(new JObject { { "name", "report_name" }, { "description", "Report name" }, { "required", true } }) } } },
                { "security_posture_analysis", new JObject { { "name", "security_posture_analysis" }, { "description", "Security posture analysis" }, { "arguments", new JArray(new JObject { { "name", "assessment_type" }, { "description", "Assessment type" }, { "required", true } }) } } },
                { "query_fingerprinting", new JObject { { "name", "query_fingerprinting" }, { "description", "Query fingerprinting" }, { "arguments", new JArray(new JObject { { "name", "time_range" }, { "description", "Time range" }, { "required", true } }) } } },
                { "performance_regression_detection", new JObject { { "name", "performance_regression_detection" }, { "description", "Performance regression detection" }, { "arguments", new JArray(new JObject { { "name", "query_id" }, { "description", "Query ID" }, { "required", true } }, new JObject { { "name", "baseline_period" }, { "description", "Baseline period" }, { "required", true } }) } } },
                { "metadata_version_tracking", new JObject { { "name", "metadata_version_tracking" }, { "description", "Metadata version tracking" }, { "arguments", new JArray(new JObject { { "name", "object_name" }, { "description", "Object name" }, { "required", true } }) } } },
                { "storage_optimization", new JObject { { "name", "storage_optimization" }, { "description", "Storage optimization" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "table_growth_trending", new JObject { { "name", "table_growth_trending" }, { "description", "Table growth trending" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }, new JObject { { "name", "time_period" }, { "description", "Time period (months)" }, { "required", true } }) } } },
                { "concurrent_query_analysis", new JObject { { "name", "concurrent_query_analysis" }, { "description", "Concurrent query analysis" }, { "arguments", new JArray(new JObject { { "name", "warehouse_name" }, { "description", "Warehouse name" }, { "required", true } }) } } },
                { "result_cache_tuning", new JObject { { "name", "result_cache_tuning" }, { "description", "Result cache tuning" }, { "arguments", new JArray(new JObject { { "name", "warehouse_name" }, { "description", "Warehouse name" }, { "required", true } }) } } },
                { "materialized_view_sustainability", new JObject { { "name", "materialized_view_sustainability" }, { "description", "Materialized view sustainability" }, { "arguments", new JArray(new JObject { { "name", "view_name" }, { "description", "View name" }, { "required", true } }) } } },
                { "dynamic_pruning", new JObject { { "name", "dynamic_pruning" }, { "description", "Dynamic pruning" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "lateral_join_optimization", new JObject { { "name", "lateral_join_optimization" }, { "description", "Lateral join optimization" }, { "arguments", new JArray(new JObject { { "name", "table1" }, { "description", "First table" }, { "required", true } }, new JObject { { "name", "table2" }, { "description", "Second table" }, { "required", true } }) } } },
                { "bloom_filter_tuning", new JObject { { "name", "bloom_filter_tuning" }, { "description", "Bloom filter tuning" }, { "arguments", new JArray(new JObject { { "name", "join_column" }, { "description", "Join column" }, { "required", true } }) } } },
                { "dictionary_encoding", new JObject { { "name", "dictionary_encoding" }, { "description", "Dictionary encoding" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }, new JObject { { "name", "column_name" }, { "description", "Column name" }, { "required", true } }) } } },
                { "compression_analysis", new JObject { { "name", "compression_analysis" }, { "description", "Compression analysis" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "data_skew_detection", new JObject { { "name", "data_skew_detection" }, { "description", "Data skew detection" }, { "arguments", new JArray(new JObject { { "name", "column_name" }, { "description", "Column name" }, { "required", true } }, new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "parallel_execution_tuning", new JObject { { "name", "parallel_execution_tuning" }, { "description", "Parallel execution tuning" }, { "arguments", new JArray(new JObject { { "name", "warehouse_name" }, { "description", "Warehouse name" }, { "required", true } }) } } },
                { "query_semantic_parsing", new JObject { { "name", "query_semantic_parsing" }, { "description", "Query semantic parsing" }, { "arguments", new JArray(new JObject { { "name", "query" }, { "description", "Query string" }, { "required", true } }) } } },
                { "join_selectivity", new JObject { { "name", "join_selectivity" }, { "description", "Join selectivity" }, { "arguments", new JArray(new JObject { { "name", "table1" }, { "description", "First table" }, { "required", true } }, new JObject { { "name", "table2" }, { "description", "Second table" }, { "required", true } }) } } },
                { "estimated_execution_time", new JObject { { "name", "estimated_execution_time" }, { "description", "Estimated execution time" }, { "arguments", new JArray(new JObject { { "name", "query" }, { "description", "Query string" }, { "required", true } }) } } },
                { "partitioning_strategy", new JObject { { "name", "partitioning_strategy" }, { "description", "Partitioning strategy" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }, new JObject { { "name", "workload_type" }, { "description", "Workload type" }, { "required", true } }) } } },
                { "index_selectivity", new JObject { { "name", "index_selectivity" }, { "description", "Index selectivity" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }, new JObject { { "name", "column_name" }, { "description", "Column name" }, { "required", true } }) } } },
                { "predicate_pushdown", new JObject { { "name", "predicate_pushdown" }, { "description", "Predicate pushdown" }, { "arguments", new JArray(new JObject { { "name", "query" }, { "description", "Query string" }, { "required", true } }) } } },
                { "statistics_collection", new JObject { { "name", "statistics_collection" }, { "description", "Statistics collection" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "query_plan_caching", new JObject { { "name", "query_plan_caching" }, { "description", "Query plan caching" }, { "arguments", new JArray(new JObject { { "name", "warehouse_name" }, { "description", "Warehouse name" }, { "required", true } }) } } },
                { "cost_based_optimization", new JObject { { "name", "cost_based_optimization" }, { "description", "Cost-based optimization" }, { "arguments", new JArray(new JObject { { "name", "query" }, { "description", "Query string" }, { "required", true } }) } } },
                { "adaptive_query_execution", new JObject { { "name", "adaptive_query_execution" }, { "description", "Adaptive query execution" }, { "arguments", new JArray(new JObject { { "name", "warehouse_name" }, { "description", "Warehouse name" }, { "required", true } }) } } },
                { "vectorization", new JObject { { "name", "vectorization" }, { "description", "Vectorization" }, { "arguments", new JArray(new JObject { { "name", "column_type" }, { "description", "Column data type" }, { "required", true } }) } } },
                { "null_handling_optimization", new JObject { { "name", "null_handling_optimization" }, { "description", "NULL handling optimization" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "operator_ordering", new JObject { { "name", "operator_ordering" }, { "description", "Operator ordering" }, { "arguments", new JArray(new JObject { { "name", "query" }, { "description", "Query string" }, { "required", true } }) } } },
                { "expression_simplification", new JObject { { "name", "expression_simplification" }, { "description", "Expression simplification" }, { "arguments", new JArray(new JObject { { "name", "expression" }, { "description", "SQL expression" }, { "required", true } }) } } },
                { "aggregate_function_optimization", new JObject { { "name", "aggregate_function_optimization" }, { "description", "Aggregate function optimization" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }, new JObject { { "name", "function_name" }, { "description", "Function name" }, { "required", true } }) } } },
                { "window_function_optimization", new JObject { { "name", "window_function_optimization" }, { "description", "Window function optimization" }, { "arguments", new JArray(new JObject { { "name", "query" }, { "description", "Query string" }, { "required", true } }) } } },
                { "subquery_optimization", new JObject { { "name", "subquery_optimization" }, { "description", "Subquery optimization" }, { "arguments", new JArray(new JObject { { "name", "query" }, { "description", "Query string" }, { "required", true } }) } } },
                { "cte_optimization", new JObject { { "name", "cte_optimization" }, { "description", "CTE optimization" }, { "arguments", new JArray(new JObject { { "name", "query" }, { "description", "Query string" }, { "required", true } }) } } },
                { "correlated_subquery", new JObject { { "name", "correlated_subquery" }, { "description", "Correlated subquery" }, { "arguments", new JArray(new JObject { { "name", "query" }, { "description", "Query string" }, { "required", true } }) } } },
                { "scalar_function_inlining", new JObject { { "name", "scalar_function_inlining" }, { "description", "Scalar function inlining" }, { "arguments", new JArray(new JObject { { "name", "function_name" }, { "description", "Function name" }, { "required", true } }) } } },
                { "join_reordering", new JObject { { "name", "join_reordering" }, { "description", "Join reordering" }, { "arguments", new JArray(new JObject { { "name", "query" }, { "description", "Query string" }, { "required", true } }) } } },
                { "table_elimination", new JObject { { "name", "table_elimination" }, { "description", "Table elimination" }, { "arguments", new JArray(new JObject { { "name", "query" }, { "description", "Query string" }, { "required", true } }) } } },
                { "constant_folding", new JObject { { "name", "constant_folding" }, { "description", "Constant folding" }, { "arguments", new JArray(new JObject { { "name", "expression" }, { "description", "SQL expression" }, { "required", true } }) } } },
                { "transitive_closure", new JObject { { "name", "transitive_closure" }, { "description", "Transitive closure" }, { "arguments", new JArray(new JObject { { "name", "query" }, { "description", "Query string" }, { "required", true } }) } } },
                { "redundant_join_removal", new JObject { { "name", "redundant_join_removal" }, { "description", "Redundant join removal" }, { "arguments", new JArray(new JObject { { "name", "query" }, { "description", "Query string" }, { "required", true } }) } } },
                { "view_merging", new JObject { { "name", "view_merging" }, { "description", "View merging" }, { "arguments", new JArray(new JObject { { "name", "view_name" }, { "description", "View name" }, { "required", true } }) } } },
                { "grouping_set_optimization", new JObject { { "name", "grouping_set_optimization" }, { "description", "Grouping set optimization" }, { "arguments", new JArray(new JObject { { "name", "query" }, { "description", "Query string" }, { "required", true } }) } } },
                { "sort_elimination", new JObject { { "name", "sort_elimination" }, { "description", "Sort elimination" }, { "arguments", new JArray(new JObject { { "name", "query" }, { "description", "Query string" }, { "required", true } }) } } },
                { "distinct_elimination", new JObject { { "name", "distinct_elimination" }, { "description", "Distinct elimination" }, { "arguments", new JArray(new JObject { { "name", "query" }, { "description", "Query string" }, { "required", true } }) } } },
                { "unused_column_elimination", new JObject { { "name", "unused_column_elimination" }, { "description", "Unused column elimination" }, { "arguments", new JArray(new JObject { { "name", "query" }, { "description", "Query string" }, { "required", true } }) } } },
                { "semi_join_optimization", new JObject { { "name", "semi_join_optimization" }, { "description", "Semi-join optimization" }, { "arguments", new JArray(new JObject { { "name", "query" }, { "description", "Query string" }, { "required", true } }) } } },
                { "anti_join_optimization", new JObject { { "name", "anti_join_optimization" }, { "description", "Anti-join optimization" }, { "arguments", new JArray(new JObject { { "name", "query" }, { "description", "Query string" }, { "required", true } }) } } },
                { "outer_join_optimization", new JObject { { "name", "outer_join_optimization" }, { "description", "Outer join optimization" }, { "arguments", new JArray(new JObject { { "name", "query" }, { "description", "Query string" }, { "required", true } }) } } },
                { "hash_aggregate_optimization", new JObject { { "name", "hash_aggregate_optimization" }, { "description", "Hash aggregate optimization" }, { "arguments", new JArray(new JObject { { "name", "query" }, { "description", "Query string" }, { "required", true } }) } } },
                { "sort_aggregate_optimization", new JObject { { "name", "sort_aggregate_optimization" }, { "description", "Sort aggregate optimization" }, { "arguments", new JArray(new JObject { { "name", "query" }, { "description", "Query string" }, { "required", true } }) } } },
                { "limit_pushdown", new JObject { { "name", "limit_pushdown" }, { "description", "Limit pushdown" }, { "arguments", new JArray(new JObject { { "name", "query" }, { "description", "Query string" }, { "required", true } }) } } },
                { "early_filtering", new JObject { { "name", "early_filtering" }, { "description", "Early filtering" }, { "arguments", new JArray(new JObject { { "name", "query" }, { "description", "Query string" }, { "required", true } }) } } },
                { "expression_elimination", new JObject { { "name", "expression_elimination" }, { "description", "Expression elimination" }, { "arguments", new JArray(new JObject { { "name", "query" }, { "description", "Query string" }, { "required", true } }) } } },
                { "constraint_propagation", new JObject { { "name", "constraint_propagation" }, { "description", "Constraint propagation" }, { "arguments", new JArray(new JObject { { "name", "query" }, { "description", "Query string" }, { "required", true } }) } } },
                { "null_propagation", new JObject { { "name", "null_propagation" }, { "description", "NULL propagation" }, { "arguments", new JArray(new JObject { { "name", "query" }, { "description", "Query string" }, { "required", true } }) } } },
                { "boolean_simplification", new JObject { { "name", "boolean_simplification" }, { "description", "Boolean simplification" }, { "arguments", new JArray(new JObject { { "name", "expression" }, { "description", "Boolean expression" }, { "required", true } }) } } },
                { "function_elimination", new JObject { { "name", "function_elimination" }, { "description", "Function elimination" }, { "arguments", new JArray(new JObject { { "name", "query" }, { "description", "Query string" }, { "required", true } }) } } },
                { "type_coercion_optimization", new JObject { { "name", "type_coercion_optimization" }, { "description", "Type coercion optimization" }, { "arguments", new JArray(new JObject { { "name", "expression" }, { "description", "SQL expression" }, { "required", true } }) } } },
                { "collation_optimization", new JObject { { "name", "collation_optimization" }, { "description", "Collation optimization" }, { "arguments", new JArray(new JObject { { "name", "column_name" }, { "description", "Column name" }, { "required", true } }, new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "temporal_optimization", new JObject { { "name", "temporal_optimization" }, { "description", "Temporal optimization" }, { "arguments", new JArray(new JObject { { "name", "query" }, { "description", "Query string" }, { "required", true } }) } } },
                { "implicit_casting_analysis", new JObject { { "name", "implicit_casting_analysis" }, { "description", "Implicit casting analysis" }, { "arguments", new JArray(new JObject { { "name", "expression" }, { "description", "SQL expression" }, { "required", true } }) } } },
                { "type_inference", new JObject { { "name", "type_inference" }, { "description", "Type inference" }, { "arguments", new JArray(new JObject { { "name", "expression" }, { "description", "SQL expression" }, { "required", true } }) } } },
                { "data_type_compatibility", new JObject { { "name", "data_type_compatibility" }, { "description", "Data type compatibility" }, { "arguments", new JArray(new JObject { { "name", "type1" }, { "description", "First data type" }, { "required", true } }, new JObject { { "name", "type2" }, { "description", "Second data type" }, { "required", true } }) } } },
                { "precision_loss_detection", new JObject { { "name", "precision_loss_detection" }, { "description", "Precision loss detection" }, { "arguments", new JArray(new JObject { { "name", "expression" }, { "description", "SQL expression" }, { "required", true } }) } } },
                { "overflow_checking", new JObject { { "name", "overflow_checking" }, { "description", "Overflow checking" }, { "arguments", new JArray(new JObject { { "name", "expression" }, { "description", "SQL expression" }, { "required", true } }) } } },
                { "rounding_modes", new JObject { { "name", "rounding_modes" }, { "description", "Rounding modes" }, { "arguments", new JArray(new JObject { { "name", "rounding_mode" }, { "description", "Rounding mode" }, { "required", true } }) } } },
                { "interval_arithmetic", new JObject { { "name", "interval_arithmetic" }, { "description", "Interval arithmetic" }, { "arguments", new JArray(new JObject { { "name", "interval_type" }, { "description", "Interval type" }, { "required", true } }) } } },
                { "date_arithmetic", new JObject { { "name", "date_arithmetic" }, { "description", "Date arithmetic" }, { "arguments", new JArray(new JObject { { "name", "operation" }, { "description", "Operation (+/-)" }, { "required", true } }) } } },
                { "timestamp_precision", new JObject { { "name", "timestamp_precision" }, { "description", "Timestamp precision" }, { "arguments", new JArray(new JObject { { "name", "precision_level" }, { "description", "Precision level" }, { "required", true } }) } } },
                { "timezone_aware_operations", new JObject { { "name", "timezone_aware_operations" }, { "description", "Timezone aware operations" }, { "arguments", new JArray(new JObject { { "name", "timezone" }, { "description", "Timezone name" }, { "required", true } }) } } },
                { "locale_specific_operations", new JObject { { "name", "locale_specific_operations" }, { "description", "Locale specific operations" }, { "arguments", new JArray(new JObject { { "name", "locale" }, { "description", "Locale code" }, { "required", true } }) } } },
                { "string_encoding", new JObject { { "name", "string_encoding" }, { "description", "String encoding" }, { "arguments", new JArray(new JObject { { "name", "encoding" }, { "description", "Encoding type (UTF-8/ASCII)" }, { "required", true } }) } } },
                { "character_set_handling", new JObject { { "name", "character_set_handling" }, { "description", "Character set handling" }, { "arguments", new JArray(new JObject { { "name", "character_set" }, { "description", "Character set name" }, { "required", true } }) } } },
                { "unicode_normalization", new JObject { { "name", "unicode_normalization" }, { "description", "Unicode normalization" }, { "arguments", new JArray(new JObject { { "name", "column_name" }, { "description", "Column name" }, { "required", true } }, new JObject { { "name", "normalization_form" }, { "description", "Normalization form (NFC/NFD)" }, { "required", true } }) } } },
                { "collation_comparison", new JObject { { "name", "collation_comparison" }, { "description", "Collation comparison" }, { "arguments", new JArray(new JObject { { "name", "collation1" }, { "description", "First collation" }, { "required", true } }, new JObject { { "name", "collation2" }, { "description", "Second collation" }, { "required", true } }) } } },
                { "lock_management", new JObject { { "name", "lock_management" }, { "description", "Lock management" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }, new JObject { { "name", "lock_type" }, { "description", "Lock type (SHARED/EXCLUSIVE)" }, { "required", true } }) } } },
                { "transaction_isolation", new JObject { { "name", "transaction_isolation" }, { "description", "Transaction isolation" }, { "arguments", new JArray(new JObject { { "name", "isolation_level" }, { "description", "Isolation level" }, { "required", true } }) } } },
                { "deadlock_detection", new JObject { { "name", "deadlock_detection" }, { "description", "Deadlock detection" }, { "arguments", new JArray(new JObject { { "name", "query_id" }, { "description", "Query ID" }, { "required", true } }) } } },
                { "optimistic_locking", new JObject { { "name", "optimistic_locking" }, { "description", "Optimistic locking" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "pessimistic_locking", new JObject { { "name", "pessimistic_locking" }, { "description", "Pessimistic locking" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "distributed_transactions", new JObject { { "name", "distributed_transactions" }, { "description", "Distributed transactions" }, { "arguments", new JArray(new JObject { { "name", "account_list" }, { "description", "Account list (comma-separated)" }, { "required", true } }) } } },
                { "two_phase_commit", new JObject { { "name", "two_phase_commit" }, { "description", "Two-phase commit" }, { "arguments", new JArray(new JObject { { "name", "transaction_id" }, { "description", "Transaction ID" }, { "required", true } }) } } },
                { "savepoint_management", new JObject { { "name", "savepoint_management" }, { "description", "Savepoint management" }, { "arguments", new JArray(new JObject { { "name", "savepoint_name" }, { "description", "Savepoint name" }, { "required", true } }) } } },
                { "cursor_management", new JObject { { "name", "cursor_management" }, { "description", "Cursor management" }, { "arguments", new JArray(new JObject { { "name", "cursor_name" }, { "description", "Cursor name" }, { "required", true } }) } } },
                { "fetch_optimization", new JObject { { "name", "fetch_optimization" }, { "description", "Fetch optimization" }, { "arguments", new JArray(new JObject { { "name", "fetch_size" }, { "description", "Fetch size (rows)" }, { "required", true } }) } } },
                { "prefetching", new JObject { { "name", "prefetching" }, { "description", "Prefetching" }, { "arguments", new JArray(new JObject { { "name", "prefetch_size" }, { "description", "Prefetch size (rows)" }, { "required", true } }) } } },
                { "streaming_results", new JObject { { "name", "streaming_results" }, { "description", "Streaming results" }, { "arguments", new JArray(new JObject { { "name", "query" }, { "description", "Query string" }, { "required", true } }) } } },
                { "pagination", new JObject { { "name", "pagination" }, { "description", "Pagination" }, { "arguments", new JArray(new JObject { { "name", "page_size" }, { "description", "Page size (rows)" }, { "required", true } }) } } },
                { "batch_processing", new JObject { { "name", "batch_processing" }, { "description", "Batch processing" }, { "arguments", new JArray(new JObject { { "name", "batch_size" }, { "description", "Batch size (rows)" }, { "required", true } }) } } },
                { "connection_pool_optimization", new JObject { { "name", "connection_pool_optimization" }, { "description", "Connection pool optimization" }, { "arguments", new JArray(new JObject { { "name", "max_pool_size" }, { "description", "Maximum pool size" }, { "required", true } }) } } },
                { "error_handling", new JObject { { "name", "error_handling" }, { "description", "Error handling" }, { "arguments", new JArray(new JObject { { "name", "error_type" }, { "description", "Error type to handle" }, { "required", true } }) } } },
                { "exception_management", new JObject { { "name", "exception_management" }, { "description", "Exception management" }, { "arguments", new JArray(new JObject { { "name", "procedure_name" }, { "description", "Procedure name" }, { "required", true } }) } } },
                { "query_logging", new JObject { { "name", "query_logging" }, { "description", "Query logging" }, { "arguments", new JArray(new JObject { { "name", "log_level" }, { "description", "Log level (DEBUG/INFO/WARN)" }, { "required", true } }) } } },
                { "debug_mode", new JObject { { "name", "debug_mode" }, { "description", "Debug mode" }, { "arguments", new JArray(new JObject { { "name", "enable_debug" }, { "description", "Enable debug mode (true/false)" }, { "required", true } }) } } },
                { "monitoring_setup", new JObject { { "name", "monitoring_setup" }, { "description", "Monitoring setup" }, { "arguments", new JArray(new JObject { { "name", "metrics" }, { "description", "Metrics to monitor (comma-separated)" }, { "required", true } }) } } },
                { "alert_rules", new JObject { { "name", "alert_rules" }, { "description", "Alert rules" }, { "arguments", new JArray(new JObject { { "name", "metric_name" }, { "description", "Metric name" }, { "required", true } }, new JObject { { "name", "threshold" }, { "description", "Alert threshold" }, { "required", true } }) } } },
                { "unit_testing", new JObject { { "name", "unit_testing" }, { "description", "Unit testing" }, { "arguments", new JArray(new JObject { { "name", "function_name" }, { "description", "Function name" }, { "required", true } }) } } },
                { "integration_testing", new JObject { { "name", "integration_testing" }, { "description", "Integration testing" }, { "arguments", new JArray(new JObject { { "name", "test_name" }, { "description", "Test name" }, { "required", true } }) } } },
                { "data_validation_framework", new JObject { { "name", "data_validation_framework" }, { "description", "Data validation framework" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "documentation_generation", new JObject { { "name", "documentation_generation" }, { "description", "Documentation generation" }, { "arguments", new JArray(new JObject { { "name", "schema_name" }, { "description", "Schema name" }, { "required", true } }) } } },
                { "migration_planning", new JObject { { "name", "migration_planning" }, { "description", "Migration planning" }, { "arguments", new JArray(new JObject { { "name", "source_system" }, { "description", "Source system" }, { "required", true } }, new JObject { { "name", "target_system" }, { "description", "Target system" }, { "required", true } }) } } },
                { "capacity_planning", new JObject { { "name", "capacity_planning" }, { "description", "Capacity planning" }, { "arguments", new JArray(new JObject { { "name", "time_horizon" }, { "description", "Time horizon (months)" }, { "required", true } }) } } },
                { "resource_allocation", new JObject { { "name", "resource_allocation" }, { "description", "Resource allocation" }, { "arguments", new JArray(new JObject { { "name", "department" }, { "description", "Department name" }, { "required", true } }) } } },
                { "integration_patterns", new JObject { { "name", "integration_patterns" }, { "description", "Integration patterns" }, { "arguments", new JArray(new JObject { { "name", "pattern_type" }, { "description", "Pattern type" }, { "required", true } }) } } },
                { "compliance_framework", new JObject { { "name", "compliance_framework" }, { "description", "Compliance framework" }, { "arguments", new JArray(new JObject { { "name", "compliance_standard" }, { "description", "Compliance standard (HIPAA/GDPR/SOC2)" }, { "required", true } }) } } },
                { "query_tuning_advisor", new JObject { { "name", "query_tuning_advisor" }, { "description", "Query tuning advisor" }, { "arguments", new JArray(new JObject { { "name", "query_id" }, { "description", "Query ID" }, { "required", true } }) } } },
                { "index_recommendation", new JObject { { "name", "index_recommendation" }, { "description", "Index recommendation" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }, new JObject { { "name", "workload_type" }, { "description", "Workload type" }, { "required", true } }) } } },
                { "hot_cold_partitioning", new JObject { { "name", "hot_cold_partitioning" }, { "description", "Hot/cold partitioning" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }, new JObject { { "name", "age_threshold" }, { "description", "Age threshold (days)" }, { "required", true } }) } } },
                { "data_archival", new JObject { { "name", "data_archival" }, { "description", "Data archival" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }, new JObject { { "name", "retention_days" }, { "description", "Retention days" }, { "required", true } }) } } },
                { "table_compression", new JObject { { "name", "table_compression" }, { "description", "Table compression" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }, new JObject { { "name", "compression_type" }, { "description", "Compression type" }, { "required", true } }) } } },
                { "delta_merge", new JObject { { "name", "delta_merge" }, { "description", "Delta merge" }, { "arguments", new JArray(new JObject { { "name", "target_table" }, { "description", "Target table" }, { "required", true } }, new JObject { { "name", "delta_table" }, { "description", "Delta table" }, { "required", true } }) } } },
                { "slowly_changing_dimensions", new JObject { { "name", "slowly_changing_dimensions" }, { "description", "Slowly changing dimensions" }, { "arguments", new JArray(new JObject { { "name", "dimension_table" }, { "description", "Dimension table" }, { "required", true } }, new JObject { { "name", "scd_type" }, { "description", "SCD type (1/2/3)" }, { "required", true } }) } } },
                { "fact_table_optimization", new JObject { { "name", "fact_table_optimization" }, { "description", "Fact table optimization" }, { "arguments", new JArray(new JObject { { "name", "fact_table" }, { "description", "Fact table name" }, { "required", true } }) } } },
                { "dimension_table_optimization", new JObject { { "name", "dimension_table_optimization" }, { "description", "Dimension table optimization" }, { "arguments", new JArray(new JObject { { "name", "dimension_table" }, { "description", "Dimension table name" }, { "required", true } }) } } },
                { "star_schema_design", new JObject { { "name", "star_schema_design" }, { "description", "Star schema design" }, { "arguments", new JArray(new JObject { { "name", "fact_table" }, { "description", "Fact table name" }, { "required", true } }) } } },
                { "snowflake_schema_design", new JObject { { "name", "snowflake_schema_design" }, { "description", "Snowflake schema design" }, { "arguments", new JArray(new JObject { { "name", "fact_table" }, { "description", "Fact table name" }, { "required", true } }) } } },
                { "data_vault_modeling", new JObject { { "name", "data_vault_modeling" }, { "description", "Data vault modeling" }, { "arguments", new JArray(new JObject { { "name", "business_entity" }, { "description", "Business entity" }, { "required", true } }) } } },
                { "lambda_architecture", new JObject { { "name", "lambda_architecture" }, { "description", "Lambda architecture" }, { "arguments", new JArray(new JObject { { "name", "data_source" }, { "description", "Data source" }, { "required", true } }) } } },
                { "kappa_architecture", new JObject { { "name", "kappa_architecture" }, { "description", "Kappa architecture" }, { "arguments", new JArray(new JObject { { "name", "streaming_source" }, { "description", "Streaming source" }, { "required", true } }) } } },
                { "medallion_architecture", new JObject { { "name", "medallion_architecture" }, { "description", "Medallion architecture" }, { "arguments", new JArray(new JObject { { "name", "layer" }, { "description", "Layer (bronze/silver/gold)" }, { "required", true } }) } } },
                { "cicd_pipeline", new JObject { { "name", "cicd_pipeline" }, { "description", "CI/CD pipeline" }, { "arguments", new JArray(new JObject { { "name", "pipeline_name" }, { "description", "Pipeline name" }, { "required", true } }) } } },
                { "gitops_workflow", new JObject { { "name", "gitops_workflow" }, { "description", "GitOps workflow" }, { "arguments", new JArray(new JObject { { "name", "repository_url" }, { "description", "Repository URL" }, { "required", true } }) } } },
                { "infrastructure_as_code", new JObject { { "name", "infrastructure_as_code" }, { "description", "Infrastructure as code" }, { "arguments", new JArray(new JObject { { "name", "tool" }, { "description", "Tool (Terraform/Pulumi)" }, { "required", true } }) } } },
                { "blue_green_deployment", new JObject { { "name", "blue_green_deployment" }, { "description", "Blue-green deployment" }, { "arguments", new JArray(new JObject { { "name", "environment" }, { "description", "Environment name" }, { "required", true } }) } } },
                { "canary_deployment", new JObject { { "name", "canary_deployment" }, { "description", "Canary deployment" }, { "arguments", new JArray(new JObject { { "name", "traffic_percentage" }, { "description", "Traffic percentage" }, { "required", true } }) } } },
                { "feature_flags", new JObject { { "name", "feature_flags" }, { "description", "Feature flags" }, { "arguments", new JArray(new JObject { { "name", "flag_name" }, { "description", "Flag name" }, { "required", true } }) } } },
                { "observability_framework", new JObject { { "name", "observability_framework" }, { "description", "Observability framework" }, { "arguments", new JArray(new JObject { { "name", "component" }, { "description", "Component to observe" }, { "required", true } }) } } },
                { "distributed_tracing", new JObject { { "name", "distributed_tracing" }, { "description", "Distributed tracing" }, { "arguments", new JArray(new JObject { { "name", "trace_id" }, { "description", "Trace ID" }, { "required", true } }) } } },
                { "metric_collection", new JObject { { "name", "metric_collection" }, { "description", "Metric collection" }, { "arguments", new JArray(new JObject { { "name", "metric_type" }, { "description", "Metric type" }, { "required", true } }) } } },
                { "log_aggregation", new JObject { { "name", "log_aggregation" }, { "description", "Log aggregation" }, { "arguments", new JArray(new JObject { { "name", "log_source" }, { "description", "Log source" }, { "required", true } }) } } },
                { "anomaly_alerting", new JObject { { "name", "anomaly_alerting" }, { "description", "Anomaly alerting" }, { "arguments", new JArray(new JObject { { "name", "metric_name" }, { "description", "Metric name" }, { "required", true } }, new JObject { { "name", "sensitivity" }, { "description", "Sensitivity level" }, { "required", true } }) } } },
                { "real_time_analytics", new JObject { { "name", "real_time_analytics" }, { "description", "Real-time analytics" }, { "arguments", new JArray(new JObject { { "name", "data_source" }, { "description", "Data source" }, { "required", true } }) } } },
                { "stream_processing", new JObject { { "name", "stream_processing" }, { "description", "Stream processing" }, { "arguments", new JArray(new JObject { { "name", "stream_name" }, { "description", "Stream name" }, { "required", true } }) } } },
                { "event_sourcing", new JObject { { "name", "event_sourcing" }, { "description", "Event sourcing" }, { "arguments", new JArray(new JObject { { "name", "event_store" }, { "description", "Event store name" }, { "required", true } }) } } },
                { "cqrs_pattern", new JObject { { "name", "cqrs_pattern" }, { "description", "CQRS pattern" }, { "arguments", new JArray(new JObject { { "name", "command_model" }, { "description", "Command model name" }, { "required", true } }, new JObject { { "name", "query_model" }, { "description", "Query model name" }, { "required", true } }) } } },
                { "cortex_embeddings", new JObject { { "name", "cortex_embeddings" }, { "description", "Cortex embeddings" }, { "arguments", new JArray(new JObject { { "name", "text_column" }, { "description", "Text column" }, { "required", true } }, new JObject { { "name", "model_name" }, { "description", "Embedding model" }, { "required", true } }) } } },
                { "cortex_image_classification", new JObject { { "name", "cortex_image_classification" }, { "description", "Image classification" }, { "arguments", new JArray(new JObject { { "name", "image_column" }, { "description", "Image column" }, { "required", true } }) } } },
                { "cortex_ocr", new JObject { { "name", "cortex_ocr" }, { "description", "OCR extraction" }, { "arguments", new JArray(new JObject { { "name", "image_path" }, { "description", "Image path" }, { "required", true } }) } } },
                { "vector_search_similarity", new JObject { { "name", "vector_search_similarity" }, { "description", "Vector similarity search" }, { "arguments", new JArray(new JObject { { "name", "query_vector" }, { "description", "Query vector" }, { "required", true } }, new JObject { { "name", "vector_column" }, { "description", "Vector column" }, { "required", true } }) } } },
                { "cortex_model_fine_tuning", new JObject { { "name", "cortex_model_fine_tuning" }, { "description", "Model fine-tuning" }, { "arguments", new JArray(new JObject { { "name", "model_name" }, { "description", "Model name" }, { "required", true } }, new JObject { { "name", "training_data" }, { "description", "Training data table" }, { "required", true } }) } } },
                { "cortex_batch_inference", new JObject { { "name", "cortex_batch_inference" }, { "description", "Batch inference" }, { "arguments", new JArray(new JObject { { "name", "model_name" }, { "description", "Model name" }, { "required", true } }, new JObject { { "name", "batch_table" }, { "description", "Batch table" }, { "required", true } }) } } },
                { "cortex_model_monitoring", new JObject { { "name", "cortex_model_monitoring" }, { "description", "Model monitoring" }, { "arguments", new JArray(new JObject { { "name", "model_name" }, { "description", "Model name" }, { "required", true } }) } } },
                { "cortex_prompt_engineering", new JObject { { "name", "cortex_prompt_engineering" }, { "description", "Prompt engineering" }, { "arguments", new JArray(new JObject { { "name", "prompt_template" }, { "description", "Prompt template" }, { "required", true } }) } } },
                { "cortex_entity_extraction", new JObject { { "name", "cortex_entity_extraction" }, { "description", "Entity extraction" }, { "arguments", new JArray(new JObject { { "name", "text_column" }, { "description", "Text column" }, { "required", true } }) } } },
                { "cortex_classification", new JObject { { "name", "cortex_classification" }, { "description", "Text classification" }, { "arguments", new JArray(new JObject { { "name", "text_column" }, { "description", "Text column" }, { "required", true } }, new JObject { { "name", "categories" }, { "description", "Categories (comma-separated)" }, { "required", true } }) } } },
                { "vector_index_creation", new JObject { { "name", "vector_index_creation" }, { "description", "Vector index creation" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }, new JObject { { "name", "vector_column" }, { "description", "Vector column" }, { "required", true } }) } } },
                { "approximate_nearest_neighbor", new JObject { { "name", "approximate_nearest_neighbor" }, { "description", "ANN search" }, { "arguments", new JArray(new JObject { { "name", "query_vector" }, { "description", "Query vector" }, { "required", true } }, new JObject { { "name", "k" }, { "description", "Number of neighbors" }, { "required", true } }) } } },
                { "embedding_dimension_reduction", new JObject { { "name", "embedding_dimension_reduction" }, { "description", "Dimension reduction" }, { "arguments", new JArray(new JObject { { "name", "vector_column" }, { "description", "Vector column" }, { "required", true } }, new JObject { { "name", "target_dimensions" }, { "description", "Target dimensions" }, { "required", true } }) } } },
                { "cortex_reranking", new JObject { { "name", "cortex_reranking" }, { "description", "Reranking" }, { "arguments", new JArray(new JObject { { "name", "query" }, { "description", "Query text" }, { "required", true } }, new JObject { { "name", "documents_column" }, { "description", "Documents column" }, { "required", true } }) } } },
                { "rag_pattern", new JObject { { "name", "rag_pattern" }, { "description", "RAG pattern" }, { "arguments", new JArray(new JObject { { "name", "knowledge_base" }, { "description", "Knowledge base table" }, { "required", true } }, new JObject { { "name", "llm_model" }, { "description", "LLM model" }, { "required", true } }) } } },
                { "snowpark_dataframe_operations", new JObject { { "name", "snowpark_dataframe_operations" }, { "description", "Snowpark DataFrame operations" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }, new JObject { { "name", "operation" }, { "description", "Operation type" }, { "required", true } }) } } },
                { "snowpark_ml_pipeline", new JObject { { "name", "snowpark_ml_pipeline" }, { "description", "Snowpark ML pipeline" }, { "arguments", new JArray(new JObject { { "name", "pipeline_name" }, { "description", "Pipeline name" }, { "required", true } }) } } },
                { "snowpark_custom_transformer", new JObject { { "name", "snowpark_custom_transformer" }, { "description", "Custom transformer" }, { "arguments", new JArray(new JObject { { "name", "transformer_name" }, { "description", "Transformer name" }, { "required", true } }) } } },
                { "snowpark_distributed_computing", new JObject { { "name", "snowpark_distributed_computing" }, { "description", "Distributed computing" }, { "arguments", new JArray(new JObject { { "name", "computation_type" }, { "description", "Computation type" }, { "required", true } }) } } },
                { "snowpark_python_specific", new JObject { { "name", "snowpark_python_specific" }, { "description", "Python-specific features" }, { "arguments", new JArray(new JObject { { "name", "feature" }, { "description", "Feature name" }, { "required", true } }) } } },
                { "snowpark_scala_specific", new JObject { { "name", "snowpark_scala_specific" }, { "description", "Scala-specific features" }, { "arguments", new JArray(new JObject { { "name", "feature" }, { "description", "Feature name" }, { "required", true } }) } } },
                { "snowpark_java_specific", new JObject { { "name", "snowpark_java_specific" }, { "description", "Java-specific features" }, { "arguments", new JArray(new JObject { { "name", "feature" }, { "description", "Feature name" }, { "required", true } }) } } },
                { "snowpark_feature_engineering", new JObject { { "name", "snowpark_feature_engineering" }, { "description", "Feature engineering" }, { "arguments", new JArray(new JObject { { "name", "feature_name" }, { "description", "Feature name" }, { "required", true } }) } } },
                { "snowpark_model_training", new JObject { { "name", "snowpark_model_training" }, { "description", "Model training" }, { "arguments", new JArray(new JObject { { "name", "model_type" }, { "description", "Model type" }, { "required", true } }) } } },
                { "snowpark_model_deployment", new JObject { { "name", "snowpark_model_deployment" }, { "description", "Model deployment" }, { "arguments", new JArray(new JObject { { "name", "model_name" }, { "description", "Model name" }, { "required", true } }) } } },
                { "snowpark_batch_prediction", new JObject { { "name", "snowpark_batch_prediction" }, { "description", "Batch prediction" }, { "arguments", new JArray(new JObject { { "name", "model_name" }, { "description", "Model name" }, { "required", true } }, new JObject { { "name", "input_table" }, { "description", "Input table" }, { "required", true } }) } } },
                { "snowpark_data_preprocessing", new JObject { { "name", "snowpark_data_preprocessing" }, { "description", "Data preprocessing" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "snowpark_cross_validation", new JObject { { "name", "snowpark_cross_validation" }, { "description", "Cross-validation" }, { "arguments", new JArray(new JObject { { "name", "folds" }, { "description", "Number of folds" }, { "required", true } }) } } },
                { "snowpark_hyperparameter_tuning", new JObject { { "name", "snowpark_hyperparameter_tuning" }, { "description", "Hyperparameter tuning" }, { "arguments", new JArray(new JObject { { "name", "model_type" }, { "description", "Model type" }, { "required", true } }) } } },
                { "snowpark_model_registry_integration", new JObject { { "name", "snowpark_model_registry_integration" }, { "description", "Model registry integration" }, { "arguments", new JArray(new JObject { { "name", "registry_name" }, { "description", "Registry name" }, { "required", true } }) } } },
                { "parquet_optimization", new JObject { { "name", "parquet_optimization" }, { "description", "Parquet optimization" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "avro_schema_evolution", new JObject { { "name", "avro_schema_evolution" }, { "description", "Avro schema evolution" }, { "arguments", new JArray(new JObject { { "name", "schema_name" }, { "description", "Schema name" }, { "required", true } }) } } },
                { "orc_compression", new JObject { { "name", "orc_compression" }, { "description", "ORC compression" }, { "arguments", new JArray(new JObject { { "name", "compression_type" }, { "description", "Compression type" }, { "required", true } }) } } },
                { "delta_lake_integration", new JObject { { "name", "delta_lake_integration" }, { "description", "Delta Lake integration" }, { "arguments", new JArray(new JObject { { "name", "delta_table" }, { "description", "Delta table path" }, { "required", true } }) } } },
                { "apache_hudi_integration", new JObject { { "name", "apache_hudi_integration" }, { "description", "Apache Hudi integration" }, { "arguments", new JArray(new JObject { { "name", "hudi_table" }, { "description", "Hudi table path" }, { "required", true } }) } } },
                { "json_compression", new JObject { { "name", "json_compression" }, { "description", "JSON compression" }, { "arguments", new JArray(new JObject { { "name", "compression_level" }, { "description", "Compression level" }, { "required", true } }) } } },
                { "csv_optimization", new JObject { { "name", "csv_optimization" }, { "description", "CSV optimization" }, { "arguments", new JArray(new JObject { { "name", "delimiter" }, { "description", "Delimiter character" }, { "required", true } }) } } },
                { "file_format_detection", new JObject { { "name", "file_format_detection" }, { "description", "File format detection" }, { "arguments", new JArray(new JObject { { "name", "file_path" }, { "description", "File path" }, { "required", true } }) } } },
                { "columnar_storage_optimization", new JObject { { "name", "columnar_storage_optimization" }, { "description", "Columnar storage optimization" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "compression_codec_selection", new JObject { { "name", "compression_codec_selection" }, { "description", "Compression codec selection" }, { "arguments", new JArray(new JObject { { "name", "data_type" }, { "description", "Data type" }, { "required", true } }) } } },
                { "file_splitting_strategy", new JObject { { "name", "file_splitting_strategy" }, { "description", "File splitting strategy" }, { "arguments", new JArray(new JObject { { "name", "file_size_mb" }, { "description", "Target file size" }, { "required", true } }) } } },
                { "schema_inference", new JObject { { "name", "schema_inference" }, { "description", "Schema inference" }, { "arguments", new JArray(new JObject { { "name", "file_path" }, { "description", "File path" }, { "required", true } }) } } },
                { "file_format_conversion", new JObject { { "name", "file_format_conversion" }, { "description", "File format conversion" }, { "arguments", new JArray(new JObject { { "name", "source_format" }, { "description", "Source format" }, { "required", true } }, new JObject { { "name", "target_format" }, { "description", "Target format" }, { "required", true } }) } } },
                { "nested_data_optimization", new JObject { { "name", "nested_data_optimization" }, { "description", "Nested data optimization" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "data_lake_partitioning", new JObject { { "name", "data_lake_partitioning" }, { "description", "Data lake partitioning" }, { "arguments", new JArray(new JObject { { "name", "partition_columns" }, { "description", "Partition columns" }, { "required", true } }) } } },
                { "attribute_based_access_control", new JObject { { "name", "attribute_based_access_control" }, { "description", "ABAC" }, { "arguments", new JArray(new JObject { { "name", "attribute_name" }, { "description", "Attribute name" }, { "required", true } }) } } },
                { "dynamic_data_masking_policy", new JObject { { "name", "dynamic_data_masking_policy" }, { "description", "Dynamic masking" }, { "arguments", new JArray(new JObject { { "name", "policy_name" }, { "description", "Policy name" }, { "required", true } }) } } },
                { "pii_detection", new JObject { { "name", "pii_detection" }, { "description", "PII detection" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "sensitive_data_discovery", new JObject { { "name", "sensitive_data_discovery" }, { "description", "Sensitive data discovery" }, { "arguments", new JArray(new JObject { { "name", "database_name" }, { "description", "Database name" }, { "required", true } }) } } },
                { "encryption_key_rotation", new JObject { { "name", "encryption_key_rotation" }, { "description", "Key rotation" }, { "arguments", new JArray(new JObject { { "name", "key_name" }, { "description", "Key name" }, { "required", true } }) } } },
                { "external_tokenization", new JObject { { "name", "external_tokenization" }, { "description", "External tokenization" }, { "arguments", new JArray(new JObject { { "name", "tokenization_service" }, { "description", "Service name" }, { "required", true } }) } } },
                { "data_loss_prevention", new JObject { { "name", "data_loss_prevention" }, { "description", "DLP policies" }, { "arguments", new JArray(new JObject { { "name", "policy_name" }, { "description", "Policy name" }, { "required", true } }) } } },
                { "audit_policy_creation", new JObject { { "name", "audit_policy_creation" }, { "description", "Audit policies" }, { "arguments", new JArray(new JObject { { "name", "policy_name" }, { "description", "Policy name" }, { "required", true } }) } } },
                { "session_variable_security", new JObject { { "name", "session_variable_security" }, { "description", "Session variable security" }, { "arguments", new JArray(new JObject { { "name", "variable_name" }, { "description", "Variable name" }, { "required", true } }) } } },
                { "oauth_integration", new JObject { { "name", "oauth_integration" }, { "description", "OAuth integration" }, { "arguments", new JArray(new JObject { { "name", "client_id" }, { "description", "Client ID" }, { "required", true } }) } } },
                { "saml_sso_configuration", new JObject { { "name", "saml_sso_configuration" }, { "description", "SAML SSO" }, { "arguments", new JArray(new JObject { { "name", "idp_url" }, { "description", "IdP URL" }, { "required", true } }) } } },
                { "multi_factor_authentication", new JObject { { "name", "multi_factor_authentication" }, { "description", "MFA" }, { "arguments", new JArray(new JObject { { "name", "mfa_type" }, { "description", "MFA type" }, { "required", true } }) } } },
                { "privileged_access_management", new JObject { { "name", "privileged_access_management" }, { "description", "PAM" }, { "arguments", new JArray(new JObject { { "name", "role_name" }, { "description", "Role name" }, { "required", true } }) } } },
                { "security_event_monitoring", new JObject { { "name", "security_event_monitoring" }, { "description", "Security monitoring" }, { "arguments", new JArray(new JObject { { "name", "event_type" }, { "description", "Event type" }, { "required", true } }) } } },
                { "compliance_reporting", new JObject { { "name", "compliance_reporting" }, { "description", "Compliance reporting" }, { "arguments", new JArray(new JObject { { "name", "report_type" }, { "description", "Report type" }, { "required", true } }) } } },
                { "organization_management", new JObject { { "name", "organization_management" }, { "description", "Organization management" }, { "arguments", new JArray(new JObject { { "name", "organization_name" }, { "description", "Organization name" }, { "required", true } }) } } },
                { "account_provisioning", new JObject { { "name", "account_provisioning" }, { "description", "Account provisioning" }, { "arguments", new JArray(new JObject { { "name", "account_name" }, { "description", "Account name" }, { "required", true } }) } } },
                { "credit_allocation", new JObject { { "name", "credit_allocation" }, { "description", "Credit allocation" }, { "arguments", new JArray(new JObject { { "name", "account_name" }, { "description", "Account name" }, { "required", true } }, new JObject { { "name", "credits" }, { "description", "Credit amount" }, { "required", true } }) } } },
                { "billing_analysis", new JObject { { "name", "billing_analysis" }, { "description", "Billing analysis" }, { "arguments", new JArray(new JObject { { "name", "time_period" }, { "description", "Time period" }, { "required", true } }) } } },
                { "usage_tracking", new JObject { { "name", "usage_tracking" }, { "description", "Usage tracking" }, { "arguments", new JArray(new JObject { { "name", "resource_type" }, { "description", "Resource type" }, { "required", true } }) } } },
                { "resource_quota_management", new JObject { { "name", "resource_quota_management" }, { "description", "Resource quota management" }, { "arguments", new JArray(new JObject { { "name", "quota_type" }, { "description", "Quota type" }, { "required", true } }) } } },
                { "account_replication_setup", new JObject { { "name", "account_replication_setup" }, { "description", "Account replication" }, { "arguments", new JArray(new JObject { { "name", "source_account" }, { "description", "Source account" }, { "required", true } }, new JObject { { "name", "target_account" }, { "description", "Target account" }, { "required", true } }) } } },
                { "cross_region_connectivity", new JObject { { "name", "cross_region_connectivity" }, { "description", "Cross-region connectivity" }, { "arguments", new JArray(new JObject { { "name", "source_region" }, { "description", "Source region" }, { "required", true } }, new JObject { { "name", "target_region" }, { "description", "Target region" }, { "required", true } }) } } },
                { "warehouse_policy_management", new JObject { { "name", "warehouse_policy_management" }, { "description", "Warehouse policies" }, { "arguments", new JArray(new JObject { { "name", "policy_name" }, { "description", "Policy name" }, { "required", true } }) } } },
                { "storage_policy_configuration", new JObject { { "name", "storage_policy_configuration" }, { "description", "Storage policies" }, { "arguments", new JArray(new JObject { { "name", "policy_name" }, { "description", "Policy name" }, { "required", true } }) } } },
                { "account_parameter_tuning", new JObject { { "name", "account_parameter_tuning" }, { "description", "Account parameter tuning" }, { "arguments", new JArray(new JObject { { "name", "parameter_name" }, { "description", "Parameter name" }, { "required", true } }) } } },
                { "organization_billing_dashboard", new JObject { { "name", "organization_billing_dashboard" }, { "description", "Billing dashboard" }, { "arguments", new JArray(new JObject { { "name", "dashboard_name" }, { "description", "Dashboard name" }, { "required", true } }) } } },
                { "cost_center_allocation", new JObject { { "name", "cost_center_allocation" }, { "description", "Cost center allocation" }, { "arguments", new JArray(new JObject { { "name", "cost_center" }, { "description", "Cost center" }, { "required", true } }) } } },
                { "service_level_agreement", new JObject { { "name", "service_level_agreement" }, { "description", "SLA definition" }, { "arguments", new JArray(new JObject { { "name", "sla_type" }, { "description", "SLA type" }, { "required", true } }) } } },
                { "capacity_planning_analysis", new JObject { { "name", "capacity_planning_analysis" }, { "description", "Capacity planning" }, { "arguments", new JArray(new JObject { { "name", "resource_type" }, { "description", "Resource type" }, { "required", true } }) } } },
                { "healthcare_fhir_processing", new JObject { { "name", "healthcare_fhir_processing" }, { "description", "FHIR processing" }, { "arguments", new JArray(new JObject { { "name", "fhir_resource" }, { "description", "FHIR resource type" }, { "required", true } }) } } },
                { "financial_fraud_detection", new JObject { { "name", "financial_fraud_detection" }, { "description", "Fraud detection" }, { "arguments", new JArray(new JObject { { "name", "transaction_table" }, { "description", "Transaction table" }, { "required", true } }) } } },
                { "retail_customer_360", new JObject { { "name", "retail_customer_360" }, { "description", "Customer 360" }, { "arguments", new JArray(new JObject { { "name", "customer_id" }, { "description", "Customer ID" }, { "required", true } }) } } },
                { "manufacturing_quality_control", new JObject { { "name", "manufacturing_quality_control" }, { "description", "Quality control" }, { "arguments", new JArray(new JObject { { "name", "product_line" }, { "description", "Product line" }, { "required", true } }) } } },
                { "iot_sensor_data_processing", new JObject { { "name", "iot_sensor_data_processing" }, { "description", "IoT sensor processing" }, { "arguments", new JArray(new JObject { { "name", "sensor_type" }, { "description", "Sensor type" }, { "required", true } }) } } },
                { "telecommunications_cdr_analysis", new JObject { { "name", "telecommunications_cdr_analysis" }, { "description", "CDR analysis" }, { "arguments", new JArray(new JObject { { "name", "cdr_table" }, { "description", "CDR table" }, { "required", true } }) } } },
                { "insurance_claims_processing", new JObject { { "name", "insurance_claims_processing" }, { "description", "Claims processing" }, { "arguments", new JArray(new JObject { { "name", "claims_table" }, { "description", "Claims table" }, { "required", true } }) } } },
                { "ecommerce_recommendation_engine", new JObject { { "name", "ecommerce_recommendation_engine" }, { "description", "Recommendation engine" }, { "arguments", new JArray(new JObject { { "name", "user_id" }, { "description", "User ID" }, { "required", true } }) } } },
                { "supply_chain_optimization", new JObject { { "name", "supply_chain_optimization" }, { "description", "Supply chain optimization" }, { "arguments", new JArray(new JObject { { "name", "warehouse_id" }, { "description", "Warehouse ID" }, { "required", true } }) } } },
                { "energy_smart_grid_analytics", new JObject { { "name", "energy_smart_grid_analytics" }, { "description", "Smart grid analytics" }, { "arguments", new JArray(new JObject { { "name", "grid_id" }, { "description", "Grid ID" }, { "required", true } }) } } },
                { "media_content_analytics", new JObject { { "name", "media_content_analytics" }, { "description", "Content analytics" }, { "arguments", new JArray(new JObject { { "name", "content_type" }, { "description", "Content type" }, { "required", true } }) } } },
                { "gaming_player_behavior", new JObject { { "name", "gaming_player_behavior" }, { "description", "Player behavior analysis" }, { "arguments", new JArray(new JObject { { "name", "game_id" }, { "description", "Game ID" }, { "required", true } }) } } },
                { "automotive_telematics", new JObject { { "name", "automotive_telematics" }, { "description", "Telematics processing" }, { "arguments", new JArray(new JObject { { "name", "vehicle_id" }, { "description", "Vehicle ID" }, { "required", true } }) } } },
                { "real_estate_market_analysis", new JObject { { "name", "real_estate_market_analysis" }, { "description", "Market analysis" }, { "arguments", new JArray(new JObject { { "name", "region" }, { "description", "Region" }, { "required", true } }) } } },
                { "education_student_analytics", new JObject { { "name", "education_student_analytics" }, { "description", "Student analytics" }, { "arguments", new JArray(new JObject { { "name", "student_id" }, { "description", "Student ID" }, { "required", true } }) } } },
                { "scd_type2_implementation", new JObject { { "name", "scd_type2_implementation" }, { "description", "SCD Type 2" }, { "arguments", new JArray(new JObject { { "name", "dimension_table" }, { "description", "Dimension table" }, { "required", true } }) } } },
                { "scd_type3_implementation", new JObject { { "name", "scd_type3_implementation" }, { "description", "SCD Type 3" }, { "arguments", new JArray(new JObject { { "name", "dimension_table" }, { "description", "Dimension table" }, { "required", true } }) } } },
                { "scd_type6_hybrid", new JObject { { "name", "scd_type6_hybrid" }, { "description", "SCD Type 6 hybrid" }, { "arguments", new JArray(new JObject { { "name", "dimension_table" }, { "description", "Dimension table" }, { "required", true } }) } } },
                { "bridge_table_design", new JObject { { "name", "bridge_table_design" }, { "description", "Bridge table design" }, { "arguments", new JArray(new JObject { { "name", "relationship_type" }, { "description", "Relationship type" }, { "required", true } }) } } },
                { "factless_fact_table", new JObject { { "name", "factless_fact_table" }, { "description", "Factless fact table" }, { "arguments", new JArray(new JObject { { "name", "event_type" }, { "description", "Event type" }, { "required", true } }) } } },
                { "accumulating_snapshot_fact", new JObject { { "name", "accumulating_snapshot_fact" }, { "description", "Accumulating snapshot" }, { "arguments", new JArray(new JObject { { "name", "process_name" }, { "description", "Process name" }, { "required", true } }) } } },
                { "periodic_snapshot_fact", new JObject { { "name", "periodic_snapshot_fact" }, { "description", "Periodic snapshot" }, { "arguments", new JArray(new JObject { { "name", "period" }, { "description", "Period" }, { "required", true } }) } } },
                { "late_arriving_dimension", new JObject { { "name", "late_arriving_dimension" }, { "description", "Late-arriving dimensions" }, { "arguments", new JArray(new JObject { { "name", "dimension_table" }, { "description", "Dimension table" }, { "required", true } }) } } },
                { "incremental_dimension_load", new JObject { { "name", "incremental_dimension_load" }, { "description", "Incremental dimension load" }, { "arguments", new JArray(new JObject { { "name", "dimension_table" }, { "description", "Dimension table" }, { "required", true } }) } } },
                { "full_outer_merge", new JObject { { "name", "full_outer_merge" }, { "description", "Full outer merge" }, { "arguments", new JArray(new JObject { { "name", "source_table" }, { "description", "Source table" }, { "required", true } }, new JObject { { "name", "target_table" }, { "description", "Target table" }, { "required", true } }) } } },
                { "upsert_pattern", new JObject { { "name", "upsert_pattern" }, { "description", "Upsert pattern" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "soft_delete_pattern", new JObject { { "name", "soft_delete_pattern" }, { "description", "Soft delete" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "data_pipeline_orchestration", new JObject { { "name", "data_pipeline_orchestration" }, { "description", "Pipeline orchestration" }, { "arguments", new JArray(new JObject { { "name", "pipeline_name" }, { "description", "Pipeline name" }, { "required", true } }) } } },
                { "dependency_management", new JObject { { "name", "dependency_management" }, { "description", "Dependency management" }, { "arguments", new JArray(new JObject { { "name", "task_name" }, { "description", "Task name" }, { "required", true } }) } } },
                { "error_recovery_pattern", new JObject { { "name", "error_recovery_pattern" }, { "description", "Error recovery" }, { "arguments", new JArray(new JObject { { "name", "recovery_strategy" }, { "description", "Recovery strategy" }, { "required", true } }) } } },
                { "idempotent_pipeline", new JObject { { "name", "idempotent_pipeline" }, { "description", "Idempotent pipeline" }, { "arguments", new JArray(new JObject { { "name", "pipeline_name" }, { "description", "Pipeline name" }, { "required", true } }) } } },
                { "query_timeout_debugging", new JObject { { "name", "query_timeout_debugging" }, { "description", "Query timeout debugging" }, { "arguments", new JArray(new JObject { { "name", "query_id" }, { "description", "Query ID" }, { "required", true } }) } } },
                { "memory_spill_analysis", new JObject { { "name", "memory_spill_analysis" }, { "description", "Memory spill analysis" }, { "arguments", new JArray(new JObject { { "name", "query_id" }, { "description", "Query ID" }, { "required", true } }) } } },
                { "warehouse_contention", new JObject { { "name", "warehouse_contention" }, { "description", "Warehouse contention" }, { "arguments", new JArray(new JObject { { "name", "warehouse_name" }, { "description", "Warehouse name" }, { "required", true } }) } } },
                { "slow_query_diagnosis", new JObject { { "name", "slow_query_diagnosis" }, { "description", "Slow query diagnosis" }, { "arguments", new JArray(new JObject { { "name", "query_id" }, { "description", "Query ID" }, { "required", true } }) } } },
                { "lock_timeout_resolution", new JObject { { "name", "lock_timeout_resolution" }, { "description", "Lock timeout resolution" }, { "arguments", new JArray(new JObject { { "name", "session_id" }, { "description", "Session ID" }, { "required", true } }) } } },
                { "session_hung_debugging", new JObject { { "name", "session_hung_debugging" }, { "description", "Session hung debugging" }, { "arguments", new JArray(new JObject { { "name", "session_id" }, { "description", "Session ID" }, { "required", true } }) } } },
                { "out_of_memory_errors", new JObject { { "name", "out_of_memory_errors" }, { "description", "OOM errors" }, { "arguments", new JArray(new JObject { { "name", "query_id" }, { "description", "Query ID" }, { "required", true } }) } } },
                { "connection_pooling_issues", new JObject { { "name", "connection_pooling_issues" }, { "description", "Connection pooling" }, { "arguments", new JArray(new JObject { { "name", "pool_name" }, { "description", "Pool name" }, { "required", true } }) } } },
                { "network_latency_diagnosis", new JObject { { "name", "network_latency_diagnosis" }, { "description", "Network latency" }, { "arguments", new JArray(new JObject { { "name", "account_name" }, { "description", "Account name" }, { "required", true } }) } } },
                { "authentication_failures", new JObject { { "name", "authentication_failures" }, { "description", "Authentication failures" }, { "arguments", new JArray(new JObject { { "name", "user_name" }, { "description", "User name" }, { "required", true } }) } } },
                { "permission_denied_errors", new JObject { { "name", "permission_denied_errors" }, { "description", "Permission errors" }, { "arguments", new JArray(new JObject { { "name", "object_name" }, { "description", "Object name" }, { "required", true } }) } } },
                { "data_type_mismatch", new JObject { { "name", "data_type_mismatch" }, { "description", "Data type mismatch" }, { "arguments", new JArray(new JObject { { "name", "column_name" }, { "description", "Column name" }, { "required", true } }) } } },
                { "compilation_errors", new JObject { { "name", "compilation_errors" }, { "description", "Compilation errors" }, { "arguments", new JArray(new JObject { { "name", "query_text" }, { "description", "Query text" }, { "required", true } }) } } },
                { "execution_plan_analysis", new JObject { { "name", "execution_plan_analysis" }, { "description", "Execution plan analysis" }, { "arguments", new JArray(new JObject { { "name", "query_id" }, { "description", "Query ID" }, { "required", true } }) } } },
                { "bottleneck_identification", new JObject { { "name", "bottleneck_identification" }, { "description", "Bottleneck identification" }, { "arguments", new JArray(new JObject { { "name", "query_id" }, { "description", "Query ID" }, { "required", true } }) } } },
                { "resource_exhaustion", new JObject { { "name", "resource_exhaustion" }, { "description", "Resource exhaustion" }, { "arguments", new JArray(new JObject { { "name", "resource_type" }, { "description", "Resource type" }, { "required", true } }) } } },
                { "tableau_live_connection", new JObject { { "name", "tableau_live_connection" }, { "description", "Tableau live connection" }, { "arguments", new JArray(new JObject { { "name", "database_name" }, { "description", "Database name" }, { "required", true } }) } } },
                { "powerbi_direct_query", new JObject { { "name", "powerbi_direct_query" }, { "description", "Power BI DirectQuery" }, { "arguments", new JArray(new JObject { { "name", "dataset_name" }, { "description", "Dataset name" }, { "required", true } }) } } },
                { "looker_pdt_optimization", new JObject { { "name", "looker_pdt_optimization" }, { "description", "Looker PDT optimization" }, { "arguments", new JArray(new JObject { { "name", "pdt_name" }, { "description", "PDT name" }, { "required", true } }) } } },
                { "dbt_incremental_model", new JObject { { "name", "dbt_incremental_model" }, { "description", "dbt incremental model" }, { "arguments", new JArray(new JObject { { "name", "model_name" }, { "description", "Model name" }, { "required", true } }) } } },
                { "dbt_snapshot", new JObject { { "name", "dbt_snapshot" }, { "description", "dbt snapshot" }, { "arguments", new JArray(new JObject { { "name", "snapshot_name" }, { "description", "Snapshot name" }, { "required", true } }) } } },
                { "dbt_test_creation", new JObject { { "name", "dbt_test_creation" }, { "description", "dbt tests" }, { "arguments", new JArray(new JObject { { "name", "test_type" }, { "description", "Test type" }, { "required", true } }) } } },
                { "dbt_macro_development", new JObject { { "name", "dbt_macro_development" }, { "description", "dbt macros" }, { "arguments", new JArray(new JObject { { "name", "macro_name" }, { "description", "Macro name" }, { "required", true } }) } } },
                { "tableau_extract_refresh", new JObject { { "name", "tableau_extract_refresh" }, { "description", "Tableau extract refresh" }, { "arguments", new JArray(new JObject { { "name", "extract_name" }, { "description", "Extract name" }, { "required", true } }) } } },
                { "powerbi_dataflow", new JObject { { "name", "powerbi_dataflow" }, { "description", "Power BI dataflow" }, { "arguments", new JArray(new JObject { { "name", "dataflow_name" }, { "description", "Dataflow name" }, { "required", true } }) } } },
                { "looker_aggregate_table", new JObject { { "name", "looker_aggregate_table" }, { "description", "Looker aggregate table" }, { "arguments", new JArray(new JObject { { "name", "table_name" }, { "description", "Table name" }, { "required", true } }) } } },
                { "superset_integration", new JObject { { "name", "superset_integration" }, { "description", "Superset integration" }, { "arguments", new JArray(new JObject { { "name", "database_name" }, { "description", "Database name" }, { "required", true } }) } } },
                { "metabase_query_optimization", new JObject { { "name", "metabase_query_optimization" }, { "description", "Metabase optimization" }, { "arguments", new JArray(new JObject { { "name", "query_name" }, { "description", "Query name" }, { "required", true } }) } } },
                { "qlik_sense_integration", new JObject { { "name", "qlik_sense_integration" }, { "description", "Qlik Sense integration" }, { "arguments", new JArray(new JObject { { "name", "app_name" }, { "description", "App name" }, { "required", true } }) } } },
                { "sisense_elasticube", new JObject { { "name", "sisense_elasticube" }, { "description", "Sisense ElastiCube" }, { "arguments", new JArray(new JObject { { "name", "cube_name" }, { "description", "Cube name" }, { "required", true } }) } } },
                { "thoughtspot_embrace", new JObject { { "name", "thoughtspot_embrace" }, { "description", "ThoughtSpot Embrace" }, { "arguments", new JArray(new JObject { { "name", "connection_name" }, { "description", "Connection name" }, { "required", true } }) } } },
                { "visualization_best_practices", new JObject { { "name", "visualization_best_practices" }, { "description", "Visualization best practices" }, { "arguments", new JArray(new JObject { { "name", "tool_name" }, { "description", "Tool name" }, { "required", true } }) } } },
                { "multi_tenant_architecture", new JObject { { "name", "multi_tenant_architecture" }, { "description", "Multi-tenant architecture" }, { "arguments", new JArray(new JObject { { "name", "tenant_model" }, { "description", "Tenant model" }, { "required", true } }) } } },
                { "chargeback_model", new JObject { { "name", "chargeback_model" }, { "description", "Chargeback model" }, { "arguments", new JArray(new JObject { { "name", "department" }, { "description", "Department" }, { "required", true } }) } } },
                { "query_prioritization", new JObject { { "name", "query_prioritization" }, { "description", "Query prioritization" }, { "arguments", new JArray(new JObject { { "name", "priority_level" }, { "description", "Priority level" }, { "required", true } }) } } },
                { "workload_isolation", new JObject { { "name", "workload_isolation" }, { "description", "Workload isolation" }, { "arguments", new JArray(new JObject { { "name", "workload_type" }, { "description", "Workload type" }, { "required", true } }) } } },
                { "resource_governor", new JObject { { "name", "resource_governor" }, { "description", "Resource governor" }, { "arguments", new JArray(new JObject { { "name", "policy_name" }, { "description", "Policy name" }, { "required", true } }) } } },
                { "tenant_isolation_pattern", new JObject { { "name", "tenant_isolation_pattern" }, { "description", "Tenant isolation" }, { "arguments", new JArray(new JObject { { "name", "isolation_level" }, { "description", "Isolation level" }, { "required", true } }) } } },
                { "shared_schema_pattern", new JObject { { "name", "shared_schema_pattern" }, { "description", "Shared schema" }, { "arguments", new JArray(new JObject { { "name", "schema_name" }, { "description", "Schema name" }, { "required", true } }) } } },
                { "schema_per_tenant", new JObject { { "name", "schema_per_tenant" }, { "description", "Schema-per-tenant" }, { "arguments", new JArray(new JObject { { "name", "tenant_id" }, { "description", "Tenant ID" }, { "required", true } }) } } },
                { "database_per_tenant", new JObject { { "name", "database_per_tenant" }, { "description", "Database-per-tenant" }, { "arguments", new JArray(new JObject { { "name", "tenant_id" }, { "description", "Tenant ID" }, { "required", true } }) } } },
                { "workload_classification", new JObject { { "name", "workload_classification" }, { "description", "Workload classification" }, { "arguments", new JArray(new JObject { { "name", "classifier_name" }, { "description", "Classifier name" }, { "required", true } }) } } },
                { "queue_management", new JObject { { "name", "queue_management" }, { "description", "Queue management" }, { "arguments", new JArray(new JObject { { "name", "queue_name" }, { "description", "Queue name" }, { "required", true } }) } } },
                { "concurrency_scaling_policy", new JObject { { "name", "concurrency_scaling_policy" }, { "description", "Concurrency scaling" }, { "arguments", new JArray(new JObject { { "name", "warehouse_name" }, { "description", "Warehouse name" }, { "required", true } }) } } },
                { "resource_group_management", new JObject { { "name", "resource_group_management" }, { "description", "Resource groups" }, { "arguments", new JArray(new JObject { { "name", "group_name" }, { "description", "Group name" }, { "required", true } }) } } },
                { "tenant_onboarding", new JObject { { "name", "tenant_onboarding" }, { "description", "Tenant onboarding" }, { "arguments", new JArray(new JObject { { "name", "tenant_name" }, { "description", "Tenant name" }, { "required", true } }) } } },
                { "tenant_offboarding", new JObject { { "name", "tenant_offboarding" }, { "description", "Tenant offboarding" }, { "arguments", new JArray(new JObject { { "name", "tenant_name" }, { "description", "Tenant name" }, { "required", true } }) } } },
                { "cross_tenant_analytics", new JObject { { "name", "cross_tenant_analytics" }, { "description", "Cross-tenant analytics" }, { "arguments", new JArray(new JObject { { "name", "metric_name" }, { "description", "Metric name" }, { "required", true } }) } } },
                { "zero_downtime_migration", new JObject { { "name", "zero_downtime_migration" }, { "description", "Zero-downtime migration" }, { "arguments", new JArray(new JObject { { "name", "source_system" }, { "description", "Source system" }, { "required", true } }, new JObject { { "name", "target_system" }, { "description", "Target system" }, { "required", true } }) } } },
                { "cross_cloud_replication", new JObject { { "name", "cross_cloud_replication" }, { "description", "Cross-cloud replication" }, { "arguments", new JArray(new JObject { { "name", "source_cloud" }, { "description", "Source cloud" }, { "required", true } }, new JObject { { "name", "target_cloud" }, { "description", "Target cloud" }, { "required", true } }) } } },
                { "data_lakehouse_pattern", new JObject { { "name", "data_lakehouse_pattern" }, { "description", "Data lakehouse" }, { "arguments", new JArray(new JObject { { "name", "storage_format" }, { "description", "Storage format" }, { "required", true } }) } } },
                { "streaming_analytics", new JObject { { "name", "streaming_analytics" }, { "description", "Streaming analytics" }, { "arguments", new JArray(new JObject { { "name", "stream_source" }, { "description", "Stream source" }, { "required", true } }) } } },
                { "real_time_data_warehouse", new JObject { { "name", "real_time_data_warehouse" }, { "description", "Real-time warehouse" }, { "arguments", new JArray(new JObject { { "name", "latency_requirement" }, { "description", "Latency requirement" }, { "required", true } }) } } },
                { "modern_data_stack", new JObject { { "name", "modern_data_stack" }, { "description", "Modern data stack" }, { "arguments", new JArray(new JObject { { "name", "stack_components" }, { "description", "Stack components" }, { "required", true } }) } } },
                { "reverse_etl", new JObject { { "name", "reverse_etl" }, { "description", "Reverse ETL" }, { "arguments", new JArray(new JObject { { "name", "destination_system" }, { "description", "Destination system" }, { "required", true } }) } } },
                { "data_mesh_implementation", new JObject { { "name", "data_mesh_implementation" }, { "description", "Data mesh" }, { "arguments", new JArray(new JObject { { "name", "domain_name" }, { "description", "Domain name" }, { "required", true } }) } } },
                { "data_product_catalog", new JObject { { "name", "data_product_catalog" }, { "description", "Data product catalog" }, { "arguments", new JArray(new JObject { { "name", "product_name" }, { "description", "Product name" }, { "required", true } }) } } },
                { "federated_governance", new JObject { { "name", "federated_governance" }, { "description", "Federated governance" }, { "arguments", new JArray(new JObject { { "name", "governance_domain" }, { "description", "Governance domain" }, { "required", true } }) } } },
                { "data_quality_framework", new JObject { { "name", "data_quality_framework" }, { "description", "Data quality framework" }, { "arguments", new JArray(new JObject { { "name", "framework_type" }, { "description", "Framework type" }, { "required", true } }) } } },
                { "metadata_driven_pipeline", new JObject { { "name", "metadata_driven_pipeline" }, { "description", "Metadata-driven pipeline" }, { "arguments", new JArray(new JObject { { "name", "pipeline_name" }, { "description", "Pipeline name" }, { "required", true } }) } } },
                { "schema_registry_integration", new JObject { { "name", "schema_registry_integration" }, { "description", "Schema registry integration" }, { "arguments", new JArray(new JObject { { "name", "registry_type" }, { "description", "Registry type" }, { "required", true } }) } } },
                { "data_versioning", new JObject { { "name", "data_versioning" }, { "description", "Data versioning" }, { "arguments", new JArray(new JObject { { "name", "version_strategy" }, { "description", "Version strategy" }, { "required", true } }) } } },
                { "active_active_deployment", new JObject { { "name", "active_active_deployment" }, { "description", "Active-active deployment" }, { "arguments", new JArray(new JObject { { "name", "region1" }, { "description", "Region 1" }, { "required", true } }, new JObject { { "name", "region2" }, { "description", "Region 2" }, { "required", true } }) } } },
                { "comprehensive_monitoring", new JObject { { "name", "comprehensive_monitoring" }, { "description", "Comprehensive monitoring" }, { "arguments", new JArray(new JObject { { "name", "monitoring_scope" }, { "description", "Monitoring scope" }, { "required", true } }) } } },
                { "get_account_structure", new JObject { { "name", "get_account_structure" }, { "description", "Retrieve account structure including databases, warehouses, and users - returns key resources" }, { "arguments", new JArray(new JObject { { "name", "resource_type" }, { "description", "Resource type: databases, warehouses, users, all" }, { "required", false } }) } } },
                { "get_security_posture", new JObject { { "name", "get_security_posture" }, { "description", "Retrieve security configuration including roles, grants, and masking policies - returns security resources" }, { "arguments", new JArray(new JObject { { "name", "detail_level" }, { "description", "Detail level: summary, detailed" }, { "required", false } }) } } },
                { "get_data_pipeline_status", new JObject { { "name", "get_data_pipeline_status" }, { "description", "Retrieve data pipeline resources including tasks, pipes, and streams with current status" }, { "arguments", new JArray(new JObject { { "name", "pipeline_type" }, { "description", "Pipeline type: tasks, pipes, streams, all" }, { "required", false } }) } } },
                { "get_query_performance_metrics", new JObject { { "name", "get_query_performance_metrics" }, { "description", "Retrieve query execution data and performance metrics - returns query resources and metrics" }, { "arguments", new JArray(new JObject { { "name", "metric_type" }, { "description", "Metric type: recent, slow, failed, all" }, { "required", false } }) } } },
                { "get_resource_catalog", new JObject { { "name", "get_resource_catalog" }, { "description", "Retrieve complete catalog of available resources organized by category" }, { "arguments", new JArray(new JObject { { "name", "category" }, { "description", "Category: data-objects, compute, security, pipelines, integration, monitoring, all" }, { "required", false } }) } } },
                { "inspect_resource", new JObject { { "name", "inspect_resource" }, { "description", "Inspect a specific resource by URI and retrieve its detailed information" }, { "arguments", new JArray(new JObject { { "name", "resource_uri" }, { "description", "Resource URI (e.g., snowflake://databases/mydb or snowflake://account/roles)" }, { "required", true } }) } } }
            };

            var prompt = promptTemplates.ContainsKey(promptName) ? promptTemplates[promptName] : null;

            var promptResponse = new JObject
            {
                { "jsonrpc", "2.0" },
                { "id", requestObject?["id"] ?? 1 },
                { "result", prompt ?? new JObject() }
            };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonConvert.SerializeObject(promptResponse), Encoding.UTF8, "application/json")
            };
        }

        if (method == "resources/list")
        {
            await LogToAppInsights("ResourcesListHandled", new { Method = "resources/list" });

            var resourceResponse = new JObject
            {
                { "jsonrpc", "2.0" },
                { "id", requestObject?["id"] ?? 1 },
                { "result", new JObject
                    {
                        { "resources", new JArray(
                            new JObject 
                            { 
                                { "uri", "snowflake://databases" }, 
                                { "name", "Databases" },
                                { "description", "List all databases in the account" },
                                { "mimeType", "application/json" }
                            },
                            new JObject 
                            { 
                                { "uri", "snowflake://warehouses" }, 
                                { "name", "Warehouses" },
                                { "description", "List all warehouses and their status" },
                                { "mimeType", "application/json" }
                            },
                            new JObject 
                            { 
                                { "uri", "snowflake://account/usage" }, 
                                { "name", "Account Usage" },
                                { "description", "Account credit usage and metrics" },
                                { "mimeType", "application/json" }
                            },
                            new JObject 
                            { 
                                { "uri", "snowflake://account/users" }, 
                                { "name", "Account Users" },
                                { "description", "Active users in the account" },
                                { "mimeType", "application/json" }
                            },
                            new JObject 
                            { 
                                { "uri", "snowflake://account/sessions" }, 
                                { "name", "Active Sessions" },
                                { "description", "Current sessions in the account" },
                                { "mimeType", "application/json" }
                            },
                            new JObject 
                            { 
                                { "uri", "snowflake://account/roles" }, 
                                { "name", "Account Roles" },
                                { "description", "Roles defined in the account" },
                                { "mimeType", "application/json" }
                            },
                            new JObject 
                            { 
                                { "uri", "snowflake://databases" }, 
                                { "name", "Database Schemas and Tables" },
                                { "description", "Navigate database structure and table metadata" },
                                { "mimeType", "application/json" }
                            },
                            new JObject 
                            { 
                                { "uri", "snowflake://account/external-tables" }, 
                                { "name", "External Tables" },
                                { "description", "External table definitions" },
                                { "mimeType", "application/json" }
                            },
                            new JObject 
                            { 
                                { "uri", "snowflake://account/tasks" }, 
                                { "name", "Tasks" },
                                { "description", "Scheduled tasks and their status" },
                                { "mimeType", "application/json" }
                            },
                            new JObject 
                            { 
                                { "uri", "snowflake://account/pipes" }, 
                                { "name", "Pipes" },
                                { "description", "Data pipes and their state" },
                                { "mimeType", "application/json" }
                            },
                            new JObject 
                            { 
                                { "uri", "snowflake://account/streams" }, 
                                { "name", "Streams" },
                                { "description", "Change data capture streams" },
                                { "mimeType", "application/json" }
                            },
                            new JObject 
                            { 
                                { "uri", "snowflake://account/compute-pools" }, 
                                { "name", "Compute Pools" },
                                { "description", "Compute pools for Snowpark jobs" },
                                { "mimeType", "application/json" }
                            },
                            new JObject 
                            { 
                                { "uri", "snowflake://account/parameters" }, 
                                { "name", "Account Parameters" },
                                { "description", "Account-level configuration settings" },
                                { "mimeType", "application/json" }
                            },
                            new JObject 
                            { 
                                { "uri", "snowflake://account/password-policies" }, 
                                { "name", "Password Policies" },
                                { "description", "Password policy definitions" },
                                { "mimeType", "application/json" }
                            },
                            new JObject 
                            { 
                                { "uri", "snowflake://account/alerts" }, 
                                { "name", "Alerts" },
                                { "description", "Alerts and notifications" },
                                { "mimeType", "application/json" }
                            },
                            new JObject 
                            { 
                                { "uri", "snowflake://account/grants" }, 
                                { "name", "Grants and Permissions" },
                                { "description", "Account-level grants and role permissions" },
                                { "mimeType", "application/json" }
                            },
                            new JObject 
                            { 
                                { "uri", "snowflake://account/masking-policies" }, 
                                { "name", "Masking Policies" },
                                { "description", "Data masking policies" },
                                { "mimeType", "application/json" }
                            },
                            new JObject 
                            { 
                                { "uri", "snowflake://account/secrets" }, 
                                { "name", "Secrets" },
                                { "description", "Secrets in Secret Management" },
                                { "mimeType", "application/json" }
                            },
                            new JObject 
                            { 
                                { "uri", "snowflake://account/stages" }, 
                                { "name", "External Stages" },
                                { "description", "External stages (S3, Azure, GCS)" },
                                { "mimeType", "application/json" }
                            },
                            new JObject 
                            { 
                                { "uri", "snowflake://account/integrations" }, 
                                { "name", "Integrations" },
                                { "description", "Integrations (Kafka, webhooks, APIs)" },
                                { "mimeType", "application/json" }
                            },
                            new JObject 
                            { 
                                { "uri", "snowflake://account/shares" }, 
                                { "name", "Data Shares" },
                                { "description", "Outbound data shares and marketplaces" },
                                { "mimeType", "application/json" }
                            },
                            new JObject 
                            { 
                                { "uri", "snowflake://account/dynamic-tables" }, 
                                { "name", "Dynamic Tables" },
                                { "description", "Dynamic tables with automatic refresh" },
                                { "mimeType", "application/json" }
                            },
                            new JObject 
                            { 
                                { "uri", "snowflake://account/iceberg-tables" }, 
                                { "name", "Iceberg Tables" },
                                { "description", "Tables in Iceberg format" },
                                { "mimeType", "application/json" }
                            },
                            new JObject 
                            { 
                                { "uri", "snowflake://account/hybrid-tables" }, 
                                { "name", "Hybrid Tables" },
                                { "description", "Hybrid tables for OLTP workloads" },
                                { "mimeType", "application/json" }
                            },
                            new JObject 
                            { 
                                { "uri", "snowflake://account/queries" }, 
                                { "name", "Query History" },
                                { "description", "Recent queries and performance metrics" },
                                { "mimeType", "application/json" }
                            },
                            new JObject 
                            { 
                                { "uri", "snowflake://account/failed-queries" }, 
                                { "name", "Failed Queries" },
                                { "description", "Failed queries for debugging" },
                                { "mimeType", "application/json" }
                            }
                        )}
                    }
                }
            };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonConvert.SerializeObject(resourceResponse), Encoding.UTF8, "application/json")
            };
        }

        if (method == "resources/read")
        {
            var uri = requestObject?["params"]?["uri"]?.ToString();
            var requestId = requestObject?["id"]?.Value<int?>();

            if (string.IsNullOrWhiteSpace(uri))
            {
                await LogToAppInsights("ResourcesReadError", new { Error = "Missing URI" });
                return CreateErrorResponse(requestId, "Resource URI is required", "MISSING_PARAMETER");
            }

            if (!IsValidResourceUri(uri))
            {
                await LogToAppInsights("ResourcesReadError", new { Error = "Invalid URI format", Uri = uri });
                return CreateErrorResponse(requestId, "Invalid resource URI format", "INVALID_URI");
            }

            await LogToAppInsights("ResourcesReadHandled", new { Method = "resources/read", Uri = uri });

            JObject contents = null;
            string mimeType = "application/json";

            if (uri == "snowflake://databases")
            {
                contents = new JObject
                {
                    { "description", "All databases in the account" },
                    { "query", "SHOW DATABASES" },
                    { "note", "Use tools/call to execute 'SHOW DATABASES' or 'SELECT DATABASE_NAME FROM INFORMATION_SCHEMA.DATABASES' to fetch actual data" }
                };
            }
            else if (uri == "snowflake://warehouses")
            {
                contents = new JObject
                {
                    { "description", "All warehouses and their status" },
                    { "queries", new JArray(
                        "SHOW WAREHOUSES",
                        "SELECT * FROM SNOWFLAKE.ACCOUNT_USAGE.WAREHOUSES WHERE DELETED_ON IS NULL"
                    )},
                    { "note", "Use tools/call to execute these queries to see warehouse names, sizes, and current status" }
                };
            }
            else if (uri == "snowflake://account/usage")
            {
                contents = new JObject
                {
                    { "description", "Account credit usage and metrics" },
                    { "queries", new JArray(
                        "SELECT TRUNC(USAGE_DATE) as date, SUM(CREDITS_USED) as credits_used FROM SNOWFLAKE.ACCOUNT_USAGE.METERING_HISTORY GROUP BY 1 ORDER BY 1 DESC LIMIT 30",
                        "SELECT * FROM SNOWFLAKE.ACCOUNT_USAGE.QUERY_HISTORY LIMIT 100",
                        "SELECT WAREHOUSE_NAME, SUM(CREDITS_USED) FROM SNOWFLAKE.ACCOUNT_USAGE.WAREHOUSE_METERING_HISTORY GROUP BY 1"
                    )},
                    { "note", "Use tools/call to execute these queries for credit usage, query history, and warehouse costs" }
                };
            }
            else if (uri == "snowflake://account/users")
            {
                contents = new JObject
                {
                    { "description", "Active users in the account" },
                    { "queries", new JArray(
                        "SHOW USERS",
                        "SELECT NAME, CREATED_ON, LAST_SUCCESS_LOGIN FROM SNOWFLAKE.ACCOUNT_USAGE.USERS"
                    )},
                    { "note", "Use tools/call to execute these queries to see user accounts and their login history" }
                };
            }
            else if (uri == "snowflake://account/sessions")
            {
                contents = new JObject
                {
                    { "description", "Current sessions in the account" },
                    { "queries", new JArray(
                        "SELECT * FROM SNOWFLAKE.ACCOUNT_USAGE.LOGIN_HISTORY WHERE IS_SUCCESS = 'YES' ORDER BY EVENT_TIMESTAMP DESC LIMIT 50",
                        "SHOW SESSIONS"
                    )},
                    { "note", "Use tools/call to execute these queries to see active sessions and login activity" }
                };
            }
            else if (uri == "snowflake://account/roles")
            {
                contents = new JObject
                {
                    { "description", "Roles defined in the account" },
                    { "queries", new JArray(
                        "SHOW ROLES",
                        "SELECT NAME, CREATED_ON FROM SNOWFLAKE.ACCOUNT_USAGE.ROLES"
                    )},
                    { "note", "Use tools/call to execute these queries to see all roles and their creation dates" }
                };
            }
            else if (uri?.StartsWith("snowflake://databases/") == true)
            {
                var db = uri.Substring("snowflake://databases/".Length).Split('/')[0];
                
                if (!IsValidDatabaseOrSchemaName(db))
                {
                    await LogToAppInsights("ResourcesReadError", new { Error = "Invalid database name", Database = db });
                    return CreateErrorResponse(requestId, "Invalid database name format", "INVALID_IDENTIFIER");
                }

                contents = new JObject
                {
                    { "description", $"Schemas in database {db}" },
                    { "queries", new JArray(
                        $"SHOW SCHEMAS IN DATABASE {db}",
                        $"SELECT SCHEMA_NAME FROM {db}.INFORMATION_SCHEMA.SCHEMATA"
                    )},
                    { "note", "Use tools/call to execute these queries to list schemas in this database" }
                };
            }
            else if (uri?.StartsWith("snowflake://databases/") == true && uri.Contains("/schemas/"))
            {
                var parts = uri.Substring("snowflake://databases/".Length).Split(new[] { "/schemas/" }, StringSplitOptions.None);
                var db = parts[0];
                var schema = parts.Length > 1 ? parts[1].Split('/')[0] : null;
                
                if (!IsValidDatabaseOrSchemaName(db) || !IsValidDatabaseOrSchemaName(schema))
                {
                    await LogToAppInsights("ResourcesReadError", new { Error = "Invalid identifier", Database = db, Schema = schema });
                    return CreateErrorResponse(requestId, "Invalid database or schema name format", "INVALID_IDENTIFIER");
                }

                contents = new JObject
                {
                    { "description", $"Tables and views in {db}.{schema}" },
                    { "queries", new JArray(
                        $"SHOW TABLES IN {db}.{schema}",
                        $"SHOW VIEWS IN {db}.{schema}",
                        $"SELECT TABLE_NAME, TABLE_TYPE FROM {db}.INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = '{schema}'"
                    )},
                    { "note", "Use tools/call to execute these queries to list objects in this schema" }
                };
            }
            else if (uri?.StartsWith("snowflake://databases/") == true && uri.Contains("/schemas/") && uri.Contains("/tables/"))
            {
                var parts = uri.Substring("snowflake://databases/".Length).Split(new[] { "/schemas/" }, StringSplitOptions.None);
                var db = parts[0];
                var remaining = parts.Length > 1 ? parts[1].Split(new[] { "/tables/" }, StringSplitOptions.None) : new string[] { };
                var schema = remaining.Length > 0 ? remaining[0] : null;
                var table = remaining.Length > 1 ? remaining[1] : null;
                
                if (!IsValidDatabaseOrSchemaName(db) || !IsValidDatabaseOrSchemaName(schema) || !IsValidDatabaseOrSchemaName(table))
                {
                    await LogToAppInsights("ResourcesReadError", new { Error = "Invalid identifier", Database = db, Schema = schema, Table = table });
                    return CreateErrorResponse(requestId, "Invalid database, schema, or table name format", "INVALID_IDENTIFIER");
                }

                contents = new JObject
                {
                    { "description", $"Column metadata for {db}.{schema}.{table}" },
                    { "queries", new JArray(
                        $"DESCRIBE TABLE {db}.{schema}.{table}",
                        $"SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE, COLUMN_DEFAULT FROM {db}.INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = '{schema}' AND TABLE_NAME = '{table}'",
                        $"SELECT * FROM {db}.INFORMATION_SCHEMA.TABLE_CONSTRAINTS WHERE TABLE_SCHEMA = '{schema}' AND TABLE_NAME = '{table}'"
                    )},
                    { "note", "Use tools/call to execute these queries to see table structure, columns, and constraints" }
                };
            }
            else if (uri?.StartsWith("snowflake://databases/") == true && uri.Contains("/schemas/") && uri.Contains("/procedures"))
            {
                var parts = uri.Substring("snowflake://databases/".Length).Split(new[] { "/schemas/" }, StringSplitOptions.None);
                var db = parts[0];
                var schema = parts.Length > 1 ? parts[1].Split('/')[0] : null;
                
                if (!IsValidDatabaseOrSchemaName(db) || !IsValidDatabaseOrSchemaName(schema))
                {
                    await LogToAppInsights("ResourcesReadError", new { Error = "Invalid identifier", Database = db, Schema = schema });
                    return CreateErrorResponse(requestId, "Invalid database or schema name format", "INVALID_IDENTIFIER");
                }

                contents = new JObject
                {
                    { "description", $"Stored procedures in {db}.{schema}" },
                    { "queries", new JArray(
                        $"SHOW PROCEDURES IN SCHEMA {db}.{schema}",
                        $"SELECT PROCEDURE_NAME, PROCEDURE_DEFINITION FROM {db}.INFORMATION_SCHEMA.PROCEDURES WHERE PROCEDURE_SCHEMA = '{schema}'"
                    )},
                    { "note", "Use tools/call to execute these queries to list stored procedures" }
                };
            }
            else if (uri == "snowflake://account/tasks")
            {
                contents = new JObject
                {
                    { "description", "All tasks in the account" },
                    { "queries", new JArray(
                        "SHOW TASKS",
                        "SELECT NAME, DATABASE_NAME, SCHEMA_NAME, STATE, CREATED_ON FROM SNOWFLAKE.ACCOUNT_USAGE.TASK_HISTORY WHERE STATE IS NOT NULL GROUP BY NAME, DATABASE_NAME, SCHEMA_NAME, STATE, CREATED_ON"
                    )},
                    { "note", "Use tools/call to execute these queries to see task definitions and execution history" }
                };
            }
            else if (uri == "snowflake://account/pipes")
            {
                contents = new JObject
                {
                    { "description", "All pipes in the account" },
                    { "queries", new JArray(
                        "SHOW PIPES",
                        "SELECT * FROM SNOWFLAKE.ACCOUNT_USAGE.PIPE_USAGE_METRICS ORDER BY LAST_ACTIVITY_TIME DESC LIMIT 100"
                    )},
                    { "note", "Use tools/call to execute these queries to see pipe configurations and activity" }
                };
            }
            else if (uri == "snowflake://account/streams")
            {
                contents = new JObject
                {
                    { "description", "All streams (CDC) in the account" },
                    { "queries", new JArray(
                        "SHOW STREAMS",
                        "SELECT * FROM SNOWFLAKE.ACCOUNT_USAGE.REPLICATION_GROUP_USAGE"
                    )},
                    { "note", "Use tools/call to execute these queries to see stream definitions and change tracking status" }
                };
            }
            else if (uri == "snowflake://account/grants")
            {
                contents = new JObject
                {
                    { "description", "Grants and permissions across account" },
                    { "queries", new JArray(
                        "SHOW GRANTS",
                        "SELECT GRANTEE_NAME, PRIVILEGE, GRANTED_ON, NAME FROM SNOWFLAKE.ACCOUNT_USAGE.GRANTS_TO_ROLES ORDER BY GRANTEE_NAME",
                        "SELECT ROLE, PRIVILEGE, GRANTED_ON, NAME FROM SNOWFLAKE.ACCOUNT_USAGE.GRANTS_TO_USERS"
                    )},
                    { "note", "Use tools/call to execute these queries to see all role and user grants" }
                };
            }
            else if (uri?.StartsWith("snowflake://account/roles/") == true)
            {
                var role = uri.Substring("snowflake://account/roles/".Length);
                
                if (!IsValidDatabaseOrSchemaName(role))
                {
                    await LogToAppInsights("ResourcesReadError", new { Error = "Invalid role name", Role = role });
                    return CreateErrorResponse(requestId, "Invalid role name format", "INVALID_IDENTIFIER");
                }

                contents = new JObject
                {
                    { "description", $"Grants assigned to role {role}" },
                    { "queries", new JArray(
                        $"SHOW GRANTS TO ROLE {role}",
                        $"SELECT PRIVILEGE, GRANTED_ON, NAME FROM SNOWFLAKE.ACCOUNT_USAGE.GRANTS_TO_ROLES WHERE GRANTEE_NAME = '{role}'"
                    )},
                    { "note", "Use tools/call to execute these queries to see all privileges granted to this role" }
                };
            }
            else if (uri == "snowflake://account/masking-policies")
            {
                contents = new JObject
                {
                    { "description", "Data masking policies in account" },
                    { "queries", new JArray(
                        "SHOW MASKING POLICIES",
                        "SELECT POLICY_NAME, DATABASE_NAME, SCHEMA_NAME, CREATED_ON FROM SNOWFLAKE.ACCOUNT_USAGE.MASKING_POLICIES"
                    )},
                    { "note", "Use tools/call to execute these queries to see masking policy definitions" }
                };
            }
            else if (uri == "snowflake://account/queries")
            {
                contents = new JObject
                {
                    { "description", "Recent query history and performance" },
                    { "queries", new JArray(
                        "SELECT QUERY_ID, QUERY_TEXT, USER_NAME, WAREHOUSE_NAME, EXECUTION_TIME, TOTAL_ELAPSED_TIME FROM SNOWFLAKE.ACCOUNT_USAGE.QUERY_HISTORY ORDER BY START_TIME DESC LIMIT 100",
                        "SELECT USER_NAME, COUNT(*) as query_count, AVG(EXECUTION_TIME) as avg_execution_time FROM SNOWFLAKE.ACCOUNT_USAGE.QUERY_HISTORY WHERE START_TIME > CURRENT_TIMESTAMP - INTERVAL '1 hour' GROUP BY USER_NAME",
                        "SELECT WAREHOUSE_NAME, AVG(TOTAL_ELAPSED_TIME) as avg_query_time FROM SNOWFLAKE.ACCOUNT_USAGE.QUERY_HISTORY WHERE WAREHOUSE_NAME IS NOT NULL GROUP BY WAREHOUSE_NAME"
                    )},
                    { "note", "Use tools/call to execute these queries to analyze query performance and patterns" }
                };
            }
            else if (uri?.StartsWith("snowflake://warehouses/") == true && uri.Contains("/queries"))
            {
                var warehouse = uri.Substring("snowflake://warehouses/".Length).Split('/')[0];
                
                if (!IsValidDatabaseOrSchemaName(warehouse))
                {
                    await LogToAppInsights("ResourcesReadError", new { Error = "Invalid warehouse name", Warehouse = warehouse });
                    return CreateErrorResponse(requestId, "Invalid warehouse name format", "INVALID_IDENTIFIER");
                }

                contents = new JObject
                {
                    { "description", $"Query history for warehouse {warehouse}" },
                    { "queries", new JArray(
                        $"SELECT QUERY_ID, QUERY_TEXT, USER_NAME, EXECUTION_TIME, TOTAL_ELAPSED_TIME FROM SNOWFLAKE.ACCOUNT_USAGE.QUERY_HISTORY WHERE WAREHOUSE_NAME = '{warehouse}' ORDER BY START_TIME DESC LIMIT 100"
                    )},
                    { "note", "Use tools/call to execute this query to see all queries run on this warehouse" }
                };
            }
            else if (uri == "snowflake://account/external-tables")
            {
                contents = new JObject
                {
                    { "description", "External table definitions" },
                    { "queries", new JArray(
                        "SHOW EXTERNAL TABLES",
                        "SELECT TABLE_NAME, DATABASE_NAME, SCHEMA_NAME, TABLE_TYPE FROM SNOWFLAKE.ACCOUNT_USAGE.TABLES WHERE TABLE_TYPE = 'EXTERNAL'"
                    )},
                    { "note", "Use tools/call to execute these queries to see external table configurations" }
                };
            }
            else if (uri == "snowflake://account/compute-pools")
            {
                contents = new JObject
                {
                    { "description", "Compute pools for Snowpark jobs" },
                    { "queries", new JArray(
                        "SHOW COMPUTE POOLS",
                        "SELECT * FROM SNOWFLAKE.ACCOUNT_USAGE.COMPUTE_POOL_USAGE_METRICS"
                    )},
                    { "note", "Use tools/call to execute these queries to see compute pool status and utilization" }
                };
            }
            else if (uri == "snowflake://account/parameters")
            {
                contents = new JObject
                {
                    { "description", "Account-level configuration parameters" },
                    { "queries", new JArray(
                        "SHOW PARAMETERS",
                        "SELECT * FROM TABLE(INFORMATION_SCHEMA.ACCOUNT_PARAMETERS())"
                    )},
                    { "note", "Use tools/call to execute these queries to see all account-level parameters and their values" }
                };
            }
            else if (uri == "snowflake://account/password-policies")
            {
                contents = new JObject
                {
                    { "description", "Password policy definitions" },
                    { "queries", new JArray(
                        "SHOW PASSWORD POLICIES",
                        "SELECT * FROM SNOWFLAKE.ACCOUNT_USAGE.PASSWORD_POLICIES"
                    )},
                    { "note", "Use tools/call to execute these queries to see password policies" }
                };
            }
            else if (uri == "snowflake://account/alerts")
            {
                contents = new JObject
                {
                    { "description", "Alerts and notifications" },
                    { "queries", new JArray(
                        "SHOW ALERTS",
                        "SELECT ALERT_NAME, DATABASE_NAME, SCHEMA_NAME, CONDITION, ACTION FROM SNOWFLAKE.ACCOUNT_USAGE.ALERTS"
                    )},
                    { "note", "Use tools/call to execute these queries to see alert definitions and triggers" }
                };
            }
            else if (uri == "snowflake://account/secrets")
            {
                contents = new JObject
                {
                    { "description", "Secrets in Secret Management" },
                    { "queries", new JArray(
                        "SHOW SECRETS",
                        "SELECT SECRET_NAME, DATABASE_NAME, SCHEMA_NAME, SECRET_TYPE FROM SNOWFLAKE.ACCOUNT_USAGE.SECRETS"
                    )},
                    { "note", "Use tools/call to execute these queries to see secret definitions (not the secret values)" }
                };
            }
            else if (uri == "snowflake://account/stages")
            {
                contents = new JObject
                {
                    { "description", "External stages (S3, Azure, GCS)" },
                    { "queries", new JArray(
                        "SHOW STAGES",
                        "SELECT STAGE_NAME, DATABASE_NAME, SCHEMA_NAME, STAGE_TYPE FROM SNOWFLAKE.ACCOUNT_USAGE.STAGES"
                    )},
                    { "note", "Use tools/call to execute these queries to see stage configurations and locations" }
                };
            }
            else if (uri == "snowflake://account/integrations")
            {
                contents = new JObject
                {
                    { "description", "Integrations (Kafka, webhooks, APIs)" },
                    { "queries", new JArray(
                        "SHOW INTEGRATIONS",
                        "SELECT INTEGRATION_NAME, INTEGRATION_TYPE, CATEGORY FROM SNOWFLAKE.ACCOUNT_USAGE.INTEGRATIONS"
                    )},
                    { "note", "Use tools/call to execute these queries to see configured integrations" }
                };
            }
            else if (uri == "snowflake://account/shares")
            {
                contents = new JObject
                {
                    { "description", "Outbound data shares and marketplaces" },
                    { "queries", new JArray(
                        "SHOW SHARES",
                        "SELECT SHARE_NAME, DATABASE_NAME, ACCOUNTS_IN_SHARE FROM SNOWFLAKE.ACCOUNT_USAGE.SHARES"
                    )},
                    { "note", "Use tools/call to execute these queries to see shared databases and their recipients" }
                };
            }
            else if (uri == "snowflake://account/dynamic-tables")
            {
                contents = new JObject
                {
                    { "description", "Dynamic tables with automatic refresh" },
                    { "queries", new JArray(
                        "SELECT TABLE_NAME, DATABASE_NAME, SCHEMA_NAME FROM SNOWFLAKE.ACCOUNT_USAGE.TABLES WHERE TABLE_TYPE = 'DYNAMIC'",
                        "SHOW DYNAMIC TABLES"
                    )},
                    { "note", "Use tools/call to execute these queries to see dynamic table definitions and refresh schedules" }
                };
            }
            else if (uri == "snowflake://account/iceberg-tables")
            {
                contents = new JObject
                {
                    { "description", "Tables in Iceberg format" },
                    { "queries", new JArray(
                        "SELECT TABLE_NAME, DATABASE_NAME, SCHEMA_NAME FROM SNOWFLAKE.ACCOUNT_USAGE.TABLES WHERE TABLE_TYPE = 'ICEBERG'",
                        "SHOW ICEBERG TABLES"
                    )},
                    { "note", "Use tools/call to execute these queries to see Iceberg table configurations" }
                };
            }
            else if (uri == "snowflake://account/hybrid-tables")
            {
                contents = new JObject
                {
                    { "description", "Hybrid tables for OLTP workloads" },
                    { "queries", new JArray(
                        "SELECT TABLE_NAME, DATABASE_NAME, SCHEMA_NAME FROM SNOWFLAKE.ACCOUNT_USAGE.TABLES WHERE TABLE_TYPE = 'HYBRID'",
                        "SELECT * FROM SNOWFLAKE.ACCOUNT_USAGE.HYBRID_TABLE_USAGE_METRICS"
                    )},
                    { "note", "Use tools/call to execute these queries to see hybrid table configurations and performance" }
                };
            }
            else if (uri == "snowflake://account/failed-queries")
            {
                contents = new JObject
                {
                    { "description", "Failed queries for debugging and troubleshooting" },
                    { "queries", new JArray(
                        "SELECT QUERY_ID, QUERY_TEXT, USER_NAME, ERROR_MESSAGE, ERROR_CODE FROM SNOWFLAKE.ACCOUNT_USAGE.QUERY_HISTORY WHERE ERROR_CODE IS NOT NULL ORDER BY START_TIME DESC LIMIT 50"
                    )},
                    { "note", "Use tools/call to execute this query to see recent query failures and error details" }
                };
            }
            else if (uri?.StartsWith("snowflake://databases/") == true && uri.Contains("/grants"))
            {
                var db = uri.Substring("snowflake://databases/".Length).Split('/')[0];
                
                if (!IsValidDatabaseOrSchemaName(db))
                {
                    await LogToAppInsights("ResourcesReadError", new { Error = "Invalid database name", Database = db });
                    return CreateErrorResponse(requestId, "Invalid database name format", "INVALID_IDENTIFIER");
                }

                contents = new JObject
                {
                    { "description", $"Grants for database {db}" },
                    { "queries", new JArray(
                        $"SHOW GRANTS ON DATABASE {db}"
                    )},
                    { "note", "Use tools/call to execute this query to see all grants on this database" }
                };
            }
            else if (uri?.StartsWith("snowflake://databases/") == true && uri.Contains("/replication"))
            {
                var db = uri.Substring("snowflake://databases/".Length).Split('/')[0];
                
                if (!IsValidDatabaseOrSchemaName(db))
                {
                    await LogToAppInsights("ResourcesReadError", new { Error = "Invalid database name", Database = db });
                    return CreateErrorResponse(requestId, "Invalid database name format", "INVALID_IDENTIFIER");
                }

                contents = new JObject
                {
                    { "description", $"Replication status for database {db}" },
                    { "queries", new JArray(
                        $"SELECT * FROM SNOWFLAKE.ACCOUNT_USAGE.REPLICATION_DATABASE_USAGE WHERE DATABASE_NAME = '{db}'"
                    )},
                    { "note", "Use tools/call to execute this query to see replication details and status" }
                };
            }
            else if (uri?.StartsWith("snowflake://databases/") == true && uri.Contains("/tags"))
            {
                var db = uri.Substring("snowflake://databases/".Length).Split('/')[0];
                
                if (!IsValidDatabaseOrSchemaName(db))
                {
                    await LogToAppInsights("ResourcesReadError", new { Error = "Invalid database name", Database = db });
                    return CreateErrorResponse(requestId, "Invalid database name format", "INVALID_IDENTIFIER");
                }

                contents = new JObject
                {
                    { "description", $"Tag definitions in database {db}" },
                    { "queries", new JArray(
                        $"SHOW TAGS IN DATABASE {db}",
                        $"SELECT TAG_NAME, TAG_VALUE, OBJECT_NAME FROM SNOWFLAKE.ACCOUNT_USAGE.TAG_REFERENCES WHERE DATABASE_NAME = '{db}'"
                    )},
                    { "note", "Use tools/call to execute these queries to see tags and their assignments" }
                };
            }
            else
            {
                contents = new JObject
                {
                    { "error", "Unknown or unsupported resource URI" },
                    { "uri", uri },
                    { "note", "Available resource types: snowflake://databases, snowflake://warehouses, snowflake://account/usage, snowflake://account/users, snowflake://account/sessions, snowflake://account/roles" }
                };
            }

            var resourceReadResponse = new JObject
            {
                { "jsonrpc", "2.0" },
                { "id", requestObject?["id"] ?? 1 },
                { "result", new JObject
                    {
                        { "contents", new JArray(
                            new JObject
                            {
                                { "uri", uri },
                                { "mimeType", mimeType },
                                { "text", JsonConvert.SerializeObject(contents) }
                            }
                        )}
                    }
                }
            };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonConvert.SerializeObject(resourceReadResponse), Encoding.UTF8, "application/json")
            };
        }

        // Handle resource-returning tools
        if (method == "tools/call")
        {
            var toolName = requestObject?.Value<string>("params.name");
            var toolInput = requestObject?.SelectToken("params.arguments") as JObject;

            if (toolName == "get_account_structure")
            {
                await LogToAppInsights("ToolCall", new { Tool = "get_account_structure" });
                
                var resourceType = toolInput?.Value<string>("resource_type") ?? "all";
                var resources = new JArray();

                if (resourceType == "databases" || resourceType == "all")
                {
                    resources.Add(new JObject 
                    { 
                        { "type", "resource" },
                        { "uri", "snowflake://databases" },
                        { "title", "Databases" },
                        { "description", "All databases in the account" }
                    });
                }
                
                if (resourceType == "warehouses" || resourceType == "all")
                {
                    resources.Add(new JObject 
                    { 
                        { "type", "resource" },
                        { "uri", "snowflake://warehouses" },
                        { "title", "Warehouses" },
                        { "description", "All compute warehouses" }
                    });
                }
                
                if (resourceType == "users" || resourceType == "all")
                {
                    resources.Add(new JObject 
                    { 
                        { "type", "resource" },
                        { "uri", "snowflake://account/users" },
                        { "title", "Users" },
                        { "description", "All users in account" }
                    });
                }

                var result = new JObject
                {
                    { "jsonrpc", "2.0" },
                    { "id", requestObject?["id"] ?? 1 },
                    { "result", new JObject
                        {
                            { "content", new JArray(
                                new JObject
                                {
                                    { "type", "text" },
                                    { "text", $"Account Structure Resources ({resourceType}):\n" + JsonConvert.SerializeObject(resources) }
                                }
                            )}
                        }
                    }
                };

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonConvert.SerializeObject(result), Encoding.UTF8, "application/json")
                };
            }

            if (toolName == "get_security_posture")
            {
                await LogToAppInsights("ToolCall", new { Tool = "get_security_posture" });
                
                var resources = new JArray();
                resources.Add(new JObject { { "type", "resource" }, { "uri", "snowflake://account/roles" }, { "title", "Roles" } });
                resources.Add(new JObject { { "type", "resource" }, { "uri", "snowflake://account/grants" }, { "title", "Grants" } });
                resources.Add(new JObject { { "type", "resource" }, { "uri", "snowflake://account/masking-policies" }, { "title", "Masking Policies" } });

                var result = new JObject
                {
                    { "jsonrpc", "2.0" },
                    { "id", requestObject?["id"] ?? 1 },
                    { "result", new JObject
                        {
                            { "content", new JArray(
                                new JObject
                                {
                                    { "type", "text" },
                                    { "text", "Security Resources:\n" + JsonConvert.SerializeObject(resources) }
                                }
                            )}
                        }
                    }
                };

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonConvert.SerializeObject(result), Encoding.UTF8, "application/json")
                };
            }

            if (toolName == "get_data_pipeline_status")
            {
                await LogToAppInsights("ToolCall", new { Tool = "get_data_pipeline_status" });
                
                var pipelineType = toolInput?.Value<string>("pipeline_type") ?? "all";
                var resources = new JArray();

                if (pipelineType == "tasks" || pipelineType == "all")
                {
                    resources.Add(new JObject { { "type", "resource" }, { "uri", "snowflake://account/tasks" }, { "title", "Scheduled Tasks" } });
                }
                
                if (pipelineType == "pipes" || pipelineType == "all")
                {
                    resources.Add(new JObject { { "type", "resource" }, { "uri", "snowflake://account/pipes" }, { "title", "Pipes (Continuous Data Load)" } });
                }
                
                if (pipelineType == "streams" || pipelineType == "all")
                {
                    resources.Add(new JObject { { "type", "resource" }, { "uri", "snowflake://account/streams" }, { "title", "Streams (Change Data)" } });
                }

                var result = new JObject
                {
                    { "jsonrpc", "2.0" },
                    { "id", requestObject?["id"] ?? 1 },
                    { "result", new JObject
                        {
                            { "content", new JArray(
                                new JObject
                                {
                                    { "type", "text" },
                                    { "text", $"Data Pipeline Resources ({pipelineType}):\n" + JsonConvert.SerializeObject(resources) }
                                }
                            )}
                        }
                    }
                };

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonConvert.SerializeObject(result), Encoding.UTF8, "application/json")
                };
            }

            if (toolName == "get_query_performance_metrics")
            {
                await LogToAppInsights("ToolCall", new { Tool = "get_query_performance_metrics" });
                
                var resources = new JArray();
                resources.Add(new JObject { { "type", "resource" }, { "uri", "snowflake://account/queries" }, { "title", "Query History" } });
                resources.Add(new JObject { { "type", "resource" }, { "uri", "snowflake://account/failed-queries" }, { "title", "Failed Queries" } });

                var result = new JObject
                {
                    { "jsonrpc", "2.0" },
                    { "id", requestObject?["id"] ?? 1 },
                    { "result", new JObject
                        {
                            { "content", new JArray(
                                new JObject
                                {
                                    { "type", "text" },
                                    { "text", "Query Performance Resources:\n" + JsonConvert.SerializeObject(resources) }
                                }
                            )}
                        }
                    }
                };

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonConvert.SerializeObject(result), Encoding.UTF8, "application/json")
                };
            }

            if (toolName == "get_resource_catalog")
            {
                await LogToAppInsights("ToolCall", new { Tool = "get_resource_catalog" });
                
                var catalog = new JObject
                {
                    { "data_objects", new JArray(
                        new JObject { { "uri", "snowflake://databases" }, { "description", "Database instances" } },
                        new JObject { { "uri", "snowflake://databases/{db}/schemas" }, { "description", "Schemas in database" } },
                        new JObject { { "uri", "snowflake://databases/{db}/external-tables" }, { "description", "External tables" } }
                    )},
                    { "compute", new JArray(
                        new JObject { { "uri", "snowflake://warehouses" }, { "description", "Compute warehouses" } },
                        new JObject { { "uri", "snowflake://account/compute-pools" }, { "description", "Compute pools" } }
                    )},
                    { "security", new JArray(
                        new JObject { { "uri", "snowflake://account/roles" }, { "description", "Access roles" } },
                        new JObject { { "uri", "snowflake://account/grants" }, { "description", "Permission grants" } },
                        new JObject { { "uri", "snowflake://account/masking-policies" }, { "description", "Data masking policies" } }
                    )},
                    { "pipelines", new JArray(
                        new JObject { { "uri", "snowflake://account/tasks" }, { "description", "Scheduled tasks" } },
                        new JObject { { "uri", "snowflake://account/pipes" }, { "description", "Continuous data load" } },
                        new JObject { { "uri", "snowflake://account/streams" }, { "description", "Change data streams" } }
                    )},
                    { "monitoring", new JArray(
                        new JObject { { "uri", "snowflake://account/usage" }, { "description", "Account usage metrics" } },
                        new JObject { { "uri", "snowflake://account/queries" }, { "description", "Query execution history" } }
                    )}
                };

                var result = new JObject
                {
                    { "jsonrpc", "2.0" },
                    { "id", requestObject?["id"] ?? 1 },
                    { "result", new JObject
                        {
                            { "content", new JArray(
                                new JObject
                                {
                                    { "type", "text" },
                                    { "text", "Complete Resource Catalog:\n" + JsonConvert.SerializeObject(catalog, Newtonsoft.Json.Formatting.Indented) }
                                }
                            )}
                        }
                    }
                };

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonConvert.SerializeObject(result), Encoding.UTF8, "application/json")
                };
            }

            if (toolName == "inspect_resource")
            {
                await LogToAppInsights("ToolCall", new { Tool = "inspect_resource" });
                
                var resourceUri = toolInput?.Value<string>("resource_uri");
                
                if (string.IsNullOrEmpty(resourceUri) || !IsValidResourceUri(resourceUri))
                {
                    var errorResponse = new JObject
                    {
                        { "jsonrpc", "2.0" },
                        { "id", requestObject?["id"] ?? 1 },
                        { "error", new JObject
                            {
                                { "code", "INVALID_URI" },
                                { "message", "Invalid or missing resource URI" }
                            }
                        }
                    };
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(JsonConvert.SerializeObject(errorResponse), Encoding.UTF8, "application/json")
                    };
                }

                // Reuse the resources/read logic by extracting the contents
                var inspectContents = new JObject { { "uri", resourceUri }, { "description", "Resource inspection details" } };
                
                var inspectResult = new JObject
                {
                    { "jsonrpc", "2.0" },
                    { "id", requestObject?["id"] ?? 1 },
                    { "result", new JObject
                        {
                            { "content", new JArray(
                                new JObject
                                {
                                    { "type", "text" },
                                    { "text", $"Resource: {resourceUri}\n\nUse resources/read with this URI to get full details." }
                                }
                            )}
                        }
                    }
                };

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonConvert.SerializeObject(inspectResult), Encoding.UTF8, "application/json")
                };
            }
        }

        var forwardRequest = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Content = new StringContent(requestBody, Encoding.UTF8, request.Content?.Headers?.ContentType?.MediaType ?? "application/json")
        };

        foreach (var header in request.Headers)
        {
            forwardRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (!forwardRequest.Headers.Contains("User-Agent") || (forwardRequest.Headers.UserAgent != null && forwardRequest.Headers.UserAgent.Count == 0))
        {
            forwardRequest.Headers.Add("User-Agent", "Snowflake-MCP-Connector/1.0");
        }

        var response = await this.Context.SendAsync(forwardRequest, this.CancellationToken).ConfigureAwait(false);
        var latencyMs = (DateTime.UtcNow - startTime).TotalMilliseconds;

        // Log response and clean up malformed responses
        var wasMalformed = false;
        if (response?.Content != null)
        {
            var respBody = await response.Content.ReadAsStringAsync();
            
            // If response body is the malformed array [{"jsonrpc":"2.0"}], blank it out
            if (respBody?.Trim() == "[{\"jsonrpc\":\"2.0\"}]")
            {
                wasMalformed = true;
                response.Content = new StringContent(string.Empty, Encoding.UTF8, "application/json");
            }

            await LogToAppInsights("SnowflakeResponse", new
            {
                Method = method,
                StatusCode = (int)response.StatusCode,
                ResponseBody = wasMalformed ? "[MALFORMED - BLANKED]" : respBody?.Substring(0, Math.Min(1000, respBody?.Length ?? 0)),
                WasMalformed = wasMalformed,
                LatencyMs = latencyMs
            });
        }

        return response;
    }

    private async Task LogToAppInsights(string eventName, object properties)
    {
        try
        {
            var instrumentationKey = ExtractInstrumentationKey(APP_INSIGHTS_CONNECTION_STRING);
            var ingestionEndpoint = ExtractIngestionEndpoint(APP_INSIGHTS_CONNECTION_STRING);
            
            if (string.IsNullOrEmpty(instrumentationKey) || string.IsNullOrEmpty(ingestionEndpoint))
            {
                return;
            }

            var telemetryData = new
            {
                name = $"Microsoft.ApplicationInsights.{instrumentationKey}.Event",
                time = DateTime.UtcNow.ToString("o"),
                iKey = instrumentationKey,
                data = new
                {
                    baseType = "EventData",
                    baseData = new
                    {
                        ver = 2,
                        name = eventName,
                        properties = properties
                    }
                }
            };

            var json = JsonConvert.SerializeObject(telemetryData);
            var telemetryUrl = new Uri(ingestionEndpoint.TrimEnd('/') + "/v2/track");

            var telemetryRequest = new HttpRequestMessage(HttpMethod.Post, telemetryUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            await this.Context.SendAsync(telemetryRequest, this.CancellationToken);
        }
        catch
        {
            // Suppress telemetry errors
        }
    }

    private string ExtractInstrumentationKey(string connectionString)
    {
        try
        {
            var parts = connectionString.Split(';');
            foreach (var part in parts)
            {
                if (part.StartsWith("InstrumentationKey=", StringComparison.OrdinalIgnoreCase))
                {
                    return part.Substring("InstrumentationKey=".Length);
                }
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    private string ExtractIngestionEndpoint(string connectionString)
    {
        try
        {
            var parts = connectionString.Split(';');
            foreach (var part in parts)
            {
                if (part.StartsWith("IngestionEndpoint=", StringComparison.OrdinalIgnoreCase))
                {
                    return part.Substring("IngestionEndpoint=".Length);
                }
            }
            return "https://dc.services.visualstudio.com/";
        }
        catch
        {
            return "https://dc.services.visualstudio.com/";
        }
    }

    private HttpResponseMessage CreateErrorResponse(int? id, string errorMessage, string errorCode = "INVALID_REQUEST")
    {
        var errorResponse = new JObject
        {
            { "jsonrpc", "2.0" },
            { "id", id ?? 1 },
            { "error", new JObject
                {
                    { "code", errorCode },
                    { "message", errorMessage }
                }
            }
        };

        return new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(JsonConvert.SerializeObject(errorResponse), Encoding.UTF8, "application/json")
        };
    }

    private bool IsValidResourceUri(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return false;

        // Only allow alphanumeric, dashes, underscores, dots, slashes, and curly braces for path parameters
        var allowedChars = System.Text.RegularExpressions.Regex.IsMatch(uri, @"^snowflake://[a-zA-Z0-9_/\-\.{}\*]+$");
        
        return allowedChars && !uri.Contains("..") && !uri.Contains("--");
    }

    private bool IsValidDatabaseOrSchemaName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        // SQL identifiers: alphanumeric, underscores, can start with letter or underscore
        return System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-zA-Z_][a-zA-Z0-9_]*$");
    }

    private void NormalizeJsonRpcId(JObject requestObject, List<string> transformations)
    {
        var idToken = requestObject["id"];
        
        if (idToken == null)
        {
            requestObject["id"] = 1;
            transformations.Add("id:null->1");
        }
        else if (idToken.Type == JTokenType.Integer)
        {
            // Already integer, do nothing
        }
        else if (idToken.Type == JTokenType.Float)
        {
            requestObject["id"] = (int)idToken.Value<double>();
            transformations.Add("id:float->int");
        }
        else if (idToken.Type == JTokenType.String)
        {
            if (int.TryParse(idToken.ToString(), out var parsed))
            {
                requestObject["id"] = parsed;
                transformations.Add("id:string->int");
            }
            else
            {
                requestObject["id"] = 1;
                transformations.Add("id:invalid_string->1");
            }
        }
        else if (idToken.Type == JTokenType.Boolean)
        {
            requestObject["id"] = idToken.Value<bool>() ? 1 : 0;
            transformations.Add("id:bool->int");
        }
        else
        {
            requestObject["id"] = 1;
            transformations.Add($"id:{idToken.Type}->1");
        }
    }
}
