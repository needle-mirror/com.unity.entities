#if ROSLYN_SOURCEGEN_ENABLED
namespace Unity.Entities
{
    /// <summary>
    /// Any type which implements this interface and also contains an `OnUpdate()` method (with any number of parameters)
    /// will trigger source generation of a corresponding IJobEntityBatch type. The generated IJobEntityBatch type in turn
    /// invokes the OnUpdate() method on the IJobEntity type with the appropriate arguments.
    /// </summary>
    public interface IJobEntity
    {
    }
}
#endif
