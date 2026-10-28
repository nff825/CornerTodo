namespace CornerTodo
{
    /// <summary>The state writer needed by a session; loading remains an application-startup concern.</summary>
    public interface ITodoStore
    {
        void Save(TodoState state);
    }
}
