using System;

namespace TickerNet.Utilities
{
    /// <summary>
    /// Naming rule for functions that run on remote worker nodes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A function that runs on a worker connected through TickerNet.Hub is stored and scheduled as
    /// <c>function@node</c>: the function's own name, <c>@</c>, then the worker's node name. Several
    /// worker nodes can then offer functions with the same name. Names without <c>@</c> are local
    /// functions that run in the scheduler's own process.
    /// </para>
    /// <para>
    /// TickerQ's remote executor used the same rule, so jobs stored as <c>function@node</c> by TickerQ
    /// run on a TickerNet worker that uses the same node name.
    /// </para>
    /// </remarks>
    public static class RemoteFunctionName
    {
        /// <summary>The character between the function name and the node name.</summary>
        public const char Separator = '@';

        /// <summary>True when <paramref name="functionName"/> has the form <c>function@node</c>.</summary>
        public static bool IsRemote(string functionName)
        {
            if (string.IsNullOrEmpty(functionName)) return false;
            var index = functionName.IndexOf(Separator);
            return index > 0 && index < functionName.Length - 1;
        }

        /// <summary>
        /// Returns <c>function@node</c>. A name that is already remote is returned unchanged; an empty
        /// node name returns the function name unchanged.
        /// </summary>
        public static string Qualify(string functionName, string nodeName)
        {
            if (string.IsNullOrWhiteSpace(functionName) || string.IsNullOrWhiteSpace(nodeName))
                return functionName;
            return IsRemote(functionName) ? functionName : functionName + Separator + nodeName;
        }

        /// <summary>The node part of <c>function@node</c>, or an empty string for a local function.</summary>
        public static string GetNodeName(string functionName)
            => IsRemote(functionName)
                ? functionName.Substring(functionName.IndexOf(Separator) + 1)
                : string.Empty;

        /// <summary>The function part of <c>function@node</c>, or the name unchanged for a local function.</summary>
        public static string GetFunctionName(string functionName)
            => IsRemote(functionName)
                ? functionName.Substring(0, functionName.IndexOf(Separator))
                : functionName;

        /// <summary>
        /// Checks that <paramref name="nodeName"/> can be used as a worker node name: not empty, at most
        /// 100 characters, and without <c>@</c> or whitespace.
        /// </summary>
        public static bool IsValidNodeName(string nodeName)
        {
            if (string.IsNullOrWhiteSpace(nodeName) || nodeName.Length > 100) return false;
            foreach (var c in nodeName)
            {
                if (c == Separator || char.IsWhiteSpace(c) || char.IsControl(c)) return false;
            }
            return true;
        }
    }
}
