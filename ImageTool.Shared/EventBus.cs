using ImageTool.Core;

namespace ImageTool.Shared;

public class EventBus : IEventBus
{
    private readonly Dictionary<Type, List<object>> _handlers = new();

    public void Publish<TEvent>(TEvent @event) where TEvent : IEvent
    {
        var type = typeof(TEvent);
        if (_handlers.TryGetValue(type, out var handlers))
        {
            foreach (var handler in handlers.ToList())
            {
                ((Action<TEvent>)handler)(@event);
            }
        }
    }

    public void Subscribe<TEvent>(Action<TEvent> handler) where TEvent : IEvent
    {
        var type = typeof(TEvent);
        if (!_handlers.ContainsKey(type))
        {
            _handlers[type] = new List<object>();
        }
        _handlers[type].Add(handler);
    }

    public void Unsubscribe<TEvent>(Action<TEvent> handler) where TEvent : IEvent
    {
        var type = typeof(TEvent);
        if (_handlers.TryGetValue(type, out var handlers))
        {
            handlers.Remove(handler);
        }
    }
}
