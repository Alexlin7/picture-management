using Microsoft.EntityFrameworkCore;
using Pm.Data;
using Pm.Data.Entities;
using Xunit;

namespace Pm.Data.Tests;

public class ModelTests
{
    private static PmDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<PmDbContext>()
            .UseSqlite("Data Source=:memory:")   // 只為建出 IModel,不會真的連
            .Options;
        return new PmDbContext(options);
    }

    [Fact]
    public void Model_maps_all_nine_entities()
    {
        using var ctx = BuildContext();
        var model = ctx.Model;

        Assert.NotNull(model.FindEntityType(typeof(LibraryRoot)));
        Assert.NotNull(model.FindEntityType(typeof(Photo)));
        Assert.NotNull(model.FindEntityType(typeof(PhotoLocation)));
        Assert.NotNull(model.FindEntityType(typeof(Tag)));
        Assert.NotNull(model.FindEntityType(typeof(TagRelation)));
        Assert.NotNull(model.FindEntityType(typeof(PhotoTag)));
        Assert.NotNull(model.FindEntityType(typeof(PathTagRule)));
        Assert.NotNull(model.FindEntityType(typeof(SavedSearch)));
        Assert.NotNull(model.FindEntityType(typeof(TaggingJob)));
    }

    [Fact]
    public void Photo_uses_snake_case_and_split_gps()
    {
        using var ctx = BuildContext();
        var photo = ctx.Model.FindEntityType(typeof(Photo))!;

        Assert.Equal("photo", photo.GetTableName());
        Assert.Equal("file_hash", photo.FindProperty(nameof(Photo.FileHash))!.GetColumnName());
        Assert.Equal("camera_model", photo.FindProperty(nameof(Photo.CameraModel))!.GetColumnName());
        Assert.Equal("gps_lat", photo.FindProperty(nameof(Photo.GpsLat))!.GetColumnName());
        Assert.Equal("gps_lon", photo.FindProperty(nameof(Photo.GpsLon))!.GetColumnName());
    }

    [Fact]
    public void PhotoTag_has_composite_primary_key()
    {
        using var ctx = BuildContext();
        var pt = ctx.Model.FindEntityType(typeof(PhotoTag))!;
        var pk = pt.FindPrimaryKey()!;

        Assert.Equal(2, pk.Properties.Count);
        Assert.Contains(pk.Properties, p => p.Name == nameof(PhotoTag.PhotoId));
        Assert.Contains(pk.Properties, p => p.Name == nameof(PhotoTag.TagId));
    }
}
