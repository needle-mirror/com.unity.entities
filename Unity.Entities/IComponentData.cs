namespace Unity.Entities
{
    public interface IComponentData
    {
    }

    public interface ISharedComponentData
    {
    }

    public interface ISystemStateComponentData : IComponentData
    {
    }

    public interface ISystemStateSharedComponentData : ISharedComponentData
    {
    }
}
