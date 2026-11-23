namespace TickerNet.Utilities.Enums
{
    public enum TickerNetStartMode
    {
        /// <summary>
        /// Start job processing immediately when UseTickerNet is called.
        /// Background services are registered and start automatically.
        /// </summary>
        Immediate,
        
        /// <summary>
        /// Background services are registered but skip the first run.
        /// Job processing needs to be started manually via ITickerNetHostScheduler.
        /// </summary>
        Manual
    }
}