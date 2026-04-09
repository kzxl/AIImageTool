namespace ImageTool.Core;

public interface IEventBus
{
    void Publish<TEvent>(TEvent @event) where TEvent : IEvent;
    void Subscribe<TEvent>(Action<TEvent> handler) where TEvent : IEvent;
    void Unsubscribe<TEvent>(Action<TEvent> handler) where TEvent : IEvent;
}
