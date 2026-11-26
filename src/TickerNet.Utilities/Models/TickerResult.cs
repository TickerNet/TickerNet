using System;
using System.Runtime.ExceptionServices;


namespace TickerNet.Utilities.Models
{
    public class TickerResult<TEntity> where TEntity : class
    {
        internal TickerResult(Exception exception) : this(false)
            => Exception = exception;
        internal TickerResult(TEntity result) : this(true)
            => Result = result;
        internal TickerResult(int affectedRows) : this(true)
            => AffectedRows = affectedRows;
        private TickerResult(bool isSucceeded)
            => IsSucceeded = isSucceeded;


        internal TickerResult(TEntity result, int affectedRows) : this(true)
        {
            Result = result;
            AffectedRows = affectedRows;
        }
        /// <summary>
        /// Returns this result when the operation succeeded.
        /// </summary>
        /// <returns>This result, so calls can be chained.</returns>
        /// <exception cref="Exception">
        /// The stored exception, rethrown with its original stack trace, when the operation failed.
        /// </exception>
        public TickerResult<TEntity> EnsureSuccess()
        {
            if (!IsSucceeded)
                ExceptionDispatchInfo.Capture(Exception).Throw();

            return this;
        }


        public readonly bool IsSucceeded;
        public readonly int AffectedRows;
        public readonly TEntity Result;
        public readonly Exception Exception;
    }
}

