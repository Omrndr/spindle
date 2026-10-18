namespace Spindle.Core;

/// <summary>Office otomasyonu STA iş parçacığı ister; her iş için kısa ömürlü bir STA thread açar.</summary>
public static class StaRunner
{
    public static Task RunAsync(Action action)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { action(); tcs.SetResult(); }
            catch (Exception ex) { tcs.SetException(ex); }
        })
        { IsBackground = true, Name = "Spindle STA" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }
}

public sealed class ConversionException(string message, Exception? inner = null) : Exception(message, inner);
