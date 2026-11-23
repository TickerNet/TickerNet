using System;

namespace TickerNet.Utilities.Exceptions
{
    public class TickerValidatorException : Exception
    {
        public TickerValidatorException(string message) : base(message)
        {
        }
    }
}
