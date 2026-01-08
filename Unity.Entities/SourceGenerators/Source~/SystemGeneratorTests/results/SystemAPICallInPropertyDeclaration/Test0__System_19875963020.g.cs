#pragma warning disable 0219
#line 1 "Temp/GeneratedCode/TestProject/Test0__System_19875963020.g.cs"
using Unity.Entities;
using Unity.Entities.Tests;
[global::System.Runtime.CompilerServices.CompilerGenerated]
public partial class SystemAPICallInPropertyDeclaration
{
    [global::Unity.Entities.DOTSCompilerPatchedProperty("SystemAPICallInPropertyDeclaration.MyEntity_Property")]
    Entity __MyEntity_Property_71216975 =>  __query_1641826530_0.GetSingletonEntity();

    
    TypeHandle __TypeHandle;
     Unity.Entities.EntityQuery __query_1641826530_0;
    struct TypeHandle
    {
        [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public void __AssignHandles(ref global::Unity.Entities.SystemState state)
        {
        }
        
    }
    [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    void __AssignQueries(ref global::Unity.Entities.SystemState state)
    {
        var entityQueryBuilder = new global::Unity.Entities.EntityQueryBuilder(global::Unity.Collections.Allocator.Temp);
        __query_1641826530_0 = entityQueryBuilder.WithAll<global::Unity.Entities.Tests.EcsTestData>()
.Build(ref state);
        entityQueryBuilder.Reset();
        entityQueryBuilder.Dispose();
    }
    
    protected override void OnCreateForCompiler()
    {
        base.OnCreateForCompiler();
        __AssignQueries(ref this.CheckedStateRef);
        __TypeHandle.__AssignHandles(ref this.CheckedStateRef);
    }
}
