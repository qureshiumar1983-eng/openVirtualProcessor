using System;
using System.Threading;
using OpenVP.SimplSharp;

namespace Crestron.SimplSharp
{
    public delegate void CTimerCallbackFunction(object userSpecific);

    /// <summary>One-shot or repeating timer. Callbacks run on the slot thread.</summary>
    public class CTimer : IDisposable
    {
        private readonly CTimerCallbackFunction _callback;
        private readonly object _userObject;
        private Timer _timer;
        private long _repeat;
        private readonly object _gate = new object();

        public CTimer(CTimerCallbackFunction callbackFunction, long dueTime)
            : this(callbackFunction, null, dueTime, Timeout.Infinite) { }

        public CTimer(CTimerCallbackFunction callbackFunction, object callbackObject, long dueTime)
            : this(callbackFunction, callbackObject, dueTime, Timeout.Infinite) { }

        public CTimer(CTimerCallbackFunction callbackFunction, object callbackObject, long dueTime, long repeatPeriod)
        {
            _callback = callbackFunction ?? throw new ArgumentNullException(nameof(callbackFunction));
            _userObject = callbackObject;
            _repeat = repeatPeriod;
            _timer = new Timer(Fire, null, Normalize(dueTime), Normalize(repeatPeriod));
            OpenVPBridge.Track(this);
        }

        public bool Disposed { get; private set; }

        public void Reset() => Reset(_repeat < 0 ? 0 : _repeat, _repeat);

        public void Reset(long dueTime) => Reset(dueTime, Timeout.Infinite);

        public void Reset(long dueTime, long repeatPeriod)
        {
            lock (_gate)
            {
                if (Disposed) return;
                _repeat = repeatPeriod;
                _timer.Change(Normalize(dueTime), Normalize(repeatPeriod));
            }
        }

        public void Stop()
        {
            lock (_gate)
            {
                if (!Disposed) _timer.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (Disposed) return;
                Disposed = true;
                _timer.Dispose();
            }
            OpenVPBridge.Untrack(this);
        }

        private void Fire(object _)
        {
            if (Disposed) return;
            OpenVPBridge.Post(() =>
            {
                if (!Disposed) _callback(_userObject);
            });
        }

        private static long Normalize(long ms) => ms < 0 ? Timeout.Infinite : ms;
    }

    /// <summary>Thin wrapper over a lock, as in the SIMPL# API.</summary>
    public class CCriticalSection : IDisposable
    {
        private readonly object _lock = new object();
        public void Enter() => Monitor.Enter(_lock);
        public bool TryEnter() => Monitor.TryEnter(_lock);
        public void Leave() => Monitor.Exit(_lock);
        public bool Disposed { get; private set; }
        public void Dispose() { Disposed = true; }
    }

    public class CMutex : IDisposable
    {
        private readonly SemaphoreSlim _sem;
        public CMutex() : this(false) { }
        public CMutex(bool initiallyOwned) { _sem = new SemaphoreSlim(initiallyOwned ? 0 : 1, 1); }
        public bool WaitForMutex() { _sem.Wait(); return true; }
        public bool WaitForMutex(int timeout) => _sem.Wait(timeout);
        public void ReleaseMutex() { if (_sem.CurrentCount == 0) _sem.Release(); }
        public void Close() => Dispose();
        public void Dispose() => _sem.Dispose();
    }

    public class CEvent : IDisposable
    {
        private readonly EventWaitHandle _handle;
        public CEvent() : this(false, false) { }
        public CEvent(bool autoReset, bool initialState)
        {
            _handle = new EventWaitHandle(initialState, autoReset ? EventResetMode.AutoReset : EventResetMode.ManualReset);
        }
        public bool Set() => _handle.Set();
        public bool Reset() => _handle.Reset();
        public bool Wait() => _handle.WaitOne();
        public bool Wait(int timeout) => _handle.WaitOne(timeout);
        public void Close() => Dispose();
        public void Dispose() => _handle.Dispose();
    }
}

namespace Crestron.SimplSharpPro.CrestronThread
{
    public delegate object ThreadCallbackFunction(object userSpecific);

    public class Thread : IDisposable
    {
        private readonly System.Threading.Thread _thread;
        private readonly ThreadCallbackFunction _callback;
        private readonly object _obj;

        /// <summary>Set in the default program template. Kept for compatibility; OpenVP does not cap threads.</summary>
        public static int MaxNumberOfUserThreads { get; set; } = 50;

        public enum eThreadStartOptions { Running = 0, CreateSuspended = 1 }
        public enum eThreadPriority { LowestPriority = 0, LowPriority = 1, MediumPriority = 2, HighPriority = 3, UberPriority = 4 }
        public enum eThreadStates { ThreadCreated, ThreadRunning, ThreadSuspended, ThreadFinished, ThreadAborting }

        public Thread(ThreadCallbackFunction callbackFunction, object userObject)
            : this(callbackFunction, userObject, eThreadStartOptions.Running) { }

        public Thread(ThreadCallbackFunction callbackFunction, object userObject, eThreadStartOptions options)
        {
            _callback = callbackFunction;
            _obj = userObject;
            var slot = OpenVPBridge.Slot;
            _thread = new System.Threading.Thread(() =>
            {
                ThreadState = eThreadStates.ThreadRunning;
                try { _callback(_obj); }
                catch (System.Threading.ThreadInterruptedException) { }
                catch (Exception e) { slot?.Log("Error", "Exception in user thread " + Name + ": " + e); }
                finally { ThreadState = eThreadStates.ThreadFinished; }
            }) { IsBackground = true };
            ThreadState = eThreadStates.ThreadCreated;
            if (options == eThreadStartOptions.Running) Start();
        }

        public string Name { get => _thread.Name; set { if (_thread.Name == null) _thread.Name = value; } }
        public eThreadPriority Priority { get; set; } = eThreadPriority.MediumPriority;
        public eThreadStates ThreadState { get; private set; }

        public void Start()
        {
            if (_thread.ThreadState == System.Threading.ThreadState.Unstarted) _thread.Start();
        }

        public bool Join() { _thread.Join(); return true; }
        public bool Join(int ms) => _thread.Join(ms);

        /// <summary>.NET 8 cannot abort threads; this interrupts the thread if it is waiting.</summary>
        public void Abort() { ThreadState = eThreadStates.ThreadAborting; _thread.Interrupt(); }

        public static void Sleep(int ms) => System.Threading.Thread.Sleep(ms);

        public void Dispose() { }
    }
}
