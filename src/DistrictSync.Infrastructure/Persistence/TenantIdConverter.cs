using DistrictSync.Domain.Common;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DistrictSync.Infrastructure.Persistence;

public sealed class TenantIdConverter() : ValueConverter<TenantId, Guid>(id => id.Value, value => new TenantId(value));
