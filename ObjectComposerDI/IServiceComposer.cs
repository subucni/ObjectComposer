namespace ObjectComposer.Services.Composer;

public interface IServiceComposer<T>
{
    T Implementation { get; }
}
