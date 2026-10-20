using Microsoft.EntityFrameworkCore.Diagnostics;
using SchoolManagement.Domain.Common;

namespace SchoolManagement.Infrastructure.Persistence;

/// <summary>Marks every entity materialised from the database as persisted (see <see cref="Entity.IsTransient"/>).</summary>
public sealed class PersistedEntityMarker : IMaterializationInterceptor
{
    public static readonly PersistedEntityMarker Instance = new();

    public object InitializedInstance(MaterializationInterceptionData materializationData, object entity)
    {
        if (entity is Entity e) e.MarkPersisted();
        return entity;
    }
}
