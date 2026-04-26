using System;
using iSpyApplication.Sources;
using iSpyApplication.Sources.Audio;

namespace iSpyApplication.Utilities
{
    internal static class SafeEventDispatch
    {
        public static void DataAvailable(DataAvailableEventHandler handler, object sender, DataAvailableEventArgs args, string context)
        {
            if (handler == null)
                return;

            try
            {
                handler(sender, args);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, context);
            }
        }

        public static void NewFrame(NewFrameEventHandler handler, object sender, NewFrameEventArgs args, string context)
        {
            if (handler == null)
                return;

            try
            {
                handler(sender, args);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, context);
            }
        }
    }
}
