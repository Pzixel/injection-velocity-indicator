using System;

namespace SimpleSplitter.Engineer
{
    // Use the host log; do not register KER's separate logger MonoBehaviour.
    internal static class MyLogger
    {
        public static void Log(object message) => UnityEngine.Debug.Log("[SimpleSplitter/Engineer] " + message);
        public static void Exception(Exception error, string context) =>
            UnityEngine.Debug.LogError("[SimpleSplitter/Engineer] " + context + ": " + error);
    }
}
