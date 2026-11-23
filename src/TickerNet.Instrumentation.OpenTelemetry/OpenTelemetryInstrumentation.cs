using System;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TickerNet.Utilities;
using TickerNet.Utilities.Enums;
using TickerNet.Utilities.Instrumentation;
using TickerNet.Utilities.Models;

namespace TickerNet.Instrumentation.OpenTelemetry;

internal class OpenTelemetryInstrumentation : TickerNetBaseLoggerInstrumentation , ITickerNetInstrumentation
{
    private readonly ILogger<OpenTelemetryInstrumentation> _logger;
    private static readonly ActivitySource ActivitySource = new("TickerNet", "1.0.0");

    public OpenTelemetryInstrumentation(ILogger<OpenTelemetryInstrumentation> logger, SchedulerOptionsBuilder optionsBuilder) : base(logger, optionsBuilder.NodeIdentifier)
    {
        _logger = logger;
    }

    public override Activity? StartJobActivity(string activityName, InternalFunctionContext context)
    {
        var activity = ActivitySource.StartActivity(activityName);
            
        if (activity != null)
        {
            activity.SetTag("tickernet.job.id", context.TickerId.ToString());
            activity.SetTag("tickernet.job.type", context.Type.ToString());
            activity.SetTag("tickernet.job.function", context.FunctionName);
            activity.SetTag("tickernet.job.priority", context.CachedPriority.ToString());
            activity.SetTag("tickernet.job.machine", InstanceIdentifier);
            activity.SetTag("tickernet.job.retries", context.Retries);
                
            if (context.ParentId.HasValue)
            {
                activity.SetTag("tickernet.job.parent_id", context.ParentId.Value.ToString());
            }

            if (context.Type == TickerType.TimeTicker && context.ParentId.HasValue)
            {
                activity.SetTag("tickernet.job.run_condition", context.RunCondition.ToString());
            }
        }
            
        return activity;
    }

    public override void LogJobEnqueued(string jobType, string functionName, Guid jobId, string? enqueuedFrom = null)
    {
        using var activity = ActivitySource.StartActivity("tickernet.job.enqueued");
        activity?.SetTag("tickernet.job.id", jobId.ToString());
        activity?.SetTag("tickernet.job.type", jobType);
        activity?.SetTag("tickernet.job.function", functionName);
            
        // Get detailed caller information for OpenTelemetry
        var callerInfo = string.IsNullOrEmpty(enqueuedFrom) ? CallerInfoHelper.GetCallerInfo(6) : enqueuedFrom;
        activity?.SetTag("tickernet.job.enqueued_from", callerInfo);
            
        _logger.LogInformation("TickerNet Job enqueued: {JobType} - {Function} ({JobId}) from {EnqueuedFrom}", 
            jobType, functionName, jobId, callerInfo);
    }

    public override void LogJobCompleted(Guid jobId, string functionName, long executionTimeMs, bool success)
    {
        using var activity = ActivitySource.StartActivity("tickernet.job.completed");
        activity?.SetTag("tickernet.job.id", jobId.ToString());
        activity?.SetTag("tickernet.job.function", functionName);
        activity?.SetTag("tickernet.job.execution_time_ms", executionTimeMs);
        activity?.SetTag("tickernet.job.success", success);
            
        // Set activity status based on success
        if (activity != null)
        {
            activity.SetStatus(success ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
        }
            
        base.LogJobCompleted(jobId, functionName, executionTimeMs, success);
    }

    public override void LogJobFailed(Guid jobId, string functionName, Exception exception, int retryCount)
    {
        using var activity = ActivitySource.StartActivity("tickernet.job.failed");
        activity?.SetTag("tickernet.job.id", jobId.ToString());
        activity?.SetTag("tickernet.job.function", functionName);
        activity?.SetTag("tickernet.job.retry_count", retryCount);
        activity?.SetTag("tickernet.job.error_type", exception.GetType().Name);
        activity?.SetTag("tickernet.job.error_message", exception.Message);
            
        if (activity != null)
        {
            activity.SetStatus(ActivityStatusCode.Error, exception.Message);
            // Record exception information in tags instead of RecordException (not available in all .NET versions)
            if (exception.StackTrace != null)
            {
                activity.SetTag("tickernet.job.error_stack_trace", exception.StackTrace);
            }
        }
            
        base.LogJobFailed(jobId, functionName, exception, retryCount);
    }

    public override void LogJobCancelled(Guid jobId, string functionName, string reason)
    {
        using var activity = ActivitySource.StartActivity("tickernet.job.cancelled");
        activity?.SetTag("tickernet.job.id", jobId.ToString());
        activity?.SetTag("tickernet.job.function", functionName);
        activity?.SetTag("tickernet.job.cancellation_reason", reason);
            
        if (activity != null)
        {
            activity.SetStatus(ActivityStatusCode.Error, reason);
        }
            
        base.LogJobCancelled(jobId, functionName, reason);
    }

    public override void LogJobSkipped(Guid jobId, string functionName, string reason)
    {
        using var activity = ActivitySource.StartActivity("tickernet.job.skipped");
        activity?.SetTag("tickernet.job.id", jobId.ToString());
        activity?.SetTag("tickernet.job.function", functionName);
        activity?.SetTag("tickernet.job.skip_reason", reason);
            
        base.LogJobSkipped(jobId, functionName, reason);
    }

    public override void LogSeedingDataStarted(string seedingDataType)
    {
        using var activity = ActivitySource.StartActivity("tickernet.seeding.started");
        activity?.SetTag("tickernet.seeding.type", seedingDataType);
        activity?.SetTag("tickernet.seeding.environment", InstanceIdentifier);
            
        base.LogSeedingDataStarted(seedingDataType);
    }

    public override void LogSeedingDataCompleted(string seedingDataType)
    {
        using var activity = ActivitySource.StartActivity("tickernet.seeding.completed");
        activity?.SetTag("tickernet.seeding.type", seedingDataType);
        activity?.SetTag("tickernet.seeding.environment", InstanceIdentifier);
            
        base.LogSeedingDataCompleted(seedingDataType);
    }

    public override void LogRequestDeserializationFailure(string requestType, string functionName, Guid tickerId, TickerType type, Exception exception)
    {
        using var activity = ActivitySource.StartActivity("tickernet.job_request_serialization.failed");
        activity?.SetTag("tickernet.job.id", tickerId.ToString());
        activity?.SetTag("tickernet.job.function", functionName);
        activity?.SetTag("tickernet.job.cancellation_reason", exception.Message);
        base.LogRequestDeserializationFailure(requestType, functionName, tickerId, type, exception);
    }
}