using System.Collections.Concurrent;
using ImageTool.Core;

namespace ImageTool.Shared;

/// <summary>
/// Thread-safe event bus. Publish gọi handlers trên thread của caller; nếu cần marshal về UI thì handler tự dispatch.
/// </summary>
public class EventBus : IEventBus
{
    private readonly ConcurrentDictionary<Type, List<object>> _handlers = new();
    private readonly object _lock = new();

    public void Publish<TEvent>(TEvent @event) where TEvent : IEvent
    {
        var type = typeof(TEvent);
        if (!_handlers.TryGetValue(type, out var handlers)) return;

        // Snapshot để publish không bị deadlock nếu handler subscribe/unsubscribe trong lúc gọi
        object[] snapshot;
        lock (_lock) { snapshot = handlers.ToArray(); }

        foreach (var handler in snapshot)
        {
            try { ((Action<TEvent>)handler)(@event); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EventBus] Handler error for {type.Name}: {ex.Message}");
            }
        }
    }

    public void Subscribe<TEvent>(Action<TEvent> handler) where TEvent : IEvent
    {
        var type = typeof(TEvent);
        var list = _handlers.GetOrAdd(type, _ => new List<object>());
        lock (_lock) { list.Add(handler); }
    }

    public void Unsubscribe<TEvent>(Action<TEvent> handler) where TEvent : IEvent
    {
        var type = typeof(TEvent);
        if (!_handlers.TryGetValue(type, out var list)) return;
        lock (_lock) { list.Remove(handler); }
    }
}
