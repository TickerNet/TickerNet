## Release 2.5.2

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|--------------------
TN001   | TickerNet.SourceGenerator | Error | Class should be public or internal to be used with [TickerFunction]
TN002   | TickerNet.SourceGenerator | Error | Method should be public or internal to be used with [TickerFunction]
TN003   | TickerNet.SourceGenerator | Error | Invalid cron expression
TN004   | TickerNet.SourceGenerator | Error | Missing function name in [TickerFunction] attribute
TN005   | TickerNet.SourceGenerator | Error | Duplicate function name across [TickerFunction] methods
TN006   | TickerNet.SourceGenerator | Warning | Multiple constructors detected - first constructor will be used unless [TickerNetConstructor] attribute is specified
TN007   | TickerNet.SourceGenerator | Error | Abstract class contains [TickerFunction] methods
TN008   | TickerNet.SourceGenerator | Error | Nested class contains [TickerFunction] methods - only allowed in top-level classes
TN009   | TickerNet.SourceGenerator | Error | Invalid TickerFunction parameter - only TickerFunctionContext, TickerFunctionContext<T>, CancellationToken, or no parameters allowed
TN010   | TickerNet.SourceGenerator | Error | Multiple constructors with [TickerNetConstructor] attribute - only one constructor can be marked