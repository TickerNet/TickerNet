using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using TickerNet.Utilities.Enums;
using TickerNet.Utilities.Models;

namespace TickerNet.Utilities.Instrumentation;

public class ActivitySourceInstrumentation(ILogger<ActivitySourceInstrumentation> logger, SchedulerOptionsBuilder optionsBuilder)
  : TickerNetBaseLoggerInstrumentation(logger, optionsBuilder.NodeIdentifier)
  , ITickerNetInstrumentation
{
  private static readonly ActivitySource ActivitySource = new("TickerNet", "1.0.0");

  public override Activity StartJobActivity(string activityName, InternalFunctionContext context)
  {
    if (ActivitySource.StartActivity(activityName) is not { } activity)
      return null;

    activity.SetTag("tickernet.job.id", context.TickerId.ToString());
    activity.SetTag("tickernet.job.type", context.Type.ToString());
    activity.SetTag("tickernet.job.function", context.FunctionName);
    activity.SetTag("tickernet.job.priority", context.CachedPriority.ToString());
    activity.SetTag("tickernet.job.machine", InstanceIdentifier);
    activity.SetTag("tickernet.job.retries", context.Retries);

    if (context.ParentId.HasValue)
      activity.SetTag("tickernet.job.parent_id", context.ParentId.Value.ToString());

    if (context.Type == TickerType.TimeTicker && context.ParentId.HasValue)
      activity.SetTag("tickernet.job.run_condition", context.RunCondition.ToString());

    return activity;
  }

  public override void LogJobEnqueued(string jobType, string functionName, Guid jobId, string enqueuedFrom = null)
  {
    // Get detailed caller information for OpenTelemetry
    var callerInfo = string.IsNullOrEmpty(enqueuedFrom) ? CallerInfoHelper.GetCallerInfo(6) : enqueuedFrom;
    base.LogJobEnqueued(jobType, functionName, jobId, callerInfo);

    using var activity = ActivitySource.StartActivity("tickernet.job.enqueued");
    if (activity == null) return;

    activity.SetTag("tickernet.job.id", jobId.ToString());
    activity.SetTag("tickernet.job.type", jobType);
    activity.SetTag("tickernet.job.function", functionName);
    activity.SetTag("tickernet.job.enqueued_from", callerInfo);
  }

  public override void LogJobCompleted(Guid jobId, string functionName, long executionTimeMs, bool success)
  {
    base.LogJobCompleted(jobId, functionName, executionTimeMs, success);

    using var activity = ActivitySource.StartActivity("tickernet.job.completed");
    if (activity == null) return;

    activity.SetStatus(success ? ActivityStatusCode.Ok : ActivityStatusCode.Error);

    activity.SetTag("tickernet.job.id", jobId.ToString());
    activity.SetTag("tickernet.job.function", functionName);
    activity.SetTag("tickernet.job.execution_time_ms", executionTimeMs);
    activity.SetTag("tickernet.job.success", success);
  }

  public override void LogJobFailed(Guid jobId, string functionName, Exception exception, int retryCount)
  {
    base.LogJobFailed(jobId, functionName, exception, retryCount);

    using var activity = ActivitySource.StartActivity("tickernet.job.failed");
    if (activity == null) return;

    activity.SetStatus(ActivityStatusCode.Error, exception.Message);

    activity.SetTag("tickernet.job.id", jobId.ToString());
    activity.SetTag("tickernet.job.function", functionName);
    activity.SetTag("tickernet.job.retry_count", retryCount);
    activity.SetTag("tickernet.job.error_type", exception.GetType().Name);
    activity.SetTag("tickernet.job.error_message", exception.Message);

    // Record exception information in tags instead of RecordException (not available in all .NET versions)
    if (exception.StackTrace != null)
      activity.SetTag("tickernet.job.error_stack_trace", exception.StackTrace);
  }

  public override void LogJobCancelled(Guid jobId, string functionName, string reason)
  {
    base.LogJobCancelled(jobId, functionName, reason);

    using var activity = ActivitySource.StartActivity("tickernet.job.cancelled");
    if (activity == null) return;

    activity.SetStatus(ActivityStatusCode.Error, reason);

    activity.SetTag("tickernet.job.id", jobId.ToString());
    activity.SetTag("tickernet.job.function", functionName);
    activity.SetTag("tickernet.job.cancellation_reason", reason);
  }

  public override void LogJobSkipped(Guid jobId, string functionName, string reason)
  {
    base.LogJobSkipped(jobId, functionName, reason);

    using var activity = ActivitySource.StartActivity("tickernet.job.skipped");
    if (activity == null) return;

    activity.SetTag("tickernet.job.id", jobId.ToString());
    activity.SetTag("tickernet.job.function", functionName);
    activity.SetTag("tickernet.job.skip_reason", reason);
  }

  public override void LogSeedingDataStarted(string seedingDataType)
  {
    base.LogSeedingDataStarted(seedingDataType);

    using var activity = ActivitySource.StartActivity("tickernet.seeding.started");
    if (activity == null) return;

    activity.SetTag("tickernet.seeding.type", seedingDataType);
    activity.SetTag("tickernet.seeding.environment", InstanceIdentifier);
  }

  public override void LogSeedingDataCompleted(string seedingDataType)
  {
    base.LogSeedingDataCompleted(seedingDataType);

    using var activity = ActivitySource.StartActivity("tickernet.seeding.completed");
    if (activity == null) return;

    activity.SetTag("tickernet.seeding.type", seedingDataType);
    activity.SetTag("tickernet.seeding.environment", InstanceIdentifier);
  }

  public override void LogRequestDeserializationFailure(string requestType, string functionName, Guid tickerId, TickerType type, Exception exception)
  {
    base.LogRequestDeserializationFailure(requestType, functionName, tickerId, type, exception);

    using var activity = ActivitySource.StartActivity("tickernet.job_request_serialization.failed");
    if (activity == null) return;

    activity.SetTag("tickernet.job.id", tickerId.ToString());
    activity.SetTag("tickernet.job.function", functionName);
    activity.SetTag("tickernet.job.cancellation_reason", exception.Message);
  }
}