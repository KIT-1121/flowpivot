using System.Threading;

internal static class UpgradeSignals {
    internal const string ClientMutex=@"Local\FlowPivot.Windows.Client";
    internal const string ExitEvent=@"Local\FlowPivot.Windows.ExitForUpgrade";
    internal static bool Signal(){
        try{using(var signal=EventWaitHandle.OpenExisting(ExitEvent))return signal.Set();}
        catch(WaitHandleCannotBeOpenedException){return false;}
    }
    internal static bool ClientRunning(){
        try{using(var mutex=Mutex.OpenExisting(ClientMutex))return true;}
        catch(WaitHandleCannotBeOpenedException){return false;}
    }
}
