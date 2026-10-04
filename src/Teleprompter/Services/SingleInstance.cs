namespace Teleprompter.Services;

/// <summary>
/// Evita dos teleprompters superpuestos peleando por los mismos atajos globales:
/// si ya hay uno abierto, la segunda ejecucion solo lo trae al frente.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\Teleprompter.Instancia";
    private const string EventName = @"Local\Teleprompter.Activar";

    private Mutex? _mutex;
    private EventWaitHandle? _activation;
    private RegisteredWaitHandle? _registration;

    public bool TryAcquire()
    {
        _mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            _mutex.Dispose();
            _mutex = null;
            return false;
        }

        _activation = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
        return true;
    }

    public void Listen(Action onActivated)
    {
        if (_activation is null)
        {
            return;
        }

        _registration = ThreadPool.RegisterWaitForSingleObject(
            _activation, (_, _) => onActivated(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    public static void SignalExisting()
    {
        if (EventWaitHandle.TryOpenExisting(EventName, out var handle))
        {
            handle.Set();
            handle.Dispose();
        }
    }

    public void Dispose()
    {
        _registration?.Unregister(null);
        _activation?.Dispose();
        if (_mutex is not null)
        {
            _mutex.ReleaseMutex();
            _mutex.Dispose();
        }
    }
}
